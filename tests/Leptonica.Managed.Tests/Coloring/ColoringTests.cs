using System;
using System.IO;
using Leptonica;
using Leptonica.Managed.Tests.Reg;

namespace Leptonica.Managed.Tests;

/// <summary>Tests of coloring.c.</summary>
public class ColoringTests
{
    private static uint Rgb(int r, int g, int b)
    {
        Pix.ComposeRGBPixel(r, g, b, out uint p);
        return p;
    }

    private static uint Get(Pix pix, int x, int y)
    {
        pix.GetPixel(x, y, out uint v);
        return v;
    }

    private static Pix Gray(params int[] vals)
    {
        Pix pix = Pix.Create(vals.Length, 1, 8);
        for (int i = 0; i < vals.Length; i++)
            pix.SetPixel(i, 0, (uint)vals[i]);
        return pix;
    }

    private static Pix RgbRow(params uint[] vals)
    {
        Pix pix = Pix.Create(vals.Length, 1, 32);
        for (int i = 0; i < vals.Length; i++)
            pix.SetPixel(i, 0, vals[i]);
        return pix;
    }

    [Fact]
    public void PixelFractionalShift_MovesTowardBlackOrWhite()
    {
        Assert.Equal(Rgb(100, 150, 200), Pix.PixelFractionalShift(100, 150, 200, 0.0f));
        Assert.Equal(Rgb(0, 0, 0), Pix.PixelFractionalShift(100, 150, 200, -1.0f));
        Assert.Equal(Rgb(255, 255, 255), Pix.PixelFractionalShift(100, 150, 200, 1.0f));
        Assert.Equal(Rgb(50, 75, 100), Pix.PixelFractionalShift(100, 150, 200, -0.5f));
        Assert.Equal(Rgb(178, 203, 228), Pix.PixelFractionalShift(100, 150, 200, 0.5f));
        Assert.Throws<ArgumentException>(() => Pix.PixelFractionalShift(1, 2, 3, 1.5f));
    }

    [Fact]
    public void LinearMapToTargetColor_MapsSourceToTargetAndKeepsTheEnds()
    {
        Pix pix = RgbRow(Rgb(0, 0, 0), Rgb(255, 255, 255), Rgb(100, 150, 200), Rgb(50, 75, 220));
        Pix pixd = Pix.LinearMapToTargetColor(null, pix, Rgb(100, 150, 200), Rgb(30, 220, 90));
        Assert.Equal(Rgb(0, 0, 0), Get(pixd, 0, 0));
        Assert.Equal(Rgb(255, 255, 255), Get(pixd, 1, 0));
        Assert.Equal(Rgb(30, 220, 90), Get(pixd, 2, 0));
        Assert.Equal(Rgb(15, 110, 90 + (255 - 90) * (220 - 200) / 55), Get(pixd, 3, 0));
        Assert.NotEqual(Get(pix, 2, 0), Get(pixd, 2, 0));          /* pixs unchanged */
        Assert.Equal(Rgb(100, 150, 200), Get(pix, 2, 0));

        /* the same mapping on a single color */
        for (int x = 0; x < 4; x++)
            Assert.Equal(Get(pixd, x, 0),
                         Pix.PixelLinearMapToTargetColor(Get(pix, x, 0), Rgb(100, 150, 200), Rgb(30, 220, 90)));

        /* in place */
        Assert.Same(pix, Pix.LinearMapToTargetColor(pix, pix, Rgb(100, 150, 200), Rgb(30, 220, 90)));
        Assert.Equal(Get(pixd, 3, 0), Get(pix, 3, 0));

        Assert.Throws<ArgumentException>(() => Pix.LinearMapToTargetColor(null, Gray(1, 2), 0, 0));
        Assert.Throws<ArgumentException>(() => Pix.LinearMapToTargetColor(pixd, pix, 0, 0));
    }

    [Fact]
    public void MapWithInvariantHue_SendsTheSourceTowardWhiteOrBlack()
    {
        uint src = Rgb(120, 60, 200);
        Pix pix = RgbRow(src);
        Assert.Equal(Rgb(255, 255, 255), Get(Pix.MapWithInvariantHue(null, pix, src, 1.0f), 0, 0));
        Assert.Equal(Rgb(0, 0, 0), Get(Pix.MapWithInvariantHue(null, pix, src, -1.0f), 0, 0));
        Assert.Equal(src, Get(Pix.MapWithInvariantHue(null, pix, src, 0.0f), 0, 0));
        uint half = Pix.PixelFractionalShift(120, 60, 200, 0.5f);
        Assert.Equal(half, Get(Pix.MapWithInvariantHue(null, pix, src, 0.5f), 0, 0));
        Assert.Throws<ArgumentException>(() => Pix.MapWithInvariantHue(null, pix, src, 2.0f));
        Assert.Throws<ArgumentException>(() => Pix.MapWithInvariantHue(null, Gray(1), src, 0.5f));
    }

    [Fact]
    public void ShiftByComponent_ColorsTheBackgroundOrForeground()
    {
        Pix pix = RgbRow(Rgb(255, 255, 255), Rgb(0, 0, 0), Rgb(128, 128, 128));
        /* white -> light yellow: the whole range is compressed toward the target */
        Pix pixd = Pix.ShiftByComponent(null, pix, 0xffffff00, Rgb(255, 245, 235));
        Assert.Equal(Rgb(255, 245, 235), Get(pixd, 0, 0));
        Assert.Equal(Rgb(0, 0, 0), Get(pixd, 1, 0));
        /* black -> dark red shifts toward white */
        Pix pixf = Pix.ShiftByComponent(null, pix, 0x0, Rgb(200, 30, 150));
        Assert.Equal(Rgb(255, 255, 255), Get(pixf, 0, 0));
        Assert.Equal(Rgb(200, 30, 150), Get(pixf, 1, 0));
        for (int x = 0; x < 3; x++) {
            Pix.ExtractRGBValues(Get(pix, x, 0), out int r, out int g, out int b);
            Assert.Equal(Get(pixf, x, 0), Pix.PixelShiftByComponent(r, g, b, 0x0, Rgb(200, 30, 150)));
        }
        Assert.Same(pix, Pix.ShiftByComponent(pix, pix, 0x0, Rgb(200, 30, 150)));
        Assert.Throws<ArgumentException>(() => Pix.ShiftByComponent(null, Gray(1, 2), 0, 0));
    }

    [Fact]
    public void ShiftByComponent_CmappedAndRgbGiveTheSameImage()
    {
        string path = Path.Combine(TestPaths.ProgDir, "harmoniam100-11.png");
        if (!File.Exists(path))
            return;     /* testdata/prog not available */
        Pix pix0 = Pix.Read(path);
        Assert.NotNull(pix0.Colormap);
        Pix pix32 = pix0.ConvertTo32();
        foreach ((uint scolor, uint dcolor) in new[] { (0xffffff00u, Rgb(255, 245, 235)),
                                                       (0xffffff00u, Rgb(235, 245, 255)),
                                                       (0x0u, Rgb(200, 30, 150)) }) {
            Pix cmapped = Pix.ShiftByComponent(null, pix0, scolor, dcolor);
            Pix rgb = Pix.ShiftByComponent(null, pix32, scolor, dcolor);
            Assert.NotNull(cmapped.Colormap);
            Assert.True(Pix.Equal(cmapped, rgb));      /* regTestComparePix(rp, pix1, pix4) */
        }
    }

    [Fact]
    public void SnapColor_8bpp_And_Rgb()
    {
        Pix gray = Gray(10, 12, 15, 20, 100);
        Pix snapped = Pix.SnapColor(null, gray, 0x0c, 0x80, 3);
        Assert.Equal(new uint[] { 0x80, 0x80, 0x80, 20, 100 },
                     new[] { Get(snapped, 0, 0), Get(snapped, 1, 0), Get(snapped, 2, 0),
                             Get(snapped, 3, 0), Get(snapped, 4, 0) });
        Assert.Equal(10u, Get(gray, 0, 0));

        Pix rgb = RgbRow(Rgb(200, 100, 50), Rgb(203, 98, 52), Rgb(206, 100, 50), Rgb(0, 0, 0));
        Pix sr = Pix.SnapColor(null, rgb, Rgb(200, 100, 50), Rgb(1, 2, 3), 3);
        Assert.Equal(Rgb(1, 2, 3), Get(sr, 0, 0));
        Assert.Equal(Rgb(1, 2, 3), Get(sr, 1, 0));
        Assert.Equal(Rgb(206, 100, 50), Get(sr, 2, 0));
        Assert.Same(rgb, Pix.SnapColor(rgb, rgb, Rgb(200, 100, 50), Rgb(1, 2, 3), 3));
        Assert.Equal(Rgb(1, 2, 3), Get(rgb, 1, 0));

        Assert.Throws<ArgumentException>(() => Pix.SnapColor(null, Pix.Create(4, 4, 4), 0, 0, 1));
        Assert.Throws<ArgumentException>(() => Pix.SnapColor(gray, snapped, 0, 0, 1));
    }

    [Fact]
    public void SnapColorCmap_AddsTheTargetColorAndRemovesUnusedColors()
    {
        Pix pix = Pix.Create(4, 1, 8);
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(255, 255, 255);
        cmap.AddColor(200, 100, 50);
        cmap.AddColor(0, 0, 0);
        pix.SetColormap(cmap);
        for (int x = 0; x < 4; x++)
            pix.SetPixel(x, 0, (uint)(x % 3));

        Pix res = Pix.SnapColorCmap(null, pix, Rgb(200, 100, 50), Rgb(10, 20, 30), 2);
        Assert.NotNull(res.Colormap);
        Assert.NotSame(pix, res);
        Assert.Equal(3, pix.Colormap!.Count);            /* pixs unchanged */
        Assert.Equal(3, res.Colormap!.Count);           /* the old color is removed, the new one added */
        res.Colormap.GetColor((int)Get(res, 1, 0), out int r, out int g, out int b);
        Assert.Equal((10, 20, 30), (r, g, b));
        res.Colormap.GetColor((int)Get(res, 0, 0), out r, out g, out b);
        Assert.Equal((255, 255, 255), (r, g, b));
        res.Colormap.GetColor((int)Get(res, 2, 0), out r, out g, out b);
        Assert.Equal((0, 0, 0), (r, g, b));

        /* in place */
        Assert.Same(pix, Pix.SnapColorCmap(pix, pix, Rgb(200, 100, 50), Rgb(10, 20, 30), 2));
        Assert.Equal(3, pix.Colormap!.Count);
        Assert.Throws<ArgumentException>(() => Pix.SnapColorCmap(null, Gray(1), 0, 0, 1));
    }

    [Fact]
    public void ColorGray_ColorsLightPixelsOfAnEightBppImageInPlace()
    {
        Pix pix = Gray(0, 64, 128, 255);
        pix.ColorGray(null, L.L_PAINT_LIGHT, 10, 255, 0, 0);
        Assert.Equal(32, pix.Depth);
        float factor = 1.0f / 255.0f;
        Assert.Equal(Rgb(0, 0, 0), Get(pix, 0, 0));       /* below the threshold: unchanged (black) */
        Assert.Equal(Rgb((int)(255 * 64 * factor), 0, 0), Get(pix, 1, 0));
        Assert.Equal(Rgb((int)(255 * 128 * factor), 0, 0), Get(pix, 2, 0));
        Assert.Equal(Rgb((int)(255 * 255 * factor), 0, 0), Get(pix, 3, 0));

        Pix dark = RgbRow(Rgb(0, 0, 0), Rgb(100, 100, 100), Rgb(255, 255, 255));
        dark.ColorGray(null, L.L_PAINT_DARK, 200, 0, 0, 255);
        Assert.Equal(Rgb(0, 0, 255), Get(dark, 0, 0));
        Assert.Equal(Rgb((int)((255.0 - 0) * 100 * factor), (int)((255.0 - 0) * 100 * factor), 255),
                     Get(dark, 1, 0));
        Assert.Equal(Rgb(255, 255, 255), Get(dark, 2, 0));    /* above the threshold: unchanged */

        Pix region = RgbRow(Rgb(200, 200, 200), Rgb(200, 200, 200), Rgb(200, 200, 200));
        region.ColorGray(Box.Create(1, 0, 1, 1), L.L_PAINT_LIGHT, 0, 255, 0, 0);
        Assert.Equal(Rgb(200, 200, 200), Get(region, 0, 0));
        Assert.NotEqual(Rgb(200, 200, 200), Get(region, 1, 0));
        Assert.Equal(Rgb(200, 200, 200), Get(region, 2, 0));

        Assert.Throws<ArgumentException>(() => Gray(1).ColorGray(null, L.L_PAINT_LIGHT, 255, 1, 2, 3));
        Assert.Throws<ArgumentException>(() => Gray(1).ColorGray(null, L.L_PAINT_DARK, 0, 1, 2, 3));
        Assert.Throws<ArgumentException>(() => Gray(1).ColorGray(null, 7, 5, 1, 2, 3));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 4).ColorGray(null, L.L_PAINT_LIGHT, 5, 1, 2, 3));
    }

    [Fact]
    public void ColorGrayMasked_ColorsOnlyUnderTheMask()
    {
        Pix gray = Gray(200, 200, 200, 200);
        Pix mask = Pix.Create(3, 1, 1);        /* narrower than the image */
        mask.SetPixel(1, 0, 1);
        Pix res = gray.ColorGrayMasked(mask, L.L_PAINT_LIGHT, 0, 255, 0, 0);
        Assert.Equal(32, res.Depth);
        Assert.Equal(8, gray.Depth);                   /* pixs unchanged */
        Assert.Equal(Rgb(200, 200, 200), Get(res, 0, 0));
        Assert.NotEqual(Rgb(200, 200, 200), Get(res, 1, 0));
        Assert.Equal(Rgb(200, 200, 200), Get(res, 2, 0));
        Assert.Equal(Rgb(200, 200, 200), Get(res, 3, 0));
        Assert.Throws<ArgumentException>(() => gray.ColorGrayMasked(gray, L.L_PAINT_LIGHT, 0, 1, 2, 3));
        Assert.Throws<ArgumentException>(() => gray.ColorGrayMasked(mask, L.L_PAINT_LIGHT, 255, 1, 2, 3));
    }

    [Fact]
    public void ColorGrayRegions_ColorsInsideTheBoxesOnly()
    {
        Pix gray = Gray(200, 200, 200, 200);
        Boxa boxa = Boxa.Create(2);
        boxa.AddBox(Box.Create(0, 0, 1, 1), L.L_INSERT);
        boxa.AddBox(Box.Create(3, 0, 1, 1), L.L_INSERT);
        Pix res = gray.ColorGrayRegions(boxa, L.L_PAINT_LIGHT, 0, 255, 0, 0);
        Assert.Equal(32, res.Depth);
        Assert.NotEqual(Rgb(200, 200, 200), Get(res, 0, 0));
        Assert.Equal(Rgb(200, 200, 200), Get(res, 1, 0));
        Assert.NotEqual(Rgb(200, 200, 200), Get(res, 3, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 1).ColorGrayRegions(boxa, L.L_PAINT_LIGHT, 0, 1, 2, 3));
        Assert.Throws<ArgumentException>(() => gray.ColorGrayRegions(boxa, 9, 0, 1, 2, 3));

        /* a gray colormapped image stays colormapped */
        Pix cmapped = gray.ConvertTo8(true);
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(200, 200, 200);
        cmap.AddColor(0, 0, 0);
        Pix withmap = Pix.Create(4, 1, 8);
        withmap.SetColormap(cmap);
        Pix resmap = withmap.ColorGrayRegions(boxa, L.L_PAINT_LIGHT, 0, 255, 0, 0);
        Assert.Equal(8, resmap.Depth);
        Assert.NotNull(resmap.Colormap);
    }
}
