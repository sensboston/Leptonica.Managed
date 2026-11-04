using System;
using System.IO;
using System.Text;

namespace Leptonica;

/*
 *  jbclass.c: unsupervised classification of connected components.
 *
 *     These are functions for unsupervised classification of
 *     collections of connected components -- either characters or
 *     words -- in binary images.  They can be used as image
 *     processing steps in jbig2 compression.
 *
 *     Initialization
 *
 *         JbClasser.RankHausInit        [rank hausdorff encoder]
 *         JbClasser.CorrelationInit     [correlation encoder]
 *         JbClasser.CorrelationInitWithoutComponents  [ditto]
 *         static JbClasser.CorrelationInitInternal
 *
 *     Classify the pages
 *
 *         JbClasser.AddPages
 *         JbClasser.AddPage
 *         JbClasser.AddPageComponents
 *
 *     Rank hausdorff classifier
 *
 *         JbClasser.ClassifyRankHaus
 *         Pix.Haustest
 *         Pix.RankHaustest
 *
 *     Binary correlation classifier
 *
 *         JbClasser.ClassifyCorrelation
 *
 *     Determine the image components we start with
 *
 *         JbClasser.GetComponents
 *         Pix.WordMaskByDilation
 *         Pix.WordBoxesByDilation
 *
 *     Build grayscale composites (templates)
 *
 *         JbClasser.AccumulateComposites
 *         JbClasser.TemplatesFromComposites
 *
 *     Utility functions for Classer
 *
 *         JbClasser.Create (jbClasserCreate)
 *
 *     Utility functions for Data
 *
 *         JbClasser.DataSave (jbDataSave)
 *         JbData.Write (jbDataWrite)
 *         JbData.Read (jbDataRead)
 *         JbData.Render (jbDataRender)
 *         JbClasser.GetULCorners (jbGetULCorners)
 *         JbClasser.GetLLCorners (jbGetLLCorners)
 *
 *     Static helpers
 *
 *         findSimilarSizedTemplatesInit, findSimilarSizedTemplatesNext
 *         finalPositioningForAlignment
 *
 *  Not ported: jbClasserDestroy, jbDataDestroy, findSimilarSizedTemplatesDestroy
 *  (memory management only).
 *
 *     Note: this is NOT an implementation of the JPEG jbig2
 *     proposed standard encoder, the specifications for which
 *     can be found at http://www.jpeg.org/jbigpt2.html.
 *     (See below for a full implementation.)
 *     It is an implementation of the lower-level part of an encoder that:
 *
 *        (1) identifies connected components that are going to be used
 *        (2) puts them in similarity classes (this is an unsupervised
 *            classifier), and
 *        (3) stores the result in a simple file format (2 files,
 *            one for templates and one for page/coordinate/template-index
 *            quartets).
 *
 *     An actual implementation of the official jbig2 encoder could
 *     start with parts (1) and (2), and would then compress the quartets
 *     according to the standards requirements (e.g., Huffman or
 *     arithmetic coding of coordinate differences and image templates).
 *
 *     The low-level part of the encoder provided here has the
 *     following useful features:
 *
 *         ~ It is accurate in the identification of templates
 *           and classes because it uses a windowed hausdorff
 *           distance metric.
 *         ~ It is accurate in the placement of the connected
 *           components, doing a two step process of first aligning
 *           the the centroids of the template with those of each instance,
 *           and then making a further correction of up to +- 1 pixel
 *           in each direction to best align the templates.
 *         ~ It is fast because it uses a morphologically based
 *           matching algorithm to implement the hausdorff criterion,
 *           and it selects the patterns that are possible matches
 *           based on their size.
 *
 *     We provide two different matching functions, one using Hausdorff
 *     distance and one using a simple image correlation.
 *     The Hausdorff method sometimes produces better results for the
 *     same number of classes, because it gives a relatively small
 *     effective weight to foreground pixels near the boundary,
 *     and a relatively  large weight to foreground pixels that are
 *     not near the boundary.  By effectively ignoring these boundary
 *     pixels, Hausdorff weighting corresponds better to the expected
 *     probabilities of the pixel values in a scanned image, where the
 *     variations in instances of the same printed character are much
 *     more likely to be in pixels near the boundary.  By contrast,
 *     the correlation method gives equal weight to all foreground pixels.
 *
 *     For best results, use the correlation method.  Correlation takes
 *     the number of fg pixels in the AND of instance and template,
 *     divided by the product of the number of fg pixels in instance
 *     and template.  It compares this with a threshold that, in
 *     general, depends on the fractional coverage of the template.
 *     For heavy text, the threshold is raised above that for light
 *     text,  By using both these parameters (basic threshold and
 *     adjustment factor for text weight), one has more flexibility
 *     and can arrive at the fewest substitution errors, although
 *     this comes at the price of more templates.
 *
 *     The strict Hausdorff scoring is not a rank weighting, because a
 *     single pixel beyond the given distance will cause a match
 *     failure.  A rank Hausdorff is more robust to non-boundary noise,
 *     but it is also more susceptible to confusing components that
 *     should be in different classes.  For implementing a jbig2
 *     application for visually lossless binary image compression,
 *     you have two choices:
 *
 *        (1) use a 3x3 structuring element (size = 3) and a strict
 *            Hausdorff comparison (rank = 1.0 in the rank Hausdorff
 *            function).  This will result in a minimal number of classes,
 *            but confusion of small characters, such as italic and
 *            non-italic lower-case 'o', can still occur.
 *        (2) use the correlation method with a threshold of 0.85
 *            and a weighting factor of about 0.7.  This will result in
 *            a larger number of classes, but should not be confused
 *            either by similar small characters or by extremely
 *            thick sans serif characters, such as in prog/cootoots.png.
 *
 *     As mentioned above, if visual substitution errors must be
 *     avoided, you should use the correlation method.
 *
 *     We provide executables that show how to do the encoding:
 *         prog/jbrankhaus.c
 *         prog/jbcorrelation.c
 *
 *     The basic flow for correlation classification goes as follows,
 *     where specific choices have been made for parameters (Hausdorff
 *     is the same except for initialization):
 *
 *             // Initialize and save data in the classer
 *         JbClasser classer = JbClasser.CorrelationInit(L.JB_CONN_COMPS, 0, 0, 0.8f, 0.7f);
 *         Sarray safiles = Sarray.GetSortedPathnamesInDirectory(directory, null, 0, 0);
 *         classer.AddPages(safiles);
 *
 *             // Save the data in a data structure for serialization,
 *             // and write it into two files.
 *         JbData data = classer.DataSave();
 *         data.Write(rootname);
 *
 *             // Reconstruct (render) the pages from the encoded data.
 *         Pixa pixa = data.Render(false);
 *
 *     Differences from C:
 *       - Output to stderr (lept_stderr) is dropped.
 *       - The plots of the debug output of pixWordMaskByDilation() are only
 *         added to %pixadb if the plot image was made (gnuplot is not run by
 *         the port).
 */

/// <summary>Constants from jbclass.h.</summary>
public static partial class L
{
    /* JB Classifier */
    /// <summary>Classifier that matches components with the rank Hausdorff method.</summary>
    public const int JB_RANKHAUS = 0;
    /// <summary>Classifier that matches components by correlation.</summary>
    public const int JB_CORRELATION = 1;

    /* For jbGetComponents(): type of component to extract from images */
    /// <summary>Extract connected components.</summary>
    public const int JB_CONN_COMPS = 0;
    /// <summary>Extract characters, found as connected components after a small dilation.</summary>
    public const int JB_CHARACTERS = 1;
    /// <summary>Extract words, found as connected components after a larger dilation.</summary>
    public const int JB_WORDS = 2;

    /* These parameters are used for naming the two files
     * in which the jbig2-like compressed data is stored.  */
    /// <summary>File name extension of the image file that holds the templates.</summary>
    public const string JB_TEMPLATE_EXT = ".templates.png";
    /// <summary>File name extension of the file that holds the compressed classification data.</summary>
    public const string JB_DATA_EXT = ".data";
}

/// <summary>
/// The JbClasser holds all the data accumulated during the
/// classification process that can be used for a compressed
/// jbig2-type representation of a set of images.  This is created
/// in an initialization process and added to as the selected components
/// on each successive page are analyzed (struct JbClasser).
/// </summary>
public sealed class JbClasser
{
    /* For jbClassifyRankHaus(): size of border added around
     * pix of each c.c., to allow further processing.  This
     * should be at least the sum of the MAX_DIFF_HEIGHT
     * (or MAX_DIFF_WIDTH) and one-half the size of the Sel  */
    private const int JB_ADDED_PIXELS = 6;

    /* For pixHaustest(), pixRankHaustest() and pixCorrelationScore():
     * choose these to be 2 or greater */
    private const int MAX_DIFF_WIDTH = 2;  /* use at least 2 */
    private const int MAX_DIFF_HEIGHT = 2;  /* use at least 2 */

    /* In initialization, you have the option to discard components
     * (cc, characters or words) that have either width or height larger
     * than a given size.  This is convenient for jbDataSave(), because
     * the components are placed onto a regular lattice with cell
     * dimension equal to the maximum component size.  The default
     * values are given here.  If you want to save all components,
     * use a sufficiently large set of dimensions. */
    private const int MAX_CONN_COMP_WIDTH = 350;  /* default max cc width */
    private const int MAX_CHAR_COMP_WIDTH = 350;  /* default max char width */
    private const int MAX_WORD_COMP_WIDTH = 1000;  /* default max word width */
    private const int MAX_COMP_HEIGHT = 120;  /* default max component height */

    /// <summary>input page image file names</summary>
    public Sarray? Safiles { get; set; }
    /// <summary>JB_RANKHAUS, JB_CORRELATION</summary>
    public int Method { get; set; }
    /// <summary>JB_CONN_COMPS, JB_CHARACTERS or JB_WORDS</summary>
    public int Components { get; set; }
    /// <summary>max component width allowed</summary>
    public int MaxWidth { get; set; }
    /// <summary>max component height allowed</summary>
    public int MaxHeight { get; set; }
    /// <summary>number of pages already processed</summary>
    public int NPages { get; set; }
    /// <summary>number components already processed on fully processed pages</summary>
    public int BaseIndex { get; set; }
    /// <summary>number of components on each page</summary>
    public Numa NaComps { get; set; }
    /// <summary>size of square struct elem for haus</summary>
    public int SizeHaus { get; set; }
    /// <summary>rank val of haus match, each way</summary>
    public float RankHaus { get; set; }
    /// <summary>thresh value for correlation score</summary>
    public float Thresh { get; set; }
    /// <summary>corrects thresh value for heaver components; use 0 for no correction</summary>
    public float WeightFactor { get; set; }
    /// <summary>w * h of each template, without extra border pixels</summary>
    public Numa NaArea { get; set; }
    /// <summary>max width of original src images</summary>
    public int W { get; set; }
    /// <summary>max height of original src images</summary>
    public int H { get; set; }
    /// <summary>current number of classes</summary>
    public int NClass { get; set; }
    /// <summary>If zero, pixaa isn't filled</summary>
    public int KeepPixaa { get; set; }
    /// <summary>instances for each class; unbordered</summary>
    public Pixaa PixaA { get; set; }
    /// <summary>templates for each class; bordered and not dilated</summary>
    public Pixa PixaT { get; set; }
    /// <summary>templates for each class; bordered and dilated</summary>
    public Pixa PixaTd { get; set; }
    /// <summary>Hash table to find templates by size</summary>
    public L_DnaHash? DaHash { get; set; }
    /// <summary>fg areas of undilated templates; only used for rank &lt; 1.0</summary>
    public Numa NaFgt { get; set; }
    /// <summary>centroids of all bordered cc</summary>
    public Pta PtaC { get; set; }
    /// <summary>centroids of all bordered template cc</summary>
    public Pta PtaCt { get; set; }
    /// <summary>array of class ids for each component</summary>
    public Numa NaClass { get; set; }
    /// <summary>array of page nums for each component</summary>
    public Numa NaPage { get; set; }
    /// <summary>array of UL corners at which the template is to be placed for each component</summary>
    public Pta PtaUl { get; set; }
    /// <summary>similar to ptaul, but for LL corners</summary>
    public Pta? PtaLl { get; set; }

    /*----------------------------------------------------------------------*
     *                            Initialization                            *
     *----------------------------------------------------------------------*/
    // leptonica: jbRankHausInit (jbclass.c)
    /// <param name="components">JB_CONN_COMPS, JB_CHARACTERS, JB_WORDS</param>
    /// <param name="maxwidth">of component; use 0 for default</param>
    /// <param name="maxheight">of component; use 0 for default</param>
    /// <param name="size">of square structuring element; 2, representing
    /// 2x2 sel, is necessary for reasonable accuracy of
    /// small components; combine this with rank ~ 0.97
    /// to avoid undue class expansion</param>
    /// <param name="rank">rank val of match, each way; in [0.5 - 1.0];
    /// when using size = 2, 0.97 is a reasonable value</param>
    /// <returns>jbclasser</returns>
    public static JbClasser RankHausInit(int components, int maxwidth, int maxheight, int size,
                                         float rank)
    {
        const string func = "jbRankHausInit";

        if (components != L.JB_CONN_COMPS && components != L.JB_CHARACTERS &&
            components != L.JB_WORDS)
            throw Lept.Arg(func, "invalid components");
        if (size < 1 || size > 10)
            throw Lept.Arg(func, "size not reasonable");
        if (rank < 0.5 || rank > 1.0)
            throw Lept.Arg(func, "rank not in [0.5-1.0]");
        if (maxwidth == 0) {
            if (components == L.JB_CONN_COMPS)
                maxwidth = MAX_CONN_COMP_WIDTH;
            else if (components == L.JB_CHARACTERS)
                maxwidth = MAX_CHAR_COMP_WIDTH;
            else  /* JB_WORDS */
                maxwidth = MAX_WORD_COMP_WIDTH;
        }
        if (maxheight == 0)
            maxheight = MAX_COMP_HEIGHT;

        JbClasser classer = Create(L.JB_RANKHAUS, components);
        classer.MaxWidth = maxwidth;
        classer.MaxHeight = maxheight;
        classer.SizeHaus = size;
        classer.RankHaus = rank;
        classer.DaHash = L_DnaHash.Create(5507, 4);  /* 5507 is prime */
        classer.KeepPixaa = 1;  /* keep all components in pixaa */
        return classer;
    }

    // leptonica: jbCorrelationInit (jbclass.c)
    /// <param name="components">JB_CONN_COMPS, JB_CHARACTERS, JB_WORDS</param>
    /// <param name="maxwidth">of component; use 0 for default</param>
    /// <param name="maxheight">of component; use 0 for default</param>
    /// <param name="thresh">value for correlation score: in [0.4 - 0.98]</param>
    /// <param name="weightfactor">corrects thresh for thick characters [0.0 - 1.0]</param>
    /// <returns>jbclasser</returns>
    /// <remarks>
    /// (1) For scanned text, suggested input values are:
    ///       thresh ~ [0.8 - 0.85]
    ///       weightfactor ~ [0.5 - 0.6]
    /// (2) For electronically generated fonts (e.g., rasterized pdf),
    ///     a very high thresh (e.g., 0.95) will not cause a significant
    ///     increase in the number of classes.
    /// </remarks>
    public static JbClasser CorrelationInit(int components, int maxwidth, int maxheight,
                                            float thresh, float weightfactor)
    {
        return CorrelationInitInternal(components, maxwidth, maxheight, thresh,
                                       weightfactor, 1);
    }

    // leptonica: jbCorrelationInitWithoutComponents (jbclass.c)
    /// <param name="components">JB_CONN_COMPS, JB_CHARACTERS, JB_WORDS</param>
    /// <param name="maxwidth">of component; use 0 for default</param>
    /// <param name="maxheight">of component; use 0 for default</param>
    /// <param name="thresh">value for correlation score: in [0.4 - 0.98]</param>
    /// <param name="weightfactor">corrects thresh for thick characters [0.0 - 1.0]</param>
    /// <returns>jbclasser</returns>
    /// <remarks>
    /// Acts the same as jbCorrelationInit(), but the resulting
    /// object doesn't keep a list of all the components.
    /// </remarks>
    public static JbClasser CorrelationInitWithoutComponents(int components, int maxwidth,
                                                             int maxheight, float thresh,
                                                             float weightfactor)
    {
        return CorrelationInitInternal(components, maxwidth, maxheight, thresh,
                                       weightfactor, 0);
    }

    // leptonica: jbCorrelationInitInternal (jbclass.c)
    private static JbClasser CorrelationInitInternal(int components, int maxwidth, int maxheight,
                                                     float thresh, float weightfactor,
                                                     int keep_components)
    {
        const string func = "jbCorrelationInitInternal";

        if (components != L.JB_CONN_COMPS && components != L.JB_CHARACTERS &&
            components != L.JB_WORDS)
            throw Lept.Arg(func, "invalid components");
        if (thresh < 0.4 || thresh > 0.98)
            throw Lept.Arg(func, "thresh not in range [0.4 - 0.98]");
        if (weightfactor < 0.0 || weightfactor > 1.0)
            throw Lept.Arg(func, "weightfactor not in range [0.0 - 1.0]");
        if (maxwidth == 0) {
            if (components == L.JB_CONN_COMPS)
                maxwidth = MAX_CONN_COMP_WIDTH;
            else if (components == L.JB_CHARACTERS)
                maxwidth = MAX_CHAR_COMP_WIDTH;
            else  /* JB_WORDS */
                maxwidth = MAX_WORD_COMP_WIDTH;
        }
        if (maxheight == 0)
            maxheight = MAX_COMP_HEIGHT;

        JbClasser classer = Create(L.JB_CORRELATION, components);
        classer.MaxWidth = maxwidth;
        classer.MaxHeight = maxheight;
        classer.Thresh = thresh;
        classer.WeightFactor = weightfactor;
        classer.DaHash = L_DnaHash.Create(5507, 4);  /* 5507 is prime */
        classer.KeepPixaa = keep_components;
        return classer;
    }

    /*----------------------------------------------------------------------*
     *                       Classify the pages                             *
     *----------------------------------------------------------------------*/
    // leptonica: jbAddPages (jbclass.c)
    /// <param name="safiles">of page image file names</param>
    /// <remarks>
    /// (1) jbclasser makes a copy of the array of file names.
    /// (2) The caller is still responsible for destroying the input array.
    /// </remarks>
    public void AddPages(Sarray safiles)
    {
        JbClasser classer = this;

        classer.Safiles = safiles.Copy();
        int nfiles = safiles.Count;
        for (int i = 0; i < nfiles; i++) {
            string fname = safiles.GetString(i, L.L_NOCOPY);
            Pix pix;
            try {
                pix = Pix.Read(fname);
            } catch (LeptonicaException) {
                Log.Warning("jbAddPages", $"image file {i} not read");
                continue;
            }
            if (pix.Depth != 1) {
                Log.Warning("jbAddPages", $"image file {i} not 1 bpp");
                continue;
            }
            classer.AddPage(pix);
        }
    }

    // leptonica: jbAddPage (jbclass.c)
    /// <param name="pixs">input page</param>
    public void AddPage(Pix pixs)
    {
        JbClasser classer = this;

        if (pixs.Depth != 1)
            throw Lept.Arg("jbAddPage", "pixs not defined or not 1 bpp");

        classer.W = pixs.Width;
        classer.H = pixs.Height;

            /* Get the appropriate components and their bounding boxes */
        GetComponents(pixs, classer.Components, classer.MaxWidth, classer.MaxHeight,
                      out Boxa boxas, out Pixa pixas);

        classer.AddPageComponents(pixs, boxas, pixas);
    }

    // leptonica: jbAddPageComponents (jbclass.c)
    /// <param name="pixs">input page</param>
    /// <param name="boxas">b.b. of components for this page</param>
    /// <param name="pixas">components for this page</param>
    /// <remarks>
    /// (1) If there are no components on the page, we don't require input
    ///     of empty boxas or pixas, although that's the typical situation.
    /// </remarks>
    public void AddPageComponents(Pix pixs, Boxa? boxas, Pixa? pixas)
    {
        JbClasser classer = this;

            /* Test for no components on the current page.  Always update the
             * number of pages processed, even if nothing is on it. */
        if (boxas == null || pixas == null || boxas.Count == 0) {
            classer.NPages++;
            return;
        }

            /* Get classes.  For hausdorff, it uses a specified size of
             * structuring element and specified rank.  For correlation,
             * it uses a specified threshold. */
        if (classer.Method == L.JB_RANKHAUS)
            classer.ClassifyRankHaus(boxas, pixas);
        else  /* classer->method == JB_CORRELATION */
            classer.ClassifyCorrelation(boxas, pixas);

            /* Find the global UL corners, adjusted for each instance so
             * that the class template and instance will have their
             * centroids in the same place.  Then the template can be
             * used to replace the instance. */
        classer.GetULCorners(pixs, boxas);

            /* Update total component counts and number of pages processed. */
        int n = boxas.Count;
        classer.BaseIndex += n;
        classer.NaComps.AddNumber(n);
        classer.NPages++;
    }

    /*----------------------------------------------------------------------*
     *         Classification using windowed rank hausdorff metric          *
     *----------------------------------------------------------------------*/
    // leptonica: jbClassifyRankHaus (jbclass.c)
    /// <param name="boxa">new components for classification</param>
    /// <param name="pixas">new components for classification</param>
    public void ClassifyRankHaus(Boxa boxa, Pixa pixas)
    {
        JbClasser classer = this;
        int nt, iclass, wt, ht, area1, area3;
        bool found;
        Pix pix, pix1, pix2, pix3, pix4;
        Pixa pixa;
        float x1, y1, x2, y2;

        int n = pixas.Count;
        if (n == 0)
            throw Lept.Arg("jbClassifyRankHaus", "pixas is empty");
        Numa nafg = pixas.CountPixels();  /* areas for this page */

        int npages = classer.NPages;
        int size = classer.SizeHaus;
        Sel sel = Sel.CreateBrick(size, size, size / 2, size / 2, L.SEL_HIT);

            /* Generate the bordered pixa, with and without dilation.
             * pixa1 and pixa2 contain all the input components. */
        Pixa pixa1 = Pixa.Create(n);
        Pixa pixa2 = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            pix = pixas.GetPix(i, L.L_CLONE);
            pix1 = pix.AddBorderGeneral(JB_ADDED_PIXELS, JB_ADDED_PIXELS,
                                        JB_ADDED_PIXELS, JB_ADDED_PIXELS, 0);
            pix2 = Pix.Dilate(null, pix1, sel);
            pixa1.AddPix(pix1, L.L_INSERT);   /* un-dilated */
            pixa2.AddPix(pix2, L.L_INSERT);   /* dilated */
        }

            /* Get the centroids of all the bordered images.
             * These are relative to the UL corner of each (bordered) pix.  */
        Pta pta = pixa1.Centroids();  /* centroids for this page; use here */
        Pta ptac = classer.PtaC;  /* holds centroids of components up to this page */
        ptac.Join(pta, 0, -1);  /* save centroids of all components */
        Pta ptact = classer.PtaCt;  /* holds centroids of templates */

            /* Use these to save the class and page of each component. */
        Numa naclass = classer.NaClass;
        Numa napage = classer.NaPage;

            /* Store the unbordered pix in a pixaa, in a hierarchical
             * set of arrays.  There is one pixa for each class,
             * and the pix in each pixa are all the instances found
             * of that class.  This is actually more than one would need
             * for a jbig2 encoder, but there are two reasons to keep
             * them around: (1) the set of instances for each class
             * can be used to make an improved binary (or, better,
             * a grayscale) template, rather than simply using the first
             * one in the set; (2) we can investigate the failures
             * of the classifier.  This pixaa grows as we process
             * successive pages. */
        Pixaa pixaa = classer.PixaA;

            /* arrays to store class exemplars (templates) */
        Pixa pixat = classer.PixaT;   /* un-dilated */
        Pixa pixatd = classer.PixaTd;   /* dilated */

            /* Fill up the pixaa tree with the template exemplars as
             * the first pix in each pixa.  As we add each pix,
             * we also add the associated box to the pixa.
             * We also keep track of the centroid of each pix,
             * and use the difference between centroids (of the
             * pix with the exemplar we are checking it with)
             * to align the two when checking that the Hausdorff
             * distance does not exceed a threshold.
             * The threshold is set by the Sel used for dilating.
             * For example, a 3x3 brick, sel_3, corresponds to a
             * Hausdorff distance of 1.  In general, for an NxN brick,
             * with N odd, corresponds to a Hausdorff distance of (N - 1)/2.
             * It turns out that we actually need to use a sel of size 2x2
             * to avoid small bad components when there is a halftone image
             * from which components can be chosen.
             * The larger the Sel you use, the fewer the number of classes,
             * and the greater the likelihood of putting semantically
             * different objects in the same class.  For simplicity,
             * we do this separately for the case of rank == 1.0 (exact
             * match within the Hausdorff distance) and rank < 1.0.  */
        float rank = classer.RankHaus;
        L_DnaHash dahash = classer.DaHash!;
        if (rank == 1.0) {
            for (int i = 0; i < n; i++) {
                pix1 = pixa1.GetPix(i, L.L_CLONE);
                pix2 = pixa2.GetPix(i, L.L_CLONE);
                pta.GetPt(i, out x1, out y1);
                nt = pixat.Count;  /* number of templates */
                found = false;
                FindCtx findcontext = FindSimilarSizedTemplatesInit(classer, pix1);
                while ((iclass = FindSimilarSizedTemplatesNext(findcontext)) > -1) {
                        /* Find score for this template */
                    pix3 = pixat.GetPix(iclass, L.L_CLONE);
                    pix4 = pixatd.GetPix(iclass, L.L_CLONE);
                    ptact.GetPt(iclass, out x2, out y2);
                    bool testval = pix1.Haustest(pix2, pix3, pix4, x1 - x2, y1 - y2,
                                                 MAX_DIFF_WIDTH, MAX_DIFF_HEIGHT);
                    if (testval) {
                        found = true;
                        naclass.AddNumber(iclass);
                        napage.AddNumber(npages);
                        if (classer.KeepPixaa != 0) {
                            pixa = pixaa.GetPixa(iclass, L.L_CLONE);
                            pix = pixas.GetPix(i, L.L_CLONE);
                            pixa.AddPix(pix, L.L_INSERT);
                            Box box = boxa.GetBox(i, L.L_CLONE);
                            pixa.AddBox(box, L.L_INSERT);
                        }
                        break;
                    }
                }
                if (!found) {  /* new class */
                    naclass.AddNumber(nt);
                    napage.AddNumber(npages);
                    pixa = Pixa.Create(0);
                    pix = pixas.GetPix(i, L.L_CLONE);  /* unbordered instance */
                    pixa.AddPix(pix, L.L_INSERT);
                    wt = pix.Width;
                    ht = pix.Height;
                    dahash.Add((ulong)ht * (ulong)wt, nt);
                    Box box = boxa.GetBox(i, L.L_CLONE);
                    pixa.AddBox(box, L.L_INSERT);
                    pixaa.AddPixa(pixa, L.L_INSERT);  /* unbordered instance */
                    ptact.AddPt(x1, y1);
                    pixat.AddPix(pix1, L.L_INSERT);  /* bordered template */
                    pixatd.AddPix(pix2, L.L_INSERT);  /* bordered dil template */
                }
            }
        } else {  /* rank < 1.0 */
            Numa nafgt = classer.NaFgt;
            int[] tab8 = Pix.MakePixelSumTab8();
            for (int i = 0; i < n; i++) {   /* all instances on this page */
                pix1 = pixa1.GetPix(i, L.L_CLONE);
                area1 = nafg.GetIValue(i);
                pix2 = pixa2.GetPix(i, L.L_CLONE);
                pta.GetPt(i, out x1, out y1);   /* use pta for this page */
                nt = pixat.Count;  /* number of templates */
                found = false;
                FindCtx findcontext = FindSimilarSizedTemplatesInit(classer, pix1);
                while ((iclass = FindSimilarSizedTemplatesNext(findcontext)) > -1) {
                        /* Find score for this template */
                    pix3 = pixat.GetPix(iclass, L.L_CLONE);
                    area3 = nafgt.GetIValue(iclass);
                    pix4 = pixatd.GetPix(iclass, L.L_CLONE);
                    ptact.GetPt(iclass, out x2, out y2);
                    bool testval = pix1.RankHaustest(pix2, pix3, pix4, x1 - x2, y1 - y2,
                                                     MAX_DIFF_WIDTH, MAX_DIFF_HEIGHT,
                                                     area1, area3, rank, tab8);
                    if (testval) {  /* greedy match; take the first */
                        found = true;
                        naclass.AddNumber(iclass);
                        napage.AddNumber(npages);
                        if (classer.KeepPixaa != 0) {
                            pixa = pixaa.GetPixa(iclass, L.L_CLONE);
                            pix = pixas.GetPix(i, L.L_CLONE);
                            pixa.AddPix(pix, L.L_INSERT);
                            Box box = boxa.GetBox(i, L.L_CLONE);
                            pixa.AddBox(box, L.L_INSERT);
                        }
                        break;
                    }
                }
                if (!found) {  /* new class */
                    naclass.AddNumber(nt);
                    napage.AddNumber(npages);
                    pixa = Pixa.Create(0);
                    pix = pixas.GetPix(i, L.L_CLONE);  /* unbordered instance */
                    pixa.AddPix(pix, L.L_INSERT);
                    wt = pix.Width;
                    ht = pix.Height;
                    dahash.Add((ulong)ht * (ulong)wt, nt);
                    Box box = boxa.GetBox(i, L.L_CLONE);
                    pixa.AddBox(box, L.L_INSERT);
                    pixaa.AddPixa(pixa, L.L_INSERT);  /* unbordered instance */
                    ptact.AddPt(x1, y1);
                    pixat.AddPix(pix1, L.L_INSERT);  /* bordered template */
                    pixatd.AddPix(pix2, L.L_INSERT);  /* ditto */
                    nafgt.AddNumber(area1);
                }
            }
        }
        classer.NClass = pixat.Count;
    }

    /*----------------------------------------------------------------------*
     *            Classification using windowed correlation score           *
     *----------------------------------------------------------------------*/
    // leptonica: jbClassifyCorrelation (jbclass.c)
    /// <param name="boxa">new components for classification</param>
    /// <param name="pixas">new components for classification</param>
    public void ClassifyCorrelation(Boxa boxa, Pixa pixas)
    {
        JbClasser classer = this;
        int nt, iclass, wt, ht, area, area1, area2;
        bool found, overthreshold;
        float x1, y1, x2, y2, xsum, ysum, threshold;
        Pix pix, pix1, pix2;
        Pixa pixa;

        int npages = classer.NPages;

            /* Generate the bordered pixa, which contains all the the
             * input components.  This will not be saved.   */
        int n = pixas.Count;
        if (n == 0) {
            Log.Warning("jbClassifyCorrelation", "pixas is empty");
            return;
        }
        Pixa pixa1 = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            pix = pixas.GetPix(i, L.L_CLONE);
            pix1 = pix.AddBorderGeneral(JB_ADDED_PIXELS, JB_ADDED_PIXELS,
                                        JB_ADDED_PIXELS, JB_ADDED_PIXELS, 0);
            pixa1.AddPix(pix1, L.L_INSERT);
        }

            /* Use these to save the class and page of each component. */
        Numa naclass = classer.NaClass;
        Numa napage = classer.NaPage;

            /* Get the number of fg pixels in each component.  */
        Numa nafgt = classer.NaFgt;    /* holds fg areas of the templates */
        int[] sumtab = Pix.MakePixelSumTab8();

        int[] pixcts = new int[n];  /* pixel counts of each pixa */
        int[][] pixrowcts = new int[n][];  /* row-by-row pixel counts of each pixa */
        int[] centtab = Pix.MakePixelCentroidTab8();

            /* Count the "1" pixels in each row of the pix in pixa1; this
             * allows pixCorrelationScoreThresholded to abort early if a match
             * is impossible.  This loop merges three calculations: the total
             * number of "1" pixels, the number of "1" pixels in each row, and
             * the centroid.  The centroids are relative to the UL corner of
             * each (bordered) pix.  The pixrowcts[i][y] are the total number
             * of fg pixels in pixa[i] below row y. */
        Pta pta = Pta.Create(n);
        for (int i = 0; i < n; i++) {
            pix = pixa1.GetPix(i, L.L_CLONE);
            pixrowcts[i] = new int[pix.Height];
            xsum = 0;
            ysum = 0;
            int wpl = pix.Wpl;
            uint[] data = pix.Data;
            int row = (pix.Height - 1) * wpl;
            int downcount = 0;
            for (int y = pix.Height - 1; y >= 0; y--, row -= wpl) {
                pixrowcts[i][y] = downcount;
                int rowcount = 0;
                for (int x = 0; x < wpl; x++) {
                    uint word = data[row + x];
                    int b = (int)(word & 0xff);
                    rowcount += sumtab[b];
                    xsum += centtab[b] + (x * 32 + 24) * sumtab[b];
                    b = (int)((word >> 8) & 0xff);
                    rowcount += sumtab[b];
                    xsum += centtab[b] + (x * 32 + 16) * sumtab[b];
                    b = (int)((word >> 16) & 0xff);
                    rowcount += sumtab[b];
                    xsum += centtab[b] + (x * 32 + 8) * sumtab[b];
                    b = (int)((word >> 24) & 0xff);
                    rowcount += sumtab[b];
                    xsum += centtab[b] + x * 32 * sumtab[b];
                }
                downcount += rowcount;
                ysum += rowcount * y;
            }
            pixcts[i] = downcount;
            if (downcount > 0) {
                pta.AddPt(xsum / (float)downcount, ysum / (float)downcount);
            } else {  /* no pixels; shouldn't happen */
                Log.Error("jbClassifyCorrelation", "downcount == 0 !");
                pta.AddPt(pix.Width / 2, pix.Height / 2);
            }
        }

        Pta ptac = classer.PtaC;  /* holds centroids of components up to this page */
        ptac.Join(pta, 0, -1);  /* save centroids of all components */
        Pta ptact = classer.PtaCt;  /* holds centroids of templates */

        /* Store the unbordered pix in a pixaa, in a hierarchical
         * set of arrays.  There is one pixa for each class,
         * and the pix in each pixa are all the instances found
         * of that class.  This is actually more than one would need
         * for a jbig2 encoder, but there are two reasons to keep
         * them around: (1) the set of instances for each class
         * can be used to make an improved binary (or, better,
         * a grayscale) template, rather than simply using the first
         * one in the set; (2) we can investigate the failures
         * of the classifier.  This pixaa grows as we process
         * successive pages. */
        Pixaa pixaa = classer.PixaA;

            /* Array to store class exemplars */
        Pixa pixat = classer.PixaT;

            /* Fill up the pixaa tree with the template exemplars as
             * the first pix in each pixa.  As we add each pix,
             * we also add the associated box to the pixa.
             * We also keep track of the centroid of each pix,
             * and use the difference between centroids (of the
             * pix with the exemplar we are checking it with)
             * to align the two when checking that the correlation
             * score exceeds a threshold.  The correlation score
             * is given by the square of the area of the AND
             * between aligned instance and template, divided by
             * the product of areas of each image.  For identical
             * template and instance, the score is 1.0.
             * If the threshold is too small, non-equivalent instances
             * will be placed in the same class; if too large, there will
             * be an unnecessary division of classes representing the
             * same character.  The weightfactor adds in some of the
             * difference (1.0 - thresh), depending on the heaviness
             * of the template (measured as the fraction of fg pixels). */
        float thresh = classer.Thresh;
        float weight = classer.WeightFactor;
        Numa naarea = classer.NaArea;
        L_DnaHash dahash = classer.DaHash!;
        for (int i = 0; i < n; i++) {
            pix1 = pixa1.GetPix(i, L.L_CLONE);
            area1 = pixcts[i];
            pta.GetPt(i, out x1, out y1);  /* centroid for this instance */
            nt = pixat.Count;
            found = false;
            FindCtx findcontext = FindSimilarSizedTemplatesInit(classer, pix1);
            while ((iclass = FindSimilarSizedTemplatesNext(findcontext)) > -1) {
                    /* Get the template */
                pix2 = pixat.GetPix(iclass, L.L_CLONE);
                area2 = nafgt.GetIValue(iclass);
                ptact.GetPt(iclass, out x2, out y2);  /* template centroid */

                    /* Find threshold for this template */
                if (weight > 0.0) {
                    area = naarea.GetIValue(iclass);
                    threshold = (float)(thresh + (1.0 - thresh) * weight * area2 / area);
                } else {
                    threshold = thresh;
                }

                    /* Find score for this template */
                overthreshold = pix1.CorrelationScoreThresholded(pix2,
                                             area1, area2, x1 - x2, y1 - y2,
                                             MAX_DIFF_WIDTH, MAX_DIFF_HEIGHT,
                                             sumtab, pixrowcts[i], threshold);

                if (overthreshold) {  /* greedy match */
                    found = true;
                    naclass.AddNumber(iclass);
                    napage.AddNumber(npages);
                    if (classer.KeepPixaa != 0) {
                            /* We are keeping a record of all components */
                        pixa = pixaa.GetPixa(iclass, L.L_CLONE);
                        pix = pixas.GetPix(i, L.L_CLONE);
                        pixa.AddPix(pix, L.L_INSERT);
                        Box box = boxa.GetBox(i, L.L_CLONE);
                        pixa.AddBox(box, L.L_INSERT);
                    }
                    break;
                }
            }
            if (!found) {  /* new class */
                naclass.AddNumber(nt);
                napage.AddNumber(npages);
                pixa = Pixa.Create(0);
                pix = pixas.GetPix(i, L.L_CLONE);  /* unbordered instance */
                pixa.AddPix(pix, L.L_INSERT);
                wt = pix.Width;
                ht = pix.Height;
                dahash.Add((ulong)ht * (ulong)wt, nt);
                Box box = boxa.GetBox(i, L.L_CLONE);
                pixa.AddBox(box, L.L_INSERT);
                pixaa.AddPixa(pixa, L.L_INSERT);  /* unbordered instance */
                ptact.AddPt(x1, y1);
                nafgt.AddNumber(area1);
                pixat.AddPix(pix1, L.L_INSERT);   /* bordered template */
                area = (pix1.Width - 2 * JB_ADDED_PIXELS) *
                       (pix1.Height - 2 * JB_ADDED_PIXELS);
                naarea.AddNumber(area);
            }
        }
        classer.NClass = pixat.Count;
    }

    /*----------------------------------------------------------------------*
     *             Determine the image components we start with             *
     *----------------------------------------------------------------------*/
    // leptonica: jbGetComponents (jbclass.c)
    /// <param name="pixs">1 bpp</param>
    /// <param name="components">JB_CONN_COMPS, JB_CHARACTERS, JB_WORDS</param>
    /// <param name="maxwidth">of saved components; larger are discarded</param>
    /// <param name="maxheight">of saved components; larger are discarded</param>
    /// <param name="boxad">b.b. of component items</param>
    /// <param name="pixad">component items</param>
    public static void GetComponents(Pix pixs, int components, int maxwidth, int maxheight,
                                     out Boxa boxad, out Pixa pixad)
    {
        const string func = "jbGetComponents";
        int redfactor;
        Boxa boxa;
        Pix pix1, pix2, pix3;
        Pixa pixa, pixat;

        boxad = null!;
        pixad = null!;
        if (components != L.JB_CONN_COMPS && components != L.JB_CHARACTERS &&
            components != L.JB_WORDS)
            throw Lept.Arg(func, "invalid components");

        if (pixs.Zero()) {
            boxad = Boxa.Create(0);
            pixad = Pixa.Create(0);
            return;
        }

            /* If required, preprocess input pixs.  The method for both
             * characters and words is to generate a connected component
             * mask over the units that we want to aggregrate, which are,
             * in general, sets of related connected components in pixs.
             * For characters, we want to include the dots with
             * 'i', 'j' and '!', so we do a small vertical closing to
             * generate the mask.  For words, we make a mask over all
             * characters in each word.  This is a bit more tricky, because
             * the spacing between words is difficult to predict a priori,
             * and words can be typeset with variable spacing that can
             * in some cases be barely larger than the space between
             * characters.  The first step is to generate the mask and
             * identify each of its connected components.  */
        if (components == L.JB_CONN_COMPS) {  /* no preprocessing */
            boxa = pixs.ConnComp(out pixa, 8);
        } else if (components == L.JB_CHARACTERS) {
            pix1 = pixs.MorphSequence("c1.6", 0);
            boxa = pix1.ConnComp(out pixat, 8);
            pixa = pixat.ClipToPix(pixs);
        } else {  /* components == JB_WORDS */

                /* Do the operations at about 150 ppi resolution.
                 * It is much faster at 75 ppi, but the results are
                 * more accurate at 150 ppi.  This will segment the
                 * words in body text.  It can be expected that relatively
                 * infrequent words in a larger font will be split. */
            int res = pixs.XRes;
            if (res <= 200) {
                redfactor = 1;
                pix1 = Pix.Clone(pixs);
            } else if (res <= 400) {
                redfactor = 2;
                pix1 = pixs.ReduceRankBinaryCascade(1, 0, 0, 0);
            } else {
                redfactor = 4;
                pix1 = pixs.ReduceRankBinaryCascade(1, 1, 0, 0);
            }

                /* Estimate the word mask, at approximately 150 ppi.
                 * This has both very large and very small components left in. */
            pix1.WordMaskByDilation(out pix2, out _, null);

                /* Expand the optimally dilated word mask to full res. */
            pix3 = pix2.ExpandReplicate(redfactor);

                /* Pull out the pixels in pixs corresponding to the mask
                 * components in pix3.  Note that above we used threshold
                 * levels in the reduction of 1 to insure that the resulting
                 * mask fully covers the input pixs.  The downside of using
                 * a threshold of 1 is that very close characters from adjacent
                 * lines can be joined.  But with a level of 2 or greater,
                 * it is necessary to use a seedfill, followed by a pixOr():
                 *       pixt4 = pixSeedfillBinary(NULL, pix3, pixs, 8);
                 *       pixOr(pix3, pix3, pixt4);
                 * to insure that the mask coverage is complete over pixs.  */
            boxa = pix3.ConnComp(out pixat, 4);
            pixa = pixat.ClipToPix(pixs);
        }

            /* Remove large components, and save the results.  */
        pixad = pixa.SelectBySize(maxwidth, maxheight, L.L_SELECT_IF_BOTH,
                                  L.L_SELECT_IF_LTE, out _);
        boxad = boxa.SelectBySize(maxwidth, maxheight, L.L_SELECT_IF_BOTH,
                                  L.L_SELECT_IF_LTE, out _);
    }

    /*----------------------------------------------------------------------*
     *                 Build grayscale composites (templates)               *
     *----------------------------------------------------------------------*/
    // leptonica: jbAccumulateComposites (jbclass.c)
    /// <param name="pixaa">one pixa for each class</param>
    /// <param name="na">number of samples used to build each composite</param>
    /// <param name="ptat">centroids of bordered composites</param>
    /// <returns>pixad accumulated sum of samples in each class</returns>
    public static Pixa AccumulateComposites(Pixaa pixaa, out Numa na, out Pta ptat)
    {
        int n = pixaa.GetCount(out _);
        ptat = Pta.Create(n);
        Pixa pixad = Pixa.Create(n);
        na = Numa.Create(n);

        for (int i = 0; i < n; i++) {
            Pixa pixa = pixaa.GetPixa(i, L.L_CLONE);
            int nt = pixa.Count;
            na.AddNumber(nt);
            if (nt == 0) {
                Log.Warning("jbAccumulateComposites", "empty pixa found!");
                continue;
            }
            pixa.SizeRange(out _, out _, out int maxw, out int maxh);
            Pix pix = pixa.GetPix(0, L.L_CLONE);
            int d = pix.Depth;
            Pix pixt1 = Pix.Create(maxw, maxh, d);
            Pix pixsum = Pix.InitAccumulate(maxw, maxh, 0);
            Pta pta = pixa.Centroids();

                /* Find the average value of the centroids ... */
            float xave = 0, yave = 0;
            for (int j = 0; j < nt; j++) {
                pta.GetPt(j, out float x, out float y);
                xave += x;
                yave += y;
            }
            xave = xave / (float)nt;
            yave = yave / (float)nt;

                /* and place all centroids at their average value */
            for (int j = 0; j < nt; j++) {
                Pix pixt2 = pixa.GetPix(j, L.L_CLONE);
                pta.GetPt(j, out float x, out float y);
                int xdiff = (int)(x - xave);
                int ydiff = (int)(y - yave);
                pixt1.ClearAll();
                pixt1.Rasterop(xdiff, ydiff, maxw, maxh, L.PIX_SRC, pixt2, 0, 0);
                pixsum.Accumulate(pixt1, L.L_ARITH_ADD);
            }
            pixad.AddPix(pixsum, L.L_INSERT);
            ptat.AddPt(xave, yave);
        }

        return pixad;
    }

    // leptonica: jbTemplatesFromComposites (jbclass.c)
    /// <param name="pixac">one pix of composites for each class</param>
    /// <param name="na">number of samples used for each class composite</param>
    /// <returns>pixad 8 bpp templates for each class</returns>
    public static Pixa TemplatesFromComposites(Pixa pixac, Numa na)
    {
        int n = pixac.Count;
        Pixa pixad = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pixsum = pixac.GetPix(i, L.L_COPY);  /* changed internally */
            float nt = na.GetFValue(i);  /* number of samples in the composite; always an integer */
            float factor = (float)(255.0 / nt);
            pixsum.MultConstAccumulate(factor, 0);  /* changes pixsum */
            Pix pixd = pixsum.FinalAccumulate(0, 8);
            pixad.AddPix(pixd, L.L_INSERT);
        }

        return pixad;
    }

    /*----------------------------------------------------------------------*
     *                       jbig2 utility routines                         *
     *----------------------------------------------------------------------*/
    // leptonica: jbClasserCreate (jbclass.c)
    /// <param name="method">JB_RANKHAUS, JB_CORRELATION</param>
    /// <param name="components">JB_CONN_COMPS, JB_CHARACTERS, JB_WORDS</param>
    public static JbClasser Create(int method, int components)
    {
        if (method != L.JB_RANKHAUS && method != L.JB_CORRELATION)
            throw Lept.Arg("jbClasserCreate", "invalid method");
        if (components != L.JB_CONN_COMPS && components != L.JB_CHARACTERS &&
            components != L.JB_WORDS)
            throw Lept.Arg("jbClasserCreate", "invalid component");

        return new JbClasser(method, components);
    }

    private JbClasser(int method, int components)
    {
        Method = method;
        Components = components;
        NaComps = Numa.Create(0);
        PixaA = Pixaa.Create(0);
        PixaT = Pixa.Create(0);
        PixaTd = Pixa.Create(0);
        NaFgt = Numa.Create(0);
        NaArea = Numa.Create(0);
        PtaC = Pta.Create(0);
        PtaCt = Pta.Create(0);
        NaClass = Numa.Create(0);
        NaPage = Numa.Create(0);
        PtaUl = Pta.Create(0);
    }

    // leptonica: jbDataSave (jbclass.c)
    /// <returns>jbdata</returns>
    /// <remarks>
    /// (1) This routine stores the jbig2-type data required for
    ///     generating a lossy jbig2 version of the image.
    ///     It can be losslessly written to (and read from) two files.
    /// (2) It generates and stores the mosaic of templates.
    /// (3) It clones the Numa and Pta arrays, so these must all
    ///     be destroyed by the caller.
    /// (4) Input 0 to use the default values for latticew and/or latticeh,
    /// </remarks>
    public JbData DataSave()
    {
        JbClasser classer = this;

            /* Write the templates into an array. */
        classer.PixaT.SizeRange(out _, out _, out int maxw, out int maxh);
        Pix pix = classer.PixaT.DisplayOnLattice(maxw + 1, maxh + 1, out _, out _)
                  ?? throw Lept.Fail("jbDataSave", "data not made");

        JbData data = new JbData();
        data.Pix = pix;
        data.NPages = classer.NPages;
        data.W = classer.W;
        data.H = classer.H;
        data.NClass = classer.NClass;
        data.LatticeW = maxw + 1;
        data.LatticeH = maxh + 1;
        data.NaClass = classer.NaClass.Clone();
        data.NaPage = classer.NaPage.Clone();
        data.PtaUl = classer.PtaUl.Clone();
        return data;
    }

    // leptonica: jbGetULCorners (jbclass.c)
    /// <param name="pixs">full res image</param>
    /// <param name="boxa">of c.c. bounding rectangles for this page</param>
    /// <remarks>
    /// (1) This computes the ptaul field, which has the global UL corners,
    ///     adjusted for each specific component, so that each component
    ///     can be replaced by the template for its class and have the
    ///     centroid in the template in the same position as the
    ///     centroid of the original connected component.  It is important
    ///     that this be done properly to avoid a wavy baseline in the
    ///     result.
    /// (2) The array fields ptac and ptact give the centroids of
    ///     those components relative to the UL corner of each component.
    ///     Here, we compute the difference in each component, round to
    ///     nearest integer, and correct the box->x and box->y by
    ///     the appropriate integral difference.
    /// (3) The templates and stored instances are all bordered.
    /// </remarks>
    public void GetULCorners(Pix pixs, Boxa boxa)
    {
        JbClasser classer = this;
        int idelx, idely;

        int n = boxa.Count;
        Pta ptaul = classer.PtaUl;
        Numa naclass = classer.NaClass;
        Pta ptac = classer.PtaC;
        Pta ptact = classer.PtaCt;
        int baseindex = classer.BaseIndex;  /* num components before this page */
        int[] sumtab = Pix.MakePixelSumTab8();
        for (int i = 0; i < n; i++) {
            int index = baseindex + i;
            ptac.GetPt(index, out float x1, out float y1);
            int iclass = naclass.GetIValue(index);
            ptact.GetPt(iclass, out float x2, out float y2);
            float delx = x2 - x1;
            float dely = y2 - y1;
            if (delx >= 0)
                idelx = (int)(delx + 0.5);
            else
                idelx = (int)(delx - 0.5);
            if (dely >= 0)
                idely = (int)(dely + 0.5);
            else
                idely = (int)(dely - 0.5);
            Box box = boxa.GetBox(i, L.L_CLONE);
            box.GetGeometry(out int x, out int y, out _, out _);

                /* Get final increments dx and dy for best alignment */
            Pix pixt = classer.PixaT.GetPix(iclass, L.L_CLONE);
            FinalPositioningForAlignment(pixs, x, y, idelx, idely,
                                         pixt, sumtab, out int dx, out int dy);
            ptaul.AddPt(x - idelx + dx, y - idely + dy);
        }
    }

    // leptonica: jbGetLLCorners (jbclass.c)
    /// <remarks>
    /// (1) This computes the ptall field, which has the global LL corners,
    ///     adjusted for each specific component, so that each component
    ///     can be replaced by the template for its class and have the
    ///     centroid in the template in the same position as the
    ///     centroid of the original connected component. It is important
    ///     that this be done properly to avoid a wavy baseline in the result.
    /// (2) It is computed here from the corresponding UL corners, where
    ///     the input templates and stored instances are all bordered.
    ///     This should be done after all pages have been processed.
    /// (3) For proper substitution, the templates whose LL corners are
    ///     placed in these locations must be UN-bordered.
    ///     This is available for a realistic jbig2 encoder, which would
    ///     (1) encode each template without a border, and (2) encode
    ///     the position using the LL corner (rather than the UL
    ///     corner) because the difference between y-values
    ///     of successive instances is typically close to zero.
    /// </remarks>
    public void GetLLCorners()
    {
        JbClasser classer = this;

        Pta ptaul = classer.PtaUl;
        Numa naclass = classer.NaClass;
        Pixa pixat = classer.PixaT;

        int n = ptaul.Count;
        Pta ptall = Pta.Create(n);
        classer.PtaLl = ptall;

            /* If the templates were bordered, we would add h - 1 to the UL
             * corner y-value.  However, because the templates to be used
             * here have their borders removed, and the borders are
             * JB_ADDED_PIXELS on each side, we add h - 1 - 2 * JB_ADDED_PIXELS
             * to the UL corner y-value.  */
        for (int i = 0; i < n; i++) {
            ptaul.GetIPt(i, out int x1, out int y1);
            int iclass = naclass.GetIValue(i);
            Pix pix = pixat.GetPix(iclass, L.L_CLONE);
            int h = pix.Height;
            ptall.AddPt(x1, y1 + h - 1 - 2 * JB_ADDED_PIXELS);
        }
    }

    /*----------------------------------------------------------------------*
     *                              Static helpers                          *
     *----------------------------------------------------------------------*/
    /* When looking for similar matches we check templates whose size is +/- 2 in
     * each direction. This involves 25 possible sizes. This array contains the
     * offsets for each of those positions in a spiral pattern. There are 25 pairs
     * of numbers in this array: even positions are x values. */
    private static readonly int[] two_by_two_walk = {
      0, 0,
      0, 1,
      -1, 0,
      0, -1,
      1, 0,
      -1, 1,
      1, 1,
      -1, -1,
      1, -1,
      0, -2,
      2, 0,
      0, 2,
      -2, 0,
      -1, -2,
      1, -2,
      2, -1,
      2, 1,
      1, 2,
      -1, 2,
      -2, 1,
      -2, -1,
      -2, -2,
      2, -2,
      2, 2,
      -2, 2};

    /// <summary>This stores the state of a state machine which fetches similar sized templates (struct JbFindTemplatesState).</summary>
    private sealed class FindCtx
    {
        public JbClasser classer = null!;   /* classer                               */
        public int w;          /* desired width                         */
        public int h;          /* desired height                        */
        public int i;          /* index into two_by_two step array      */
        public L_Dna? dna;     /* current number array                  */
        public int n;          /* current element of dna                */
    }

    // leptonica: findSimilarSizedTemplatesInit (jbclass.c)
    /// <param name="classer"></param>
    /// <param name="pixs">instance to be matched</param>
    /// <returns>Allocated context to be used with findSimilar*</returns>
    private static FindCtx FindSimilarSizedTemplatesInit(JbClasser classer, Pix pixs)
    {
        FindCtx state = new FindCtx();
        state.w = pixs.Width - 2 * JB_ADDED_PIXELS;
        state.h = pixs.Height - 2 * JB_ADDED_PIXELS;
        state.classer = classer;
        return state;
    }

    // leptonica: findSimilarSizedTemplatesNext (jbclass.c)
    /// <param name="state">from findSimilarSizedTemplatesInit</param>
    /// <returns>next template number, or -1 when finished</returns>
    /// <remarks>
    /// We have a dna hash table that maps template area to a list of template
    /// numbers with that area.  We wish to find similar sized templates,
    /// so we first look for templates with the same width and height, and
    /// then with width + 1, etc.  This walk is guided by the
    /// two_by_two_walk array, above.
    ///
    /// We don't want to have to collect the whole list of templates first,
    /// because we hope to find a well-matching template quickly.  So we
    /// keep the context for this walk in an explictit state structure,
    /// and this function acts like a generator.
    /// </remarks>
    private static int FindSimilarSizedTemplatesNext(FindCtx state)
    {
        while (true) {  /* Continue the walk over step 'i' */
            if (state.i >= 25) {  /* all done; didn't find a good match */
                return -1;
            }

            int desiredw = state.w + two_by_two_walk[2 * state.i];
            int desiredh = state.h + two_by_two_walk[2 * state.i + 1];
            if (desiredh < 1 || desiredw < 1) {  /* invalid size */
                state.i++;
                continue;
            }

            if (state.dna == null) {
                    /* We have yet to start walking the array for the step 'i' */
                state.dna = state.classer.DaHash!.GetDna((ulong)desiredh * (ulong)desiredw,
                                                         L.L_CLONE);
                if (state.dna == null) {  /* nothing there */
                    state.i++;
                    continue;
                }

                state.n = 0;  /* OK, we got a dna. */
            }

                /* Continue working on this dna */
            int size = state.dna.Count;
            for ( ; state.n < size; ) {
                int templ = (int)(state.dna.GetDValue(state.n++) + 0.5);
                Pix pixt = state.classer.PixaT.GetPix(templ, L.L_CLONE);
                if (pixt.Width - 2 * JB_ADDED_PIXELS == desiredw &&
                    pixt.Height - 2 * JB_ADDED_PIXELS == desiredh) {
                    return templ;
                }
            }

                /* Exhausted the dna (no match found); take another step and
                 * try again. */
            state.i++;
            state.dna = null;
        }
    }

    // leptonica: finalPositioningForAlignment (jbclass.c)
    /// <param name="pixs">input page image</param>
    /// <param name="x">location of UL corner of bb of component in pixs</param>
    /// <param name="y">location of UL corner of bb of component in pixs</param>
    /// <param name="idelx">compensation to match centroids of component and template</param>
    /// <param name="idely">compensation to match centroids of component and template</param>
    /// <param name="pixt">template, with JB_ADDED_PIXELS of padding on all sides</param>
    /// <param name="sumtab">for summing fg pixels in an image</param>
    /// <param name="pdx">return delta on position for best match; each one is in the set {-1, 0, 1}</param>
    /// <param name="pdy">return delta on position for best match</param>
    private static void FinalPositioningForAlignment(Pix pixs, int x, int y, int idelx, int idely,
                                                     Pix pixt, int[] sumtab, out int pdx,
                                                     out int pdy)
    {
        int minx = 0, miny = 0;

        pdx = pdy = 0;

            /* Use JB_ADDED_PIXELS pixels padding on each side */
        pixt.GetDimensions(out int w, out int h, out _);
        Box box = Box.Create(x - idelx - JB_ADDED_PIXELS,
                             y - idely - JB_ADDED_PIXELS, w, h);
        Pix pixi = pixs.ClipRectangle(box, out _)  /* clipped from source pixs */
                   ?? throw Lept.Fail("finalPositioningForAlignment", "pixi not made");

        Pix pixr = Pix.Create(pixi.Width, pixi.Height, 1);  /* temporary storage */
        int mincount = 0x7fffffff;
        for (int i = -1; i <= 1; i++) {
            for (int j = -1; j <= 1; j++) {
                Pix.Copy(pixr, pixi);
                pixr.Rasterop(j, i, w, h, L.PIX_SRC ^ L.PIX_DST, pixt, 0, 0);
                int count = pixr.CountPixels(sumtab);
                if (count < mincount) {
                    minx = j;
                    miny = i;
                    mincount = count;
                }
            }
        }

        pdx = minx;
        pdy = miny;
    }
}

/// <summary>
/// The JbData holds all the data required for the compressed jbig-type
/// representation of a set of images.  The data can be written to file, read back,
/// and used to regenerate an approximate version of the original,
/// which differs in two ways from the original:
///   (1) It uses a template image for each c.c. instead of the
///       original instance, for each occurrence on each page.
///   (2) It discards components with either a height or width larger
///       than the maximuma, given here by the lattice dimensions
///       used for storing the templates.
/// (struct JbData)
/// </summary>
public sealed class JbData
{
    private const int L_BUF_SIZE = 512;

    /// <summary>template composite for all classes</summary>
    public Pix Pix { get; set; } = null!;
    /// <summary>number of pages</summary>
    public int NPages { get; set; }
    /// <summary>max width of original page images</summary>
    public int W { get; set; }
    /// <summary>max height of original page images</summary>
    public int H { get; set; }
    /// <summary>number of classes</summary>
    public int NClass { get; set; }
    /// <summary>lattice width for template composite</summary>
    public int LatticeW { get; set; }
    /// <summary>lattice height for template composite</summary>
    public int LatticeH { get; set; }
    /// <summary>array of class ids for each component</summary>
    public Numa NaClass { get; set; } = null!;
    /// <summary>array of page nums for each component</summary>
    public Numa NaPage { get; set; } = null!;
    /// <summary>array of UL corners at which the template is to be placed for each component</summary>
    public Pta PtaUl { get; set; } = null!;

    // leptonica: jbDataWrite (jbclass.c)
    /// <param name="rootout">for output files; everything but the extension</param>
    /// <remarks>
    /// (1) Serialization function that writes data in jbdata to file.
    /// </remarks>
    public void Write(string rootout)
    {
        JbData jbdata = this;

        int npages = jbdata.NPages;
        int w = jbdata.W;
        int h = jbdata.H;
        Pix pixt = jbdata.Pix;
        int nclass = jbdata.NClass;
        int cellw = jbdata.LatticeW;
        int cellh = jbdata.LatticeH;
        Numa naclass = jbdata.NaClass;
        Numa napage = jbdata.NaPage;
        Pta ptaul = jbdata.PtaUl;

        string buf = $"{rootout}{L.JB_TEMPLATE_EXT}";
        Leptonica.Pix.Write(buf, pixt, L.IFF_PNG);

        buf = $"{rootout}{L.JB_DATA_EXT}";
        using FileStream fp = Utils.FopenWriteStream(buf, "wb");
        int ncomp = ptaul.Count;
        CStdio.Fprintf(fp, $"jb data file\n");
        CStdio.Fprintf(fp, $"num pages = {npages}\n");
        CStdio.Fprintf(fp, $"page size: w = {w}, h = {h}\n");
        CStdio.Fprintf(fp, $"num components = {ncomp}\n");
        CStdio.Fprintf(fp, $"num classes = {nclass}\n");
        CStdio.Fprintf(fp, $"template lattice size: w = {cellw}, h = {cellh}\n");
        for (int i = 0; i < ncomp; i++) {
            int ipage = napage.GetIValue(i);
            int iclass = naclass.GetIValue(i);
            ptaul.GetIPt(i, out int x, out int y);
            CStdio.Fprintf(fp, $"{ipage} {iclass} {x} {y}\n");
        }
    }

    // leptonica: jbDataRead (jbclass.c)
    /// <param name="rootname">for template and data files</param>
    /// <returns>jbdata</returns>
    public static JbData Read(string rootname)
    {
        const string func = "jbDataRead";

        string fname = $"{rootname}{L.JB_TEMPLATE_EXT}";
        Pix pixs = Leptonica.Pix.Read(fname);

        fname = $"{rootname}{L.JB_DATA_EXT}";
        byte[] data = Utils.BinaryRead(fname);

        Sarray sa = Sarray.CreateLinesFromString(Encoding.Latin1.GetString(data), 0);
        int nsa = sa.Count;   /* number of cc + 6 */
        string linestr = sa.GetString(0, L.L_NOCOPY);
        if (linestr != "jb data file")
            throw Lept.Arg(func, "invalid jb data file");
        int npages = 0, w = 0, h = 0, ncomp = 0, nclass = 0, cellw = 0, cellh = 0;
        linestr = sa.GetString(1, L.L_NOCOPY);
        if (CFormat.Sscanf(linestr, "num pages = %d", out object?[] v) >= 1)
            npages = (int)v[0]!;
        linestr = sa.GetString(2, L.L_NOCOPY);
        if (CFormat.Sscanf(linestr, "page size: w = %d, h = %d", out v) >= 2) {
            w = (int)v[0]!;
            h = (int)v[1]!;
        }
        linestr = sa.GetString(3, L.L_NOCOPY);
        if (CFormat.Sscanf(linestr, "num components = %d", out v) >= 1)
            ncomp = (int)v[0]!;
        linestr = sa.GetString(4, L.L_NOCOPY);
        if (CFormat.Sscanf(linestr, "num classes = %d\n", out v) >= 1)
            nclass = (int)v[0]!;
        linestr = sa.GetString(5, L.L_NOCOPY);
        if (CFormat.Sscanf(linestr, "template lattice size: w = %d, h = %d\n", out v) >= 2) {
            cellw = (int)v[0]!;
            cellh = (int)v[1]!;
        }

        int ninit = ncomp;
        if (ncomp > 1000000) {  /* fuzz protection */
            Log.Warning(func, "ncomp > 1M");
            ninit = 1000000;
        }
        Numa naclass = Numa.Create(ninit);
        Numa napage = Numa.Create(ninit);
        Pta ptaul = Pta.Create(ninit);
        for (int i = 6; i < nsa; i++) {
            linestr = sa.GetString(i, L.L_NOCOPY);
            int ipage = 0, iclass = 0, x = 0, y = 0;
            if (CFormat.Sscanf(linestr, "%d %d %d %d\n", out v) >= 4) {
                ipage = (int)v[0]!;
                iclass = (int)v[1]!;
                x = (int)v[2]!;
                y = (int)v[3]!;
            }
            napage.AddNumber(ipage);
            naclass.AddNumber(iclass);
            ptaul.AddPt(x, y);
        }

        JbData jbdata = new JbData();
        jbdata.Pix = pixs;
        jbdata.NPages = npages;
        jbdata.W = w;
        jbdata.H = h;
        jbdata.NClass = nclass;
        jbdata.LatticeW = cellw;
        jbdata.LatticeH = cellh;
        jbdata.NaClass = naclass;
        jbdata.NaPage = napage;
        jbdata.PtaUl = ptaul;
        return jbdata;
    }

    // leptonica: jbDataRender (jbclass.c)
    /// <param name="debugflag">if true, writes into 2 bpp pix and adds component outlines in color</param>
    /// <returns>pixa reconstruction of original images, using templates</returns>
    public Pixa Render(bool debugflag)
    {
        JbData data = this;
        Pix pix1, pix2, pixd;
        Boxaa? baa = null;

        int npages = data.NPages;
        int w = data.W;
        int h = data.H;
        Pix pixt = data.Pix;
        int nclass = data.NClass;
        int cellw = data.LatticeW;
        int cellh = data.LatticeH;
        Numa naclass = data.NaClass;
        Numa napage = data.NaPage;
        Pta ptaul = data.PtaUl;
        int ncomp = naclass.Count;

            /* Reconstruct the original set of images from the templates
             * and the data associated with each component.  First,
             * generate the output pixa as a set of empty pix.  For debug,
             * where the bounding boxes of each component will be displayed
             * in red, use 2 bpp colormapped output pix. */
        Pixa pixad = Pixa.Create(npages);  /* pixa of output images */
        for (int i = 0; i < npages; i++) {
            if (!debugflag) {
                pix1 = Leptonica.Pix.Create(w, h, 1);
            } else {
                pix1 = Leptonica.Pix.Create(w, h, 2);
                PixColormap cmap = PixColormap.Create(2);
                cmap.AddColor(255, 255, 255);
                cmap.AddColor(0, 0, 0);
                pix1.SetColormap(cmap);
            }
            pixad.AddPix(pix1, L.L_INSERT);
        }

            /* Put the class templates into a pixa. */
        Pixa pixat = Pixa.CreateFromPix(pixt, nclass, cellw, cellh);  /* pixa of templates */

            /* Place each component in the right location on its page.
             * For debug, first generate the boxa of component bounding
             * boxes for each page, and save the results in a boxaa.
             * Nota bene. In general we cannot use rasterop on colormap
             * indices with operations like PIX_SRC | PIX_DST.  So we must
             * do the rasterop of image components first, and then paint
             * the component bounding boxes later.  We can use rasterop
             * on the 2 bpp pix here because the colormap has only two
             * index values, 0 and 1, * so doing a bit-or between pixels
             * only affects the lower-order bit and does not generate
             * spurious colormap indices. */
        if (debugflag) {
            baa = Boxaa.Create(npages);
            Boxa boxa = Boxa.Create(0);
            baa.InitFull(boxa);
        }
        for (int i = 0; i < ncomp; i++) {
            int ipage = napage.GetIValue(i);
            int iclass = naclass.GetIValue(i);
            pix1 = pixat.GetPix(iclass, L.L_CLONE);  /* the template */
            int wp = pix1.Width;
            int hp = pix1.Height;
            ptaul.GetIPt(i, out int x, out int y);
            pixd = pixad.GetPix(ipage, L.L_CLONE);   /* the output page */
            if (!debugflag) {
                pixd.Rasterop(x, y, wp, hp, L.PIX_SRC | L.PIX_DST, pix1, 0, 0);
            } else {
                pix2 = pix1.Convert1To2Cmap();
                pixd.Rasterop(x, y, wp, hp, L.PIX_SRC | L.PIX_DST, pix2, 0, 0);
                Boxa boxa = baa!.GetBoxa(ipage, L.L_CLONE);
                Box box = Box.Create(x, y, wp, hp);
                boxa.AddBox(box, L.L_INSERT);
            }
        }

            /* For debug, for each page image, render the box outlines in red.
             * This adds a red colormap entry to each page. */
        if (debugflag) {
            for (int i = 0; i < npages; i++) {
                pixd = pixad.GetPix(i, L.L_CLONE);
                Boxa boxa = baa!.GetBoxa(i, L.L_CLONE);
                pixd.RenderBoxaArb(boxa, 1, 255, 0, 0);
            }
        }

        return pixad;
    }
}

public sealed partial class Pix
{
    /*----------------------------------------------------------------------*
     *         Classification using windowed rank hausdorff metric          *
     *----------------------------------------------------------------------*/
    // leptonica: pixHaustest (jbclass.c)
    /// <param name="pix2">new pix, dilated</param>
    /// <param name="pix3">exemplar pix, not dilated</param>
    /// <param name="pix4">exemplar pix, dilated</param>
    /// <param name="delx">x comp of centroid difference</param>
    /// <param name="dely">y comp of centroid difference</param>
    /// <param name="maxdiffw">max width difference of pix1 and pix2</param>
    /// <param name="maxdiffh">max height difference of pix1 and pix2</param>
    /// <returns>false if no match, true if the new pix is in the same class as the exemplar;
    /// this pix (pix1) is the new pix, not dilated</returns>
    /// <remarks>
    /// We check first that the two pix are roughly
    /// the same size.  Only if they meet that criterion do
    /// we compare the bitmaps.  The Hausdorff is a 2-way
    /// check.  The centroid difference is used to align the two
    /// images to the nearest integer for each of the checks.
    /// These check that the dilated image of one contains
    /// ALL the pixels of the undilated image of the other.
    /// Checks are done in both direction.  A single pixel not
    /// contained in either direction results in failure of the test.
    /// </remarks>
    public bool Haustest(Pix pix2, Pix pix3, Pix pix4, float delx, float dely, int maxdiffw,
                         int maxdiffh)
    {
        Pix pix1 = this;
        int idelx, idely;

            /* Eliminate possible matches based on size difference */
        int wi = pix1.Width;
        int hi = pix1.Height;
        int wt = pix3.Width;
        int ht = pix3.Height;
        int delw = Math.Abs(wi - wt);
        if (delw > maxdiffw)
            return false;
        int delh = Math.Abs(hi - ht);
        if (delh > maxdiffh)
            return false;

            /* Round difference in centroid location to nearest integer;
             * use this as a shift when doing the matching. */
        if (delx >= 0)
            idelx = (int)(delx + 0.5);
        else
            idelx = (int)(delx - 0.5);
        if (dely >= 0)
            idely = (int)(dely + 0.5);
        else
            idely = (int)(dely - 0.5);

            /*  Do 1-direction hausdorff, checking that every pixel in pix1
             *  is within a dilation distance of some pixel in pix3.  Namely,
             *  that pix4 entirely covers pix1:
             *       pixt = pixSubtract(NULL, pix1, pix4), including shift
             *  where pixt has no ON pixels.  */
        Pix pixt = CreateTemplate(pix1);
        pixt.Rasterop(0, 0, wi, hi, L.PIX_SRC, pix1, 0, 0);
        pixt.Rasterop(idelx, idely, wi, hi, L.PIX_DST & L.PIX_NOT(L.PIX_SRC),
                      pix4, 0, 0);
        bool boolmatch = pixt.Zero();
        if (!boolmatch)
            return false;

            /*  Do 1-direction hausdorff, checking that every pixel in pix3
             *  is within a dilation distance of some pixel in pix1.  Namely,
             *  that pix2 entirely covers pix3:
             *      pixSubtract(pixt, pix3, pix2), including shift
             *  where pixt has no ON pixels. */
        pixt.Rasterop(idelx, idely, wt, ht, L.PIX_SRC, pix3, 0, 0);
        pixt.Rasterop(0, 0, wt, ht, L.PIX_DST & L.PIX_NOT(L.PIX_SRC), pix2, 0, 0);
        return pixt.Zero();
    }

    // leptonica: pixRankHaustest (jbclass.c)
    /// <param name="pix2">new pix, dilated</param>
    /// <param name="pix3">exemplar pix, not dilated</param>
    /// <param name="pix4">exemplar pix, dilated</param>
    /// <param name="delx">x comp of centroid difference</param>
    /// <param name="dely">y comp of centroid difference</param>
    /// <param name="maxdiffw">max width difference of pix1 and pix2</param>
    /// <param name="maxdiffh">max height difference of pix1 and pix2</param>
    /// <param name="area1">fg pixels in pix1</param>
    /// <param name="area3">fg pixels in pix3</param>
    /// <param name="rank">rank value of test, each way</param>
    /// <param name="tab8">table of pixel sums for byte</param>
    /// <returns>false if no match, true if the new pix is in the same class as the exemplar;
    /// this pix (pix1) is the new pix, not dilated</returns>
    /// <remarks>
    /// We check first that the two pix are roughly
    /// the same size.  Only if they meet that criterion do
    /// we compare the bitmaps.  We convert the rank value to
    /// a number of pixels by multiplying the rank fraction by the number
    /// of pixels in the undilated image.  The Hausdorff is a 2-way
    /// check.  The centroid difference is used to align the two
    /// images to the nearest integer for each of the checks.
    /// The rank hausdorff checks that the dilated image of one
    /// contains the rank fraction of the pixels of the undilated
    /// image of the other.   Checks are done in both direction.
    /// Failure of the test in either direction results in failure
    /// of the test.
    /// </remarks>
    public bool RankHaustest(Pix pix2, Pix pix3, Pix pix4, float delx, float dely, int maxdiffw,
                             int maxdiffh, int area1, int area3, float rank, int[] tab8)
    {
        Pix pix1 = this;
        int idelx, idely;

            /* Eliminate possible matches based on size difference */
        int wi = pix1.Width;
        int hi = pix1.Height;
        int wt = pix3.Width;
        int ht = pix3.Height;
        int delw = Math.Abs(wi - wt);
        if (delw > maxdiffw)
            return false;
        int delh = Math.Abs(hi - ht);
        if (delh > maxdiffh)
            return false;

            /* Upper bounds in remaining pixels for allowable match */
        int thresh1 = (int)(area1 * (1.0 - rank) + 0.5);
        int thresh3 = (int)(area3 * (1.0 - rank) + 0.5);

            /* Round difference in centroid location to nearest integer;
             * use this as a shift when doing the matching. */
        if (delx >= 0)
            idelx = (int)(delx + 0.5);
        else
            idelx = (int)(delx - 0.5);
        if (dely >= 0)
            idely = (int)(dely + 0.5);
        else
            idely = (int)(dely - 0.5);

            /*  Do 1-direction hausdorff, checking that every pixel in pix1
             *  is within a dilation distance of some pixel in pix3.  Namely,
             *  that pix4 entirely covers pix1:
             *       pixt = pixSubtract(NULL, pix1, pix4), including shift
             *  where pixt has no ON pixels.  */
        Pix pixt = CreateTemplate(pix1);
        pixt.Rasterop(0, 0, wi, hi, L.PIX_SRC, pix1, 0, 0);
        pixt.Rasterop(idelx, idely, wi, hi, L.PIX_DST & L.PIX_NOT(L.PIX_SRC),
                      pix4, 0, 0);
        bool boolmatch = pixt.ThresholdPixelSum(thresh1, tab8);
        if (boolmatch) /* above thresh1 */
            return false;

            /*  Do 1-direction hausdorff, checking that every pixel in pix3
             *  is within a dilation distance of some pixel in pix1.  Namely,
             *  that pix2 entirely covers pix3:
             *      pixSubtract(pixt, pix3, pix2), including shift
             *  where pixt has no ON pixels. */
        pixt.Rasterop(idelx, idely, wt, ht, L.PIX_SRC, pix3, 0, 0);
        pixt.Rasterop(0, 0, wt, ht, L.PIX_DST & L.PIX_NOT(L.PIX_SRC), pix2, 0, 0);
        boolmatch = pixt.ThresholdPixelSum(thresh3, tab8);
        if (boolmatch)  /* above thresh3 */
            return false;
        else
            return true;
    }

    /*----------------------------------------------------------------------*
     *             Determine the image components we start with             *
     *----------------------------------------------------------------------*/
    // leptonica: pixWordMaskByDilation (jbclass.c)
    /// <param name="pixm">dilated word mask</param>
    /// <param name="size">size of good horizontal dilation</param>
    /// <param name="pixadb">[optional] debug: pixa of intermediate steps</param>
    /// <remarks>
    /// (1) This gives an estimate of the word masks.  See
    ///     pixWordBoxesByDilation() for further filtering of the word boxes.
    /// (2) The resolution should be between 75 and 150 ppi, and the optimal
    ///     dilation will be between 3 and 10.
    /// (3) A good size for dilating to get word masks is optionally returned.
    /// (4) Typically, the number of c.c. reduced with each successive
    ///     dilation (stored in nadiff) decreases quickly to a minimum
    ///     (where the characters in a word are joined), and then
    ///     increases again as the smaller number of words are joined.
    ///     For the typical case, you can then look for this minimum
    ///     and dilate to get the word mask.  However, there are many
    ///     cases where the function is not so simple. For example, if the
    ///     pix has been upscaled 2x, the nadiff function oscillates, with
    ///     every other value being zero!  And for some images it tails
    ///     off without a clear minimum to indicate where to break.
    ///     So a more simple and robust method is to find the dilation
    ///     where the initial number of c.c. has been reduced by some
    ///     fraction (we use a 70% reduction).
    /// (5) pixs is 1 bpp; typ. at 75 to 150 ppi.
    /// </remarks>
    public void WordMaskByDilation(out Pix pixm, out int size, Pixa? pixadb)
    {
        Pix pixs = this;
        int diff, total = 0;
        int ndil;
        int[] ncc = new int[13];  /* max dilation + 1 */

        pixm = null!;
        size = 0;
        if (pixs.Depth != 1)
            throw Lept.Arg("pixWordMaskByDilation", "pixs undefined or not 1 bpp");

            /* Find a good dilation to create the word mask, by successively
             * increasing dilation size and counting the connected components. */
        Pix pix1 = Copy(null, pixs);
        ndil = 12;  /* appropriate for 75 to 150 ppi */
        Numa nacc = Numa.Create(ndil + 1);
        Numa nadiff = Numa.Create(ndil + 1);
        for (int i = 0; i <= ndil; i++) {
            Pix pix2;
            if (i == 0)  /* first one not dilated */
                pix2 = Copy(null, pix1);
            else  /* successive dilation by sel_2h */
                pix2 = pix1.MorphSequence("d2.1", 0);
            Boxa boxa = pix2.ConnCompBB(4);
            ncc[i] = boxa.Count;
            nacc.AddNumber(ncc[i]);
            if (i == 0) total = ncc[0];
            if (i > 0) {
                diff = ncc[i - 1] - ncc[i];
                nadiff.AddNumber(diff);
            }
            pix1 = pix2;
        }

            /* Find the dilation at which the c.c. count has reduced
             * to 30% of the initial value.  Although 30% seems high,
             * it seems better to use this but add one to ibest.  */
        int[] diffa = nadiff.GetIArray();
        int n = nadiff.Count;
        int maxdiff = 0;
        bool check = true;
        int ibest = 2;
        for (int i = 1; i < n; i++) {
            int count = nacc.GetIValue(i);
            if (check && count < 0.3 * total) {
                ibest = i + 1;
                check = false;
            }
            diff = diffa[i];
            if (diff > maxdiff)
                maxdiff = diff;
        }

            /* Add small compensation for higher resolution */
        int xres = pixs.XRes;
        if (xres == 0) xres = 150;
        if (xres > 110) ibest++;
        if (ibest < 2) {
            Log.Info("pixWordMaskByDilation", "setting ibest to minimum allowed value of 2");
            ibest = 2;
        }

        if (pixadb != null) {
            Utils.LeptMkdir("lept/jb");
            Log.Info("pixWordMaskByDilation", $"Best dilation: {Math.Max(3, ibest + 1)}");
            Numa naseq = Numa.MakeSequence(1, 1, nacc.Count);
            AddPlotToDebug(pixadb, naseq, nacc, "/tmp/lept/jb/numcc",
                           "Number of cc vs. horizontal dilation",
                           "Sel horiz", "Number of cc");
            naseq = Numa.MakeSequence(1, 1, nadiff.Count);
            AddPlotToDebug(pixadb, naseq, nadiff, "/tmp/lept/jb/diffcc",
                           "Diff count of cc vs. horizontal dilation",
                           "Sel horiz", "Diff in cc");
            Pix pix3 = CloseBrick(null, pixs, ibest + 1, 1);
            Pix pix4 = pix3.ScaleToSize(600, 0);
            pixadb.AddPix(pix4, L.L_INSERT);
        }

        size = ibest + 1;
        pixm = CloseBrick(null, pixs, ibest + 1, 1);
    }

    /// <summary>
    /// C: pix3 = gplotGeneralPix2(...); pixaAddPix(pixadb, pix3, L_INSERT).  Without gnuplot the
    /// plot image is not made; the pix is then not added (in C pixRead() fails and pixaAddPix() logs an error).
    /// </summary>
    private static void AddPlotToDebug(Pixa pixadb, Numa na1, Numa na2, string rootname,
                                       string title, string xlabel, string ylabel)
    {
        Pix pix3;
        try {
            pix3 = Gplot.GeneralPix2(na1, na2, L.GPLOT_LINES, rootname, title, xlabel, ylabel);
        } catch (LeptonicaException) {
            Log.Error("pixaAddPix", "pix not defined");
            return;
        }
        pixadb.AddPix(pix3, L.L_INSERT);
    }

    // leptonica: pixWordBoxesByDilation (jbclass.c)
    /// <param name="minwidth">saved components; smaller are discarded</param>
    /// <param name="minheight">saved components; smaller are discarded</param>
    /// <param name="maxwidth">saved components; larger are discarded</param>
    /// <param name="maxheight">saved components; larger are discarded</param>
    /// <param name="boxa">of dilated word mask</param>
    /// <param name="size">size of good horizontal dilation</param>
    /// <param name="pixadb">[optional] debug: pixa of intermediate steps</param>
    /// <remarks>
    /// (1) Returns a pruned set of word boxes.
    /// (2) See pixWordMaskByDilation().
    /// (3) pixs is 1 bpp; typ. 75 - 200 ppi.
    /// </remarks>
    public void WordBoxesByDilation(int minwidth, int minheight, int maxwidth, int maxheight,
                                    out Boxa boxa, out int size, Pixa? pixadb)
    {
        Pix pixs = this;

        size = 0;
        boxa = null!;
        if (pixs.Depth != 1)
            throw Lept.Arg("pixWordBoxesByDilation", "pixs undefined or not 1 bpp");

            /* Make a first estimate of the word mask */
        pixs.WordMaskByDilation(out Pix pix1, out size, pixadb);

            /* Prune the word mask.  Get the bounding boxes of the words.
             * Remove the small ones, which can be due to punctuation
             * that was not joined to a word.  Also remove the large ones,
             * which are not likely to be words. */
        Boxa boxa1 = pix1.ConnComp(8);
        Boxa boxa2 = boxa1.SelectBySize(minwidth, minheight, L.L_SELECT_IF_BOTH,
                                        L.L_SELECT_IF_GTE, out _);
        boxa = boxa2.SelectBySize(maxwidth, maxheight, L.L_SELECT_IF_BOTH,
                                  L.L_SELECT_IF_LTE, out _);
        if (pixadb != null) {
            Pix pix2 = pixs.UnpackBinary(32, 1);
            pix2.RenderBoxaArb(boxa1, 2, 255, 0, 0);
            pixadb.AddPix(pix2, L.L_INSERT);
            pix2 = pixs.UnpackBinary(32, 1);
            pix2.RenderBoxaArb(boxa2, 2, 0, 255, 0);
            pixadb.AddPix(pix2, L.L_INSERT);
        }
    }
}
