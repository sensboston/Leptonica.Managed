using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Colormap;

public class PaintCmapTests
{
    private static uint Get(Pix pix, int x, int y)
    {
        pix.GetPixel(x, y, out uint v);
        return v;
    }

    private static Pix Cmapped(int w, int h, int d, uint val, params (int r, int g, int b)[] colors)
    {
        Pix pix = Pix.Create(w, h, d);
        PixColormap cmap = PixColormap.Create(d);
        foreach (var (r, g, b) in colors)
            cmap.AddColor(r, g, b);
        pix.SetColormap(cmap);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                pix.SetPixel(j, i, val);
        return pix;
    }

    private static int[] Rgb(PixColormap cmap, int index)
    {
        cmap.GetColor(index, out int r, out int g, out int b);
        return new[] { r, g, b };
    }

    [Fact]
    public void SetSelectCmap_RegionAndNewColor()
    {
        Pix pix = Cmapped(8, 6, 8, 1, (0, 0, 0), (255, 255, 255));
        pix.SetSelectCmap(Box.Create(1, 1, 3, 2), 1, 255, 0, 0);
        Assert.Equal(3, pix.Colormap!.Count);
        Assert.Equal(new[] { 255, 0, 0 }, Rgb(pix.Colormap, 2));
        Assert.Equal(2u, Get(pix, 1, 1));
        Assert.Equal(2u, Get(pix, 3, 2));
        Assert.Equal(1u, Get(pix, 4, 2));
        Assert.Equal(1u, Get(pix, 0, 0));
        Assert.Equal(1u, Get(pix, 1, 3));
    }

    [Fact]
    public void SetSelectCmap_ExistingColorAndWholeImageAndClipping()
    {
        Pix pix = Cmapped(8, 6, 8, 1, (0, 0, 0), (255, 255, 255));
        pix.SetSelectCmap(Box.Create(-3, -3, 6, 6), 1, 0, 0, 0);   /* clipped to (0..2, 0..2) */
        Assert.Equal(2, pix.Colormap!.Count);
        Assert.Equal(0u, Get(pix, 2, 2));
        Assert.Equal(1u, Get(pix, 3, 3));
        pix.SetSelectCmap(null, 1, 0, 0, 0);
        Assert.Equal(0u, Get(pix, 7, 5));
    }

    [Fact]
    public void SetSelectCmap_OtherDepths()
    {
        Pix p1 = Cmapped(8, 3, 1, 1, (255, 255, 255), (0, 0, 0));
        p1.SetSelectCmap(null, 1, 255, 255, 255);   /* index 0 */
        Assert.Equal(0u, Get(p1, 3, 1));
        Pix p2 = Cmapped(5, 3, 2, 2, (0, 0, 0), (128, 128, 128), (255, 255, 255));
        p2.SetSelectCmap(null, 2, 128, 128, 128);
        Assert.Equal(1u, Get(p2, 4, 2));
        Pix p4 = Cmapped(5, 3, 4, 2, (0, 0, 0), (128, 128, 128), (255, 255, 255));
        p4.SetSelectCmap(null, 2, 1, 2, 3);
        Assert.Equal(3u, Get(p4, 1, 1));
    }

    [Fact]
    public void SetSelectCmap_InvalidArguments()
    {
        Pix pix = Cmapped(4, 4, 8, 0, (0, 0, 0), (255, 255, 255));
        Assert.Throws<ArgumentException>(() => pix.SetSelectCmap(null, 2, 1, 1, 1));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).SetSelectCmap(null, 0, 1, 1, 1));
        Assert.Throws<ArgumentException>(() => Cmapped(4, 4, 16, 0).SetSelectCmap(null, 0, 1, 1, 1));
    }

    private static Pix GrayRow()
    {
        Pix pix = Cmapped(3, 1, 8, 0, (0, 0, 0), (128, 128, 128), (255, 255, 255), (10, 20, 30));
        pix.SetPixel(0, 0, 0);
        pix.SetPixel(1, 0, 1);
        pix.SetPixel(2, 0, 2);
        return pix;
    }

    [Fact]
    public void ColorGrayCmap_Light()
    {
        Pix pix = GrayRow();
        pix.ColorGrayCmap(null, L.L_PAINT_LIGHT, 255, 0, 0);
        PixColormap cmap = pix.Colormap!;
        /* gray 128 -> 128 * 255 / 255; white -> full color; black and the color entry are skipped */
        Assert.Equal(6, cmap.Count);
        Assert.Equal(new[] { 128, 0, 0 }, Rgb(cmap, 4));
        Assert.Equal(new[] { 255, 0, 0 }, Rgb(cmap, 5));
        Assert.Equal(0u, Get(pix, 0, 0));
        Assert.Equal(4u, Get(pix, 1, 0));
        Assert.Equal(5u, Get(pix, 2, 0));
    }

    [Fact]
    public void ColorGrayCmap_Dark()
    {
        Pix pix = GrayRow();
        pix.ColorGrayCmap(null, L.L_PAINT_DARK, 0, 0, 255);
        PixColormap cmap = pix.Colormap!;
        /* white is skipped; black -> (0, 0, 255), gray 128 -> (128, 128, 255) */
        Assert.Equal(6, cmap.Count);
        Assert.Equal(new[] { 0, 0, 255 }, Rgb(cmap, 4));
        Assert.Equal(new[] { 128, 128, 255 }, Rgb(cmap, 5));
        Assert.Equal(4u, Get(pix, 0, 0));
        Assert.Equal(5u, Get(pix, 1, 0));
        Assert.Equal(2u, Get(pix, 2, 0));
    }

    [Fact]
    public void ColorGrayCmap_BoxLimitsTheRegionAndTwoBppBecomesEight()
    {
        Pix pix = Cmapped(3, 1, 2, 0, (0, 0, 0), (128, 128, 128), (255, 255, 255));
        pix.SetPixel(0, 0, 1);
        pix.SetPixel(1, 0, 1);
        pix.SetPixel(2, 0, 1);
        pix.ColorGrayCmap(Box.Create(1, 0, 1, 1), L.L_PAINT_LIGHT, 255, 0, 0);
        Assert.Equal(8, pix.Depth);
        Assert.Equal(1u, Get(pix, 0, 0));
        Assert.Equal(3u, Get(pix, 1, 0));
        Assert.Equal(1u, Get(pix, 2, 0));
    }

    [Fact]
    public void ColorGrayRegionsCmap_OverlappingBoxes()
    {
        Pix pix = GrayRow();
        Boxa boxa = Boxa.Create(2);
        boxa.AddBox(Box.Create(0, 0, 2, 1), L.L_COPY);
        boxa.AddBox(Box.Create(1, 0, 2, 1), L.L_COPY);   /* (1,0) already recolored by the first */
        pix.ColorGrayRegionsCmap(boxa, L.L_PAINT_LIGHT, 255, 0, 0);
        Assert.Equal(4u, Get(pix, 1, 0));
        Assert.Equal(5u, Get(pix, 2, 0));
        Assert.Equal(0u, Get(pix, 0, 0));
    }

    [Fact]
    public void ColorGrayCmap_ColormapFull()
    {
        Pix pix = Pix.Create(4, 1, 8);
        PixColormap cmap = PixColormap.Create(8);
        for (int i = 0; i < 256; i++)
            cmap.AddColor(i, i, i);
        pix.SetColormap(cmap);
        Assert.Throws<LeptonicaException>(() => pix.ColorGrayCmap(null, L.L_PAINT_LIGHT, 255, 0, 0));
    }

    [Fact]
    public void ColorGrayCmap_InvalidArguments()
    {
        Pix pix = GrayRow();
        Assert.Throws<ArgumentException>(() => pix.ColorGrayCmap(null, 7, 1, 2, 3));
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 3, 8).ColorGrayCmap(null, L.L_PAINT_DARK, 1, 2, 3));
        Assert.Throws<ArgumentException>(() => Cmapped(3, 3, 1, 0, (0, 0, 0), (255, 255, 255))
                                                   .ColorGrayCmap(null, L.L_PAINT_DARK, 1, 2, 3));
    }

    [Fact]
    public void ColorGrayMaskedCmap_OnlyUnderTheMask()
    {
        Pix pix = GrayRow();
        Pix mask = Pix.Create(3, 1, 1);
        mask.SetPixel(2, 0, 1);
        pix.ColorGrayMaskedCmap(mask, L.L_PAINT_LIGHT, 255, 0, 0);
        Assert.Equal(1u, Get(pix, 1, 0));    /* not under the mask */
        Assert.Equal(5u, Get(pix, 2, 0));
        Assert.Throws<ArgumentException>(() => pix.ColorGrayMaskedCmap(Pix.Create(3, 1, 8), L.L_PAINT_LIGHT, 1, 2, 3));
    }

    [Fact]
    public void AddColorizedGrayToCmap_Table()
    {
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(0, 0, 0);
        cmap.AddColor(100, 100, 100);
        cmap.AddColor(1, 2, 3);
        Assert.True(cmap.AddColorizedGrayToCmap(L.L_PAINT_LIGHT, 255, 255, 0, out Numa? na));
        Assert.Equal(3, na!.Count);
        Assert.Equal(256, na.GetIValue(0));     /* black */
        Assert.Equal(3, na.GetIValue(1));
        Assert.Equal(256, na.GetIValue(2));     /* not gray */
        /* 255 * 100 / 255 = 100 */
        Assert.Equal(new[] { 100, 100, 0 }, Rgb(cmap, 3));
        Assert.Throws<ArgumentException>(() => cmap.AddColorizedGrayToCmap(0, 1, 1, 1, out _));
    }

    [Fact]
    public void SetMaskedCmap_PaintsThroughTheMaskWithOffsetAndClipping()
    {
        Pix pix = Cmapped(8, 6, 8, 0, (0, 0, 0), (255, 255, 255));
        Pix mask = Pix.Create(3, 3, 1);
        mask.SetPixel(0, 0, 1);
        mask.SetPixel(2, 2, 1);
        mask.SetPixel(1, 1, 1);
        pix.SetMaskedCmap(mask, 4, 2, 10, 20, 30);
        Assert.Equal(3, pix.Colormap!.Count);
        Assert.Equal(2u, Get(pix, 4, 2));
        Assert.Equal(2u, Get(pix, 5, 3));
        Assert.Equal(2u, Get(pix, 6, 4));
        Assert.Equal(0u, Get(pix, 5, 2));
        /* existing color is reused; negative origin clips the mask */
        pix.SetMaskedCmap(mask, -1, -1, 255, 255, 255);
        Assert.Equal(3, pix.Colormap.Count);
        Assert.Equal(1u, Get(pix, 0, 0));       /* mask (1,1) */
        Assert.Equal(1u, Get(pix, 1, 1));       /* mask (2,2) */
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void SetMaskedCmap_LowDepths(int d)
    {
        Pix pix = Cmapped(5, 5, d, 0, (0, 0, 0), (255, 255, 255));
        Pix mask = Pix.Create(5, 5, 1);
        mask.SetPixel(3, 3, 1);
        pix.SetMaskedCmap(mask, 0, 0, 7, 8, 9);
        Assert.Equal(2u, Get(pix, 3, 3));
        Assert.Equal(0u, Get(pix, 2, 2));
    }

    [Fact]
    public void SetMaskedCmap_NullMaskAndInvalidArguments()
    {
        Pix pix = Cmapped(4, 4, 8, 0, (0, 0, 0), (255, 255, 255));
        pix.SetMaskedCmap(null, 0, 0, 1, 2, 3);
        Assert.Equal(2, pix.Colormap!.Count);
        Assert.Throws<ArgumentException>(() => pix.SetMaskedCmap(Pix.Create(2, 2, 8), 0, 0, 1, 2, 3));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).SetMaskedCmap(Pix.Create(2, 2, 1), 0, 0, 1, 2, 3));
        Assert.Throws<ArgumentException>(() => Cmapped(4, 4, 1, 0, (0, 0, 0), (255, 255, 255))
                                                   .SetMaskedCmap(Pix.Create(2, 2, 1), 0, 0, 1, 2, 3));
    }

    [Fact]
    public void SetSelectMaskedCmap_OnlyThePixelsOfTheSelectedIndex()
    {
        Pix pix = Cmapped(6, 2, 8, 0, (0, 0, 0), (255, 255, 255));
        pix.SetPixel(1, 0, 1);
        pix.SetPixel(2, 0, 1);
        Pix mask = Pix.Create(3, 1, 1);
        mask.SetPixel(0, 0, 1);
        mask.SetPixel(1, 0, 1);
        mask.SetPixel(2, 0, 1);
        pix.SetSelectMaskedCmap(mask, 0, 0, 1, 9, 9, 9);   /* index 1 pixels under the mask -> new color 2 */
        Assert.Equal(0u, Get(pix, 0, 0));
        Assert.Equal(2u, Get(pix, 1, 0));
        Assert.Equal(2u, Get(pix, 2, 0));
        pix.SetSelectMaskedCmap(null, 0, 0, 1, 9, 9, 9);   /* no-op */
        Assert.Throws<ArgumentException>(() => pix.SetSelectMaskedCmap(mask, 0, 0, 5, 9, 9, 9));
        Assert.Throws<ArgumentException>(() => Cmapped(4, 4, 1, 0, (0, 0, 0), (255, 255, 255))
                                                   .SetSelectMaskedCmap(mask, 0, 0, 1, 9, 9, 9));
    }
}
