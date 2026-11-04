using System;

namespace Leptonica;

/*
 *  graphics.c: generation of points on lines, boxes and other shapes, and
 *  rendering of those shapes (and of plots, polygons and contours) in a Pix.
 *
 *  Differences from C:
 *    - No ptaDestroy()/boxDestroy()/pixDestroy(): the GC owns the objects
 *      (not ported).
 *    - ERROR_INT / ERROR_PTR paths throw; the render functions return void.
 *    - pixRenderPlotFromNuma() and pixRenderPlotFromNumaGen() take the pix by
 *      ref, because the C functions replace it by a 32 bpp version.
 *    - pixFillPolygon() returns null (with a warning) when there is nothing
 *      to fill, as the C function does.
 */

/// <summary>Constants from pix.h used by graphics.c.</summary>
public static partial class L
{
    /* Location of a plot of a Numa in a Pix (L_PLOT_AT_*) */
    /// <summary>Plot horizontally at the top of the image.</summary>
    public const int L_PLOT_AT_TOP = 1;
    /// <summary>Plot horizontally at the middle of the image.</summary>
    public const int L_PLOT_AT_MID_HORIZ = 2;
    /// <summary>Plot horizontally at the bottom of the image.</summary>
    public const int L_PLOT_AT_BOT = 3;
    /// <summary>Plot vertically at the left of the image.</summary>
    public const int L_PLOT_AT_LEFT = 4;
    /// <summary>Plot vertically at the middle of the image.</summary>
    public const int L_PLOT_AT_MID_VERT = 5;
    /// <summary>Plot vertically at the right of the image.</summary>
    public const int L_PLOT_AT_RIGHT = 6;
}

public sealed partial class Pix
{
    /*------------------------------------------------------------------*
     *        Pta generation for arbitrary shapes built with lines      *
     *------------------------------------------------------------------*/

    // leptonica: generatePtaLine (graphics.c)
    /// <summary>Generates the points on a line, using Bresenham line drawing.</summary>
    /// <param name="x1">end point 1</param>
    /// <param name="y1">end point 1</param>
    /// <param name="x2">end point 2</param>
    /// <param name="y2">end point 2</param>
    /// <returns>pta of the points on the line between the end points</returns>
    /// <remarks>
    /// (1) Uses Bresenham line drawing, which results in an 8-connected line.
    /// </remarks>
    public static Pta GeneratePtaLine(int x1, int y1, int x2, int y2)
    {
        int npts, diff, sign;
        bool getyofx;
        float slope;

            /* Generate line parameters */
        if (x1 == x2 && y1 == y2) {  /* same point */
            getyofx = true;
            npts = 1;
            diff = 0;
            sign = 0;
            slope = 0.0f;
        } else if (Math.Abs(x2 - x1) >= Math.Abs(y2 - y1)) {
            getyofx = true;
            npts = Math.Abs(x2 - x1) + 1;
            diff = x2 - x1;
            sign = (x2 - x1 < 0) ? -1 : 1;
            slope = (float)(sign * (y2 - y1)) / (float)diff;
        } else {
            getyofx = false;
            npts = Math.Abs(y2 - y1) + 1;
            diff = y2 - y1;
            sign = (y2 - y1 < 0) ? -1 : 1;
            slope = (float)(sign * (x2 - x1)) / (float)diff;
        }

        Pta pta = Pta.Create(npts);

        if (npts == 1) {  /* degenerate case */
            pta.AddPt(x1, y1);
            return pta;
        }

            /* Generate the set of points */
        if (getyofx) {  /* y = y(x) */
            for (int i = 0; i < npts; i++) {
                int x = x1 + sign * i;
                int y = (int)(y1 + (float)i * slope + 0.5);
                pta.AddPt(x, y);
            }
        } else {   /* x = x(y) */
            for (int i = 0; i < npts; i++) {
                int x = (int)(x1 + (float)i * slope + 0.5);
                int y = y1 + sign * i;
                pta.AddPt(x, y);
            }
        }

        return pta;
    }

    // leptonica: generatePtaWideLine (graphics.c)
    /// <summary>Generates the points on a line of the given width.</summary>
    /// <param name="x1">end point 1</param>
    /// <param name="y1">end point 1</param>
    /// <param name="x2">end point 2</param>
    /// <param name="y2">end point 2</param>
    /// <param name="width">of the line</param>
    /// <returns>ptaj</returns>
    public static Pta GeneratePtaWideLine(int x1, int y1, int x2, int y2, int width)
    {
        const string func = "generatePtaWideLine";

        if (width < 1) {
            Log.Warning(func, "width < 1; setting to 1\n");
            width = 1;
        }

        Pta ptaj = GeneratePtaLine(x1, y1, x2, y2);
        if (width == 1)
            return ptaj;

            /* width > 1; estimate line direction & join */
        if (Math.Abs(x1 - x2) > Math.Abs(y1 - y2)) {  /* "horizontal" line  */
            for (int i = 1; i < width; i++) {
                int y1a, y2a;
                if ((i & 1) == 1) {   /* place above */
                    y1a = y1 - (i + 1) / 2;
                    y2a = y2 - (i + 1) / 2;
                } else {  /* place below */
                    y1a = y1 + (i + 1) / 2;
                    y2a = y2 + (i + 1) / 2;
                }
                Pta pta = GeneratePtaLine(x1, y1a, x2, y2a);
                ptaj.Join(pta, 0, -1);
            }
        } else  {  /* "vertical" line  */
            for (int i = 1; i < width; i++) {
                int x1a, x2a;
                if ((i & 1) == 1) {   /* place to left */
                    x1a = x1 - (i + 1) / 2;
                    x2a = x2 - (i + 1) / 2;
                } else {  /* place to right */
                    x1a = x1 + (i + 1) / 2;
                    x2a = x2 + (i + 1) / 2;
                }
                Pta pta = GeneratePtaLine(x1a, y1, x2a, y2);
                ptaj.Join(pta, 0, -1);
            }
        }

        return ptaj;
    }

    // leptonica: generatePtaBox (graphics.c)
    /// <summary>Generates the points on the outline of a box.</summary>
    /// <param name="box">the box</param>
    /// <param name="width">of line</param>
    /// <returns>ptad</returns>
    /// <remarks>
    /// (1) Because the box is constructed so that we don't have any
    ///     overlapping lines, there is no need to remove duplicates.
    /// </remarks>
    public static Pta GeneratePtaBox(Box box, int width)
    {
        const string func = "generatePtaBox";

        if (width < 1) {
            Log.Warning(func, "width < 1; setting to 1\n");
            width = 1;
        }

            /* Generate line points and add them to the pta. */
        box.GetGeometry(out int x, out int y, out int w, out int h);
        if (w == 0 || h == 0)
            throw Lept.Arg(func, "box has w = 0 or h = 0");
        Pta ptad = Pta.Create(0);
        Pta pta;
        if ((width & 1) == 1) {   /* odd width */
            pta = GeneratePtaWideLine(x - width / 2, y,
                                      x + w - 1 + width / 2, y, width);
            ptad.Join(pta, 0, -1);
            pta = GeneratePtaWideLine(x + w - 1, y + 1 + width / 2,
                                      x + w - 1, y + h - 2 - width / 2, width);
            ptad.Join(pta, 0, -1);
            pta = GeneratePtaWideLine(x + w - 1 + width / 2, y + h - 1,
                                      x - width / 2, y + h - 1, width);
            ptad.Join(pta, 0, -1);
            pta = GeneratePtaWideLine(x, y + h - 2 - width / 2,
                                      x, y + 1 + width / 2, width);
            ptad.Join(pta, 0, -1);
        } else {   /* even width */
            pta = GeneratePtaWideLine(x - width / 2, y,
                                      x + w - 2 + width / 2, y, width);
            ptad.Join(pta, 0, -1);
            pta = GeneratePtaWideLine(x + w - 1, y + 0 + width / 2,
                                      x + w - 1, y + h - 2 - width / 2, width);
            ptad.Join(pta, 0, -1);
            pta = GeneratePtaWideLine(x + w - 2 + width / 2, y + h - 1,
                                      x - width / 2, y + h - 1, width);
            ptad.Join(pta, 0, -1);
            pta = GeneratePtaWideLine(x, y + h - 2 - width / 2,
                                      x, y + 0 + width / 2, width);
            ptad.Join(pta, 0, -1);
        }

        return ptad;
    }

    // leptonica: generatePtaBoxa (graphics.c)
    /// <summary>Generates the points on the outlines of all boxes in a boxa.</summary>
    /// <param name="boxa">the boxes</param>
    /// <param name="width">of lines</param>
    /// <param name="removedups">1 to remove, 0 to leave</param>
    /// <returns>ptad</returns>
    /// <remarks>
    /// (1) If boxa has overlapping boxes, and if blending will
    ///     be used to give a transparent effect, transparency
    ///     artifacts at line intersections can be removed using
    ///     removedups = 1.
    /// </remarks>
    public static Pta GeneratePtaBoxa(Boxa boxa, int width, int removedups)
    {
        const string func = "generatePtaBoxa";

        if (width < 1) {
            Log.Warning(func, "width < 1; setting to 1\n");
            width = 1;
        }

        int n = boxa.Count;
        Pta ptat = Pta.Create(0);
        for (int i = 0; i < n; i++) {
            Box box = boxa.GetBox(i, L.L_CLONE);
            Pta pta = GeneratePtaBox(box, width);
            ptat.Join(pta, 0, -1);
        }

        Pta ptad;
        if (removedups != 0)
            ptad = ptat.RemoveDupsByAset();
        else
            ptad = ptat.Clone();

        return ptad;
    }

    // leptonica: generatePtaHashBox (graphics.c)
    /// <summary>Generates the points of hash lines in a box, optionally with its outline.</summary>
    /// <param name="box">the box</param>
    /// <param name="spacing">spacing between lines; must be &gt; 1</param>
    /// <param name="width">of line</param>
    /// <param name="orient">orientation of lines: L_HORIZONTAL_LINE,
    /// L_POS_SLOPE_LINE, L_VERTICAL_LINE, L_NEG_SLOPE_LINE</param>
    /// <param name="outline">0 to skip drawing box outline</param>
    /// <returns>ptad</returns>
    /// <remarks>
    /// (1) The orientation takes on one of 4 orientations (horiz, vertical,
    ///     slope +1, slope -1).
    /// (2) The full outline is also drawn if outline = 1.
    /// </remarks>
    public static Pta GeneratePtaHashBox(Box box, int spacing, int width, int orient,
                                         int outline)
    {
        const string func = "generatePtaHashBox";

        if (spacing <= 1)
            throw Lept.Arg(func, "spacing not > 1");
        if (orient != L.L_HORIZONTAL_LINE && orient != L.L_POS_SLOPE_LINE &&
            orient != L.L_VERTICAL_LINE && orient != L.L_NEG_SLOPE_LINE)
            throw Lept.Arg(func, "invalid line orientation");
        box.GetGeometry(out int bx, out int by, out int bw, out int bh);
        if (bw == 0 || bh == 0)
            throw Lept.Arg(func, "box has bw = 0 or bh = 0");
        if (width < 1) {
            Log.Warning(func, "width < 1; setting to 1\n");
            width = 1;
        }

            /* Generate line points and add them to the pta. */
        Pta ptad = Pta.Create(0);
        Pta pta;
        if (outline != 0) {
            pta = GeneratePtaBox(box, width);
            ptad.Join(pta, 0, -1);
        }
        if (orient == L.L_HORIZONTAL_LINE) {
            int n = 1 + bh / spacing;
            for (int i = 0; i < n; i++) {
                int y = by + (i * (bh - 1)) / (n - 1);
                pta = GeneratePtaWideLine(bx, y, bx + bw - 1, y, width);
                ptad.Join(pta, 0, -1);
            }
        } else if (orient == L.L_VERTICAL_LINE) {
            int n = 1 + bw / spacing;
            for (int i = 0; i < n; i++) {
                int x = bx + (i * (bw - 1)) / (n - 1);
                pta = GeneratePtaWideLine(x, by, x, by + bh - 1, width);
                ptad.Join(pta, 0, -1);
            }
        } else if (orient == L.L_POS_SLOPE_LINE) {
            int n = 2 + (int)((bw + bh) / (1.4 * spacing));
            for (int i = 0; i < n; i++) {
                int x = (int)(bx + (i + 0.5) * 1.4 * spacing);
                box.IntersectByLine(x, by - 1, 1.0f, out int x1, out int y1,
                                    out int x2, out int y2, out int npts);
                if (npts == 2) {
                    pta = GeneratePtaWideLine(x1, y1, x2, y2, width);
                    ptad.Join(pta, 0, -1);
                }
            }
        } else {  /* orient == L_NEG_SLOPE_LINE */
            int n = 2 + (int)((bw + bh) / (1.4 * spacing));
            for (int i = 0; i < n; i++) {
                int x = (int)(bx - bh + (i + 0.5) * 1.4 * spacing);
                box.IntersectByLine(x, by - 1, -1.0f, out int x1, out int y1,
                                    out int x2, out int y2, out int npts);
                if (npts == 2) {
                    pta = GeneratePtaWideLine(x1, y1, x2, y2, width);
                    ptad.Join(pta, 0, -1);
                }
            }
        }

        return ptad;
    }

    // leptonica: generatePtaHashBoxa (graphics.c)
    /// <summary>Generates the points of hash lines in all boxes of a boxa.</summary>
    /// <param name="boxa">the boxes</param>
    /// <param name="spacing">spacing between lines; must be &gt; 1</param>
    /// <param name="width">of line</param>
    /// <param name="orient">orientation of lines: L_HORIZONTAL_LINE,
    /// L_POS_SLOPE_LINE, L_VERTICAL_LINE, L_NEG_SLOPE_LINE</param>
    /// <param name="outline">0 to skip drawing box outline</param>
    /// <param name="removedups">1 to remove, 0 to leave</param>
    /// <returns>ptad</returns>
    /// <remarks>
    /// (1) The orientation takes on one of 4 orientations (horiz, vertical,
    ///     slope +1, slope -1).
    /// (2) The full outline is also drawn if outline = 1.
    /// (3) If the boxa has overlapping boxes, and if blending will
    ///     be used to give a transparent effect, transparency
    ///     artifacts at line intersections can be removed using
    ///     removedups = 1.
    /// </remarks>
    public static Pta GeneratePtaHashBoxa(Boxa boxa, int spacing, int width, int orient,
                                          int outline, int removedups)
    {
        const string func = "generatePtaHashBoxa";

        if (spacing <= 1)
            throw Lept.Arg(func, "spacing not > 1");
        if (width < 1) {
            Log.Warning(func, "width < 1; setting to 1\n");
            width = 1;
        }
        if (orient != L.L_HORIZONTAL_LINE && orient != L.L_POS_SLOPE_LINE &&
            orient != L.L_VERTICAL_LINE && orient != L.L_NEG_SLOPE_LINE)
            throw Lept.Arg(func, "invalid line orientation");

        int n = boxa.Count;
        Pta ptat = Pta.Create(0);
        for (int i = 0; i < n; i++) {
            Box box = boxa.GetBox(i, L.L_CLONE);
            Pta pta = GeneratePtaHashBox(box, spacing, width, orient, outline);
            ptat.Join(pta, 0, -1);
        }

        Pta ptad;
        if (removedups != 0)
            ptad = ptat.RemoveDupsByAset();
        else
            ptad = ptat.Clone();

        return ptad;
    }

    // leptonica: generatePtaaBoxa (graphics.c)
    /// <summary>Generates a pta of the four corners for each box in a boxa.</summary>
    /// <param name="boxa">the boxes</param>
    /// <returns>ptaa</returns>
    /// <remarks>
    /// (1) This generates a pta of the four corners for each box in
    ///     the boxa.
    /// (2) Each of these pta can be rendered onto a pix with random colors,
    ///     by using pixRenderRandomCmapPtaa() with closeflag = 1.
    /// </remarks>
    public static Ptaa GeneratePtaaBoxa(Boxa boxa)
    {
        int n = boxa.Count;
        Ptaa ptaa = Ptaa.Create(n);
        for (int i = 0; i < n; i++) {
            Box box = boxa.GetBox(i, L.L_CLONE);
            box.GetGeometry(out int x, out int y, out int w, out int h);
            Pta pta = Pta.Create(4);
            pta.AddPt(x, y);
            pta.AddPt(x + w - 1, y);
            pta.AddPt(x + w - 1, y + h - 1);
            pta.AddPt(x, y + h - 1);
            ptaa.AddPta(pta, L.L_INSERT);
        }

        return ptaa;
    }

    // leptonica: generatePtaaHashBoxa (graphics.c)
    /// <summary>Generates a pta of hash lines for each box in a boxa.</summary>
    /// <param name="boxa">the boxes</param>
    /// <param name="spacing">spacing between hash lines; must be &gt; 1</param>
    /// <param name="width">hash line width</param>
    /// <param name="orient">orientation of lines: L_HORIZONTAL_LINE,
    /// L_POS_SLOPE_LINE, L_VERTICAL_LINE, L_NEG_SLOPE_LINE</param>
    /// <param name="outline">0 to skip drawing box outline</param>
    /// <returns>ptaa</returns>
    /// <remarks>
    /// (1) The orientation takes on one of 4 orientations (horiz, vertical,
    ///     slope +1, slope -1).
    /// (2) The full outline is also drawn if outline = 1.
    /// (3) Each of these pta can be rendered onto a pix with random colors,
    ///     by using pixRenderRandomCmapPtaa() with closeflag = 1.
    /// </remarks>
    public static Ptaa GeneratePtaaHashBoxa(Boxa boxa, int spacing, int width, int orient,
                                            int outline)
    {
        const string func = "generatePtaaHashBoxa";

        if (spacing <= 1)
            throw Lept.Arg(func, "spacing not > 1");
        if (width < 1) {
            Log.Warning(func, "width < 1; setting to 1\n");
            width = 1;
        }
        if (orient != L.L_HORIZONTAL_LINE && orient != L.L_POS_SLOPE_LINE &&
            orient != L.L_VERTICAL_LINE && orient != L.L_NEG_SLOPE_LINE)
            throw Lept.Arg(func, "invalid line orientation");

        int n = boxa.Count;
        Ptaa ptaa = Ptaa.Create(n);
        for (int i = 0; i < n; i++) {
            Box box = boxa.GetBox(i, L.L_CLONE);
            Pta pta = GeneratePtaHashBox(box, spacing, width, orient, outline);
            ptaa.AddPta(pta, L.L_INSERT);
        }

        return ptaa;
    }

    // leptonica: generatePtaPolyline (graphics.c)
    /// <summary>Generates the points on a polyline.</summary>
    /// <param name="ptas">vertices of polyline</param>
    /// <param name="width">of line</param>
    /// <param name="closeflag">1 to close the contour; 0 otherwise</param>
    /// <param name="removedups">1 to remove, 0 to leave</param>
    /// <returns>ptad</returns>
    public static Pta GeneratePtaPolyline(Pta ptas, int width, int closeflag, int removedups)
    {
        const string func = "generatePtaPolyline";

        if (width < 1) {
            Log.Warning(func, "width < 1; setting to 1\n");
            width = 1;
        }

        int n = ptas.Count;
        Pta ptat = Pta.Create(0);
        if (n < 2)  /* nothing to do */
            return ptat;

        ptas.GetIPt(0, out int x1, out int y1);
        int x2, y2;
        Pta pta;
        for (int i = 1; i < n; i++) {
            ptas.GetIPt(i, out x2, out y2);
            pta = GeneratePtaWideLine(x1, y1, x2, y2, width);
            ptat.Join(pta, 0, -1);
            x1 = x2;
            y1 = y2;
        }

        if (closeflag != 0) {
            ptas.GetIPt(0, out x2, out y2);
            pta = GeneratePtaWideLine(x1, y1, x2, y2, width);
            ptat.Join(pta, 0, -1);
        }

        Pta ptad;
        if (removedups != 0)
            ptad = ptat.RemoveDupsByAset();
        else
            ptad = ptat.Clone();

        return ptad;
    }

    // leptonica: generatePtaGrid (graphics.c)
    /// <summary>Generates the points of a grid.</summary>
    /// <param name="w">width of region where grid will be displayed</param>
    /// <param name="h">height of region where grid will be displayed</param>
    /// <param name="nx">number of rectangles in the x direction in the grid</param>
    /// <param name="ny">number of rectangles in the y direction in the grid</param>
    /// <param name="width">of rendered lines</param>
    /// <returns>ptad</returns>
    public static Pta GeneratePtaGrid(int w, int h, int nx, int ny, int width)
    {
        const string func = "generatePtaGrid";

        if (nx < 1 || ny < 1)
            throw Lept.Arg(func, "nx and ny must be > 0");
        if (w < 2 * nx || h < 2 * ny)
            throw Lept.Arg(func, "w and/or h too small");
        if (width < 1) {
            Log.Warning(func, "width < 1; setting to 1\n");
            width = 1;
        }

        Boxa boxa = Boxa.Create(nx * ny);
        int bx = (w + nx - 1) / nx;
        int by = (h + ny - 1) / ny;
        for (int i = 0; i < ny; i++) {
            int y1 = by * i;
            int y2 = Math.Min(y1 + by, h - 1);
            for (int j = 0; j < nx; j++) {
                int x1 = bx * j;
                int x2 = Math.Min(x1 + bx, w - 1);
                Box box = Box.Create(x1, y1, x2 - x1 + 1, y2 - y1 + 1);
                boxa.AddBox(box, L.L_INSERT);
            }
        }

        return GeneratePtaBoxa(boxa, width, 1);
    }

    // leptonica: convertPtaLineTo4cc (graphics.c)
    /// <summary>Converts an 8-connected line of points to a 4-connected line.</summary>
    /// <param name="ptas">8-connected line of points</param>
    /// <returns>ptad 4-connected line</returns>
    /// <remarks>
    /// (1) When a polyline is generated with width = 1, the resulting
    ///     line is not 4-connected in general.  This function adds
    ///     points as necessary to convert the line to 4-cconnected.
    ///     It is useful when rendering 1 bpp on a pix.
    /// (2) Do not use this for lines generated with width &gt; 1.
    /// </remarks>
    public static Pta ConvertPtaLineTo4cc(Pta ptas)
    {
        int n = ptas.Count;
        Pta ptad = Pta.Create(n);
        ptas.GetIPt(0, out int xp, out int yp);
        ptad.AddPt(xp, yp);
        for (int i = 1; i < n; i++) {
            ptas.GetIPt(i, out int x, out int y);
            if (x != xp && y != yp)  /* diagonal */
                ptad.AddPt(x, yp);
            ptad.AddPt(x, y);
            xp = x;
            yp = y;
        }

        return ptad;
    }

    // leptonica: generatePtaFilledCircle (graphics.c)
    /// <summary>Generates the points of a filled circle.</summary>
    /// <param name="radius">of the circle</param>
    /// <returns>pta</returns>
    /// <remarks>
    /// (1) The circle is has diameter = 2 * radius + 1.
    /// (2) It is located with the center of the circle at the
    ///     point (radius, radius).
    /// (3) Consequently, it typically must be translated if
    ///     it is to represent a set of pixels in an image.
    /// </remarks>
    public static Pta GeneratePtaFilledCircle(int radius)
    {
        if (radius < 1)
            throw Lept.Arg("generatePtaFilledCircle", "radius must be >= 1");

        Pta pta = Pta.Create(0);
        float radthresh = (float)((radius + 0.5) * (radius + 0.5));
        for (int y = 0; y <= 2 * radius; y++) {
            for (int x = 0; x <= 2 * radius; x++) {
                float sqdist = (float)((y - radius) * (y - radius) +
                                       (x - radius) * (x - radius));
                if (sqdist <= radthresh)
                    pta.AddPt(x, y);
            }
        }

        return pta;
    }

    // leptonica: generatePtaFilledSquare (graphics.c)
    /// <summary>Generates the points of a filled square.</summary>
    /// <param name="side">of the square</param>
    /// <returns>pta</returns>
    /// <remarks>
    /// (1) The center of the square can be chosen to be at
    ///     (side / 2, side / 2).  It must be translated by this amount
    ///     when used for replication.
    /// </remarks>
    public static Pta GeneratePtaFilledSquare(int side)
    {
        if (side < 1)
            throw Lept.Arg("generatePtaFilledSquare", "side must be > 0");

        Pta pta = Pta.Create(0);
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
                pta.AddPt(x, y);

        return pta;
    }

    // leptonica: generatePtaLineFromPt (graphics.c)
    /// <summary>Generates the points of a line from a point, a length and an angle.</summary>
    /// <param name="x">point of origination</param>
    /// <param name="y">point of origination</param>
    /// <param name="length">of line, including starting point</param>
    /// <param name="radang">angle in radians, CW from horizontal</param>
    /// <returns>pta</returns>
    /// <remarks>
    /// (1) length of the line is 1 greater than the distance
    ///     used in locatePtRadially().  Example: a distance of 1
    ///     gives rise to a length of 2.
    /// </remarks>
    public static Pta GeneratePtaLineFromPt(int x, int y, double length, double radang)
    {
        int x2 = x + (int)((length - 1.0) * Math.Cos(radang));  /* the point at the other end of the line */
        int y2 = y + (int)((length - 1.0) * Math.Sin(radang));
        return GeneratePtaLine(x, y, x2, y2);
    }

    // leptonica: locatePtRadially (graphics.c)
    /// <summary>Locates a point at a distance and an angle from a reference point.</summary>
    /// <param name="xr">reference point</param>
    /// <param name="yr">reference point</param>
    /// <param name="dist">distance of point from reference point along
    /// line given by the specified angle</param>
    /// <param name="radang">angle in radians, CW from horizontal</param>
    /// <param name="px">location of point</param>
    /// <param name="py">location of point</param>
    public static void LocatePtRadially(int xr, int yr, double dist, double radang,
                                        out double px, out double py)
    {
        px = xr + dist * Math.Cos(radang);
        py = yr + dist * Math.Sin(radang);
    }

    /*------------------------------------------------------------------*
     *            Rendering function plots directly on images           *
     *------------------------------------------------------------------*/

    // leptonica: pixRenderPlotFromNuma (graphics.c)
    /// <summary>Plots row or column aligned data on a pix.</summary>
    /// <param name="pix">any type; replaced if not 32 bpp rgb</param>
    /// <param name="na">to be plotted</param>
    /// <param name="plotloc">location of plot: L_PLOT_AT_TOP, etc</param>
    /// <param name="linewidth">width of "line" that is drawn; between 1 and 7</param>
    /// <param name="max">maximum excursion in pixels from baseline</param>
    /// <param name="color">plot color: 0xrrggbb00</param>
    /// <remarks>
    /// (1) Simplified interface for plotting row or column aligned data
    ///     on a pix.
    /// (2) This replaces pix with a 32 bpp rgb version if it is not
    ///     already 32 bpp.  It then draws the plot on the pix.
    /// (3) See makePlotPtaFromNumaGen() for more details.
    /// </remarks>
    public static void RenderPlotFromNuma(ref Pix pix, Numa na, int plotloc, int linewidth,
                                          int max, uint color)
    {
        pix.GetDimensions(out int w, out int h, out _);
        int size = (plotloc == L.L_PLOT_AT_TOP || plotloc == L.L_PLOT_AT_MID_HORIZ ||
                    plotloc == L.L_PLOT_AT_BOT) ? h : w;
        Pta pta = MakePlotPtaFromNuma(na, size, plotloc, linewidth, max);

        if (pix.Depth != 32)
            pix = pix.ConvertTo32();
        ExtractRGBValues(color, out int rval, out int gval, out int bval);
        pix.RenderPtaArb(pta, (byte)rval, (byte)gval, (byte)bval);
    }

    // leptonica: makePlotPtaFromNuma (graphics.c)
    /// <summary>Generates the points for a plot of a Numa.</summary>
    /// <param name="na">the values</param>
    /// <param name="size">pix height for horizontal plot; pix width for vertical plot</param>
    /// <param name="plotloc">location of plot: L_PLOT_AT_TOP, etc</param>
    /// <param name="linewidth">width of "line" that is drawn; between 1 and 7</param>
    /// <param name="max">maximum excursion in pixels from baseline</param>
    /// <returns>ptad</returns>
    /// <remarks>
    /// (1) This generates points from numa representing y(x) or x(y)
    ///     with respect to a pix.  A horizontal plot y(x) is drawn for
    ///     a function of column position, and a vertical plot is drawn
    ///     for a function x(y) of row position.  The baseline is located
    ///     so that all plot points will fit in the pix.
    /// (2) See makePlotPtaFromNumaGen() for more details.
    /// </remarks>
    public static Pta MakePlotPtaFromNuma(Numa na, int size, int plotloc, int linewidth,
                                          int max)
    {
        int orient, refpos;

        if (plotloc == L.L_PLOT_AT_TOP || plotloc == L.L_PLOT_AT_MID_HORIZ ||
            plotloc == L.L_PLOT_AT_BOT) {
            orient = L.L_HORIZONTAL_LINE;
        } else if (plotloc == L.L_PLOT_AT_LEFT || plotloc == L.L_PLOT_AT_MID_VERT ||
                   plotloc == L.L_PLOT_AT_RIGHT) {
            orient = L.L_VERTICAL_LINE;
        } else {
            throw Lept.Arg("makePlotPtaFromNuma", "invalid plotloc");
        }

        if (plotloc == L.L_PLOT_AT_LEFT || plotloc == L.L_PLOT_AT_TOP)
            refpos = max;
        else if (plotloc == L.L_PLOT_AT_MID_VERT || plotloc == L.L_PLOT_AT_MID_HORIZ)
            refpos = size / 2;
        else  /* L_PLOT_AT_RIGHT || L_PLOT_AT_BOT */
            refpos = size - max - 1;

        return MakePlotPtaFromNumaGen(na, orient, linewidth, refpos, max, 1);
    }

    // leptonica: pixRenderPlotFromNumaGen (graphics.c)
    /// <summary>Plots row or column aligned data on a pix (general interface).</summary>
    /// <param name="pix">any type; replaced if not 32 bpp rgb</param>
    /// <param name="na">to be plotted</param>
    /// <param name="orient">L_HORIZONTAL_LINE, L_VERTICAL_LINE</param>
    /// <param name="linewidth">width of "line" that is drawn; between 1 and 7</param>
    /// <param name="refpos">reference position: y for horizontal; x for vertical</param>
    /// <param name="max">maximum excursion in pixels from baseline</param>
    /// <param name="drawref">1 to draw the reference line and its normal</param>
    /// <param name="color">plot color: 0xrrggbb00</param>
    /// <remarks>
    /// (1) General interface for plotting row or column aligned data
    ///     on a pix.
    /// (2) This replaces pix with a 32 bpp rgb version if it is not
    ///     already 32 bpp.  It then draws the plot on the pix.
    /// (3) See makePlotPtaFromNumaGen() for other input parameters.
    /// </remarks>
    public static void RenderPlotFromNumaGen(ref Pix pix, Numa na, int orient, int linewidth,
                                             int refpos, int max, int drawref, uint color)
    {
        Pta pta = MakePlotPtaFromNumaGen(na, orient, linewidth, refpos, max, drawref);

        if (pix.Depth != 32)
            pix = pix.ConvertTo32();
        ExtractRGBValues(color, out int rval, out int gval, out int bval);
        pix.RenderPtaArb(pta, (byte)rval, (byte)gval, (byte)bval);
    }

    // leptonica: makePlotPtaFromNumaGen (graphics.c)
    /// <summary>Generates the points for a plot of a Numa (general interface).</summary>
    /// <param name="na">the values</param>
    /// <param name="orient">L_HORIZONTAL_LINE, L_VERTICAL_LINE</param>
    /// <param name="linewidth">width of "line" that is drawn; between 1 and 7</param>
    /// <param name="refpos">reference position: y for horizontal; x for vertical</param>
    /// <param name="max">maximum excursion in pixels from baseline</param>
    /// <param name="drawref">1 to draw the reference line and its normal</param>
    /// <returns>ptad</returns>
    /// <remarks>
    /// (1) This generates points from numa representing y(x) or x(y)
    ///     with respect to a pix.  For y(x), we draw a horizontal line
    ///     at the reference position and a vertical line at the edge; then
    ///     we draw the values of numa, scaled so that the maximum
    ///     excursion from the reference position is max pixels.
    /// (2) The start and delx parameters of numa are used to refer
    ///     its values to the raster lines (L_VERTICAL_LINE) or columns
    ///     (L_HORIZONTAL_LINE).
    /// (3) The linewidth is chosen in the interval [1 ... 7].
    /// (4) refpos should be chosen so the plot is entirely within the pix
    ///     that it will be painted onto.
    /// (5) This would typically be used to plot, in place, a function
    ///     computed along pixel rows or columns.
    /// </remarks>
    public static Pta MakePlotPtaFromNumaGen(Numa na, int orient, int linewidth, int refpos,
                                             int max, int drawref)
    {
        const string func = "makePlotPtaFromNumaGen";

        if (orient != L.L_HORIZONTAL_LINE && orient != L.L_VERTICAL_LINE)
            throw Lept.Arg(func, "invalid orient");
        if (linewidth < 1) {
            Log.Warning(func, "linewidth < 1; setting to 1\n");
            linewidth = 1;
        }
        if (linewidth > 7) {
            Log.Warning(func, "linewidth > 7; setting to 7\n");
            linewidth = 7;
        }

        na.GetMin(out float minval, out _);
        na.GetMax(out float maxval, out _);
        float absval = Math.Max(minval < 0 ? -minval : minval, maxval < 0 ? -maxval : maxval);
        float scale = (float)max / (float)absval;
        int n = na.Count;
        na.GetParameters(out float start, out float del);

            /* Generate the plot points */
        Pta pta1 = Pta.Create(n);
        int maxw = 0, maxh = 0;
        for (int i = 0; i < n; i++) {
            float val = na.GetFValue(i);
            if (orient == L.L_HORIZONTAL_LINE) {
                pta1.AddPt(start + i * del, refpos + scale * val);
                maxw = (int)((del >= 0) ? start + n * del + linewidth
                                        : start + linewidth);
                maxh = refpos + max + linewidth;
            } else {  /* vertical line */
                pta1.AddPt(refpos + scale * val, start + i * del);
                maxw = refpos + max + linewidth;
                maxh = (int)((del >= 0) ? start + n * del + linewidth
                                        : start + linewidth);
            }
        }

            /* Optionally, widen the plot */
        Pta ptad;
        if (linewidth > 1) {
            Pta pta2;
            if (linewidth % 2 == 0)  /* even linewidth; use side of a square */
                pta2 = GeneratePtaFilledSquare(linewidth);
            else  /* odd linewidth; use radius of a circle */
                pta2 = GeneratePtaFilledCircle(linewidth / 2);
            ptad = pta1.ReplicatePattern(null, pta2, linewidth / 2,
                                         linewidth / 2, maxw, maxh);
        } else {
            ptad = pta1.Clone();
        }

            /* Optionally, add the reference lines */
        if (drawref != 0) {
            if (orient == L.L_HORIZONTAL_LINE) {
                pta1 = GeneratePtaLine((int)start, refpos, (int)(start + n * del), refpos);
                ptad.Join(pta1, 0, -1);
                pta1 = GeneratePtaLine((int)start, refpos - max,
                                       (int)start, refpos + max);
                ptad.Join(pta1, 0, -1);
            } else {  /* vertical line */
                pta1 = GeneratePtaLine(refpos, (int)start, refpos, (int)(start + n * del));
                ptad.Join(pta1, 0, -1);
                pta1 = GeneratePtaLine(refpos - max, (int)start,
                                       refpos + max, (int)start);
                ptad.Join(pta1, 0, -1);
            }
        }

        return ptad;
    }

    /*------------------------------------------------------------------*
     *        Pta generation for arbitrary shapes built with lines      *
     *------------------------------------------------------------------*/

    // leptonica: pixRenderPta (graphics.c)
    /// <summary>Renders a set of points on a pix by setting, clearing or flipping pixels.</summary>
    /// <param name="pta">arbitrary set of points</param>
    /// <param name="op">one of L_SET_PIXELS, L_CLEAR_PIXELS, L_FLIP_PIXELS</param>
    /// <remarks>
    /// The pix is any depth, not cmapped.
    /// (1) L_SET_PIXELS puts all image bits in each pixel to 1
    ///     (black for 1 bpp; white for depth &gt; 1)
    /// (2) L_CLEAR_PIXELS puts all image bits in each pixel to 0
    ///     (white for 1 bpp; black for depth &gt; 1)
    /// (3) L_FLIP_PIXELS reverses all image bits in each pixel
    /// (4) This function clips the rendering to the pix.  It performs
    ///     clipping for functions such as pixRenderLine(),
    ///     pixRenderBox() and pixRenderBoxa(), that call pixRenderPta().
    /// </remarks>
    public void RenderPta(Pta pta, int op)
    {
        const string func = "pixRenderPta";

        if (Colormap != null)
            throw Lept.Arg(func, "pix is colormapped");
        if (op != L.L_SET_PIXELS && op != L.L_CLEAR_PIXELS && op != L.L_FLIP_PIXELS)
            throw Lept.Arg(func, "invalid op");

        GetDimensions(out int w, out int h, out int d);
        uint maxval = 1;
        if (op == L.L_SET_PIXELS) {
            switch (d)
            {
            case 2:
                maxval = 0x3;
                break;
            case 4:
                maxval = 0xf;
                break;
            case 8:
                maxval = 0xff;
                break;
            case 16:
                maxval = 0xffff;
                break;
            case 32:
                maxval = 0xffffffff;
                break;
            }
        }

        int n = pta.Count;
        for (int i = 0; i < n; i++) {
            pta.GetIPt(i, out int x, out int y);
            if (x < 0 || x >= w)
                continue;
            if (y < 0 || y >= h)
                continue;
            switch (op)
            {
            case L.L_SET_PIXELS:
                SetPixel(x, y, maxval);
                break;
            case L.L_CLEAR_PIXELS:
                ClearPixel(x, y);
                break;
            case L.L_FLIP_PIXELS:
                FlipPixel(x, y);
                break;
            default:
                break;
            }
        }
    }

    // leptonica: pixRenderPtaArb (graphics.c)
    /// <summary>Renders a set of points on a pix in the given color.</summary>
    /// <param name="pta">arbitrary set of points</param>
    /// <param name="rval">red</param>
    /// <param name="gval">green</param>
    /// <param name="bval">blue</param>
    /// <remarks>
    /// The pix is any depth, cmapped ok.
    /// (1) If pix is colormapped, render this color (or the nearest
    ///     color if the cmap is full) on each pixel.
    /// (2) The rgb components have the standard dynamic range [0 ... 255]
    /// (3) If pix is not colormapped, do the best job you can using
    ///     the input colors:
    ///       ~ d = 1: set the pixels
    ///       ~ d = 2, 4, 8: average the input rgb value
    ///       ~ d = 32: use the input rgb value
    /// (4) This function clips the rendering to pix.
    /// </remarks>
    public void RenderPtaArb(Pta pta, byte rval, byte gval, byte bval)
    {
        int d = Depth;
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 32)
            throw Lept.Arg("pixRenderPtaArb", "depth not in {1,2,4,8,32}");

        if (d == 1) {
            RenderPta(pta, L.L_SET_PIXELS);
            return;
        }

        PixColormap? cmap = Colormap;
        GetDimensions(out int w, out int h, out d);
        int index = 0;
        byte val = 0;
        uint val32 = 0;
        if (cmap != null) {
            cmap.AddNearestColor(rval, gval, bval, out index);
        } else {
            if (d == 2)
                val = (byte)((rval + gval + bval) / (3 * 64));
            else if (d == 4)
                val = (byte)((rval + gval + bval) / (3 * 16));
            else if (d == 8)
                val = (byte)((rval + gval + bval) / 3);
            else  /* d == 32 */
                ComposeRGBPixel(rval, gval, bval, out val32);
        }

        int n = pta.Count;
        for (int i = 0; i < n; i++) {
            pta.GetIPt(i, out int x, out int y);
            if (x < 0 || x >= w)
                continue;
            if (y < 0 || y >= h)
                continue;
            if (cmap != null)
                SetPixel(x, y, (uint)index);
            else if (d == 32)
                SetPixel(x, y, val32);
            else
                SetPixel(x, y, val);
        }
    }

    // leptonica: pixRenderPtaBlend (graphics.c)
    /// <summary>Renders a set of points on a 32 bpp pix, blended with the existing pixels.</summary>
    /// <param name="pta">arbitrary set of points</param>
    /// <param name="rval">red</param>
    /// <param name="gval">green</param>
    /// <param name="bval">blue</param>
    /// <param name="fract">in [0.0 - 1.0]</param>
    /// <remarks>
    /// The pix is 32 bpp rgb.
    /// (1) This function clips the rendering to pix.
    /// </remarks>
    public void RenderPtaBlend(Pta pta, byte rval, byte gval, byte bval, float fract)
    {
        const string func = "pixRenderPtaBlend";

        if (Depth != 32)
            throw Lept.Arg(func, "depth not 32 bpp");
        if (fract < 0.0 || fract > 1.0) {
            Log.Warning(func, "fract must be in [0.0, 1.0]; setting to 0.5\n");
            fract = 0.5f;
        }

        GetDimensions(out int w, out int h, out _);
        int n = pta.Count;
        float frval = fract * rval;
        float fgval = fract * gval;
        float fbval = fract * bval;
        for (int i = 0; i < n; i++) {
            pta.GetIPt(i, out int x, out int y);
            if (x < 0 || x >= w)
                continue;
            if (y < 0 || y >= h)
                continue;
            GetPixel(x, y, out uint val32);
            byte nrval = (byte)ArrayAccess.GetDataByte(val32, L.COLOR_RED);
            nrval = (byte)((1.0 - fract) * nrval + frval);
            byte ngval = (byte)ArrayAccess.GetDataByte(val32, L.COLOR_GREEN);
            ngval = (byte)((1.0 - fract) * ngval + fgval);
            byte nbval = (byte)ArrayAccess.GetDataByte(val32, L.COLOR_BLUE);
            nbval = (byte)((1.0 - fract) * nbval + fbval);
            ComposeRGBPixel(nrval, ngval, nbval, out val32);
            SetPixel(x, y, val32);
        }
    }

    /*------------------------------------------------------------------*
     *           Rendering of arbitrary shapes built with lines         *
     *------------------------------------------------------------------*/

    // leptonica: pixRenderLine (graphics.c)
    /// <summary>Renders a line by setting, clearing or flipping pixels.</summary>
    /// <param name="x1">end point 1</param>
    /// <param name="y1">end point 1</param>
    /// <param name="x2">end point 2</param>
    /// <param name="y2">end point 2</param>
    /// <param name="width">thickness of line</param>
    /// <param name="op">L_SET_PIXELS, L_CLEAR_PIXELS, L_FLIP_PIXELS</param>
    /// <remarks>The pix is any depth, not cmapped.</remarks>
    public void RenderLine(int x1, int y1, int x2, int y2, int width, int op)
    {
        const string func = "pixRenderLine";

        if (width < 1) {
            Log.Warning(func, "width must be > 0; setting to 1\n");
            width = 1;
        }
        if (op != L.L_SET_PIXELS && op != L.L_CLEAR_PIXELS && op != L.L_FLIP_PIXELS)
            throw Lept.Arg(func, "invalid op");

        Pta pta = GeneratePtaWideLine(x1, y1, x2, y2, width);
        RenderPta(pta, op);
    }

    // leptonica: pixRenderLineArb (graphics.c)
    /// <summary>Renders a line in the given color.</summary>
    /// <param name="x1">end point 1</param>
    /// <param name="y1">end point 1</param>
    /// <param name="x2">end point 2</param>
    /// <param name="y2">end point 2</param>
    /// <param name="width">thickness of line</param>
    /// <param name="rval">red</param>
    /// <param name="gval">green</param>
    /// <param name="bval">blue</param>
    /// <remarks>The pix is any depth, cmapped ok.</remarks>
    public void RenderLineArb(int x1, int y1, int x2, int y2, int width,
                              byte rval, byte gval, byte bval)
    {
        if (width < 1) {
            Log.Warning("pixRenderLineArb", "width must be > 0; setting to 1\n");
            width = 1;
        }

        Pta pta = GeneratePtaWideLine(x1, y1, x2, y2, width);
        RenderPtaArb(pta, rval, gval, bval);
    }

    // leptonica: pixRenderLineBlend (graphics.c)
    /// <summary>Renders a line blended with the existing pixels.</summary>
    /// <param name="x1">end point 1</param>
    /// <param name="y1">end point 1</param>
    /// <param name="x2">end point 2</param>
    /// <param name="y2">end point 2</param>
    /// <param name="width">thickness of line</param>
    /// <param name="rval">red</param>
    /// <param name="gval">green</param>
    /// <param name="bval">blue</param>
    /// <param name="fract">in [0.0 - 1.0]</param>
    /// <remarks>The pix is 32 bpp rgb.</remarks>
    public void RenderLineBlend(int x1, int y1, int x2, int y2, int width,
                                byte rval, byte gval, byte bval, float fract)
    {
        if (width < 1) {
            Log.Warning("pixRenderLineBlend", "width must be > 0; setting to 1\n");
            width = 1;
        }

        Pta pta = GeneratePtaWideLine(x1, y1, x2, y2, width);
        RenderPtaBlend(pta, rval, gval, bval, fract);
    }

    // leptonica: pixRenderBox (graphics.c)
    /// <summary>Renders the outline of a box by setting, clearing or flipping pixels.</summary>
    /// <param name="box">the box</param>
    /// <param name="width">thickness of box lines</param>
    /// <param name="op">L_SET_PIXELS, L_CLEAR_PIXELS, L_FLIP_PIXELS</param>
    /// <remarks>The pix is any depth, not cmapped.</remarks>
    public void RenderBox(Box box, int width, int op)
    {
        const string func = "pixRenderBox";

        if (width < 1) {
            Log.Warning(func, "width < 1; setting to 1\n");
            width = 1;
        }
        if (op != L.L_SET_PIXELS && op != L.L_CLEAR_PIXELS && op != L.L_FLIP_PIXELS)
            throw Lept.Arg(func, "invalid op");

        Pta pta = GeneratePtaBox(box, width);
        RenderPta(pta, op);
    }

    // leptonica: pixRenderBoxArb (graphics.c)
    /// <summary>Renders the outline of a box in the given color.</summary>
    /// <param name="box">the box</param>
    /// <param name="width">thickness of box lines</param>
    /// <param name="rval">red</param>
    /// <param name="gval">green</param>
    /// <param name="bval">blue</param>
    /// <remarks>The pix is any depth, cmapped ok.</remarks>
    public void RenderBoxArb(Box box, int width, byte rval, byte gval, byte bval)
    {
        if (width < 1) {
            Log.Warning("pixRenderBoxArb", "width < 1; setting to 1\n");
            width = 1;
        }

        Pta pta = GeneratePtaBox(box, width);
        RenderPtaArb(pta, rval, gval, bval);
    }

    // leptonica: pixRenderBoxBlend (graphics.c)
    /// <summary>Renders the outline of a box blended with the existing pixels.</summary>
    /// <param name="box">the box</param>
    /// <param name="width">thickness of box lines</param>
    /// <param name="rval">red</param>
    /// <param name="gval">green</param>
    /// <param name="bval">blue</param>
    /// <param name="fract">in [0.0 - 1.0]: 1.0 is no transparency;
    /// 0.0 is complete transparency (no effect)</param>
    /// <remarks>The pix is 32 bpp rgb.</remarks>
    public void RenderBoxBlend(Box box, int width, byte rval, byte gval, byte bval,
                               float fract)
    {
        if (width < 1) {
            Log.Warning("pixRenderBoxBlend", "width < 1; setting to 1\n");
            width = 1;
        }

        Pta pta = GeneratePtaBox(box, width);
        RenderPtaBlend(pta, rval, gval, bval, fract);
    }

    // leptonica: pixRenderBoxa (graphics.c)
    /// <summary>Renders the outlines of boxes by setting, clearing or flipping pixels.</summary>
    /// <param name="boxa">the boxes</param>
    /// <param name="width">thickness of line</param>
    /// <param name="op">L_SET_PIXELS, L_CLEAR_PIXELS, L_FLIP_PIXELS</param>
    /// <remarks>The pix is any depth, not cmapped.</remarks>
    public void RenderBoxa(Boxa boxa, int width, int op)
    {
        const string func = "pixRenderBoxa";

        if (width < 1) {
            Log.Warning(func, "width < 1; setting to 1\n");
            width = 1;
        }
        if (op != L.L_SET_PIXELS && op != L.L_CLEAR_PIXELS && op != L.L_FLIP_PIXELS)
            throw Lept.Arg(func, "invalid op");

        Pta pta = GeneratePtaBoxa(boxa, width, 0);
        RenderPta(pta, op);
    }

    // leptonica: pixRenderBoxaArb (graphics.c)
    /// <summary>Renders the outlines of boxes in the given color.</summary>
    /// <param name="boxa">the boxes</param>
    /// <param name="width">thickness of line</param>
    /// <param name="rval">red</param>
    /// <param name="gval">green</param>
    /// <param name="bval">blue</param>
    /// <remarks>The pix is any depth; colormapped is ok.</remarks>
    public void RenderBoxaArb(Boxa boxa, int width, byte rval, byte gval, byte bval)
    {
        if (width < 1) {
            Log.Warning("pixRenderBoxaArb", "width < 1; setting to 1\n");
            width = 1;
        }

        Pta pta = GeneratePtaBoxa(boxa, width, 0);
        RenderPtaArb(pta, rval, gval, bval);
    }

    // leptonica: pixRenderBoxaBlend (graphics.c)
    /// <summary>Renders the outlines of boxes blended with the existing pixels.</summary>
    /// <param name="boxa">the boxes</param>
    /// <param name="width">thickness of line</param>
    /// <param name="rval">red</param>
    /// <param name="gval">green</param>
    /// <param name="bval">blue</param>
    /// <param name="fract">in [0.0 - 1.0]: 1.0 is no transparency;
    /// 0.0 is complete transparency (no effect)</param>
    /// <param name="removedups">1 to remove; 0 otherwise</param>
    /// <remarks>The pix is 32 bpp rgb.</remarks>
    public void RenderBoxaBlend(Boxa boxa, int width, byte rval, byte gval, byte bval,
                                float fract, int removedups)
    {
        if (width < 1) {
            Log.Warning("pixRenderBoxaBlend", "width < 1; setting to 1\n");
            width = 1;
        }

        Pta pta = GeneratePtaBoxa(boxa, width, removedups);
        RenderPtaBlend(pta, rval, gval, bval, fract);
    }

    // leptonica: pixRenderHashBox (graphics.c)
    /// <summary>Renders hash lines in a box by setting, clearing or flipping pixels.</summary>
    /// <param name="box">the box</param>
    /// <param name="spacing">spacing between lines; must be &gt; 1</param>
    /// <param name="width">thickness of box and hash lines</param>
    /// <param name="orient">orientation of lines: L_HORIZONTAL_LINE, ...</param>
    /// <param name="outline">0 to skip drawing box outline</param>
    /// <param name="op">L_SET_PIXELS, L_CLEAR_PIXELS, L_FLIP_PIXELS</param>
    /// <remarks>The pix is any depth, not cmapped.</remarks>
    public void RenderHashBox(Box box, int spacing, int width, int orient, int outline,
                              int op)
    {
        const string func = "pixRenderHashBox";

        if (spacing <= 1)
            throw Lept.Arg(func, "spacing not > 1");
        if (width < 1) {
            Log.Warning(func, "width < 1; setting to 1\n");
            width = 1;
        }
        if (orient != L.L_HORIZONTAL_LINE && orient != L.L_POS_SLOPE_LINE &&
            orient != L.L_VERTICAL_LINE && orient != L.L_NEG_SLOPE_LINE)
            throw Lept.Arg(func, "invalid line orientation");
        if (op != L.L_SET_PIXELS && op != L.L_CLEAR_PIXELS && op != L.L_FLIP_PIXELS)
            throw Lept.Arg(func, "invalid op");

        Pta pta = GeneratePtaHashBox(box, spacing, width, orient, outline);
        RenderPta(pta, op);
    }

    // leptonica: pixRenderHashBoxArb (graphics.c)
    /// <summary>Renders hash lines in a box in the given color.</summary>
    /// <param name="box">the box</param>
    /// <param name="spacing">spacing between lines; must be &gt; 1</param>
    /// <param name="width">thickness of box and hash lines</param>
    /// <param name="orient">orientation of lines: L_HORIZONTAL_LINE, ...</param>
    /// <param name="outline">0 to skip drawing box outline</param>
    /// <param name="rval">red</param>
    /// <param name="gval">green</param>
    /// <param name="bval">blue</param>
    /// <remarks>The pix is any depth; cmapped ok.</remarks>
    public void RenderHashBoxArb(Box box, int spacing, int width, int orient, int outline,
                                 int rval, int gval, int bval)
    {
        const string func = "pixRenderHashBoxArb";

        if (spacing <= 1)
            throw Lept.Arg(func, "spacing not > 1");
        if (width < 1) {
            Log.Warning(func, "width < 1; setting to 1\n");
            width = 1;
        }
        if (orient != L.L_HORIZONTAL_LINE && orient != L.L_POS_SLOPE_LINE &&
            orient != L.L_VERTICAL_LINE && orient != L.L_NEG_SLOPE_LINE)
            throw Lept.Arg(func, "invalid line orientation");

        Pta pta = GeneratePtaHashBox(box, spacing, width, orient, outline);
        RenderPtaArb(pta, (byte)rval, (byte)gval, (byte)bval);
    }

    // leptonica: pixRenderHashBoxBlend (graphics.c)
    /// <summary>Renders hash lines in a box blended with the existing pixels.</summary>
    /// <param name="box">the box</param>
    /// <param name="spacing">spacing between lines; must be &gt; 1</param>
    /// <param name="width">thickness of box and hash lines</param>
    /// <param name="orient">orientation of lines: L_HORIZONTAL_LINE, ...</param>
    /// <param name="outline">0 to skip drawing box outline</param>
    /// <param name="rval">red</param>
    /// <param name="gval">green</param>
    /// <param name="bval">blue</param>
    /// <param name="fract">in [0.0 - 1.0]: 1.0 is no transparency;
    /// 0.0 is complete transparency (no effect)</param>
    /// <remarks>The pix is 32 bpp.</remarks>
    public void RenderHashBoxBlend(Box box, int spacing, int width, int orient, int outline,
                                   int rval, int gval, int bval, float fract)
    {
        const string func = "pixRenderHashBoxBlend";

        if (spacing <= 1)
            throw Lept.Arg(func, "spacing not > 1");
        if (width < 1) {
            Log.Warning(func, "width < 1; setting to 1\n");
            width = 1;
        }
        if (orient != L.L_HORIZONTAL_LINE && orient != L.L_POS_SLOPE_LINE &&
            orient != L.L_VERTICAL_LINE && orient != L.L_NEG_SLOPE_LINE)
            throw Lept.Arg(func, "invalid line orientation");

        Pta pta = GeneratePtaHashBox(box, spacing, width, orient, outline);
        RenderPtaBlend(pta, (byte)rval, (byte)gval, (byte)bval, fract);
    }

    // leptonica: pixRenderHashMaskArb (graphics.c)
    /// <summary>Renders hash lines through a mask in the given color.</summary>
    /// <param name="pixm">1 bpp clipping mask for hash marks</param>
    /// <param name="x">UL corner of pixm with respect to pix</param>
    /// <param name="y">UL corner of pixm with respect to pix</param>
    /// <param name="spacing">spacing between lines; must be &gt; 1</param>
    /// <param name="width">thickness of box and hash lines</param>
    /// <param name="orient">orientation of lines: L_HORIZONTAL_LINE,
    /// L_POS_SLOPE_LINE, L_VERTICAL_LINE, L_NEG_SLOPE_LINE</param>
    /// <param name="outline">0 to skip drawing box outline</param>
    /// <param name="rval">red</param>
    /// <param name="gval">green</param>
    /// <param name="bval">blue</param>
    /// <remarks>
    /// The pix is any depth; cmapped ok.
    /// (1) This is an in-place operation that renders hash lines
    ///     through a mask pixm onto pix.  The mask origin is
    ///     translated by (x,y) relative to the origin of pix.
    /// </remarks>
    public void RenderHashMaskArb(Pix pixm, int x, int y, int spacing, int width, int orient,
                                  int outline, int rval, int gval, int bval)
    {
        const string func = "pixRenderHashMaskArb";

        if (pixm.Depth != 1)
            throw Lept.Arg(func, "pixm not defined or not 1 bpp");
        if (spacing <= 1)
            throw Lept.Arg(func, "spacing not > 1");
        if (width < 1) {
            Log.Warning(func, "width < 1; setting to 1\n");
            width = 1;
        }
        if (orient != L.L_HORIZONTAL_LINE && orient != L.L_POS_SLOPE_LINE &&
            orient != L.L_VERTICAL_LINE && orient != L.L_NEG_SLOPE_LINE)
            throw Lept.Arg(func, "invalid line orientation");

            /* Get the points for masked hash lines */
        pixm.GetDimensions(out int w, out int h, out _);
        Box box1 = Box.Create(0, 0, w, h);
        Pta pta1 = GeneratePtaHashBox(box1, spacing, width, orient, outline);
        Pta pta2 = pta1.CropToMask(pixm);

            /* Clip out the region and apply the hash lines */
        Box box2 = Box.Create(x, y, w, h);
        Pix pix1 = ClipRectangle(box2, out _)!;
        pix1.RenderPtaArb(pta2, (byte)rval, (byte)gval, (byte)bval);

            /* Rasterop the altered rectangle back in place */
        Rasterop(x, y, w, h, L.PIX_SRC, pix1, 0, 0);
    }

    // leptonica: pixRenderHashBoxa (graphics.c)
    /// <summary>Renders hash lines in boxes by setting, clearing or flipping pixels.</summary>
    /// <param name="boxa">the boxes</param>
    /// <param name="spacing">spacing between lines; must be &gt; 1</param>
    /// <param name="width">thickness of box and hash lines</param>
    /// <param name="orient">orientation of lines: L_HORIZONTAL_LINE,
    /// L_POS_SLOPE_LINE, L_VERTICAL_LINE, L_NEG_SLOPE_LINE</param>
    /// <param name="outline">0 to skip drawing box outline</param>
    /// <param name="op">L_SET_PIXELS, L_CLEAR_PIXELS, L_FLIP_PIXELS</param>
    /// <remarks>The pix is any depth, not cmapped.</remarks>
    public void RenderHashBoxa(Boxa boxa, int spacing, int width, int orient, int outline,
                               int op)
    {
        const string func = "pixRenderHashBoxa";

        if (spacing <= 1)
            throw Lept.Arg(func, "spacing not > 1");
        if (width < 1) {
            Log.Warning(func, "width < 1; setting to 1\n");
            width = 1;
        }
        if (orient != L.L_HORIZONTAL_LINE && orient != L.L_POS_SLOPE_LINE &&
            orient != L.L_VERTICAL_LINE && orient != L.L_NEG_SLOPE_LINE)
            throw Lept.Arg(func, "invalid line orientation");
        if (op != L.L_SET_PIXELS && op != L.L_CLEAR_PIXELS && op != L.L_FLIP_PIXELS)
            throw Lept.Arg(func, "invalid op");

        Pta pta = GeneratePtaHashBoxa(boxa, spacing, width, orient, outline, 1);
        RenderPta(pta, op);
    }

    // leptonica: pixRenderHashBoxaArb (graphics.c)
    /// <summary>Renders hash lines in boxes in the given color.</summary>
    /// <param name="boxa">the boxes</param>
    /// <param name="spacing">spacing between lines; must be &gt; 1</param>
    /// <param name="width">thickness of box and hash lines</param>
    /// <param name="orient">orientation of lines: L_HORIZONTAL_LINE,
    /// L_POS_SLOPE_LINE, L_VERTICAL_LINE, L_NEG_SLOPE_LINE</param>
    /// <param name="outline">0 to skip drawing box outline</param>
    /// <param name="rval">red</param>
    /// <param name="gval">green</param>
    /// <param name="bval">blue</param>
    /// <remarks>The pix is any depth; cmapped ok.</remarks>
    public void RenderHashBoxaArb(Boxa boxa, int spacing, int width, int orient, int outline,
                                  int rval, int gval, int bval)
    {
        const string func = "pixRenderHashBoxaArb";

        if (spacing <= 1)
            throw Lept.Arg(func, "spacing not > 1");
        if (width < 1) {
            Log.Warning(func, "width < 1; setting to 1\n");
            width = 1;
        }
        if (orient != L.L_HORIZONTAL_LINE && orient != L.L_POS_SLOPE_LINE &&
            orient != L.L_VERTICAL_LINE && orient != L.L_NEG_SLOPE_LINE)
            throw Lept.Arg(func, "invalid line orientation");

        Pta pta = GeneratePtaHashBoxa(boxa, spacing, width, orient, outline, 1);
        RenderPtaArb(pta, (byte)rval, (byte)gval, (byte)bval);
    }

    // leptonica: pixRenderHashBoxaBlend (graphics.c)
    /// <summary>Renders hash lines in boxes blended with the existing pixels.</summary>
    /// <param name="boxa">the boxes</param>
    /// <param name="spacing">spacing between lines; must be &gt; 1</param>
    /// <param name="width">thickness of box and hash lines</param>
    /// <param name="orient">orientation of lines: L_HORIZONTAL_LINE,
    /// L_POS_SLOPE_LINE, L_VERTICAL_LINE, L_NEG_SLOPE_LINE</param>
    /// <param name="outline">0 to skip drawing box outline</param>
    /// <param name="rval">red</param>
    /// <param name="gval">green</param>
    /// <param name="bval">blue</param>
    /// <param name="fract">in [0.0 - 1.0]: 1.0 is no transparency;
    /// 0.0 is complete transparency (no effect)</param>
    /// <remarks>The pix is 32 bpp rgb.</remarks>
    public void RenderHashBoxaBlend(Boxa boxa, int spacing, int width, int orient, int outline,
                                    int rval, int gval, int bval, float fract)
    {
        const string func = "pixRenderHashBoxaBlend";

        if (spacing <= 1)
            throw Lept.Arg(func, "spacing not > 1");
        if (width < 1) {
            Log.Warning(func, "width < 1; setting to 1\n");
            width = 1;
        }
        if (orient != L.L_HORIZONTAL_LINE && orient != L.L_POS_SLOPE_LINE &&
            orient != L.L_VERTICAL_LINE && orient != L.L_NEG_SLOPE_LINE)
            throw Lept.Arg(func, "invalid line orientation");

        Pta pta = GeneratePtaHashBoxa(boxa, spacing, width, orient, outline, 1);
        RenderPtaBlend(pta, (byte)rval, (byte)gval, (byte)bval, fract);
    }

    // leptonica: pixRenderPolyline (graphics.c)
    /// <summary>Renders a polyline by setting, clearing or flipping pixels.</summary>
    /// <param name="ptas">the vertices</param>
    /// <param name="width">thickness of line</param>
    /// <param name="op">L_SET_PIXELS, L_CLEAR_PIXELS, L_FLIP_PIXELS</param>
    /// <param name="closeflag">1 to close the contour; 0 otherwise</param>
    /// <remarks>The pix is any depth, not cmapped.  This renders a closed contour.</remarks>
    public void RenderPolyline(Pta ptas, int width, int op, int closeflag)
    {
        const string func = "pixRenderPolyline";

        if (width < 1) {
            Log.Warning(func, "width < 1; setting to 1\n");
            width = 1;
        }
        if (op != L.L_SET_PIXELS && op != L.L_CLEAR_PIXELS && op != L.L_FLIP_PIXELS)
            throw Lept.Arg(func, "invalid op");

        Pta pta = GeneratePtaPolyline(ptas, width, closeflag, 0);
        RenderPta(pta, op);
    }

    // leptonica: pixRenderPolylineArb (graphics.c)
    /// <summary>Renders a polyline in the given color.</summary>
    /// <param name="ptas">the vertices</param>
    /// <param name="width">thickness of line</param>
    /// <param name="rval">red</param>
    /// <param name="gval">green</param>
    /// <param name="bval">blue</param>
    /// <param name="closeflag">1 to close the contour; 0 otherwise</param>
    /// <remarks>The pix is any depth; cmapped ok.  This renders a closed contour.</remarks>
    public void RenderPolylineArb(Pta ptas, int width, byte rval, byte gval, byte bval,
                                  int closeflag)
    {
        if (width < 1) {
            Log.Warning("pixRenderPolylineArb", "width < 1; setting to 1\n");
            width = 1;
        }

        Pta pta = GeneratePtaPolyline(ptas, width, closeflag, 0);
        RenderPtaArb(pta, rval, gval, bval);
    }

    // leptonica: pixRenderPolylineBlend (graphics.c)
    /// <summary>Renders a polyline blended with the existing pixels.</summary>
    /// <param name="ptas">the vertices</param>
    /// <param name="width">thickness of line</param>
    /// <param name="rval">red</param>
    /// <param name="gval">green</param>
    /// <param name="bval">blue</param>
    /// <param name="fract">in [0.0 - 1.0]: 1.0 is no transparency;
    /// 0.0 is complete transparency (no effect)</param>
    /// <param name="closeflag">1 to close the contour; 0 otherwise</param>
    /// <param name="removedups">1 to remove; 0 otherwise</param>
    /// <remarks>The pix is 32 bpp rgb.</remarks>
    public void RenderPolylineBlend(Pta ptas, int width, byte rval, byte gval, byte bval,
                                    float fract, int closeflag, int removedups)
    {
        if (width < 1) {
            Log.Warning("pixRenderPolylineBlend", "width < 1; setting to 1\n");
            width = 1;
        }

        Pta pta = GeneratePtaPolyline(ptas, width, closeflag, removedups);
        RenderPtaBlend(pta, rval, gval, bval, fract);
    }

    // leptonica: pixRenderGridArb (graphics.c)
    /// <summary>Renders a grid in the given color.</summary>
    /// <param name="nx">number of rectangles in the x direction</param>
    /// <param name="ny">number of rectangles in the y direction</param>
    /// <param name="width">thickness of grid lines</param>
    /// <param name="rval">red</param>
    /// <param name="gval">green</param>
    /// <param name="bval">blue</param>
    /// <remarks>The pix is any depth, cmapped ok.</remarks>
    public void RenderGridArb(int nx, int ny, int width, byte rval, byte gval, byte bval)
    {
        const string func = "pixRenderGridArb";

        if (nx < 1 || ny < 1)
            throw Lept.Arg(func, "nx, ny must be > 0");
        if (width < 1) {
            Log.Warning(func, "width < 1; setting to 1\n");
            width = 1;
        }

        GetDimensions(out int w, out int h, out _);
        Pta pta = GeneratePtaGrid(w, h, nx, ny, width);
        RenderPtaArb(pta, rval, gval, bval);
    }

    // leptonica: pixRenderRandomCmapPtaa (graphics.c)
    /// <summary>Renders the points of a Ptaa in random colors on an 8 bpp colormapped copy.</summary>
    /// <param name="ptaa">the point sets</param>
    /// <param name="polyflag">1 to interpret each Pta as a polyline;
    /// 0 to simply render the Pta as a set of pixels</param>
    /// <param name="width">thickness of line; use only for polyline</param>
    /// <param name="closeflag">1 to close the contour; 0 otherwise;
    /// use only for polyline mode</param>
    /// <returns>pixd cmapped, 8 bpp</returns>
    /// <remarks>
    /// The pix is 1, 2, 4, 8, 16, 32 bpp.
    /// (1) This is a debugging routine, that displays a set of
    ///     pixels, selected by the set of Ptas in a Ptaa,
    ///     in a random color in a pix.
    /// (2) If polyflag == 1, each Pta is considered to be a polyline,
    ///     and is rendered using width and closeflag.  Each polyline
    ///     is rendered in a random color.
    /// (3) If polyflag == 0, all points in each Pta are rendered in a
    ///     random color.  The width and closeflag parameters are ignored.
    /// (4) The output pix is 8 bpp and colormapped.  Up to 254
    ///     different, randomly selected colors, can be used.
    /// (5) The rendered pixels replace the input pixels.  They will
    ///     be clipped silently to the input pix.
    /// </remarks>
    public Pix RenderRandomCmapPtaa(Ptaa ptaa, int polyflag, int width, int closeflag)
    {
        if (polyflag != 0 && width < 1) {
            Log.Warning("pixRenderRandomCmapPtaa", "width < 1; setting to 1\n");
            width = 1;
        }

        Pix pixd = ConvertTo8(false);
        PixColormap cmap = PixColormap.CreateRandom(8, true, true);
        pixd.SetColormap(cmap);

        int n;
        if ((n = ptaa.Count) == 0)
            return pixd;

        for (int i = 0; i < n; i++) {
            int index = 1 + (i % 254);
            cmap.GetColor(index, out int rval, out int gval, out int bval);
            Pta pta = ptaa.GetPta(i, L.L_CLONE);
            Pta ptat;
            if (polyflag != 0)
                ptat = GeneratePtaPolyline(pta, width, closeflag, 0);
            else
                ptat = pta.Clone();
            pixd.RenderPtaArb(ptat, (byte)rval, (byte)gval, (byte)bval);
        }

        return pixd;
    }

    /*------------------------------------------------------------------*
     *                Rendering and filling of polygons                 *
     *------------------------------------------------------------------*/

    // leptonica: pixRenderPolygon (graphics.c)
    /// <summary>Renders the outline of a polygon on a minimum-sized 1 bpp pix.</summary>
    /// <param name="ptas">of vertices, none repeated</param>
    /// <param name="width">of polygon outline</param>
    /// <param name="pxmin">min x value of input pts</param>
    /// <param name="pymin">min y value of input pts</param>
    /// <returns>pix 1 bpp, with outline generated</returns>
    /// <remarks>
    /// (1) The pix is the minimum size required to contain the origin
    ///     and the polygon.  For example, the max x value of the input
    ///     points is w - 1, where w is the pix width.
    /// (2) The rendered line is 4-connected, so that an interior or
    ///     exterior 8-c.c. flood fill operation works properly.
    /// </remarks>
    public static Pix RenderPolygon(Pta ptas, int width, out int pxmin, out int pymin)
    {
        pxmin = 0;
        pymin = 0;

            /* Generate a 4-connected polygon line */
        Pta pta1 = GeneratePtaPolyline(ptas, width, 1, 0);
        Pta pta2;
        if (width < 2)
            pta2 = ConvertPtaLineTo4cc(pta1);
        else
            pta2 = pta1.Clone();

            /* Render onto a minimum-sized pix */
        pta2.GetRange(out float fxmin, out float fxmax, out float fymin, out float fymax);
        pxmin = (int)(fxmin + 0.5);
        pymin = (int)(fymin + 0.5);
        Pix pixd = Create((int)(fxmax + 0.5) + 1, (int)(fymax + 0.5) + 1, 1);
        pixd.RenderPolyline(pta2, width, L.L_SET_PIXELS, 1);
        return pixd;
    }

    // leptonica: pixFillPolygon (graphics.c)
    /// <summary>Fills the interior of a polygon whose 4-connected outline is given.</summary>
    /// <param name="pta">vertices of the polygon</param>
    /// <param name="xmin">min x value of vertices of polygon</param>
    /// <param name="ymin">min y value of vertices of polygon</param>
    /// <returns>pixd with outline filled, or null if there is nothing to fill</returns>
    /// <remarks>
    /// This pix is 1 bpp, with a 4-connected polygon outline.
    /// (1) This fills the interior of the polygon, returning a
    ///     new pix.  It works for both convex and non-convex polygons.
    /// (2) To generate a filled polygon from pta:
    ///       Pix pixt = Pix.RenderPolygon(pta, 1, out xmin, out ymin);
    ///       Pix pixd = pixt.FillPolygon(pta, xmin, ymin);
    /// </remarks>
    public Pix? FillPolygon(Pta pta, int xmin, int ymin)
    {
        const string func = "pixFillPolygon";

        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (pta.Count < 2)
            throw Lept.Arg(func, "pta has < 2 pts");

        GetDimensions(out int w, out int h, out _);
        int[] xstart = new int[w / 2 + 1];
        int[] xend = new int[w / 2 + 1];

            /* Find a raster with 2 or more black runs.  The first background
             * pixel after the end of the first run is likely to be inside
             * the polygon, and can be used as a seed pixel. */
        bool found = false;
        int i;
        for (i = ymin + 1; i < h; i++) {
            FindHorizontalRuns(i, xstart, xend, out int n);
            if (n > 1) {
                bool inside = pta.PtInsidePolygon(xend[0] + 1, i);
                if (inside) {
                    found = true;
                    break;
                }
            }
        }
        if (!found) {
            Log.Warning(func, "nothing found to fill\n");
            return null;
        }

            /* Place the seed pixel in the output image */
        Pix pixd = CreateTemplate(this);
        pixd.SetPixel(xend[0] + 1, i, 1);

            /* Invert pixs to make a filling mask, and fill from the seed */
        Pix pixi = Invert(null, this);
        SeedfillBinary(pixd, pixd, pixi, 4);

            /* Add the pixels of the original polygon outline */
        Or(pixd, pixd, this);

        return pixd;
    }

    /*------------------------------------------------------------------*
     *             Contour rendering on grayscale images                *
     *------------------------------------------------------------------*/

    // leptonica: pixRenderContours (graphics.c)
    /// <summary>Renders contour lines of a grayscale image.</summary>
    /// <param name="startval">value of lowest contour; must be in [0 ... maxval]</param>
    /// <param name="incr">increment to next contour; must be &gt; 0</param>
    /// <param name="outdepth">either 1 or depth of pixs</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// The pix is 8 or 16 bpp; no colormap.
    /// (1) The output can be either 1 bpp, showing just the contour
    ///     lines, or a copy of the input pixs with the contour lines
    ///     superposed.
    /// </remarks>
    public Pix RenderContours(int startval, int incr, int outdepth)
    {
        const string func = "pixRenderContours";

        if (Colormap != null)
            throw Lept.Arg(func, "pixs has colormap");
        GetDimensions(out int w, out int h, out int d);
        if (d != 8 && d != 16)
            throw Lept.Arg(func, "pixs not 8 or 16 bpp");
        if (outdepth != 1 && outdepth != d) {
            Log.Warning(func, "invalid outdepth; setting to 1\n");
            outdepth = 1;
        }
        int maxval = (1 << d) - 1;
        if (startval < 0 || startval > maxval)
            throw Lept.Arg(func, "startval not in [0 ... maxval]");
        if (incr < 1)
            throw Lept.Arg(func, "incr < 1");

        Pix pixd;
        if (outdepth == d)
            pixd = Copy(null, this);
        else
            pixd = Create(w, h, 1);

        pixd.CopyResolution(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        uint[] datas = Data;
        int wpls = Wpl;

        switch (d)
        {
        case 8:
            if (outdepth == 1) {
                for (int i = 0; i < h; i++) {
                    ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                    Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                    for (int j = 0; j < w; j++) {
                        int val = ArrayAccess.GetDataByte(lines, j);
                        if (val < startval)
                            continue;
                        int test = (val - startval) % incr;
                        if (test == 0)
                            ArrayAccess.SetDataBit(lined, j);
                    }
                }
            } else {  /* outdepth == d */
                for (int i = 0; i < h; i++) {
                    ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                    Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                    for (int j = 0; j < w; j++) {
                        int val = ArrayAccess.GetDataByte(lines, j);
                        if (val < startval)
                            continue;
                        int test = (val - startval) % incr;
                        if (test == 0)
                            ArrayAccess.SetDataByte(lined, j, 0);
                    }
                }
            }
            break;

        case 16:
            if (outdepth == 1) {
                for (int i = 0; i < h; i++) {
                    ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                    Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                    for (int j = 0; j < w; j++) {
                        int val = ArrayAccess.GetDataTwoBytes(lines, j);
                        if (val < startval)
                            continue;
                        int test = (val - startval) % incr;
                        if (test == 0)
                            ArrayAccess.SetDataBit(lined, j);
                    }
                }
            } else {  /* outdepth == d */
                for (int i = 0; i < h; i++) {
                    ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                    Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                    for (int j = 0; j < w; j++) {
                        int val = ArrayAccess.GetDataTwoBytes(lines, j);
                        if (val < startval)
                            continue;
                        int test = (val - startval) % incr;
                        if (test == 0)
                            ArrayAccess.SetDataTwoBytes(lined, j, 0);
                    }
                }
            }
            break;

        default:
            throw Lept.Arg(func, "pixs not 8 or 16 bpp");
        }

        return pixd;
    }

    /*------------------------------------------------------------------*
     *             Boundary pt generation on 1 bpp images               *
     *------------------------------------------------------------------*/

    // leptonica: pixGeneratePtaBoundary (graphics.c)
    /// <summary>Generates the boundary points of the foreground of a 1 bpp image.</summary>
    /// <param name="width">of boundary line</param>
    /// <returns>pta</returns>
    /// <remarks>
    /// The pix is 1 bpp.
    /// (1) Similar to ptaGetBoundaryPixels(), except here:
    ///     * we only get pixels in the foreground
    ///     * we can have a "line" width greater than 1 pixel.
    /// (2) Once generated, this can be applied to a random 1 bpp image
    ///     to add a color boundary as follows:
    ///        Pta pta = pixs.GeneratePtaBoundary(width);
    ///        Pix pix1 = pixs.Convert1To8Cmap();
    ///        pix1.RenderPtaArb(pta, rval, gval, bval);
    /// </remarks>
    public Pta GeneratePtaBoundary(int width)
    {
        const string func = "pixGeneratePtaBoundary";

        if (Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (width < 1) {
            Log.Warning(func, "width < 1; setting to 1\n");
            width = 1;
        }

        Pix pix1 = ErodeBrick(null, this, 2 * width + 1, 2 * width + 1);
        Xor(pix1, pix1, this);
        return Pta.GetPixelsFromPix(pix1, null);
    }
}

public sealed partial class FPix
{
    // leptonica: fpixAutoRenderContours (graphics.c)
    /// <summary>Renders contour lines of an fpix, with the increment set to get about ncontours.</summary>
    /// <param name="ncontours">in [2 ... 500]; typically about 50</param>
    /// <returns>pixd 8 bpp</returns>
    /// <remarks>
    /// (1) The increment is set to get approximately ncontours.
    /// (2) The proximity to the target value for contour display
    ///     is set to 0.15.
    /// (3) Negative values are rendered in red; positive values as black.
    /// </remarks>
    public Pix AutoRenderContours(int ncontours)
    {
        const string func = "fpixAutoRenderContours";

        if (ncontours < 2 || ncontours > 500)
            throw Lept.Arg(func, "ncontours < 2 or > 500");

        GetMin(out float minval, out _, out _);
        GetMax(out float maxval, out _, out _);
        if (minval == maxval)
            throw Lept.Arg(func, "all values in fpix are equal");
        float incr = (maxval - minval) / ((float)ncontours - 1);
        return RenderContours(incr, 0.15f);
    }

    // leptonica: fpixRenderContours (graphics.c)
    /// <summary>Renders contour lines of an fpix.</summary>
    /// <param name="incr">increment between contours; must be &gt; 0.0</param>
    /// <param name="proxim">required proximity to target value; default 0.15</param>
    /// <returns>pixd 8 bpp</returns>
    /// <remarks>
    /// (1) Values are displayed when val/incr is within +-proxim
    ///     to an integer.  The default value is 0.15; smaller values
    ///     result in thinner contour lines.
    /// (2) Negative values are rendered in red; positive values as black.
    /// </remarks>
    public Pix RenderContours(float incr, float proxim)
    {
        const string func = "fpixRenderContours";

        if (incr <= 0.0)
            throw Lept.Arg(func, "incr <= 0.0");
        if (proxim <= 0.0)
            proxim = 0.15f;  /* default */

        GetDimensions(out int w, out int h);
        Pix pixd = Pix.Create(w, h, 8);
        PixColormap cmap = PixColormap.Create(8);
        pixd.SetColormap(cmap);
        cmap.AddColor(255, 255, 255);  /* white */
        cmap.AddColor(0, 0, 0);  /* black */
        cmap.AddColor(255, 0, 0);  /* red */

        float[] datas = Data;
        int wpls = Wpl;
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        float invincr = (float)(1.0 / incr);
        for (int i = 0; i < h; i++) {
            int lines = i * wpls;
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                float val = datas[lines + j];
                float finter = invincr * val;
                float above = finter - MathF.Floor(finter);
                float below = MathF.Ceiling(finter) - finter;
                float diff = Math.Min(above, below);
                if (diff <= proxim) {
                    if (val < 0.0)
                        ArrayAccess.SetDataByte(lined, j, 2);
                    else
                        ArrayAccess.SetDataByte(lined, j, 1);
                }
            }
        }

        return pixd;
    }
}
