using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Transform;

public class WarperTests
{
    private static Pix Ramp8(int w, int h)
    {
        Pix pix = Pix.Create(w, h, 8);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                pix.SetPixel(j, i, (uint)((j * 10 + i * 3) & 0xff));
        return pix;
    }

    [Fact]
    public void RandomHarmonicWarpIsDeterministicPerSeed()
    {
        Pix pix = Ramp8(40, 30);
        Pix a = pix.RandomHarmonicWarp(4f, 6f, 0.1f, 0.13f, 3, 3, 7, 255);
        Pix b = pix.RandomHarmonicWarp(4f, 6f, 0.1f, 0.13f, 3, 3, 7, 255);
        Pix c = pix.RandomHarmonicWarp(4f, 6f, 0.1f, 0.13f, 3, 3, 8, 255);
        Assert.True(Pix.Equal(a, b));
        Assert.False(Pix.Equal(a, c));
        Assert.Equal((40, 30, 8), (a.Width, a.Height, a.Depth));
    }

    [Fact]
    public void RandomHarmonicWarpRequires8bpp()
    {
        Assert.Throws<System.ArgumentException>(() =>
            Pix.Create(10, 10, 32).RandomHarmonicWarp(1f, 1f, 0.1f, 0.1f, 1, 1, 1, 255));
    }

    [Fact]
    public void SimpleCaptchaAddsBorderAndValidatesTerms()
    {
        Pix pix = Ramp8(20, 10);
        Pix cap = pix.SimpleCaptcha(5, 2, 3, 0xff000000, 0);
        Assert.Equal((30, 20, 32), (cap.Width, cap.Height, cap.Depth));
        Assert.Throws<System.ArgumentException>(() => pix.SimpleCaptcha(5, 5, 3, 0, 0));
    }

    [Fact]
    public void StretchWithZeroDisplacementIsIdentity()
    {
        Pix pix = Ramp8(25, 8);
        Pix s = pix.StretchHorizontalSampled(L.L_WARP_TO_LEFT, L.L_LINEAR_WARP, 0, L.L_BRING_IN_WHITE);
        Assert.True(Pix.Equal(pix, s));
        Pix li = pix.StretchHorizontal(L.L_WARP_TO_RIGHT, L.L_QUADRATIC_WARP, 0, L.L_INTERPOLATED, L.L_BRING_IN_WHITE);
        Assert.True(Pix.Equal(pix, li));
    }

    [Fact]
    public void StretchMovesPixelsAwayFromFixedEdge()
    {
        Pix pix = Ramp8(21, 4);
        Pix s = pix.StretchHorizontalSampled(L.L_WARP_TO_RIGHT, L.L_LINEAR_WARP, 10, L.L_BRING_IN_WHITE);
        /* the left edge is fixed; the right edge samples from 10 pixels to the left */
        Assert.Equal(pix.GetPixel(0, 0), s.GetPixel(0, 0));
        Assert.Equal(pix.GetPixel(10, 0), s.GetPixel(20, 0));
    }

    [Fact]
    public void Interpolated1bppStretchThrowsLikeUpstream()
    {
        Pix pix = Pix.Create(16, 4, 1);
        Assert.Throws<System.ArgumentException>(() =>
            pix.StretchHorizontal(L.L_WARP_TO_LEFT, L.L_LINEAR_WARP, 3, L.L_INTERPOLATED, L.L_BRING_IN_WHITE));
        Pix s = pix.StretchHorizontal(L.L_WARP_TO_LEFT, L.L_LINEAR_WARP, 3, L.L_SAMPLED, L.L_BRING_IN_WHITE);
        Assert.Equal(1, s.Depth);
    }

    [Fact]
    public void QuadraticVShearZeroCopiesAndEdgeIsUnchanged()
    {
        Pix pix = Ramp8(21, 30);
        Pix z = pix.QuadraticVShear(L.L_WARP_TO_LEFT, 0, 0, L.L_SAMPLED, L.L_BRING_IN_WHITE);
        Assert.True(Pix.Equal(pix, z));
        Pix s = pix.QuadraticVShearSampled(L.L_WARP_TO_LEFT, 6, 6, L.L_BRING_IN_WHITE);
        /* L_WARP_TO_LEFT: the right edge column is unchanged */
        for (int i = 0; i < 30; i++)
            Assert.Equal(pix.GetPixel(20, i), s.GetPixel(20, i));
        Pix li = pix.QuadraticVShear(L.L_WARP_TO_RIGHT, 6, -6, L.L_INTERPOLATED, L.L_BRING_IN_WHITE);
        Assert.Equal(8, li.Depth);
        for (int i = 0; i < 30; i++)
            Assert.Equal(pix.GetPixel(0, i), li.GetPixel(0, i));
    }

    [Fact]
    public void StereoFromPairTakesGreenBlueFromSecond()
    {
        Pix p1 = Pix.Create(2, 1, 32);
        Pix p2 = Pix.Create(2, 1, 32);
        p1.SetRGBPixel(0, 0, 100, 200, 50);
        p2.SetRGBPixel(0, 0, 1, 2, 3);
        Pix d = Pix.StereoFromPair(p1, p2, 0f, 1f, 0f);
        Pix.ExtractRGBValues(d.GetPixel(0, 0), out int r, out int g, out int b);
        Assert.Equal((200, 2, 3), (r, g, b));
        /* default weights (0, 0.7, 0.3) */
        d = Pix.StereoFromPair(p1, p2, 0f, 0f, 0f);
        Pix.ExtractRGBValues(d.GetPixel(0, 0), out r, out _, out _);
        Assert.Equal((int)(0.7f * 200 + 0.3f * 50 + 0.5), r);
    }

    [Fact]
    public void WarpStereoscopicKeepsSizeAnd32bpp()
    {
        Pix pix = Ramp8(40, 20);
        Pix d = pix.WarpStereoscopic(6, 4, -4, 3, 0, 1);
        Assert.Equal((40, 20, 32), (d.Width, d.Height, d.Depth));
    }
}
