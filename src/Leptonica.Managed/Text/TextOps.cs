using System;
using System.Globalization;

namespace Leptonica;

/*
 *  textops.c: Text rendering on images with bitmap fonts.
 *
 *      Font layout
 *          pixAddSingleTextblock()
 *          pixAddTextlines()
 *          pixSetTextblock()
 *          pixSetTextline()
 *          pixaAddTextNumber()
 *          pixaAddTextlines()
 *          pixaAddPixWithText()
 *
 *      Text size estimation and partitioning
 *          bmfGetLineStrings()
 *          bmfGetWordWidths()
 *          bmfGetStringWidth()
 *
 *      Text splitting
 *          splitStringToParagraphs()
 *          stringAllWhitespace()
 *          stringLeadingWhitespace()
 *
 *  This is a simple utility to put text on images.  One font and style
 *  is provided, with a variety of pt sizes.  For example, to put a
 *  line of green 10 pt text on an image, with the beginning baseline
 *  at (50, 50):
 *      L_Bmf bmf = L_Bmf.Create(null, 10);
 *      string textstr = "This is a funny cat";
 *      pixs.SetTextline(bmf, textstr, 0x00ff0000, 50, 50, out _, out _);
 *
 *  The simplest interfaces for adding text to an image are
 *  pixAddTextlines() and pixAddSingleTextblock().
 *  For example, to add the same text in red, centered, below the image:
 *      Pix pixd = pixs.AddTextlines(bmf, textstr, 0xff000000, L.L_ADD_BELOW);
 *
 *  To add text to all pix in a pixa, generating a new pixa, use
 *  either an sarray to hold the strings for each pix, or use the
 *  strings in the text field of each pix; e.g.,
 *      Pixa pixa2 = pixa1.AddTextlines(bmf, sa, 0x0000ff00, L.L_ADD_LEFT);
 *      Pixa pixa2 = pixa1.AddTextlines(bmf, null, 0x00ff0000, L.L_ADD_RIGHT);
 *
 *  Differences from C:
 *    - The functions with a bitmap font argument that return a copy in C when
 *      the font is NULL take a nullable L_Bmf and do the same.
 *    - bmfGetLineStrings(), bmfGetWordWidths() and bmfGetStringWidth() are
 *      members of L_Bmf; the heights and widths are returned as values.
 *    - splitStringToParagraphs() is a static member of Pix.
 *    - Errors that return an error code in C throw instead.
 *
 *  Not ported: nothing.
 */

/// <summary>Constants from pix.h used by the text functions.</summary>
public static partial class L
{
    /* Add Text Location (L_ADD_*) */
    /// <summary>Add the text above the image.</summary>
    public const int L_ADD_ABOVE = 1;
    /// <summary>Add the text below the image.</summary>
    public const int L_ADD_BELOW = 2;
    /// <summary>Add the text to the left of the image.</summary>
    public const int L_ADD_LEFT = 3;
    /// <summary>Add the text to the right of the image.</summary>
    public const int L_ADD_RIGHT = 4;
    /// <summary>Add the text over the top of the image.</summary>
    public const int L_ADD_AT_TOP = 5;
    /// <summary>Add the text over the bottom of the image.</summary>
    public const int L_ADD_AT_BOT = 6;
    /// <summary>Add the text over the left side of the image.</summary>
    public const int L_ADD_AT_LEFT = 7;
    /// <summary>Add the text over the right side of the image.</summary>
    public const int L_ADD_AT_RIGHT = 8;
}

public sealed partial class Pix
{
    /*---------------------------------------------------------------------*
     *                                 Font layout                         *
     *---------------------------------------------------------------------*/
    // leptonica: pixAddSingleTextblock (textops.c)
    /// <summary>Paints a block of text, breaking it into lines that fit the image width.</summary>
    /// <param name="bmf">bitmap font data; if null, returns a copy</param>
    /// <param name="textstr">[optional] text string to be added; if null, uses the text field of the pix</param>
    /// <param name="val">color to set the text</param>
    /// <param name="location">L_ADD_ABOVE, L_ADD_AT_TOP, L_ADD_AT_BOT, L_ADD_BELOW</param>
    /// <param name="overflow">1 if the text overflows the allocated region and is clipped; 0 otherwise</param>
    /// <returns>new pix with rendered text, or a copy</returns>
    /// <remarks>
    /// (1) This function paints a set of lines of text over an image.
    ///     If location is L_ADD_ABOVE or L_ADD_BELOW, the pix size
    ///     is expanded with a border and rendered over the border.
    /// (2) val is the pixel value to be painted through the font mask.
    ///     It should be chosen to agree with the depth of pixs.
    ///     If it is out of bounds, an intermediate value is chosen.
    ///     For RGB, use hex notation: 0xRRGGBB00, where RR is the
    ///     hex representation of the red intensity, etc.
    /// (3) If textstr == NULL, use the text field in the pix.
    /// (4) If there is a colormap, this does the best it can to use
    ///     the requested color, or something similar to it.
    /// (5) Typical usage is for labelling a pix with some text data.
    /// </remarks>
    public Pix AddSingleTextblock(L_Bmf? bmf, string? textstr, uint val, int location, out int overflow)
    {
        const string func = "pixAddSingleTextblock";
        Pix pixs = this;

        overflow = 0;
        if (location != L.L_ADD_ABOVE && location != L.L_ADD_AT_TOP &&
            location != L.L_ADD_AT_BOT && location != L.L_ADD_BELOW)
            throw Lept.Arg(func, "invalid location");
        if (bmf == null) {
            Log.Error(func, "no bitmap fonts; returning a copy");
            return Copy(null, pixs);
        }
        if (textstr == null)
            textstr = pixs.Text;
        if (textstr == null) {
            Log.Warning(func, "no textstring defined; returning a copy");
            return Copy(null, pixs);
        }

        /* Make sure the "color" value for the text will work
         * for the pix.  If the pix is not colormapped and the
         * value is out of range, set it to mid-range. */
        pixs.GetDimensions(out int w, out int h, out int d);
        PixColormap? cmap = pixs.Colormap;
        if (d == 1 && val > 1)
            val = 1;
        else if (d == 2 && val > 3 && cmap == null)
            val = 2;
        else if (d == 4 && val > 15 && cmap == null)
            val = 8;
        else if (d == 8 && val > 0xff && cmap == null)
            val = 128;
        else if (d == 16 && val > 0xffff)
            val = 0x8000;
        else if (d == 32 && val < 256)
            val = 0x80808000;

        int xstart = (int)(0.1 * w);
        Sarray salines = bmf.GetLineStrings(textstr, w - 2 * xstart, 0, out int htext);
        int nlines = salines.Count;

        /* Add white border if required */
        int spacer = 10;  /* pixels away from image boundary or added border */
        Pix pixd;
        if (location == L.L_ADD_ABOVE || location == L.L_ADD_BELOW) {
            int extra = htext + 2 * spacer;
            pixd = Create(w, h + extra, d);
            pixd.CopyColormap(pixs);
            pixd.CopyResolution(pixs);
            pixd.CopyText(pixs);
            pixd.SetBlackOrWhite(L.L_BRING_IN_WHITE);
            if (location == L.L_ADD_ABOVE)
                pixd.Rasterop(0, extra, w, h, L.PIX_SRC, pixs, 0, 0);
            else  /* add below */
                pixd.Rasterop(0, 0, w, h, L.PIX_SRC, pixs, 0, 0);
        } else {
            pixd = Copy(null, pixs);
        }
        PixColormap? cmapd = pixd.Colormap;

        /* bmf->baselinetab[93] is the approximate distance from
         * the top of the tallest character to the baseline.  93 was chosen
         * at random, as all the baselines are essentially equal for
         * each character in a font. */
        int offset = bmf.BaselineTab[93];
        int ystart;
        if (location == L.L_ADD_ABOVE || location == L.L_ADD_AT_TOP)
            ystart = offset + spacer;
        else if (location == L.L_ADD_AT_BOT)
            ystart = h - htext - spacer + offset;
        else   /* add below */
            ystart = h + offset + spacer;

        /* If cmapped, add the color if necessary to the cmap.  If the
         * cmap is full, use the nearest color to the requested color. */
        uint textcolor;
        if (cmapd != null) {
            ExtractRGBValues(val, out int rval, out int gval, out int bval);
            cmapd.AddNearestColor(rval, gval, bval, out int index);
            cmapd.GetColor(index, out rval, out gval, out bval);
            ComposeRGBPixel(rval, gval, bval, out textcolor);
        } else {
            textcolor = val;
        }

        /* Keep track of overflow condition on line width */
        int ovf;
        overflow = 0;
        for (int i = 0, y0 = ystart; i < nlines; i++) {
            string linestr = salines.GetString(i, L.L_NOCOPY);
            pixd.SetTextline(bmf, linestr, textcolor, xstart, y0, out _, out ovf);
            y0 += bmf.LineHeight + bmf.VertLineSep;
            if (ovf != 0)
                overflow = 1;
        }

        /* Also consider vertical overflow where there is too much text to
         * fit inside the image: the cases L_ADD_AT_TOP and L_ADD_AT_BOT.
         * The text requires a total of htext + 2 * spacer vertical pixels. */
        if (location == L.L_ADD_AT_TOP || location == L.L_ADD_AT_BOT) {
            if (h < htext + 2 * spacer)
                overflow = 1;
        }

        return pixd;
    }

    // leptonica: pixAddTextlines (textops.c)
    /// <summary>Adds one or more lines of text adjacent to the image, expanding it as required.</summary>
    /// <param name="bmf">bitmap font data; if null, returns a copy</param>
    /// <param name="textstr">[optional] text string to be added; if null, uses the text field of the pix</param>
    /// <param name="val">color to set the text</param>
    /// <param name="location">L_ADD_ABOVE, L_ADD_BELOW, L_ADD_LEFT, L_ADD_RIGHT</param>
    /// <returns>new pix with rendered text, or a copy</returns>
    /// <remarks>
    /// (1) This function expands an image as required to paint one or
    ///     more lines of text adjacent to the image.  If bmf == NULL,
    ///     this returns a copy.  If above or below, the lines are
    ///     centered with respect to the image; if left or right, they
    ///     are left justified.
    /// (2) val is the pixel value to be painted through the font mask.
    ///     It should be chosen to agree with the depth of pixs.
    ///     If it is out of bounds, an intermediate value is chosen.
    ///     For RGB, use hex notation: 0xRRGGBB00, where RR is the
    ///     hex representation of the red intensity, etc.
    /// (3) If textstr == NULL, use the text field in the pix.  The
    ///     text field contains one or most "lines" of text, where newlines
    ///     are used as line separators.
    /// (4) If there is a colormap, this does the best it can to use
    ///     the requested color, or something similar to it.
    /// (5) Typical usage is for labelling a pix with some text data.
    /// </remarks>
    public Pix AddTextlines(L_Bmf? bmf, string? textstr, uint val, int location)
    {
        const string func = "pixAddTextlines";
        Pix pixs = this;

        if (location != L.L_ADD_ABOVE && location != L.L_ADD_BELOW &&
            location != L.L_ADD_LEFT && location != L.L_ADD_RIGHT)
            throw Lept.Arg(func, "invalid location");
        if (bmf == null) {
            Log.Error(func, "no bitmap fonts; returning a copy");
            return Copy(null, pixs);
        }
        if (textstr == null) {
            textstr = pixs.Text;
            if (textstr == null) {
                Log.Warning(func, "no textstring defined; returning a copy");
                return Copy(null, pixs);
            }
        }

        /* Make sure the "color" value for the text will work
         * for the pix.  If the pix is not colormapped and the
         * value is out of range, set it to mid-range. */
        pixs.GetDimensions(out int w, out int h, out int d);
        PixColormap? cmap = pixs.Colormap;
        if (d == 1 && val > 1)
            val = 1;
        else if (d == 2 && val > 3 && cmap == null)
            val = 2;
        else if (d == 4 && val > 15 && cmap == null)
            val = 8;
        else if (d == 8 && val > 0xff && cmap == null)
            val = 128;
        else if (d == 16 && val > 0xffff)
            val = 0x8000;
        else if (d == 32 && val < 256)
            val = 0x80808000;

        /* Get the text in each line */
        Sarray sa = Sarray.CreateLinesFromString(textstr, 0);
        int nlines = sa.Count;

        /* Get the necessary text size */
        int wtext = 0;
        for (int i = 0; i < nlines; i++) {
            string str = sa.GetString(i, L.L_NOCOPY);
            int wline = bmf.GetStringWidth(str);
            if (wline > wtext)
                wtext = wline;
        }
        int hbaseline = bmf.BaselineTab[93];
        int htext = (int)(1.5 * hbaseline * nlines);

        /* Add white border */
        int spacer = 10;  /* pixels away from the added border */
        Pix pixd;
        if (location == L.L_ADD_ABOVE || location == L.L_ADD_BELOW) {
            int hadd = htext + 2 * spacer;
            pixd = Create(w, h + hadd, d);
            pixd.CopyColormap(pixs);
            pixd.CopyResolution(pixs);
            pixd.CopyText(pixs);
            pixd.SetBlackOrWhite(L.L_BRING_IN_WHITE);
            if (location == L.L_ADD_ABOVE)
                pixd.Rasterop(0, hadd, w, h, L.PIX_SRC, pixs, 0, 0);
            else  /* add below */
                pixd.Rasterop(0, 0, w, h, L.PIX_SRC, pixs, 0, 0);
        } else {  /*  L_ADD_LEFT or L_ADD_RIGHT */
            int wadd = wtext + 2 * spacer;
            pixd = Create(w + wadd, h, d);
            pixd.CopyColormap(pixs);
            pixd.CopyResolution(pixs);
            pixd.CopyText(pixs);
            pixd.SetBlackOrWhite(L.L_BRING_IN_WHITE);
            if (location == L.L_ADD_LEFT)
                pixd.Rasterop(wadd, 0, w, h, L.PIX_SRC, pixs, 0, 0);
            else  /* add to right */
                pixd.Rasterop(0, 0, w, h, L.PIX_SRC, pixs, 0, 0);
        }

        /* If cmapped, add the color if necessary to the cmap.  If the
         * cmap is full, use the nearest color to the requested color. */
        PixColormap? cmapd = pixd.Colormap;
        uint textcolor;
        if (cmapd != null) {
            ExtractRGBValues(val, out int rval, out int gval, out int bval);
            cmapd.AddNearestColor(rval, gval, bval, out int index);
            cmapd.GetColor(index, out rval, out gval, out bval);
            ComposeRGBPixel(rval, gval, bval, out textcolor);
        } else {
            textcolor = val;
        }

        /* Add the text */
        for (int i = 0; i < nlines; i++) {
            string str = sa.GetString(i, L.L_NOCOPY);
            wtext = bmf.GetStringWidth(str);
            if (location == L.L_ADD_ABOVE)
                pixd.SetTextline(bmf, str, textcolor,
                                 (w - wtext) / 2, (int)(spacer + hbaseline * (1 + 1.5 * i)),
                                 out _, out _);
            else if (location == L.L_ADD_BELOW)
                pixd.SetTextline(bmf, str, textcolor,
                                 (w - wtext) / 2, (int)(h + spacer +
                                 hbaseline * (1 + 1.5 * i)), out _, out _);
            else if (location == L.L_ADD_LEFT)
                pixd.SetTextline(bmf, str, textcolor,
                                 spacer, (int)((h - htext) / 2 + hbaseline * (1 + 1.5 * i)),
                                 out _, out _);
            else  /* location == L_ADD_RIGHT */
                pixd.SetTextline(bmf, str, textcolor,
                                 w + spacer, (int)((h - htext) / 2 +
                                 hbaseline * (1 + 1.5 * i)), out _, out _);
        }

        return pixd;
    }

    // leptonica: pixSetTextblock (textops.c)
    /// <summary>Paints a block of text over the image, breaking it into lines of width wtext.</summary>
    /// <param name="bmf">bitmap font data</param>
    /// <param name="textstr">block text string to be set</param>
    /// <param name="val">color to set the text</param>
    /// <param name="x0">left edge for each line of text</param>
    /// <param name="y0">baseline location for the first text line</param>
    /// <param name="wtext">max width of each line of text</param>
    /// <param name="firstindent">indentation of first line, in x-widths</param>
    /// <param name="overflow">0 if text is contained in pixs; 1 if it is clipped</param>
    /// <remarks>
    /// (1) This function paints a set of lines of text over an image.
    /// (2) val is the pixel value to be painted through the font mask.
    ///     It should be chosen to agree with the depth of pixs.
    ///     If it is out of bounds, an intermediate value is chosen.
    ///     For RGB, use hex notation: 0xRRGGBB00, where RR is the
    ///     hex representation of the red intensity, etc.
    ///     The last two hex digits are 00 (byte value 0), assigned to
    ///     the A component.  Note that, as usual, RGBA proceeds from
    ///     left to right in the order from MSB to LSB (see pix.h
    ///     for details).
    /// (3) If there is a colormap, this does the best it can to use
    ///     the requested color, or something similar to it.
    /// </remarks>
    public void SetTextblock(L_Bmf bmf, string textstr, uint val, int x0, int y0,
                             int wtext, int firstindent, out int overflow)
    {
        const string func = "pixSetTextblock";
        Pix pixs = this;

        overflow = 0;

        /* Make sure the "color" value for the text will work
         * for the pix.  If the pix is not colormapped and the
         * value is out of range, set it to mid-range. */
        pixs.GetDimensions(out int w, out int h, out int d);
        PixColormap? cmap = pixs.Colormap;
        if (d == 1 && val > 1)
            val = 1;
        else if (d == 2 && val > 3 && cmap == null)
            val = 2;
        else if (d == 4 && val > 15 && cmap == null)
            val = 8;
        else if (d == 8 && val > 0xff && cmap == null)
            val = 128;
        else if (d == 16 && val > 0xffff)
            val = 0x8000;
        else if (d == 32 && val < 256)
            val = 0x80808000;

        if (w < x0 + wtext) {
            Log.Warning(func, "reducing width of textblock");
            wtext = w - x0 - w / 10;
            if (wtext <= 0)
                throw Lept.Fail(func, "wtext too small; no room for text");
        }

        Sarray salines = bmf.GetLineStrings(textstr, wtext, firstindent, out int htext);
        int nlines = salines.Count;
        int xwidth = bmf.GetWidth('x');
        int y = y0;
        int ovfl = 0;
        for (int i = 0; i < nlines; i++) {
            int x;
            if (i == 0)
                x = x0 + firstindent * xwidth;
            else
                x = x0;
            string linestr = salines.GetString(i, L.L_NOCOPY);
            pixs.SetTextline(bmf, linestr, val, x, y, out _, out int ovf);
            y += bmf.LineHeight + bmf.VertLineSep;
            if (ovf != 0)
                ovfl = 1;
        }

        /* (y0 - baseline) is the top of the printed text.  Character
         * 93 was chosen at random, as all the baselines are essentially
         * equal for each character in a font. */
        if (h < y0 - bmf.BaselineTab[93] + htext)
            ovfl = 1;
        overflow = ovfl;
    }

    // leptonica: pixSetTextline (textops.c)
    /// <summary>Paints a line of text over the image.</summary>
    /// <param name="bmf">bitmap font data</param>
    /// <param name="textstr">text string to be set on the line</param>
    /// <param name="val">color to set the text</param>
    /// <param name="x0">left edge for first char</param>
    /// <param name="y0">baseline location for all text on line</param>
    /// <param name="width">width of the painted text</param>
    /// <param name="overflow">0 if text is contained in pixs; 1 if it is clipped</param>
    /// <remarks>
    /// (1) This function paints a line of text over an image.
    /// (2) val is the pixel value to be painted through the font mask.
    ///     It should be chosen to agree with the depth of pixs.
    ///     If it is out of bounds, an intermediate value is chosen.
    ///     For RGB, use hex notation: 0xRRGGBB00, where RR is the
    ///     hex representation of the red intensity, etc.
    ///     The last two hex digits are 00 (byte value 0), assigned to
    ///     the A component.  Note that, as usual, RGBA proceeds from
    ///     left to right in the order from MSB to LSB (see pix.h
    ///     for details).
    /// (3) If there is a colormap, this does the best it can to use
    ///     the requested color, or something similar to it.
    /// </remarks>
    public void SetTextline(L_Bmf bmf, string textstr, uint val, int x0, int y0,
                            out int width, out int overflow)
    {
        Pix pixs = this;

        int d = pixs.Depth;
        PixColormap? cmap = pixs.Colormap;
        if (d == 1 && val > 1)
            val = 1;
        else if (d == 2 && val > 3 && cmap == null)
            val = 2;
        else if (d == 4 && val > 15 && cmap == null)
            val = 8;
        else if (d == 8 && val > 0xff && cmap == null)
            val = 128;
        else if (d == 16 && val > 0xffff)
            val = 0x8000;
        else if (d == 32 && val < 256)
            val = 0x80808000;

        /* If cmapped, add the color if necessary to the cmap.  If the
         * cmap is full, use the nearest color to the requested color. */
        uint textcolor;
        if (cmap != null) {
            ExtractRGBValues(val, out int rval, out int gval, out int bval);
            cmap.AddNearestColor(rval, gval, bval, out int index);
            cmap.GetColor(index, out rval, out gval, out bval);
            ComposeRGBPixel(rval, gval, bval, out textcolor);
        } else {
            textcolor = val;
        }

        int nchar = textstr.Length;
        int x = x0;
        for (int i = 0; i < nchar; i++) {
            char chr = textstr[i];
            if ((int)chr == 10) continue;  /* NL */
            Pix? pix = bmf.GetPix(chr);
            int baseline = bmf.GetBaseline(chr);
            pixs.PaintThroughMask(pix, x, y0 - baseline, textcolor);
            int w = (pix != null) ? pix.Width : 0;
            x += w + bmf.KernWidth;
        }

        width = x - bmf.KernWidth - x0;
        overflow = (x > pixs.Width - 1) ? 1 : 0;
    }

    /*---------------------------------------------------------------------*
     *                             Text splitting                          *
     *---------------------------------------------------------------------*/
    // leptonica: splitStringToParagraphs (textops.c)
    /// <summary>Splits a text string into paragraphs.</summary>
    /// <param name="textstr">text string</param>
    /// <param name="splitflag">SPLIT_ON_LEADING_WHITE, SPLIT_ON_BLANK_LINE, SPLIT_ON_BOTH</param>
    /// <returns>sarray where each string is a paragraph of the input</returns>
    /// <remarks>
    /// (1) Each paragraph is a string, with the newlines of its lines kept.
    /// </remarks>
    public static Sarray SplitStringToParagraphs(string textstr, int splitflag)
    {
        Sarray salines = Sarray.CreateLinesFromString(textstr, 1);
        int nlines = salines.Count;
        Sarray saout = Sarray.Create(0);
        Sarray satemp = Sarray.Create(0);
        string linestr = salines.GetString(0, L.L_NOCOPY);
        satemp.AddString(linestr, L.L_COPY);
        for (int i = 1; i < nlines; i++) {
            linestr = salines.GetString(i, L.L_NOCOPY);
            stringAllWhitespace(linestr, out int allwhite);
            stringLeadingWhitespace(linestr, out int leadwhite);
            if ((splitflag == L.SPLIT_ON_LEADING_WHITE && leadwhite != 0) ||
                (splitflag == L.SPLIT_ON_BLANK_LINE && allwhite != 0) ||
                (splitflag == L.SPLIT_ON_BOTH && (allwhite != 0 || leadwhite != 0))) {
                string parastring = satemp.ToString(1);  /* add nl to each line */
                saout.AddString(parastring, L.L_INSERT);
                satemp = Sarray.Create(0);
            }
            satemp.AddString(linestr, L.L_COPY);
        }
        string parastring2 = satemp.ToString(1);  /* add nl to each line */
        saout.AddString(parastring2, L.L_INSERT);

        return saout;
    }

    // leptonica: stringAllWhitespace (textops.c)
    private static void stringAllWhitespace(string textstr, out int pval)
    {
        int len = textstr.Length;
        pval = 1;
        for (int i = 0; i < len; i++) {
            if (textstr[i] != ' ' && textstr[i] != '\t' && textstr[i] != '\n') {
                pval = 0;
                return;
            }
        }
    }

    // leptonica: stringLeadingWhitespace (textops.c)
    private static void stringLeadingWhitespace(string textstr, out int pval)
    {
        pval = 0;
        if (textstr.Length > 0 && (textstr[0] == ' ' || textstr[0] == '\t'))
            pval = 1;
    }
}

public sealed partial class Pixa
{
    // leptonica: pixaAddTextNumber (textops.c)
    /// <summary>Adds a number label to each pix of a pixa, in a position given by location.</summary>
    /// <param name="bmf">bitmap font data</param>
    /// <param name="na">[optional] number array; can be null</param>
    /// <param name="val">color to set the text</param>
    /// <param name="location">L_ADD_ABOVE, L_ADD_BELOW, L_ADD_LEFT, L_ADD_RIGHT</param>
    /// <returns>pixad new pixa with rendered numbers</returns>
    /// <remarks>
    /// (1) Typical usage is for labelling each pix in a pixa with a number.
    /// (2) This function paints numbers external to each pix, in a position
    ///     given by location.  In all cases, the pix is expanded on
    ///     on side and the number is painted over white in the added region.
    /// (3) val is the pixel value to be painted through the font mask.
    ///     It should be chosen to agree with the depth of pixs.
    ///     If it is out of bounds, an intermediate value is chosen.
    ///     For RGB, use hex notation: 0xRRGGBB00, where RR is the
    ///     hex representation of the red intensity, etc.
    /// (4) If na == NULL, number each pix sequentially, starting with 1.
    /// (5) If there is a colormap, this does the best it can to use
    ///     the requested color, or something similar to it.
    /// </remarks>
    public Pixa AddTextNumber(L_Bmf bmf, Numa? na, uint val, int location)
    {
        if (location != L.L_ADD_ABOVE && location != L.L_ADD_BELOW &&
            location != L.L_ADD_LEFT && location != L.L_ADD_RIGHT)
            throw Lept.Arg("pixaAddTextNumber", "invalid location");

        int n = Count;
        Pixa pixad = Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = GetPix(i, L.L_CLONE);
            int index;
            if (na != null)
                index = na.GetIValue(i);
            else
                index = i + 1;
            string textstr = index.ToString(CultureInfo.InvariantCulture);
            Pix pix2 = pix1.AddTextlines(bmf, textstr, val, location);
            pixad.AddPix(pix2, L.L_INSERT);
        }

        return pixad;
    }

    // leptonica: pixaAddTextlines (textops.c)
    /// <summary>Adds one or more lines of text externally to each pix.</summary>
    /// <param name="bmf">bitmap font data</param>
    /// <param name="sa">[optional] sarray; use text embedded in each pix if null</param>
    /// <param name="val">color to set the text</param>
    /// <param name="location">L_ADD_ABOVE, L_ADD_BELOW, L_ADD_LEFT, L_ADD_RIGHT</param>
    /// <returns>pixad new pixa with rendered text</returns>
    /// <remarks>
    /// (1) This function adds one or more lines of text externally to
    ///     each pix, in a position given by location.  In all cases,
    ///     the pix is expanded as necessary to accommodate the text.
    /// (2) val is the pixel value to be painted through the font mask.
    ///     It should be chosen to agree with the depth of pixs.
    ///     If it is out of bounds, an intermediate value is chosen.
    ///     For RGB, use hex notation: 0xRRGGBB00, where RR is the
    ///     hex representation of the red intensity, etc.
    /// (3) If sa == NULL, use the text embedded in each pix.  In all
    ///     cases, newlines in the text string are used to separate the
    ///     lines of text that are added to the pix.
    /// (4) If sa has a smaller count than pixa, issue a warning
    ///     and do not use any embedded text.
    /// (5) If there is a colormap, this does the best it can to use
    ///     the requested color, or something similar to it.
    /// </remarks>
    public Pixa AddTextlines(L_Bmf bmf, Sarray? sa, uint val, int location)
    {
        const string func = "pixaAddTextlines";

        if (location != L.L_ADD_ABOVE && location != L.L_ADD_BELOW &&
            location != L.L_ADD_LEFT && location != L.L_ADD_RIGHT)
            throw Lept.Arg(func, "invalid location");

        int n = Count;
        Pixa pixad = Create(n);
        int nstr = (sa != null) ? sa.Count : 0;
        if (nstr > 0 && nstr < n)
            Log.Warning(func, $"There are {nstr} strings and {n} pix");
        for (int i = 0; i < n; i++) {
            Pix pix1 = GetPix(i, L.L_CLONE);
            string? textstr;
            if (i < nstr)
                textstr = sa!.GetString(i, L.L_NOCOPY);
            else
                textstr = pix1.Text;
            Pix pix2 = pix1.AddTextlines(bmf, textstr, val, location);
            pixad.AddPix(pix2, L.L_INSERT);
        }

        return pixad;
    }

    // leptonica: pixaAddPixWithText (textops.c)
    /// <summary>Generates a new pix with added text, and adds it by insertion into the pixa.</summary>
    /// <param name="pixs">any depth, can be cmapped</param>
    /// <param name="reduction">integer subsampling factor</param>
    /// <param name="bmf">[optional] bitmap font data; if null, an 8 pt font is generated (but see Notes)</param>
    /// <param name="textstr">[optional] text string to be added; if null, uses the text field of the pix</param>
    /// <param name="val">color to set the text</param>
    /// <param name="location">L_ADD_ABOVE, L_ADD_BELOW, L_ADD_LEFT, L_ADD_RIGHT</param>
    /// <remarks>
    /// (1) This function generates a new pix with added text, and adds
    ///     it by insertion into the pixa.
    /// (2) If the input pixs is not cmapped and not 32 bpp, it is
    ///     converted to 32 bpp rgb.  val is a standard 32 bpp pixel,
    ///     expressed as 0xrrggbb00.  If there is a colormap, this does
    ///     the best it can to use the requested color, or something close.
    /// (3) if bmf == NULL, generate an 8 pt font; this takes about 5 msec.
    /// (4) If textstr == NULL, use the text field in the pix.
    /// (5) In general, the text string can be written in multiple lines;
    ///     use newlines as the separators.
    /// (6) Typical usage is for debugging, where the pixa of labeled images
    ///     is used to generate a pdf.  Suggest using 1.0 for scalefactor.
    /// </remarks>
    public void AddPixWithText(Pix pixs, int reduction, L_Bmf? bmf, string? textstr, uint val, int location)
    {
        const string func = "pixaAddPixWithText";

        if (location != L.L_ADD_ABOVE && location != L.L_ADD_BELOW &&
            location != L.L_ADD_LEFT && location != L.L_ADD_RIGHT)
            throw Lept.Arg(func, "invalid location");
        if (textstr == null) {
            textstr = pixs.Text;
            if (textstr == null) {
                Log.Warning(func, "no textstring defined; inserting copy");
                AddPix(pixs, L.L_COPY);
                return;
            }
        }

        /* Default font size is 8. */
        L_Bmf? bmf8 = (bmf != null) ? bmf : L_Bmf.Create(null, 8);
        Pix pix1;
        if (reduction != 1)
            pix1 = pixs.ScaleByIntSampling(reduction);
        else
            pix1 = Pix.Clone(pixs);

        /* We want the text to be rendered in color.  This works
         * automatically if pixs is cmapped or 32 bpp rgb; otherwise,
         * we need to convert to rgb. */
        PixColormap? cmap = pix1.Colormap;
        int d = pix1.Depth;
        Pix pix2;
        if (cmap == null && d != 32)
            pix2 = pix1.ConvertTo32();
        else
            pix2 = Pix.Clone(pix1);
        // NOTE(upstream): the font that is used is bmf, not bmf8, so a null bmf gives a copy
        Pix pix3 = pix2.AddTextlines(bmf, textstr, val, location);
        AddPix(pix3, L.L_INSERT);
    }
}

public sealed partial class L_Bmf
{
    /*---------------------------------------------------------------------*
     *                   Text size estimation and partitioning             *
     *---------------------------------------------------------------------*/
    // leptonica: bmfGetLineStrings (textops.c)
    /// <summary>Divides the input text string into an array of text strings, each of which fits within maxw.</summary>
    /// <param name="textstr">text string to be divided into lines</param>
    /// <param name="maxw">max width of a line, in pixels</param>
    /// <param name="firstindent">indentation of first line, in x-widths</param>
    /// <param name="ph">height required to hold text bitmap</param>
    /// <returns>sarray of text strings for each line</returns>
    /// <remarks>
    /// (1) Divides the input text string into an array of text strings,
    ///     each of which will fit within maxw bits of width.
    /// </remarks>
    public Sarray GetLineStrings(string textstr, int maxw, int firstindent, out int ph)
    {
        const string func = "bmfGetLineStrings";
        L_Bmf bmf = this;

        ph = 0;
        Sarray sawords = Sarray.CreateWordsFromString(textstr);
        Numa na = bmf.GetWordWidths(textstr, sawords);
        int nwords = na.Count;
        if (nwords == 0)
            throw Lept.Arg(func, "no words in textstr");

        int xwidth = bmf.GetWidth('x');
        Sarray sa = Sarray.Create(0);

        int ifirst = 0;
        int w = na.GetIValue(0);
        int sumw = firstindent * xwidth + w;
        for (int i = 1; i < nwords; i++) {
            w = na.GetIValue(i);
            int newsum = sumw + bmf.SpaceWidth + w;
            if (newsum > maxw) {
                string linestr = sawords.ToStringRange(ifirst, i - ifirst, 2);
                if (linestr == null)
                    continue;
                int len = linestr.Length;
                if (len > 0)  /* it should always be */
                    linestr = linestr.Substring(0, len - 1);  /* remove the last space */
                sa.AddString(linestr, L.L_INSERT);
                ifirst = i;
                sumw = w;
            }
            else
                sumw += bmf.SpaceWidth + w;
        }
        string linestr2 = sawords.ToStringRange(ifirst, nwords - ifirst, 2);
        if (linestr2 != null)
            sa.AddString(linestr2, L.L_INSERT);

        int nlines = sa.Count;
        ph = nlines * bmf.LineHeight + (nlines - 1) * bmf.VertLineSep;

        return sa;
    }

    // leptonica: bmfGetWordWidths (textops.c)
    /// <summary>Gets the width in pixels of each word of a text string, for this font.</summary>
    /// <param name="textstr">text string</param>
    /// <param name="sa">sarray of the words in the string</param>
    /// <returns>numa of word lengths in pixels for the font represented by the bmf</returns>
    public Numa GetWordWidths(string textstr, Sarray sa)
    {
        int nwords = sa.Count;
        Numa na = Numa.Create(nwords);
        for (int i = 0; i < nwords; i++) {
            string wordstr = sa.GetString(i, L.L_NOCOPY);
            int width = GetStringWidth(wordstr);
            na.AddNumber(width);
        }

        return na;
    }

    // leptonica: bmfGetStringWidth (textops.c)
    /// <summary>Gets the width in pixels of a text string, for this font.</summary>
    /// <param name="textstr">text string</param>
    /// <returns>width of text string, in pixels for the font represented by the bmf</returns>
    public int GetStringWidth(string textstr)
    {
        int nchar = textstr.Length;
        int w = 0;
        for (int i = 0; i < nchar; i++) {
            char chr = textstr[i];
            int width = GetWidth(chr);
            if (width != L.UNDEF)
                w += width + KernWidth;
        }
        w -= KernWidth;  /* remove last one */
        return w;
    }
}
