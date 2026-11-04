using System;

namespace Leptonica;

/*
 *  flipdetect.c: page orientation and mirror detection.
 *
 *      High-level interface for detection and correction
 *          Pix.OrientCorrect (pixOrientCorrect)
 *
 *      Page orientation detection (pure rotation by 90 degree increments):
 *          Pix.OrientDetect (pixOrientDetect)
 *          Pix.MakeOrientDecision (makeOrientDecision)
 *          Pix.UpDownDetect (pixUpDownDetect)
 *
 *      Page mirror detection (flip 180 degrees about line in plane of image):
 *          Pix.MirrorDetect (pixMirrorDetect)
 *
 *      Static debug helper
 *          DebugFlipDetect (pixDebugFlipDetect)
 *
 *  ===================================================================
 *
 *  Page transformation detection:
 *
 *  Once a page is deskewed, there are 8 possible states that it
 *  can be in, shown symbolically below.  Suppose state 0 is correct.
 *
 *      0: correct     1          2          3
 *      +------+   +------+   +------+   +------+
 *      | **** |   | *    |   | **** |   |    * |
 *      | *    |   | *    |   |    * |   |    * |
 *      | *    |   | **** |   |    * |   | **** |
 *      +------+   +------+   +------+   +------+
 *
 *         4          5          6          7
 *      +-----+    +-----+    +-----+    +-----+
 *      | *** |    |   * |    | *** |    | *   |
 *      |   * |    |   * |    | *   |    | *   |
 *      |   * |    |   * |    | *   |    | *   |
 *      |   * |    | *** |    | *   |    | *** |
 *      +-----+    +-----+    +-----+    +-----+
 *
 *  Each of the other seven can be derived from state 0 by applying some
 *  combination of a 90 degree clockwise rotation, a flip about
 *  a horizontal line, and a flip about a vertical line,
 *  all abbreviated as:
 *      R = Rotation (about a line perpendicular to the image)
 *      H = Horizontal flip (about a vertical line in the plane of the image)
 *      V = Vertical flip (about a horizontal line in the plane of the image)
 *
 *  We get these transformations:
 *      RHV
 *      000  -> 0
 *      001  -> 1
 *      010  -> 2
 *      011  -> 3
 *      100  -> 4
 *      101  -> 5
 *      110  -> 6
 *      111  -> 7
 *
 *  Note that in four of these, the sum of H and V is 1 (odd).
 *  For these four, we have a change in parity (handedness) of
 *  the image, and the transformation cannot be performed by
 *  rotation about a vertical line out of the page.   Under
 *  rotation R, the set of 8 transformations decomposes into
 *  two subgroups linking {0, 3, 4, 7} and {1, 2, 5, 6} independently.
 *
 *  pixOrientDetect() tests for a pure rotation (0, 90, 180, 270 degrees).
 *  It doesn't change parity.
 *
 *  pixMirrorDetect() tests for a horizontal flip about the vertical axis.
 *  It changes parity.
 *
 *  The landscape/portrait rotation can be detected in two ways:
 *
 *    (1) Compute the deskew confidence for an image segment,
 *        both as is and rotated 90 degrees  (see skew.c).
 *
 *    (2) Compute the ascender/descender signal for the image,
 *        both as is and rotated 90 degrees  (implemented here).
 *
 *  The ascender/descender signal is useful for determining text
 *  orientation in Roman alphabets because the incidence of letters
 *  with straight-line ascenders (b, d, h, k, l, 't') outnumber
 *  those with descenders ('g', p, q).  The letters 't' and 'g'
 *  will respond variably to the filter, depending on the type face.
 *
 *  Note that for Cyrillic text, the ascender/descender signal is
 *  opposite (more descenders than ascenders) and weaker (a smaller
 *  expected ratio as a fraction of the total number of ascenders
 *  and descenders).  Therefore, when used on Cyrillic text, the
 *  decision of rightside-up or upside-down will be wrong and if any
 *  interpretation is to be made, it should be interpreted oppositely.
 *  For horizontal Cyrillic text, the left-right detector will work
 *  correctly, giving very low confidence because no signal for
 *  90/270 degree text was detected.
 *
 *  What about the mirror image situations?  These aren't common
 *  unless you're dealing with film, for example.
 *  But you can reliably test if the image has undergone a
 *  parity-changing flip once about some axis in the plane
 *  of the image, using pixMirrorDetect*().  This works ostensibly by
 *  counting the number of characters with ascenders that
 *  stick out to the left and right of the ascender.  Characters
 *  that are not mirror flipped are more likely to extend to the
 *  right (b, h, k) than to the left (d).  Of course, that is for
 *  text that is rightside-up.  So before you apply the mirror
 *  test, it is necessary to insure that the text has the ascenders
 *  going up, and not down or to the left or right.  But here's
 *  what *really* happens.  It turns out that the pre-filtering before
 *  the hit-miss transform (HMT) is crucial, and surprisingly, when
 *  the pre-filtering is chosen to generate a large signal, the majority
 *  of the signal comes from open regions of common lower-case
 *  letters such as 'e', 'c' and 'f'.
 *
 *  The set of operations you actually use depends on your prior knowledge:
 *
 *  (1) If the page is known to be either rightside-up or upside-down, use
 *      either pixOrientDetect() with pleftconf = NULL, or
 *      pixUpDownDetect().
 *
 *  (2) If any of the four orientations are possible, use pixOrientDetect().
 *
 *  (3) If the text is horizontal and rightside-up, the only remaining
 *      degree of freedom is a left-right mirror flip: use pixMirrorDetect().
 *
 *  (4) If you have a relatively large amount of numbers on the page,
 *      use the slower pixUpDownDetect().
 *
 *  We summarize the full orientation and mirror flip detection process:
 *
 *  (1) First determine which of the four 90 degree rotations
 *      causes the text to be rightside-up.  This can be done
 *      with either skew confidence or the pixOrientDetect()
 *      signals.  For the latter, see the table for pixOrientDetect().
 *
 *  (2) Then, with ascenders pointing up, apply pixMirrorDetect().
 *      In the normal situation the confidence confidence will be
 *      large and positive.  However, if mirror flipped, the
 *      confidence will be large and negative.
 *
 *  A high-level interface, pixOrientCorrect() combines the detection
 *  of the orientation with the rotation decision and the rotation itself.
 *
 *  The structuring elements used for text orientation detection require text
 *  with ascenders and descenders.  They have been designed to work best
 *  with normal sized text (about 10 pt font), scanned with a resolution
 *  between 150 and 300 ppi.
 *
 *  Differences from C:
 *    - The optional outputs of pixOrientDetect() and pixOrientCorrect() are
 *      always computed.  Output to stderr (lept_stderr) is dropped.
 */

/// <summary>Constants from pix.h for the text orientation.</summary>
public static partial class L
{
    /* Text orientation */
    /// <summary>Low confidence in the text orientation.</summary>
    public const int L_TEXT_ORIENT_UNKNOWN = 0;
    /// <summary>Portrait page, text right-side up.</summary>
    public const int L_TEXT_ORIENT_UP = 1;
    /// <summary>Landscape page, text up to the left.</summary>
    public const int L_TEXT_ORIENT_LEFT = 2;
    /// <summary>Portrait page, text upside down.</summary>
    public const int L_TEXT_ORIENT_DOWN = 3;
    /// <summary>Landscape page, text up to the right.</summary>
    public const int L_TEXT_ORIENT_RIGHT = 4;
}

public sealed partial class Pix
{
        /* Sels for pixOrientDetect() and pixMirrorDetect() */
    private const string textsel1 = "x  oo " +
                                    "x oOo " +
                                    "x  o  " +
                                    "x     " +
                                    "xxxxxx";

    private const string textsel2 = " oo  x" +
                                    " oOo x" +
                                    "  o  x" +
                                    "     x" +
                                    "xxxxxx";

    private const string textsel3 = "xxxxxx" +
                                    "x     " +
                                    "x  o  " +
                                    "x oOo " +
                                    "x  oo ";

    private const string textsel4 = "xxxxxx" +
                                    "     x" +
                                    "  o  x" +
                                    " oOo x" +
                                    " oo  x";

        /* Parameters for determining orientation */
    private const int DefaultMinUpDownCount = 70;
    private const float DefaultMinUpDownConf = 8.0f;
    private const float DefaultMinUpDownRatio = 2.5f;

        /* Parameters for determining mirror flip */
    private const int DefaultMinMirrorFlipCount = 100;
    private const float DefaultMinMirrorFlipConf = 5.0f;

    /*----------------------------------------------------------------*
     *        High-level interface for detection and correction       *
     *----------------------------------------------------------------*/
    // leptonica: pixOrientCorrect (flipdetect.c)
    /// <param name="minupconf">minimum value for which a decision can be made</param>
    /// <param name="minratio">minimum conf ratio required for a decision</param>
    /// <param name="upconf"></param>
    /// <param name="leftconf"></param>
    /// <param name="rotation">0, 90, 180 or 270 (cw)</param>
    /// <param name="debug">1 for debug output; 0 otherwise</param>
    /// <returns>pixd  may be rotated by 90, 180 or 270; pixs is 1 bpp, deskewed, Roman text, 150 - 300 ppi</returns>
    /// <remarks>
    /// (1) Simple top-level function to detect if Roman text is in
    ///     reading orientation, and to rotate the image accordingly if not.
    /// (2) Returns a copy if no rotation is needed.
    /// (3) See notes for pixOrientDetect() and pixOrientDecision().
    ///     Use 0.0 for default values for %minupconf and %minratio
    /// (4) Optional output of intermediate confidence results and
    ///     the rotation performed on pixs.
    /// (5) Use on text images with a resolution between 150 and 300 ppi.
    /// </remarks>
    public Pix OrientCorrect(float minupconf, float minratio, out float upconf, out float leftconf,
                             out int rotation, int debug)
    {
        Pix pixs = this;

        if (pixs.Depth != 1)
            throw Lept.Arg("pixOrientCorrect", "pixs undefined or not 1 bpp");

            /* Get confidences for orientation */
        upconf = pixs.UpDownDetect(0, 0, debug);
        Pix pix1 = pixs.Rotate90(1);
        leftconf = pix1.UpDownDetect(0, 0, debug);

            /* Decide what to do */
        int orient = MakeOrientDecision(upconf, leftconf, minupconf, minratio, debug);

            /* Do it */
        switch (orient)
        {
        case L.L_TEXT_ORIENT_UNKNOWN:
            Log.Info("pixOrientCorrect", "text orientation not determined; no rotation");
            rotation = 0;
            return Copy(null, pixs);
        case L.L_TEXT_ORIENT_UP:
            Log.Info("pixOrientCorrect", "text is oriented up; no rotation");
            rotation = 0;
            return Copy(null, pixs);
        case L.L_TEXT_ORIENT_LEFT:
            Log.Info("pixOrientCorrect", "landscape; text oriented left; 90 cw rotation");
            rotation = 90;
            return pixs.RotateOrth(1);
        case L.L_TEXT_ORIENT_DOWN:
            Log.Info("pixOrientCorrect", "text oriented down; 180 cw rotation");
            rotation = 180;
            return pixs.RotateOrth(2);
        case L.L_TEXT_ORIENT_RIGHT:
            Log.Info("pixOrientCorrect", "landscape; text oriented right; 270 cw rotation");
            rotation = 270;
            return pixs.RotateOrth(3);
        default:
            Log.Error("pixOrientCorrect", "invalid orient flag!");
            rotation = 0;
            return Copy(null, pixs);
        }
    }

    /*----------------------------------------------------------------*
     *         Orientation detection (four 90 degree angles)          *
     *----------------------------------------------------------------*/
    // leptonica: pixOrientDetect (flipdetect.c)
    /// <param name="upconf"></param>
    /// <param name="leftconf"></param>
    /// <param name="mincount">min number of up + down; use 0 for default</param>
    /// <param name="debug">1 for debug output; 0 otherwise</param>
    /// <remarks>
    /// (1) See "Measuring document image skew and orientation"
    ///     Dan S. Bloomberg, Gary E. Kopec and Lakshmi Dasari
    ///     IS&amp;T/SPIE EI'95, Conference 2422: Document Recognition II
    ///     pp 302-316, Feb 6-7, 1995, San Jose, CA
    /// (2) upconf is the normalized difference between up ascenders
    ///     and down ascenders.  The image is analyzed without rotation
    ///     for being rightside-up or upside-down.
    /// (3) leftconf is the normalized difference between up ascenders
    ///     and down ascenders in the image after it has been
    ///     rotated 90 degrees clockwise.  With that rotation, ascenders
    ///     projecting to the left in the source image will project up
    ///     in the rotated image.  We compute this by rotating 90 degrees
    ///     clockwise and testing for up and down ascenders.
    /// (4) Note that upconf and leftconf are not linear measures of
    ///     confidence, e.g., in a range between 0 and 100.  They
    ///     measure how far you are out on the tail of a (presumably)
    ///     normal distribution.  For example, a confidence of 10 means
    ///     that it is nearly certain that the difference did not
    ///     happen at random.  However, these values must be interpreted
    ///     cautiously, taking into consideration the estimated prior
    ///     for a particular orientation or mirror flip.   The up-down
    ///     signal is very strong if applied to text with ascenders
    ///     up and down, and relatively weak for text at 90 degrees,
    ///     but even at 90 degrees, the difference can look significant.
    ///     For example, suppose the ascenders are oriented horizontally,
    ///     but the test is done vertically.  Then upconf can
    ///     be &lt; -MIN_CONF_FOR_UP_DOWN, suggesting the text may be
    ///     upside-down.  However, if instead the test were done
    ///     horizontally, leftconf will be very much larger
    ///     (in absolute value), giving the correct orientation.
    /// (5) If you compute both upconf and leftconf, and there is
    ///     sufficient signal, the following table determines the
    ///     cw angle necessary to rotate pixs so that the text is
    ///     rightside-up:
    ///        0 deg :           upconf >> 1,    abs(upconf) >> abs(leftconf)
    ///        90 deg :          leftconf >> 1,  abs(leftconf) >> abs(upconf)
    ///        180 deg :         upconf &lt;&lt; -1,   abs(upconf) >> abs(leftconf)
    ///        270 deg :         leftconf &lt;&lt; -1, abs(leftconf) >> abs(upconf)
    /// (6) One should probably not interpret the direction unless
    ///     there are a sufficient number of counts for both orientations,
    ///     in which case neither upconf nor leftconf will be 0.0.
    /// (7) Use on text images with a resolution between 150 and 300 ppi.
    /// (8) This algorithm will fail on some images, such as tables,
    ///     where most of the characters are numbers and appear as
    ///     uppercase, but there are some repeated words that give a
    ///     biased signal.  It may be advisable to run a table detector
    ///     first (e.g., pixDecideIfTable()), and not run the orientation
    ///     detector if it is a table.
    /// (9) Uses rasterop implementation of HMT.
    /// (10) pixs is 1 bpp, deskewed, Roman text, 150 - 300 ppi.
    /// </remarks>
    public void OrientDetect(out float upconf, out float leftconf, int mincount, int debug)
    {
        Pix pixs = this;

        upconf = 0.0f;
        leftconf = 0.0f;
        if (pixs.Depth != 1)
            throw Lept.Arg("pixOrientDetect", "pixs not defined or not 1 bpp");
        if (mincount == 0)
            mincount = DefaultMinUpDownCount;

        upconf = pixs.UpDownDetect(mincount, 0, debug);
        Pix pix1 = pixs.Rotate90(1);
        leftconf = pix1.UpDownDetect(mincount, 0, debug);
    }

    // leptonica: makeOrientDecision (flipdetect.c)
    /// <param name="upconf">nonzero</param>
    /// <param name="leftconf">nonzero</param>
    /// <param name="minupconf">minimum value for which a decision can be made</param>
    /// <param name="minratio">minimum conf ratio required for a decision</param>
    /// <param name="debug">1 for debug output; 0 otherwise</param>
    /// <returns>text orientation enum {0,1,2,3,4}</returns>
    /// <remarks>
    /// (1) This can be run after pixOrientDetect()
    /// (2) Both upconf and leftconf must be nonzero; otherwise the
    ///     orientation cannot be determined.
    /// (3) The abs values of the input confidences are compared to
    ///     minupconf.
    /// (4) The abs value of the largest of (upconf/leftconf) and
    ///     (leftconf/upconf) is compared with minratio.
    /// (5) Input 0.0 for the default values for minupconf and minratio.
    /// (6) The return value of orient is interpreted thus:
    ///       L_TEXT_ORIENT_UNKNOWN:  not enough evidence to determine
    ///       L_TEXT_ORIENT_UP:       text rightside-up
    ///       L_TEXT_ORIENT_LEFT:     landscape, text up facing left
    ///       L_TEXT_ORIENT_DOWN:     text upside-down
    ///       L_TEXT_ORIENT_RIGHT:    landscape, text up facing right
    /// </remarks>
    public static int MakeOrientDecision(float upconf, float leftconf, float minupconf,
                                         float minratio, int debug)
    {
        int orient = L.L_TEXT_ORIENT_UNKNOWN;  /* default: no decision */
        if (upconf == 0.0 || leftconf == 0.0) {
            Log.Info("makeOrientDecision", "not enough confidence to get orientation");
            return orient;
        }

        if (minupconf == 0.0)
            minupconf = DefaultMinUpDownConf;
        if (minratio == 0.0)
            minratio = DefaultMinUpDownRatio;
        float absupconf = Math.Abs(upconf);
        float absleftconf = Math.Abs(leftconf);

            /* Here are the four possible orientation decisions, based
             * on satisfaction of two threshold constraints. */
        if (upconf > minupconf && absupconf > minratio * absleftconf)
            orient = L.L_TEXT_ORIENT_UP;
        else if (leftconf > minupconf && absleftconf > minratio * absupconf)
            orient = L.L_TEXT_ORIENT_LEFT;
        else if (upconf < -minupconf && absupconf > minratio * absleftconf)
            orient = L.L_TEXT_ORIENT_DOWN;
        else if (leftconf < -minupconf && absleftconf > minratio * absupconf)
            orient = L.L_TEXT_ORIENT_RIGHT;

        return orient;
    }

    // leptonica: pixUpDownDetect (flipdetect.c)
    /// <param name="mincount">min number of up + down; use 0 for default</param>
    /// <param name="npixels">number of pixels removed from each side of word box</param>
    /// <param name="debug">1 for debug output; 0 otherwise</param>
    /// <returns>confidence that text is rightside-up; pixs is 1 bpp, deskewed, Roman text, 150 - 300 ppi</returns>
    /// <remarks>
    /// (1) See pixOrientDetect() for other details.
    /// (2) The detected confidence %conf is the normalized difference
    ///     between the number of detected up and down ascenders,
    ///     assuming that the text is either rightside-up or upside-down
    ///     and not rotated at a 90 degree angle.
    /// (3) The typical mode of operation is %npixels == 0.
    ///     If %npixels > 0, this removes HMT matches at the
    ///     beginning and ending of "words."  This is useful for
    ///     pages that may have mostly digits, because if npixels == 0,
    ///     leading "1" and "3" digits can register as having
    ///     ascenders or descenders, and "7" digits can match descenders.
    ///     Consequently, a page image of only digits may register
    ///     as being upside-down.
    /// (4) We want to count the number of instances found using the HMT.
    ///     An expensive way to do this would be to count the
    ///     number of connected components.  A cheap way is to do a rank
    ///     reduction cascade that reduces each component to a single
    ///     pixel, and results (after two or three 2x reductions)
    ///     in one pixel for each of the original components.
    ///     After the reduction, you have a much smaller pix over
    ///     which to count pixels.  We do only 2 reductions, because
    ///     this function is designed to work for input pix between
    ///     150 and 300 ppi, and an 8x reduction on a 150 ppi image
    ///     is going too far -- components will get merged.
    /// (5) Use on text images with a resolution between 150 and 300 ppi.
    /// </remarks>
    public float UpDownDetect(int mincount, int npixels, int debug)
    {
        Pix pixs = this;
        Pix pix1, pix2, pix3;

        float conf = 0.0f;
        if (pixs.Depth != 1)
            throw Lept.Arg("pixUpDownDetect", "pixs not defined or not 1 bpp");
        if (mincount == 0)
            mincount = DefaultMinUpDownCount;
        if (npixels < 0)
            npixels = 0;

        if (debug != 0) {
            Utils.LeptMkdir("lept/orient");
        }

        Sel sel1 = Sel.CreateFromString(textsel1, 5, 6, null);
        Sel sel2 = Sel.CreateFromString(textsel2, 5, 6, null);
        Sel sel3 = Sel.CreateFromString(textsel3, 5, 6, null);
        Sel sel4 = Sel.CreateFromString(textsel4, 5, 6, null);

            /* One of many reasonable pre-filtering sequences: (1, 8) and (30, 1).
             * This closes holes in x-height characters and joins them at
             * the x-height.  There is more noise in the descender detection
             * from this, but it works fairly well. */
        Pix pix0 = pixs.MorphCompSequence("c1.8 + c30.1", 0);

            /* Optionally, make a mask of the word bounding boxes, shortening
             * each of them by a fixed amount at each end. */
        Pix? pixm = null;
        if (npixels > 0) {
            pix1 = pix0.MorphSequence("o10.1", 0);
            Boxa boxa = pix1.ConnComp(8);
            pixm = CreateTemplate(pix1);
            int nbox = boxa.Count;
            for (int i = 0; i < nbox; i++) {
                Box box = boxa.GetBox(i, L.L_CLONE);
                box.GetGeometry(out int x, out int y, out int w, out int h);
                if (w > 2 * npixels)
                    pixm.Rasterop(x + npixels, y - 6, w - 2 * npixels, h + 13,
                                  L.PIX_SET, null, 0, 0);
            }
        }

            /* Find the ascenders and optionally filter with pixm.
             * For an explanation of the procedure used for counting the result
             * of the HMT, see comments at the beginning of this function. */
        pix1 = HMT(null, pix0, sel1);
        pix2 = HMT(null, pix0, sel2);
        Or(pix1, pix1, pix2);
        if (pixm != null)
            And(pix1, pix1, pixm);
        pix3 = pix1.ReduceRankBinaryCascade(1, 1, 0, 0);
        int countup = pix3.CountPixels(null);
        DebugFlipDetect("/tmp/lept/orient/up.png", pixs, pix1, debug);

            /* Find the ascenders and optionally filter with pixm. */
        pix1 = HMT(null, pix0, sel3);
        pix2 = HMT(null, pix0, sel4);
        Or(pix1, pix1, pix2);
        if (pixm != null)
            And(pix1, pix1, pixm);
        pix3 = pix1.ReduceRankBinaryCascade(1, 1, 0, 0);
        int countdown = pix3.CountPixels(null);
        DebugFlipDetect("/tmp/lept/orient/down.png", pixs, pix1, debug);

            /* Evaluate statistically, generating a confidence that is
             * related to the probability with a gaussian distribution. */
        float nup = (float)(countup);
        float ndown = (float)(countdown);
        int nmax = Math.Max(countup, countdown);
        if (nmax > mincount)
            conf = (float)(2.0 * ((nup - ndown) / Math.Sqrt(nup + ndown)));

        if (debug != 0) {
            if (pixm != null) pixm.WriteDebug("/tmp/lept/orient/pixm1.png", L.IFF_PNG);
        }

        return conf;
    }

    /*----------------------------------------------------------------*
     *                     Left-right mirror detection                *
     *----------------------------------------------------------------*/
    // leptonica: pixMirrorDetect (flipdetect.c)
    /// <param name="mincount">min number of left + right; use 0 for default</param>
    /// <param name="debug">1 for debug output; 0 otherwise</param>
    /// <returns>confidence that text is not LR mirror reversed; pixs is 1 bpp, deskewed, Roman text, 150 - 300 ppi</returns>
    /// <remarks>
    /// (1) For this test, it is necessary that the text is horizontally
    ///     oriented, with ascenders going up.
    /// (2) conf is the normalized difference between the number of
    ///     right and left facing characters with ascenders.
    ///     Left-facing are {d}; right-facing are {b, h, k}.
    ///     At least that was the expectation.  In practice, we can
    ///     really just say that it is the normalized difference in
    ///     hits using two specific hit-miss filters, textsel1 and textsel2,
    ///     after the image has been suitably pre-filtered so that
    ///     these filters are effective.  See (4) for what's really happening.
    /// (3) A large positive conf value indicates normal text, whereas
    ///     a large negative conf value means the page is mirror reversed.
    /// (4) The implementation is a bit tricky.  The general idea is
    ///     to fill the x-height part of characters, but not the space
    ///     between them, before doing the HMT.  This is done by
    ///     finding pixels added using two different operations -- a
    ///     horizontal close and a vertical dilation -- and adding
    ///     the intersection of these sets to the original.  It turns
    ///     out that the original intuition about the signal was largely
    ///     in error: much of the signal for right-facing characters
    ///     comes from the lower part of common x-height characters, like
    ///     the e and c, that remain open after these operations.
    ///     So it's important that the operations to close the x-height
    ///     parts of the characters are purposely weakened sufficiently
    ///     to allow these characters to remain open.  The wonders
    ///     of morphology!
    /// (5) Use on text images with a resolution between 150 and 300 ppi.
    /// </remarks>
    public float MirrorDetect(int mincount, int debug)
    {
        Pix pixs = this;

        float conf = 0.0f;
        if (pixs.Depth != 1)
            throw Lept.Arg("pixMirrorDetect", "pixs not defined or not 1 bpp");
        if (mincount == 0)
            mincount = DefaultMinMirrorFlipCount;

        if (debug != 0) {
            Utils.LeptMkdir("lept/orient");
        }

        Sel sel1 = Sel.CreateFromString(textsel1, 5, 6, null);
        Sel sel2 = Sel.CreateFromString(textsel2, 5, 6, null);

            /* Fill x-height characters but not space between them, sort of. */
        Pix pix3 = pixs.MorphCompSequence("d1.30", 0);
        Xor(pix3, pix3, pixs);
        Pix pix0 = pixs.MorphCompSequence("c15.1", 0);
        Xor(pix0, pix0, pixs);
        And(pix0, pix0, pix3);
        Or(pix0, pix0, pixs);

            /* Filter the right-facing characters. */
        Pix pix1 = HMT(null, pix0, sel1);
        pix3 = pix1.ReduceRankBinaryCascade(1, 1, 0, 0);
        int count1 = pix3.CountPixels(null);
        DebugFlipDetect("/tmp/lept/orient/right.png", pixs, pix1, debug);

            /* Filter the left-facing characters. */
        Pix pix2 = HMT(null, pix0, sel2);
        pix3 = pix2.ReduceRankBinaryCascade(1, 1, 0, 0);
        int count2 = pix3.CountPixels(null);
        DebugFlipDetect("/tmp/lept/orient/left.png", pixs, pix2, debug);

        float nright = (float)count1;
        float nleft = (float)count2;
        int nmax = Math.Max(count1, count2);

        if (nmax > mincount)
            conf = (float)(2.0 * ((nright - nleft) / Math.Sqrt(nright + nleft)));

        return conf;
    }

    /*----------------------------------------------------------------*
     *                        Static debug helper                     *
     *----------------------------------------------------------------*/
    // leptonica: pixDebugFlipDetect (flipdetect.c)
    /// <param name="filename">for output debug file</param>
    /// <param name="pixs">input to pix*Detect</param>
    /// <param name="pixhm">hit-miss result from ascenders or descenders</param>
    /// <param name="enable">1 to enable this function; 0 to disable</param>
    private static void DebugFlipDetect(string filename, Pix pixs, Pix pixhm, int enable)
    {
        if (enable == 0) return;

            /* Display with red dot at counted locations */
        Pix pixt = pixs.Convert1To4Cmap();
        Pix pixthm = pixhm.MorphSequence("d5.5", 0);
        pixt.SetMaskedCmap(pixthm, 0, 0, 255, 0, 0);

        pixt.WriteDebug(filename, L.IFF_PNG);
    }
}
