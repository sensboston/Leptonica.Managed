using System;

namespace Leptonica;

/*
 *  boxfunc3.c: Boxa/Boxaa painting into Pix, splitting mask components into
 *  Boxa, mosaic strips, comparison of Boxa, selection of a large box.
 *
 *  Differences from C:
 *    - No reference counting and no boxDestroy(): the GC owns the objects.
 *    - Functions that return a Pix, Boxa or Pixa and signal failure with NULL
 *      throw instead.  pixSelectLargeULComp() returns null when the image has
 *      no components (not an error in C either).
 *    - The optional outputs of boxaCompareRegions() are covered by three
 *      overloads, because the C code skips the xor computation (and the debug
 *      image) when pdiffxor (ppixdb) is NULL.
 *    - pixSearchForRectangle() is static in C; here it is internal so that the
 *      tests can reach it.
 *    - pixcmapAddNewColor() keeps its status result (0, or 2 if the color
 *      could not be added), because pixPaintBoxa() tests it.
 *
 *  Stubs created for callees in files that are not ported yet:
 *    pixConnComp, pixConnCompBB (conncomp.c); pixConvertTo8, pixConvertTo32,
 *    pixConvert1To8 (pixconv.c); pixcmapAddNewColor, pixcmapAddBlackOrWhite,
 *    pixcmapCreateRandom (colormap.c); extractRGBValues, pixSetInRectArbitrary,
 *    pixClearInRect, pixSetInRect, pixBlendInRect (pix2.c); pixAnd, pixXor,
 *    pixPaintThroughMask, pixCountPixels, makePixelSumTab8 (pix3.c);
 *    pixClipBoxToForeground (pix5.c); pixRenderBoxArb, generatePtaaBoxa,
 *    pixRenderRandomCmapPtaa, pixRenderHashBoxaBlend (graphics.c);
 *    boxTransform, boxaTransform, boxaSort, boxaaGetExtent (boxfunc2.c);
 *    boxaSelectByArea (boxfunc4.c); numaGetSortIndex (numafunc1.c);
 *    pixaCreate, pixaGetCount, pixaGetPix (pixabasic.c);
 *    pixaDisplayTiledInRows (pixafunc2.c).
 */

/// <summary>Constants from pix.h used by the box painting functions.</summary>
public static partial class L
{
    /* Colors for drawing boxes (L_DRAW_*) */
    /// <summary>Draws in red.</summary>
    public const int L_DRAW_RED = 0;

    /// <summary>Draws in green.</summary>
    public const int L_DRAW_GREEN = 1;

    /// <summary>Draws in blue.</summary>
    public const int L_DRAW_BLUE = 2;

    /// <summary>Draws in a specified color.</summary>
    public const int L_DRAW_SPECIFIED = 3;

    /// <summary>Draws as a sequence of red, green and blue.</summary>
    public const int L_DRAW_RGB = 4;

    /// <summary>Draws in randomly chosen colors.</summary>
    public const int L_DRAW_RANDOM = 5;

    /* Pixel setting (L_*_PIXELS) */

    /// <summary>Sets all bits in each pixel to 1.</summary>
    public const int L_SET_PIXELS = 1;

    /// <summary>Sets all bits in each pixel to 0.</summary>
    public const int L_CLEAR_PIXELS = 2;

    /// <summary>Flips all bits in each pixel.</summary>
    public const int L_FLIP_PIXELS = 3;

    /* Size comparison */

    /// <summary>Saves the item if the value is greater than or equal to the threshold.</summary>
    public const int L_SELECT_IF_GTE = 4;

    /* Line orientation */

    /// <summary>A 45 degree line with positive slope.</summary>
    public const int L_POS_SLOPE_LINE = 1;

    /// <summary>A 45 degree line with negative slope.</summary>
    public const int L_NEG_SLOPE_LINE = 3;

    /* Scan direction */

    /// <summary>A horizontal scan; the direction along the line is unimportant.</summary>
    public const int L_SCAN_HORIZONTAL = 7;

    /// <summary>A vertical scan; the direction along the line is unimportant.</summary>
    public const int L_SCAN_VERTICAL = 8;
}

/*---------------------------------------------------------------------*
 *                     Boxa/Boxaa painting into Pix                    *
 *---------------------------------------------------------------------*/
public sealed partial class Pix
{
    // leptonica: pixMaskConnComp (boxfunc3.c)
    /// <summary>Generates a mask over the bounding boxes of the connected components.</summary>
    /// <param name="connectivity">4 or 8</param>
    /// <param name="pboxa">bounding boxes of c.c.</param>
    /// <returns>pixd 1 bpp mask over the c.c.</returns>
    /// <remarks>
    /// (1) This generates a mask image with ON pixels over the
    ///     b.b. of the c.c. in pixs.  If there are no ON pixels in pixs,
    ///     pixd will also have no ON pixels.
    /// </remarks>
    public Pix MaskConnComp(int connectivity, out Boxa? pboxa)
    {
        const string func = "pixMaskConnComp";

        pboxa = null;
        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not 4 or 8");

        Boxa boxa = ConnComp(out _, connectivity);
        Pix pixd = CreateTemplate(this);
        if (boxa.Count != 0)
            MaskBoxa(pixd, pixd, boxa, L.L_SET_PIXELS);
        pboxa = boxa;
        return pixd;
    }

    // leptonica: pixMaskBoxa (boxfunc3.c)
    /// <summary>Applies a set, clear or flip operation over the boxes.</summary>
    /// <param name="pixd">optional; may be null</param>
    /// <param name="pixs">any depth; not cmapped</param>
    /// <param name="boxa">of boxes, to paint</param>
    /// <param name="op">L_SET_PIXELS, L_CLEAR_PIXELS, L_FLIP_PIXELS</param>
    /// <returns>pixd with masking op over the boxes</returns>
    /// <remarks>
    /// (1) This can be used with:
    ///         pixd = null  (makes a new pixd)
    ///         pixd = pixs  (in-place)
    /// (2) If pixd == null, this first makes a copy of pixs, and then
    ///     bit-twiddles over the boxes.  Otherwise, it operates directly
    ///     on pixs.
    /// (3) This simple function is typically used with 1 bpp images.
    ///     It uses the 1-image rasterop function, rasteropUniLow(),
    ///     to set, clear or flip the pixels in pixd.
    /// (4) If you want to generate a 1 bpp mask of ON pixels from the boxes
    ///     in a Boxa, in a pix of size (w,h):
    ///         pix = pixCreate(w, h, 1);
    ///         pixMaskBoxa(pix, pix, boxa, L_SET_PIXELS);
    /// </remarks>
    public static Pix MaskBoxa(Pix? pixd, Pix pixs, Boxa boxa, int op)
    {
        const string func = "pixMaskBoxa";

        if (pixs.Colormap != null)
            throw Lept.Arg(func, "pixs is cmapped");
        if (pixd != null && pixd != pixs)
            throw Lept.Arg(func, "if pixd, must be in-place");
        if (op != L.L_SET_PIXELS && op != L.L_CLEAR_PIXELS && op != L.L_FLIP_PIXELS)
            throw Lept.Arg(func, "invalid op");

        pixd = Copy(pixd, pixs);
        int n;
        if ((n = boxa.Count) == 0) {
            Log.Warning(func, "no boxes to mask\n");
            return pixd;
        }

        for (int i = 0; i < n; i++) {
            Box box = boxa.GetBox(i, L.L_CLONE);
            box.GetGeometry(out int x, out int y, out int w, out int h);
            if (op == L.L_SET_PIXELS)
                pixd.Rasterop(x, y, w, h, L.PIX_SET, null, 0, 0);
            else if (op == L.L_CLEAR_PIXELS)
                pixd.Rasterop(x, y, w, h, L.PIX_CLR, null, 0, 0);
            else  /* op == L_FLIP_PIXELS */
                pixd.Rasterop(x, y, w, h, L.PIX_NOT(L.PIX_DST), null, 0, 0);
        }

        return pixd;
    }

    // leptonica: pixPaintBoxa (boxfunc3.c)
    /// <summary>Paints the boxes with a solid color.</summary>
    /// <param name="boxa">of boxes, to paint</param>
    /// <param name="val">rgba color to paint</param>
    /// <returns>pixd with painted boxes</returns>
    /// <remarks>
    /// (1) If pixs is 1 bpp or is colormapped, it is converted to 8 bpp
    ///     and the boxa is painted using a colormap; otherwise,
    ///     it is converted to 32 bpp rgb.
    /// (2) There are several ways to display a box on an image:
    ///       * Paint it as a solid color
    ///       * Draw the outline
    ///       * Blend the outline or region with the existing image
    ///     We provide painting and drawing here; blending is in blend.c.
    ///     When painting or drawing, the result can be either a
    ///     cmapped image or an rgb image.  The dest will be cmapped
    ///     if the src is either 1 bpp or has a cmap that is not full.
    ///     To force RGB output, use pixConvertTo8(pixs, FALSE)
    ///     before calling any of these paint and draw functions.
    /// </remarks>
    public Pix PaintBoxa(Boxa boxa, uint val)
    {
        const string func = "pixPaintBoxa";

        int n;
        if ((n = boxa.Count) == 0) {
            Log.Warning(func, "no boxes to paint; returning a copy\n");
            return Copy(null, this);
        }

        bool mapvacancy = false;   /* true only if cmap and not full */
        PixColormap? cmap;
        if ((cmap = Colormap) != null) {
            if (cmap.Count < 256)
                mapvacancy = true;
        }
        Pix pixd;
        if (Depth == 1 || mapvacancy)
            pixd = ConvertTo8(true);
        else
            pixd = ConvertTo32();

        int newindex = 0;
        int d = pixd.Depth;
        if (d == 8) {  /* colormapped */
            cmap = pixd.Colormap;
            ExtractRGBValues(val, out int rval, out int gval, out int bval);
            if (cmap!.AddNewColor(rval, gval, bval, out newindex) != 0)
                throw Lept.Fail(func, "cmap full; can't add");
        }

        for (int i = 0; i < n; i++) {
            Box box = boxa.GetBox(i, L.L_CLONE);
            if (d == 8)
                pixd.SetInRectArbitrary(box, (uint)newindex);
            else
                pixd.SetInRectArbitrary(box, val);
        }

        return pixd;
    }

    // leptonica: pixSetBlackOrWhiteBoxa (boxfunc3.c)
    /// <summary>Fills the boxes with black or white.</summary>
    /// <param name="boxa">optional; of boxes, to clear or set</param>
    /// <param name="op">L_SET_BLACK, L_SET_WHITE</param>
    /// <returns>pixd with boxes filled with white or black</returns>
    public Pix SetBlackOrWhiteBoxa(Boxa? boxa, int op)
    {
        const string func = "pixSetBlackOrWhiteBoxa";

        if (boxa == null)
            return Copy(null, this);
        int n;
        if ((n = boxa.Count) == 0)
            return Copy(null, this);

        Pix pixd = Copy(null, this);
        int d = pixd.Depth;
        if (d == 1) {
            for (int i = 0; i < n; i++) {
                Box box = boxa.GetBox(i, L.L_CLONE);
                if (op == L.L_SET_WHITE)
                    pixd.ClearInRect(box);
                else
                    pixd.SetInRect(box);
            }
            return pixd;
        }

        uint color;
        int index = 0;
        PixColormap? cmap = Colormap;
        if (cmap != null) {
            // NOTE(upstream): the color is added to the colormap of pixs, not
            // to the one of the copy pixd, which is the one that is painted.
            color = (op == L.L_SET_WHITE) ? 1u : 0u;
            cmap.AddBlackOrWhite((int)color, out index);
        } else if (d == 8) {
            color = (op == L.L_SET_WHITE) ? 0xffu : 0x0u;
        } else if (d == 32) {
            color = (op == L.L_SET_WHITE) ? 0xffffff00u : 0x0u;
        } else if (d == 2) {
            color = (op == L.L_SET_WHITE) ? 0x3u : 0x0u;
        } else if (d == 4) {
            color = (op == L.L_SET_WHITE) ? 0xfu : 0x0u;
        } else if (d == 16) {
            color = (op == L.L_SET_WHITE) ? 0xffffu : 0x0u;
        } else {
            throw Lept.Arg(func, "invalid depth");
        }

        for (int i = 0; i < n; i++) {
            Box box = boxa.GetBox(i, L.L_CLONE);
            if (cmap != null)
                pixd.SetInRectArbitrary(box, (uint)index);
            else
                pixd.SetInRectArbitrary(box, color);
        }

        return pixd;
    }

    // leptonica: pixPaintBoxaRandom (boxfunc3.c)
    /// <summary>Paints the boxes with random colors.</summary>
    /// <param name="boxa">of boxes, to paint</param>
    /// <returns>pixd with painted boxes</returns>
    /// <remarks>
    /// (1) If pixs is 1 bpp, we paint the boxa using a colormap;
    ///     otherwise, we convert to 32 bpp.
    /// (2) We use up to 254 different colors for painting the regions.
    /// (3) If boxes overlap, the later ones paint over earlier ones.
    /// </remarks>
    public Pix PaintBoxaRandom(Boxa boxa)
    {
        const string func = "pixPaintBoxaRandom";

        int n;
        if ((n = boxa.Count) == 0) {
            Log.Warning(func, "no boxes to paint; returning a copy\n");
            return Copy(null, this);
        }

        Pix pixd;
        if (Depth == 1)
            pixd = Convert1To8(null, this, 255, 0);
        else
            pixd = ConvertTo32();

        PixColormap cmap = PixColormap.CreateRandom(8, true, true);
        int d = pixd.Depth;  /* either 8 or 32 */
        if (d == 8)  /* colormapped */
            pixd.SetColormap(cmap);

        for (int i = 0; i < n; i++) {
            Box box = boxa.GetBox(i, L.L_CLONE);
            int index = 1 + (i % 254);
            if (d == 8) {
                pixd.SetInRectArbitrary(box, (uint)index);
            } else {  /* d == 32 */
                cmap.GetColor(index, out int rval, out int gval, out int bval);
                ComposeRGBPixel(rval, gval, bval, out uint val);
                pixd.SetInRectArbitrary(box, val);
            }
        }

        return pixd;
    }

    // leptonica: pixBlendBoxaRandom (boxfunc3.c)
    /// <summary>Blends the boxes with random colors.</summary>
    /// <param name="boxa">of boxes, to blend/paint</param>
    /// <param name="fract">of box color to use</param>
    /// <returns>pixd 32 bpp, with blend/painted boxes</returns>
    /// <remarks>
    /// (1) pixs is converted to 32 bpp.
    /// (2) This differs from pixPaintBoxaRandom(), in that the
    ///     colors here are blended with the color of pixs.
    /// (3) We use up to 254 different colors for painting the regions.
    /// (4) If boxes overlap, the final color depends only on the last
    ///     rect that is used.
    /// </remarks>
    public Pix BlendBoxaRandom(Boxa boxa, float fract)
    {
        const string func = "pixBlendBoxaRandom";

        if (fract < 0.0f || fract > 1.0f) {
            Log.Warning(func, "fract must be in [0.0, 1.0]; setting to 0.5\n");
            fract = 0.5f;
        }

        int n;
        if ((n = boxa.Count) == 0) {
            Log.Warning(func, "no boxes to paint; returning a copy\n");
            return Copy(null, this);
        }

        Pix pixd = ConvertTo32();

        PixColormap cmap = PixColormap.CreateRandom(8, true, true);
        for (int i = 0; i < n; i++) {
            Box box = boxa.GetBox(i, L.L_CLONE);
            int index = 1 + (i % 254);
            cmap.GetColor(index, out int rval, out int gval, out int bval);
            ComposeRGBPixel(rval, gval, bval, out uint val);
            pixd.BlendInRect(box, val, fract);
        }

        return pixd;
    }

    // leptonica: pixDrawBoxa (boxfunc3.c)
    /// <summary>Draws the outlines of the boxes.</summary>
    /// <param name="boxa">of boxes, to draw</param>
    /// <param name="width">of lines</param>
    /// <param name="val">rgba color to draw</param>
    /// <returns>pixd with outlines of boxes added</returns>
    /// <remarks>
    /// (1) If pixs is 1 bpp or is colormapped, it is converted to 8 bpp
    ///     and the boxa is drawn using a colormap; otherwise,
    ///     it is converted to 32 bpp rgb.
    /// </remarks>
    public Pix DrawBoxa(Boxa boxa, int width, uint val)
    {
        const string func = "pixDrawBoxa";

        if (width < 1)
            throw Lept.Arg(func, "width must be >= 1");

        if (boxa.Count == 0) {
            Log.Warning(func, "no boxes to draw; returning a copy\n");
            return Copy(null, this);
        }

        bool mapvacancy = false;   /* true only if cmap and not full */
        PixColormap? cmap;
        if ((cmap = Colormap) != null) {
            if (cmap.Count < 256)
                mapvacancy = true;
        }
        Pix pixd;
        if (Depth == 1 || mapvacancy)
            pixd = ConvertTo8(true);
        else
            pixd = ConvertTo32();

        ExtractRGBValues(val, out int rval, out int gval, out int bval);
        if (pixd.Depth == 8) {  /* colormapped */
            cmap = pixd.Colormap;
            cmap!.AddNewColor(rval, gval, bval, out _);
        }

        pixd.RenderBoxaArb(boxa, width, (byte)rval, (byte)gval, (byte)bval);
        return pixd;
    }

    // leptonica: pixDrawBoxaRandom (boxfunc3.c)
    /// <summary>Draws the outlines of the boxes in random colors.</summary>
    /// <param name="boxa">of boxes, to draw</param>
    /// <param name="width">thickness of line</param>
    /// <returns>pixd with box outlines drawn</returns>
    /// <remarks>
    /// (1) If pixs is 1 bpp, we draw the boxa using a colormap;
    ///     otherwise, we convert to 32 bpp.
    /// (2) We use up to 254 different colors for drawing the boxes.
    /// (3) If boxes overlap, the later ones draw over earlier ones.
    /// </remarks>
    public Pix DrawBoxaRandom(Boxa boxa, int width)
    {
        const string func = "pixDrawBoxaRandom";

        if (width < 1)
            throw Lept.Arg(func, "width must be >= 1");

        int n;
        if ((n = boxa.Count) == 0) {
            Log.Warning(func, "no boxes to draw; returning a copy\n");
            return Copy(null, this);
        }

            /* Input depth = 1 bpp; generate cmapped output */
        if (Depth == 1) {
            Ptaa ptaa = GeneratePtaaBoxa(boxa);
            return RenderRandomCmapPtaa(ptaa, 1, width, 1);
        }

            /* Generate rgb output */
        Pix pixd = ConvertTo32();
        PixColormap cmap = PixColormap.CreateRandom(8, true, true);
        for (int i = 0; i < n; i++) {
            Box box = boxa.GetBox(i, L.L_CLONE);
            int index = 1 + (i % 254);
            cmap.GetColor(index, out int rval, out int gval, out int bval);
            pixd.RenderBoxArb(box, width, (byte)rval, (byte)gval, (byte)bval);
        }
        return pixd;
    }
}

public sealed partial class Boxaa
{
    // leptonica: boxaaDisplay (boxfunc3.c)
    /// <summary>Renders the boxes of a Boxaa, and the extent of each Boxa.</summary>
    /// <param name="pixs">optional; 1 bpp</param>
    /// <param name="linewba">line width to display outline of each boxa</param>
    /// <param name="linewb">line width to display outline of each box</param>
    /// <param name="colorba">color to display boxa</param>
    /// <param name="colorb">color to display box</param>
    /// <param name="w">width of output pix; use 0 if determined by %pixs or %baa</param>
    /// <param name="h">height of output pix; use 0 if determined by %pixs or %baa</param>
    /// <returns>the rendered pix</returns>
    /// <remarks>
    /// (1) If %pixs exists, this renders the boxes over an 8 bpp version
    ///     of it.  Otherwise, it renders the boxes over an empty image
    ///     with a white background.
    /// (2) If %pixs exists, the dimensions of %pixd are the same,
    ///     and input values of %w and %h are ignored.
    ///     If %pixs is null, the dimensions of %pixd are determined by
    ///       - %w and %h if both are > 0, or
    ///       - the minimum size required using all boxes in %baa.
    /// </remarks>
    public Pix Display(Pix? pixs, int linewba, int linewb, uint colorba, uint colorb,
                       int w, int h)
    {
        if (w <= 0 || h <= 0) {
            if (pixs != null)
                pixs.GetDimensions(out w, out h, out _);
            else
                GetExtent(out w, out h, out _, out _);
        }

        Pix pixd;
        PixColormap cmap;
        if (pixs != null) {
            pixd = pixs.ConvertTo8(true);
            cmap = pixd.Colormap!;
        } else {
            pixd = Pix.Create(w, h, 8);
            cmap = PixColormap.Create(8);
            pixd.SetColormap(cmap);
            cmap.AddColor(255, 255, 255);
        }
        Pix.ExtractRGBValues(colorb, out int rbox, out int gbox, out int bbox);
        Pix.ExtractRGBValues(colorba, out int rboxa, out int gboxa, out int bboxa);
        cmap.AddColor(rbox, gbox, bbox);
        cmap.AddColor(rboxa, gboxa, bboxa);

        int n = Count;
        for (int i = 0; i < n; i++) {
            Boxa boxa = GetBoxa(i, L.L_CLONE);
            boxa.GetExtent(out _, out _, out Box box);
            pixd.RenderBoxArb(box, linewba, (byte)rboxa, (byte)gboxa, (byte)bboxa);
            int m = boxa.Count;
            for (int j = 0; j < m; j++) {
                box = boxa.GetBox(j, L.L_CLONE);
                pixd.RenderBoxArb(box, linewb, (byte)rbox, (byte)gbox, (byte)bbox);
            }
        }

        return pixd;
    }
}

public sealed partial class Pixa
{
    private static int Rand() => CRand.Next();

    // leptonica: pixaDisplayBoxaa (boxfunc3.c)
    /// <summary>Draws the boxes of each Boxa of a Boxaa on the corresponding pix.</summary>
    /// <param name="baa">boxes to draw on input pixa</param>
    /// <param name="colorflag">L_DRAW_RED, L_DRAW_GREEN, etc</param>
    /// <param name="width">thickness of lines</param>
    /// <returns>pixa with box outlines drawn on each pix</returns>
    /// <remarks>
    /// (1) All pix in %pixas that are not rgb are converted to rgb.
    /// (2) Each boxa in %baa contains boxes that will be drawn on
    ///     the corresponding pix in %pixas.
    /// (3) The color of the boxes drawn on each pix are selected with
    ///     %colorflag:
    ///       * For red, green or blue: use L_DRAW_RED, etc.
    ///       * For sequential r, g, b: use L_DRAW_RGB
    ///       * For random colors: use L_DRAW_RANDOM
    /// </remarks>
    public Pixa DisplayBoxaa(Boxaa baa, int colorflag, int width)
    {
        const string func = "pixaDisplayBoxaa";

        int nba, n;
        uint color;
        uint[] colors = new uint[255];

        if (width < 1)
            throw Lept.Arg(func, "width must be >= 1");
        if ((nba = baa.Count) < 1)
            throw Lept.Arg(func, "no boxa in baa");
        if ((n = Count) == 0)
            throw Lept.Arg(func, "no pix in pixas");
        if (n != nba)
            throw Lept.Arg(func, "num pix != num boxa");
        if (colorflag == L.L_DRAW_RED)
            color = 0xff000000;
        else if (colorflag == L.L_DRAW_GREEN)
            color = 0x00ff0000;
        else if (colorflag == L.L_DRAW_BLUE)
            color = 0x0000ff00;
        else if (colorflag == L.L_DRAW_RGB)
            color = 0x000000ff;
        else if (colorflag == L.L_DRAW_RANDOM)
            color = 0x00000000;
        else
            throw Lept.Arg(func, "invalid colorflag");

        if (colorflag == L.L_DRAW_RED || colorflag == L.L_DRAW_GREEN ||
            colorflag == L.L_DRAW_BLUE) {
            for (int i = 0; i < 255; i++)
                colors[i] = color;
        } else if (colorflag == L.L_DRAW_RGB) {
            for (int i = 0; i < 255; i++) {
                if (i % 3 == L.L_DRAW_RED)
                    colors[i] = 0xff000000;
                else if (i % 3 == L.L_DRAW_GREEN)
                    colors[i] = 0x00ff0000;
                else  /* i % 3 == L_DRAW_BLUE) */
                    colors[i] = 0x0000ff00;
            }
        } else if (colorflag == L.L_DRAW_RANDOM) {
            for (int i = 0; i < 255; i++) {
                int rval = (int)((uint)Rand() & 0xff);
                int gval = (int)((uint)Rand() & 0xff);
                int bval = (int)((uint)Rand() & 0xff);
                Pix.ComposeRGBPixel(rval, gval, bval, out colors[i]);
            }
        }

        Pixa pixad = Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix = GetPix(i, L.L_COPY);
            Boxa boxa = baa.GetBoxa(i, L.L_CLONE);
            int nbox = boxa.Count;
            for (int j = 0; j < nbox; j++) {
                Box box = boxa.GetBox(j, L.L_CLONE);
                Pix.ExtractRGBValues(colors[j % 255], out int rval, out int gval, out int bval);
                pix.RenderBoxArb(box, width, (byte)rval, (byte)gval, (byte)bval);
            }
            pixad.AddPix(pix, L.L_INSERT);
        }

        return pixad;
    }
}

/*---------------------------------------------------------------------*
 *                   Split mask components into Boxa                   *
 *---------------------------------------------------------------------*/
public sealed partial class Pix
{
    // leptonica: pixSplitIntoBoxa (boxfunc3.c)
    /// <summary>Generates a boxa of rectangles that covers the fg of a mask.</summary>
    /// <param name="minsum">minimum pixels to trigger propagation</param>
    /// <param name="skipdist">distance before computing sum for propagation</param>
    /// <param name="delta">difference required to stop propagation</param>
    /// <param name="maxbg">maximum number of allowed bg pixels in ref scan</param>
    /// <param name="maxcomps">use 0 for unlimited number of subdivided components</param>
    /// <param name="remainder">set to 1 to get b.b. of remaining stuff</param>
    /// <returns>boxa of rectangles covering the fg of pixs</returns>
    /// <remarks>
    /// (1) This generates a boxa of rectangles that covers
    ///     the fg of a mask.  For each 8-connected component in pixs,
    ///     it does a greedy partitioning, choosing the largest
    ///     rectangle found from each of the four directions at each iter.
    ///     See pixSplitComponentIntoBoxa() for details.
    /// (2) The input parameters give some flexibility for boundary
    ///     noise.  The resulting set of rectangles may cover some
    ///     bg pixels.
    /// (3) This should be used when there are a small number of
    ///     mask components, each of which has sides that are close
    ///     to horizontal and vertical.  The input parameters %delta
    ///     and %maxbg determine whether or not holes in the mask are covered.
    /// (4) The parameter %maxcomps gives the maximum number of allowed
    ///     rectangles extracted from any single connected component.
    ///     Use 0 if no limit is to be applied.
    /// (5) The flag %remainder specifies whether we take a final bounding
    ///     box for anything left after the maximum number of allowed
    ///     rectangle is extracted.
    /// </remarks>
    public Boxa SplitIntoBoxa(int minsum, int skipdist, int delta, int maxbg,
                              int maxcomps, int remainder)
    {
        if (Depth != 1)
            throw Lept.Arg("pixSplitIntoBoxa", "pixs undefined or not 1 bpp");

        Boxa boxas = ConnComp(out Pixa? pixas, 8);
        int n = boxas.Count;
        Boxa boxad = Boxa.Create(0);
        for (int i = 0; i < n; i++) {
            Pix pix = pixas!.GetPix(i, L.L_CLONE);
            Box box = boxas.GetBox(i, L.L_CLONE);
            Boxa boxa = pix.SplitComponentIntoBoxa(box, minsum, skipdist,
                                                   delta, maxbg, maxcomps, remainder);
            boxad.Join(boxa, 0, -1);
        }

        return boxad;
    }

    // leptonica: pixSplitComponentIntoBoxa (boxfunc3.c)
    /// <summary>Generates a boxa of rectangles that covers the fg of a single component.</summary>
    /// <param name="box">optional; location of pix w/rt an origin</param>
    /// <param name="minsum">minimum pixels to trigger propagation</param>
    /// <param name="skipdist">distance before computing sum for propagation</param>
    /// <param name="delta">difference required to stop propagation</param>
    /// <param name="maxbg">maximum number of allowed bg pixels in ref scan</param>
    /// <param name="maxcomps">use 0 for unlimited number of subdivided components</param>
    /// <param name="remainder">set to 1 to get b.b. of remaining stuff</param>
    /// <returns>boxa of rectangles covering the fg of pix</returns>
    /// <remarks>
    /// (1) This generates a boxa of rectangles that covers
    ///     the fg of a mask.  It does so by a greedy partitioning of
    ///     the mask, choosing the largest rectangle found from
    ///     each of the four directions at each step.
    /// (2) The input parameters give some flexibility for boundary
    ///     noise.  The resulting set of rectangles must cover all
    ///     the fg pixels and, in addition, may cover some bg pixels.
    ///     Using small input parameters on a noiseless mask (i.e., one
    ///     that has only large vertical and horizontal edges) will
    ///     result in a proper covering of only the fg pixels of the mask.
    /// (3) The input is assumed to be a single connected component, that
    ///     may have holes.  From each side, sweep inward, counting
    ///     the pixels.  If the count becomes greater than %minsum,
    ///     and we have moved forward a further amount %skipdist,
    ///     record that count ('countref'), but don't accept if the scan
    ///     contains more than %maxbg bg pixels.  Continue the scan
    ///     until we reach a count that differs from countref by at
    ///     least %delta, at which point the propagation stops.  The box
    ///     swept out gets a score, which is the sum of fg pixels
    ///     minus a penalty.  The penalty is the number of bg pixels
    ///     in the box.  This is done from all four sides, and the
    ///     side with the largest score is saved as a rectangle.
    ///     The process repeats until there is either no rectangle
    ///     left, or there is one that can't be captured from any
    ///     direction.  For the latter case, we simply accept the
    ///     last rectangle.
    /// (4) The input box is only used to specify the location of
    ///     the UL corner of pix, with respect to an origin that
    ///     typically represents the UL corner of an underlying image,
    ///     of which pix is one component.  If %box is null,
    ///     the UL corner is taken to be (0, 0).
    /// (5) The parameter %maxcomps gives the maximum number of allowed
    ///     rectangles extracted from any single connected component.
    ///     Use 0 if no limit is to be applied.
    /// (6) The flag %remainder specifies whether we take a final bounding
    ///     box for anything left after the maximum number of allowed
    ///     rectangle is extracted.
    /// (7) So if %maxcomps > 0, it specifies that we want no more than
    ///     the first %maxcomps rectangles that satisfy the input
    ///     criteria.  After this, we can get a final rectangle that
    ///     bounds everything left over by setting %remainder == 1.
    ///     If %remainder == 0, we only get rectangles that satisfy
    ///     the input criteria.
    /// (8) It should be noted that the removal of rectangles can
    ///     break the original c.c. into several c.c.
    /// (9) Summing up:
    ///       * If %maxcomp == 0, the splitting proceeds as far as possible.
    ///       * If %maxcomp > 0, the splitting stops when %maxcomps are
    ///           found, or earlier if no more components can be selected.
    ///       * If %remainder == 1 and components remain that cannot be
    ///           selected, they are returned as a single final rectangle;
    ///           otherwise, they are ignored.
    /// </remarks>
    public Boxa SplitComponentIntoBoxa(Box? box, int minsum, int skipdist, int delta,
                                       int maxbg, int maxcomps, int remainder)
    {
        int boxx, boxy, bw, bh, maxdir, maxscore;
        Box? boxs;  /* shrinks as rectangular regions are removed */
        Box boxt1, boxt2;

        if (Depth != 1)
            throw Lept.Arg("pixSplitComponentIntoBoxa", "pix undefined or not 1 bpp");

        Pix pixs = Copy(null, this);
        pixs.GetDimensions(out int w, out int h, out _);
        if (box != null)
            box.GetGeometry(out boxx, out boxy, out _, out _);
        else
            boxx = boxy = 0;
        boxs = Box.Create(0, 0, w, h);
        Boxa boxad = Boxa.Create(0);

        int iter = 0;
        while (boxs != null) {
                /* The geometry of boxs that C reads here is not used. */
            Boxa boxat = Boxa.Create(4);  /* potential rectangular regions */
            Numa nascore = Numa.Create(4);
            for (int i = 0; i < 4; i++) {
                SearchForRectangle(pixs, boxs, minsum, skipdist, delta, maxbg,
                                   i, boxat, nascore);
            }
            Numa nas = nascore.GetSortIndex(L.L_SORT_DECREASING);
            maxdir = nas.GetIValue(0);
            maxscore = nascore.GetIValue(maxdir);
            if (maxscore > 0) {  /* accept this */
                boxt1 = boxat.GetBox(maxdir, L.L_CLONE);
                boxt2 = boxt1.Transform(boxx, boxy, 1.0f, 1.0f);
                boxad.AddBox(boxt2, L.L_INSERT);
                pixs.ClearInRect(boxt1);
                pixs.ClipBoxToForeground(boxs, out _, out Box? boxt3);
                boxs = boxt3;
                if (boxs != null) {
                    boxs.GetGeometry(out _, out _, out bw, out bh);
                    if (bw < 2 || bh < 2)
                        boxs = null;  /* we're done */
                }
            } else {  /* no more valid rectangles can be found */
                if (remainder == 1) {  /* save the last box */
                    boxt1 = boxs.Transform(boxx, boxy, 1.0f, 1.0f);
                    boxad.AddBox(boxt1, L.L_INSERT);
                }
                boxs = null;  /* we're done */
            }

            iter++;
            if ((iter == maxcomps) && boxs != null) {
                if (remainder == 1) {  /* save the last box */
                    boxt1 = boxs.Transform(boxx, boxy, 1.0f, 1.0f);
                    boxad.AddBox(boxt1, L.L_INSERT);
                }
                boxs = null;  /* we're done */
            }
        }

        return boxad;
    }

    // leptonica: pixSearchForRectangle (boxfunc3.c)
    /// <summary>Sweeps from one side to find a rectangle; adds it, with its score, to the outputs.</summary>
    /// <param name="pixs">1 bpp</param>
    /// <param name="boxs">current region to investigate</param>
    /// <param name="minsum">minimum pixels to trigger propagation</param>
    /// <param name="skipdist">distance before computing sum for propagation</param>
    /// <param name="delta">difference required to stop propagation</param>
    /// <param name="maxbg">maximum number of allowed bg pixels in ref scan</param>
    /// <param name="sideflag">side to search from</param>
    /// <param name="boxat">add result of rectangular region found here</param>
    /// <param name="nascore">add score for this rectangle here</param>
    /// <remarks>
    /// (1) See pixSplitComponentIntoBoxa() for an explanation of the algorithm.
    ///     This does the sweep from a single side.  For each iteration
    ///     in pixSplitComponentIntoBoxa(), this will be called 4 times,
    ///     for %sideflag = {0, 1, 2, 3}.
    /// (2) If a valid rectangle is not found, add a score of 0 and
    ///     input a minimum box.
    /// </remarks>
    internal static void SearchForRectangle(Pix pixs, Box boxs, int minsum, int skipdist,
                                            int delta, int maxbg, int sideflag,
                                            Boxa boxat, Numa nascore)
    {
        int width, height, minincol, maxincol, mininrow, maxinrow, bgref;
        int x, y, x0, y0, colsum, rowsum, countref, diff;

        if (pixs.Depth != 1)
            throw Lept.Arg("pixSearchForRectangle", "pixs undefined or not 1 bpp");

        int[] lines1 = pixs.GetLinePtrs();
        uint[] data1 = pixs.Data;
        boxs.GetGeometry(out int bx, out int by, out int bw, out int bh);
        Box? boxr = null;
        int setref = 0;
        int atref = 0;
        int maxval = 0;
        int minval = 100000;
        int score = 0;  /* sum of all (fg - bg) pixels seen in the scan */
        int xref, yref;
        xref = yref = 100000;  /* init to impossibly big number */
        countref = 0;
        bgref = 0;
        if (sideflag == L.L_FROM_LEFT) {
            for (x = bx; x < bx + bw; x++) {
                colsum = 0;
                maxincol = 0;
                minincol = 100000;
                for (y = by; y < by + bh; y++) {
                    if (ArrayAccess.GetDataBit(data1.AsSpan(lines1[y]), x) != 0) {
                        colsum++;
                        if (y > maxincol) maxincol = y;
                        if (y < minincol) minincol = y;
                    }
                }
                score += colsum;

                    /* Enough fg to sweep out a rectangle? */
                if (setref == 0 && colsum >= minsum) {
                    setref = 1;
                    xref = x + 10;
                    if (xref >= bx + bw)
                        goto failure;
                }

                    /* Reached the reference line; save the count;
                     * if there is too much bg, the rectangle is invalid. */
                if (setref != 0 && x == xref) {
                    atref = 1;
                    countref = colsum;
                    bgref = maxincol - minincol + 1 - countref;
                    if (bgref > maxbg)
                        goto failure;
                }

                    /* Have we left the rectangle?  If so, save it along
                     * with the score. */
                if (atref != 0) {
                    diff = Box.LAbs(colsum - countref);
                    if (diff >= delta || x == bx + bw - 1) {
                        height = maxval - minval + 1;
                        width = x - bx;
                        if (x == bx + bw - 1) width = x - bx + 1;
                        boxr = Box.Create(bx, minval, width, height);
                        score = 2 * score - width * height;
                        goto success;
                    }
                }
                maxval = Math.Max(maxval, maxincol);
                minval = Math.Min(minval, minincol);
            }
            goto failure;
        } else if (sideflag == L.L_FROM_RIGHT) {
            for (x = bx + bw - 1; x >= bx; x--) {
                colsum = 0;
                maxincol = 0;
                minincol = 100000;
                for (y = by; y < by + bh; y++) {
                    if (ArrayAccess.GetDataBit(data1.AsSpan(lines1[y]), x) != 0) {
                        colsum++;
                        if (y > maxincol) maxincol = y;
                        if (y < minincol) minincol = y;
                    }
                }
                score += colsum;
                if (setref == 0 && colsum >= minsum) {
                    setref = 1;
                    xref = x - 10;
                    if (xref < bx)
                        goto failure;
                }
                if (setref != 0 && x == xref) {
                    atref = 1;
                    countref = colsum;
                    bgref = maxincol - minincol + 1 - countref;
                    if (bgref > maxbg)
                        goto failure;
                }
                if (atref != 0) {
                    diff = Box.LAbs(colsum - countref);
                    if (diff >= delta || x == bx) {
                        height = maxval - minval + 1;
                        x0 = x + 1;
                        if (x == bx) x0 = x;
                        width = bx + bw - x0;
                        boxr = Box.Create(x0, minval, width, height);
                        score = 2 * score - width * height;
                        goto success;
                    }
                }
                maxval = Math.Max(maxval, maxincol);
                minval = Math.Min(minval, minincol);
            }
            goto failure;
        } else if (sideflag == L.L_FROM_TOP) {
            for (y = by; y < by + bh; y++) {
                rowsum = 0;
                maxinrow = 0;
                mininrow = 100000;
                for (x = bx; x < bx + bw; x++) {
                    if (ArrayAccess.GetDataBit(data1.AsSpan(lines1[y]), x) != 0) {
                        rowsum++;
                        if (x > maxinrow) maxinrow = x;
                        if (x < mininrow) mininrow = x;
                    }
                }
                score += rowsum;
                if (setref == 0 && rowsum >= minsum) {
                    setref = 1;
                    yref = y + 10;
                    if (yref >= by + bh)
                        goto failure;
                }
                if (setref != 0 && y == yref) {
                    atref = 1;
                    countref = rowsum;
                    bgref = maxinrow - mininrow + 1 - countref;
                    if (bgref > maxbg)
                        goto failure;
                }
                if (atref != 0) {
                    diff = Box.LAbs(rowsum - countref);
                    if (diff >= delta || y == by + bh - 1) {
                        width = maxval - minval + 1;
                        height = y - by;
                        if (y == by + bh - 1) height = y - by + 1;
                        boxr = Box.Create(minval, by, width, height);
                        score = 2 * score - width * height;
                        goto success;
                    }
                }
                maxval = Math.Max(maxval, maxinrow);
                minval = Math.Min(minval, mininrow);
            }
            goto failure;
        } else if (sideflag == L.L_FROM_BOT) {
            for (y = by + bh - 1; y >= by; y--) {
                rowsum = 0;
                maxinrow = 0;
                mininrow = 100000;
                for (x = bx; x < bx + bw; x++) {
                    if (ArrayAccess.GetDataBit(data1.AsSpan(lines1[y]), x) != 0) {
                        rowsum++;
                        if (x > maxinrow) maxinrow = x;
                        if (x < mininrow) mininrow = x;
                    }
                }
                score += rowsum;
                if (setref == 0 && rowsum >= minsum) {
                    setref = 1;
                    yref = y - 10;
                    if (yref < by)
                        goto failure;
                }
                if (setref != 0 && y == yref) {
                    atref = 1;
                    countref = rowsum;
                    bgref = maxinrow - mininrow + 1 - countref;
                    if (bgref > maxbg)
                        goto failure;
                }
                if (atref != 0) {
                    diff = Box.LAbs(rowsum - countref);
                    if (diff >= delta || y == by) {
                        width = maxval - minval + 1;
                        y0 = y + 1;
                        if (y == by) y0 = y;
                        height = by + bh - y0;
                        boxr = Box.Create(minval, y0, width, height);
                        score = 2 * score - width * height;
                        goto success;
                    }
                }
                maxval = Math.Max(maxval, maxinrow);
                minval = Math.Min(minval, mininrow);
            }
            goto failure;
        }

    failure:
        nascore.AddNumber(0);
        boxat.AddBox(Box.Create(0, 0, 1, 1), L.L_INSERT);  /* min box */
        return;

    success:
        nascore.AddNumber(score);
        boxat.AddBox(boxr!, L.L_INSERT);
        return;
    }
}

/*---------------------------------------------------------------------*
 *             Represent horizontal or vertical mosaic strips          *
 *---------------------------------------------------------------------*/
public sealed partial class Boxa
{
    // leptonica: makeMosaicStrips (boxfunc3.c)
    /// <summary>Generates the boxes of horizontal or vertical strips that tile an image.</summary>
    /// <param name="w">width of the image</param>
    /// <param name="h">height of the image</param>
    /// <param name="direction">L_SCAN_HORIZONTAL or L_SCAN_VERTICAL</param>
    /// <param name="size">of strips in the scan direction</param>
    /// <remarks>
    /// (1) For example, this can be used to generate a pixa of
    ///     vertical strips of width 10 from an image, using:
    ///        pixGetDimensions(pix, &amp;w, &amp;h, NULL);
    ///        boxa = makeMosaicStrips(w, h, L_SCAN_HORIZONTAL, 10);
    ///        pixa = pixClipRectangles(pix, boxa);
    ///     All strips except the last will be the same width.  The
    ///     last strip will have width w % 10.
    /// </remarks>
    public static Boxa MakeMosaicStrips(int w, int h, int direction, int size)
    {
        const string func = "makeMosaicStrips";

        int nstrips, extra;
        Box box;

        if (w < 1 || h < 1)
            throw Lept.Arg(func, "invalid w or h");
        if (direction != L.L_SCAN_HORIZONTAL && direction != L.L_SCAN_VERTICAL)
            throw Lept.Arg(func, "invalid direction");
        if (size < 1)
            throw Lept.Arg(func, "size < 1");

        Boxa boxa = Create(0);
        if (direction == L.L_SCAN_HORIZONTAL) {
            nstrips = w / size;
            for (int i = 0; i < nstrips; i++) {
                box = Box.Create(i * size, 0, size, h);
                boxa.AddBox(box, L.L_INSERT);
            }
            if ((extra = w % size) > 0) {
                box = Box.Create(nstrips * size, 0, extra, h);
                boxa.AddBox(box, L.L_INSERT);
            }
        } else {
            nstrips = h / size;
            for (int i = 0; i < nstrips; i++) {
                box = Box.Create(0, i * size, w, size);
                boxa.AddBox(box, L.L_INSERT);
            }
            if ((extra = h % size) > 0) {
                box = Box.Create(0, nstrips * size, w, extra);
                boxa.AddBox(box, L.L_INSERT);
            }
        }
        return boxa;
    }

/*---------------------------------------------------------------------*
 *                        Comparison between boxa                      *
 *---------------------------------------------------------------------*/
    // leptonica: boxaCompareRegions (boxfunc3.c)
    /// <summary>Compares the regions of two boxa (number of boxes and total area only).</summary>
    /// <param name="boxa2">the second boxa; this boxa is the first</param>
    /// <param name="areathresh">minimum area of boxes to be considered</param>
    /// <param name="pnsame">true if same number of boxes</param>
    /// <param name="pdiffarea">fractional difference in total area</param>
    /// <remarks>
    /// This overload does not compute the xor difference, as when pdiffxor is NULL in C.
    /// See the overload with the pdiffxor output for the Notes.
    /// </remarks>
    public void CompareRegions(Boxa boxa2, int areathresh, out bool pnsame, out float pdiffarea)
    {
        CompareRegionsCore(boxa2, areathresh, false, false, out pnsame, out pdiffarea,
                           out _, out _);
    }

    /// <summary>Compares the regions of two boxa, including the xor of the regions.</summary>
    /// <param name="boxa2">the second boxa; this boxa is the first</param>
    /// <param name="areathresh">minimum area of boxes to be considered</param>
    /// <param name="pnsame">true if same number of boxes</param>
    /// <param name="pdiffarea">fractional difference in total area</param>
    /// <param name="pdiffxor">fractional difference in xor of regions</param>
    /// <remarks>
    /// (1) This takes 2 boxa, removes all boxes smaller than a given area,
    ///     and compares the remaining boxes between the boxa.
    /// (2) The area threshold is introduced to help remove noise from
    ///     small components.  Any box with a smaller value of w * h
    ///     will be removed from consideration.
    /// (3) The xor difference is the most stringent test, requiring alignment
    ///     of the corresponding boxes.  It is also more computationally
    ///     intensive and is optionally returned.  Alignment is to the
    ///     UL corner of each region containing all boxes, as given by
    ///     boxaGetExtent().
    /// (4) Both fractional differences are with respect to the total
    ///     area in the two boxa.  They range from 0.0 to 1.0.
    ///     A perfect match has value 0.0.  If both boxa are empty,
    ///     we return 0.0; if one is empty we return 1.0.
    /// (5) An example input might be the rectangular regions of a
    ///     segmentation mask for text or images from two pages.
    /// </remarks>
    public void CompareRegions(Boxa boxa2, int areathresh, out bool pnsame,
                               out float pdiffarea, out float pdiffxor)
    {
        CompareRegionsCore(boxa2, areathresh, true, false, out pnsame, out pdiffarea,
                           out pdiffxor, out _);
    }

    /// <summary>Compares the regions of two boxa, including the xor of the regions and a debug pix.</summary>
    /// <param name="boxa2">the second boxa; this boxa is the first</param>
    /// <param name="areathresh">minimum area of boxes to be considered</param>
    /// <param name="pnsame">true if same number of boxes</param>
    /// <param name="pdiffarea">fractional difference in total area</param>
    /// <param name="pdiffxor">fractional difference in xor of regions</param>
    /// <param name="ppixdb">debug pix showing two boxa; null if not made</param>
    /// <remarks>See the overload with the pdiffxor output for the Notes.</remarks>
    public void CompareRegions(Boxa boxa2, int areathresh, out bool pnsame,
                               out float pdiffarea, out float pdiffxor, out Pix? ppixdb)
    {
        CompareRegionsCore(boxa2, areathresh, true, true, out pnsame, out pdiffarea,
                           out pdiffxor, out ppixdb);
    }

    private void CompareRegionsCore(Boxa boxa2, int areathresh, bool wantxor, bool wantdb,
                                    out bool pnsame, out float pdiffarea,
                                    out float pdiffxor, out Pix? ppixdb)
    {
        Boxa boxa1 = this;

        pdiffxor = 1.0f;
        ppixdb = null;
        pnsame = false;
        pdiffarea = 1.0f;

        Boxa boxa3 = boxa1.SelectByArea(areathresh, L.L_SELECT_IF_GTE, out _);
        Boxa boxa4 = boxa2.SelectByArea(areathresh, L.L_SELECT_IF_GTE, out _);
        int n3 = boxa3.Count;
        int n4 = boxa4.Count;
        if (n3 == n4)
            pnsame = true;

            /* There are no boxes in one or both */
        if (n3 == 0 || n4 == 0) {
            if (n3 == 0 && n4 == 0) { /* they are both empty: we say they are the
                                       * same; otherwise, they differ maximally
                                       * and retain the default value. */
                pdiffarea = 0.0f;
                pdiffxor = 0.0f;
            }
            return;
        }

            /* There are boxes in both */
        int area1 = boxa3.GetArea();
        int area2 = boxa4.GetArea();
        pdiffarea = (float)Box.LAbs(area1 - area2) / (float)(area1 + area2);
        if (!wantxor)
            return;

            /* The easiest way to get the xor of aligned boxes is to work
             * with images of each boxa.  This is done by translating each
             * boxa so that the UL corner of the region that includes all
             * boxes in the boxa is placed at the origin of each pix. */
        boxa3.GetExtent(out int w, out int h, out Box box3);
        boxa4.GetExtent(out w, out h, out Box box4);
        box3.GetGeometry(out int x3, out int y3, out int w3, out int h3);
        box4.GetGeometry(out int x4, out int y4, out int w4, out int h4);
        Boxa boxa3t = boxa3.Transform(-x3, -y3, 1.0f, 1.0f);
        Boxa boxa4t = boxa4.Transform(-x4, -y4, 1.0f, 1.0f);
        w = Math.Max(x3 + w3, x4 + w4);
        h = Math.Max(y3 + h3, y4 + h4);
        Pix pix3 = Pix.Create(w, h, 1);  /* use the max to keep everything in the xor */
        Pix pix4 = Pix.Create(w, h, 1);
        Pix.MaskBoxa(pix3, pix3, boxa3t, L.L_SET_PIXELS);
        Pix.MaskBoxa(pix4, pix4, boxa4t, L.L_SET_PIXELS);
        int[] tab = Pix.MakePixelSumTab8();
        int count3 = pix3.CountPixels(tab);
        int count4 = pix4.CountPixels(tab);
        Pix pix5 = Pix.Xor(null, pix3, pix4);
        int countxor = pix5.CountPixels(tab);
        pdiffxor = (float)countxor / (float)(count3 + count4);

        if (wantdb) {
            Pixa pixa = Pixa.Create(2);
            Pix pix1 = Pix.Create(w, h, 32);
            pix1.SetAll();
            pix1.RenderHashBoxaBlend(boxa3, 5, 1, L.L_POS_SLOPE_LINE, 2,
                                     255, 0, 0, 0.5f);
            pix1.RenderHashBoxaBlend(boxa4, 5, 1, L.L_NEG_SLOPE_LINE, 2,
                                     0, 255, 0, 0.5f);
            pixa.AddPix(pix1, L.L_INSERT);
            Pix pix2 = Pix.Create(w, h, 32);
            pix2.PaintThroughMask(pix3, x3, y3, 0xff000000);
            pix2.PaintThroughMask(pix4, x4, y4, 0x00ff0000);
            Pix.And(pix3, pix3, pix4);
            pix2.PaintThroughMask(pix3, x3, y3, 0x0000ff00);
            pixa.AddPix(pix2, L.L_INSERT);
            ppixdb = pixa.DisplayTiledInRows(32, 1000, 1.0f, 0, 30, 2);
        }
    }

/*---------------------------------------------------------------------*
 *                Reliable selection of a single large box             *
 *---------------------------------------------------------------------*/
    // leptonica: boxaSelectLargeULBox (boxfunc3.c)
    /// <summary>Selects a large box near the upper left, from the boxes of this boxa.</summary>
    /// <param name="areaslop">fraction near but less than 1.0</param>
    /// <param name="yslop">number of pixels in y direction</param>
    /// <returns>the selected box</returns>
    /// <remarks>
    /// (1) See usage notes in pixSelectLargeULComp().
    /// </remarks>
    public Box SelectLargeULBox(float areaslop, int yslop)
    {
        const string func = "boxaSelectLargeULBox";

        int w, h, x1, y1, x2, y2;
        float area, max_area;

        if (Count == 0)
            throw Lept.Arg(func, "no boxes in boxas");
        if (areaslop < 0.0f || areaslop > 1.0f)
            throw Lept.Arg(func, "invalid value for areaslop");
        yslop = Math.Max(0, yslop);

        Boxa boxa1 = Sort(L.L_SORT_BY_AREA, L.L_SORT_DECREASING, out _);
        Boxa boxa2 = boxa1.Sort(L.L_SORT_BY_Y, L.L_SORT_INCREASING, out _);
        int n = boxa2.Count;
        boxa1.GetBoxGeometry(0, out _, out _, out w, out h);  /* biggest box by area */
        max_area = (float)(w * h);

            /* boxa3 collects all boxes eligible by area, sorted top-down */
        Boxa boxa3 = Create(4);
        for (int i = 0; i < n; i++) {
            boxa2.GetBoxGeometry(i, out _, out _, out w, out h);
            area = (float)(w * h);
            if (area / max_area >= areaslop) {
                Box box1 = boxa2.GetBox(i, L.L_COPY);
                boxa3.AddBox(box1, L.L_INSERT);
            }
        }

            /* Take the first (top-most box) unless the second (etc) has
             * nearly the same y value but a smaller x value. */
        n = boxa3.Count;
        boxa3.GetBoxGeometry(0, out x1, out y1, out _, out _);
        int select = 0;
        for (int i = 1; i < n; i++) {
            boxa3.GetBoxGeometry(i, out x2, out y2, out _, out _);
            if (y2 - y1 < yslop && x2 < x1) {
                select = i;
                x1 = x2;  /* but always compare against y1 */
            }
        }

        return boxa3.GetBox(select, L.L_COPY);
    }
}

public sealed partial class Pix
{
    // leptonica: pixSelectLargeULComp (boxfunc3.c)
    /// <summary>Selects a large component near the upper left of the image.</summary>
    /// <param name="areaslop">fraction near but less than 1.0</param>
    /// <param name="yslop">number of pixels in y direction</param>
    /// <param name="connectivity">4 or 8</param>
    /// <returns>box, or null if there are no components</returns>
    /// <remarks>
    /// (1) This selects a box near the top (first) and left (second)
    ///     of the image, from the set of all boxes that have
    ///           area >= %areaslop * (area of biggest box),
    ///     where %areaslop is some fraction; say ~ 0.9.
    /// (2) For all boxes satisfying the above condition, select
    ///     the left-most box that is within %yslop (say, 20) pixels
    ///     of the box nearest the top.
    /// (3) This can be used to reliably select a specific one of
    ///     the largest regions in an image, for applications where
    ///     there are expected to be small variations in region size
    ///     and location.
    /// (4) See boxSelectLargeULBox() for implementation details.
    /// </remarks>
    public Box? SelectLargeULComp(float areaslop, int yslop, int connectivity)
    {
        if (areaslop < 0.0f || areaslop > 1.0f)
            throw Lept.Arg("pixSelectLargeULComp", "invalid value for areaslop");
        yslop = Math.Max(0, yslop);

        Boxa boxa1 = ConnCompBB(connectivity);
        if (boxa1.Count == 0)
            return null;
        return boxa1.SelectLargeULBox(areaslop, yslop);
    }
}
