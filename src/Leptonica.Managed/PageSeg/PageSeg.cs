using System;
using System.IO;

namespace Leptonica;

/*
 *  pageseg.c: page segmentation.
 *
 *      Top level page segmentation
 *          Pix.GetRegionsBinary
 *
 *      Halftone region extraction
 *          Pix.GenHalftoneMask (deprecated wrapper), Pix.GenerateHalftoneMask
 *
 *      Textline extraction
 *          Pix.GenTextlineMask
 *
 *      Textblock extraction
 *          Pix.GenTextblockMask
 *
 *      Location and extraction of page foreground; cleaning pages
 *          Pix.CropImage, Pix.CleanImage, Pix.FindPageForeground
 *
 *      Extraction of characters from image with only text
 *          Pix.SplitIntoCharacters, Pix.SplitComponentWithProfile
 *
 *      Extraction of lines of text
 *          Pix.ExtractTextlines, Pix.ExtractRawTextlines
 *
 *      How many text columns
 *          Pix.CountTextColumns
 *
 *      Decision: text vs photo
 *          Pix.DecideIfText, Pix.FindThreshFgExtent
 *
 *      Decision: table vs text
 *          Pix.DecideIfTable, Pix.Prepare1bpp
 *
 *      Estimate the grayscale background value
 *          Pix.EstimateBackground
 *
 *      Largest white or black rectangles in an image
 *          Pix.FindLargeRectangles, Pix.FindLargestRectangle
 *
 *      Generate rectangle inside connected component
 *          Pix.FindRectangleInCC
 *
 *      Automatic photoinvert for OCR
 *          Pix.AutoPhotoinvert
 *
 *  Differences from C:
 *    - Optional output pointers are 'out' parameters that are always set.  The
 *      debug outputs of pixSplitIntoCharacters(), pixSplitComponentWithProfile(),
 *      pixFindLargeRectangles() and pixFindLargestRectangle() have overloads
 *      without them, which skip the work of making the debug image.
 *    - Output to stderr (lept_stderr) is dropped.
 */

/// <summary>Constants from pix.h used by the page segmentation functions.</summary>
public static partial class L
{
    /* Page orientation */
    /// <summary>Typical page orientation: the page is viewed with height greater than width.</summary>
    public const int L_PORTRAIT_MODE = 0;
    /// <summary>The page is viewed rotated by 90 degrees relative to portrait mode.</summary>
    public const int L_LANDSCAPE_MODE = 1;

    /* Compression encoding for pdf output (imageio.h) */
    /// <summary>Use the default encoding, chosen from the image content.</summary>
    public const int L_DEFAULT_ENCODE = 0;

    /* Selection of a box from two boxes */
    /// <summary>Use the union (bounding region) of the two boxes.</summary>
    public const int L_GEOMETRIC_UNION = 1;
    /// <summary>Use the intersection (overlap region) of the two boxes.</summary>
    public const int L_GEOMETRIC_INTERSECTION = 2;
    /// <summary>Use the box with the largest area.</summary>
    public const int L_LARGEST_AREA = 3;
    /// <summary>Use the box with the smallest area.</summary>
    public const int L_SMALLEST_AREA = 4;
}

public sealed partial class Pix
{
        /* These functions are not intended to work on very low-res images */
    private const int MinWidth = 100;
    private const int MinHeight = 100;

    /*------------------------------------------------------------------*
     *                     Top level page segmentation                  *
     *------------------------------------------------------------------*/
    // leptonica: pixGetRegionsBinary (pageseg.c)
    /// <summary>Top level page segmentation: halftone, textline and textblock masks.</summary>
    /// <param name="pixhm">halftone mask</param>
    /// <param name="pixtm">textline mask</param>
    /// <param name="pixtb">textblock mask</param>
    /// <param name="pixadb">input for collecting debug pix; use null to skip</param>
    /// <remarks>
    /// (1) It is best to deskew the image before segmenting.
    /// (2) Passing in %pixadb enables debug output.
    /// (3) This is called on a 1 bpp image that is assumed to be 300 to 400 ppi.
    /// </remarks>
    public void GetRegionsBinary(out Pix pixhm, out Pix pixtm, out Pix pixtb, Pixa? pixadb)
    {
        const string func = "pixGetRegionsBinary";
        Pix pixs = this;
        Pix pix1, pix2;
        Pix? pixtb2, pixtbf2;

        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        pixs.GetDimensions(out int w, out int h, out _);
        if (w < MinWidth || h < MinHeight)
            throw Lept.Arg(func, $"pix too small: w = {w}, h = {h}");

            /* 2x reduce, to 150 -200 ppi */
        Pix pixr = pixs.ReduceRankBinaryCascade(1, 0, 0, 0);
        pixadb?.AddPix(pixr, L.L_COPY);

            /* Get the halftone mask */
        Pix pixhm2 = pixr.GenerateHalftoneMask(out Pix pixtext, out _, pixadb);  /* halftone mask; 2x reduction */

            /* Get the textline mask from the text pixels */
        Pix pixtm2 = pixtext.GenTextlineMask(out Pix pixvws, out _, pixadb);  /* textline mask; 2x reduction */

            /* Get the textblock mask from the textline mask */
        pixtb2 = pixtm2.GenTextblockMask(pixvws, pixadb);  /* textblock mask; 2x reduction */

            /* Remove small components from the mask, where a small
             * component is defined as one with both width and height < 60 */
        pixtbf2 = null;  /* textblock mask; 2x reduction; small comps filtered */
        if (pixtb2 != null) {
            pixtbf2 = pixtb2.SelectBySize(60, 60, 4, L.L_SELECT_IF_EITHER,
                                          L.L_SELECT_IF_GTE, out _);
            pixadb?.AddPix(pixtbf2, L.L_COPY);
        }

            /* Expand all masks to full resolution, and do filling or
             * small dilations for better coverage. */
        pixhm = pixhm2.ExpandReplicate(2);
        pix1 = SeedfillBinary(null, pixhm, pixs, 8);
        Or(pixhm, pixhm, pix1);
        pixadb?.AddPix(pixhm, L.L_COPY);

        pix1 = pixtm2.ExpandReplicate(2);
        pixtm = DilateBrick(null, pix1, 3, 3);
        pixadb?.AddPix(pixtm, L.L_COPY);

        if (pixtbf2 != null) {
            pix1 = pixtbf2.ExpandReplicate(2);
            pixtb = DilateBrick(null, pix1, 3, 3);
            pixadb?.AddPix(pixtb, L.L_COPY);
        } else {
            pixtb = CreateTemplate(pixs);  /* empty mask */
        }

            /* Debug: identify objects that are neither text nor halftone image */
        if (pixadb != null) {
            pix1 = Subtract(null, pixs, pixtm);  /* remove text pixels */
            pix2 = Subtract(null, pix1, pixhm);  /* remove halftone pixels */
            pixadb.AddPix(pix2, L.L_INSERT);
        }

            /* Debug: display textline components with random colors */
        if (pixadb != null) {
            pixtm.ConnComp(out Pixa pixa, 8);
            pixtm.GetDimensions(out int w2, out int h2, out _);
            pix1 = pixa.DisplayRandomCmap(w2, h2);
            pix1.Colormap!.ResetColor(0, 255, 255, 255);
            pixadb.AddPix(pix1, L.L_INSERT);
        }

            /* Debug: identify the outlines of each textblock */
        if (pixadb != null) {
            Ptaa ptaa = pixtb.GetOuterBordersPtaa();
            Utils.LeptMkdir("lept/pageseg");
            ptaa.WriteDebug("/tmp/lept/pageseg/tb_outlines.ptaa", 1);
            pix1 = pixtb.RenderRandomCmapPtaa(ptaa, 1, 16, 1);
            PixColormap cmap = pix1.Colormap!;
            cmap.ResetColor(0, 130, 130, 130);
            pixadb.AddPix(pix1, L.L_INSERT);
        }

            /* Debug: get b.b. for all mask components */
        if (pixadb != null) {
            Boxa bahm = pixhm.ConnComp(4);
            Boxa batm = pixtm.ConnComp(4);
            Boxa batb = pixtb.ConnComp(4);
            bahm.WriteDebug("/tmp/lept/pageseg/htmask.boxa");
            batm.WriteDebug("/tmp/lept/pageseg/textmask.boxa");
            batb.WriteDebug("/tmp/lept/pageseg/textblock.boxa");
        }
        if (pixadb != null) {
            pixadb.ConvertToPdf(0, 1.0f, 0, 0, "Debug page segmentation",
                                "/tmp/lept/pageseg/debug.pdf");
            Log.Info(func, "Writing debug pdf to /tmp/lept/pageseg/debug.pdf");
        }
    }

    /*------------------------------------------------------------------*
     *                    Halftone region extraction                    *
     *------------------------------------------------------------------*/
    // leptonica: pixGenHalftoneMask (pageseg.c)
    /// <summary>Deprecated wrapper of pixGenerateHalftoneMask().</summary>
    /// <remarks>
    /// This wrapper avoids an ABI change with tesseract 3.0.4.
    /// It should be removed when we no longer need to support 3.0.4.
    /// The debug parameter is ignored (assumed 0).
    /// </remarks>
    public Pix GenHalftoneMask(out Pix pixtext, out bool htfound, int debug)
    {
        return GenerateHalftoneMask(out pixtext, out htfound, null);
    }

    // leptonica: pixGenerateHalftoneMask (pageseg.c)
    /// <param name="pixtext">text part of pixs</param>
    /// <param name="htfound">true if the mask is not empty</param>
    /// <param name="pixadb">input for collecting debug pix; use null to skip</param>
    /// <returns>pixd halftone mask</returns>
    /// <remarks>
    /// (1) This is not intended to work on small thumbnails.  The
    ///     dimensions of pixs must be at least MinWidth x MinHeight.
    /// (2) pixs is 1 bpp, assumed to be 150 to 200 ppi.
    /// </remarks>
    public Pix GenerateHalftoneMask(out Pix pixtext, out bool htfound, Pixa? pixadb)
    {
        Pix pixd = GenerateHalftoneMaskImpl(true, out Pix? pixt, out htfound, pixadb);
        pixtext = pixt!;
        return pixd;
    }

    /// <summary>Like <see cref="GenerateHalftoneMask(out Pix,out bool,Pixa?)"/>, without the text part.</summary>
    public Pix GenerateHalftoneMask(out bool htfound, Pixa? pixadb)
    {
        return GenerateHalftoneMaskImpl(false, out _, out htfound, pixadb);
    }

    private Pix GenerateHalftoneMaskImpl(bool wanttext, out Pix? pixtext, out bool htfound, Pixa? pixadb)
    {
        const string func = "pixGenerateHalftoneMask";
        Pix pixs = this;

        pixtext = null;
        htfound = false;
        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        pixs.GetDimensions(out int w, out int h, out _);
        if (w < MinWidth || h < MinHeight)
            throw Lept.Arg(func, $"pix too small: w = {w}, h = {h}");

            /* Compute seed for halftone parts at 8x reduction */
        Pix pix1 = pixs.ReduceRankBinaryCascade(4, 4, 0, 0);
        Pix pix2 = OpenBrick(null, pix1, 5, 5);
        Pix pixhs = pix2.ExpandReplicate(4);  /* back to 2x reduction */
        pixadb?.AddPix(pixhs, L.L_COPY);

            /* Compute mask for connected regions */
        Pix pixhm = CloseSafeBrick(null, pixs, 4, 4);
        pixadb?.AddPix(pixhm, L.L_COPY);

            /* Fill seed into mask to get halftone mask */
        Pix pixd = SeedfillBinary(null, pixhs, pixhm, 4);
        pixadb?.AddPix(pixd, L.L_COPY);

            /* Check if mask is empty */
        bool empty = pixd.Zero();
        if (!empty)
            htfound = true;

            /* Optionally, get all pixels that are not under the halftone mask */
        if (wanttext) {
            if (empty)
                pixtext = Copy(null, pixs);
            else
                pixtext = Subtract(null, pixs, pixd);
            pixadb?.AddPix(pixtext, L.L_COPY);
        }

        return pixd;
    }

    /*------------------------------------------------------------------*
     *                         Textline extraction                      *
     *------------------------------------------------------------------*/
    // leptonica: pixGenTextlineMask (pageseg.c)
    /// <param name="pixvws">vertical whitespace mask</param>
    /// <param name="tlfound">true if the mask is not empty</param>
    /// <param name="pixadb">input for collecting debug pix; use null to skip</param>
    /// <returns>pixd textline mask</returns>
    /// <remarks>
    /// (1) The input pixs should be deskewed.
    /// (2) pixs should have no halftone pixels.
    /// (3) This is not intended to work on small thumbnails.  The
    ///     dimensions of pixs must be at least MinWidth x MinHeight.
    /// (4) Both the input image and the returned textline mask
    ///     are at the same resolution.
    /// (5) pixs is 1 bpp, assumed to be 150 to 200 ppi.
    /// </remarks>
    public Pix GenTextlineMask(out Pix pixvws, out bool tlfound, Pixa? pixadb)
    {
        const string func = "pixGenTextlineMask";
        Pix pixs = this;

        tlfound = false;
        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        pixs.GetDimensions(out int w, out int h, out _);
        if (w < MinWidth || h < MinHeight)
            throw Lept.Arg(func, $"pix too small: w = {w}, h = {h}");

            /* First we need a vertical whitespace mask.  Invert the image. */
        Pix pix1 = Invert(null, pixs);

            /* The whitespace mask will break textlines where there
             * is a large amount of white space below or above.
             * This can be prevented by identifying regions of the
             * inverted image that have large horizontal extent (bigger than
             * the separation between columns) and significant
             * vertical extent (bigger than the separation between
             * textlines), and subtracting this from the bg. */
        Pix pix2 = pix1.MorphCompSequence("o80.60", 0);
        Subtract(pix1, pix1, pix2);
        pixadb?.AddPix(pix1, L.L_COPY);

            /* Identify vertical whitespace by opening the remaining bg.
             * o5.1 removes thin vertical bg lines and o1.200 extracts
             * long vertical bg lines. */
        pixvws = pix1.MorphCompSequence("o5.1 + o1.200", 0);
        pixadb?.AddPix(pixvws, L.L_COPY);

            /* Three steps to getting text line mask:
             *   (1) close the characters and words in the textlines
             *   (2) open the vertical whitespace corridors back up
             *   (3) small opening to remove noise    */
        pix1 = pixs.MorphSequence("c30.1", 0);
        pixadb?.AddPix(pix1, L.L_COPY);
        Pix pixd = Subtract(null, pix1, pixvws);
        OpenBrick(pixd, pixd, 3, 3);
        pixadb?.AddPix(pixd, L.L_COPY);

            /* Check if text line mask is empty */
        if (!pixd.Zero())
            tlfound = true;

        return pixd;
    }

    /*------------------------------------------------------------------*
     *                       Textblock extraction                       *
     *------------------------------------------------------------------*/
    // leptonica: pixGenTextblockMask (pageseg.c)
    /// <param name="pixvws">vertical white space mask</param>
    /// <param name="pixadb">input for collecting debug pix; use null to skip</param>
    /// <returns>pixd textblock mask, or null if empty</returns>
    /// <remarks>
    /// (1) Both the input masks (textline and vertical white space) and
    ///     the returned textblock mask are at the same resolution.
    /// (2) This is not intended to work on small thumbnails.  The
    ///     dimensions of pixs must be at least MinWidth x MinHeight.
    /// (3) The result is somewhat noisy, in that small "blocks" of
    ///     text may be included.  These can be removed by post-processing,
    ///     using, e.g.,
    ///        pixSelectBySize(pix, 60, 60, 4, L_SELECT_IF_EITHER,
    ///                        L_SELECT_IF_GTE, NULL);
    /// (4) pixs is the 1 bpp textline mask, assumed to be 150 to 200 ppi.
    /// </remarks>
    public Pix? GenTextblockMask(Pix pixvws, Pixa? pixadb)
    {
        const string func = "pixGenTextblockMask";
        Pix pixs = this;

        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        pixs.GetDimensions(out int w, out int h, out _);
        if (w < MinWidth || h < MinHeight)
            throw Lept.Arg(func, $"pix too small: w = {w}, h = {h}");

            /* Join pixels vertically to make a textblock mask */
        Pix pix1 = pixs.MorphSequence("c1.10 + o4.1", 0);
        if (pix1.Zero()) {
            Log.Info(func, "no fg pixels in textblock mask");
            return null;
        }
        pixadb?.AddPix(pix1, L.L_COPY);

            /* Solidify the textblock mask and remove noise:
             *   (1) For each cc, close the blocks and dilate slightly
             *       to form a solid mask.
             *   (2) Small horizontal closing between components.
             *   (3) Open the white space between columns, again.
             *   (4) Remove small components. */
        Pix pix2 = pix1.MorphSequenceByComponent("c30.30 + d3.3", 8, 0, 0, out _);
        CloseSafeBrick(pix2, pix2, 10, 1);
        pixadb?.AddPix(pix2, L.L_COPY);
        Pix pix3 = Subtract(null, pix2, pixvws);
        pixadb?.AddPix(pix3, L.L_COPY);
        Pix pixd = pix3.SelectBySize(25, 5, 8, L.L_SELECT_IF_BOTH,
                                     L.L_SELECT_IF_GTE, out _);
        pixadb?.AddPix(pixd, L.L_COPY);

        return pixd;
    }

    /*------------------------------------------------------------------*
     *    Location and extraction of page foreground; cleaning pages    *
     *------------------------------------------------------------------*/
    // leptonica: pixCropImage (pageseg.c)
    /// <param name="lr_clear">full res pixels cleared at left and right sides</param>
    /// <param name="tb_clear">full res pixels cleared at top and bottom sides</param>
    /// <param name="edgeclean">parameter for removing edge noise (-1 to 15)
    /// default = 0 (no removal);
    /// 15 is maximally aggressive for random noise
    /// -1 for aggressively removing side noise
    /// -2 to extract page embedded in black background</param>
    /// <param name="lr_border">full res final "added" pixels on left and right</param>
    /// <param name="tb_border">full res final "added" pixels on top and bottom</param>
    /// <param name="maxwiden">max fractional horizontal stretch allowed</param>
    /// <param name="printwiden">0 to skip, 1 for 8.5x11, 2 for A4</param>
    /// <param name="debugfile">[optional] usually is null</param>
    /// <param name="cropbox">crop box at full resolution</param>
    /// <returns>cropped pix; this pix is full resolution (any type or depth)</returns>
    /// <remarks>
    /// (1) This binarizes and crops a page image.
    ///     (a) Binarizes if necessary and does 2x reduction.
    ///     (b) Clears near the border by %lr_clear and %tb_clear full
    ///         resolution pixels.  (This is done at 2x reduction.)
    ///     (c) If %edgeclean > 0, it removes isolated sets of pixels,
    ///         using a close/open operation of size %edgeclean + 1.
    ///         If %edgeclean == -1, it uses a large vertical morphological
    ///         close/open and the extraction of either the largest
    ///         resulting connected component (or the largest two components
    ///         if the page has 2 columns), to eliminate noise on left
    ///         and right sides.
    ///         If %edgeclean == -2, it extracts the page region from a
    ///         possible exterior black surround.
    ///     (d) Find the bounding box of remaining fg pixels and scales
    ///         the box up 2x back to full resolution.
    ///     (e) Crops the binarized image to the bounding box.
    ///     (f) Slightly thickens long horizontal lines.
    ///     (g) Rescales this image to fit within the original image,
    ///         less lr_border on the sides and tb_border above and below.
    ///         The rescaling is done isomorphically with a (possible)
    ///         optional additional widening.  Suggest the additional
    ///         widening factor not exceed 1.15.
    ///     (h) Optionally do additional horizontal stretch if needed to
    ///         better fill a printed page.  Default is 0 to skip; 1 to
    ///         widen for 8.5x11 page, 2 for A4 page.
    ///     Note that (b) - (d) are done at 2x reduction for efficiency.
    /// (2) Side clearing must not exceed 1/6 of the dimension on that side.
    /// (3) The clear and border pixel parameters must be >= 0.
    /// (4) The "clear" parameters act on the input image, whereas the
    ///     "border" parameters act to give a white border to the final
    ///     image.  They are not literally added, because the input and final
    ///     images are the same size.  If the resulting images are to be
    ///     printed, it is useful to have border pixel parameters of at
    ///     least 60 at 300 ppi, to avoid losing content at the edges.
    /// (5) This is not intended to work on small thumbnails.  The
    ///     dimensions of pixs must be at least MinWidth x MinHeight.
    /// (6) Step (f) above helps with orthographically-produced music notation,
    ///     where the horizontal staff lines can be very thin and thus
    ///     subject to printer alias.
    /// (7) With orthographically-produced (as opposed to scanned) images,
    ///     there is no scan noise, so you should skip noise removal
    ///     by setting %edgeclean = 0.
    /// (8) If you are not concerned with printing on paper, use the
    ///     default value 0 for %printwiden.  Widening only takes place
    ///     if the ratio h/w exceeds the specified paper size by 3%,
    ///     and the horizontal scaling factor will not exceed 1.25.
    /// </remarks>
    public Pix CropImage(int lr_clear, int tb_clear, int edgeclean, int lr_border,
                         int tb_border, float maxwiden, int printwiden,
                         string? debugfile, out Box? cropbox)
    {
        const string func = "pixCropImage";
        Pix pixs = this;
        Box? box1;
        Pix pix1, pix2, pix3, pix4;
        bool ret;

        cropbox = null;
        if (edgeclean > 15) {
            Log.Warning(func, "edgeclean > 15; setting to 15");
            edgeclean = 15;
        }
        if (edgeclean < -1)
            edgeclean = -2;  /* C: lept_stderr("Using edgeclean = -2\n") */
        pixs.GetDimensions(out int w, out int h, out _);
        if (w < MinWidth || h < MinHeight)
            throw Lept.Arg(func, $"pix too small: w = {w}, h = {h}");
        if (lr_clear < 0) lr_clear = 0;
        if (tb_clear < 0) tb_clear = 0;
        if (lr_border < 0) lr_border = 0;
        if (tb_border < 0) tb_border = 0;
        if (lr_clear > w / 6 || tb_clear > h / 6)
            throw Lept.Arg(func, $"lr_clear or tb_clear too large; must be <= {w / 6} and {h / 6}");
        if (maxwiden > 1.15)
            Log.Warning(func, $"maxwiden = {maxwiden:F6} > 1.15; suggest between 1.0 and 1.15");
        if (printwiden < 0 || printwiden > 2) printwiden = 0;
        Pixa? pixa1 = (debugfile != null) ? Pixa.Create(5) : null;
        pixa1?.AddPix(pixs, L.L_COPY);

            /* Binarize if necessary and 2x reduction */
        pix1 = pixs.BackgroundNormTo1MinMax(1, 1);
        pix2 = pix1.ReduceRankBinary2(2);

            /* Clear out pixels near the image edges */
        pix2.SetOrClearBorder(lr_clear / 2, lr_clear / 2, tb_clear / 2,
                              tb_clear / 2, L.PIX_CLR);
        pixa1?.AddPix(pix2.Scale(2.0f, 2.0f), L.L_INSERT);

            /* Choose one of three methods for extracting foreground pixels:
             * (1) Include all foreground pixels
             * (2) Do a morphological close/open to remove noise throughout
             *     the image before finding a b.b. for remaining f.g. pixels
             * (3) Do a large vertical closing and choose the largest (by area)
             *     component to avoid foreground noise on left and right sides */
        if (edgeclean == 0) {
            pix2.ClipToForeground(out _, out box1);
            ret = (box1 == null);  /* C: pixClipToForeground() returns 1 if there are no fg pixels */
        } else if (edgeclean > 0) {
            int val = edgeclean + 1;
            string cmd = $"c{val}.{val} + o{val}.{val}";
            pix3 = pix2.MorphSequence(cmd, 0);
            pix3.ClipToForeground(out _, out box1);
            ret = (box1 == null);
        } else if (edgeclean == -1) {
            ret = MaxCompAfterVClosing(pix2, out box1);
        } else {  /* edgeclean == -2 */
            ret = FindPageInsideBlackBorder(pix2, out box1);
        }
        if (ret)
            throw Lept.Fail(func, "no returned b.b. for foreground");

            /* Transform to full resolution */
        Box box2 = box1!.Transform(0, 0, 2.0f, 2.0f);  /* full res */
        if (pixa1 != null) {
            pix2 = Copy(null, pix1);
            pix2.RenderBoxArb(box2, 5, 255, 0, 0);
            pixa1.AddPix(pix2, L.L_INSERT);
        }

            /* Grab the foreground region */
        pix2 = pix1.ClipRectangle(box2, out _)
               ?? throw Lept.Fail(func, "foreground region not clipped");

            /* Slightly thicken long horizontal lines.  This prevents loss of
             * printed thin music staff lines due to aliasing. */
        pix3 = pix2.MorphSequence("o80.1 + d1.2", 0);
        Or(pix2, pix2, pix3);

            /* Rescale the fg and paste into the input-sized image */
        pix3 = RescaleForCropping(pix2, w, h, lr_border, tb_border, maxwiden, out _);
        if (pixa1 != null) {
            pix2 = Copy(null, pix3);
            pixa1.AddPix(pix2, L.L_INSERT);
        }

            /* Optionally widen image if possible, for printing on 8.5 x 11 inch
             * or A4 paper.  Specifically, widen the image if the h/w asperity
             * ratio of the input image exceeds that of the selected paper by
             * more than 3%.  Do not widen by more than 20%.  */
        float r1 = (float)h / (float)w;
        float r2 = 0.0f;  /* for default case */
        if (printwiden == 1)  /* standard */
            r2 = (float)(r1 / 1.294);
        else if (printwiden == 2)  /* A4 */
            r2 = (float)(r1 / 1.414);
        if (r2 > 1.03) {
            r2 = (float)(r2 < 1.20 ? r2 : 1.20);  /* L_MIN(r2, 1.20) */
            pix4 = pix3.Scale(r2, 1.0f);
        } else {
            pix4 = Clone(pix3);
        }

        cropbox = box2;
        if (pixa1 != null) {
            pixa1.AddPix(pix4, L.L_COPY);
            pixa1.ConvertToPdf(0, 1.0f, L.L_DEFAULT_ENCODE, 0, null, debugfile!);
        }
        return pix4;
    }

    // leptonica: pixMaxCompAfterVClosing (pageseg.c)
    /// <param name="pixs">1 bpp (input at 2x reduction)</param>
    /// <param name="pbox">main region at input resolution (2x reduction)</param>
    /// <returns>true on error (C: 1); false if OK</returns>
    /// <remarks>
    /// (1) This removes foreground noise along left and right edges,
    ///     returning a bounding box for the remaining foreground pixels
    ///     at the input resolution.
    /// (2) The input %pixs should be at a resolution 100 - 150 ppi.
    /// (3) It does two 2x level1 rank binary reductions, followed
    ///     by a large vertical close/open, with a very small horizontal
    ///     close/oopen, and then a 4x expansion back to the input resolution.
    /// (4) To work properly with 2-column layout, if the largest and
    ///     second-largest regions are comparable in size, both are included.
    /// (5) This is used as an option to pixCropImage(), when given
    ///     an %edgecrop parameter of -1.
    /// </remarks>
    private static bool MaxCompAfterVClosing(Pix pixs, out Box? pbox)
    {
        const string func = "pixMaxCompAfterVClosing";

        pbox = null;
        if (pixs.Depth != 1) {
            Log.Error(func, "pixs undefined or not 1 bpp");
            return true;
        }

            /* Strong vertical closing */
        Pix pix1 = pixs.MorphSequence("r11 + c3.80 + o3.80 + x4", 0);
        if (pix1.Zero()) {
            Log.Error(func, "pix1 is empty");
            return true;
        }

            /* Find the two c.c. with largest area. If they are not comparable
             * in area, return the bounding box of the largest; otherwise,
             * return the bounding box of both regions. */
        Boxa boxa1 = pix1.ConnCompBB(8);
        Boxa boxa2 = boxa1.Sort(L.L_SORT_BY_AREA, L.L_SORT_DECREASING, out _);
        if (boxa2.Count == 1) {
            pbox = boxa2.GetBox(0, L.L_COPY);
        } else {  /* 2 or more */
            Box box1 = boxa2.GetBox(0, L.L_COPY);
            Box box2 = boxa2.GetBox(1, L.L_COPY);
            box1.GetGeometry(out _, out _, out int w1, out int h1);
            box2.GetGeometry(out _, out _, out int w2, out int h2);
            if (((float)(w2 * h2) / (float)(w1 * h1)) > 0.7) {
                pbox = box1.BoundingRegion(box2);
            } else {
                pbox = box1;
            }
        }
        return false;
    }

    // leptonica: pixFindPageInsideBlackBorder (pageseg.c)
    /// <param name="pixs">1 bpp (input at 2x reduction)</param>
    /// <param name="pbox">page region at input resolution (2x reduction)</param>
    /// <returns>true on error (C: 1); false if OK</returns>
    /// <remarks>
    /// (1) This extracts the page region from the image, returning a
    ///     bounding box for the remaining foreground pixels.  It is designed
    ///     to work when the page is within a fairly solid black border.
    /// (2) It returns a bounding box for the page region at the input res.
    /// (3) The input %pixs is expected to be at a resolution 100 - 150 ppi.
    /// (4) This is used as an option to pixCropImage(), when given an
    ///     %edgecrop parameter of -2.
    /// </remarks>
    private static bool FindPageInsideBlackBorder(Pix pixs, out Box? pbox)
    {
        const string func = "pixFindPageInsideBlackBorder";

        pbox = null;
        if (pixs.Depth != 1) {
            Log.Error(func, "pixs undefined or not 1 bpp");
            return true;
        }

            /* Reduce 4x and remove some remaining small foreground */
        Pix pix1 = pixs.MorphSequence("r22 + c5.5 + o7.7", 0);
        if (pix1.Zero()) {
            Log.Error(func, "pix1 is empty");
            return true;
        }

            /* Photoinvert image and Find the c.c. with largest area. */
        Invert(pix1, pix1);
        Pix pix2 = pix1.MorphSequence("c11.11 + o11.11", 0);
        Boxa boxa1 = pix2.ConnCompBB(8);
        Boxa boxa2 = boxa1.Sort(L.L_SORT_BY_AREA, L.L_SORT_DECREASING, out _);
        Box box1 = boxa2.GetBox(0, L.L_COPY);  /* largest by area */
        Box.AdjustSides(box1, box1, 5, -5, 5, -5);
        Box box2 = box1.Transform(0, 0, 4.0f, 4.0f);

            /* Crop this page from the original image and find the foreground */
        Pix pix3 = pixs.ClipRectangle(box2, out _)
                   ?? throw Lept.Fail(func, "page region not clipped");
        pix3.ClipToForeground(out _, out Box? box3);
        box2.GetGeometry(out int x, out int y, out _, out _);
        pbox = (box3 ?? throw Lept.Arg("boxTransform", "box not defined")).Transform(x, y, 1.0f, 1.0f);
        return false;
    }

    // leptonica: pixRescaleForCropping (pageseg.c)
    /// <param name="pixs">1 bpp</param>
    /// <param name="w">width of output image</param>
    /// <param name="h">height of output image</param>
    /// <param name="lr_border">cleared final border pixels on left and right</param>
    /// <param name="tb_border">cleared final border pixels on top and bottom</param>
    /// <param name="maxwiden">max fractional horizontal stretch allowed; >= 1.0</param>
    /// <param name="pixsc">rescaled foreground region</param>
    /// <returns>pixd  output image</returns>
    /// <remarks>
    /// (1) This rescales %pixs to fit maximally within an image of
    ///     size (w x h), under two conditions:
    ///     (a) the final image has cleared border regions given by the
    ///         input parameters %lr_border and %tb_border, and
    ///     (b) the input image is first isotropically scaled to fit
    ///         maximally within the allowed final region, and then further
    ///         maxiximally widened, subject to the constraints of the
    ///         cleared border and the %maxwiden parameter.
    /// (2) The cleared border pixel parameters must be >= 0.
    /// (3) If there is extra horizontal stretching by a factor
    ///     %maxwiden larger than about 1.15, the appearance may be
    ///     unpleasingly distorted; hence the suggestion not to exceed it.
    /// </remarks>
    private static Pix RescaleForCropping(Pix pixs, int w, int h, int lr_border, int tb_border,
                                          float maxwiden, out Pix pixsc)
    {
        const string func = "pixRescaleForCropping";
        Pix pix1;
        int wf, hf, xf;

        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (lr_border < 0) lr_border = 0;
        if (tb_border < 0) tb_border = 0;
        maxwiden = (1.0 > maxwiden) ? 1.0f : maxwiden;  /* L_MAX(1.0, maxwiden) */
        if (maxwiden > 1.15)
            Log.Warning(func, $"maxwiden = {maxwiden:F6} > 1.15; suggest between 1.0 and 1.15");

            /* Rescale the foreground region.
             * First, decide if scaling is to full width or full height.
             * If scaling to full height, determine how much additional
             * width widening is possible, given the maxwiden constraint.
             * If scaling to full width, both width and height are
             * scaled isotropically.  Scaling is done so that the resulting
             * foreground is maximally widened, so it can be horizontally
             * centered in an image of size (w x h), less %lr_border
             * on each side. */
        pixs.GetDimensions(out int wi, out int hi, out _);
        int wmax = w - 2 * lr_border;
        int hmax = h - 2 * tb_border;
        float ratio = (float)(wmax * hi) / (float)(hmax * wi);
        if (ratio >= 1.0) {  /* width can be widened after isotropic scaling */
            float scaleh = (float)hmax / (float)hi;
            int wn = (int)(scaleh * wi);  /* scaled but not widened */
            float t = (float)wmax / (float)wn;
            float scalewid = (maxwiden < t) ? maxwiden : t;  /* L_MIN */
            float scalew = scaleh * scalewid;
            wf = (int)(scalew * wi);
            hf = hmax;  /* scale to full height */
            pix1 = pixs.Scale(scalew, scaleh);
            xf = (int)((w - wf) / 2.0);
        } else {  /* width cannot be widened after isotropic scaling */
            float scalew = (float)wmax / (float)wi;
            pix1 = pixs.Scale(scalew, scalew);
            wf = wmax;  /* scale to full width */
            hf = (int)(scalew * hi);  /* no extra vertical stretching allowed */
            xf = lr_border;
        }

            /* Paste it, horizontally centered and vertically placed as
             * high as allowed (by %tb_border) into the final page image. */
        Pix pixd = Create(w, h, 1);
        pixd.Rasterop(xf, tb_border, wf, hf, L.PIX_SRC, pix1, 0, 0);

        pixsc = pix1;
        return pixd;
    }

    // leptonica: pixCleanImage (pageseg.c)
    /// <param name="contrast">vary contrast: 1 = lightest; 10 = darkest;
    /// suggest 1 unless light features are being lost</param>
    /// <param name="rotation">cw by 90 degrees: {0,1,2,3} represent
    /// 0, 90, 180 and 270 degree cw rotations</param>
    /// <param name="scale">1 (no scaling) or 2 (2x upscaling)</param>
    /// <param name="opensize">opening size of structuring element for noise
    /// removal: {0 or 1 to skip; 2, 3 for opening}</param>
    /// <returns>cleaned pix; this pix is full resolution (any type or depth)</returns>
    /// <remarks>
    /// (1) This deskews, optionally rotates and darkens, cleans background
    ///     to white, binarizes and optionally removes small noise.
    /// (2) For color and grayscale input, local background normalization is
    ///     done to 200, and a threshold of 180 sets the maximum foreground
    ///     value in the normalized image.
    /// (3) The %contrast parameter adjusts the binarization to avoid losing
    ///     lighter input pixels.  Contrast is increased as %contrast increases
    ///     from 1 to 10.
    /// (4) The %scale parameter controls the thresholding to 1 bpp. Two values:
    ///         1 = threshold
    ///         2 = linear interpolated 2x upscaling before threshold.
    /// (5) The #opensize parameter is the size of a square SEL used with
    ///     opening to remove small speckle noise.  Allowed open sizes are 2,3.
    ///     If this is to be used, try 2 before 3.
    /// (6) This does the image processing for cleanTo1bppFilesToPdf() and
    ///     prog/cleanpdf.c.
    /// </remarks>
    public Pix CleanImage(int contrast, int rotation, int scale, int opensize)
    {
        const string func = "pixCleanImage";
        Pix pixs = this;
        Pix pix1, pix2, pix3, pix4, pix5;

        if (rotation < 0 || rotation > 3)
            throw Lept.Arg(func, $"invalid rotation = {rotation}; rotation must be in  {{0,1,2,3}}");
        if (contrast < 1 || contrast > 10)
            throw Lept.Arg(func, $"invalid contrast = {contrast}; contrast must be in [1...10]");
        if (scale != 1 && scale != 2)  /* NOTE(upstream): the message prints opensize */
            throw Lept.Arg(func, $"invalid scale = {opensize}; scale must be 1 or 2");
        if (opensize > 3)
            throw Lept.Arg(func, $"invalid opensize = {opensize}; opensize must be <= 3");

        if (pixs.Depth == 1) {
            if (rotation > 0)
                pix1 = pixs.RotateOrth(rotation);
            else
                pix1 = Clone(pixs);
            pix2 = pix1.FindSkewAndDeskew(2, out _, out _);
            if (scale == 2)
                pix4 = pix2.ExpandBinaryReplicate(2, 2);
            else  /* scale == 1 */
                pix4 = Clone(pix2);
        } else {
            pix1 = pixs.ConvertTo8MinMax();
            if (rotation > 0)
                pix2 = pix1.RotateOrth(rotation);
            else
                pix2 = Clone(pix1);
            pix3 = pix2.FindSkewAndDeskew(2, out _, out _);
            pix4 = pix3.BackgroundNormTo1MinMax(contrast, scale);
        }

        if (opensize == 2 || opensize == 3) {
            string sequence = $"o{opensize}.{opensize}";
            pix5 = pix4.MorphSequence(sequence, 0);
        } else {
            pix5 = Clone(pix4);
        }

        return pix5;
    }

    // leptonica: pixFindPageForeground (pageseg.c)
    /// <param name="threshold">for binarization; typically about 128</param>
    /// <param name="mindist">min distance of text from border to allow
    /// cleaning near border; at 2x reduction, this
    /// should be larger than 50; typically about 70</param>
    /// <param name="erasedist">when conditions are satisfied, erase anything
    /// within this distance of the edge;
    /// typically 20-30 at 2x reduction</param>
    /// <param name="showmorph">debug: set to a negative integer to show steps
    /// in generating masks; this is typically used
    /// for debugging region extraction</param>
    /// <param name="pixac">debug: allocate outside and pass this in to
    /// accumulate results of each call to this function,
    /// which can be displayed in a mosaic or a pdf.</param>
    /// <returns>box region including foreground, with some pixel noise
    /// removed, or null if not found; pixs is full resolution (any type or depth)</returns>
    /// <remarks>
    /// (1) This doesn't simply crop to the fg.  It attempts to remove
    ///     pixel noise and junk at the edge of the image before cropping.
    ///     The input %threshold is used if pixs is not 1 bpp.
    /// (2) This is not intended to work on small thumbnails.  The
    ///     dimensions of pixs must be at least MinWidth x MinHeight.
    /// (3) Debug: set showmorph to display the intermediate image in
    ///     the morphological operations on this page.
    /// (4) Debug: to get pdf output of results when called repeatedly,
    ///     call with an existing pixac, which will add an image of this page,
    ///     with the fg outlined.  If no foreground is found, there is
    ///     no output for this page image.
    /// </remarks>
    public Box? FindPageForeground(int threshold, int mindist, int erasedist,
                                   int showmorph, Pixacomp? pixac)
    {
        const string func = "pixFindPageForeground";
        Pix pixs = this;
        Pix pix1;

        pixs.GetDimensions(out int w, out int h, out _);
        if (w < MinWidth || h < MinHeight)
            throw Lept.Arg(func, $"pix too small: w = {w}, h = {h}");

            /* Binarize, downscale by 0.5, remove the noise to generate a seed,
             * and do a seedfill back from the seed into those 8-connected
             * components of the binarized image for which there was at least
             * one seed pixel. */
        int flag = (showmorph != 0) ? 100 : 0;
        Pix pixb = pixs.ConvertTo1(threshold);
        Pix pixb2 = pixb.Scale(0.5f, 0.5f);
        Pix pixseed = pixb2.MorphSequence("o1.2 + c9.9 + o3.3", flag);
        pix1 = pixb2.MorphSequence("o50.1", 0);
        Or(pixseed, pixseed, pix1);
        pix1 = pixb2.MorphSequence("o1.50", 0);
        Or(pixseed, pixseed, pix1);
        Pix pixsf = SeedfillBinary(null, pixseed, pixb2, 8);
        Pix pixm = pixsf.RemoveBorderConnComps(8);

            /* Now, where is the main block of text?  We want to remove noise near
             * the edge of the image, but to do that, we have to be convinced that
             * (1) there is noise and (2) it is far enough from the text block
             * and close enough to the edge.  For each edge, if the block
             * is more than mindist from that edge, then clean 'erasedist'
             * pixels from the edge. */
        pix1 = pixm.MorphSequence("c50.50", flag);
        Boxa ba1 = pix1.ConnComp(8);
        Boxa ba2 = ba1.Sort(L.L_SORT_BY_AREA, L.L_SORT_DECREASING, out _);
        pix1.GetDimensions(out w, out h, out _);
        int nbox = ba2.Count;
        if (nbox > 1) {
            Box box = ba2.GetBox(0, L.L_CLONE);
            box.GetGeometry(out int bx, out int by, out int bw, out int bh);
            int left = (bx > mindist) ? erasedist : 0;
            int right = (w - bx - bw > mindist) ? erasedist : 0;
            int top = (by > mindist) ? erasedist : 0;
            int bottom = (h - by - bh > mindist) ? erasedist : 0;
            pixm.SetOrClearBorder(left, right, top, bottom, L.PIX_CLR);
        }

            /* Locate the foreground region; don't bother cropping */
        pixm.ClipToForeground(out _, out Box? boxfg);

            /* Sanity check the fg region.  Make sure it's not confined
             * to a thin boundary on the left and right sides of the image,
             * in which case it is likely to be noise. */
        if (boxfg != null) {
            Box boxin = Box.Create((int)(0.1 * w), 0, (int)(0.8 * w), h);
            bool intersects = boxfg.Intersects(boxin);
            if (!intersects) boxfg = null;
        }

        Box? boxd = null;
        if (boxfg != null) {
            Box.AdjustSides(boxfg, boxfg, -2, 2, -2, 2);  /* tiny expansion */
            boxd = boxfg.Transform(0, 0, 2.0f, 2.0f);

                /* Save the debug image showing the box for this page */
            if (pixac != null) {
                Pix pixg2 = pixb.Convert1To4Cmap();
                pixg2.RenderBoxArb(boxd, 3, 255, 0, 0);
                pixac.AddPix(pixg2, L.IFF_DEFAULT);
            }
        }

        return boxd;
    }

    /*------------------------------------------------------------------*
     *         Extraction of characters from image with only text       *
     *------------------------------------------------------------------*/
    // leptonica: pixSplitIntoCharacters (pageseg.c)
    /// <param name="minw">min component width for initial filtering; typ. 4</param>
    /// <param name="minh">min component height for initial filtering; typ. 4</param>
    /// <param name="boxa">character bounding boxes</param>
    /// <param name="pixa">character images</param>
    /// <param name="pixdebug">showing splittings; null if there was no splitting</param>
    /// <remarks>
    /// (1) This is a simple function that attempts to find split points
    ///     based on vertical pixel profiles.
    /// (2) It should be given an image that has an arbitrary number
    ///     of text characters.
    /// (3) The returned pixa includes the boxes from which the
    ///     (possibly split) components are extracted.
    /// (4) pixs is 1 bpp and contains only deskewed text.
    /// </remarks>
    public void SplitIntoCharacters(int minw, int minh, out Boxa boxa, out Pixa pixa,
                                    out Pix? pixdebug)
    {
        SplitIntoCharactersImpl(minw, minh, true, out boxa, out pixa, out pixdebug);
    }

    /// <summary>Like <see cref="SplitIntoCharacters(int,int,out Boxa,out Pixa,out Pix?)"/>, without the debug image.</summary>
    public void SplitIntoCharacters(int minw, int minh, out Boxa boxa, out Pixa pixa)
    {
        SplitIntoCharactersImpl(minw, minh, false, out boxa, out pixa, out _);
    }

    private void SplitIntoCharactersImpl(int minw, int minh, bool wantdebug, out Boxa boxa,
                                         out Pixa pixa, out Pix? pixdebug)
    {
        Pix pixs = this;

        pixdebug = null;
        if (pixs.Depth != 1)
            throw Lept.Arg("pixSplitIntoCharacters", "pixs not defined or not 1 bpp");

            /* Remove the small stuff */
        Pix pix1 = pixs.SelectBySize(minw, minh, 8, L.L_SELECT_IF_BOTH,
                                     L.L_SELECT_IF_GT, out _);

            /* Small vertical close for consolidation */
        Pix pix2 = pix1.MorphSequence("c1.10", 0);

            /* Get the 8-connected components */
        pix2.ConnComp(out Pixa pixa1, 8);

            /* Split the components if obvious */
        int ncomp = pixa1.Count;
        Boxa boxa2 = Boxa.Create(ncomp);
        Pixa? pixadb = wantdebug ? Pixa.Create(ncomp) : null;
        for (int i = 0; i < ncomp; i++) {
            Pix pix = pixa1.GetPix(i, L.L_CLONE);
            Boxa boxat1;
            if (wantdebug) {
                boxat1 = pix.SplitComponentWithProfile(10, 7, out Pix? pixdb);
                if (pixdb != null)
                    pixadb!.AddPix(pixdb, L.L_INSERT);
            } else {
                boxat1 = pix.SplitComponentWithProfile(10, 7);
            }
            pixa1.GetBoxGeometry(i, out int xoff, out int yoff, out _, out _);
            Boxa boxat2 = boxat1.Transform(xoff, yoff, 1.0f, 1.0f);
            boxa2.Join(boxat2, 0, -1);
        }

            /* Generate the debug image */
        if (wantdebug) {
            if (pixadb!.Count > 0) {
                pixdebug = pixadb.DisplayTiledInRows(32, 1500, 1.0f, 0, 20, 1);
            }
        }

            /* Do a 2D sort on the bounding boxes, and flatten the result to 1D */
        Boxaa baa = boxa2.Sort2d(out _, 0, 0, 5);
        Boxa boxad = baa.FlattenToBoxa(out _, L.L_CLONE);

            /* Extract the pieces from the input image */
        pixa = pixs.ClipRectangles(boxad);
        boxa = boxad;
    }

    // leptonica: pixSplitComponentWithProfile (pageseg.c)
    /// <param name="delta">distance used in extrema finding in a numa; typ. 10</param>
    /// <param name="mindel">minimum required difference between profile
    /// minimum and profile values +2 and -2 away; typ. 7</param>
    /// <param name="pixdebug">debug image of splitting; null if there was no splitting</param>
    /// <returns>boxa of c.c. after splitting; pixs is 1 bpp, exactly one connected component</returns>
    /// <remarks>
    /// (1) This will split the most obvious cases of touching characters.
    ///     The split points it is searching for are narrow and deep
    ///     minimima in the vertical pixel projection profile, after a
    ///     large vertical closing has been applied to the component.
    /// (2) NOTE(upstream): array1[xmin - 2] is read without checking that
    ///     xmin >= 2.
    /// </remarks>
    public Boxa SplitComponentWithProfile(int delta, int mindel, out Pix? pixdebug)
    {
        return SplitComponentWithProfileImpl(delta, mindel, true, out pixdebug);
    }

    /// <summary>Like <see cref="SplitComponentWithProfile(int,int,out Pix?)"/>, without the debug image.</summary>
    public Boxa SplitComponentWithProfile(int delta, int mindel)
    {
        return SplitComponentWithProfileImpl(delta, mindel, false, out _);
    }

    private Boxa SplitComponentWithProfileImpl(int delta, int mindel, bool wantdebug, out Pix? pixdebug)
    {
        Pix pixs = this;
        Box box;

        pixdebug = null;
        if (pixs.Depth != 1)
            throw Lept.Arg("pixSplitComponentWithProfile", "pixa undefined or not 1 bpp");
        pixs.GetDimensions(out int w, out int h, out _);

            /* Closing to consolidate characters vertically */
        Pix pix1 = CloseSafeBrick(null, pixs, 1, 100);

            /* Get extrema of column projections */
        Boxa boxad = Boxa.Create(2);
        Numa na1 = pix1.CountPixelsByColumn();  /* w elements */
        Numa na2 = na1.FindExtrema(delta, out _);
        int n2 = na2.Count;
        if (n2 < 3) {  /* no split possible */
            box = Box.Create(0, 0, w, h);
            boxad.AddBox(box, L.L_INSERT);
            return boxad;
        }

            /* Look for sufficiently deep and narrow minima.
             * All minima of of interest must be surrounded by max on each
             * side.  firstmin is the index of first possible minimum. */
        int[] array1 = na1.GetIArray();
        int[] array2 = na2.GetIArray();
        int firstmin = (array1[array2[0]] > array1[array2[1]]) ? 1 : 2;
        Numa nasplit = Numa.Create(n2);  /* will hold split locations */
        for (int i = firstmin; i < n2 - 1; i += 2) {
            int xmin = array2[i];
            int nmin = array1[xmin];
            if (xmin + 2 >= w) break;  /* no more splits possible */
            int nleft = array1[xmin - 2];
            int nright = array1[xmin + 2];
            if (nleft - nmin >= mindel && nright - nmin >= mindel)  /* split */
                nasplit.AddNumber(xmin);
        }
        int nsplit = nasplit.Count;

        if (nsplit == 0) {  /* no splitting */
            box = Box.Create(0, 0, w, h);
            boxad.AddBox(box, L.L_INSERT);
            return boxad;
        }

            /* Use split points to generate b.b. after splitting */
        int xshift = 0;
        for (int i = 0; i < nsplit; i++) {
            int isplit = nasplit.GetIValue(i);
            box = Box.Create(xshift, 0, isplit - xshift, h);
            boxad.AddBox(box, L.L_INSERT);
            xshift = isplit + 1;
        }
        box = Box.Create(xshift, 0, w - xshift, h);
        boxad.AddBox(box, L.L_INSERT);

        if (wantdebug) {
            Pix pixdb = pixs.ConvertTo32();
            int ncomp = boxad.Count;
            for (int i = 0; i < ncomp; i++) {
                box = boxad.GetBox(i, L.L_CLONE);
                pixdb.RenderBoxBlend(box, 1, 255, 0, 0, 0.5f);
            }
            pixdebug = pixdb;
        }

        return boxad;
    }

    /*------------------------------------------------------------------*
     *                    Extraction of lines of text                   *
     *------------------------------------------------------------------*/
    // leptonica: pixExtractTextlines (pageseg.c)
    /// <param name="maxw">initial filtering: remove any components in pixs with components larger than maxw or maxh</param>
    /// <param name="maxh">initial filtering: see maxw</param>
    /// <param name="minw">final filtering: remove extracted 'lines'
    /// with sizes smaller than minw or minh; use 0 for default.</param>
    /// <param name="minh">final filtering: see minw</param>
    /// <param name="adjw">final adjustment of boxes representing each
    /// text line.  If > 0, these increase the box size at each edge by this amount.</param>
    /// <param name="adjh">final adjustment: see adjw</param>
    /// <param name="pixadb">pixa for saving intermediate steps; null to omit</param>
    /// <returns>pixa of textline images, including bounding boxes, or
    /// null if there are no fg pixels; pixs is any depth, assumed to have nearly horizontal text</returns>
    /// <remarks>
    /// (1) This function assumes that textline fragments have sufficient
    ///     vertical separation and small enough skew so that a
    ///     horizontal dilation sufficient to join words will not join
    ///     textlines.  It does not guarantee that horizontally adjacent
    ///     textline fragments on the same line will be joined.
    /// (2) For images with multiple columns, it attempts to avoid joining
    ///     textlines across the space between columns.  If that is not
    ///     a concern, you can also use pixExtractRawTextlines(),
    ///     which will join them with alacrity.
    /// (3) This first removes components from pixs that are either
    ///     wide (> %maxw) or tall (> %maxh).
    /// (4) A final filtering operation removes small components, such
    ///     that width &lt; %minw or height &lt; %minh.
    /// (5) For reasonable accuracy, the resolution of pixs should be
    ///     at least 100 ppi.  For reasonable efficiency, the resolution
    ///     should not exceed 600 ppi.
    /// (6) This can be used to determine if some region of a scanned
    ///     image is horizontal text.
    /// (7) As an example, for a pix with resolution 300 ppi, a reasonable
    ///     set of parameters is:
    ///        pixExtractTextlines(pix, 150, 150, 36, 20, 5, 5, NULL);
    ///     The defaults minw and minh for 300 ppi are about 36 and 20,
    ///     so the same result is obtained with:
    ///        pixExtractTextlines(pix, 150, 150, 0, 0, 5, 5, NULL);
    /// (8) The output pixa is composed of subimages, one for each textline,
    ///     and the boxa in the pixa tells where in %pixs each textline goes.
    /// </remarks>
    public Pixa? ExtractTextlines(int maxw, int maxh, int minw, int minh, int adjw, int adjh,
                                  Pixa? pixadb)
    {
        Pix pixs = this;
        Pix pix1, pix2, pix3;

            /* Binarize carefully, if necessary */
        if (pixs.Depth > 1) {
            pix2 = pixs.ConvertTo8(false);
            pix3 = pix2.CleanBackgroundToWhite(null, null, 1.0f, 70, 190);
            pix1 = pix3.ThresholdToBinary(150);
        } else {
            pix1 = Clone(pixs);
        }
        if (pix1.Zero()) {
            Log.Info("pixExtractTextlines", "no fg pixels in input image");
            return null;
        }
        pixadb?.AddPix(pix1, L.L_COPY);

            /* Remove any very tall or very wide connected components */
        pix2 = pix1.SelectBySize(maxw, maxh, 8, L.L_SELECT_IF_BOTH,
                                 L.L_SELECT_IF_LT, out _);
        pixadb?.AddPix(pix2, L.L_COPY);

            /* Filter to solidify the text lines within the x-height region.
             * The closing (csize) bridges gaps between words.  The opening
             * removes isolated bridges between textlines. */
        int res;
        if ((res = pixs.XRes) == 0) {
            Log.Info("pixExtractTextlines", "Resolution is not set: setting to 300 ppi");
            res = 300;
        }
        int csize = (int)(120.0 < 60.0 * res / 300.0 ? 120.0 : 60.0 * res / 300.0);  /* L_MIN(120., 60.0 * res / 300.0) */
        string buf = $"c{csize}.1 + o{csize / 3}.1";
        pix3 = pix2.MorphCompSequence(buf, 0);
        pixadb?.AddPix(pix3, L.L_COPY);

            /* Extract the connected components.  These should be dilated lines */
        pix3.ConnComp(out Pixa pixa1, 4);
        if (pixadb != null) {
            pix1 = pixa1.DisplayRandomCmap(0, 0);
            pix1.Colormap!.ResetColor(0, 255, 255, 255);
            pixadb.AddPix(pix1, L.L_INSERT);
        }

            /* Set minw, minh if default is requested */
        minw = (minw != 0) ? minw : (int)(0.12 * res);
        minh = (minh != 0) ? minh : (int)(0.07 * res);

            /* Remove line components that are too small */
        Pixa pixa2 = pixa1.SelectBySize(minw, minh, L.L_SELECT_IF_BOTH,
                                        L.L_SELECT_IF_GTE, out _);
        if (pixadb != null) {
            pix1 = pixa2.DisplayRandomCmap(0, 0);
            pix1.Colormap!.ResetColor(0, 255, 255, 255);
            pixadb.AddPix(pix1, L.L_INSERT);
            pix1 = pix2.ConvertTo32();
            pix1.RenderBoxaArb(pixa2.GetBoxa(L.L_CLONE), 2, 255, 0, 0);
            pixadb.AddPix(pix1, L.L_INSERT);
        }

            /* Selectively AND with the version before dilation, and save */
        Boxa boxa2 = pixa2.GetBoxa(L.L_CLONE);
        Boxa boxa3 = boxa2.AdjustSides(-adjw, adjw, -adjh, adjh);
        Pixa pixa3 = pix2.ClipRectangles(boxa3);
        if (pixadb != null) {
            pix1 = pixa3.DisplayRandomCmap(0, 0);
            pix1.Colormap!.ResetColor(0, 255, 255, 255);
            pixadb.AddPix(pix1, L.L_INSERT);
        }

        return pixa3;
    }

    // leptonica: pixExtractRawTextlines (pageseg.c)
    /// <param name="maxw">initial filtering: remove any components in pixs with
    /// components larger than maxw or maxh; use 0 for default values.</param>
    /// <param name="maxh">initial filtering: see maxw</param>
    /// <param name="adjw">final adjustment of boxes representing each
    /// text line.  If > 0, these increase the box size at each edge by this amount.</param>
    /// <param name="adjh">final adjustment: see adjw</param>
    /// <param name="pixadb">pixa for saving intermediate steps; null to omit</param>
    /// <returns>pixa of textline images, including bounding boxes, or
    /// null if there are no fg pixels; pixs is any depth, assumed to have nearly horizontal text</returns>
    /// <remarks>
    /// (1) This function assumes that textlines have sufficient
    ///     vertical separation and small enough skew so that a
    ///     horizontal dilation sufficient to join words will not join
    ///     textlines.  It aggressively joins textlines across multiple
    ///     columns, so if that is not desired, you must either (a) make
    ///     sure that %pixs is a single column of text or (b) use instead
    ///     pixExtractTextlines(), which is more conservative
    ///     about joining text fragments that have vertical overlap.
    /// (2) This first removes components from pixs that are either
    ///     very wide (> %maxw) or very tall (> %maxh).
    /// (3) For reasonable accuracy, the resolution of pixs should be
    ///     at least 100 ppi.  For reasonable efficiency, the resolution
    ///     should not exceed 600 ppi.
    /// (4) This can be used to determine if some region of a scanned
    ///     image is horizontal text.
    /// (5) As an example, for a pix with resolution 300 ppi, a reasonable
    ///     set of parameters is:
    ///        pixExtractRawTextlines(pix, 150, 150, 0, 0, NULL);
    /// (6) The output pixa is composed of subimages, one for each textline,
    ///     and the boxa in the pixa tells where in %pixs each textline goes.
    /// </remarks>
    public Pixa? ExtractRawTextlines(int maxw, int maxh, int adjw, int adjh, Pixa? pixadb)
    {
        Pix pixs = this;
        Pix pix1, pix2, pix3;

            /* Set maxw, maxh if default is requested */
        int res;
        if ((res = pixs.XRes) == 0) {
            Log.Info("pixExtractRawTextlines", "Resolution is not set: setting to 300 ppi");
            res = 300;
        }
        maxw = (maxw != 0) ? maxw : (int)(0.5 * res);
        maxh = (maxh != 0) ? maxh : (int)(0.5 * res);

            /* Binarize carefully, if necessary */
        if (pixs.Depth > 1) {
            pix2 = pixs.ConvertTo8(false);
            pix3 = pix2.CleanBackgroundToWhite(null, null, 1.0f, 70, 190);
            pix1 = pix3.ThresholdToBinary(150);
        } else {
            pix1 = Clone(pixs);
        }
        if (pix1.Zero()) {
            Log.Info("pixExtractRawTextlines", "no fg pixels in input image");
            return null;
        }
        pixadb?.AddPix(pix1, L.L_COPY);

            /* Remove any very tall or very wide connected components */
        pix2 = pix1.SelectBySize(maxw, maxh, 8, L.L_SELECT_IF_BOTH,
                                 L.L_SELECT_IF_LT, out _);
        pixadb?.AddPix(pix2, L.L_COPY);

            /* Filter to solidify the text lines within the x-height region.
             * The closing (csize) bridges gaps between words. */
        int csize = (int)(120.0 < 60.0 * res / 300.0 ? 120.0 : 60.0 * res / 300.0);  /* L_MIN(120., 60.0 * res / 300.0) */
        string buf = $"c{csize}.1";
        pix3 = pix2.MorphCompSequence(buf, 0);
        pixadb?.AddPix(pix3, L.L_COPY);

            /* Extract the connected components.  These should be dilated lines */
        Boxa boxa1 = pix3.ConnComp(out Pixa pixa1, 4);
        if (pixadb != null) {
            pix1 = pixa1.DisplayRandomCmap(0, 0);
            pix1.Colormap!.ResetColor(0, 255, 255, 255);
            pixadb.AddPix(pix1, L.L_INSERT);
        }

            /* Do a 2-d sort, and generate a bounding box for each set of text
             * line segments that is aligned horizontally (i.e., has vertical
             * overlap) into a box representing a single text line. */
        Boxaa baa1 = boxa1.Sort2d(out _, -1, -1, 5);
        baa1.GetExtent(out _, out _, out _, out Boxa boxa2);
        if (pixadb != null) {
            pix1 = pix2.ConvertTo32();
            pix1.RenderBoxaArb(boxa2, 2, 255, 0, 0);
            pixadb.AddPix(pix1, L.L_INSERT);
        }

            /* Optionally adjust the sides of each text line box, and then
             * use the boxes to generate a pixa of the text lines. */
        Boxa boxa3 = boxa2.AdjustSides(-adjw, adjw, -adjh, adjh);
        Pixa pixa2 = pix2.ClipRectangles(boxa3);
        if (pixadb != null) {
            pix1 = pixa2.DisplayRandomCmap(0, 0);
            pix1.Colormap!.ResetColor(0, 255, 255, 255);
            pixadb.AddPix(pix1, L.L_INSERT);
        }

        return pixa2;
    }

    /*------------------------------------------------------------------*
     *                      How many text columns                       *
     *------------------------------------------------------------------*/
    // leptonica: pixCountTextColumns (pageseg.c)
    /// <param name="deltafract">fraction of (max - min) to be used in the delta
    /// for extrema finding; typ 0.3</param>
    /// <param name="peakfract">fraction of (max - min) to be used to threshold
    /// the peak value; typ. 0.5</param>
    /// <param name="clipfract">fraction of image dimension removed on each side;
    /// typ. 0.1, which leaves w and h reduced by 0.8</param>
    /// <param name="pixadb">[optional] pre-allocated, for showing
    /// intermediate computation; use null to skip</param>
    /// <returns>number of columns; -1 if not determined; pixs is 1 bpp</returns>
    /// <remarks>
    /// (1) It is assumed that pixs has the correct resolution set.
    ///     If the resolution is 0, we set to 300 and issue a warning.
    /// (2) If necessary, the image is scaled to between 37 and 75 ppi;
    ///     most of the processing is done at this resolution.
    /// (3) If no text is found (essentially a blank page),
    ///     this returns ncols = 0.
    /// (4) For debug output, input a pre-allocated pixa.
    /// </remarks>
    public int CountTextColumns(float deltafract, float peakfract, float clipfract, Pixa? pixadb)
    {
        const string func = "pixCountTextColumns";
        Pix pixs = this;
        Pix pix1;

        int ncols = -1;  /* init */
        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not defined or not 1 bpp");
        if (deltafract < 0.15 || deltafract > 0.75)
            Log.Warning(func, "deltafract not in [0.15 ... 0.75]");
        if (peakfract < 0.25 || peakfract > 0.9)
            Log.Warning(func, "peakfract not in [0.25 ... 0.9]");
        if (clipfract < 0.0 || clipfract >= 0.5)
            throw Lept.Arg(func, "clipfract not in [0.0 ... 0.5)\n");
        pixadb?.AddPix(pixs, L.L_COPY);

            /* Scale to between 37.5 and 75 ppi */
        int res;
        if ((res = pixs.XRes) == 0) {
            Log.Warning(func, "resolution undefined; set to 300");
            pixs.SetResolution(300, 300);
            res = 300;
        }
        if (res < 37) {
            Log.Warning(func, $"resolution {res} very low");
            float scalefact = (float)(37.5 / res);
            pix1 = pixs.Scale(scalefact, scalefact);
        } else {
            float redfact = (float)((float)res / 37.5);
            if (redfact < 2.0)
                pix1 = Clone(pixs);
            else if (redfact < 4.0)
                pix1 = pixs.ReduceRankBinaryCascade(1, 0, 0, 0);
            else if (redfact < 8.0)
                pix1 = pixs.ReduceRankBinaryCascade(1, 2, 0, 0);
            else if (redfact < 16.0)
                pix1 = pixs.ReduceRankBinaryCascade(1, 2, 2, 0);
            else
                pix1 = pixs.ReduceRankBinaryCascade(1, 2, 2, 2);
        }
        pixadb?.AddPix(pix1, L.L_COPY);

            /* Crop inner 80% of image */
        pix1.GetDimensions(out int w, out int h, out _);
        Box box = Box.Create((int)(clipfract * w), (int)(clipfract * h),
                             (int)((1.0 - 2 * clipfract) * w), (int)((1.0 - 2 * clipfract) * h));
        Pix pix2 = pix1.ClipRectangle(box, out _)
                   ?? throw Lept.Fail(func, "clipped pix not made");
        pix2.GetDimensions(out w, out h, out _);
        pixadb?.AddPix(pix2, L.L_COPY);

            /* Deskew */
        Pix pix3 = pix2.Deskew(0);
        pixadb?.AddPix(pix3, L.L_COPY);

            /* Close to increase column counts for text */
        Pix pix4 = CloseSafeBrick(null, pix3, 5, 21);
        pixadb?.AddPix(pix4, L.L_COPY);
        Invert(pix4, pix4);
        Numa na1 = pix4.CountByColumn(null);

        if (pixadb != null) {
            Gplot.Simple1(na1, L.GPLOT_PNG, "/tmp/lept/plot", null);
            if (File.Exists(Utils.GenPathname("/tmp/lept/plot.png", null))) {  /* the plot needs gnuplot */
                Pix pix5 = Read("/tmp/lept/plot.png");
                pixadb.AddPix(pix5, L.L_INSERT);
            }
        }

            /* Analyze the column counts.  na4 gives the locations of
             * the extrema in normalized units (0.0 to 1.0) across the
             * cropped image.  na5 gives the magnitude of the
             * extrema, normalized to the dynamic range.  The peaks
             * are values that are at least peakfract of (max - min). */
        na1.GetMax(out float maxval, out _);
        na1.GetMin(out float minval, out _);
        float fract = (float)(maxval - minval) / h;  /* is there much at all? */
        if (fract < 0.05) {
            Log.Info(func, "very little content on page; 0 text columns");
            ncols = 0;
        } else {
            Numa na2 = na1.FindExtrema(deltafract * (maxval - minval), out Numa na3);
            Numa na4 = na2.Transform(0, (float)(1.0 / w));
            Numa na5 = na3.Transform(-minval, (float)(1.0 / (maxval - minval)));
            int n = na4.Count;
            int npeak = 0;
            for (int i = 0; i < n; i++) {
                float val4 = na4.GetFValue(i);
                float val5 = na5.GetFValue(i);
                if (val4 > 0.3 && val4 < 0.7 && val5 >= peakfract) {
                    npeak++;
                    Log.Info(func, $"Peak(loc,val) = ({val4:F3},{val5:F3})");
                }
            }
            ncols = npeak + 1;
        }

        return ncols;
    }

    /*------------------------------------------------------------------*
     *                      Decision text vs photo                      *
     *------------------------------------------------------------------*/
    // leptonica: pixDecideIfText (pageseg.c)
    /// <summary>Decides whether an image is mostly text.</summary>
    /// <param name="box">[optional]  if null, use entire pixs</param>
    /// <param name="pixadb">[optional] pre-allocated, for showing intermediate
    /// computation; use null to skip</param>
    /// <returns>1 if text; 0 if photo; -1 if not determined or empty; pixs is any depth</returns>
    /// <remarks>
    /// (1) It is assumed that pixs has the correct resolution set.
    ///     If the resolution is 0, we set to 300 and issue a warning.
    /// (2) If necessary, the image is scaled to 300 ppi; most of the
    ///     processing is done at this resolution.
    /// (3) Text is assumed to be in horizontal lines.
    /// (4) Because thin vertical lines are removed before filtering for
    ///     text lines, this should identify tables as text.
    /// (5) If %box is null and pixs contains both text lines and line art,
    ///     this function might return %istext == true.
    /// (6) If the input pixs is empty, or for some other reason the
    ///     result can not be determined, return -1.
    /// (7) For debug output, input a pre-allocated pixa.
    /// (8) NOTE(upstream): if there are fewer than two connected components,
    ///     C reads %maxw uninitialized; here it is 0.
    /// </remarks>
    public int DecideIfText(Box? box, Pixa? pixadb)
    {
        Pix pixs = this;
        int h;
        int maxw = 0;
        L_Bmf? bmf = null;

        int istext = -1;

            /* Crop, convert to 1 bpp, 300 ppi */
        Pix pix1 = pixs.Prepare1bpp(box, 0.1f, 300)
                   ?? throw Lept.Fail("pixDecideIfText", "pix1 not made");

        if (pix1.Zero()) {
            Log.Info("pixDecideIfText", "pix is empty");
            return istext;
        }
        int w = pix1.Width;

            /* Identify and remove tall, thin vertical lines (as found in tables)
             * that are up to 9 pixels wide.  Make a hit-miss sel with an
             * 81 pixel vertical set of hits and with 3 pairs of misses that
             * are 10 pixels apart horizontally.  It is necessary to use a
             * hit-miss transform; if we only opened with a vertical line of
             * hits, we would remove solid regions of pixels that are not
             * text or vertical lines. */
        Pix pix2 = Create(11, 81, 1);
        for (int i = 0; i < 81; i++)
            pix2.SetPixel(5, i, 1);
        Sel sel1 = Sel.CreateFromPix(pix2, 40, 5, null);
        sel1.SetElement(20, 0, L.SEL_MISS);
        sel1.SetElement(20, 10, L.SEL_MISS);
        sel1.SetElement(40, 0, L.SEL_MISS);
        sel1.SetElement(40, 10, L.SEL_MISS);
        sel1.SetElement(60, 0, L.SEL_MISS);
        sel1.SetElement(60, 10, L.SEL_MISS);
        Pix pix3 = HMT(null, pix1, sel1);
        Pix pix4 = SeedfillBinaryRestricted(null, pix3, pix1, 8, 5, 1000);
        Pix pix5 = Xor(null, pix1, pix4);

            /* Convert the text lines to separate long horizontal components */
        Pix pix6 = pix5.MorphCompSequence("c30.1 + o15.1 + c60.1 + o2.2", 0);

            /* Estimate the distance to the bottom of the significant region */
        if (box != null) {  /* use full height */
            pix6.GetDimensions(out _, out h, out _);
        } else {  /* use height of region that has text lines */
            pix6.FindThreshFgExtent(400, out _, out h);
        }

        if (pixadb != null) {
            bmf = L_Bmf.Create(null, 6);
            pixadb.AddPixWithText(pix1, 1, bmf, "threshold/crop to binary",
                                  0x0000ff00, L.L_ADD_BELOW);
            pixadb.AddPixWithText(pix3, 2, bmf, "hit-miss for vertical line",
                                  0x0000ff00, L.L_ADD_BELOW);
            pixadb.AddPixWithText(pix4, 2, bmf, "restricted seed-fill",
                                  0x0000ff00, L.L_ADD_BELOW);
            pixadb.AddPixWithText(pix5, 2, bmf, "remove using xor",
                                  0x0000ff00, L.L_ADD_BELOW);
            pixadb.AddPixWithText(pix6, 2, bmf, "make long horiz components",
                                  0x0000ff00, L.L_ADD_BELOW);
        }

            /* Extract the connected components */
        Boxa boxa1;
        if (pixadb != null) {
            boxa1 = pix6.ConnComp(out Pixa pixa1, 8);
            Pix pix7 = pixa1.DisplayRandomCmap(0, 0);
            pix7.Colormap!.ResetColor(0, 255, 255, 255);
            pixadb.AddPixWithText(pix7, 2, bmf!, "show connected components",
                                  0x0000ff00, L.L_ADD_BELOW);
        } else {
            boxa1 = pix6.ConnComp(8);
        }

            /* Analyze the connected components.  The following conditions
             * at 300 ppi must be satisfied if the image is text:
             * (1) There are no components that are wider than 400 pixels and
             *     taller than 175 pixels.
             * (2) The second longest component is at least 60% of the
             *     (possibly cropped) image width.  This catches images
             *     that don't have any significant content.
             * (3) Of the components that are at least 40% of the length
             *     of the longest (n2), at least 80% of them must not exceed
             *     60 pixels in height.
             * (4) The number of those long, thin components (n3) must
             *     equal or exceed a minimum that scales linearly with the
             *     image height.
             * Most images that are not text fail more than one of these
             * conditions. */
        Boxa boxa2 = boxa1.Sort(L.L_SORT_BY_WIDTH, L.L_SORT_DECREASING, out _);
        if (boxa2.Count > 1)
            boxa2.GetBoxGeometry(1, out _, out _, out maxw, out _);  /* 2nd longest */
        Boxa boxa3 = boxa1.SelectBySize((int)(0.4 * maxw), 0, L.L_SELECT_WIDTH,
                                        L.L_SELECT_IF_GTE, out _);
        Boxa boxa4 = boxa3.SelectBySize(0, 60, L.L_SELECT_HEIGHT,
                                        L.L_SELECT_IF_LTE, out _);
        Boxa boxa5 = boxa1.SelectBySize(400, 175, L.L_SELECT_IF_BOTH,
                                        L.L_SELECT_IF_GT, out _);
        int big_comp = (boxa5.Count == 0) ? 0 : 1;
        int n1 = boxa1.Count;
        int n2 = boxa3.Count;
        int n3 = boxa4.Count;
        float ratio1 = (float)maxw / (float)w;
        float ratio2 = (float)n3 / (float)n2;
        int minlines = Math.Max(2, h / 125);
        if (big_comp != 0 || ratio1 < 0.6 || ratio2 < 0.8 || n3 < minlines)
            istext = 0;
        else
            istext = 1;
        if (pixadb != null) {
            string nt = (istext == 1) ? "This is text" : "This is not text";
            Log.Info("pixDecideIfText",
                     $"{nt}: \n  n1 = {n1}, n2 = {n2}, n3 = {n3}, minlines = {minlines}\n" +
                     $"  maxw = {maxw}, ratio1 = {ratio1:F2}, h = {h}, big_comp = {big_comp}");
        }

        return istext;
    }

    // leptonica: pixFindThreshFgExtent (pageseg.c)
    /// <param name="thresh">threshold number of pixels in row</param>
    /// <param name="top">location of top of region</param>
    /// <param name="bot">location of bottom of region</param>
    /// <remarks>pixs is 1 bpp.</remarks>
    public void FindThreshFgExtent(int thresh, out int top, out int bot)
    {
        Pix pixs = this;

        top = 0;
        bot = 0;
        if (pixs.Depth != 1)
            throw Lept.Arg("pixFindThreshFgExtent", "pixs not defined or not 1 bpp");

        Numa na = pixs.CountPixelsByRow(null);
        int n = na.Count;
        int[] array = na.GetIArray();
        for (int i = 0; i < n; i++) {
            if (array[i] >= thresh) {
                top = i;
                break;
            }
        }
        for (int i = n - 1; i >= 0; i--) {
            if (array[i] >= thresh) {
                bot = i;
                break;
            }
        }
    }

    /*------------------------------------------------------------------*
     *                     Decision: table vs text                      *
     *------------------------------------------------------------------*/
    // leptonica: pixDecideIfTable (pageseg.c)
    /// <param name="box">[optional] if null, use entire pixs</param>
    /// <param name="orient">L_PORTRAIT_MODE, L_LANDSCAPE_MODE</param>
    /// <param name="pixadb">[optional] pre-allocated, for showing intermediate
    /// computation; use null to skip</param>
    /// <returns>score 0 - 4; -1 if not determined; pixs is any depth, any resolution >= 75 ppi</returns>
    /// <remarks>
    /// (1) It is assumed that pixs has the correct resolution set.
    ///     If the resolution is 0, we assume it is 300 ppi and issue a warning.
    /// (2) If %orient == L_LANDSCAPE_MODE, the image is rotated 90 degrees
    ///     clockwise before being analyzed.
    /// (3) The interpretation of the returned score:
    ///       -1     undetermined
    ///        0     no table
    ///        1     unlikely to have a table
    ///        2     likely to have a table
    ///        3     even more likely to have a table
    ///        4     extremely likely to have a table
    ///     * Setting the condition for finding a table at score >= 2 works
    ///       well, except for false positives on kanji and landscape text.
    ///     * These false positives can be removed by setting the condition
    ///       at score >= 3, but recall is lowered because it will not find
    ///       tables without either horizontal or vertical lines.
    /// (4) Most of the processing takes place at 75 ppi.
    /// (5) Internally, three numbers are determined, for horizontal and
    ///     vertical fg lines, and for vertical bg lines.  From these,
    ///     four tests are made to decide if there is a table occupying
    ///     a significant part of the image.
    /// (6) Images have arbitrary content and would be likely to trigger
    ///     this detector, so they are checked for first, and if found,
    ///     return with a 0 (no table) score.
    /// (7) Musical scores (tablature) are likely to trigger the detector.
    /// (8) Tables of content with more than 2 columns are likely to
    ///     trigger the detector.
    /// (9) For debug output, input a pre-allocated pixa.
    /// </remarks>
    public int DecideIfTable(Box? box, int orient, Pixa? pixadb)
    {
        Pix pixs = this;
        Pix pix1, pix2, pix3, pix4, pix5, pix6, pix7, pix8, pix9;

        int score = -1;

            /* Check if there is an image region.  First convert to 1 bpp
             * at 175 ppi.  If an image is found, assume there is no table.  */
        pix1 = pixs.Prepare1bpp(box, 0.1f, 175)
               ?? throw Lept.Fail("pixDecideIfTable", "pix1 not made");
        pix2 = pix1.GenerateHalftoneMask(out bool htfound, null);
        if (htfound && pixadb != null) pixadb.AddPix(pix2, L.L_COPY);
        if (htfound) {
            Log.Info("pixDecideIfTable", "pix has an image region");
            return 0;
        }

            /* Crop, convert to 1 bpp, 75 ppi */
        pix1 = pixs.Prepare1bpp(box, 0.05f, 75)
               ?? throw Lept.Fail("pixDecideIfTable", "pix1 not made");

        if (pix1.Zero()) {
            Log.Info("pixDecideIfTable", "pix is empty");
            return 0;
        }

            /* The 2x2 dilation on 75 ppi makes these two approaches very similar:
             * (1) pix1 = pixPrepare1bpp(..., 300);  // 300 ppi resolution
             *     pix2 = pixReduceRankBinaryCascade(pix1, 1, 1, 0, 0);
             * (2) pix1 = pixPrepare1bpp(..., 75);  // 75 ppi resolution
             *     pix2 = pixDilateBrick(NULL, pix1, 2, 2);
             * But (2) is more efficient if the input image to pixPrepare1bpp()
             * is not at 300 ppi.   */
        pix2 = DilateBrick(null, pix1, 2, 2);

            /* Deskew both horizontally and vertically; rotate by 90
             * degrees if in landscape mode. */
        pix3 = pix2.DeskewBoth(1);
        if (pixadb != null) {
            pixadb.AddPix(pix2, L.L_COPY);
            pixadb.AddPix(pix3, L.L_COPY);
        }
        if (orient == L.L_LANDSCAPE_MODE)
            pix4 = pix3.Rotate90(1);
        else
            pix4 = Clone(pix3);
        pix1 = Clone(pix4);

            /* Look for horizontal and vertical lines */
        pix2 = pix1.MorphSequence("o100.1 + c1.4", 0);
        pix3 = SeedfillBinary(null, pix2, pix1, 8);
        pix4 = pix1.MorphSequence("o1.100 + c4.1", 0);
        pix5 = SeedfillBinary(null, pix4, pix1, 8);
        pix6 = Or(null, pix3, pix5);
        if (pixadb != null) {
            pixadb.AddPix(pix2, L.L_COPY);
            pixadb.AddPix(pix4, L.L_COPY);
            pixadb.AddPix(pix3, L.L_COPY);
            pixadb.AddPix(pix5, L.L_COPY);
            pixadb.AddPix(pix6, L.L_COPY);
        }
        int nhb = pix2.CountConnComp(8);  /* number of horizontal black lines */
        int nvb = pix4.CountConnComp(8);  /* number of vertical black lines */

            /* Remove the lines */
        Subtract(pix1, pix1, pix6);
        pixadb?.AddPix(pix1, L.L_COPY);

            /* Remove noise pixels */
        pix7 = pix1.MorphSequence("c4.1 + o8.1", 0);
        pixadb?.AddPix(pix7, L.L_COPY);

            /* Look for vertical white space.  Invert to convert white bg
             * to fg.  Use a single rank-1 2x reduction, which closes small
             * fg holes, for the final processing at 37.5 ppi.
             * The vertical opening is then about 3 inches on a 300 ppi image.
             * We also remove vertical whitespace that is less than 5 pixels
             * wide at this resolution (about 0.1 inches) */
        Invert(pix7, pix7);
        pix8 = pix7.MorphSequence("r1 + o1.100", 0);
        pix9 = pix8.SelectBySize(5, 0, 8, L.L_SELECT_WIDTH,
                                 L.L_SELECT_IF_GTE, out _);
        int nvw = pix9.CountConnComp(8);  /* number of vertical white lines */
        if (pixadb != null) {
            pixadb.AddPix(pix8.Scale(2.0f, 2.0f), L.L_INSERT);
            pixadb.AddPix(pix9.Scale(2.0f, 2.0f), L.L_INSERT);
        }

            /* Require at least 2 of the following 4 conditions for a table.
             * Some tables do not have black (fg) lines, and for those we
             * require more than 6 long vertical whitespace (bg) lines.  */
        score = 0;
        if (nhb > 1) score++;
        if (nvb > 2) score++;
        if (nvw > 3) score++;
        if (nvw > 6) score++;

        return score;
    }

    // leptonica: pixPrepare1bpp (pageseg.c)
    /// <param name="box">[optional] if null, use entire pixs</param>
    /// <param name="cropfract">fraction to be removed from the boundary;
    /// use 0.0 to retain the entire image</param>
    /// <param name="outres">desired resolution of output image; if the
    /// input image resolution is not set, assume 300 ppi; use 0 to skip scaling.</param>
    /// <returns>pixd; pixs is any depth</returns>
    /// <remarks>
    /// (1) This handles some common pre-processing operations,
    ///     where the page segmentation algorithm takes a 1 bpp image.
    /// </remarks>
    public Pix? Prepare1bpp(Box? box, float cropfract, int outres)
    {
        Pix pixs = this;
        Pix? pix1;
        Pix pix4;

            /* Crop the image.  If no box is given, use %cropfract to remove
             * pixels near the image boundary; this helps avoid false
             * negatives from noise that is often found there. */
        if (box != null) {
            pix1 = pixs.ClipRectangle(box, out _);
        } else {
            pixs.GetDimensions(out int w, out int h, out _);
            Box box1 = Box.Create((int)(cropfract * w), (int)(cropfract * h),
                                  (int)((1.0 - 2 * cropfract) * w),
                                  (int)((1.0 - 2 * cropfract) * h));
            pix1 = pixs.ClipRectangle(box1, out _);
        }
        if (pix1 == null)
            throw Lept.Fail("pixClipRectangle", "pix not clipped");

            /* Convert to 1 bpp with adaptive background cleaning */
        if (pixs.Depth > 1) {
            Pix pix2 = pix1.ConvertTo8(false);
            Pix pix3 = pix2.CleanBackgroundToWhite(null, null, 1.0f, 70, 160);
            pix4 = pix3.ThresholdToBinary(200);
        } else {
            pix4 = Clone(pix1);
        }

            /* Scale the image to the requested output resolution;
               do not scale if %outres <= 0 */
        if (outres <= 0)
            return pix4;
        int res;
        if ((res = pixs.XRes) == 0) {
            Log.Warning("pixPrepare1bpp", "Resolution is not set: using 300 ppi");
            res = 300;
        }
        Pix pix5;
        if (res != outres) {
            float factor = (float)outres / (float)res;
            pix5 = pix4.Scale(factor, factor);
        } else {
            pix5 = Clone(pix4);
        }
        return pix5;
    }

    /*------------------------------------------------------------------*
     *               Estimate the grayscale background value            *
     *------------------------------------------------------------------*/
    // leptonica: pixEstimateBackground (pageseg.c)
    /// <param name="darkthresh">pixels below this value are never considered
    /// part of the background; typ. 70; use 0 to skip</param>
    /// <param name="edgecrop">fraction of half-width on each side, and of
    /// half-height at top and bottom, that are cropped</param>
    /// <returns>estimated background; pixs is 8 bpp, with or without colormap</returns>
    /// <remarks>
    /// (1) Caller should check that return bg value is > 0.
    /// </remarks>
    public int EstimateBackground(int darkthresh, float edgecrop)
    {
        const string func = "pixEstimateBackground";
        Pix pixs = this;
        Pix pix2;

        if (pixs.Depth != 8)
            throw Lept.Arg(func, "pixs not defined or not 8 bpp");
        if (darkthresh > 128)
            Log.Warning(func, "darkthresh unusually large");
        if (edgecrop < 0.0 || edgecrop >= 1.0)
            throw Lept.Arg(func, "edgecrop not in [0.0 ... 1.0)");

        Pix pix1 = pixs.RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        pix1.GetDimensions(out int w, out int h, out _);

            /* Optionally crop inner part of image */
        if (edgecrop > 0.0) {
            Box box = Box.Create((int)(0.5 * edgecrop * w), (int)(0.5 * edgecrop * h),
                                 (int)((1.0 - edgecrop) * w), (int)((1.0 - edgecrop) * h));
            pix2 = pix1.ClipRectangle(box, out _)
                   ?? throw Lept.Fail(func, "clipped pix not made");
        } else {
            pix2 = Clone(pix1);
        }

            /* We will use no more than 50K samples */
        int sampling = Math.Max(1, (int)Math.Sqrt((double)(w * h) / 50000.0 + 0.5));

            /* Optionally make a mask over all pixels lighter than %darkthresh */
        Pix? pixm = null;
        if (darkthresh > 0) {
            pixm = pix2.ThresholdToBinary(darkthresh);
            Invert(pixm, pixm);
        }

        pix2.GetRankValueMasked(pixm, 0, 0, sampling, 0.5f, out float fbg, out _);
        return (int)(fbg + 0.5);
    }

    /*---------------------------------------------------------------------*
     *             Largest white or black rectangles in an image           *
     *---------------------------------------------------------------------*/
    // leptonica: pixFindLargeRectangles (pageseg.c)
    /// <param name="polarity">0 within background, 1 within foreground</param>
    /// <param name="nrect">number of rectangles to be found</param>
    /// <param name="boxa">largest rectangles, sorted by decreasing area</param>
    /// <param name="pixdb">output with rectangles drawn on it</param>
    /// <remarks>
    /// (1) This does a greedy search to find the largest rectangles,
    ///     either black or white and without overlaps, in %pix.
    /// (2) See pixFindLargestRectangle(), which is called multiple
    ///     times, for details.  On each call, the largest rectangle
    ///     found is painted, so that none of its pixels can be
    ///     used later, before calling it again.
    /// (3) This function is surprisingly fast.  Although
    ///     pixFindLargestRectangle() runs at about 50 MPix/sec, when it
    ///     is run multiple times by pixFindLargeRectangles(), it processes
    ///     at 150 - 250 MPix/sec, and the time is approximately linear
    ///     in %nrect.  For example, for a 1 MPix image, searching for
    ///     the largest 50 boxes takes about 0.2 seconds.
    /// (4) pixs is 1 bpp.
    /// </remarks>
    public void FindLargeRectangles(int polarity, int nrect, out Boxa boxa, out Pix pixdb)
    {
        FindLargeRectanglesImpl(polarity, nrect, out boxa);
        pixdb = DrawBoxaRandom(boxa, 3);
    }

    /// <summary>Like <see cref="FindLargeRectangles(int,int,out Boxa,out Pix)"/>, without the debug image.</summary>
    public void FindLargeRectangles(int polarity, int nrect, out Boxa boxa)
    {
        FindLargeRectanglesImpl(polarity, nrect, out boxa);
    }

    private void FindLargeRectanglesImpl(int polarity, int nrect, out Boxa boxa)
    {
        const string func = "pixFindLargeRectangles";
        Pix pixs = this;

        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not defined or not 1 bpp");
        if (polarity != 0 && polarity != 1)
            throw Lept.Arg(func, "invalid polarity");
        if (nrect > 1000) {
            Log.Warning(func, $"large num rectangles = {nrect} requested; using 1000");
            nrect = 1000;
        }

        Pix pix = Copy(null, pixs);
        boxa = Boxa.Create(nrect);

            /* Sequentially find largest rectangle and fill with opposite color */
        for (int i = 0; i < nrect; i++) {
            FindLargestRectangleImpl(pix, polarity, out Box box);
            boxa.AddBox(box, L.L_INSERT);
            int op = (polarity == 0) ? L.PIX_SET : L.PIX_CLR;
            box.GetGeometry(out int bx, out int by, out int bw, out int bh);
            pix.Rasterop(bx, by, bw, bh, op, null, 0, 0);
        }
    }

    // leptonica: pixFindLargestRectangle (pageseg.c)
    /// <param name="polarity">0 within background, 1 within foreground</param>
    /// <param name="box">largest area rectangle</param>
    /// <param name="pixdb">output with rectangle drawn on it</param>
    /// <remarks>
    /// (1) This is a simple and elegant solution to a problem in
    ///     computational geometry that at first appears to be quite
    ///     difficult: what is the largest rectangle that can be
    ///     placed in the image, covering only pixels of one polarity
    ///     (bg or fg)?  The solution is O(n), where n is the number
    ///     of pixels in the image, and it requires nothing more than
    ///     using a simple recursion relation in a single sweep of the image.
    /// (2) In a sweep from UL to LR with left-to-right being the fast
    ///     direction, calculate the largest white rectangle at (x, y),
    ///     using previously calculated values at pixels #1 and #2:
    ///        #1:    (x, y - 1)
    ///        #2:    (x - 1, y)
    ///     We also need the most recent "black" pixels that were seen
    ///     in the current row and column.
    ///     Consider the largest area.  There are only two possibilities:
    ///        (a)  Min(w(1), horizdist) * (h(1) + 1)
    ///        (b)  Min(h(2), vertdist) * (w(2) + 1)
    ///     where
    ///        horizdist: the distance from the rightmost "black" pixel seen
    ///                   in the current row across to the current pixel
    ///        vertdist: the distance from the lowest "black" pixel seen
    ///                  in the current column down to the current pixel
    ///     and we choose the Max of (a) and (b).
    /// (3) To convince yourself that these recursion relations are correct,
    ///     it helps to draw the maximum rectangles at #1 and #2.
    ///     Then for #1, you try to extend the rectangle down one line,
    ///     so that the height is h(1) + 1.  Do you get the full
    ///     width of #1, w(1)?  It depends on where the black pixels are
    ///     in the current row.  You know the final width is bounded by w(1)
    ///     and w(2) + 1, but the actual value depends on the distribution
    ///     of black pixels in the current row that are at a distance
    ///     from the current pixel that is between these limits.
    ///     We call that value "horizdist", and the area is then given
    ///     by the expression (a) above.  Using similar reasoning for #2,
    ///     where you attempt to extend the rectangle to the right
    ///     by 1 pixel, you arrive at (b).  The largest rectangle is
    ///     then found by taking the Max.
    /// (4) pixs is 1 bpp.
    /// </remarks>
    public void FindLargestRectangle(int polarity, out Box box, out Pix pixdb)
    {
        FindLargestRectangleImpl(this, polarity, out box);
        pixdb = ConvertTo8(true);
        pixdb.RenderHashBoxArb(box, 6, 2, L.L_NEG_SLOPE_LINE, 1, 255, 0, 0);
    }

    /// <summary>Like <see cref="FindLargestRectangle(int,out Box,out Pix)"/>, without the debug image.</summary>
    public void FindLargestRectangle(int polarity, out Box box)
    {
        FindLargestRectangleImpl(this, polarity, out box);
    }

    private static void FindLargestRectangleImpl(Pix pixs, int polarity, out Box pbox)
    {
        const string func = "pixFindLargestRectangle";
        int i, j, wp, hp, w1, w2, h1, h2, wmin, hmin, area1, area2, horizdist, vertdist;

        pixs.GetDimensions(out int w, out int h, out int d);
        if (d != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (polarity != 0 && polarity != 1)
            throw Lept.Arg(func, "invalid polarity");

            /* Initialize lowest "fg" seen so far for each column */
        int[] lowestfg = new int[w];
        for (i = 0; i < w; i++)
            lowestfg[i] = -1;

            /* The combination (val ^ polarity) is the color for which we
             * are searching for the maximum rectangle.  For polarity == 0,
             * we search in the bg (white). */
        Pix pixw = Create(w, h, 32);  /* stores width */
        Pix pixh = Create(w, h, 32);  /* stores height */
        uint[] linew = pixw.Data;  /* keeps the width and height for the largest */
        uint[] lineh = pixh.Data;  /* rectangles whose LR corner is located there. */
        int wplw = pixw.Wpl;
        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        int maxarea = 0, xmax = 0, ymax = 0, wmax = 0, hmax = 0;
        for (i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls);
            int prevfg = -1;
            for (j = 0; j < w; j++) {
                int val = ArrayAccess.GetDataBit(lines, j);
                if ((val ^ polarity) == 0) {  /* bg (0) if polarity == 0, etc. */
                    if (i == 0 && j == 0) {
                        wp = hp = 1;
                    } else if (i == 0) {
                        wp = (int)linew[i * wplw + j - 1] + 1;
                        hp = 1;
                    } else if (j == 0) {
                        wp = 1;
                        hp = (int)lineh[(i - 1) * wplw + j] + 1;
                    } else {
                            /* Expand #1 prev rectangle down */
                        w1 = (int)linew[(i - 1) * wplw + j];
                        h1 = (int)lineh[(i - 1) * wplw + j];
                        horizdist = j - prevfg;
                        wmin = (w1 < horizdist) ? w1 : horizdist;  /* width of new rectangle */
                        area1 = wmin * (h1 + 1);

                            /* Expand #2 prev rectangle to right */
                        w2 = (int)linew[i * wplw + j - 1];
                        h2 = (int)lineh[i * wplw + j - 1];
                        vertdist = i - lowestfg[j];
                        hmin = (h2 < vertdist) ? h2 : vertdist;  /* height of new rectangle */
                        area2 = hmin * (w2 + 1);

                        if (area1 > area2) {
                            wp = wmin;
                            hp = h1 + 1;
                        } else {
                            wp = w2 + 1;
                            hp = hmin;
                        }
                    }
                } else {  /* fg (1) if polarity == 0; bg (0) if polarity == 1 */
                    prevfg = j;
                    lowestfg[j] = i;
                    wp = hp = 0;
                }
                linew[i * wplw + j] = (uint)wp;
                lineh[i * wplw + j] = (uint)hp;
                if (wp * hp > maxarea) {
                    maxarea = wp * hp;
                    xmax = j;
                    ymax = i;
                    wmax = wp;
                    hmax = hp;
                }
            }
        }

            /* Translate from LR corner to Box coords (UL corner, w, h) */
        pbox = Box.Create(xmax - wmax + 1, ymax - hmax + 1, wmax, hmax);
    }

    /*---------------------------------------------------------------------*
     *            Generate rectangle inside connected component            *
     *---------------------------------------------------------------------*/
    // leptonica: pixFindRectangleInCC (pageseg.c)
    /// <param name="boxs">[optional] if null, %pixs should be a minimum
    /// container of a single c.c.</param>
    /// <param name="fract">first and all consecutive lines found must be at
    /// least this fraction of the fast scan dimension</param>
    /// <param name="dir">L_SCAN_HORIZONTAL, L_SCAN_VERTICAL; direction of fast scan</param>
    /// <param name="select">L_GEOMETRIC_UNION, L_GEOMETRIC_INTERSECTION,
    /// L_LARGEST_AREA, L_SMALLEST_AREA</param>
    /// <param name="debug">if 1, generates output pdf showing intermediate
    /// computation and final result</param>
    /// <returns>box of included rectangle, or null if there is no run of sufficient size;
    /// pixs is 1 bpp, with sufficient closings to make the fg be a single c.c. that is a convex hull</returns>
    /// <remarks>
    /// (1) Computation is similar to pixFindLargestRectangle(), but allows
    ///     a different set of results to choose from.
    /// (2) Select the fast scan direction.  Then, scanning in the slow
    ///     direction, find the longest run of ON pixels in the fast
    ///     scan direction and look for the first run that is longer
    ///     than %fract of the dimension.  Continue until a shorter run
    ///     is found.  This generates a box of ON pixels fitting into the c.c.
    /// (3) Do this from both slow scan directions and use %select to get
    ///     a resulting box from these two.
    /// (4) The extracted rectangle is not necessarily the largest that
    ///     can fit in the c.c.  To get that, use pixFindLargestRectangle().
    /// </remarks>
    public Box? FindRectangleInCC(Box? boxs, float fract, int dir, int select, int debug)
    {
        const string func = "pixFindRectangleInCC";
        Pix pixs = this;
        Pix pix1, pix2;
        Pix? pixdb1;
        Box box3;
        int w1, h1, w2, h2;
        int xfirst = 0, xlast = 0, yfirst = 0, ylast = 0;

        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (fract <= 0.0 || fract > 1.0)
            throw Lept.Arg(func, "invalid fraction");
        if (dir != L.L_SCAN_VERTICAL && dir != L.L_SCAN_HORIZONTAL)
            throw Lept.Arg(func, "invalid scan direction");
        if (select != L.L_GEOMETRIC_UNION && select != L.L_GEOMETRIC_INTERSECTION &&
            select != L.L_LARGEST_AREA && select != L.L_SMALLEST_AREA)
            throw Lept.Arg(func, "invalid select");

            /* Extract the c.c. if necessary */
        int x = 0, y = 0;
        if (boxs != null) {
            pix1 = pixs.ClipRectangle(boxs, out _)
                   ?? throw Lept.Fail(func, "c.c. not clipped");
            boxs.GetGeometry(out x, out y, out _, out _);
        } else {
            pix1 = Clone(pixs);
        }

            /* All fast scans are horizontal; rotate 90 deg cw if necessary */
        if (dir == L.L_SCAN_VERTICAL)
            pix2 = pix1.Rotate90(1);
        else  /* L_SCAN_HORIZONTAL */
            pix2 = Clone(pix1);
        pix2.GetDimensions(out int w, out int h, out _);

        Pixa? pixadb = (debug != 0) ? Pixa.Create(0) : null;
        pixdb1 = null;
        if (pixadb != null) {
            Utils.LeptMkdir("lept/rect");
            pixadb.AddPix(pix1, L.L_CLONE);
            pixdb1 = pix2.ConvertTo32();
        }

            /* Scanning down, find the first scanline with a long enough run.
             * That run goes from (xfirst, yfirst) to (xlast, yfirst).  */
        bool found = false;
        int xstart, length;
        for (int i = 0; i < h; i++) {
            pix2.FindMaxHorizontalRunOnLine(i, out xstart, out length);
            if (length >= (int)(fract * w + 0.5)) {
                yfirst = i;
                xfirst = xstart;
                xlast = xfirst + length - 1;
                found = true;
                break;
            }
        }
        if (!found) {
            Log.Warning(func, "no run of sufficient size was found");
            return null;
        }

             /* Continue down until the condition fails */
        w1 = xlast - xfirst + 1;
        h1 = h - yfirst;  /* init */
        ylast = h - 1;  /* init */
        for (int i = yfirst + 1; i < h; i++) {
            pix2.FindMaxHorizontalRunOnLine(i, out xstart, out length);
            if (xstart > xfirst || (xstart + length - 1 < xlast) ||
                i == h - 1) {
                ylast = i - 1;
                h1 = ylast - yfirst + 1;
                break;
            }
        }
        Box box1 = Box.Create(xfirst, yfirst, w1, h1);

            /* Scanning up, find the first scanline with a long enough run.
             * That run goes from (xfirst, ylast) to (xlast, ylast).  */
        for (int i = h - 1; i >= 0; i--) {
            pix2.FindMaxHorizontalRunOnLine(i, out xstart, out length);
            if (length >= (int)(fract * w + 0.5)) {
                ylast = i;
                xfirst = xstart;
                xlast = xfirst + length - 1;
                break;
            }
        }

             /* Continue up until the condition fails */
        w2 = xlast - xfirst + 1;
        h2 = ylast + 1;  /* initialize */
        for (int i = ylast - 1; i >= 0; i--) {
            pix2.FindMaxHorizontalRunOnLine(i, out xstart, out length);
            if (xstart > xfirst || (xstart + length - 1 < xlast) ||
                i == 0) {
                yfirst = i + 1;
                h2 = ylast - yfirst + 1;
                break;
            }
        }
        Box box2 = Box.Create(xfirst, yfirst, w2, h2);

        if (pixadb != null) {
            pixdb1!.RenderBoxArb(box1, 2, 255, 0, 0);
            pixdb1.RenderBoxArb(box2, 2, 0, 255, 0);
            pixadb.AddPix(pixdb1, L.L_INSERT);
        }

            /* Select the final result from the two boxes */
        Box? box3n;
        if (select == L.L_GEOMETRIC_UNION)
            box3n = box1.BoundingRegion(box2);
        else if (select == L.L_GEOMETRIC_INTERSECTION)
            box3n = box1.OverlapRegion(box2);
        else if (select == L.L_LARGEST_AREA)
            box3n = (w1 * h1 >= w2 * h2) ? box1.Copy() : box2.Copy();
        else  /* select == L_SMALLEST_AREA) */
            box3n = (w1 * h1 <= w2 * h2) ? box1.Copy() : box2.Copy();

            /* Rotate the box 90 degrees ccw if necessary */
        Box? box4 = null;
        if (box3n != null) {
            box3 = box3n;
            if (dir == L.L_SCAN_VERTICAL)
                box4 = box3.RotateOrth(w, h, 3);
            else
                box4 = box3.Copy();
        }

            /* Transform back to global coordinates if %boxs exists */
        Box? box5 = (box4 != null) ? box4.Transform(x, y, 1.0f, 1.0f) : null;

            /* Debug output */
        if (pixadb != null) {
            pixdb1 = pixs.ConvertTo8(false);
            pixdb1.AddConstantGray(190);
            Pix pixdb2 = pixdb1.ConvertTo32();
            if (box5 != null) pixdb2.RenderBoxArb(box5, 4, 0, 0, 255);
            pixadb.AddPix(pixdb2, L.L_INSERT);
            int res = pixs.XRes;
            Log.Info(func, "Writing debug files to /tmp/lept/rect/");
            pixadb.ConvertToPdf(res, 1.0f, L.L_DEFAULT_ENCODE, 75, null,
                                "/tmp/lept/rect/fitrect.pdf");
            pix1 = pixadb.DisplayTiledAndScaled(32, 800, 1, 0, 40, 2);
            Write("/tmp/lept/rect/fitrect.png", pix1, L.IFF_PNG);
        }

        return box5;
    }

    /*------------------------------------------------------------------*
     *                    Automatic photoinvert for OCR                 *
     *------------------------------------------------------------------*/
    // leptonica: pixAutoPhotoinvert (pageseg.c)
    /// <param name="thresh">binarization threshold; use 0 for default</param>
    /// <param name="pixm">image regions to be inverted; null if there are none</param>
    /// <param name="pixadb">[optional] debug; input null to skip</param>
    /// <returns>pixd   1 bpp image to be sent to OCR; pixs is any depth, colormap ok</returns>
    /// <remarks>
    /// (1) A 1 bpp image is returned, where pixels in image regions are
    ///     photo-inverted.
    /// (2) If there is light text with a dark background, this will
    ///     identify the region and photoinvert the pixels there if
    ///     there are at least 60% fg pixels in the region.
    /// (3) For debug output, input a (typically empty) %pixadb.
    /// </remarks>
    public Pix AutoPhotoinvert(int thresh, out Pix? pixm, Pixa? pixadb)
    {
        Pix pixs = this;

        pixm = null;
        if (thresh == 0) thresh = 128;

        Pix pix1 = pixs.ConvertTo1(thresh);
        pixadb?.AddPix(pix1, L.L_COPY);

            /* Identify regions for photo-inversion:
             * (1) Start with the halftone mask.
             * (2) Eliminate ordinary text and halftones in the mask.
             * (3) Some regions of inverted text may have been removed in
             *     steps (1) and (2).  Conditionally fill holes in the mask,
             *     but do not fill out to the bounding rect. */
        Pix pix2 = pix1.GenerateHalftoneMask(out _, pixadb);
        Pix pix3 = pix2.MorphSequence("o15.15 + c25.25", 0);  /* remove noise */
        Pix pix4 = pix3.FillHolesToBoundingRect(1, 0.5f, 1.0f);
        if (pixadb != null) {
            pixadb.AddPix(pix2, L.L_CLONE);
            pixadb.AddPix(pix3, L.L_CLONE);
            pixadb.AddPix(pix4, L.L_COPY);
        }
        if (pix4.Zero())
            return pix1;

            /* Examine each component and validate the inversion.
             * Require at least 60% of pixels under each component to be FG. */
        Boxa boxa1 = pix4.ConnCompBB(8);
        int n = boxa1.Count;
        for (int i = 0; i < n; i++) {
            Box box1 = boxa1.GetBox(i, L.L_COPY);
            Pix pix5a = pix1.ClipRectangle(box1, out _)
                        ?? throw Lept.Fail("pixAutoPhotoinvert", "pix5 not clipped");
            float fgfract = pix5a.ForegroundFraction();
            box1.GetGeometry(out int x, out int y, out int w, out int h);
            if (fgfract < 0.6)  /* erase from the mask */
                pix4.Rasterop(x, y, w, h, L.PIX_CLR, null, 0, 0);
        }
        if (pix4.Zero())
            return pix1;

            /* Combine pixels of the photo-inverted pix with the binarized input */
        Pix pix5 = Invert(null, pix1);
        pix1.CombineMasked(pix5, pix4);

        if (pixadb != null) {
            pixadb.AddPix(pix5, L.L_CLONE);
            pixadb.AddPix(pix1, L.L_COPY);
        }
        pixm = pix4;
        return pix1;
    }
}
