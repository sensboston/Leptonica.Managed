using System;
using Leptonica;

namespace Leptonica.Managed.Tests.ColorQuant1Tests;

public class ColorQuant1Tests
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

    private static (int r, int g, int b) CmapColorAt(Pix pixd, int x, int y)
    {
        pixd.GetPixel(x, y, out uint index);
        pixd.Colormap!.GetColor((int)index, out int r, out int g, out int b);
        return (r, g, b);
    }

    private static uint Get(Pix pix, int x, int y)
    {
        pix.GetPixel(x, y, out uint v);
        return v;
    }

    /*------------------------------ index tables -----------------------*/

    [Fact]
    public void MakeRGBToIndexTables_InterleavesBits()
    {
        for (int level = 1; level <= 6; level++) {
            Pix.MakeRGBToIndexTables(level, out uint[] rtab, out uint[] gtab, out uint[] btab);
            for (int v = 0; v < 256; v += 5) {
                uint expectedR = 0, expectedG = 0, expectedB = 0;
                for (int k = 0; k < level; k++) {
                    int bit = 7 - k;                       // r7, r6, ...
                    int shift = 3 * (level - 1 - k);       // position of the triple
                    expectedR |= (uint)(((v >> bit) & 1) << (shift + 2));
                    expectedG |= (uint)(((v >> bit) & 1) << (shift + 1));
                    expectedB |= (uint)(((v >> bit) & 1) << shift);
                }
                Assert.Equal(expectedR, rtab[v]);
                Assert.Equal(expectedG, gtab[v]);
                Assert.Equal(expectedB, btab[v]);
            }
        }
    }

    [Fact]
    public void MakeRGBToIndexTables_RejectsBadLevels()
    {
        Assert.Throws<ArgumentException>(() => Pix.MakeRGBToIndexTables(0, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => Pix.MakeRGBToIndexTables(7, out _, out _, out _));
    }

    [Fact]
    public void GetOctcubeIndexFromRGB_Level1And2()
    {
        Pix.MakeRGBToIndexTables(1, out uint[] r1, out uint[] g1, out uint[] b1);
        Assert.Equal(4u, Pix.GetOctcubeIndexFromRGB(255, 0, 0, r1, g1, b1));
        Assert.Equal(2u, Pix.GetOctcubeIndexFromRGB(0, 255, 0, r1, g1, b1));
        Assert.Equal(1u, Pix.GetOctcubeIndexFromRGB(0, 0, 255, r1, g1, b1));
        Assert.Equal(7u, Pix.GetOctcubeIndexFromRGB(200, 200, 200, r1, g1, b1));
        Pix.MakeRGBToIndexTables(2, out uint[] r2, out uint[] g2, out uint[] b2);
        Assert.Equal(63u, Pix.GetOctcubeIndexFromRGB(255, 255, 255, r2, g2, b2));
    }

    [Fact]
    public void GetRGBFromOctcube_IsCenterOfCube()
    {
        Pix.GetRGBFromOctcube(0, 1, out int r, out int g, out int b);
        Assert.Equal((64, 64, 64), (r, g, b));
        Pix.GetRGBFromOctcube(7, 1, out r, out g, out b);
        Assert.Equal((192, 192, 192), (r, g, b));
        Pix.GetRGBFromOctcube(4, 1, out r, out g, out b);
        Assert.Equal((192, 64, 64), (r, g, b));
    }

    /*------------------------------- histogram -------------------------*/

    [Fact]
    public void OctcubeHistogram_CountsOccupiedCubes()
    {
        Pix pix = MakeRgb(4, 2, (x, y) => x < 3 ? (0, 0, 0) : (255, 255, 255));
        Numa na = pix.OctcubeHistogram(1, out int ncolors);
        Assert.Equal(2, ncolors);
        Assert.Equal(8, na.Count);
        Assert.Equal(6, na.GetIValue(0));
        Assert.Equal(2, na.GetIValue(7));
        Assert.Equal(0, na.GetIValue(3));
    }

    [Fact]
    public void NumberOccupiedOctcubes_MinCount()
    {
        Pix pix = MakeRgb(4, 2, (x, y) => x < 3 ? (0, 0, 0) : (255, 255, 255));
        Assert.Equal(2, pix.NumberOccupiedOctcubes(1, 1, -1f));
        Assert.Equal(1, pix.NumberOccupiedOctcubes(1, 3, -1f));
        Assert.Equal(2, pix.NumberOccupiedOctcubes(3, 0, -1f));
        Assert.Throws<ArgumentException>(() => pix.NumberOccupiedOctcubes(1, -1, -1f));
        Assert.Throws<ArgumentException>(() => pix.NumberOccupiedOctcubes(1, 1, 0.5f));
        Assert.Throws<ArgumentException>(() => pix.NumberOccupiedOctcubes(0, 1, -1f));
    }

    /*------------------------------ few colors -------------------------*/

    [Fact]
    public void FewColorsOctcubeQuant1_ThreeColors()
    {
        Pix pix = MakeRgb(6, 1, (x, y) => (x % 3) switch { 0 => (0, 0, 0), 1 => (255, 0, 0), _ => (255, 255, 255) });
        Pix pixd = pix.FewColorsOctcubeQuant1(1)!;
        Assert.Equal(2, pixd.Depth);
        Assert.Equal(3, pixd.Colormap!.Count);
        // the colormap is ordered by octcube index: black (0), red (4), white (7)
        Assert.Equal((0, 0, 0), CmapColorAt(pixd, 0, 0));
        Assert.Equal((255, 0, 0), CmapColorAt(pixd, 1, 0));
        Assert.Equal((255, 255, 255), CmapColorAt(pixd, 2, 0));
        Assert.Equal(0u, Get(pixd, 0, 0));
        Assert.Equal(1u, Get(pixd, 1, 0));
        Assert.Equal(2u, Get(pixd, 5, 0));
    }

    [Fact]
    public void FewColorsOctcubeQuant1_AveragesWithinCube()
    {
        Pix pix = MakeRgb(2, 1, (x, y) => x == 0 ? (10, 20, 30) : (30, 40, 50));
        Pix pixd = pix.FewColorsOctcubeQuant1(1)!;
        Assert.Equal(1, pixd.Colormap!.Count);
        Assert.Equal((20, 30, 40), CmapColorAt(pixd, 0, 0));
        Assert.Equal((20, 30, 40), CmapColorAt(pixd, 1, 0));
    }

    [Fact]
    public void FewColorsOctcubeQuant1_TooManyColorsReturnsNull()
    {
        // 300 distinct level-6 cubes along the diagonal-ish pattern
        Pix pix = MakeRgb(20, 20, (x, y) => ((x * 12) % 256, (y * 12) % 256, ((x + y) * 6) % 256));
        Pix? pixd = pix.FewColorsOctcubeQuant1(6);
        Assert.Null(pixd);
    }

    [Fact]
    public void FewColorsOctcubeQuant1_InvalidArgs()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 2, 8).FewColorsOctcubeQuant1(3));
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 2, 32).FewColorsOctcubeQuant1(0));
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 2, 32).FewColorsOctcubeQuant1(7));
    }

    [Fact]
    public void FewColorsOctcubeQuant2_UsesFirstColorOfEachCube()
    {
        Pix pix = MakeRgb(4, 1, (x, y) => x < 2 ? (0, 0, 0) : (255, 255, 255));
        Numa na = pix.OctcubeHistogram(3, out int ncolors);
        Pix pixd = pix.FewColorsOctcubeQuant2(3, na, ncolors, out int nerrors);
        Assert.Equal(0, nerrors);
        Assert.Equal(2, pixd.Depth);
        Assert.Equal(2, pixd.Colormap!.Count);
        Assert.Equal((0, 0, 0), CmapColorAt(pixd, 0, 0));
        Assert.Equal((255, 255, 255), CmapColorAt(pixd, 3, 0));
    }

    [Fact]
    public void FewColorsOctcubeQuant2_CountsInexactPixels()
    {
        // two different colors in the same level 3 cube
        Pix pix = MakeRgb(2, 1, (x, y) => x == 0 ? (10, 10, 10) : (12, 12, 12));
        Numa na = pix.OctcubeHistogram(3, out int ncolors);
        Assert.Equal(1, ncolors);
        Pix pixd = pix.FewColorsOctcubeQuant2(3, na, ncolors, out int nerrors);
        Assert.Equal(1, nerrors);
        Assert.Equal((10, 10, 10), CmapColorAt(pixd, 1, 0));
    }

    [Fact]
    public void FewColorsOctcubeQuant2_InvalidArgs()
    {
        Pix pix = Pix.Create(2, 2, 32);
        Numa na = pix.OctcubeHistogram(3, out int ncolors);
        Assert.Throws<ArgumentException>(() => pix.FewColorsOctcubeQuant2(2, na, ncolors, out _));
        Assert.Throws<ArgumentException>(() => pix.FewColorsOctcubeQuant2(3, na, 257, out _));
    }

    /*----------------------------- fixed octcubes ----------------------*/

    [Fact]
    public void FixedOctcubeQuant256_NoDither_MapsToCellIndex()
    {
        Pix pix = MakeRgb(3, 1, (x, y) => x switch { 0 => (255, 255, 255), 1 => (0, 0, 0), _ => (0xe0, 0, 0x40) });
        Pix pixd = pix.FixedOctcubeQuant256(0);
        Assert.Equal(8, pixd.Depth);
        Assert.Equal(256, pixd.Colormap!.Count);
        Assert.Equal(255u, Get(pixd, 0, 0));
        Assert.Equal(0u, Get(pixd, 1, 0));
        Assert.Equal(0xe1u, Get(pixd, 2, 0));
        Assert.Equal((0xf0, 0xf0, 0xe0), CmapColorAt(pixd, 0, 0));
        Assert.Equal((0x10, 0x10, 0x20), CmapColorAt(pixd, 1, 0));
    }

    [Fact]
    public void FixedOctcubeQuant256_Dither_OfFlatColorIsStable()
    {
        // a flat image of a cell center has no error to diffuse
        Pix pix = MakeRgb(300, 4, (x, y) => (0xf0, 0xf0, 0xe0));
        Pix pixd = pix.FixedOctcubeQuant256(1);
        for (int x = 0; x < 300; x += 37)
            Assert.Equal((0xf0, 0xf0, 0xe0), CmapColorAt(pixd, x, 2));
    }

    [Fact]
    public void FixedOctcubeQuantGenRGB_QuantizesToCubeCenters()
    {
        Pix pix = MakeRgb(2, 1, (x, y) => x == 0 ? (0, 0, 0) : (255, 255, 255));
        Pix pixd = pix.FixedOctcubeQuantGenRGB(1);
        Assert.Equal(32, pixd.Depth);
        Pix.ComposeRGBPixel(64, 64, 64, out uint dark);
        Pix.ComposeRGBPixel(192, 192, 192, out uint light);
        Assert.Equal(dark, Get(pixd, 0, 0));
        Assert.Equal(light, Get(pixd, 1, 0));
        Assert.Throws<ArgumentException>(() => pix.FixedOctcubeQuantGenRGB(0));
    }

    /*----------------------------- from a colormap ---------------------*/

    [Fact]
    public void ToOctcubeLUT_BlackAndWhite()
    {
        PixColormap cmap = PixColormap.Create(2);
        cmap.AddColor(0, 0, 0);
        cmap.AddColor(255, 255, 255);
        int[] tab = cmap.ToOctcubeLUT(1, L.L_EUCLIDEAN_DISTANCE);
        Assert.Equal(new[] { 0, 0, 0, 1, 0, 1, 1, 1 }, tab);
        int[] tab2 = cmap.ToOctcubeLUT(1, L.L_MANHATTAN_DISTANCE);
        Assert.Equal(8, tab2.Length);
        Assert.Equal(0, tab2[0]);
        Assert.Equal(1, tab2[7]);
        Assert.Throws<ArgumentException>(() => cmap.ToOctcubeLUT(0, L.L_EUCLIDEAN_DISTANCE));
        Assert.Throws<ArgumentException>(() => cmap.ToOctcubeLUT(1, 99));
    }

    [Fact]
    public void OctcubeQuantFromCmap_PicksNearestColor()
    {
        PixColormap cmap = PixColormap.Create(2);
        cmap.AddColor(0, 0, 0);
        cmap.AddColor(255, 255, 255);
        Pix pix = MakeRgb(2, 1, (x, y) => x == 0 ? (10, 12, 8) : (250, 251, 249));
        Pix pixd = pix.OctcubeQuantFromCmap(cmap, 2, 4, L.L_EUCLIDEAN_DISTANCE);
        Assert.Equal(2, pixd.Depth);
        Assert.Equal(0u, Get(pixd, 0, 0));
        Assert.Equal(1u, Get(pixd, 1, 0));
        Assert.Equal(2, pixd.Colormap!.Count);
    }

    [Fact]
    public void OctcubeQuantFromCmap_MinDepthRaisesDepth()
    {
        PixColormap cmap = PixColormap.Create(2);
        cmap.AddColor(0, 0, 0);
        cmap.AddColor(255, 255, 255);
        Pix pix = MakeRgb(2, 1, (x, y) => (0, 0, 0));
        Assert.Equal(4, pix.OctcubeQuantFromCmap(cmap, 4, 4, L.L_EUCLIDEAN_DISTANCE).Depth);
        Assert.Equal(8, pix.QuantFromCmap(cmap, 8, 4, L.L_EUCLIDEAN_DISTANCE).Depth);
    }

    [Fact]
    public void QuantFromCmap_InvalidArgs()
    {
        PixColormap cmap = PixColormap.Create(2);
        cmap.AddColor(0, 0, 0);
        Assert.Throws<ArgumentException>(() =>
            Pix.Create(2, 2, 32).QuantFromCmap(cmap, 3, 4, L.L_EUCLIDEAN_DISTANCE));
        Assert.Throws<ArgumentException>(() =>
            Pix.Create(2, 2, 16).QuantFromCmap(cmap, 2, 4, L.L_EUCLIDEAN_DISTANCE));
        Assert.Throws<ArgumentException>(() =>
            Pix.Create(2, 2, 32).OctcubeQuantFromCmap(cmap, 2, 7, L.L_EUCLIDEAN_DISTANCE));
        Assert.Throws<ArgumentException>(() =>
            Pix.Create(2, 2, 32).OctcubeQuantFromCmap(cmap, 2, 4, 55));
    }

    /*----------------------------- unused colors -----------------------*/

    [Fact]
    public void RemoveUnusedColors_RenumbersPixels()
    {
        Pix pix = Pix.Create(4, 1, 8);
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(10, 10, 10);
        cmap.AddColor(20, 20, 20);
        cmap.AddColor(30, 30, 30);
        cmap.AddColor(40, 40, 40);
        pix.SetColormap(cmap);
        pix.SetPixel(0, 0, 1);
        pix.SetPixel(1, 0, 3);
        pix.SetPixel(2, 0, 3);
        pix.SetPixel(3, 0, 1);
        pix.RemoveUnusedColors();
        Assert.Equal(2, pix.Colormap!.Count);
        Assert.Equal((20, 20, 20), CmapColorAt(pix, 0, 0));
        Assert.Equal((40, 40, 40), CmapColorAt(pix, 1, 0));
        Assert.Equal(0u, Get(pix, 0, 0));
        Assert.Equal(1u, Get(pix, 1, 0));
    }

    [Fact]
    public void RemoveUnusedColors_NoOpCases()
    {
        Pix gray = Pix.Create(2, 2, 8);
        gray.RemoveUnusedColors();   // no colormap: nothing to do
        Pix pix = Pix.Create(2, 1, 8);
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(1, 1, 1);
        cmap.AddColor(2, 2, 2);
        pix.SetColormap(cmap);
        pix.SetPixel(1, 0, 1);
        pix.RemoveUnusedColors();    // all used
        Assert.Equal(2, pix.Colormap!.Count);
    }

    /*----------------------------- adaptive octree ---------------------*/

    [Fact]
    public void OctreeQuantByPopulation_FewColorsAreExact()
    {
        Pix pix = MakeRgb(8, 8, (x, y) => (x / 4 + y / 4) switch { 0 => (10, 20, 30), 1 => (200, 100, 50), _ => (250, 250, 250) });
        Pix pixd = pix.OctreeQuantByPopulation(4, 0);
        Assert.Equal(2, pixd.Depth);
        Assert.Equal(3, pixd.Colormap!.Count);
        Assert.Equal((10, 20, 30), CmapColorAt(pixd, 0, 0));
        Assert.Equal((200, 100, 50), CmapColorAt(pixd, 5, 0));
        Assert.Equal((250, 250, 250), CmapColorAt(pixd, 5, 5));
    }

    [Fact]
    public void OctreeQuantByPopulation_ManyColors_Uses256Entries()
    {
        Pix pix = MakeRgb(64, 64, (x, y) => (x * 4, y * 4, (x + y) * 2));
        Pix pixd = pix.OctreeQuantByPopulation(0, 0);
        Assert.Equal(8, pixd.Depth);
        Assert.Equal(256, pixd.Colormap!.Count);
        // every pixel is mapped to a color near the original (within an octcube at level 2)
        for (int y = 0; y < 64; y += 9)
            for (int x = 0; x < 64; x += 7) {
                var (r, g, b) = CmapColorAt(pixd, x, y);
                Assert.True(Math.Abs(r - x * 4) <= 64);
                Assert.True(Math.Abs(g - y * 4) <= 64);
                Assert.True(Math.Abs(b - (x + y) * 2) <= 64);
            }
    }

    [Fact]
    public void OctreeQuantByPopulation_InvalidArgs()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).OctreeQuantByPopulation(4, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 32).OctreeQuantByPopulation(5, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 32).OctreeQuantByPopulation(2, 0));
    }

    [Fact]
    public void OctreeQuantNumColors_MinimumColorsUseBaseOctcubes()
    {
        Pix pix = MakeRgb(4, 1, (x, y) => x < 2 ? (0, 0, 0) : (255, 255, 255));
        Pix pixd = pix.OctreeQuantNumColors(8, 0);
        Assert.Equal(4, pixd.Depth);
        Assert.Equal(8, pixd.Colormap!.Count);
        Assert.Equal(0u, Get(pixd, 0, 0));
        Assert.Equal(7u, Get(pixd, 3, 0));
        Assert.Equal((0, 0, 0), CmapColorAt(pixd, 0, 0));
        Assert.Equal((255, 255, 255), CmapColorAt(pixd, 3, 0));
        // empty cubes get their centers
        pixd.Colormap.GetColor(3, out int r, out int g, out int b);
        Assert.Equal((64, 192, 192), (r, g, b));
    }

    [Fact]
    public void OctreeQuantNumColors_GeneralCaseDropsEmptyEntries()
    {
        Pix pix = MakeRgb(20, 20, (x, y) => x < 10 ? (0, 0, 0) : (255, 255, 255));
        Pix pixd = pix.OctreeQuantNumColors(16, 1);
        Assert.Equal(4, pixd.Depth);
        Assert.Equal(2, pixd.Colormap!.Count);
        Assert.Equal((0, 0, 0), CmapColorAt(pixd, 2, 2));
        Assert.Equal((255, 255, 255), CmapColorAt(pixd, 15, 19));

        Pix pix8 = pix.OctreeQuantNumColors(100, 0);
        Assert.Equal(8, pix8.Depth);
        Assert.Equal(2, pix8.Colormap!.Count);
        Assert.Equal((255, 255, 255), CmapColorAt(pix8, 19, 0));
    }

    [Fact]
    public void OctreeQuantNumColors_ClampsMaxColors()
    {
        Pix pix = MakeRgb(4, 4, (x, y) => (x * 60, y * 60, 100));
        Assert.Equal(4, pix.OctreeQuantNumColors(2, 0).Depth);      // set to 8
        Assert.Equal(8, pix.OctreeQuantNumColors(1000, 0).Depth);   // set to 256
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).OctreeQuantNumColors(16, 0));
    }

    [Fact]
    public void OctcubeQuantMixedWithGray_SeparatesColorAndGray()
    {
        Pix pix = MakeRgb(2, 1, (x, y) => x == 0 ? (200, 10, 10) : (128, 128, 128));
        Pix pixd = pix.OctcubeQuantMixedWithGray(8, 4, 10);
        Assert.Equal(8, pixd.Depth);
        Assert.Equal(64 + 4, pixd.Colormap!.Count);
        Assert.True(Get(pixd, 0, 0) < 64);
        Assert.Equal((200, 10, 10), CmapColorAt(pixd, 0, 0));
        Assert.InRange(Get(pixd, 1, 0), 64u, 67u);
        var (r, g, b) = CmapColorAt(pixd, 1, 0);
        Assert.True(r == g && g == b);
    }

    [Fact]
    public void OctcubeQuantMixedWithGray_InvalidArgs()
    {
        Pix pix = Pix.Create(2, 2, 32);
        Assert.Throws<ArgumentException>(() => pix.OctcubeQuantMixedWithGray(8, 1, 10));
        Assert.Throws<ArgumentException>(() => pix.OctcubeQuantMixedWithGray(4, 9, 10));
        Assert.Throws<ArgumentException>(() => pix.OctcubeQuantMixedWithGray(8, 193, 10));
        Assert.Throws<ArgumentException>(() => pix.OctcubeQuantMixedWithGray(2, 4, 10));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void OctreeColorQuantGeneral_ThreeColors(int dither)
    {
        Pix pix = MakeRgb(64, 64, (x, y) => (x / 22) switch { 0 => (200, 30, 30), 1 => (30, 200, 30), _ => (30, 30, 200) });
        Pix pixd = pix.OctreeColorQuantGeneral(200, dither, 0f, 0f);
        Assert.Equal(8, pixd.Depth);
        Assert.NotNull(pixd.Colormap);
        Assert.True(pixd.Colormap!.Count <= 256);
        for (int y = 0; y < 64; y += 5)
            for (int x = 0; x < 64; x += 3) {
                var (r, g, b) = CmapColorAt(pixd, x, y);
                var (er, eg, eb) = (x / 22) switch { 0 => (200, 30, 30), 1 => (30, 200, 30), _ => (30, 30, 200) };
                Assert.True(Math.Abs(r - er) <= 32 && Math.Abs(g - eg) <= 32 && Math.Abs(b - eb) <= 32,
                            $"({x},{y}) got {r},{g},{b}");
            }
    }

    [Fact]
    public void OctreeColorQuantGeneral_WideImageIsSubsampledForTheTree()
    {
        Pix pix = MakeRgb(400, 8, (x, y) => (x / 100) switch { 0 => (220, 20, 20), 1 => (20, 220, 20), 2 => (20, 20, 220), _ => (220, 220, 20) });
        Pix pixd = pix.OctreeColorQuantGeneral(128, 1, 0f, 0f);
        Assert.Equal(400, pixd.Width);
        var (r, g, b) = CmapColorAt(pixd, 150, 4);
        Assert.True(g > 150 && r < 100 && b < 100);
    }

    [Fact]
    public void OctreeColorQuant_InvalidArgs()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(8, 8, 8).OctreeColorQuant(200, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(8, 8, 32).OctreeColorQuant(100, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(8, 8, 32).OctreeColorQuant(241, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(8, 8, 32).OctreeColorQuantGeneral(100, 0, 0f, 0f));
    }
}
