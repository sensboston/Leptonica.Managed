using System;
using Leptonica;

namespace Leptonica.Managed.Tests;

/// <summary>
/// Tests for boxfunc3.c with hand-computed expected values.  Functions whose callees
/// are stubs (connected components, colormap helpers, conversions, ...) are tested
/// only up to the point where those callees are reached.
/// </summary>
public class BoxFunc3Tests
{
    private static Boxa MakeBoxa(params (int x, int y, int w, int h)[] boxes)
    {
        Boxa boxa = Boxa.Create(0);
        foreach (var b in boxes)
            boxa.AddBox(Box.Create(b.x, b.y, b.w, b.h), L.L_INSERT);
        return boxa;
    }

    private static (int, int, int, int) G(Boxa boxa, int i)
    {
        boxa.GetBoxGeometry(i, out int x, out int y, out int w, out int h);
        return (x, y, w, h);
    }

    private static int Bit(Pix pix, int x, int y)
    {
        return ArrayAccess.GetDataBit(pix.Data.AsSpan(y * pix.Wpl), x);
    }

    private static int CountOn(Pix pix)
    {
        pix.GetDimensions(out int w, out int h, out _);
        int count = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                count += Bit(pix, x, y);
        return count;
    }

    private static Pix Mask(int w, int h, params (int x, int y, int w, int h)[] boxes)
    {
        Pix pix = Pix.Create(w, h, 1);
        Pix.MaskBoxa(pix, pix, MakeBoxa(boxes), L.L_SET_PIXELS);
        return pix;
    }

    /*--------------------------- makeMosaicStrips ---------------------------*/

    [Fact]
    public void MakeMosaicStrips_Horizontal_LastStripIsShorter()
    {
        Boxa boxa = Boxa.MakeMosaicStrips(25, 7, L.L_SCAN_HORIZONTAL, 10);
        Assert.Equal(3, boxa.Count);
        Assert.Equal((0, 0, 10, 7), G(boxa, 0));
        Assert.Equal((10, 0, 10, 7), G(boxa, 1));
        Assert.Equal((20, 0, 5, 7), G(boxa, 2));
    }

    [Fact]
    public void MakeMosaicStrips_Vertical_LastStripIsShorter()
    {
        Boxa boxa = Boxa.MakeMosaicStrips(7, 25, L.L_SCAN_VERTICAL, 10);
        Assert.Equal(3, boxa.Count);
        Assert.Equal((0, 0, 7, 10), G(boxa, 0));
        Assert.Equal((0, 10, 7, 10), G(boxa, 1));
        Assert.Equal((0, 20, 7, 5), G(boxa, 2));
    }

    [Fact]
    public void MakeMosaicStrips_ExactMultiple_HasNoExtraStrip()
    {
        Boxa boxa = Boxa.MakeMosaicStrips(20, 3, L.L_SCAN_HORIZONTAL, 10);
        Assert.Equal(2, boxa.Count);
        Assert.Equal((10, 0, 10, 3), G(boxa, 1));
    }

    [Fact]
    public void MakeMosaicStrips_SizeLargerThanImage_GivesOneStrip()
    {
        Boxa boxa = Boxa.MakeMosaicStrips(5, 4, L.L_SCAN_HORIZONTAL, 10);
        Assert.Equal(1, boxa.Count);
        Assert.Equal((0, 0, 5, 4), G(boxa, 0));

        boxa = Boxa.MakeMosaicStrips(5, 4, L.L_SCAN_VERTICAL, 10);
        Assert.Equal(1, boxa.Count);
        Assert.Equal((0, 0, 5, 4), G(boxa, 0));
    }

    [Fact]
    public void MakeMosaicStrips_SizeOne_OneStripPerColumn()
    {
        Boxa boxa = Boxa.MakeMosaicStrips(3, 2, L.L_SCAN_HORIZONTAL, 1);
        Assert.Equal(3, boxa.Count);
        Assert.Equal((2, 0, 1, 2), G(boxa, 2));
    }

    [Fact]
    public void MakeMosaicStrips_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentException>(() => Boxa.MakeMosaicStrips(0, 5, L.L_SCAN_HORIZONTAL, 2));
        Assert.Throws<ArgumentException>(() => Boxa.MakeMosaicStrips(5, 0, L.L_SCAN_HORIZONTAL, 2));
        Assert.Throws<ArgumentException>(() => Boxa.MakeMosaicStrips(5, 5, L.L_FROM_LEFT, 2));
        Assert.Throws<ArgumentException>(() => Boxa.MakeMosaicStrips(5, 5, L.L_SCAN_VERTICAL, 0));
    }

    /*------------------------------- MaskBoxa -------------------------------*/

    [Fact]
    public void MaskBoxa_SetPixels_NewPix()
    {
        Pix pixs = Pix.Create(10, 8, 1);
        Boxa boxa = MakeBoxa((2, 1, 3, 2), (7, 5, 3, 3));
        Pix pixd = Pix.MaskBoxa(null, pixs, boxa, L.L_SET_PIXELS);

        Assert.NotSame(pixs, pixd);
        Assert.Equal(0, CountOn(pixs));          // the source is untouched
        Assert.Equal(3 * 2 + 3 * 3, CountOn(pixd));
        Assert.Equal(1, Bit(pixd, 2, 1));
        Assert.Equal(1, Bit(pixd, 4, 2));
        Assert.Equal(0, Bit(pixd, 5, 2));
        Assert.Equal(0, Bit(pixd, 2, 3));
        Assert.Equal(1, Bit(pixd, 7, 5));
        Assert.Equal(1, Bit(pixd, 9, 7));
    }

    [Fact]
    public void MaskBoxa_InPlace_ReturnsSamePix()
    {
        Pix pix = Pix.Create(40, 3, 1);          // spans two words per line
        Pix pixd = Pix.MaskBoxa(pix, pix, MakeBoxa((30, 0, 10, 3)), L.L_SET_PIXELS);
        Assert.Same(pix, pixd);
        Assert.Equal(30, CountOn(pix));
        Assert.Equal(0, Bit(pix, 29, 1));
        Assert.Equal(1, Bit(pix, 30, 1));
        Assert.Equal(1, Bit(pix, 39, 2));
    }

    [Fact]
    public void MaskBoxa_ClearAndFlip()
    {
        Pix pix = Mask(10, 10, (0, 0, 10, 10));
        Assert.Equal(100, CountOn(pix));

        Pix.MaskBoxa(pix, pix, MakeBoxa((2, 2, 4, 4)), L.L_CLEAR_PIXELS);
        Assert.Equal(100 - 16, CountOn(pix));
        Assert.Equal(0, Bit(pix, 3, 3));

        // flip a box that overlaps the cleared one by 2 x 2 pixels
        Pix.MaskBoxa(pix, pix, MakeBoxa((4, 4, 4, 4)), L.L_FLIP_PIXELS);
        // the 4 x 4 box held 4 cleared pixels (now on) and 12 on pixels (now off)
        Assert.Equal(100 - 16 + 4 - 12, CountOn(pix));
        Assert.Equal(1, Bit(pix, 4, 4));
        Assert.Equal(0, Bit(pix, 7, 7));
    }

    [Fact]
    public void MaskBoxa_OverlappingBoxesSetTheUnion()
    {
        Pix pix = Mask(10, 10, (0, 0, 5, 5), (3, 3, 5, 5));
        Assert.Equal(25 + 25 - 4, CountOn(pix));
    }

    [Fact]
    public void MaskBoxa_EmptyBoxa_ReturnsCopyOrSamePix()
    {
        Pix pixs = Mask(8, 8, (1, 1, 2, 2));
        Pix copy = Pix.MaskBoxa(null, pixs, Boxa.Create(0), L.L_SET_PIXELS);
        Assert.NotSame(pixs, copy);
        Assert.Equal(pixs.Data, copy.Data);

        Pix same = Pix.MaskBoxa(pixs, pixs, Boxa.Create(0), L.L_FLIP_PIXELS);
        Assert.Same(pixs, same);
        Assert.Equal(4, CountOn(pixs));
    }

    [Fact]
    public void MaskBoxa_InvalidArguments_Throw()
    {
        Pix pix = Pix.Create(8, 8, 1);
        Pix other = Pix.Create(8, 8, 1);
        Boxa boxa = MakeBoxa((0, 0, 1, 1));
        Assert.Throws<ArgumentException>(() => Pix.MaskBoxa(other, pix, boxa, L.L_SET_PIXELS));
        Assert.Throws<ArgumentException>(() => Pix.MaskBoxa(null, pix, boxa, 0));
        Assert.Throws<ArgumentException>(() => Pix.MaskBoxa(null, pix, boxa, 4));
    }

    /*--------------------- empty-boxa shortcuts and checks --------------------*/

    [Fact]
    public void Paint_Draw_Blend_EmptyBoxa_ReturnACopy()
    {
        Pix pixs = Mask(8, 8, (2, 2, 3, 3));
        Boxa empty = Boxa.Create(0);

        Pix[] results =
        {
            pixs.PaintBoxa(empty, 0xff000000),
            pixs.PaintBoxaRandom(empty),
            pixs.BlendBoxaRandom(empty, 0.5f),
            pixs.BlendBoxaRandom(empty, 2.0f),       // out of range: warning, then the same path
            pixs.DrawBoxa(empty, 1, 0xff000000),
            pixs.DrawBoxaRandom(empty, 1),
        };
        foreach (Pix pixd in results) {
            Assert.NotSame(pixs, pixd);
            Assert.Equal(pixs.Data, pixd.Data);
        }
    }

    [Fact]
    public void DrawBoxa_InvalidWidth_Throws()
    {
        Pix pix = Pix.Create(8, 8, 1);
        Boxa boxa = MakeBoxa((0, 0, 4, 4));
        Assert.Throws<ArgumentException>(() => pix.DrawBoxa(boxa, 0, 0));
        Assert.Throws<ArgumentException>(() => pix.DrawBoxaRandom(boxa, 0));
    }

    [Fact]
    public void SetBlackOrWhiteBoxa_NullOrEmptyBoxa_ReturnsCopy()
    {
        Pix pixs = Mask(8, 8, (2, 2, 3, 3));
        Pix copy1 = pixs.SetBlackOrWhiteBoxa(null, L.L_SET_WHITE);
        Pix copy2 = pixs.SetBlackOrWhiteBoxa(Boxa.Create(0), L.L_SET_BLACK);
        Assert.NotSame(pixs, copy1);
        Assert.NotSame(pixs, copy2);
        Assert.Equal(pixs.Data, copy1.Data);
        Assert.Equal(pixs.Data, copy2.Data);
    }

    [Fact]
    public void MaskConnComp_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(8, 8, 8).MaskConnComp(4, out _));
        Assert.Throws<ArgumentException>(() => Pix.Create(8, 8, 1).MaskConnComp(6, out _));
    }

    [Fact]
    public void SplitIntoBoxa_NotOneBpp_Throws()
    {
        Pix pix = Pix.Create(8, 8, 8);
        Assert.Throws<ArgumentException>(() => pix.SplitIntoBoxa(5, 0, 3, 2, 0, 0));
        Assert.Throws<ArgumentException>(() => pix.SplitComponentIntoBoxa(null, 5, 0, 3, 2, 0, 0));
    }

    [Fact]
    public void DisplayBoxaa_InvalidArguments_Throw()
    {
        Pixa pixas = Pixa.Create(0);
        Boxaa empty = Boxaa.Create(0);
        Assert.Throws<ArgumentException>(() => pixas.DisplayBoxaa(empty, L.L_DRAW_RED, 0));
        Assert.Throws<ArgumentException>(() => pixas.DisplayBoxaa(empty, L.L_DRAW_RED, 1));
    }

    [Fact]
    public void DisplayBoxaa_DrawRandom_DrawsBoxesOn32BppOutput()
    {
        Pixa pixas = Pixa.Create(1);
        pixas.AddPix(Pix.Create(20, 20, 32), L.L_INSERT);
        Boxaa baa = Boxaa.Create(1);
        baa.AddBoxa(MakeBoxa((2, 2, 10, 10)), L.L_INSERT);

        Pixa pixad = pixas.DisplayBoxaa(baa, L.L_DRAW_RANDOM, 2);
        Assert.Equal(1, pixad.Count);
        Pix outPix = pixad.GetPix(0, L.L_CLONE);
        Assert.Equal(32, outPix.Depth);
        outPix.GetPixel(2, 2, out uint borderVal);
        outPix.GetPixel(0, 0, out uint bgVal);
        Assert.Equal(0u, bgVal);
        Assert.NotEqual(0u, borderVal);
    }

    /*--------------------------- SelectLargeUL* ---------------------------*/

    [Fact]
    public void SelectLargeULBox_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentException>(() => Boxa.Create(0).SelectLargeULBox(0.9f, 5));
        Boxa boxa = MakeBoxa((0, 0, 4, 4));
        Assert.Throws<ArgumentException>(() => boxa.SelectLargeULBox(-0.1f, 5));
        Assert.Throws<ArgumentException>(() => boxa.SelectLargeULBox(1.1f, 5));
    }

    [Fact]
    public void SelectLargeULComp_InvalidAreaslop_Throws()
    {
        Pix pix = Pix.Create(8, 8, 1);
        Assert.Throws<ArgumentException>(() => pix.SelectLargeULComp(1.5f, 5, 8));
        Assert.Throws<ArgumentException>(() => pix.SelectLargeULComp(-0.5f, 5, 8));
    }

    /*------------------------- SearchForRectangle -------------------------*/

    /// <summary>Runs one sweep and returns (score, box) of the single result.</summary>
    private static (int score, (int, int, int, int) box) Search(
        Pix pix, int minsum, int delta, int maxbg, int side)
    {
        Box boxs = Box.Create(0, 0, pix.Width, pix.Height);
        Boxa boxat = Boxa.Create(4);
        Numa nascore = Numa.Create(4);
        Pix.SearchForRectangle(pix, boxs, minsum, 0, delta, maxbg, side, boxat, nascore);
        Assert.Equal(1, boxat.Count);
        Assert.Equal(1, nascore.Count);
        return (nascore.GetIValue(0), G(boxat, 0));
    }

    // 40 x 20 image, solid block at x = 5..24, y = 4..16 (20 x 13 = 260 pixels)
    private static Pix SolidBlock() => Mask(40, 20, (5, 4, 20, 13));

    [Fact]
    public void SearchForRectangle_FromLeft()
    {
        // The scan starts at column 0: width is 25 (up to the first column that
        // differs by delta), score = 2 * 260 - 25 * 13.
        var (score, box) = Search(SolidBlock(), 5, 3, 2, L.L_FROM_LEFT);
        Assert.Equal((0, 4, 25, 13), box);
        Assert.Equal(2 * 260 - 25 * 13, score);
    }

    [Fact]
    public void SearchForRectangle_FromRight()
    {
        // Columns 39..25 are empty; the block is swept from x = 24 down to 5,
        // the stop column is 4, so x0 = 5 and the width is 40 - 5 = 35.
        var (score, box) = Search(SolidBlock(), 5, 3, 2, L.L_FROM_RIGHT);
        Assert.Equal((5, 4, 35, 13), box);
        Assert.Equal(2 * 260 - 35 * 13, score);
    }

    [Fact]
    public void SearchForRectangle_FromTop()
    {
        // Rows 0..3 are empty, rows 4..16 hold 20 pixels each; row 17 ends the scan.
        var (score, box) = Search(SolidBlock(), 5, 3, 2, L.L_FROM_TOP);
        Assert.Equal((5, 0, 20, 17), box);
        Assert.Equal(2 * 260 - 20 * 17, score);
    }

    [Fact]
    public void SearchForRectangle_FromBot()
    {
        // Rows 19..17 are empty; the scan stops at row 3, so y0 = 4, height = 20 - 4.
        var (score, box) = Search(SolidBlock(), 5, 3, 2, L.L_FROM_BOT);
        Assert.Equal((5, 4, 20, 16), box);
        Assert.Equal(2 * 260 - 20 * 16, score);
    }

    [Fact]
    public void SearchForRectangle_BlockAtTheFarEdge_EndsAtLastColumn()
    {
        // Columns 5..29 of a 30 x 12 image, rows 2..9: the end of the image stops the scan.
        Pix pix = Mask(30, 12, (5, 2, 25, 8));
        var (score, box) = Search(pix, 4, 3, 0, L.L_FROM_LEFT);
        Assert.Equal((0, 2, 30, 8), box);
        Assert.Equal(2 * 200 - 30 * 8, score);
    }

    [Fact]
    public void SearchForRectangle_EmptyImage_Fails_WithMinimumBox()
    {
        var (score, box) = Search(Pix.Create(40, 20, 1), 5, 3, 2, L.L_FROM_LEFT);
        Assert.Equal(0, score);
        Assert.Equal((0, 0, 1, 1), box);
    }

    [Fact]
    public void SearchForRectangle_ReferenceLineBeyondTheRegion_Fails()
    {
        // The first column with enough fg is x = 5; the reference line x = 15
        // lies outside the 12 pixel wide image.
        Pix pix = Mask(12, 8, (5, 0, 7, 8));
        var (score, box) = Search(pix, 3, 3, 2, L.L_FROM_LEFT);
        Assert.Equal(0, score);
        Assert.Equal((0, 0, 1, 1), box);
    }

    [Fact]
    public void SearchForRectangle_TooMuchBackgroundInReferenceScan()
    {
        // Each column of x = 5..24 holds two 5-pixel runs at y = 4..8 and y = 12..16:
        // colsum = 10, extent = 13, so there are 3 bg pixels in the reference scan.
        Pix pix = Mask(40, 20, (5, 4, 20, 5), (5, 12, 20, 5));

        var (score, box) = Search(pix, 5, 3, 2, L.L_FROM_LEFT);   // 3 > maxbg
        Assert.Equal(0, score);
        Assert.Equal((0, 0, 1, 1), box);

        (score, box) = Search(pix, 5, 3, 3, L.L_FROM_LEFT);       // 3 <= maxbg
        Assert.Equal((0, 4, 25, 13), box);
        Assert.Equal(2 * 200 - 25 * 13, score);
    }

    [Fact]
    public void SearchForRectangle_UsesOnlyTheRegionOfBoxs()
    {
        // Restrict the search to x = 10..39: the block part is x = 10..24 (15 columns).
        // From the left: x = 10 is the first column with fg, xref = 20, the scan ends
        // at x = 25 with width 25 - 10 = 15 and score = 2 * (15 * 13) - 15 * 13.
        Pix pix = SolidBlock();
        Boxa boxat = Boxa.Create(4);
        Numa nascore = Numa.Create(4);
        Pix.SearchForRectangle(pix, Box.Create(10, 0, 30, 20), 5, 0, 3, 2,
                               L.L_FROM_LEFT, boxat, nascore);
        Assert.Equal((10, 4, 15, 13), G(boxat, 0));
        Assert.Equal(15 * 13, nascore.GetIValue(0));
    }

    [Fact]
    public void SearchForRectangle_NotOneBpp_Throws()
    {
        Pix pix = Pix.Create(8, 8, 8);
        Assert.Throws<ArgumentException>(() =>
            Pix.SearchForRectangle(pix, Box.Create(0, 0, 8, 8), 1, 0, 1, 0,
                                   L.L_FROM_LEFT, Boxa.Create(1), Numa.Create(1)));
    }
}
