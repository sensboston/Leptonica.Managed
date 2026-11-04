using System;
using System.Collections.Generic;
using Leptonica;

namespace Leptonica.Managed.Tests.PixTests;

public class GraphicsTests
{
    private static List<(int x, int y)> Points(Pta pta)
    {
        var list = new List<(int, int)>();
        for (int i = 0; i < pta.Count; i++) {
            pta.GetIPt(i, out int x, out int y);
            list.Add((x, y));
        }
        return list;
    }

    private static int CountOn(Pix pix)
    {
        int n = 0;
        for (int y = 0; y < pix.Height; y++)
            for (int x = 0; x < pix.Width; x++) {
                pix.GetPixel(x, y, out uint v);
                if (v != 0) n++;
            }
        return n;
    }

    private static uint Get(Pix pix, int x, int y)
    {
        pix.GetPixel(x, y, out uint v);
        return v;
    }

    /*---------------------------- Pta generation -----------------------*/

    [Fact]
    public void GeneratePtaLine_Shallow()
    {
        Pta pta = Pix.GeneratePtaLine(0, 0, 4, 2);
        Assert.Equal(new[] { (0, 0), (1, 1), (2, 1), (3, 2), (4, 2) }, Points(pta));
    }

    [Fact]
    public void GeneratePtaLine_Vertical_And_Reversed()
    {
        Assert.Equal(new[] { (2, 0), (2, 1), (2, 2), (2, 3) },
                     Points(Pix.GeneratePtaLine(2, 0, 2, 3)));
        Assert.Equal(new[] { (2, 3), (2, 2), (2, 1), (2, 0) },
                     Points(Pix.GeneratePtaLine(2, 3, 2, 0)));
    }

    [Fact]
    public void GeneratePtaLine_SamePoint()
    {
        Assert.Equal(new[] { (5, 7) }, Points(Pix.GeneratePtaLine(5, 7, 5, 7)));
    }

    [Fact]
    public void GeneratePtaWideLine_AddsParallelLines()
    {
        Pta pta = Pix.GeneratePtaWideLine(0, 5, 3, 5, 3);
        // the center line, one above, one below
        Assert.Equal(12, pta.Count);
        var pts = Points(pta);
        Assert.Contains((0, 4), pts);
        Assert.Contains((3, 6), pts);
    }

    [Fact]
    public void GeneratePtaBox_OddWidthHasPerimeterPoints()
    {
        Pta pta = Pix.GeneratePtaBox(Box.Create(1, 1, 4, 3), 1);
        Assert.Equal(10, pta.Count);
        Assert.Equal(10, new HashSet<(int, int)>(Points(pta)).Count);
    }

    [Fact]
    public void GeneratePtaBox_ZeroSizeThrows()
    {
        Assert.Throws<ArgumentException>(() => Pix.GeneratePtaBox(Box.Create(1, 1, 0, 3), 1));
    }

    [Fact]
    public void GeneratePtaBoxa_KeepDups()
    {
        Boxa boxa = Boxa.Create(2);
        boxa.AddBox(Box.Create(0, 0, 5, 5), L.L_INSERT);
        boxa.AddBox(Box.Create(0, 0, 5, 5), L.L_INSERT);
        Assert.Equal(32, Pix.GeneratePtaBoxa(boxa, 1, 0).Count);
    }

    [Fact]
    public void GeneratePtaBoxa_RemoveDups()
    {
        Boxa boxa = Boxa.Create(2);
        boxa.AddBox(Box.Create(0, 0, 5, 5), L.L_INSERT);
        boxa.AddBox(Box.Create(0, 0, 5, 5), L.L_INSERT);
        Assert.Equal(16, Pix.GeneratePtaBoxa(boxa, 1, 1).Count);
    }

    [Fact]
    public void GeneratePtaHashBox_Horizontal()
    {
        Pta pta = Pix.GeneratePtaHashBox(Box.Create(0, 0, 10, 10), 5, 1, L.L_HORIZONTAL_LINE, 0);
        // n = 1 + 10/5 = 3 lines of 10 points
        Assert.Equal(30, pta.Count);
    }

    [Fact]
    public void GeneratePtaHashBox_InvalidArgs()
    {
        Box box = Box.Create(0, 0, 10, 10);
        Assert.Throws<ArgumentException>(() => Pix.GeneratePtaHashBox(box, 1, 1, L.L_HORIZONTAL_LINE, 0));
        Assert.Throws<ArgumentException>(() => Pix.GeneratePtaHashBox(box, 5, 1, 17, 0));
    }

    [Fact]
    public void GeneratePtaaBoxa_Corners()
    {
        Boxa boxa = Boxa.Create(1);
        boxa.AddBox(Box.Create(2, 3, 4, 5), L.L_INSERT);
        Ptaa ptaa = Pix.GeneratePtaaBoxa(boxa);
        Assert.Equal(1, ptaa.Count);
        Assert.Equal(new[] { (2, 3), (5, 3), (5, 7), (2, 7) },
                     Points(ptaa.GetPta(0, L.L_CLONE)));
    }

    [Fact]
    public void GeneratePtaPolyline_Closed()
    {
        Pta ptas = Pta.Create(3);
        ptas.AddPt(0, 0);
        ptas.AddPt(3, 0);
        ptas.AddPt(3, 3);
        Pta open = Pix.GeneratePtaPolyline(ptas, 1, 0, 0);
        Pta closed = Pix.GeneratePtaPolyline(ptas, 1, 1, 0);
        Assert.Equal(8, open.Count);
        Assert.Equal(12, closed.Count);
    }

    [Fact]
    public void GeneratePtaPolyline_RemoveDups()
    {
        Pta ptas = Pta.Create(3);
        ptas.AddPt(0, 0);
        ptas.AddPt(3, 0);
        ptas.AddPt(3, 3);
        // closed triangle: 4 distinct points on the top edge, 3 more on the right edge
        // and 2 more on the closing diagonal
        Assert.Equal(9, Pix.GeneratePtaPolyline(ptas, 1, 1, 1).Count);
    }

    [Fact]
    public void GeneratePtaPolyline_FewerThanTwoPoints()
    {
        Pta ptas = Pta.Create(1);
        ptas.AddPt(1, 1);
        Assert.Equal(0, Pix.GeneratePtaPolyline(ptas, 1, 1, 0).Count);
    }

    [Fact]
    public void GeneratePtaGrid_Validates()
    {
        Assert.Throws<ArgumentException>(() => Pix.GeneratePtaGrid(10, 10, 0, 2, 1));
        Assert.Throws<ArgumentException>(() => Pix.GeneratePtaGrid(3, 10, 2, 2, 1));
    }

    [Fact]
    public void GeneratePtaGrid_Points()
    {
        Assert.True(Pix.GeneratePtaGrid(20, 20, 2, 2, 1).Count > 0);
    }

    [Fact]
    public void ConvertPtaLineTo4cc_AddsCornerPoints()
    {
        Pta ptas = Pta.Create(2);
        ptas.AddPt(0, 0);
        ptas.AddPt(1, 1);
        Assert.Equal(new[] { (0, 0), (1, 0), (1, 1) }, Points(Pix.ConvertPtaLineTo4cc(ptas)));
    }

    [Fact]
    public void FilledCircle_And_Square()
    {
        Assert.Equal(9, Pix.GeneratePtaFilledCircle(1).Count);
        Assert.Equal(21, Pix.GeneratePtaFilledCircle(2).Count);
        Assert.Throws<ArgumentException>(() => Pix.GeneratePtaFilledCircle(0));
        Assert.Equal(9, Pix.GeneratePtaFilledSquare(3).Count);
        Assert.Throws<ArgumentException>(() => Pix.GeneratePtaFilledSquare(0));
    }

    [Fact]
    public void GeneratePtaLineFromPt_Horizontal()
    {
        Pta pta = Pix.GeneratePtaLineFromPt(1, 1, 4.0, 0.0);
        Assert.Equal(new[] { (1, 1), (2, 1), (3, 1), (4, 1) }, Points(pta));
    }

    [Fact]
    public void LocatePtRadially_Axes()
    {
        Pix.LocatePtRadially(3, 4, 2.0, 0.0, out double x, out double y);
        Assert.Equal(5.0, x);
        Assert.Equal(4.0, y);
    }

    [Fact]
    public void MakePlotPtaFromNumaGen_NoWiden()
    {
        Numa na = Numa.Create(3);
        na.AddNumber(0f);
        na.AddNumber(1f);
        na.AddNumber(2f);
        Pta pta = Pix.MakePlotPtaFromNumaGen(na, L.L_HORIZONTAL_LINE, 1, 20, 10, 0);
        // scale = 10 / 2 = 5
        Assert.Equal(new[] { (0, 20), (1, 25), (2, 30) }, Points(pta));
        Assert.Throws<ArgumentException>(() =>
            Pix.MakePlotPtaFromNumaGen(na, 77, 1, 20, 10, 0));
    }

    [Fact]
    public void MakePlotPtaFromNuma_InvalidPlotloc()
    {
        Numa na = Numa.Create(1);
        na.AddNumber(1f);
        Assert.Throws<ArgumentException>(() => Pix.MakePlotPtaFromNuma(na, 100, 99, 1, 10));
    }

    /*------------------------------- Rendering -------------------------*/

    [Fact]
    public void RenderPta_Set_Clear_Flip_AndClip()
    {
        Pix pix = Pix.Create(8, 8, 1);
        Pta pta = Pta.Create(0);
        pta.AddPt(1, 1);
        pta.AddPt(-2, 3);
        pta.AddPt(3, 100);
        pta.AddPt(7, 7);
        pix.RenderPta(pta, L.L_SET_PIXELS);
        Assert.Equal(2, CountOn(pix));
        pix.RenderPta(pta, L.L_FLIP_PIXELS);
        Assert.Equal(0, CountOn(pix));
        pix.RenderPta(pta, L.L_FLIP_PIXELS);
        pix.RenderPta(pta, L.L_CLEAR_PIXELS);
        Assert.Equal(0, CountOn(pix));
    }

    [Fact]
    public void RenderPta_SetOnGray8_Is255()
    {
        Pix pix = Pix.Create(4, 4, 8);
        Pta pta = Pta.Create(0);
        pta.AddPt(2, 2);
        pix.RenderPta(pta, L.L_SET_PIXELS);
        Assert.Equal(255u, Get(pix, 2, 2));
    }

    [Fact]
    public void RenderPta_Invalid()
    {
        Pix pix = Pix.Create(4, 4, 8);
        Pta pta = Pta.Create(0);
        Assert.Throws<ArgumentException>(() => pix.RenderPta(pta, 9));
        pix.SetColormap(PixColormap.Create(8));
        Assert.Throws<ArgumentException>(() => pix.RenderPta(pta, L.L_SET_PIXELS));
    }

    [Fact]
    public void RenderPtaArb_Depths()
    {
        Pta pta = Pta.Create(0);
        pta.AddPt(1, 1);

        Pix p2 = Pix.Create(4, 4, 2);
        p2.RenderPtaArb(pta, 255, 255, 255);
        Assert.Equal(3u, Get(p2, 1, 1));

        Pix p4 = Pix.Create(4, 4, 4);
        p4.RenderPtaArb(pta, 255, 255, 255);
        Assert.Equal(15u, Get(p4, 1, 1));

        Pix p8 = Pix.Create(4, 4, 8);
        p8.RenderPtaArb(pta, 30, 60, 90);
        Assert.Equal(60u, Get(p8, 1, 1));

        Pix p32 = Pix.Create(4, 4, 32);
        p32.RenderPtaArb(pta, 10, 20, 30);
        Pix.ComposeRGBPixel(10, 20, 30, out uint expected);
        Assert.Equal(expected, Get(p32, 1, 1));

        Pix p16 = Pix.Create(4, 4, 16);
        Assert.Throws<ArgumentException>(() => p16.RenderPtaArb(pta, 1, 2, 3));
    }

    [Fact]
    public void RenderPtaArb_Colormapped_UsesNearestColor()
    {
        Pix pix = Pix.Create(4, 4, 8);
        PixColormap cmap = PixColormap.Create(8);
        pix.SetColormap(cmap);
        Pta pta = Pta.Create(0);
        pta.AddPt(2, 1);
        pix.RenderPtaArb(pta, 200, 10, 10);
        uint idx = Get(pix, 2, 1);
        cmap.GetColor((int)idx, out int r, out int g, out int b);
        Assert.Equal((200, 10, 10), (r, g, b));
    }

    [Fact]
    public void RenderPtaBlend_HalfTransparency()
    {
        Pix pix = Pix.Create(4, 4, 32);
        Pta pta = Pta.Create(0);
        pta.AddPt(1, 1);
        pix.RenderPtaBlend(pta, 200, 100, 50, 0.5f);
        Pix.ComposeRGBPixel(100, 50, 25, out uint expected);
        Assert.Equal(expected, Get(pix, 1, 1));
        Assert.Equal(0u, Get(pix, 0, 0));
    }

    [Fact]
    public void RenderPtaBlend_RequiresRgb()
    {
        Pix pix = Pix.Create(4, 4, 8);
        Assert.Throws<ArgumentException>(() =>
            pix.RenderPtaBlend(Pta.Create(0), 1, 2, 3, 0.5f));
    }

    [Fact]
    public void RenderLine_And_Box()
    {
        Pix pix = Pix.Create(12, 12, 1);
        pix.RenderLine(0, 0, 9, 0, 1, L.L_SET_PIXELS);
        Assert.Equal(10, CountOn(pix));

        Pix pix2 = Pix.Create(12, 12, 1);
        pix2.RenderBox(Box.Create(2, 2, 5, 4), 1, L.L_SET_PIXELS);
        Assert.Equal(14, CountOn(pix2));
        Assert.Throws<ArgumentException>(() =>
            pix2.RenderBox(Box.Create(2, 2, 5, 4), 1, 0));
    }

    [Fact]
    public void RenderBoxa_And_Arb_Variants()
    {
        Boxa boxa = Boxa.Create(1);
        boxa.AddBox(Box.Create(1, 1, 6, 6), L.L_INSERT);

        Pix pix = Pix.Create(10, 10, 8);
        pix.RenderBoxa(boxa, 1, L.L_SET_PIXELS);
        Assert.Equal(20, CountOn(pix));

        Pix rgb = Pix.Create(10, 10, 32);
        rgb.RenderBoxaArb(boxa, 1, 255, 0, 0);
        Assert.Equal(20, CountOn(rgb));
        rgb.RenderBoxArb(Box.Create(0, 0, 3, 3), 1, 0, 255, 0);
        rgb.RenderLineArb(0, 9, 9, 9, 1, 0, 0, 255);
        rgb.RenderBoxBlend(Box.Create(2, 2, 3, 3), 1, 255, 255, 255, 0.5f);
        rgb.RenderBoxaBlend(boxa, 1, 255, 255, 255, 0.5f, 0);
        rgb.RenderLineBlend(0, 8, 9, 8, 1, 255, 255, 255, 0.5f);
        Assert.True(CountOn(rgb) > 20);
    }

    [Fact]
    public void RenderHashBox_Set()
    {
        Pix pix = Pix.Create(12, 12, 1);
        pix.RenderHashBox(Box.Create(0, 0, 10, 10), 5, 1, L.L_HORIZONTAL_LINE, 0, L.L_SET_PIXELS);
        Assert.Equal(30, CountOn(pix));
        Assert.Throws<ArgumentException>(() =>
            pix.RenderHashBox(Box.Create(0, 0, 10, 10), 5, 1, L.L_HORIZONTAL_LINE, 0, 7));
    }

    [Fact]
    public void RenderHashBoxa_Variants_Run()
    {
        Boxa boxa = Boxa.Create(1);
        boxa.AddBox(Box.Create(0, 0, 10, 10), L.L_INSERT);

        Pix pix = Pix.Create(12, 12, 1);
        pix.RenderHashBoxa(boxa, 5, 1, L.L_VERTICAL_LINE, 1, L.L_SET_PIXELS);
        Assert.True(CountOn(pix) > 0);

        Pix rgb = Pix.Create(12, 12, 32);
        rgb.RenderHashBoxaArb(boxa, 4, 1, L.L_POS_SLOPE_LINE, 0, 255, 0, 0);
        Assert.True(CountOn(rgb) > 0);
        Pix rgb2 = Pix.Create(12, 12, 32);
        rgb2.RenderHashBoxaBlend(boxa, 4, 1, L.L_NEG_SLOPE_LINE, 0, 255, 0, 0, 0.5f);
        Assert.True(CountOn(rgb2) > 0);
        rgb2.RenderHashBoxArb(Box.Create(0, 0, 10, 10), 4, 1, L.L_HORIZONTAL_LINE, 0, 0, 0, 255);
        rgb2.RenderHashBoxBlend(Box.Create(0, 0, 10, 10), 4, 1, L.L_HORIZONTAL_LINE, 0, 0, 0, 255, 0.5f);
    }

    [Fact]
    public void RenderHashMaskArb_OnlyInsideMask()
    {
        Pix pix = Pix.Create(20, 20, 8);
        Pix mask = Pix.Create(6, 6, 1);
        mask.Rasterop(0, 0, 3, 6, L.PIX_SET, null, 0, 0);  // left half on
        pix.RenderHashMaskArb(mask, 5, 5, 2, 1, L.L_VERTICAL_LINE, 0, 255, 255, 255);
        int inside = 0, outside = 0;
        for (int y = 0; y < 20; y++)
            for (int x = 0; x < 20; x++) {
                if (Get(pix, x, y) == 0) continue;
                if (x >= 5 && x < 8 && y >= 5 && y < 11) inside++; else outside++;
            }
        Assert.True(inside > 0);
        Assert.Equal(0, outside);
    }

    [Fact]
    public void RenderPolyline_Variants()
    {
        Pta ptas = Pta.Create(3);
        ptas.AddPt(0, 0);
        ptas.AddPt(5, 0);
        ptas.AddPt(5, 5);
        Pix pix = Pix.Create(8, 8, 1);
        pix.RenderPolyline(ptas, 1, L.L_SET_PIXELS, 0);
        Assert.Equal(11, CountOn(pix));
        Pix rgb = Pix.Create(8, 8, 32);
        rgb.RenderPolylineArb(ptas, 1, 255, 0, 0, 1);
        Assert.True(CountOn(rgb) > 11);
        rgb.RenderPolylineBlend(ptas, 1, 0, 255, 0, 0.5f, 1, 0);
    }

    [Fact]
    public void RenderGridArb_Draws()
    {
        Pix pix = Pix.Create(20, 20, 1);
        pix.RenderGridArb(2, 2, 1, 0, 0, 0);
        Assert.True(CountOn(pix) > 0);
    }

    [Fact]
    public void RenderGridArb_Validates()
    {
        Pix pix = Pix.Create(20, 20, 1);
        Assert.Throws<ArgumentException>(() => pix.RenderGridArb(0, 2, 1, 0, 0, 0));
    }

    [Fact]
    public void RenderRandomCmapPtaa_ProducesCmappedGray()
    {
        Pix pix = Pix.Create(10, 10, 8);
        Boxa boxa = Boxa.Create(2);
        boxa.AddBox(Box.Create(1, 1, 4, 4), L.L_INSERT);
        boxa.AddBox(Box.Create(4, 4, 5, 5), L.L_INSERT);
        Ptaa ptaa = Pix.GeneratePtaaBoxa(boxa);
        Pix pixd = pix.RenderRandomCmapPtaa(ptaa, 1, 1, 1);
        Assert.Equal(8, pixd.Depth);
        Assert.NotNull(pixd.Colormap);
        Assert.NotEqual(0u, Get(pixd, 1, 1));
        Assert.Equal(0u, Get(pixd, 0, 9));
    }

    [Fact]
    public void RenderPlotFromNuma_ConvertsTo32()
    {
        Numa na = Numa.Create(3);
        na.AddNumber(0f);
        na.AddNumber(1f);
        na.AddNumber(2f);
        Pix pix = Pix.Create(10, 40, 8);
        Pix.RenderPlotFromNuma(ref pix, na, L.L_PLOT_AT_TOP, 1, 5, 0xff000000);
        Assert.Equal(32, pix.Depth);
        Assert.True(CountOn(pix) > 0);
    }

    /*------------------------------- Polygons --------------------------*/

    [Fact]
    public void RenderPolygon_MinimumSize()
    {
        Pta ptas = Pta.Create(3);
        ptas.AddPt(2, 2);
        ptas.AddPt(8, 2);
        ptas.AddPt(5, 8);
        Pix pix = Pix.RenderPolygon(ptas, 1, out int xmin, out int ymin);
        Assert.Equal((2, 2), (xmin, ymin));
        Assert.Equal((9, 9), (pix.Width, pix.Height));
        Assert.Equal(1, pix.Depth);
        Assert.Equal(1u, Get(pix, 2, 2));
        Assert.Equal(1u, Get(pix, 8, 2));
    }

    [Fact]
    public void FillPolygon_FillsInterior()
    {
        Pta ptas = Pta.Create(4);
        ptas.AddPt(1, 1);
        ptas.AddPt(9, 1);
        ptas.AddPt(9, 9);
        ptas.AddPt(1, 9);
        Pix outline = Pix.RenderPolygon(ptas, 1, out int xmin, out int ymin);
        Pix filled = outline.FillPolygon(ptas, xmin, ymin)!;
        Assert.Equal(81, CountOn(filled));
    }

    [Fact]
    public void FillPolygon_RequiresBinary()
    {
        Pix pix = Pix.Create(8, 8, 8);
        Pta pta = Pta.Create(2);
        pta.AddPt(1, 1);
        pta.AddPt(2, 2);
        Assert.Throws<ArgumentException>(() => pix.FillPolygon(pta, 0, 0));
    }

    /*------------------------------- Contours --------------------------*/

    [Fact]
    public void RenderContours_Binary8And16()
    {
        Pix pix = Pix.Create(4, 1, 8);
        pix.SetPixel(0, 0, 5);
        pix.SetPixel(1, 0, 10);
        pix.SetPixel(2, 0, 15);
        pix.SetPixel(3, 0, 20);
        Pix c1 = pix.RenderContours(10, 10, 1);
        Assert.Equal(new uint[] { 0, 1, 0, 1 },
                     new[] { Get(c1, 0, 0), Get(c1, 1, 0), Get(c1, 2, 0), Get(c1, 3, 0) });
        Pix c8 = pix.RenderContours(10, 10, 8);
        Assert.Equal(new uint[] { 5, 0, 15, 0 },
                     new[] { Get(c8, 0, 0), Get(c8, 1, 0), Get(c8, 2, 0), Get(c8, 3, 0) });

        Pix p16 = Pix.Create(3, 1, 16);
        p16.SetPixel(0, 0, 100);
        p16.SetPixel(1, 0, 150);
        p16.SetPixel(2, 0, 200);
        Pix c16 = p16.RenderContours(100, 100, 16);
        Assert.Equal(new uint[] { 0, 150, 0 },
                     new[] { Get(c16, 0, 0), Get(c16, 1, 0), Get(c16, 2, 0) });
    }

    [Fact]
    public void RenderContours_Invalid()
    {
        Pix pix = Pix.Create(4, 1, 8);
        Assert.Throws<ArgumentException>(() => pix.RenderContours(300, 10, 1));
        Assert.Throws<ArgumentException>(() => pix.RenderContours(0, 0, 1));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 1, 4).RenderContours(0, 1, 1));
    }

    [Fact]
    public void FPixRenderContours_NegativeInRed()
    {
        FPix fpix = FPix.Create(3, 1);
        fpix.SetPixel(0, 0, -2f);
        fpix.SetPixel(1, 0, 0.5f);
        fpix.SetPixel(2, 0, 3f);
        Pix pix = fpix.RenderContours(1f, 0.15f);
        Assert.Equal(new uint[] { 2, 0, 1 },
                     new[] { Get(pix, 0, 0), Get(pix, 1, 0), Get(pix, 2, 0) });
        Assert.Equal(3, pix.Colormap!.Count);
    }

    [Fact]
    public void FPixAutoRenderContours_ValidatesCount()
    {
        FPix fpix = FPix.Create(3, 1);
        Assert.Throws<ArgumentException>(() => fpix.AutoRenderContours(1));
        Assert.Throws<ArgumentException>(() => fpix.AutoRenderContours(501));
    }

    [Fact]
    public void FPixAutoRenderContours_Validates()
    {
        FPix fpix = FPix.Create(3, 1);
        Assert.Throws<ArgumentException>(() => fpix.AutoRenderContours(10));  // all equal
        fpix.SetPixel(2, 0, 10f);
        Assert.Equal(8, fpix.AutoRenderContours(11).Depth);
    }

    /*------------------------------- Boundary --------------------------*/

    [Fact]
    public void GeneratePtaBoundary_RingOfBlock()
    {
        Pix pix = Pix.Create(15, 15, 1);
        pix.Rasterop(4, 4, 7, 7, L.PIX_SET, null, 0, 0);
        Pta pta = pix.GeneratePtaBoundary(1);
        Assert.Equal(24, pta.Count);
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).GeneratePtaBoundary(1));
    }
}
