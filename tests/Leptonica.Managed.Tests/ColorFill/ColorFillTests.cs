using System;
using Leptonica;

namespace Leptonica.Managed.Tests.ColorFillTests;

public class ColorFillTests
{
    private static Pix MakeRgb(int w, int h, Func<int, int, (int r, int g, int b)> color)
    {
        Pix pix = Pix.Create(w, h, 32);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) {
                var (r, g, b) = color(x, y);
                Pix.ComposeRGBPixel(r, g, b, out uint pixel);
                pix.SetPixel(x, y, pixel);
            }
        return pix;
    }

    private static uint Get(Pix pix, int x, int y)
    {
        pix.GetPixel(x, y, out uint v);
        return v;
    }

    [Fact]
    public void Create_MakesTilesAndValidates()
    {
        Pix pix = Pix.Create(60, 40, 32);
        L_Colorfill cf = L_Colorfill.Create(pix, 3, 2);
        Assert.Equal((3, 2, 20, 20), (cf.Nx, cf.Ny, cf.Tw, cf.Th));
        Assert.Equal(6, cf.Boxas.Count);
        cf.Boxas.GetBoxGeometry(4, out int x, out int y, out int w, out int h);
        Assert.Equal((20, 20, 20, 20), (x, y, w, h));
        Assert.Throws<ArgumentException>(() => L_Colorfill.Create(pix, 7, 1));      // 8 pixel wide tiles
        Assert.Throws<ArgumentException>(() => L_Colorfill.Create(Pix.Create(60, 40, 8), 1, 1));
    }

    [Fact]
    public void ColorFill_SeparatesRegionsOfDifferentColor()
    {
        Pix pix = MakeRgb(30, 20, (x, y) => x < 15 ? (200, 30, 30) : (30, 30, 200));
        Pix mask = pix.ColorFill(70, 20, 1, 10, 0);
        Assert.Equal((30, 20, 1), (mask.Width, mask.Height, mask.Depth));
        Assert.Equal(1u, Get(mask, 6, 10));
        Assert.Equal(1u, Get(mask, 22, 10));
        // the two colors are never in one component
        mask.ConnComp(out Pixa comps, 8);
        Assert.Equal(2, comps.Count);
        // the outer ring is never part of a region
        Assert.Equal(0u, Get(mask, 0, 0));
        Assert.Equal(0u, Get(mask, 29, 19));
    }

    [Fact]
    public void ColorFill_DarkImageGivesEmptyMask_AndSetsNonColorToBlack()
    {
        Pix pix = MakeRgb(20, 20, (x, y) => (30, 20, 10));
        Pix mask = pix.ColorFill(70, 20, 1, 10, 0);
        Assert.Equal(0, mask.CountPixels(null));
        Assert.Equal(0u, Get(pix, 5, 5));    // pixs itself was modified
    }

    [Fact]
    public void ColorFill_SmallRegionsAreRemoved()
    {
        // a small bright patch inside a dark image
        Pix pix = MakeRgb(30, 30, (x, y) => (x >= 10 && x < 15 && y >= 10 && y < 15) ? (200, 30, 30) : (10, 10, 10));
        Pix mask = pix.ColorFill(70, 20, 1, 100, 0);
        Assert.Equal(0, mask.CountPixels(null));
        Pix pix2 = MakeRgb(30, 30, (x, y) => (x >= 10 && x < 15 && y >= 10 && y < 15) ? (200, 30, 30) : (10, 10, 10));
        Assert.True(pix2.ColorFill(70, 20, 1, 4, 0).CountPixels(null) > 0);
    }

    [Fact]
    public void ColorFill_GradualChangeStaysInOneRegion_StepSplits()
    {
        // slow ramp in red (stays within maxdiff of the seed color)
        Pix ramp = MakeRgb(40, 20, (x, y) => (150 + x / 10, 30, 30));
        ramp.ColorFill(70, 5, 1, 10, 0).ConnComp(out Pixa c1, 8);
        Assert.Equal(1, c1.Count);
        // a step of 60 in red splits the image in two regions
        Pix step = MakeRgb(40, 20, (x, y) => x < 20 ? (150, 30, 30) : (210, 30, 30));
        step.ColorFill(70, 5, 1, 10, 0).ConnComp(out Pixa c2, 8);
        Assert.Equal(2, c2.Count);
    }

    [Fact]
    public void ColorFill_Smoothing_Runs()
    {
        Pix pix = MakeRgb(30, 20, (x, y) => x < 15 ? (200, 30, 30) : (30, 30, 200));
        Pix mask = pix.ColorFill(70, 20, 3, 10, 0);
        Assert.Equal(1, mask.Depth);
        Assert.Equal(1u, Get(mask, 6, 10));
    }

    [Fact]
    public void ColorFill_RejectsNon32Bpp()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(10, 10, 8).ColorFill(70, 20, 1, 10, 0));
    }

    [Fact]
    public void ColorContentByLocation_FindsOneRegionPerTile()
    {
        Pix pix = MakeRgb(40, 20, (x, y) => x < 20 ? (200, 30, 30) : (30, 30, 200));
        L_Colorfill cf = L_Colorfill.Create(pix, 2, 1);
        Pix.ColorContentByLocation(cf, 0, 0, 0, 0, 20, 10, 1, 0);
        Assert.Equal(2, cf.Pixas!.Count);
        Assert.Equal(2, cf.Pixam!.Count);
        Pix.ComposeRGBPixel(200, 30, 30, out uint red);
        Pix.ComposeRGBPixel(30, 30, 200, out uint blue);
        for (int i = 0; i < 2; i++) {
            Numa na = cf.Naa.GetNuma(i, L.L_CLONE);
            Assert.Equal(1, na.Count);
            Assert.True(na.GetIValue(0) > 100);
            L_Dna da = cf.Dnaa.GetDna(i, L.L_CLONE);
            Assert.Equal(1, da.Count);
            Assert.Equal(i == 0 ? red : blue, (uint)da.GetDValue(0));
        }
    }

    [Fact]
    public void ColorContentByLocation_ValidatesMinmax()
    {
        Pix pix = MakeRgb(40, 20, (x, y) => (200, 30, 30));
        L_Colorfill cf = L_Colorfill.Create(pix, 2, 1);
        Assert.Throws<ArgumentException>(() => Pix.ColorContentByLocation(cf, 0, 0, 0, 201, 20, 10, 1, 0));
    }

    [Fact]
    public void MakeColorfillTestData_ProducesThreeColormappedImages()
    {
        Pixa pixa = Pix.MakeColorfillTestData(50, 40, 6, 100);
        Assert.Equal(3, pixa.Count);
        for (int i = 0; i < 3; i++) {
            Pix p = pixa.GetPix(i, L.L_CLONE);
            Assert.Equal((50, 40, 8), (p.Width, p.Height, p.Depth));
            Assert.NotNull(p.Colormap);
            Assert.Equal(7, p.Colormap!.Count);
        }
        // the data is reproducible
        Pixa again = Pix.MakeColorfillTestData(50, 40, 6, 100);
        for (int i = 0; i < 3; i++)
            Assert.True(Pix.Equal(pixa.GetPix(i, L.L_CLONE), again.GetPix(i, L.L_CLONE)));

        // the third image has black boundaries and a single color elsewhere
        Pix third = pixa.GetPix(2, L.L_CLONE);
        var colors = new System.Collections.Generic.HashSet<int>();
        for (int y = 0; y < 40; y++)
            for (int x = 0; x < 50; x++) {
                third.Colormap!.GetColor((int)Get(third, x, y), out int r, out int g, out int b);
                colors.Add((r << 16) | (g << 8) | b);
            }
        Assert.True(colors.Count <= 2);
        Assert.Contains(0, colors);
    }
}
