using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Morph;

/// <summary>Tests of morphapp.c on small synthetic images.</summary>
public class MorphAppTests
{
    private static uint Get(Pix p, int x, int y)
    {
        p.GetPixel(x, y, out uint v);
        return v;
    }

    private static Pix Rect(int w, int h, int x0, int y0, int rw, int rh)
    {
        Pix pix = Pix.Create(w, h, 1);
        for (int y = y0; y < y0 + rh; y++)
            for (int x = x0; x < x0 + rw; x++)
                pix.SetPixel(x, y, 1);
        return pix;
    }

    [Fact]
    public void ExtractBoundary_IsTheRing()
    {
        Pix pix = Rect(12, 12, 3, 3, 6, 6);
        Pix fg = pix.ExtractBoundary(1);
        Assert.Equal(20, fg.CountPixels(null));     /* 6x6 square minus the 4x4 inside */
        Assert.Equal(1u, Get(fg, 3, 3));
        Assert.Equal(0u, Get(fg, 5, 5));
        Pix bg = pix.ExtractBoundary(0);
        Assert.Equal(28, bg.CountPixels(null));     /* 8x8 square minus the 6x6 inside */
        Assert.Equal(1u, Get(bg, 2, 2));
        Assert.Equal(0u, Get(bg, 3, 3));
    }

    [Fact]
    public void UnionAndIntersectionOfMorphOps()
    {
        Pix pix = Rect(20, 20, 5, 5, 6, 6);
        Sela sela = Sela.Create(2);
        sela.AddSel(Sel.CreateBrick(1, 3, 0, 1, L.SEL_HIT), "h", L.L_COPY);
        sela.AddSel(Sel.CreateBrick(3, 1, 1, 0, L.SEL_HIT), "v", L.L_COPY);
        Assert.Equal(60, pix.UnionOfMorphOps(sela, L.L_MORPH_DILATE).CountPixels(null));
        Assert.Equal(36, pix.IntersectionOfMorphOps(sela, L.L_MORPH_DILATE).CountPixels(null));
        Assert.Throws<ArgumentException>(() => pix.UnionOfMorphOps(sela, 99));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).UnionOfMorphOps(sela, L.L_MORPH_ERODE));
    }

    [Fact]
    public void SeedfillMorph_FillsTheComponentOfTheSeed()
    {
        Pix mask = Rect(30, 10, 2, 2, 8, 4);
        Pix2Add(mask, 20, 2, 5, 4);
        Pix seed = Pix.Create(30, 10, 1);
        seed.SetPixel(3, 3, 1);
        Pix res = seed.SeedfillMorph(mask, 0, 8);
        Assert.Equal(32, res.CountPixels(null));
        Assert.Equal(0u, Get(res, 21, 3));
        Pix one = seed.SeedfillMorph(mask, 1, 8);
        Assert.Equal(9, one.CountPixels(null));
    }

    private static void Pix2Add(Pix pix, int x0, int y0, int rw, int rh)
    {
        for (int y = y0; y < y0 + rh; y++)
            for (int x = x0; x < x0 + rw; x++)
                pix.SetPixel(x, y, 1);
    }

    [Fact]
    public void SelectiveConnCompFill_FillsHolesOfLargeComponentsOnly()
    {
        Pix pix = Rect(40, 20, 2, 2, 12, 12);
        Pix2Clear(pix, 6, 6, 4, 4);
        Pix2Add(pix, 20, 2, 5, 5);
        Pix2Clear(pix, 22, 4, 1, 1);
        Pix res = pix.SelectiveConnCompFill(8, 10, 10);
        Assert.Equal(1u, Get(res, 7, 7));          /* hole of the big c.c. filled */
        Assert.Equal(0u, Get(res, 22, 4));         /* small c.c. untouched */
        Assert.Equal(pix.CountPixels(null) + 16, res.CountPixels(null));
    }

    private static void Pix2Clear(Pix pix, int x0, int y0, int rw, int rh)
    {
        for (int y = y0; y < y0 + rh; y++)
            for (int x = x0; x < x0 + rw; x++)
                pix.SetPixel(x, y, 0);
    }

    [Fact]
    public void MorphSequenceByComponent_DilationIsClippedToTheComponent()
    {
        Pix pix = Rect(30, 12, 2, 2, 5, 5);
        Pix2Add(pix, 15, 3, 6, 4);
        Pix res = pix.MorphSequenceByComponent("d3.3", 8, 0, 0, out Boxa boxa);
        Assert.Equal(2, boxa.Count);
        Assert.True(Pix.Equal(pix, res));
        Pix big = pix.MorphSequenceByComponent("e3.3", 8, 6, 0, out Boxa boxa2);
        Assert.Equal(1, boxa2.Count);
        Assert.Equal(8, big.CountPixels(null));     /* only the 6x4 component, eroded to 4x2 */
    }

    [Fact]
    public void RunHistogramMorph_CountsRuns()
    {
        Pix pix = Pix.Create(40, 3, 1);
        for (int x = 2; x < 5; x++) pix.SetPixel(x, 1, 1);      /* run of 3 */
        for (int x = 10; x < 15; x++) pix.SetPixel(x, 1, 1);    /* run of 5 */
        Numa na = pix.RunHistogramMorph(L.L_RUN_ON, L.L_HORIZ, 10);
        Assert.Equal(1.0f, na.GetFValue(3));
        Assert.Equal(1.0f, na.GetFValue(5));
        Assert.Equal(0.0f, na.GetFValue(4));
        Assert.Throws<ArgumentException>(() => pix.RunHistogramMorph(2, L.L_HORIZ, 10));
    }

    [Fact]
    public void Tophat_RemovesTheBackground()
    {
        Pix pix = Pix.Create(21, 21, 8);
        for (int y = 0; y < 21; y++)
            for (int x = 0; x < 21; x++)
                pix.SetPixel(x, y, 50);
        pix.SetPixel(10, 10, 200);
        Pix white = pix.Tophat(5, 5, L.L_TOPHAT_WHITE);
        Assert.Equal(150u, Get(white, 10, 10));
        Assert.Equal(0u, Get(white, 3, 3));
        Pix black = pix.Tophat(5, 5, L.L_TOPHAT_BLACK);
        Assert.Equal(0u, Get(black, 10, 10));
        Assert.Equal(0, (int)Get(pix.Tophat(1, 1, L.L_TOPHAT_WHITE), 10, 10));
        Assert.Throws<ArgumentException>(() => pix.Tophat(5, 5, 7));
    }

    [Fact]
    public void HDome_ClipsPeaksAtTheHeight()
    {
        Pix pix = Pix.Create(21, 21, 8);
        for (int y = 0; y < 21; y++)
            for (int x = 0; x < 21; x++)
                pix.SetPixel(x, y, 20);
        pix.SetPixel(10, 10, 120);
        Pix d = pix.HDome(40, 4);
        Assert.Equal(40u, Get(d, 10, 10));
        Assert.Equal(0u, Get(d, 2, 2));
        Assert.Equal(0u, Get(pix.HDome(0, 4), 10, 10));
    }

    [Fact]
    public void MorphGradient_IsZeroOnFlatImage()
    {
        Pix pix = Pix.Create(15, 15, 8);
        for (int y = 0; y < 15; y++)
            for (int x = 0; x < 15; x++)
                pix.SetPixel(x, y, 77);
        Assert.Equal(0u, Get(pix.MorphGradient(3, 3, 0), 7, 7));
        pix.SetPixel(7, 7, 177);
        Assert.Equal(100u, Get(pix.MorphGradient(3, 3, 0), 6, 7));
    }

    [Fact]
    public void Centroid_1bpp_And_8bpp()
    {
        Pix pix = Rect(70, 20, 40, 4, 5, 3);
        pix.Centroid(null, null, out float x, out float y);
        Assert.Equal(42.0f, x);
        Assert.Equal(5.0f, y);
        Pix g = Pix.Create(10, 10, 8);
        g.SetPixel(2, 3, 10);
        g.SetPixel(4, 3, 10);
        g.Centroid(null, null, out x, out y);
        Assert.Equal(3.0f, x);
        Assert.Equal(3.0f, y);
        Pix empty = Pix.Create(10, 10, 1);
        empty.Centroid(null, null, out x, out y);
        Assert.Equal(0.0f, x);
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 4).Centroid(null, null, out _, out _));
    }

    [Fact]
    public void PixaCentroids_And_ExtendByMorph()
    {
        Pixa pixa = Pixa.Create(2);
        pixa.AddPix(Rect(9, 9, 2, 2, 3, 3), L.L_INSERT);
        pixa.AddPix(Rect(8, 8, 0, 0, 2, 2), L.L_INSERT);
        Pta pta = pixa.Centroids();
        pta.GetPt(0, out float x, out float y);
        Assert.Equal(3.0f, x);
        Assert.Equal(3.0f, y);
        Pixa ext = pixa.ExtendByMorph(L.L_MORPH_DILATE, 2, null, 1);
        Assert.Equal(6, ext.Count);
        Assert.Equal(9, ext.GetPix(0, L.L_CLONE).CountPixels(null));
        Assert.True(ext.GetPix(2, L.L_CLONE).CountPixels(null) > 9);
        Assert.Same(pixa.Copy(L.L_CLONE).GetPix(0, L.L_CLONE), pixa.ExtendByMorph(L.L_MORPH_DILATE, 0, null, 1).GetPix(0, L.L_CLONE));
    }

    [Fact]
    public void ExtendByScaling_ScalesEachPix()
    {
        Pixa pixa = Pixa.Create(1);
        pixa.AddPix(Pix.Create(10, 20, 8), L.L_INSERT);
        Numa nasc = Numa.Create(2);
        nasc.AddNumber(0.5f);
        nasc.AddNumber(2.0f);
        Pixa res = pixa.ExtendByScaling(nasc, L.L_HORIZ, 1);
        Assert.Equal(3, res.Count);
        res.GetPixDimensions(1, out int w, out int h, out _);
        Assert.Equal((5, 20), (w, h));
        res.GetPixDimensions(2, out w, out h, out _);
        Assert.Equal((20, 20), (w, h));
    }

    [Fact]
    public void RemoveMatchedPattern_SubtractsThePattern()
    {
        Pix pix = Rect(30, 30, 10, 10, 5, 5);
        Pix pixp = Rect(5, 5, 0, 0, 5, 5);
        Pix pixe = Pix.Create(30, 30, 1);
        pixe.SetPixel(12, 12, 1);
        pix.RemoveMatchedPattern(pixp, pixe, 2, 2, 0);
        Assert.Equal(0, pix.CountPixels(null));
    }
}
