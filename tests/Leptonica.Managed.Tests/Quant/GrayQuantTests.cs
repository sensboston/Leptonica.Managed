using System;
using Leptonica;
using Naive = Leptonica.Managed.Tests.Rop.NaiveRop;

namespace Leptonica.Managed.Tests.Quant;

/// <summary>
/// Tests of grayquant.c.  Expected values are worked out by hand on small images.
/// Tests of functions that call pixRemoveColormap (pixconv.c) return early while that
/// function is not ported.
/// </summary>
public class GrayQuantTests
{
    private static Pix MakePix(int w, int h, int d, params uint[] vals)
    {
        Assert.Equal(w * h, vals.Length);
        Pix pix = Pix.Create(w, h, d);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                Naive.SetPixel(pix, j, i, vals[i * w + j]);
        return pix;
    }

    private static void AssertPixels(Pix pix, int w, int h, int d, params uint[] expected)
    {
        Assert.Equal(w, pix.Width);
        Assert.Equal(h, pix.Height);
        Assert.Equal(d, pix.Depth);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                Assert.True(expected[i * w + j] == Naive.GetPixel(pix, j, i),
                            $"pixel ({j},{i}): expected {expected[i * w + j]}, got {Naive.GetPixel(pix, j, i)}");
    }

    private static uint Rgb(int r, int g, int b) => ((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8);

    /// <summary>pixRemoveColormap is in pixconv.c; true if it can be used.</summary>
    private static bool RemoveColormapAvailable()
    {
        try {
            Pix.Create(1, 1, 8).RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
            return true;
        } catch (NotImplementedException) {
            return false;
        }
    }

    private static void SetBytes(Span<uint> buf, params int[] vals)
    {
        for (int i = 0; i < vals.Length; i++)
            ArrayAccess.SetDataByte(buf, i, vals[i]);
    }

    /*-------------------------------------------------------------*
     *                         Tables                              *
     *-------------------------------------------------------------*/
    [Fact]
    public void MakeGrayQuantIndexTable_FourLevels()
    {
        // thresholds 255 * (2j + 1) / 6 = 42, 127, 212
        int[] tab = Pix.MakeGrayQuantIndexTable(4);
        Assert.Equal(256, tab.Length);
        Assert.Equal(0, tab[0]);
        Assert.Equal(0, tab[42]);
        Assert.Equal(1, tab[43]);
        Assert.Equal(1, tab[127]);
        Assert.Equal(2, tab[128]);
        Assert.Equal(2, tab[212]);
        Assert.Equal(3, tab[213]);
        Assert.Equal(3, tab[255]);
    }

    [Fact]
    public void MakeGrayQuantIndexTable_TwoLevels()
    {
        int[] tab = Pix.MakeGrayQuantIndexTable(2);
        Assert.Equal(0, tab[127]);
        Assert.Equal(1, tab[128]);
        Assert.Equal(1, tab[255]);
    }

    [Fact]
    public void MakeGrayQuantTargetTable_ThreeLevels8bpp()
    {
        // The two thresholds are 0x3f and 0xbf; the targets are 0, 0x7f, 0xff
        int[] tab = Pix.MakeGrayQuantTargetTable(3, 8);
        Assert.Equal(0, tab[0]);
        Assert.Equal(0, tab[0x3f]);
        Assert.Equal(0x7f, tab[0x40]);
        Assert.Equal(0x7f, tab[0xbf]);
        Assert.Equal(0xff, tab[0xc0]);
        Assert.Equal(0xff, tab[255]);
    }

    [Fact]
    public void MakeGrayQuantTargetTable_IgnoresNlevelsBelow8bpp()
    {
        // 4 bpp: 16 levels, thresholds 255 * (2j + 1) / 30 = 8, 25, ...
        int[] tab = Pix.MakeGrayQuantTargetTable(2, 4);
        Assert.Equal(0, tab[8]);
        Assert.Equal(1, tab[9]);
        Assert.Equal(1, tab[25]);
        Assert.Equal(2, tab[26]);
        Assert.Equal(15, tab[255]);
    }

    [Fact]
    public void Make8To2DitherTables_Values()
    {
        Pix.Make8To2DitherTables(out int[] tabval, out int[] tab38, out int[] tab14, 5, 5);
        // clipped to black
        Assert.Equal((0, 0, 0), (tabval[5], tab38[5], tab14[5]));
        Assert.Equal((0, 2, 2), (tabval[6], tab38[6], tab14[6]));
        Assert.Equal((0, 16, 11), (tabval[42], tab38[42], tab14[42]));
        Assert.Equal((1, -16, -11), (tabval[43], tab38[43], tab14[43]));
        Assert.Equal((1, 0, 0), (tabval[84], tab38[84], tab14[84]));
        Assert.Equal((1, 0, 0), (tabval[85], tab38[85], tab14[85]));
        Assert.Equal((1, 16, 11), (tabval[127], tab38[127], tab14[127]));
        Assert.Equal((2, -16, -11), (tabval[128], tab38[128], tab14[128]));
        Assert.Equal((2, 16, 11), (tabval[212], tab38[212], tab14[212]));
        Assert.Equal((3, -16, -11), (tabval[213], tab38[213], tab14[213]));
        Assert.Equal((3, -2, -2), (tabval[249], tab38[249], tab14[249]));
        // clipped to white
        Assert.Equal((3, 0, 0), (tabval[250], tab38[250], tab14[250]));
        Assert.Equal((3, 0, 0), (tabval[255], tab38[255], tab14[255]));
    }

    [Fact]
    public void MakeGrayQuantTableArb_ThreeEdges()
    {
        Numa na = Numa.Create(3);
        na.AddNumber(64);
        na.AddNumber(128);
        na.AddNumber(192);
        Pix.MakeGrayQuantTableArb(na, 2, out int[] tab, out PixColormap cmap);

        Assert.Equal(0, tab[0]);
        Assert.Equal(0, tab[63]);
        Assert.Equal(1, tab[64]);
        Assert.Equal(1, tab[127]);
        Assert.Equal(2, tab[128]);
        Assert.Equal(2, tab[191]);
        Assert.Equal(3, tab[192]);
        Assert.Equal(3, tab[255]);

        // Colors at the center of each bin: 32, 96, 160, (192 + 255) / 2 = 223
        Assert.Equal(4, cmap.Count);
        int[] expected = { 32, 96, 160, 223 };
        for (int i = 0; i < 4; i++) {
            cmap.GetColor(i, out int r, out int g, out int b);
            Assert.Equal((expected[i], expected[i], expected[i]), (r, g, b));
        }
    }

    [Fact]
    public void MakeGrayQuantTableArb_TooManyBins_Throws()
    {
        Numa na = Numa.Create(3);
        na.AddNumber(64);
        na.AddNumber(128);
        na.AddNumber(192);
        Assert.Throws<ArgumentException>(() =>
            Pix.MakeGrayQuantTableArb(na, 1, out _, out _));
    }

    /*-------------------------------------------------------------*
     *                    Thresholding to binary                   *
     *-------------------------------------------------------------*/
    [Fact]
    public void ThresholdToBinaryLineLow_8bpp_Tail()
    {
        // 3 pixels in one word: 10, 100, 200; thresh 100 -> 1, 0, 0
        uint[] lines = { 0x0a64c800u };
        uint[] lined = new uint[1];
        Pix.ThresholdToBinaryLineLow(lined, 3, lines, 8, 100);
        Assert.Equal(0x80000000u, lined[0]);
    }

    [Theory]
    [InlineData(8, 70, 100)]
    [InlineData(8, 64, 0)]
    [InlineData(8, 64, 256)]
    [InlineData(8, 33, 255)]
    [InlineData(4, 33, 8)]
    [InlineData(4, 64, 0)]
    [InlineData(4, 64, 16)]
    [InlineData(4, 5, 3)]
    public void ThresholdToBinaryLineLow_MatchesPixelwise(int d, int w, int thresh)
    {
        Pix pixs = Pix.Create(w, 1, d);
        uint mask = (1u << d) - 1;
        for (int j = 0; j < w; j++)
            Naive.SetPixel(pixs, j, 0, (uint)(j * 37 + 5) & mask);
        Pix pixd = Pix.Create(w, 1, 1);

        Pix.ThresholdToBinaryLineLow(pixd.Data, w, pixs.Data, d, thresh);

        for (int j = 0; j < w; j++) {
            uint expected = Naive.GetPixel(pixs, j, 0) < thresh ? 1u : 0u;
            Assert.True(expected == Naive.GetPixel(pixd, j, 0), $"d={d} w={w} thresh={thresh} j={j}");
        }
    }

    [Fact]
    public void ThresholdToBinary_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 2).ThresholdToBinary(1));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).ThresholdToBinary(-1));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).ThresholdToBinary(257));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 4).ThresholdToBinary(17));
    }

    [Fact]
    public void ThresholdToBinary_8bpp()
    {
        if (!RemoveColormapAvailable())
            return;
        Pix pixs = MakePix(4, 2, 8, 0, 99, 100, 255, 100, 50, 150, 200);
        Pix pixd = pixs.ThresholdToBinary(100);
        AssertPixels(pixd, 4, 2, 1, 1, 1, 0, 0, 0, 1, 0, 0);
        AssertPixels(pixs.ThresholdToBinary(0), 4, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0);
        AssertPixels(pixs.ThresholdToBinary(256), 4, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1);
    }

    [Fact]
    public void ThresholdToBinary_4bpp()
    {
        if (!RemoveColormapAvailable())
            return;
        Pix pixs = MakePix(4, 1, 4, 0, 7, 8, 15);
        AssertPixels(pixs.ThresholdToBinary(8), 4, 1, 1, 1, 1, 0, 0);
    }

    [Fact]
    public void ThresholdToBinary_4bppColormapped_Promoted()
    {
        if (!RemoveColormapAvailable())
            return;
        // Colormap removal gives 8 bpp, so the 4 bpp threshold is scaled by 16.
        Pix pixs = MakePix(2, 1, 4, 0, 1);
        PixColormap cmap = PixColormap.Create(4);
        cmap.AddColor(10, 10, 10);
        cmap.AddColor(200, 200, 200);
        pixs.SetColormap(cmap);
        AssertPixels(pixs.ThresholdToBinary(8), 2, 1, 1, 1, 0);
    }

    [Fact]
    public void VarThresholdToBinary_Pixelwise()
    {
        Pix pixs = MakePix(4, 2, 8, 10, 20, 30, 40, 50, 60, 70, 80);
        Pix pixg = MakePix(4, 2, 8, 11, 20, 31, 39, 50, 61, 69, 90);
        Pix pixd = pixs.VarThresholdToBinary(pixg);
        AssertPixels(pixd, 4, 2, 1, 1, 0, 1, 0, 0, 1, 0, 1);
    }

    [Fact]
    public void VarThresholdToBinary_InvalidArguments()
    {
        Pix pixs = Pix.Create(4, 2, 8);
        Assert.Throws<ArgumentException>(() => pixs.VarThresholdToBinary(Pix.Create(5, 2, 8)));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 2, 4).VarThresholdToBinary(Pix.Create(4, 2, 4)));
    }

    /*-------------------------------------------------------------*
     *                  Floyd-Steinberg dithering                  *
     *-------------------------------------------------------------*/
    [Fact]
    public void DitherToBinaryLineLow_LastLine()
    {
        // w = 3, values 100, 200, 50, clips 10/10:
        //  j=0: 100 -> ON, adds 3*100/8 = 37 to the right neighbor: 237
        //  j=1: 237 -> OFF, eval = 18 > 10, subtracts 3*18/8 = 6 from the right: 44
        //  j=2: 44 -> ON
        uint[] bufs1 = new uint[1];
        SetBytes(bufs1, 100, 200, 50);
        uint[] bufs2 = new uint[1];
        uint[] lined = new uint[1];
        Pix.DitherToBinaryLineLow(lined, 3, bufs1, bufs2, 10, 10, 1);
        Assert.Equal(0xa0000000u, lined[0]);
        Assert.Equal(237, ArrayAccess.GetDataByte(bufs1, 1));
        Assert.Equal(44, ArrayAccess.GetDataByte(bufs1, 2));
    }

    [Fact]
    public void DitherToBinaryLineLow_NotLastLine()
    {
        // w = 2, values 100, 200 over a zero next line:
        //  j=0: 100 -> ON; right = 237, below = 37, diagonal = 100/4 = 25
        //  j=1 (last column): 237 -> OFF, eval = 18, below: 25 - 6 = 19
        uint[] bufs1 = new uint[1];
        SetBytes(bufs1, 100, 200);
        uint[] bufs2 = new uint[1];
        uint[] lined = new uint[1];
        Pix.DitherToBinaryLineLow(lined, 2, bufs1, bufs2, 10, 10, 0);
        Assert.Equal(0x80000000u, lined[0]);
        Assert.Equal(237, ArrayAccess.GetDataByte(bufs1, 1));
        Assert.Equal(37, ArrayAccess.GetDataByte(bufs2, 0));
        Assert.Equal(19, ArrayAccess.GetDataByte(bufs2, 1));
    }

    [Fact]
    public void DitherToBinaryLineLow_ClipsNearBlackAndWhite()
    {
        // 5 <= lowerclip and 250 > 255 - upperclip: no propagation
        uint[] bufs1 = new uint[1];
        SetBytes(bufs1, 5, 250, 128);
        uint[] bufs2 = new uint[1];
        uint[] lined = new uint[1];
        Pix.DitherToBinaryLineLow(lined, 3, bufs1, bufs2, 10, 10, 1);
        Assert.Equal(5, ArrayAccess.GetDataByte(bufs1, 0));
        Assert.Equal(250, ArrayAccess.GetDataByte(bufs1, 1));
        Assert.Equal(128, ArrayAccess.GetDataByte(bufs1, 2));
        Assert.Equal(0x80000000u, lined[0]);
    }

    [Fact]
    public void DitherToBinary_OneRow()
    {
        if (!RemoveColormapAvailable())
            return;
        Pix pixs = MakePix(3, 1, 8, 100, 200, 50);
        AssertPixels(pixs.DitherToBinary(), 3, 1, 1, 1, 0, 1);
    }

    [Fact]
    public void DitherToBinarySpec_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 4).DitherToBinary());
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).DitherToBinarySpec(-1, 10));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).DitherToBinarySpec(10, 256));
    }

    [Fact]
    public void DitherTo2bpp_SingleRow()
    {
        if (!RemoveColormapAvailable())
            return;
        // 128 -> 2, right neighbor gets -16 = 112 -> 1
        Pix pixs = MakePix(2, 1, 8, 128, 128);
        Pix pixd = pixs.DitherTo2bpp(false);
        AssertPixels(pixd, 2, 1, 2, 2, 1);
        Assert.Null(pixd.Colormap);

        Pix pixc = pixs.DitherTo2bpp(true);
        Assert.NotNull(pixc.Colormap);
        Assert.Equal(4, pixc.Colormap!.Count);
        pixc.Colormap.GetColor(1, out int r, out _, out _);
        Assert.Equal(85, r);
    }

    [Fact]
    public void DitherTo2bpp_TwoRows()
    {
        if (!RemoveColormapAvailable())
            return;
        // Row 0: 128 -> 2 (right 112, below 112, diagonal 117); 112 -> 1, below 117 + 10 = 127.
        // Row 1 starts as (112, 127): 112 -> 1, right 127 + 10 = 137; 137 -> 2.
        Pix pixs = MakePix(2, 2, 8, 128, 128, 128, 128);
        AssertPixels(pixs.DitherTo2bpp(false), 2, 2, 2, 2, 1, 1, 2);
    }

    [Fact]
    public void DitherTo2bppSpec_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 4).DitherTo2bpp(false));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).DitherTo2bppSpec(256, 5, false));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).DitherTo2bppSpec(5, -1, false));
    }

    /*-------------------------------------------------------------*
     *                  Thresholding to 2, 4, 8 bpp                *
     *-------------------------------------------------------------*/
    [Fact]
    public void ThresholdTo2bpp_NoColormap_FourLevels()
    {
        if (!RemoveColormapAvailable())
            return;
        Pix pixs = MakePix(8, 1, 8, 0, 42, 43, 127, 128, 212, 213, 255);
        Pix pixd = pixs.ThresholdTo2bpp(2, false);   // nlevels ignored
        AssertPixels(pixd, 8, 1, 2, 0, 0, 1, 1, 2, 2, 3, 3);
        Assert.Null(pixd.Colormap);
    }

    [Fact]
    public void ThresholdTo2bpp_Colormap_ThreeLevels()
    {
        if (!RemoveColormapAvailable())
            return;
        Pix pixs = MakePix(6, 1, 8, 0, 63, 64, 191, 192, 255);
        Pix pixd = pixs.ThresholdTo2bpp(3, true);
        AssertPixels(pixd, 6, 1, 2, 0, 0, 1, 1, 2, 2);
        Assert.Equal(3, pixd.Colormap!.Count);
        pixd.Colormap.GetColor(1, out int r, out _, out _);
        Assert.Equal(127, r);
    }

    [Fact]
    public void ThresholdTo2bpp_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 4).ThresholdTo2bpp(4, false));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).ThresholdTo2bpp(1, false));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).ThresholdTo2bpp(5, false));
    }

    [Fact]
    public void ThresholdTo4bpp_NoColormap_SixteenLevels()
    {
        if (!RemoveColormapAvailable())
            return;
        Pix pixs = MakePix(6, 1, 8, 0, 8, 9, 25, 26, 255);
        AssertPixels(pixs.ThresholdTo4bpp(2, false), 6, 1, 4, 0, 0, 1, 1, 2, 15);
    }

    [Fact]
    public void ThresholdTo4bpp_Colormap_TwoLevels()
    {
        if (!RemoveColormapAvailable())
            return;
        Pix pixs = MakePix(4, 1, 8, 0, 127, 128, 255);
        Pix pixd = pixs.ThresholdTo4bpp(2, true);
        AssertPixels(pixd, 4, 1, 4, 0, 0, 1, 1);
        Assert.Equal(2, pixd.Colormap!.Count);
    }

    [Fact]
    public void ThresholdTo4bpp_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 2).ThresholdTo4bpp(4, false));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).ThresholdTo4bpp(1, false));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).ThresholdTo4bpp(17, false));
    }

    [Fact]
    public void ThresholdOn8bpp_QuantizedValues()
    {
        Pix pixs = MakePix(6, 1, 8, 0, 63, 64, 191, 192, 255);
        Pix pixd = pixs.ThresholdOn8bpp(3, false);
        AssertPixels(pixd, 6, 1, 8, 0, 0, 127, 127, 255, 255);
        Assert.Null(pixd.Colormap);
        // pixs is unchanged
        AssertPixels(pixs, 6, 1, 8, 0, 63, 64, 191, 192, 255);
    }

    [Fact]
    public void ThresholdOn8bpp_ColormapIndices()
    {
        Pix pixs = MakePix(6, 1, 8, 0, 63, 64, 191, 192, 255);
        Pix pixd = pixs.ThresholdOn8bpp(3, true);
        AssertPixels(pixd, 6, 1, 8, 0, 0, 1, 1, 2, 2);
        Assert.Equal(3, pixd.Colormap!.Count);
    }

    [Fact]
    public void ThresholdOn8bpp_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 4).ThresholdOn8bpp(4, false));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).ThresholdOn8bpp(1, false));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).ThresholdOn8bpp(257, false));
    }

    [Fact]
    public void ThresholdGrayArb_BinCenters()
    {
        if (!RemoveColormapAvailable())
            return;
        Pix pixs = MakePix(8, 1, 8, 0, 63, 64, 127, 128, 191, 192, 255);
        Pix pixd = pixs.ThresholdGrayArb("64 128 192", 0, false, false, false);
        AssertPixels(pixd, 8, 1, 2, 0, 0, 1, 1, 2, 2, 3, 3);
        Assert.Equal(4, pixd.Colormap!.Count);
        pixd.Colormap.GetColor(3, out int r, out _, out _);
        Assert.Equal(223, r);
    }

    [Fact]
    public void ThresholdGrayArb_UnsortedEdgesAnd4bpp()
    {
        if (!RemoveColormapAvailable())
            return;
        // Edges are sorted; 5 bins at outdepth 4
        Pix pixs = MakePix(5, 1, 8, 5, 15, 25, 35, 45);
        Pix pixd = pixs.ThresholdGrayArb("40,30 10 20", 4, false, false, false);
        AssertPixels(pixd, 5, 1, 4, 0, 1, 2, 3, 4);
    }

    [Fact]
    public void ThresholdGrayArb_AverageBlackWhite()
    {
        if (!RemoveColormapAvailable())
            return;
        // Bins: {10, 20} -> 15, {100} -> 100, {} -> center (128 + 192) / 2 = 160, {200, 250} -> 225.
        // Darkest set to black and lightest to white.
        Pix pixs = MakePix(5, 1, 8, 10, 20, 100, 200, 250);
        Pix pixd = pixs.ThresholdGrayArb("64 128 192", 2, true, true, true);
        AssertPixels(pixd, 5, 1, 2, 0, 0, 1, 3, 3);
        int[] expected = { 0, 100, 160, 255 };
        for (int i = 0; i < 4; i++) {
            pixd.Colormap!.GetColor(i, out int r, out int g, out int b);
            Assert.Equal((expected[i], expected[i], expected[i]), (r, g, b));
        }
    }

    [Fact]
    public void ThresholdGrayArb_InvalidArguments()
    {
        Pix pixs = Pix.Create(4, 4, 8);
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 4).ThresholdGrayArb("64", 0, false, false, false));
        Assert.Throws<ArgumentException>(() => pixs.ThresholdGrayArb("64", 3, false, false, false));
    }

    /*-------------------------------------------------------------*
     *                     Masks from values                       *
     *-------------------------------------------------------------*/
    [Fact]
    public void GenerateMaskByValue_8bpp()
    {
        Pix pixs = MakePix(4, 2, 8, 7, 8, 7, 0, 255, 7, 9, 7);
        AssertPixels(pixs.GenerateMaskByValue(7, true), 4, 2, 1, 1, 0, 1, 0, 0, 1, 0, 1);
        AssertPixels(pixs.GenerateMaskByValue(255, false), 4, 2, 1, 0, 0, 0, 0, 1, 0, 0, 0);
    }

    [Fact]
    public void GenerateMaskByValue_4bppAnd2bpp()
    {
        Pix pix4 = MakePix(4, 1, 4, 3, 15, 3, 0);
        AssertPixels(pix4.GenerateMaskByValue(3, true), 4, 1, 1, 1, 0, 1, 0);
        Pix pix2 = MakePix(4, 1, 2, 3, 2, 3, 0);
        AssertPixels(pix2.GenerateMaskByValue(3, true), 4, 1, 1, 1, 0, 1, 0);
    }

    [Fact]
    public void GenerateMaskByValue_ColormapIndices()
    {
        Pix pixs = MakePix(3, 1, 4, 1, 2, 1);
        PixColormap cmap = PixColormap.Create(4);
        cmap.AddColor(0, 0, 0);
        cmap.AddColor(10, 10, 10);
        cmap.AddColor(20, 20, 20);
        pixs.SetColormap(cmap);
        AssertPixels(pixs.GenerateMaskByValue(1, true), 3, 1, 1, 1, 0, 1);
    }

    [Fact]
    public void GenerateMaskByValue_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 1, 1).GenerateMaskByValue(0, true));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 1, 8).GenerateMaskByValue(256, true));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 1, 8).GenerateMaskByValue(-1, true));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 1, 4).GenerateMaskByValue(16, true));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 1, 2).GenerateMaskByValue(4, true));
    }

    [Fact]
    public void GenerateMaskByBand_InAndOutOfBand()
    {
        Pix pixs = MakePix(6, 1, 8, 0, 49, 50, 100, 101, 255);
        AssertPixels(pixs.GenerateMaskByBand(50, 100, true, true), 6, 1, 1, 0, 0, 1, 1, 0, 0);
        AssertPixels(pixs.GenerateMaskByBand(50, 100, false, true), 6, 1, 1, 1, 1, 0, 0, 1, 1);
    }

    [Fact]
    public void GenerateMaskByBand_4bpp()
    {
        Pix pixs = MakePix(4, 1, 4, 0, 5, 10, 15);
        AssertPixels(pixs.GenerateMaskByBand(5, 10, true, true), 4, 1, 1, 0, 1, 1, 0);
    }

    [Fact]
    public void GenerateMaskByBand_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 1, 8).GenerateMaskByBand(-1, 10, true, true));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 1, 8).GenerateMaskByBand(11, 10, true, true));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 1, 8).GenerateMaskByBand(0, 256, true, true));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 1, 4).GenerateMaskByBand(0, 16, true, true));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 1, 2).GenerateMaskByBand(0, 4, true, true));
    }

    /*-------------------------------------------------------------*
     *                   Thresholding from 32 bpp                  *
     *-------------------------------------------------------------*/
    [Fact]
    public void GenerateMaskByBand32_AbsoluteBand()
    {
        Pix pixs = MakePix(4, 1, 32, Rgb(95, 100, 110), Rgb(89, 100, 100),
                           Rgb(100, 111, 100), Rgb(110, 90, 100));
        Pix pixd = pixs.GenerateMaskByBand32(Rgb(100, 100, 100), 10, 10, 0.0f, 0.0f);
        AssertPixels(pixd, 4, 1, 1, 1, 0, 0, 1);
    }

    [Fact]
    public void GenerateMaskByBand32_FractionalBand()
    {
        // ref = (100, 200, 50), fractions 0.5:
        //  r in [50, 100 + 77 = 177], g in [100, 200 + 27 = 227], b in [25, 50 + 102 = 152]
        Pix pixs = MakePix(6, 1, 32, Rgb(50, 100, 25), Rgb(49, 100, 25), Rgb(177, 227, 152),
                           Rgb(178, 227, 152), Rgb(177, 228, 152), Rgb(177, 227, 153));
        Pix pixd = pixs.GenerateMaskByBand32(Rgb(100, 200, 50), 0, 0, 0.5f, 0.5f);
        AssertPixels(pixd, 6, 1, 1, 1, 0, 1, 0, 0, 0);
    }

    [Fact]
    public void GenerateMaskByBand32_InvalidArguments()
    {
        Pix pixs = Pix.Create(2, 2, 32);
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 2, 8).GenerateMaskByBand32(0, 1, 1, 0f, 0f));
        Assert.Throws<ArgumentException>(() => pixs.GenerateMaskByBand32(0, -1, 1, 0f, 0f));
        Assert.Throws<ArgumentException>(() => pixs.GenerateMaskByBand32(0, 0, 0, 1.5f, 0f));
        Assert.Throws<ArgumentException>(() => pixs.GenerateMaskByBand32(0, 0, 0, 0f, -0.1f));
        // both a delta and a fraction
        Assert.Throws<ArgumentException>(() => pixs.GenerateMaskByBand32(0, 1, 0, 0.5f, 0f));
    }

    [Fact]
    public void GenerateMaskByDiscr32_Manhattan()
    {
        // Black vs white: (100,100,100) -> 300 < 465; (200,200,200) -> 600 > 165;
        // (0, 0, 255) -> 255 vs 510
        Pix pixs = MakePix(3, 1, 32, Rgb(100, 100, 100), Rgb(200, 200, 200), Rgb(0, 0, 255));
        Pix pixd = pixs.GenerateMaskByDiscr32(Rgb(0, 0, 0), Rgb(255, 255, 255),
                                              L.L_MANHATTAN_DISTANCE);
        AssertPixels(pixd, 3, 1, 1, 1, 0, 1);
    }

    [Fact]
    public void GenerateMaskByDiscr32_Euclidean()
    {
        // (150, 0, 0) vs refs (0, 0, 0) and (255, 0, 0): L1 gives 150 > 105 (not closer to the
        // first), L2 gives 22500 > 11025.  (120, 120, 0) vs refs (0,0,0) and (255,0,0):
        // L1 = 240 vs 135 + 120 = 255 -> first is closer; L2 = 28800 vs 18225 + 14400 = 32625.
        Pix pixs = MakePix(2, 1, 32, Rgb(150, 0, 0), Rgb(120, 120, 0));
        Pix pixd = pixs.GenerateMaskByDiscr32(Rgb(0, 0, 0), Rgb(255, 0, 0),
                                              L.L_EUCLIDEAN_DISTANCE);
        AssertPixels(pixd, 2, 1, 1, 0, 1);
        Pix pixm = pixs.GenerateMaskByDiscr32(Rgb(0, 0, 0), Rgb(255, 0, 0),
                                              L.L_MANHATTAN_DISTANCE);
        AssertPixels(pixm, 2, 1, 1, 0, 1);
    }

    [Fact]
    public void GenerateMaskByDiscr32_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 2, 8).GenerateMaskByDiscr32(0, 0, L.L_MANHATTAN_DISTANCE));
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 2, 32).GenerateMaskByDiscr32(0, 0, 0));
    }

    /*-------------------------------------------------------------*
     *                    Histogram-based quantization             *
     *-------------------------------------------------------------*/
    [Fact]
    public void GrayQuantFromHisto_NoMask()
    {
        // Two gray levels: the colormap gets the averages 10 and 200 and the
        // lut maps 0..10 -> 0, 11..200 -> 1
        Pix pixs = MakePix(4, 1, 8, 10, 10, 200, 200);
        Pix pixd = Pix.GrayQuantFromHisto(null, pixs, null, 0.05f, 10);
        AssertPixels(pixd, 4, 1, 8, 0, 0, 1, 1);
        Assert.Equal(2, pixd.Colormap!.Count);
        pixd.Colormap.GetColor(0, out int r0, out _, out _);
        pixd.Colormap.GetColor(1, out int r1, out _, out _);
        Assert.Equal((10, 200), (r0, r1));
    }

    [Fact]
    public void GrayQuantFromHisto_WithMask()
    {
        // pixd already holds one color (index 0) and the first two pixels; the mask
        // selects the two gray pixels, which get index 1.
        Pix pixs = MakePix(4, 1, 8, 7, 7, 200, 200);
        Pix pixd = MakePix(4, 1, 8, 0, 0, 0, 0);
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(255, 0, 0);
        pixd.SetColormap(cmap);
        Pix pixm = MakePix(4, 1, 1, 0, 0, 1, 1);

        Pix result = Pix.GrayQuantFromHisto(pixd, pixs, pixm, 0.05f, 10);
        AssertPixels(result, 4, 1, 8, 0, 0, 1, 1);
        Assert.Equal(2, result.Colormap!.Count);
        result.Colormap.GetColor(1, out int r, out _, out _);
        Assert.Equal(200, r);
    }

    [Fact]
    public void GrayQuantFromHisto_InvalidArguments()
    {
        Pix pixs = MakePix(2, 1, 8, 1, 2);
        Pix pixm = Pix.Create(2, 1, 1);
        Assert.Throws<ArgumentException>(() => Pix.GrayQuantFromHisto(null, Pix.Create(2, 1, 4), null, 0.05f, 10));
        Assert.Throws<ArgumentException>(() => Pix.GrayQuantFromHisto(null, pixs, pixm, 0.05f, 10));
        Assert.Throws<ArgumentException>(() => Pix.GrayQuantFromHisto(pixs, pixs, null, 0.05f, 10));
        // pixd without a colormap
        Assert.Throws<ArgumentException>(() => Pix.GrayQuantFromHisto(Pix.Create(2, 1, 8), pixs, pixm, 0.05f, 10));
    }

    [Fact]
    public void GrayQuantFromCmap_NearestGray()
    {
        // Colormap 0, 85, 170, 255.  100 is closest to 85 and 200 to 170.
        Pix pixs = MakePix(4, 1, 8, 0, 100, 200, 255);
        PixColormap cmap = PixColormap.CreateLinear(2, 4);
        Pix pixd = pixs.GrayQuantFromCmap(cmap, 2);
        AssertPixels(pixd, 4, 1, 2, 0, 1, 2, 3);
        Assert.Equal(4, pixd.Colormap!.Count);

        Pix pix4 = pixs.GrayQuantFromCmap(cmap, 4);
        AssertPixels(pix4, 4, 1, 4, 0, 1, 2, 3);

        Pix pix8 = pixs.GrayQuantFromCmap(cmap, 8);
        AssertPixels(pix8, 4, 1, 8, 0, 1, 2, 3);
    }

    [Fact]
    public void GrayQuantFromCmap_ColormappedSourceIsCopied()
    {
        Pix pixs = MakePix(2, 1, 8, 0, 1);
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(0, 0, 0);
        cmap.AddColor(255, 255, 255);
        pixs.SetColormap(cmap);
        Pix pixd = pixs.GrayQuantFromCmap(PixColormap.CreateLinear(2, 4), 2);
        Assert.NotSame(pixs, pixd);
        AssertPixels(pixd, 2, 1, 8, 0, 1);
    }

    [Fact]
    public void GrayQuantFromCmap_InvalidArguments()
    {
        PixColormap cmap = PixColormap.CreateLinear(2, 4);
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 1, 4).GrayQuantFromCmap(cmap, 2));
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 1, 8).GrayQuantFromCmap(cmap, 1));
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 1, 8).GrayQuantFromCmap(cmap, 3));
    }
}
