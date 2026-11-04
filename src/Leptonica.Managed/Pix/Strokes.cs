using System;

namespace Leptonica;

/*
 *  strokes.c: Stroke measurement and stroke width regulation of 1 bpp images.
 *
 *      Operations on 1 bpp images to:
 *      (1) measure stroke parameters, such as length and average width
 *      (2) change the average stroke width to a given value by eroding
 *          or dilating the image.
 *
 *      These operations are intended to operate on a single text
 *      character, to regularize the stroke width. It is expected
 *      that character matching by correlation, as used in the recog
 *      application, can often be improved by pre-processing both
 *      template and character images to a fixed stroke width.
 *
 *      Stroke parameter measurement
 *            Pix.FindStrokeLength (pixFindStrokeLength)
 *            Pix.FindStrokeWidth (pixFindStrokeWidth)
 *            Pixa.FindStrokeWidth (pixaFindStrokeWidth)
 *
 *      Stroke width regulation
 *            Pixa.ModifyStrokeWidth (pixaModifyStrokeWidth)
 *            Pix.ModifyStrokeWidth (pixModifyStrokeWidth)
 *            Pixa.SetStrokeWidth (pixaSetStrokeWidth)
 *            Pix.SetStrokeWidth (pixSetStrokeWidth)
 */
public sealed partial class Pix
{
    /*-----------------------------------------------------------------*
     *                   Stroke parameter measurement                  *
     *-----------------------------------------------------------------*/
    // leptonica: pixFindStrokeLength (strokes.c)
    /// <summary>Estimates the length of the strokes.</summary>
    /// <param name="tab8">[optional] table for counting fg pixels; can be null</param>
    /// <returns>estimated length of the strokes</returns>
    /// <remarks>(1) Returns half the number of fg boundary pixels.</remarks>
    public int FindStrokeLength(int[]? tab8)
    {
        Pix pix1 = ExtractBoundary(1);
        int[] tab = tab8 ?? MakePixelSumTab8();
        int n = pix1.CountPixels(tab);
        return n / 2;
    }

    // leptonica: pixFindStrokeWidth (strokes.c)
    /// <summary>Estimates the width of the strokes.</summary>
    /// <param name="thresh">fractional count threshold relative to distance 1</param>
    /// <param name="tab8">[optional] table for counting fg pixels; can be null</param>
    /// <param name="nahisto">histo of pixel distances from bg</param>
    /// <returns>estimated width of the strokes</returns>
    /// <remarks>
    /// (1) This uses two methods to estimate the stroke width:
    ///     (a) half the fg boundary length
    ///     (b) a value derived from the histogram of the fg distance transform
    /// (2) Distance is measured in 8-connected
    /// (3) %thresh is the minimum fraction N(dist=d)/N(dist=1) of pixels
    ///     required to determine if the pixels at distance d are above
    ///     the noise. It is typically about 0.15.
    /// </remarks>
    public float FindStrokeWidth(float thresh, int[]? tab8, out Numa nahisto)
    {
        Pix pixs = this;
        int[] tab = tab8 ?? MakePixelSumTab8();

        /* ------- Method 1: via boundary length ------- */
            /* The computed stroke length is a bit larger than that actual
             * length, because of the addition of the 'caps' at the
             * stroke ends.  Therefore the computed width is a bit
             * smaller than the average width. */
        int length = pixs.FindStrokeLength(tab8);
        int count = pixs.CountPixels(tab8);
        float width1 = (float)count / (float)length;

        /* ------- Method 2: via distance transform ------- */
            /* First get the histogram of distances */
        Pix pix1 = pixs.DistanceFunction(8, 8, L.L_BOUNDARY_BG);
        Numa na1 = pix1.GetGrayHistogram(1);
        na1.GetNonzeroRange(0.1f, out _, out int last);
        Numa na2 = na1.ClipToInterval(0, last);
        na2.WriteStderr();

            /* Find the bucket with the largest distance whose contents
             * exceed the threshold. */
        float[] fa = na2.GetFArray(L.L_NOCOPY);
        int n = na2.Count;
        int i;
        float ratio = 0.0f;
        for (i = n - 1; i > 0; i--) {
            ratio = fa[i] / fa[1];
            if (ratio > thresh) break;
        }
            /* Let the last skipped bucket contribute to the stop bucket.
             * This is the 'extra' term below.  The result may be a slight
             * over-correction, so the computed width may be a bit larger
             * than the average width. */
        float extra = (i < n - 1) ? fa[i + 1] / fa[1] : 0;
        float width2 = 2.0f * (i - 1.0f + ratio + extra);
        Utils.LeptStderr("width1 = %5.2f, width2 = %5.2f\n", width1, width2);

            /* Average the two results */
        nahisto = na2;
        return (width1 + width2) / 2.0f;
    }

    /*-----------------------------------------------------------------*
     *                       Change stroke width                       *
     *-----------------------------------------------------------------*/
    // leptonica: pixModifyStrokeWidth (strokes.c)
    /// <summary>Erodes or dilates the image so that the stroke width approaches the target.</summary>
    /// <param name="width">measured average stroke width</param>
    /// <param name="targetw">desired stroke width</param>
    /// <returns>pix with modified stroke width</returns>
    public Pix ModifyStrokeWidth(float width, float targetw)
    {
        const string func = "pixModifyStrokeWidth";
        Pix pixs = this;
        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (targetw < 1)
            throw Lept.Arg(func, "target width < 1");

        int diff = Utils.RoundFToI(targetw - width);
        if (diff == 0) return Copy(null, pixs);

        int size = Math.Abs(diff) + 1;
        string buf;
        if (diff < 0)  /* erode */
            buf = CFormat.Sprintf("e%d.%d", size, size);
        else  /* diff > 0; dilate */
            buf = CFormat.Sprintf("d%d.%d", size, size);
        return pixs.MorphSequence(buf, 0);
    }

    // leptonica: pixSetStrokeWidth (strokes.c)
    /// <summary>Sets the stroke width by (optionally) thinning and then dilating.</summary>
    /// <param name="width">set stroke width to this value, in [1 ... 100].</param>
    /// <param name="thinfirst">1 to thin all pix to a skeleton first; 0 to skip</param>
    /// <param name="connectivity">4 or 8, to be used if %thinfirst == 1</param>
    /// <returns>pixd with stroke width set to %width</returns>
    /// <remarks>
    /// (1) See notes in pixaSetStrokeWidth().
    /// (2) A white border of sufficient width to avoid boundary
    ///     artifacts in the thickening step is added before thinning.
    /// (3) %connectivity == 8 usually gives a slightly smoother result.
    /// </remarks>
    public Pix SetStrokeWidth(int width, int thinfirst, int connectivity)
    {
        const string func = "pixSetStrokeWidth";
        Pix pixs = this;
        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (width < 1 || width > 100)
            throw Lept.Arg(func, "width not in [1 ... 100]");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not 4 or 8");

        if (thinfirst == 0 && width == 1)  /* nothing to do */
            return Copy(null, pixs);

            /* Add a white border */
        int border = width / 2;
        Pix pix1 = pixs.AddBorder(border, 0);

            /* Thin to a skeleton */
        Pix pix2;
        if (thinfirst != 0)
            pix2 = pix1.ThinConnected(L.L_THIN_FG, connectivity, 0);
        else
            pix2 = Clone(pix1);

            /* Dilate */
        string buf = CFormat.Sprintf("D%d.%d", width, width);
        Pix pixd = pix2.MorphSequence(buf, 0);
        pixd.CopyText(pixs);
        return pixd;
    }
}

public sealed partial class Pixa
{
    // leptonica: pixaFindStrokeWidth (strokes.c)
    /// <summary>Stroke widths of each pix of a pixa.</summary>
    /// <param name="thresh">fractional count threshold relative to distance 1</param>
    /// <param name="tab8">[optional] table for counting fg pixels; can be null</param>
    /// <param name="debug">1 for debug output; 0 to skip</param>
    /// <returns>array of stroke widths for each pix</returns>
    /// <remarks>(1) See pixFindStrokeWidth() for details.</remarks>
    public Numa FindStrokeWidth(float thresh, int[]? tab8, int debug)
    {
        const string func = "pixaFindStrokeWidth";
        Pixa pixa = this;
        pixa.VerifyDepth(out _, out int maxd);
        if (maxd > 1)
            throw Lept.Arg(func, "pix not all 1 bpp");

        int n = pixa.Count;
        Numa na = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix = pixa.GetPix(i, L.L_CLONE);
            float width = pix.FindStrokeWidth(thresh, tab8, out _);
            na.AddNumber(width);
        }
        return na;
    }

    // leptonica: pixaModifyStrokeWidth (strokes.c)
    /// <summary>Modifies the stroke width of each pix to approach the target.</summary>
    /// <param name="targetw">desired width for strokes in each pix</param>
    /// <returns>pixa with modified stroke widths</returns>
    public Pixa ModifyStrokeWidth(float targetw)
    {
        const string func = "pixaModifyStrokeWidth";
        Pixa pixas = this;
        if (targetw < 1)
            throw Lept.Arg(func, "target width < 1");
        pixas.VerifyDepth(out _, out int maxd);
        if (maxd > 1)
            throw Lept.Arg(func, "pix not all 1 bpp");

        Numa na = pixas.FindStrokeWidth(0.1f, null, 0);
        int n = pixas.Count;
        Pixa pixad = Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_CLONE);
            float width = na.GetFValue(i);
            Pix pix2 = pix1.ModifyStrokeWidth(width, targetw);
            pixad.AddPix(pix2, L.L_INSERT);
        }
        return pixad;
    }

    // leptonica: pixaSetStrokeWidth (strokes.c)
    /// <summary>Sets the stroke width of each pix.</summary>
    /// <param name="width">set stroke width to this value, in [1 ... 100].</param>
    /// <param name="thinfirst">1 to thin all pix to a skeleton first; 0 to skip</param>
    /// <param name="connectivity">4 or 8, to be used if %thinfirst == 1</param>
    /// <returns>pixa with all stroke widths being %width</returns>
    /// <remarks>
    /// (1) If %thinfirst == 1, thin to a skeleton using the specified
    ///     %connectivity.  Use %thinfirst == 0 if all pix in pixas
    ///     have already been thinned as far as possible.
    /// (2) The image is dilated to the required %width.  This dilation
    ///     is not connectivity preserving, so this is typically
    ///     used in a situation where merging of c.c. in the individual
    ///     pix is not a problem; e.g., where each pix is a single c.c.
    /// </remarks>
    public Pixa SetStrokeWidth(int width, int thinfirst, int connectivity)
    {
        const string func = "pixaSetStrokeWidth";
        Pixa pixas = this;
        if (width < 1 || width > 100)
            throw Lept.Arg(func, "width not in [1 ... 100]");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not 4 or 8");
        pixas.VerifyDepth(out _, out int maxd);
        if (maxd > 1)
            throw Lept.Arg(func, "pix are not all 1 bpp");

        int n = pixas.Count;
        Pixa pixad = Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_CLONE);
            Pix pix2 = pix1.SetStrokeWidth(width, thinfirst, connectivity);
            pixad.AddPix(pix2, L.L_INSERT);
        }
        return pixad;
    }
}
