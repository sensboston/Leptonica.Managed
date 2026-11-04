using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Watershed;

public class WatershedTests
{
    /* Two V-shaped valleys with minima at x = 10 and x = 30, separated by a ridge at x = 20. */
    private static Pix TwoBasins()
    {
        Pix pix = Pix.Create(41, 15, 8);
        for (int i = 0; i < 15; i++)
            for (int j = 0; j < 41; j++) {
                int v = j <= 20 ? 10 + 9 * System.Math.Abs(j - 10) : 10 + 9 * System.Math.Abs(j - 30);
                pix.SetPixel(j, i, (uint)System.Math.Min(v, 255));
            }
        return pix;
    }

    private static Pix Seeds()
    {
        Pix pixm = Pix.Create(41, 15, 1);
        pixm.SetPixel(10, 7, 1);
        pixm.SetPixel(30, 7, 1);
        return pixm;
    }

    [Fact]
    public void CreateValidatesArguments()
    {
        Pix pixs = TwoBasins();
        Assert.Throws<System.ArgumentException>(() => L_WShed.Create(Pix.Create(41, 15, 32), Seeds(), 5, 0));
        Assert.Throws<System.ArgumentException>(() => L_WShed.Create(pixs, Pix.Create(41, 15, 8), 5, 0));
        Assert.Throws<System.ArgumentException>(() => L_WShed.Create(pixs, Pix.Create(40, 15, 1), 5, 0));
        L_WShed w = L_WShed.Create(pixs, Seeds(), -3, 0);
        Assert.Equal(1, w.MinDepth);
    }

    [Fact]
    public void TwoSeededBasinsAreFound()
    {
        Pix pixs = TwoBasins();
        L_WShed wshed = L_WShed.Create(pixs, Seeds(), 10, 0);
        wshed.Apply();
        Assert.Equal(2, wshed.Nseeds);
        wshed.Basins(out Pixa pixa, out Numa nalevels);
        /* wshedApply() is documented as buggy: it can save an additional, duplicate watershed */
        Assert.True(pixa.Count >= 2);
        Assert.Equal(pixa.Count, nalevels.Count);
        bool left = false, right = false;
        for (int i = 0; i < pixa.Count; i++) {
            pixa.GetBoxGeometry(i, out int bx, out _, out int bw, out _);
            Pix basin = pixa.GetPix(i, L.L_CLONE);
            Assert.Equal(1, basin.Depth);
            Assert.Equal(bw, basin.Width);
            if (bx <= 10 && bx + bw > 10) left = true;
            if (bx <= 30 && bx + bw > 30) right = true;
        }
        Assert.True(left && right);
    }

    [Fact]
    public void RenderedOutputsHaveInputSize()
    {
        Pix pixs = TwoBasins();
        L_WShed wshed = L_WShed.Create(pixs, Seeds(), 10, 0);
        wshed.Apply();
        Pix fill = wshed.RenderFill();
        Assert.Equal((41, 15, 8), (fill.Width, fill.Height, fill.Depth));
        Pix colors = wshed.RenderColors();
        Assert.Equal((41, 15, 32), (colors.Width, colors.Height, colors.Depth));
    }

    [Fact]
    public void ShallowSeedsAreNotSaved()
    {
        Pix pixs = TwoBasins();
        L_WShed wshed = L_WShed.Create(pixs, Seeds(), 200, 0);
        wshed.Apply();
        wshed.Basins(out Pixa pixa, out _);
        Assert.Equal(0, pixa.Count);
    }
}
