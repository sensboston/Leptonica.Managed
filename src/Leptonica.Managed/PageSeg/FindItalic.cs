using System;

namespace Leptonica;

/*
 *  finditalic.c: Locate italic words using hit-miss binary morphology
 *                with binary reconstruction (filling from a seed into a mask).
 *
 *      Pix.ItalicWords    (pixItalicWords)
 *
 *  To see how this works, run with prog/italic.png.
 */

public sealed partial class Pix
{
    /* --------------------------------------------------------------- *
     * These hit-miss sels match the slanted edge of italic characters *
     * --------------------------------------------------------------- */
    private const string str_ital1 = "   o x" +
                                     "      " +
                                     "      " +
                                     "      " +
                                     "  o x " +
                                     "      " +
                                     "  C   " +
                                     "      " +
                                     " o x  " +
                                     "      " +
                                     "      " +
                                     "      " +
                                     "o x   ";

    private const string str_ital2 = "   o x" +
                                     "      " +
                                     "      " +
                                     "  o x " +
                                     "  C   " +
                                     "      " +
                                     " o x  " +
                                     "      " +
                                     "      " +
                                     "o x   ";

    /* ------------------------------------------------------------- *
     * This sel removes noise that is not oriented as a slanted edge *
     * ------------------------------------------------------------- */
    private const string str_ital3 = " x" +
                                     "Cx" +
                                     "x " +
                                     "x ";

    // leptonica: pixItalicWords (finditalic.c)
    /// <summary>Locates italic words using hit-miss morphology and binary reconstruction.</summary>
    /// <param name="boxaw">[optional] word bounding boxes; can be null</param>
    /// <param name="pixw">[optional] word box mask; can be null</param>
    /// <param name="boxa">boxa of italic words</param>
    /// <param name="debugflag">1 for debug output; 0 otherwise</param>
    /// <remarks>
    /// (1) You can input the bounding boxes for the words in one of
    ///     two forms: as bounding boxes (boxaw) or as a word mask with
    ///     the word bounding boxes filled (pixw).  For example,
    ///     to compute pixw, you can use pixWordMaskByDilation().
    /// (2) Alternatively, you can set both of these inputs to null,
    ///     in which case the word mask is generated here.  This is
    ///     done by dilating and closing the input image to connect
    ///     letters within a word, while leaving the words separated.
    ///     The parameters are chosen under the assumption that the
    ///     input is 10 to 12 pt text, scanned at about 300 ppi.
    /// (3) sel_ital1 and sel_ital2 detect the right edges that are
    ///     nearly vertical, at approximately the angle of italic
    ///     strokes.  We use the right edge to avoid getting seeds
    ///     from lower-case 'y'.  The typical italic slant has a smaller
    ///     angle with the vertical than the 'W', so in most cases we
    ///     will not trigger on the slanted lines in the 'W'.
    /// (4) Note that sel_ital2 is shorter than sel_ital1.  It is
    ///     more appropriate for a typical font scanned at 200 ppi.
    /// </remarks>
    public void ItalicWords(Boxa? boxaw, Pix? pixw, out Boxa boxa, int debugflag)
    {
        const string func = "pixItalicWords";
        int size, type;
        Pix pixsd, pixm, pixd;
        Sel sel_ital1, sel_ital2, sel_ital3;

        if (boxaw != null && pixw != null)
            throw Lept.Arg(func, "both boxaw and pixw are defined");

        sel_ital1 = Sel.CreateFromString(str_ital1, 13, 6, null);
        sel_ital2 = Sel.CreateFromString(str_ital2, 10, 6, null);
        _ = sel_ital2;
        sel_ital3 = Sel.CreateFromString(str_ital3, 4, 2, null);

            /* Make the italic seed: extract with HMT; remove noise.
             * The noise removal close/open is important to exclude
             * situations where a small slanted line accidentally
             * matches sel_ital1. */
        pixsd = HMT(null, this, sel_ital1);
        Close(pixsd, pixsd, sel_ital3);
        Open(pixsd, pixsd, sel_ital3);

            /* Make the word mask.  Use input boxes or mask if given. */
        size = 0;  /* init */
        if (boxaw != null) {
            pixm = CreateTemplate(this);
            MaskBoxa(pixm, pixm, boxaw, L.L_SET_PIXELS);
            type = 1;
        } else if (pixw != null) {
            pixm = Clone(pixw);
            type = 2;
        } else {
            WordMaskByDilation(out _, out size, null);
            Log.Info(func, $"dilation size = {size}\n");
            string opstring = $"d1.5 + c{size}.1";
            pixm = MorphSequence(opstring, 0);
            type = 3;
        }

            /* Binary reconstruction to fill in those word mask
             * components for which there is at least one seed pixel. */
        pixd = SeedfillBinary(null, pixsd, pixm, 8);
        boxa = pixd.ConnComp(8);

        if (debugflag != 0) {
                /* Save results at 2x reduction */
            int res, upper;
            Utils.LeptMkdir("lept/ital");
            Boxa boxat;
            Gplot gplot;
            Numa na;
            Pixa pixa1;
            Pix pix1, pix2, pix3;
            pixa1 = Pixa.Create(0);
            boxat = pixm.ConnComp(8);
            boxat.WriteDebug("/tmp/lept/ital/ital.ba");
            pixa1.AddPix(this, L.L_COPY);  /* orig */
            pixa1.AddPix(pixsd, L.L_COPY);  /* seed */
            pix1 = pixm.ConvertTo32();
            pix1.RenderBoxaArb(boxat, 3, 255, 0, 0);
            pixa1.AddPix(pix1, L.L_INSERT);  /* mask + outline */
            pixa1.AddPix(pixd, L.L_COPY);  /* ital mask */
            pix1 = ConvertTo32();
            pix1.RenderBoxaArb(boxa, 3, 255, 0, 0);
            pixa1.AddPix(pix1, L.L_INSERT);  /* orig + outline */
            pix1 = CreateTemplate(this);
            pix2 = pix1.SetBlackOrWhiteBoxa(boxa, L.L_SET_BLACK);
            Copy(pix1, this);
            pix3 = DilateBrick(null, this, 3, 3);
            pix1.CombineMasked(pix3, pix2);
            pixa1.AddPix(pix1, L.L_INSERT);  /* ital bolded */
            pix2 = pixa1.DisplayTiledInColumns(1, 0.5f, 20, 2);
            string buf = $"/tmp/lept/ital/ital.{type}.png";
            pix2.WriteDebug(buf, L.IFF_PNG);

                /* Assuming the image represents 6 inches of actual page width,
                 * the pixs resolution is approximately
                 *    (width of pixs in pixels) / 6
                 * and the images have been saved at half this resolution.   */
            res = Width / 12;
            Log.Info(func, $"resolution = {res}\n");
            PdfIo.SetDateAndVersion(0);
            buf = $"/tmp/lept/ital/ital.{type}.pdf";
            pixa1.ConvertToPdf(res, 1.0f, L.L_FLATE_ENCODE, 75, "Italic Finder",
                               buf);
            PdfIo.SetDateAndVersion(1);

                /* Plot histogram of horizontal white run sizes.  A small
                 * initial vertical dilation removes most runs that are neither
                 * inter-character nor inter-word.  The larger first peak is
                 * from inter-character runs, and the smaller second peak is
                 * from inter-word runs. */
            pix1 = DilateBrick(null, this, 1, 15);
            upper = Math.Max(30, 3 * size);
            na = pix1.RunHistogramMorph(L.L_RUN_OFF, L.L_HORIZ, upper);
            gplot = Gplot.Create("/tmp/lept/ital/runhisto", L.GPLOT_PNG,
                    "Histogram of horizontal runs of white pixels, vs length",
                    "run length", "number of runs");
            gplot.AddPlot(null, na, L.GPLOT_LINES, "plot1");
            gplot.MakeOutput();
        }
    }
}
