using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests;

/// <summary>Tests for pageseg.c on small synthetic images.</summary>
public class PageSegTests
{
    /// <summary>1 bpp image of size w x h with the given rectangles (x, y, w, h) set.</summary>
    private static Pix Rects(int w, int h, params (int x, int y, int w, int h)[] rects)
    {
        Pix pix = Pix.Create(w, h, 1);
        foreach (var r in rects)
            pix.Rasterop(r.x, r.y, r.w, r.h, L.PIX_SET, null, 0, 0);
        return pix;
    }

    [Fact]
    public void FindLargestRectangleInBackground()
    {
        Pix pix = Rects(12, 10, (0, 0, 4, 10));
        pix.FindLargestRectangle(0, out Box box);
        box.GetGeometry(out int x, out int y, out int w, out int h);
        Assert.Equal((4, 0, 8, 10), (x, y, w, h));
    }

    [Fact]
    public void FindLargestRectangleInForeground()
    {
        Pix pix = Rects(12, 10, (0, 0, 4, 10), (8, 2, 2, 2));
        pix.FindLargestRectangle(1, out Box box, out Pix pixdb);
        box.GetGeometry(out int x, out int y, out int w, out int h);
        Assert.Equal((0, 0, 4, 10), (x, y, w, h));
        Assert.Equal(8, pixdb.Depth);
        Assert.Equal(12, pixdb.Width);
    }

    [Fact]
    public void FindLargeRectanglesAreSortedByAreaAndDoNotOverlap()
    {
        Pix pix = Rects(12, 10, (4, 0, 2, 10));
        pix.FindLargeRectangles(0, 2, out Boxa boxa, out Pix pixdb);
        Assert.Equal(2, boxa.Count);
        boxa.GetBoxGeometry(0, out int x, out int y, out int w, out int h);
        Assert.Equal((6, 0, 6, 10), (x, y, w, h));
        boxa.GetBoxGeometry(1, out x, out y, out w, out h);
        Assert.Equal((0, 0, 4, 10), (x, y, w, h));
        Assert.Equal(12, pixdb.Width);
    }

    [Fact]
    public void FindLargestRectangleChecksArguments()
    {
        Pix pix = Rects(12, 10);
        Assert.Throws<ArgumentException>(() => pix.FindLargestRectangle(2, out Box _));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).FindLargestRectangle(0, out Box _));
    }

    [Fact]
    public void FindThreshFgExtentFindsFirstAndLastRowOverThreshold()
    {
        Pix pix = Rects(20, 12, (0, 3, 6, 1), (0, 4, 20, 1), (0, 8, 5, 1), (0, 10, 4, 1));
        pix.FindThreshFgExtent(5, out int top, out int bot);
        Assert.Equal((3, 8), (top, bot));
        pix.FindThreshFgExtent(100, out top, out bot);
        Assert.Equal((0, 0), (top, bot));
    }

    [Fact]
    public void SplitComponentWithProfileSplitsAtNarrowNeck()
    {
        /* two bumps joined at a thin neck: the column profile has a deep, narrow minimum at x = 7 */
        int[] heights = { 10, 20, 30, 30, 30, 20, 10, 3, 10, 20, 30, 30, 30, 20, 10 };
        Pix pix = Pix.Create(heights.Length, 30, 1);
        for (int i = 0; i < heights.Length; i++)
            pix.Rasterop(i, 15 - heights[i] / 2, 1, heights[i], L.PIX_SET, null, 0, 0);
        Boxa boxa = pix.SplitComponentWithProfile(10, 7, out Pix? pixdb);
        Assert.Equal(2, boxa.Count);
        boxa.GetBoxGeometry(0, out int x, out int y, out int w, out int h);
        Assert.Equal((0, 0, 7, 30), (x, y, w, h));
        boxa.GetBoxGeometry(1, out x, out y, out w, out h);
        Assert.Equal((8, 0, 7, 30), (x, y, w, h));
        Assert.NotNull(pixdb);
        Assert.Equal(32, pixdb!.Depth);
    }

    [Fact]
    public void SplitComponentWithProfileLeavesSolidBlockAlone()
    {
        Pix pix = Rects(20, 30, (0, 0, 20, 30));
        Boxa boxa = pix.SplitComponentWithProfile(10, 7);
        Assert.Equal(1, boxa.Count);
        boxa.GetBoxGeometry(0, out int x, out int y, out int w, out int h);
        Assert.Equal((0, 0, 20, 30), (x, y, w, h));
    }

    [Fact]
    public void SplitIntoCharactersFiltersSmallAndSortsLeftToRight()
    {
        Pix pix = Rects(60, 40, (30, 5, 10, 20), (5, 5, 10, 20), (50, 35, 2, 2));
        pix.SplitIntoCharacters(4, 4, out Boxa boxa, out Pixa pixa);
        Assert.Equal(2, boxa.Count);
        Assert.Equal(2, pixa.Count);
        boxa.GetBoxGeometry(0, out int x, out _, out _, out _);
        Assert.Equal(5, x);
        boxa.GetBoxGeometry(1, out x, out _, out _, out _);
        Assert.Equal(30, x);
    }

    [Fact]
    public void EstimateBackgroundOfUniformImage()
    {
        Pix pix = Pix.Create(50, 40, 8);
        pix.SetAllArbitrary(200);
        /* the rank value is interpolated within the histogram bin: 200.5, rounded up */
        Assert.Equal(201, pix.EstimateBackground(70, 0.0f));
        Assert.Equal(201, pix.EstimateBackground(0, 0.2f));
        Assert.Throws<ArgumentException>(() => pix.EstimateBackground(70, 1.0f));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 1).EstimateBackground(70, 0.0f));
    }

    [Fact]
    public void Prepare1bppCropsThresholdsAndScales()
    {
        Pix pix = Pix.Create(100, 100, 8);
        pix.SetAllArbitrary(255);
        Pix pix1 = pix.Prepare1bpp(null, 0.1f, 0)!;
        /* (int)((1.0 - 2 * 0.1f) * 100) is 79 because 0.1f is slightly larger than 0.1 */
        Assert.Equal((79, 79, 1), (pix1.Width, pix1.Height, pix1.Depth));
        Assert.True(pix1.Zero());

        pix.SetResolution(300, 300);
        Pix pix2 = pix.Prepare1bpp(null, 0.0f, 150)!;
        Assert.Equal((50, 50), (pix2.Width, pix2.Height));  /* 300 ppi to 150 ppi */
        Pix pix3 = pix.Prepare1bpp(Box.Create(10, 10, 40, 30), 0.0f, 300)!;
        Assert.Equal((40, 30), (pix3.Width, pix3.Height));
    }

    [Fact]
    public void BlankPageIsNeitherTextNorTable()
    {
        Pix pix = Pix.Create(400, 400, 1);
        Assert.Equal(-1, pix.DecideIfText(null, null));
        Assert.Equal(0, pix.DecideIfTable(null, L.L_PORTRAIT_MODE, null));
        Assert.Null(pix.ExtractTextlines(150, 150, 0, 0, 5, 5, null));
        Assert.Null(pix.ExtractRawTextlines(0, 0, 5, 5, null));
        Assert.Equal(0, pix.CountTextColumns(0.3f, 0.5f, 0.1f, null));
        Pix inverted = pix.AutoPhotoinvert(0, out Pix? pixm, null);
        Assert.Null(pixm);
        Assert.True(inverted.Zero());
    }

    [Fact]
    public void TextLinesAreExtractedAsOneBoxEach()
    {
        /* 3 text-like lines of 4 words each, at 300 ppi */
        Pix pix = Pix.Create(1000, 600, 1);
        pix.SetResolution(300, 300);
        for (int line = 0; line < 3; line++)
            for (int word = 0; word < 4; word++)
                pix.Rasterop(100 + word * 180, 100 + line * 150, 140, 30, L.PIX_SET, null, 0, 0);

        Pixa pixa = pix.ExtractTextlines(150, 150, 0, 0, 5, 5, null)!;
        Assert.Equal(3, pixa.Count);
        /* a line spans x = 100 ... 779 and is 30 high; the box is expanded by 5 on each side */
        pixa.GetBoxGeometry(0, out int x, out int y, out int w, out int h);
        Assert.Equal((95, 95, 690, 40), (x, y, w, h));

        Pixa pixa2 = pix.ExtractRawTextlines(0, 0, 0, 0, null)!;
        Assert.Equal(3, pixa2.Count);
    }

    [Fact]
    public void FindRectangleInCCOfSolidBlock()
    {
        Pix pix = Rects(20, 10, (0, 0, 20, 10));
        /* the downward scan stops one line before the last, so the box is 9 high (upstream behavior) */
        Box box = pix.FindRectangleInCC(null, 0.5f, L.L_SCAN_HORIZONTAL, L.L_LARGEST_AREA, 0)!;
        box.GetGeometry(out int x, out int y, out int w, out int h);
        Assert.Equal((0, 0, 20, 9), (x, y, w, h));

        box = pix.FindRectangleInCC(null, 0.5f, L.L_SCAN_VERTICAL, L.L_GEOMETRIC_UNION, 0)!;
        box.GetGeometry(out x, out y, out w, out h);
        Assert.Equal((0, 0, 20, 10), (x, y, w, h));

        Assert.Null(Pix.Create(20, 10, 1).FindRectangleInCC(null, 0.5f, L.L_SCAN_HORIZONTAL, L.L_LARGEST_AREA, 0));
        Assert.Throws<ArgumentException>(() => pix.FindRectangleInCC(null, 0.0f, L.L_SCAN_HORIZONTAL, L.L_LARGEST_AREA, 0));
    }

    [Fact]
    public void MasksNeedAtLeastMinimumSize()
    {
        Pix small = Pix.Create(50, 50, 1);
        Assert.Throws<ArgumentException>(() => small.GetRegionsBinary(out _, out _, out _, null));
        Assert.Throws<ArgumentException>(() => small.GenerateHalftoneMask(out bool _, null));
        Assert.Throws<ArgumentException>(() => small.GenTextlineMask(out _, out _, null));
        Assert.Throws<ArgumentException>(() => small.CropImage(0, 0, 0, 0, 0, 1.0f, 0, null, out _));
    }

    [Fact]
    public void HalftoneMaskOfBlankImageIsEmpty()
    {
        Pix pix = Pix.Create(200, 200, 1);
        Pix mask = pix.GenerateHalftoneMask(out Pix text, out bool htfound, null);
        Assert.False(htfound);
        Assert.True(mask.Zero());
        Assert.True(Pix.Equal(text, pix));

        Pix tlmask = pix.GenTextlineMask(out Pix vws, out bool tlfound, null);
        Assert.False(tlfound);
        Assert.True(tlmask.Zero());
        Assert.Null(tlmask.GenTextblockMask(vws, null));
    }

    [Fact]
    public void CleanImageChecksArguments()
    {
        Pix pix = Pix.Create(200, 200, 1);
        Assert.Throws<ArgumentException>(() => pix.CleanImage(1, 4, 1, 0));
        Assert.Throws<ArgumentException>(() => pix.CleanImage(0, 0, 1, 0));
        Assert.Throws<ArgumentException>(() => pix.CleanImage(1, 0, 3, 0));
        Assert.Throws<ArgumentException>(() => pix.CleanImage(1, 0, 1, 4));
    }

    [Fact]
    public void CropImageFitsForegroundIntoPage()
    {
        /* a dark block in the middle of a white 1 bpp page is scaled to fill the page */
        Pix pix = Rects(400, 400, (150, 150, 100, 100));
        Pix result = pix.CropImage(0, 0, 0, 0, 0, 1.0f, 0, null, out Box? cropbox);
        Assert.Equal((400, 400), (result.Width, result.Height));
        Assert.NotNull(cropbox);
        cropbox!.GetGeometry(out int x, out int y, out int w, out int h);
        Assert.Equal((150, 150, 100, 100), (x, y, w, h));
    }

    [Fact]
    public void FindPageForegroundIgnoresBlankPage()
    {
        Pix pix = Pix.Create(300, 300, 1);
        Assert.Null(pix.FindPageForeground(128, 70, 30, 0, null));
        Assert.Throws<ArgumentException>(() => Pix.Create(50, 50, 1).FindPageForeground(128, 70, 30, 0, null));
    }
}
