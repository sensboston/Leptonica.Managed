using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests;

/// <summary>Tests for baseline.c on synthetic text-line images.</summary>
public class BaselineTests
{
    /// <summary>Horizontal bars of the given height, one every 'step' lines, standing for text lines.</summary>
    private static Pix TextLines(int w, int h, int y0, int step, int barh, int barw)
    {
        Pix pix = Pix.Create(w, h, 1);
        for (int y = y0; y + barh <= h; y += step)
            pix.Rasterop(50, y, barw, barh, L.PIX_SET, null, 0, 0);
        return pix;
    }

    [Fact]
    public void BaselinesAreTheBottomRowsOfTheTextLines()
    {
        Pix pix = TextLines(800, 700, 100, 200, 30, 600);   /* bars at y = 100, 300, 500 */
        Numa? na = pix.FindBaselines(out Pta? pta, null);
        Assert.NotNull(na);
        Assert.Equal(3, na!.Count);
        Assert.Equal(129, na.GetIValue(0));
        Assert.Equal(329, na.GetIValue(1));
        Assert.Equal(529, na.GetIValue(2));

        /* a pair of points (left and right end) for each baseline, on the baseline */
        Assert.Equal(6, pta!.Count);
        pta.GetIPt(0, out int x1, out int y1);
        pta.GetIPt(1, out int x2, out int y2);
        Assert.Equal(129, y1);
        Assert.Equal(129, y2);
        Assert.True(x2 - x1 > 500);
    }

    [Fact]
    public void ShortTextBlocksAreRemovedByMinWidth()
    {
        Pix pix = TextLines(800, 400, 100, 200, 30, 600);   /* bars at y = 100 and 300 */
        pix.Rasterop(100, 200, 40, 30, L.PIX_SET, null, 0, 0);   /* a short block between them */
        Numa? na = pix.FindBaselinesGen(80, out Pta? pta, null);
        Assert.Equal(2, na!.Count);
        Assert.Equal(129, na.GetIValue(0));
        Assert.Equal(329, na.GetIValue(1));
        Assert.Equal(4, pta!.Count);

        na = pix.FindBaselinesGen(6, out _, null);
        Assert.Equal(3, na!.Count);
        Assert.Equal(229, na.GetIValue(1));
    }

    [Fact]
    public void NoBaselinesInBlankImage()
    {
        Pix pix = Pix.Create(500, 500, 1);
        Assert.Null(pix.FindBaselines(out Pta? pta, null));
        Assert.Null(pta);
    }

    [Fact]
    public void LocalSkewOfHorizontalLinesIsZero()
    {
        Pix pix = TextLines(1000, 1000, 20, 50, 25, 900);
        Numa naskew = pix.GetLocalSkewAngles(0, 0, 0, 0.0f, 0.0f, 0.0f, out float a, out float b, 0);
        Assert.Equal(1000, naskew.Count);
        Assert.True(Math.Abs(a) < 0.001f);
        Assert.True(Math.Abs(b) < 0.5f);

        pix.GetLocalSkewTransform(0, 0, 0, 0.0f, 0.0f, 0.0f, out Pta ptas, out Pta ptad);
        Assert.Equal(4, ptas.Count);
        Assert.Equal(4, ptad.Count);
        /* the dest points of each pair are on the same raster line, at the left and right edge */
        ptad.GetIPt(0, out int x0, out int y0);
        ptad.GetIPt(1, out int x1, out int y1);
        Assert.Equal((0, 999), (x0, x1));
        Assert.Equal(y0, y1);
        ptad.GetIPt(2, out x0, out y0);
        ptad.GetIPt(3, out x1, out y1);
        Assert.Equal(y0, y1);
        Assert.True(y0 > 900);
    }

    [Fact]
    public void ArgumentChecks()
    {
        Pix gray = Pix.Create(100, 100, 8);
        Assert.Throws<ArgumentException>(() => gray.FindBaselines(out _, null));
        Assert.Throws<ArgumentException>(() => gray.DeskewLocal(0, 0, 0, 0.0f, 0.0f, 0.0f));
        Assert.Throws<ArgumentException>(() => gray.GetLocalSkewAngles(0, 0, 0, 0.0f, 0.0f, 0.0f, out _, out _, 0));
        Assert.Throws<ArgumentException>(() => gray.GetLocalSkewTransform(0, 0, 0, 0.0f, 0.0f, 0.0f, out _, out _));
        /* no text lines: the skew can't be fitted */
        Pix blank = Pix.Create(200, 200, 1);
        Assert.Throws<LeptonicaException>(() => blank.GetLocalSkewAngles(0, 0, 0, 0.0f, 0.0f, 0.0f, out _, out _, 0));
    }
}
