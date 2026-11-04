using System;

namespace Leptonica;

/*
 *  bmf.c: Acquisition and generation of bitmap fonts.
 *
 *      L_Bmf.Create
 *
 *      L_Bmf.GetPix, GetWidth, GetBaseline
 *
 *      Pixa.GetFont, Pixa.SaveFont
 *      static: Pixa generation from a file, from a string, and from an image,
 *              the text baseline, and the ascii tables
 *
 *  This is not a very general utility, because it only uses bitmap
 *  representations of a single font, Palatino-Roman, with the
 *  normal style.  It uses bitmaps generated for nine sizes, from
 *  4 to 20 pts, rendered at 300 ppi.  Generalization to different
 *  fonts, styles and sizes is straightforward.
 *
 *  I chose Palatino-Roman is because I like it.
 *  The input font images were generated from a set of small
 *  PostScript files, such as chars-12.ps, which were rendered
 *  into the inputfont[] bitmap files using GhostScript.  See, for
 *  example, the bash script prog/ps2tiff, which will "rip" a
 *  PostScript file into a set of ccitt-g4 compressed tiff files.
 *
 *  The set of ascii characters from 32 through 126 are the 95
 *  printable ascii chars.  Palatino-Roman is missing char 92, '\',
 *  I have substituted an LR flip of '/', char 47, for 92, so that
 *  there are no missing printable chars in this set.  The space is
 *  char 32, and I have given it a width equal to twice the width of '!'.
 *
 *  Notes on the port:
 *    - bmfDestroy() is not ported (the GC owns the objects).
 *    - The font data of bmfdata.h is in BmfData.cs; the images there are png,
 *      not tiff (see the note in that file).
 *    - bmfGetPix(), bmfGetWidth() and bmfGetBaseline() are methods; the width and
 *      the baseline are returned (-1 and 0 for the newline, as the C output values).
 */

/// <summary>Constants for deciding when text block is divided into paragraphs (bmf.h).</summary>
public static partial class L
{
    /// <summary>Start a new paragraph at a line that begins with a tab or space.</summary>
    public const int SPLIT_ON_LEADING_WHITE = 1;
    /// <summary>Start a new paragraph after a blank line (a newline with optional white space).</summary>
    public const int SPLIT_ON_BLANK_LINE = 2;
    /// <summary>Start a new paragraph on either leading white space or a blank line.</summary>
    public const int SPLIT_ON_BOTH = 3;
}

/// <summary>Bitmap font (struct L_Bmf).</summary>
public sealed partial class L_Bmf
{
    private const float VertFractSep = 0.3f;

    /// <summary>pixa of bitmaps for 95 characters.</summary>
    public Pixa Pixa { get; private set; } = null!;
    /// <summary>font size (in points at 300 ppi).</summary>
    public int Size { get; private set; }
    /// <summary>directory containing font bitmaps.</summary>
    public string? Directory { get; private set; }
    /// <summary>baseline offset for ascii 33 - 57.</summary>
    public int Baseline1 { get; private set; }
    /// <summary>baseline offset for ascii 58 - 91.</summary>
    public int Baseline2 { get; private set; }
    /// <summary>baseline offset for ascii 93 - 126.</summary>
    public int Baseline3 { get; private set; }
    /// <summary>max height of line of chars.</summary>
    public int LineHeight { get; private set; }
    /// <summary>pixel dist between char bitmaps.</summary>
    public int KernWidth { get; private set; }
    /// <summary>pixel dist between word bitmaps.</summary>
    public int SpaceWidth { get; private set; }
    /// <summary>extra vertical space between text lines.</summary>
    public int VertLineSep { get; private set; }
    /// <summary>table mapping ascii --&gt; font index.</summary>
    public int[] FontTab { get; private set; } = Array.Empty<int>();
    /// <summary>table mapping ascii --&gt; baseline offset.</summary>
    public int[] BaselineTab { get; private set; } = Array.Empty<int>();

    private L_Bmf() { }

    /*---------------------------------------------------------------------*/
    /*                           Bmf create/destroy                        */
    /*---------------------------------------------------------------------*/
    // leptonica: bmfCreate (bmf.c)
    /// <summary>Creates a bitmap font.</summary>
    /// <param name="dir">[optional] directory holding pixa of character set</param>
    /// <param name="fontsize">4, 6, 8, ... , 20</param>
    /// <returns>bmf holding the bitmap font and associated information</returns>
    /// <remarks>
    /// (1) If %dir == null, this generates the font bitmaps from a
    ///     compiled string.
    /// (2) Otherwise, this tries to read a pre-computed pixa file with the
    ///     95 ascii chars in it.  If the file is not found, it then
    ///     attempts to generate the pixa and associated baseline
    ///     data from a tiff image containing all the characters.  If
    ///     that fails, it uses the compiled string.
    /// </remarks>
    public static L_Bmf Create(string? dir, int fontsize)
    {
        const string func = "bmfCreate";
        L_Bmf bmf;
        Pixa? pixa;
        int bl1, bl2, bl3;

        if (fontsize < 4 || fontsize > 20 || (fontsize % 2) != 0)
            throw Lept.Arg(func, "fontsize must be in {4, 6, ..., 20}");

        bmf = new L_Bmf();

        if (dir == null) {  /* Generate from a string */
            pixa = PixaGenerateFontFromString(fontsize, out bl1, out bl2, out bl3);
        } else {  /* Look for the pixa in a directory */
            pixa = Pixa.GetFont(dir, fontsize, out bl1, out bl2, out bl3);
            if (pixa == null) {  /* Not found; make it from a file */
                Log.Info(func, "Generating pixa of bitmap fonts from file");
                pixa = PixaGenerateFontFromFile(dir, fontsize, out bl1, out bl2, out bl3);
                if (pixa == null) {  /* Not made; make it from a string after all */
                    Log.Error(func, "Failed to make font; use string");
                    pixa = PixaGenerateFontFromString(fontsize, out bl1, out bl2, out bl3);
                }
            }
        }
        bmf.Baseline1 = bl1;
        bmf.Baseline2 = bl2;
        bmf.Baseline3 = bl3;

        if (pixa == null)
            throw Lept.Fail(func, "font pixa not made");

        bmf.Pixa = pixa;
        bmf.Size = fontsize;
        if (dir != null) bmf.Directory = Utils.StringNew(dir);
        bmf.MakeAsciiTables();
        return bmf;
    }

    /*---------------------------------------------------------------------*/
    /*                             Bmf accessors                           */
    /*---------------------------------------------------------------------*/
    // leptonica: bmfGetPix (bmf.c)
    /// <summary>Returns the bitmap of a character.</summary>
    /// <param name="chr">should be one of the 95 supported printable bitmaps</param>
    /// <returns>pix clone of pix in bmf; null for the newline or for a character without a bitmap</returns>
    public Pix? GetPix(char chr)
    {
        const string func = "bmfGetPix";
        int i, index;

        if ((index = (int)chr) == 10)  /* NL */
            return null;
        if (index < 32 || index >= 127)
            throw Lept.Arg(func, "invalid index");

        i = FontTab[index];
        if (i == L.UNDEF) {
            Log.Error(func, $"no bitmap representation for {index}");
            return null;
        }

        return Pixa.GetPix(i, L.L_CLONE);
    }

    // leptonica: bmfGetWidth (bmf.c)
    /// <summary>Returns the width of a character.</summary>
    /// <param name="chr">should be one of the 95 supported bitmaps</param>
    /// <returns>character width; -1 if not printable</returns>
    public int GetWidth(char chr)
    {
        const string func = "bmfGetWidth";
        int i, index;

        if ((index = (int)chr) == 10)  /* NL */
            return -1;
        if (index < 32 || index >= 127)
            throw Lept.Arg(func, "invalid index");

        i = FontTab[index];
        if (i == L.UNDEF) {
            Log.Error(func, $"no bitmap representation for {index}");
            return -1;
        }

        Pixa.GetPixDimensions(i, out int w, out _, out _);
        return w;
    }

    // leptonica: bmfGetBaseline (bmf.c)
    /// <summary>Returns the baseline of a character.</summary>
    /// <param name="chr">should be one of the 95 supported bitmaps</param>
    /// <returns>baseline distance below UL corner of bitmap char; 0 for the newline</returns>
    public int GetBaseline(char chr)
    {
        const string func = "bmfGetBaseline";
        int bl, index;

        if ((index = (int)chr) == 10)  /* NL */
            return 0;
        if (index < 32 || index >= 127)
            throw Lept.Arg(func, "invalid index");

        bl = BaselineTab[index];
        if (bl == L.UNDEF) {
            Log.Error(func, $"no bitmap representation for {index}");
            return 0;
        }

        return bl;
    }

    /*---------------------------------------------------------------------*/
    /*               Font bitmap acquisition and generation                */
    /*---------------------------------------------------------------------*/
    /*
     *  These font generation functions use 9 sets, each with bitmaps
     *  of 94 ascii characters, all in Palatino-Roman font.
     *  Each input bitmap has 3 rows of characters.  The range of
     *  ascii values in each row is as follows:
     *    row 0:  32-57   32 is a space
     *    row 1:  58-91   92, '\', is not represented in this font
     *    row 2:  93-126
     *  We LR flip the '/' char to generate a bitmap for the missing
     *  '\' character, so that we have representations of all 95
     *  printable chars.
     *
     *  Typically, use pixaGetFont() to generate the character bitmaps
     *  in memory for a bmf.  This will simply access the bitmap files
     *  in a serialized pixa that were produced in prog/genfonts.c using
     *  pixaSaveFont().
     */
    // leptonica: pixaGenerateFontFromFile (bmf.c)
    /// <summary>Generates the font pixa from the image of the character set in a directory.</summary>
    /// <param name="dir">directory holding image of character set</param>
    /// <param name="fontsize">4, 6, 8, ... , 20, in pts at 300 ppi</param>
    /// <param name="pbl0">baseline of row 1</param>
    /// <param name="pbl1">baseline of row 2</param>
    /// <param name="pbl2">baseline of row 3</param>
    /// <returns>pixa of font bitmaps for 95 characters; null if the image or the characters are not found</returns>
    internal static Pixa? PixaGenerateFontFromFile(string dir, int fontsize,
                                                   out int pbl0, out int pbl1, out int pbl2)
    {
        const string func = "pixaGenerateFontFromFile";
        int fileno;
        Pix pix;

        pbl0 = pbl1 = pbl2 = 0;
        fileno = (fontsize / 2) - 2;
        if (fileno < 0 || fileno >= BmfData.NUM_FONTS)
            throw Lept.Arg(func, "font size invalid");

        string pathname = Utils.PathJoin(dir, BmfData.InputFonts[fileno]);
        try {
            pix = Pix.Read(pathname);
        } catch (Exception e) when (e is ArgumentException || e is LeptonicaException) {
            Log.Error(func, $"pix not found for font size {fontsize}");
            return null;
        }

        try {
            return PixaGenerateFont(pix, fontsize, out pbl0, out pbl1, out pbl2);
        } catch (Exception e) when (e is ArgumentException || e is LeptonicaException) {
            return null;
        }
    }

    // leptonica: pixaGenerateFontFromString (bmf.c)
    /// <summary>Generates the font pixa from the compiled font data.</summary>
    /// <param name="fontsize">4, 6, 8, ... , 20, in pts at 300 ppi</param>
    /// <param name="pbl0">baseline of row 1</param>
    /// <param name="pbl1">baseline of row 2</param>
    /// <param name="pbl2">baseline of row 3</param>
    /// <returns>pixa of font bitmaps for 95 characters</returns>
    /// <remarks>
    /// (1) See pixaGenerateFontFromFile() for details.
    /// </remarks>
    internal static Pixa PixaGenerateFontFromString(int fontsize,
                                                    out int pbl0, out int pbl1, out int pbl2)
    {
        const string func = "pixaGenerateFontFromString";
        int redsize;

        pbl0 = pbl1 = pbl2 = 0;
        redsize = (fontsize / 2) - 2;
        if (redsize < 0 || redsize >= BmfData.NUM_FONTS)
            throw Lept.Arg(func, "invalid font size");

        byte[] data = LeptEncoding.DecodeBase64(BmfData.FontData(fontsize));

        // TODO(tiffio.c): the data is a png image until pixReadMem() reads tiff
        Pix pix = Pix.ReadMemPng(data);

        return PixaGenerateFont(pix, fontsize, out pbl0, out pbl1, out pbl2);
    }

    // leptonica: pixaGenerateFont (bmf.c)
    /// <summary>Cuts the character images out of the image of the character set.</summary>
    /// <param name="pixs">of 95 characters in 3 rows</param>
    /// <param name="fontsize">4, 6, 8, ... , 20, in pts at 300 ppi</param>
    /// <param name="pbl0">baseline of row 1</param>
    /// <param name="pbl1">baseline of row 2</param>
    /// <param name="pbl2">baseline of row 3</param>
    /// <returns>pixa of font bitmaps for 95 characters</returns>
    /// <remarks>
    /// (1) This does all the work.  See pixaGenerateFontFromFile()
    ///     for an overview.
    /// (2) The pix is for one of the 9 fonts.  %fontsize is only
    ///     used here for debugging.
    /// </remarks>
    private static Pixa PixaGenerateFont(Pix pixs, int fontsize,
                                         out int pbl0, out int pbl1, out int pbl2)
    {
        const string func = "pixaGenerateFont";
        int i, j, nrows, nrowchars, nchars, h, yval;
        int width, height;
        int[] baseline = new int[3];
        int[] tab;
        Box box, box1, box2;
        Boxa boxar, boxac, boxacs;
        Pix pix1, pix2, pixr, pixrc, pixc;
        Pixa pixa;
        int n, w, inrow, top;
        int[] ia;
        Numa na;

        pbl0 = pbl1 = pbl2 = 0;

            /* Locate the 3 rows of characters */
        w = pixs.Width;
        na = pixs.CountPixelsByRow(null);
        boxar = Boxa.Create(0);
        n = na.Count;
        ia = na.GetIArray();
        inrow = 0;
        top = 0;
        for (i = 0; i < n; i++) {
            if (inrow == 0 && ia[i] > 0) {
                inrow = 1;
                top = i;
            } else if (inrow != 0 && ia[i] == 0) {
                inrow = 0;
                box = Box.Create(0, top, w, i - top);
                boxar.AddBox(box, L.L_INSERT);
            }
        }
        nrows = boxar.Count;
        if (nrows != 3) {
            Log.Info(func, $"nrows = {nrows}; skipping fontsize {fontsize}");
            throw Lept.Fail(func, "3 rows not generated");
        }

            /* Grab the character images and baseline data */
        tab = Pix.MakePixelSumTab8();
        pixa = Pixa.Create(95);
        for (i = 0; i < nrows; i++) {
            box = boxar.GetBox(i, L.L_CLONE);
            pixr = pixs.ClipRectangle(box, out _)!;  /* row of chars */
            GetTextBaseline(pixr, tab, out yval);
            baseline[i] = yval;

            pixrc = Pix.CloseSafeBrick(null, pixr, 1, 35);
            boxac = pixrc.ConnComp(8);
            boxacs = boxac.Sort(L.L_SORT_BY_X, L.L_SORT_INCREASING, out _);
            if (i == 0) {  /* consolidate the two components of '"' */
                box1 = boxacs.GetBox(1, L.L_CLONE);
                box2 = boxacs.GetBox(2, L.L_CLONE);
                box1.Width = box2.X + box2.Width - box1.X;  /* increase width */
                boxacs.RemoveBox(2);
            }
            h = pixr.Height;
            nrowchars = boxacs.Count;
            for (j = 0; j < nrowchars; j++) {
                box = boxacs.GetBox(j, L.L_COPY);
                if (box.Width <= 2 && box.Height == 1)  /* skip 1x1, 2x1 components */
                    continue;
                box.Y = 0;
                box.Height = h - 1;
                pixc = pixr.ClipRectangle(box, out _)!;
                if (i == 0 && j == 0)  /* add a pix for the space; change later */
                    pixa.AddPix(pixc, L.L_COPY);
                if (i == 2 && j == 0)  /* add a pix for the '\'; change later */
                    pixa.AddPix(pixc, L.L_COPY);
                pixa.AddPix(pixc, L.L_INSERT);
            }
        }

        nchars = pixa.Count;
        if (nchars != 95)
            throw Lept.Fail(func, "95 chars not generated");

        pbl0 = baseline[0];
        pbl1 = baseline[1];
        pbl2 = baseline[2];

            /* Fix the space character up; it should have no ON pixels,
             * and be about twice as wide as the '!' character.    */
        pix1 = pixa.GetPix(0, L.L_CLONE);
        width = 2 * pix1.Width;
        height = pix1.Height;
        pix1 = Pix.Create(width, height, 1);
        pixa.ReplacePix(0, pix1, null);

            /* Fix up the '\' character; use a LR flip of the '/' char */
        pix1 = pixa.GetPix(15, L.L_CLONE);
        pix2 = Pix.FlipLR(null, pix1);
        pixa.ReplacePix(60, pix2, null);

        return pixa;
    }

    // leptonica: pixGetTextBaseline (bmf.c)
    /// <summary>Finds the baseline of a text line.</summary>
    /// <param name="pixs">1 bpp, one textline character set</param>
    /// <param name="tab8">[optional] pixel sum table</param>
    /// <param name="py">baseline value</param>
    /// <remarks>
    /// (1) Method: find the largest difference in pixel sums from one
    ///     raster line to the next one below it.  The baseline is the
    ///     upper raster line for the pair of raster lines that
    ///     maximizes this function.
    /// </remarks>
    private static void GetTextBaseline(Pix pixs, int[]? tab8, out int py)
    {
        int i, h, val1, val2, diff, diffmax, ymax;
        Numa na;

        na = pixs.CountPixelsByRow(tab8);
        h = na.Count;
        diffmax = 0;
        ymax = 0;
        for (i = 1; i < h; i++) {
            val1 = na.GetIValue(i - 1);
            val2 = na.GetIValue(i);
            diff = Math.Max(0, val1 - val2);
            if (diff > diffmax) {
                diffmax = diff;
                ymax = i - 1;  /* upper raster line */
            }
        }
        py = ymax;
    }

    // leptonica: bmfMakeAsciiTables (bmf.c)
    /// <summary>Makes the tables mapping ascii to font index and to baseline offset.</summary>
    /// <remarks>
    /// (1) This makes two tables, each of size 128, as follows:
    ///     ~ fonttab is a table containing the index of the Pix
    ///       that corresponds to each input ascii character;
    ///       it maps (ascii-index) --&gt; Pixa index
    ///     ~ baselinetab is a table containing the baseline offset
    ///       for the Pix that corresponds to each input ascii character;
    ///       it maps (ascii-index) --&gt; baseline offset
    /// (2) This also computes
    ///     ~ lineheight (sum of maximum character extensions above and
    ///                   below the baseline)
    ///     ~ kernwidth (spacing between characters within a word)
    ///     ~ spacewidth (space between words)
    ///     ~ vertlinesep (extra vertical spacing between textlines)
    /// (3) The baselines apply as follows:
    ///     baseline1   (ascii 32 - 57), ascii 92
    ///     baseline2   (ascii 58 - 91)
    ///     baseline3   (ascii 93 - 126)
    /// (4) The only array in bmf that is not ascii-based is the
    ///     array of bitmaps in the pixa, which starts at ascii 32.
    /// </remarks>
    private void MakeAsciiTables()
    {
        int i, maxh, height, charwidth, xwidth, kernwidth;
        int[] fonttab, baselinetab;
        Pix? pix;

            /* First get the fonttab; we use this later for the char widths */
        fonttab = new int[128];
        FontTab = fonttab;
        for (i = 0; i < 128; i++)
            fonttab[i] = L.UNDEF;
        for (i = 32; i < 127; i++)
            fonttab[i] = i - 32;

        baselinetab = new int[128];
        BaselineTab = baselinetab;
        for (i = 0; i < 128; i++)
            baselinetab[i] = L.UNDEF;
        for (i = 32; i <= 57; i++)
            baselinetab[i] = Baseline1;
        for (i = 58; i <= 91; i++)
            baselinetab[i] = Baseline2;
        baselinetab[92] = Baseline1;  /* the '\' char */
        for (i = 93; i < 127; i++)
            baselinetab[i] = Baseline3;

            /* Get the line height of text characters, from the highest
             * ascender to the lowest descender; req's fonttab to exist. */
        pix = GetPix((char)32);
        maxh = pix!.Height;
        pix = GetPix((char)58);
        height = pix!.Height;
        maxh = Math.Max(maxh, height);
        pix = GetPix((char)93);
        height = pix!.Height;
        maxh = Math.Max(maxh, height);
        LineHeight = maxh;

            /* Get the kern width (distance between characters).
             * We let it be the same for all characters in a given
             * font size, and scale it linearly with the size;
             * req's fonttab to be built first. */
        xwidth = GetWidth((char)120);
        kernwidth = (int)(0.08 * (float)xwidth + 0.5);
        KernWidth = Math.Max(1, kernwidth);

            /* Save the space width (between words) */
        charwidth = GetWidth((char)32);
        SpaceWidth = charwidth;

            /* Save the extra vertical space between lines */
        VertLineSep = (int)(VertFractSep * LineHeight + 0.5);
    }
}

public sealed partial class Pixa
{
    // leptonica: pixaGetFont (bmf.c)
    /// <summary>Reads the pre-computed pixa of the 95 ascii characters of a font size.</summary>
    /// <param name="dir">directory holding pixa of character set</param>
    /// <param name="fontsize">4, 6, 8, ... , 20</param>
    /// <param name="pbl0">baseline of row 1</param>
    /// <param name="pbl1">baseline of row 2</param>
    /// <param name="pbl2">baseline of row 3</param>
    /// <returns>pixa of font bitmaps for 95 characters; null if the file is not found</returns>
    /// <remarks>
    /// (1) This reads a pre-computed pixa file with the 95 ascii chars.
    /// </remarks>
    public static Pixa? GetFont(string dir, int fontsize, out int pbl0, out int pbl1, out int pbl2)
    {
        const string func = "pixaGetFont";
        int fileno;

        fileno = (fontsize / 2) - 2;
        if (fileno < 0 || fileno >= BmfData.NUM_FONTS)
            throw Lept.Arg(func, "font size invalid");
        pbl0 = BmfData.Baselines[fileno, 0];
        pbl1 = BmfData.Baselines[fileno, 1];
        pbl2 = BmfData.Baselines[fileno, 2];

        string pathname = Utils.PathJoin(dir, BmfData.OutputFonts[fileno]);
        try {
            return Read(pathname);
        } catch (Exception e) when (e is ArgumentException || e is LeptonicaException) {
            Log.Warning(func, "pixa of char bitmaps not found");
            return null;
        }
    }

    // leptonica: pixaSaveFont (bmf.c)
    /// <summary>Saves the pixa of the 95 ascii characters of a font size.</summary>
    /// <param name="indir">[optional] directory holding image of character set</param>
    /// <param name="outdir">directory into which the output pixa file will be written</param>
    /// <param name="fontsize">in pts, at 300 ppi</param>
    /// <remarks>
    /// (1) This saves a font of a particular size.
    /// (2) If %indir == null, this generates the font bitmaps from a
    ///     compiled string.
    /// (3) prog/genfonts calls this function for each of the
    ///     nine font sizes, to generate all the font pixa files.
    /// </remarks>
    public static void SaveFont(string? indir, string outdir, int fontsize)
    {
        const string func = "pixaSaveFont";
        Pixa? pixa;
        int bl1, bl2, bl3;

        if (fontsize < 4 || fontsize > 20 || (fontsize % 2) != 0)
            throw Lept.Arg(func, "fontsize must be in {4, 6, ..., 20}");

        if (indir == null)  /* Generate from a string */
            pixa = L_Bmf.PixaGenerateFontFromString(fontsize, out bl1, out bl2, out bl3);
        else  /* Generate from an image file */
            pixa = L_Bmf.PixaGenerateFontFromFile(indir, fontsize, out bl1, out bl2, out bl3);
        if (pixa == null)
            throw Lept.Fail(func, "pixa not made");

        string pathname = Utils.PathJoin(outdir, BmfData.OutputFonts[(fontsize - 4) / 2]);
        pixa.Write(pathname);
    }
}
