using System;
using System.Text;
using Leptonica;

namespace Leptonica.Managed.Tests;

public class PixaFunc2Tests
{
    private static Pix MakePix(int w, int h, int d, uint val)
    {
        Pix pix = Pix.Create(w, h, d);
        pix.SetAllArbitrary(val);
        return pix;
    }

    private static Pixa MakePixa(int count, int w, int h, int d, uint val)
    {
        Pixa pixa = Pixa.Create(0);
        for (int i = 0; i < count; i++)
            pixa.AddPix(MakePix(w, h, d, val), L.L_INSERT);
        return pixa;
    }

    private static Boxa TextBoxa(Pix pix)
    {
        Assert.NotNull(pix.Text);
        return Boxa.ReadMem(Encoding.ASCII.GetBytes(pix.Text!));
    }

    private static void AssertBox(Boxa boxa, int index, int x, int y, int w, int h)
    {
        boxa.GetBoxGeometry(index, out int bx, out int by, out int bw, out int bh);
        Assert.Equal((x, y, w, h), (bx, by, bw, bh));
    }

    private static void AssertNear(uint expected, uint actual, uint tolerance = 1)
    {
        Assert.True(Math.Abs((long)expected - actual) <= tolerance, $"expected {expected} +- {tolerance}, got {actual}");
    }

    /*------------------------------- Display -------------------------------*/

    [Fact]
    public void Display_PlacesPixByBoxes_8bpp()
    {
        Pixa pixa = Pixa.Create(0);
        Pix a = MakePix(10, 5, 8, 100);
        a.SetResolution(300, 300);
        pixa.AddPix(a, L.L_INSERT);
        pixa.AddBox(Box.Create(2, 3, 10, 5), L.L_INSERT);
        pixa.AddPix(MakePix(4, 4, 8, 50), L.L_INSERT);
        pixa.AddBox(Box.Create(20, 10, 4, 4), L.L_INSERT);

        Pix pixd = pixa.Display(0, 0);
        pixd.GetDimensions(out int w, out int h, out int d);
        Assert.Equal((24, 14, 8), (w, h, d));
        Assert.Equal((300, 300), (pixd.XRes, pixd.YRes));
        Assert.Equal(100u, pixd.GetPixel(2, 3));
        Assert.Equal(100u, pixd.GetPixel(11, 7));
        Assert.Equal(255u, pixd.GetPixel(12, 3));
        Assert.Equal(255u, pixd.GetPixel(2, 2));
        Assert.Equal(50u, pixd.GetPixel(20, 10));
        Assert.Equal(50u, pixd.GetPixel(23, 13));
        Assert.Equal(255u, pixd.GetPixel(0, 0));

        pixd = pixa.Display(30, 20);
        pixd.GetDimensions(out w, out h, out _);
        Assert.Equal((30, 20), (w, h));
    }

    [Fact]
    public void Display_1bpp_PaintsForeground()
    {
        Pixa pixa = Pixa.Create(0);
        Pix a = Pix.Create(8, 8, 1);
        a.SetPixel(0, 0, 1);
        Pix b = Pix.Create(8, 8, 1);
        b.SetPixel(1, 0, 1);
        pixa.AddPix(a, L.L_INSERT);
        pixa.AddBox(Box.Create(0, 0, 8, 8), L.L_INSERT);
        pixa.AddPix(b, L.L_INSERT);
        pixa.AddBox(Box.Create(0, 0, 8, 8), L.L_INSERT);

        Pix pixd = pixa.Display(0, 0);
        Assert.Equal(1, pixd.Depth);
        Assert.Equal(1u, pixd.GetPixel(0, 0));
        Assert.Equal(1u, pixd.GetPixel(1, 0));
        Assert.Equal(0u, pixd.GetPixel(2, 0));
    }

    [Fact]
    public void Display_EmptyAndErrors()
    {
        Assert.Throws<ArgumentException>(() => Pixa.Create(0).Display(0, 0));
        Pix pix = Pixa.Create(0).Display(7, 9);
        pix.GetDimensions(out int w, out int h, out int d);
        Assert.Equal((7, 9, 1), (w, h, d));

        // no boxes and no size
        Assert.Throws<ArgumentException>(() => MakePixa(2, 4, 4, 8, 1).Display(0, 0));
    }

    [Fact]
    public void Display_MissingBox_IsSkipped()
    {
        Pixa pixa = MakePixa(2, 4, 4, 8, 7);
        pixa.AddBox(Box.Create(0, 0, 4, 4), L.L_INSERT);  // only the first pix has a box
        Pix pixd = pixa.Display(10, 10);
        Assert.Equal(7u, pixd.GetPixel(0, 0));
        Assert.Equal(255u, pixd.GetPixel(5, 5));
    }

    /*---------------------------- DisplayRandomCmap -------------------------*/

    [Fact]
    public void DisplayRandomCmap_IndexPerRegion()
    {
        Pixa pixa = Pixa.Create(0);
        for (int i = 0; i < 2; i++) {
            Pix pix = Pix.Create(4, 4, 1);
            pix.SetAll();
            pixa.AddPix(pix, L.L_INSERT);
            pixa.AddBox(Box.Create(10 * i, 10 * i, 4, 4), L.L_INSERT);
        }

        Pix pixd = pixa.DisplayRandomCmap(0, 0);
        pixd.GetDimensions(out int w, out int h, out int d);
        Assert.Equal((14, 14, 8), (w, h, d));
        Assert.NotNull(pixd.Colormap);
        Assert.Equal(1u, pixd.GetPixel(1, 1));
        Assert.Equal(2u, pixd.GetPixel(11, 11));
        Assert.Equal(0u, pixd.GetPixel(8, 0));
    }

    [Fact]
    public void DisplayRandomCmap_Errors()
    {
        Assert.Throws<ArgumentException>(() => Pixa.Create(0).DisplayRandomCmap(0, 0));
        Assert.Throws<ArgumentException>(() => MakePixa(2, 4, 4, 8, 1).DisplayRandomCmap(10, 10));
    }

    /*------------------------------ DisplayUnsplit --------------------------*/

    [Fact]
    public void DisplayUnsplit_InvertsSplitPix()
    {
        Pix pixs = Pix.Create(12, 8, 8);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 12; x++)
                pixs.SetPixel(x, y, (uint)(x + 16 * y));

        Pixa pixa = Pixa.SplitPix(pixs, 3, 2, 0, 0);
        Pix pixd = pixa.DisplayUnsplit(3, 2, 0, 0);
        pixd.GetDimensions(out int w, out int h, out int d);
        Assert.Equal((12, 8, 8), (w, h, d));
        Assert.Equal(pixs.Data, pixd.Data);

        // With a border around each tile that is removed by the split
        Pixa pixa2 = Pixa.SplitPix(pixs, 3, 2, 2, 0x40404000);
        Pix pixd2 = pixa2.DisplayUnsplit(3, 2, 0, 0);
        pixd2.GetDimensions(out w, out h, out _);
        Assert.Equal((24, 16), (w, h));
        Assert.Equal(pixs.GetPixel(0, 0), pixd2.GetPixel(2, 2));
        Assert.Equal(pixs.GetPixel(11, 7), pixd2.GetPixel(21, 13));

        // A border is added around each tile
        Pix pixd3 = pixa.DisplayUnsplit(3, 2, 1, 0x7f7f7f00);
        pixd3.GetDimensions(out w, out h, out _);
        Assert.Equal((18, 12), (w, h));
        Assert.Equal(pixs.GetPixel(0, 0), pixd3.GetPixel(1, 1));
        Assert.Equal(pixs.GetPixel(4, 0), pixd3.GetPixel(7, 1));
    }

    [Fact]
    public void DisplayUnsplit_Errors()
    {
        Pixa pixa = MakePixa(6, 4, 4, 8, 1);
        Assert.Throws<ArgumentException>(() => pixa.DisplayUnsplit(0, 2, 0, 0));
        Assert.Throws<ArgumentException>(() => pixa.DisplayUnsplit(2, 2, 0, 0));
        Assert.Throws<ArgumentException>(() => Pixa.Create(0).DisplayUnsplit(1, 1, 0, 0));
    }

    /*----------------------------- DisplayTiledInRows -----------------------*/

    [Fact]
    public void DisplayTiledInRows_SingleRow()
    {
        Pixa pixa = MakePixa(3, 10, 10, 8, 77);
        Pix pixd = pixa.DisplayTiledInRows(8, 100, 1.0f, 0, 5, 0);
        pixd.GetDimensions(out int w, out int h, out int d);
        Assert.Equal((50, 20, 8), (w, h, d));
        Assert.Equal(77u, pixd.GetPixel(5, 5));
        Assert.Equal(77u, pixd.GetPixel(14, 14));
        Assert.Equal(255u, pixd.GetPixel(4, 5));
        Assert.Equal(255u, pixd.GetPixel(15, 5));
        Assert.Equal(77u, pixd.GetPixel(35, 5));

        Boxa boxa = TextBoxa(pixd);
        Assert.Equal(3, boxa.Count);
        AssertBox(boxa, 0, 5, 5, 10, 10);
        AssertBox(boxa, 1, 20, 5, 10, 10);
        AssertBox(boxa, 2, 35, 5, 10, 10);
    }

    [Fact]
    public void DisplayTiledInRows_WrapsRows()
    {
        Pixa pixa = MakePixa(3, 10, 10, 8, 77);
        Pix pixd = pixa.DisplayTiledInRows(8, 30, 1.0f, 0, 5, 0);
        pixd.GetDimensions(out int w, out int h, out _);
        Assert.Equal((20, 50), (w, h));

        Boxa boxa = TextBoxa(pixd);
        AssertBox(boxa, 0, 5, 5, 10, 10);
        AssertBox(boxa, 1, 5, 20, 10, 10);
        AssertBox(boxa, 2, 5, 35, 10, 10);
    }

    [Fact]
    public void DisplayTiledInRows_RowHeightIsTheTallest()
    {
        Pixa pixa = Pixa.Create(0);
        pixa.AddPix(MakePix(10, 6, 8, 10), L.L_INSERT);
        pixa.AddPix(MakePix(10, 14, 8, 20), L.L_INSERT);
        pixa.AddPix(MakePix(10, 8, 8, 30), L.L_INSERT);
        // widths: 2 + 12 -> 14, 26 fits in 30 only for the first two
        Pix pixd = pixa.DisplayTiledInRows(8, 30, 1.0f, 1, 2, 0);
        pixd.GetDimensions(out int w, out int h, out _);
        Assert.Equal(26, w);
        Assert.Equal(2 + 14 + 2 + 8 + 2, h);
        Boxa boxa = TextBoxa(pixd);
        AssertBox(boxa, 0, 2, 2, 10, 6);
        AssertBox(boxa, 1, 14, 2, 10, 14);
        AssertBox(boxa, 2, 2, 18, 10, 8);
        Assert.Equal(0u, pixd.GetPixel(0, 0));  // background black
    }

    [Fact]
    public void DisplayTiledInRows_BorderAndBinaryOutput()
    {
        Pixa pixa = MakePixa(1, 6, 6, 8, 200);  // white after thresholding
        Pix pixd = pixa.DisplayTiledInRows(1, 100, 1.0f, 0, 3, 2);
        pixd.GetDimensions(out int w, out int h, out int d);
        Assert.Equal((16, 16, 1), (w, h, d));
        Assert.Equal(0u, pixd.GetPixel(0, 0));  // 1 bpp white background
        Assert.Equal(1u, pixd.GetPixel(3, 3));  // black border
        Assert.Equal(1u, pixd.GetPixel(12, 12));
        Assert.Equal(0u, pixd.GetPixel(5, 5));
        Assert.Equal(0u, pixd.GetPixel(10, 10));
        Boxa boxa = TextBoxa(pixd);
        AssertBox(boxa, 0, 5, 5, 6, 6);

        // black background for 1 bpp output
        Assert.Equal(1u, pixa.DisplayTiledInRows(1, 100, 1.0f, 1, 3, 2).GetPixel(0, 0));
    }

    [Fact]
    public void DisplayTiledInRows_ScaledAndConverted()
    {
        Pixa pixa = Pixa.Create(0);
        pixa.AddPix(MakePix(6, 6, 8, 100), L.L_INSERT);
        pixa.AddPix(MakePix(6, 6, 32, 0x64646400), L.L_INSERT);
        Pix pixd = pixa.DisplayTiledInRows(32, 200, 2.0f, 0, 4, 0);
        pixd.GetDimensions(out int w, out int h, out int d);
        Assert.Equal((4 + 12 + 4 + 12 + 4, 4 + 12 + 4, 32), (w, h, d));
    }

    [Fact]
    public void DisplayTiledInRows_InvertibleWithCreateFromBoxa()
    {
        Pixa pixa = Pixa.Create(0);
        pixa.AddPix(MakePix(10, 6, 8, 10), L.L_INSERT);
        pixa.AddPix(MakePix(7, 9, 8, 20), L.L_INSERT);
        pixa.AddPix(MakePix(5, 5, 8, 30), L.L_INSERT);
        Pix pixd = pixa.DisplayTiledInRows(8, 20, 1.0f, 0, 3, 0);
        Pixa back = Pixa.CreateFromBoxa(pixd, TextBoxa(pixd), 0, 0, out bool cropwarn);
        Assert.False(cropwarn);
        Assert.Equal(3, back.Count);
        for (int i = 0; i < 3; i++) {
            Pix a = pixa.GetPix(i, L.L_CLONE);
            Pix b = back.GetPix(i, L.L_CLONE);
            Assert.Equal(a.Width, b.Width);
            Assert.Equal(a.Height, b.Height);
            for (int y = 0; y < a.Height; y++)
                for (int x = 0; x < a.Width; x++)
                    Assert.Equal(a.GetPixel(x, y), b.GetPixel(x, y));
        }
    }

    [Fact]
    public void DisplayTiledInRows_Errors()
    {
        Pixa pixa = MakePixa(1, 4, 4, 8, 1);
        Assert.Throws<ArgumentException>(() => pixa.DisplayTiledInRows(16, 100, 1.0f, 0, 1, 0));
        Assert.Throws<ArgumentException>(() => Pixa.Create(0).DisplayTiledInRows(8, 100, 1.0f, 0, 1, 0));
    }

    /*--------------------------- DisplayTiledAndScaled ----------------------*/

    [Fact]
    public void DisplayTiledAndScaled_SizesAndPlacement()
    {
        Pixa pixa = Pixa.Create(0);
        pixa.AddPix(MakePix(20, 10, 8, 100), L.L_INSERT);
        pixa.AddPix(MakePix(20, 10, 8, 60), L.L_INSERT);
        pixa.AddPix(MakePix(10, 30, 8, 200), L.L_INSERT);  // scaled by 2.0 to 20 x 60

        Pix pixd = pixa.DisplayTiledAndScaled(8, 20, 2, 0, 4, 0);
        pixd.GetDimensions(out int w, out int h, out int d);
        Assert.Equal((52, 4 * 3 + 10 + 60, 8), (w, h, d));
        Assert.Equal(100u, pixd.GetPixel(4, 4));
        Assert.Equal(100u, pixd.GetPixel(23, 13));
        Assert.Equal(255u, pixd.GetPixel(24, 4));
        Assert.Equal(60u, pixd.GetPixel(28, 4));
        AssertNear(200, pixd.GetPixel(4, 18));
        AssertNear(200, pixd.GetPixel(23, 77));
        Assert.Equal(255u, pixd.GetPixel(3, 18));
        Assert.Equal(255u, pixd.GetPixel(28, 18));
    }

    [Fact]
    public void DisplayTiledAndScaled_DepthsAndBackground()
    {
        Pixa pixa = MakePixa(3, 20, 10, 8, 100);
        Pix pix32 = pixa.DisplayTiledAndScaled(32, 20, 3, 0, 2, 0);
        pix32.GetDimensions(out int w, out int h, out int d);
        Assert.Equal((20 * 3 + 2 * 4, 10 + 2 * 2, 32), (w, h, d));

        Pix pix1 = pixa.DisplayTiledAndScaled(1, 20, 3, 1, 2, 0);
        Assert.Equal(1, pix1.Depth);
        Assert.Equal(1u, pix1.GetPixel(0, 0));  // black background

        Pix pix8 = pixa.DisplayTiledAndScaled(8, 20, 3, 1, 2, 0);
        Assert.Equal(0u, pix8.GetPixel(0, 0));
    }

    [Fact]
    public void DisplayTiledAndScaled_BorderIsLimitedToAFifth()
    {
        Pixa pixa = MakePixa(1, 20, 10, 8, 100);
        // border 5 > 20 / 5: ignored
        Pix pixd = pixa.DisplayTiledAndScaled(8, 20, 1, 0, 0, 5);
        pixd.GetDimensions(out int w, out int h, out _);
        Assert.Equal((20, 10), (w, h));

        // border 2: the image is scaled to 16 wide and a border of 2 is added
        pixd = pixa.DisplayTiledAndScaled(8, 20, 1, 0, 0, 2);
        pixd.GetDimensions(out w, out h, out _);
        Assert.Equal((20, 8 + 4), (w, h));
        Assert.Equal(0u, pixd.GetPixel(0, 0));  // black border
    }

    [Fact]
    public void DisplayTiledAndScaled_Errors()
    {
        Pixa pixa = MakePixa(1, 20, 10, 8, 100);
        Assert.Throws<ArgumentException>(() => pixa.DisplayTiledAndScaled(4, 20, 1, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => pixa.DisplayTiledAndScaled(8, 20, 0, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => Pixa.Create(0).DisplayTiledAndScaled(8, 20, 1, 0, 0, 0));
    }

    /*------------------------------- Pixaa display --------------------------*/

    private static Pixaa MakePixaaForDisplay(bool withTopBoxes)
    {
        Pixaa paa = Pixaa.Create(0);
        Pixa pixa0 = Pixa.Create(0);
        Pix a = Pix.Create(4, 4, 1);
        a.SetAll();
        pixa0.AddPix(a, L.L_INSERT);
        pixa0.AddBox(Box.Create(1, 1, 4, 4), L.L_INSERT);
        Pixa pixa1 = Pixa.Create(0);
        Pix b = Pix.Create(2, 2, 1);
        b.SetAll();
        pixa1.AddPix(b, L.L_INSERT);
        pixa1.AddBox(Box.Create(0, 0, 2, 2), L.L_INSERT);
        paa.AddPixa(pixa0, L.L_INSERT);
        paa.AddPixa(pixa1, L.L_INSERT);
        if (withTopBoxes) {
            paa.AddBox(Box.Create(10, 10, 10, 10), L.L_INSERT);
            paa.AddBox(Box.Create(20, 0, 10, 10), L.L_INSERT);
        }
        return paa;
    }

    [Fact]
    public void PixaaDisplay_TranslatesByTopLevelBoxes()
    {
        Pix pixd = MakePixaaForDisplay(true).Display(0, 0);
        pixd.GetDimensions(out int w, out int h, out int d);
        Assert.Equal((30, 20, 1), (w, h, d));
        Assert.Equal(1u, pixd.GetPixel(11, 11));
        Assert.Equal(1u, pixd.GetPixel(14, 14));
        Assert.Equal(0u, pixd.GetPixel(10, 10));
        Assert.Equal(1u, pixd.GetPixel(20, 0));
        Assert.Equal(1u, pixd.GetPixel(21, 1));
        Assert.Equal(0u, pixd.GetPixel(22, 0));
    }

    [Fact]
    public void PixaaDisplay_WithoutTopLevelBoxes()
    {
        Pix pixd = MakePixaaForDisplay(false).Display(0, 0);
        pixd.GetDimensions(out int w, out int h, out _);
        Assert.Equal((5, 5), (w, h));
        Assert.Equal(1u, pixd.GetPixel(1, 1));
        Assert.Equal(1u, pixd.GetPixel(0, 0));  // the second pixa is also at the origin
        Assert.Equal(0u, pixd.GetPixel(0, 4));

        pixd = MakePixaaForDisplay(false).Display(8, 9);
        pixd.GetDimensions(out w, out h, out _);
        Assert.Equal((8, 9), (w, h));
        Assert.Throws<ArgumentException>(() => Pixaa.Create(0).Display(0, 0));
    }

    [Fact]
    public void PixaaDisplayTiledAndScaled()
    {
        Pixaa paa = Pixaa.Create(0);
        paa.AddPixa(MakePixa(3, 20, 10, 8, 100), L.L_INSERT);
        paa.AddPixa(MakePixa(1, 20, 10, 8, 100), L.L_INSERT);
        Pixa pixa = paa.DisplayTiledAndScaled(8, 20, 2, 0, 4, 0);
        Assert.Equal(2, pixa.Count);
        pixa.GetPixDimensions(0, out int w, out int h, out _);
        Assert.Equal((52, 4 * 3 + 10 + 10), (w, h));
        pixa.GetPixDimensions(1, out w, out h, out _);
        Assert.Equal((52, 4 * 2 + 10), (w, h));
        Assert.Throws<ArgumentException>(() => paa.DisplayTiledAndScaled(2, 20, 2, 0, 4, 0));
        Assert.Throws<ArgumentException>(() => paa.DisplayTiledAndScaled(8, 20, 0, 0, 4, 0));
        Assert.Throws<ArgumentException>(() => Pixaa.Create(0).DisplayTiledAndScaled(8, 20, 2, 0, 4, 0));
    }

    /*------------------------------- conversions ----------------------------*/

    [Fact]
    public void ConvertTo1_8_32_KeepBoxes()
    {
        Pixa pixas = Pixa.Create(0);
        for (int i = 0; i < 2; i++) {
            pixas.AddPix(MakePix(6, 5, 8, (uint)(50 + 100 * i)), L.L_INSERT);
            pixas.AddBox(Box.Create(i, 2 * i, 6, 5), L.L_INSERT);
        }

        Pixa pixa1 = pixas.ConvertTo1(128);
        Assert.Equal(2, pixa1.Count);
        Assert.Equal(1, pixa1.GetPix(0, L.L_CLONE).Depth);
        Assert.Equal(1u, pixa1.GetPix(0, L.L_CLONE).GetPixel(0, 0));  // dark: ON
        Assert.Equal(0u, pixa1.GetPix(1, L.L_CLONE).GetPixel(0, 0));
        Assert.Equal(2, pixa1.BoxaCount);
        Assert.NotSame(pixas.Boxa, pixa1.Boxa);
        pixa1.GetBoxGeometry(1, out int x, out int y, out _, out _);
        Assert.Equal((1, 2), (x, y));

        Pixa pixa32 = pixas.ConvertTo32();
        Assert.Equal(32, pixa32.GetPix(1, L.L_CLONE).Depth);
        Assert.Equal(2, pixa32.BoxaCount);

        Pixa pixa8 = pixa1.ConvertTo8(false);
        Assert.Equal(8, pixa8.GetPix(0, L.L_CLONE).Depth);
        Assert.Null(pixa8.GetPix(0, L.L_CLONE).Colormap);
        Assert.Equal(2, pixa8.BoxaCount);

        Pixa pixa8c = pixa1.ConvertTo8(true);
        Assert.NotNull(pixa8c.GetPix(0, L.L_CLONE).Colormap);
    }

    [Fact]
    public void ConvertTo_EmptyPixa()
    {
        Pixa empty = Pixa.Create(0);
        Assert.Equal(0, empty.ConvertTo1(128).Count);
        Assert.Equal(0, empty.ConvertTo8(false).Count);
        Assert.Equal(0, empty.ConvertTo32().Count);
    }

    /*------------------------------ ConstrainedSelect -----------------------*/

    [Fact]
    public void ConstrainedSelect_Range()
    {
        Pixa pixas = Pixa.Create(0);
        for (int i = 0; i < 10; i++)
            pixas.AddPix(Pix.Create(4 + i, 4, 8), L.L_INSERT);

        Pixa pixad = pixas.ConstrainedSelect(2, 7, 100, 0, L.L_CLONE);
        Assert.Equal(6, pixad.Count);
        for (int i = 0; i < 6; i++)
            Assert.Same(pixas.GetPix(2 + i, L.L_CLONE), pixad.GetPix(i, L.L_CLONE));

        pixad = pixas.ConstrainedSelect(0, -1, 3, 0, L.L_COPY);
        Assert.InRange(pixad.Count, 1, 3);
        Assert.NotSame(pixas.GetPix(0, L.L_CLONE), pixad.GetPix(0, L.L_CLONE));
        Assert.Equal(pixas.GetPix(0, L.L_CLONE).Width, pixad.GetPix(0, L.L_CLONE).Width);
    }

    [Fact]
    public void ConstrainedSelect_Errors()
    {
        Pixa pixas = MakePixa(5, 4, 4, 8, 1);
        Assert.Throws<ArgumentException>(() => pixas.ConstrainedSelect(3, 2, 2, 0, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => pixas.ConstrainedSelect(0, -1, 0, 0, L.L_CLONE));
    }

    /*------------------------------ tile helpers ----------------------------*/

    [Fact]
    public void GetTileCount_ParsesTheTextField()
    {
        Pix pix = Pix.Create(4, 4, 8);
        Assert.Equal(0, pix.GetTileCount());
        pix.Text = "n = 12";
        Assert.Equal(12, pix.GetTileCount());
        pix.Text = "n=3";  // fewer than 5 chars: ignored
        Assert.Equal(0, pix.GetTileCount());
        pix.Text = "something else";
        Assert.Equal(0, pix.GetTileCount());
        pix.Text = "n = abc";
        Assert.Equal(0, pix.GetTileCount());
        pix.Text = "n = 7 and more";
        Assert.Equal(7, pix.GetTileCount());
    }

    private static Pix MakeTiledPix()
    {
        Pix pixs = Pix.Create(30, 20, 8);
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 3; j++)
                for (int y = 0; y < 10; y++)
                    for (int x = 0; x < 10; x++)
                        pixs.SetPixel(10 * j + x, 10 * i + y, (uint)(10 + 20 * (3 * i + j)));
        return pixs;
    }

    [Fact]
    public void MakeFromTiledPix_UniformTiles()
    {
        Pix pixs = MakeTiledPix();
        Pixa pixa = Pixa.MakeFromTiledPix(pixs, 10, 10, 0, 0, null);
        Assert.Equal(6, pixa.Count);
        pixa.GetPixDimensions(4, out int w, out int h, out int d);
        Assert.Equal((10, 10, 8), (w, h, d));
        Assert.Equal(90u, pixa.GetPix(4, L.L_CLONE).GetPixel(3, 3));
        Assert.Equal(10u, pixa.GetPix(0, L.L_CLONE).GetPixel(9, 9));

        pixa = Pixa.MakeFromTiledPix(pixs, 10, 10, 1, 2, null);
        Assert.Equal(2, pixa.Count);
        Assert.Equal(30u, pixa.GetPix(0, L.L_CLONE).GetPixel(0, 0));
        Assert.Equal(50u, pixa.GetPix(1, L.L_CLONE).GetPixel(0, 0));

        pixa = Pixa.MakeFromTiledPix(pixs, 10, 10, 4, 100, null);  // up to the end
        Assert.Equal(2, pixa.Count);
    }

    [Fact]
    public void MakeFromTiledPix_UsesTheTileCountOfTheText()
    {
        Pix pixs = MakeTiledPix();
        pixs.Text = "n = 5";
        Assert.Equal(5, Pixa.MakeFromTiledPix(pixs, 10, 10, 0, 0, null).Count);
        pixs.Text = "n = 3";  // not in (3, 6]: all tiles are valid
        Assert.Equal(6, Pixa.MakeFromTiledPix(pixs, 10, 10, 0, 0, null).Count);
        pixs.Text = "n = 4";
        Assert.Equal(3, Pixa.MakeFromTiledPix(pixs, 10, 10, 1, 0, null).Count);
    }

    [Fact]
    public void MakeFromTiledPix_BoxaCase_AndErrors()
    {
        Pix pixs = MakeTiledPix();
        Boxa boxa = Boxa.Create(0);
        boxa.AddBox(Box.Create(0, 0, 5, 6), L.L_INSERT);
        boxa.AddBox(Box.Create(10, 10, 7, 8), L.L_INSERT);
        Pixa pixa = Pixa.MakeFromTiledPix(pixs, 0, 0, 0, 0, boxa);
        Assert.Equal(2, pixa.Count);
        pixa.GetPixDimensions(1, out int w, out int h, out _);
        Assert.Equal((7, 8), (w, h));
        Assert.Equal(2, pixa.BoxaCount);

        Assert.Throws<ArgumentException>(() => Pixa.MakeFromTiledPix(pixs, 0, 10, 0, 0, null));
        Assert.Throws<ArgumentException>(() => Pixa.MakeFromTiledPix(pixs, 31, 10, 0, 0, null));
        Assert.Throws<ArgumentException>(() => Pixa.MakeFromTiledPix(pixs, 10, 21, 0, 0, null));
    }

    [Fact]
    public void MakeFromTiledPix_CopiesTheColormap()
    {
        Pix pixs = Pix.CreateWithCmap(20, 10, 8, L.L_SET_WHITE);
        Pixa pixa = Pixa.MakeFromTiledPix(pixs, 10, 10, 0, 0, null);
        Assert.Equal(2, pixa.Count);
        PixColormap? cmap0 = pixa.GetPix(0, L.L_CLONE).Colormap;
        PixColormap? cmap1 = pixa.GetPix(1, L.L_CLONE).Colormap;
        Assert.NotNull(cmap0);
        Assert.NotNull(cmap1);
        Assert.NotSame(cmap0, cmap1);
        Assert.NotSame(pixs.Colormap, cmap0);
    }

    [Fact]
    public void MakeFromTiledPixa_LabelsTheDigits()
    {
        Pixa pixas = Pixa.Create(0);
        for (int i = 0; i < 10; i++) {
            Pix mosaic = Pix.Create(20, 20, 1);
            mosaic.Text = "n = 4";
            pixas.AddPix(mosaic, L.L_INSERT);
        }

        Pixa pixad = pixas.MakeFromTiledPixa(10, 10, 3);
        Assert.Equal(30, pixad.Count);
        Assert.Equal("0", pixad.GetPix(0, L.L_CLONE).Text);
        Assert.Equal("1", pixad.GetPix(3, L.L_CLONE).Text);
        Assert.Equal("9", pixad.GetPix(29, L.L_CLONE).Text);
        pixad.GetPixDimensions(0, out int w, out int h, out int d);
        Assert.Equal((10, 10, 1), (w, h, d));

        Assert.Throws<ArgumentException>(() => pixas.MakeFromTiledPixa(10, 10, 1001));
        Assert.Throws<ArgumentException>(() => Pixa.Create(0).MakeFromTiledPixa(10, 10, 3));
    }

    /*-------------------- tests that use pixafunc1.c callees ----------------*/

    [Fact]
    public void DisplayTiledInColumns_LayoutAndBoxa()
    {
        Pixa pixa = MakePixa(3, 10, 10, 8, 77);
        Pix pixd = pixa.DisplayTiledInColumns(2, 1.0f, 5, 0);
        pixd.GetDimensions(out int w, out int h, out int d);
        Assert.Equal((35, 35, 8), (w, h, d));
        Boxa boxa = TextBoxa(pixd);
        Assert.Equal(3, boxa.Count);
        AssertBox(boxa, 0, 5, 5, 10, 10);
        AssertBox(boxa, 1, 20, 5, 10, 10);
        AssertBox(boxa, 2, 5, 20, 10, 10);
        Assert.Equal(77u, pixd.GetPixel(5, 5));
        Assert.Equal(255u, pixd.GetPixel(4, 4));
        Assert.Equal(255u, pixd.GetPixel(20, 20));
    }

    [Fact]
    public void DisplayTiledInColumns_MixedDepthsBorderAndScale()
    {
        Pixa pixa = Pixa.Create(0);
        pixa.AddPix(MakePix(10, 10, 1, 0), L.L_INSERT);
        pixa.AddPix(MakePix(10, 10, 8, 50), L.L_INSERT);
        Pix pixd = pixa.DisplayTiledInColumns(2, 2.0f, 4, 1);
        Assert.Equal(8, pixd.Depth);
        Boxa boxa = TextBoxa(pixd);
        AssertBox(boxa, 0, 4, 4, 22, 22);  // scaled 20 plus the border of 1 on both sides
        AssertBox(boxa, 1, 30, 4, 22, 22);
        pixd.GetDimensions(out int w, out int h, out _);
        Assert.Equal((30 + 22 + 4 + 4 - 4, 4 + 22 + 4 + 4 - 4), (w, h));

        // inverse with CreateFromBoxa for a uniform pixa
        Pixa uniform = MakePixa(5, 8, 6, 8, 99);
        Pix tiled = uniform.DisplayTiledInColumns(3, 1.0f, 2, 0);
        Pixa back = Pixa.CreateFromBoxa(tiled, TextBoxa(tiled), 0, 0, out _);
        Assert.Equal(5, back.Count);
        Assert.Equal(uniform.GetPix(0, L.L_CLONE).GetPixel(3, 3), back.GetPix(4, L.L_CLONE).GetPixel(3, 3));
    }

    [Fact]
    public void DisplayLinearly_Horizontal()
    {
        Pixa pixa = MakePixa(3, 10, 8, 8, 40);
        Pix pixd = pixa.DisplayLinearly(L.L_HORIZ, 1.0f, 0, 3, 0, out Boxa? boxa);
        pixd.GetDimensions(out int w, out int h, out _);
        Assert.Equal((36, 8), (w, h));
        Assert.NotNull(boxa);
        AssertBox(boxa!, 1, 13, 0, 10, 8);
        AssertBox(boxa!, 2, 26, 0, 10, 8);

        pixd = pixa.DisplayLinearly(L.L_VERT, 1.0f, 0, 3, 1, out boxa);
        pixd.GetDimensions(out w, out h, out _);
        Assert.Equal((12, 3 * 10 + 2 * 3), (w, h));
        Assert.Throws<ArgumentException>(() => pixa.DisplayLinearly(0, 1.0f, 0, 3, 0, out _));
    }

    [Fact]
    public void DisplayOnLattice_OmitsLargePix()
    {
        Pixa pixa = MakePixa(4, 6, 6, 8, 20);
        pixa.AddPix(MakePix(10, 10, 8, 30), L.L_INSERT);  // too large for the 8 x 8 cells
        Pix pixd = pixa.DisplayOnLattice(8, 8, out int ncols, out Boxa? boxa);
        Assert.Equal(2, ncols);
        pixd.GetDimensions(out int w, out int h, out int d);
        Assert.Equal((16, 24, 8), (w, h, d));
        Assert.Equal("n = 5", pixd.Text);
        Assert.Equal(5, boxa!.Count);
        AssertBox(boxa, 1, 8, 0, 6, 6);
        AssertBox(boxa, 2, 0, 8, 6, 6);
        boxa.GetBoxGeometry(4, out _, out _, out int bw, out int bh);
        Assert.Equal((0, 0), (bw, bh));
        Assert.Equal(20u, pixd.GetPixel(0, 0));
        Assert.Equal(255u, pixd.GetPixel(7, 0));
        Assert.Equal(255u, pixd.GetPixel(0, 16));

        // different depths are rendered in rgb
        Pixa mixed = Pixa.Create(0);
        mixed.AddPix(MakePix(4, 4, 8, 3), L.L_INSERT);  // pixaVerifyDepth() only notices a smaller depth that comes later
        mixed.AddPix(MakePix(4, 4, 1, 0), L.L_INSERT);
        Assert.Equal(32, mixed.DisplayOnLattice(4, 4, out _, out _).Depth);

        // the lattice can be used to regenerate the pixa
        Pixa uniform = MakePixa(5, 8, 8, 8, 60);
        Pix lattice = uniform.DisplayOnLattice(8, 8, out _, out _);
        Pixa back = Pixa.MakeFromTiledPix(lattice, 8, 8, 0, 0, null);
        Assert.Equal(5, back.Count);
    }

    [Fact]
    public void DisplayTiled_Lattice()
    {
        Pixa pixa = MakePixa(5, 10, 10, 8, 77);
        pixa.AddPix(MakePix(10, 10, 8, 77), L.L_INSERT);
        Pix pixd = pixa.DisplayTiled(50, 0, 2);
        pixd.GetDimensions(out int w, out int h, out int d);
        Assert.Equal((50, 10 * 2 + 2 * 3, 8), (w, h, d));
        Assert.Equal(255u, pixd.GetPixel(0, 0));
        Assert.Equal(77u, pixd.GetPixel(2, 2));
        Assert.Equal(77u, pixd.GetPixel(14, 2));

        Pixa mixed = Pixa.Create(0);
        mixed.AddPix(MakePix(4, 4, 1, 0), L.L_INSERT);
        mixed.AddPix(MakePix(4, 4, 8, 3), L.L_INSERT);
        Assert.Throws<ArgumentException>(() => mixed.DisplayTiled(50, 0, 2));
    }

    [Fact]
    public void DisplayMultiTiled_Pages()
    {
        Pixa pixa = MakePixa(5, 10, 10, 8, 77);
        Pixa pages = pixa.DisplayMultiTiled(2, 2, 0, 0, 1.0f, 3, 0);
        Assert.Equal(2, pages.Count);
        pages.GetPixDimensions(0, out int w, out int h, out _);
        Assert.Equal((3 + 10 + 3 + 10 + 3, 3 + 10 + 3 + 10 + 3), (w, h));
        Assert.Throws<ArgumentException>(() => pixa.DisplayMultiTiled(0, 2, 0, 0, 1.0f, 3, 0));
        Assert.Throws<ArgumentException>(() => pixa.DisplayMultiTiled(2, 51, 0, 0, 1.0f, 3, 0));
    }

    [Fact]
    public void PixaaDisplayByPixa_Rows()
    {
        Pixaa paa = Pixaa.Create(0);
        paa.AddPixa(MakePixa(3, 10, 10, 8, 77), L.L_INSERT);
        paa.AddPixa(MakePixa(2, 10, 10, 8, 77), L.L_INSERT);
        Pix pixd = paa.DisplayByPixa(3, 1.0f, 2, 6, 0);
        Assert.Equal(8, pixd.Depth);
        Assert.Throws<ArgumentException>(() => Pixaa.Create(0).DisplayByPixa(3, 1.0f, 2, 6, 0));
    }

    [Fact]
    public void DisplayPairTiledInColumns_WithoutText()
    {
        Pixa pixa1 = MakePixa(2, 10, 10, 8, 20);
        Pixa pixa2 = MakePixa(2, 10, 10, 8, 30);
        Pix pixd = pixa1.DisplayPairTiledInColumns(pixa2, 2, 1.0f, 0, 4, 0, 0, 0, 0, null);
        Assert.Equal(8, pixd.Depth);
        Assert.Throws<ArgumentException>(() =>
            pixa1.DisplayPairTiledInColumns(MakePixa(3, 10, 10, 8, 30), 2, 1.0f, 0, 4, 0, 0, 0, 0, null));
    }

    [Fact]
    public void ConvertToNUpPixa_Pages()
    {
        Pixa pixa = MakePixa(5, 40, 20, 8, 80);
        Pixa pages = pixa.ConvertToNUpPixa(null, 2, 2, 20, 4, 0, 0);
        Assert.Equal(2, pages.Count);
        Assert.Throws<ArgumentException>(() => pixa.ConvertToNUpPixa(null, 2, 2, 19, 4, 0, 0));
        Assert.Throws<ArgumentException>(() => pixa.ConvertToNUpPixa(null, 2, 2, 20, 4, 0, 2));
    }
}
