using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.ColorSpace;

public class ColorSpaceTests
{
    [Theory]
    [InlineData(255, 0, 0, 0, 255, 255)]
    [InlineData(255, 255, 0, 40, 255, 255)]
    [InlineData(0, 255, 0, 80, 255, 255)]
    [InlineData(0, 255, 255, 120, 255, 255)]
    [InlineData(0, 0, 255, 160, 255, 255)]
    [InlineData(255, 0, 255, 200, 255, 255)]
    [InlineData(100, 100, 100, 0, 0, 100)]
    public void RgbToHsvKnownValues(int r, int g, int b, int h, int s, int v)
    {
        Pix.ConvertRGBToHSV(r, g, b, out int hh, out int ss, out int vv);
        Assert.Equal((h, s, v), (hh, ss, vv));
        if (s > 0) {
            Pix.ConvertHSVToRGB(hh, ss, vv, out int rr, out int gg, out int bb);
            Assert.Equal((r, g, b), (rr, gg, bb));
        }
    }

    [Fact]
    public void HsvToRgbInvalidHueThrows()
    {
        Assert.Throws<System.ArgumentException>(() => Pix.ConvertHSVToRGB(241, 100, 100, out _, out _, out _));
    }

    [Fact]
    public void YuvOfWhiteAndBlack()
    {
        Pix.ConvertRGBToYUV(255, 255, 255, out int y, out int u, out int v);
        Assert.Equal((235, 128, 128), (y, u, v));
        Pix.ConvertRGBToYUV(0, 0, 0, out y, out u, out v);
        Assert.Equal((16, 128, 128), (y, u, v));
        Pix.ConvertYUVToRGB(235, 128, 128, out int r, out int g, out int b);
        Assert.Equal((255, 255, 255), (r, g, b));
    }

    [Fact]
    public void XyzOfWhiteIsTheWhitePoint()
    {
        Pix.ConvertRGBToXYZ(255, 255, 255, out float x, out float y, out float z);
        Assert.InRange(x, 242.3f, 242.4f);
        Assert.InRange(y, 254.9f, 255.1f);
        Assert.InRange(z, 277.6f, 277.8f);
        Pix.ConvertXYZToRGB(x, y, z, 0, out int r, out int g, out int b);
        Assert.Equal((255, 255, 255), (r, g, b));
    }

    [Fact]
    public void LabRoundTripAndWhiteLightness()
    {
        Pix.ConvertRGBToLAB(255, 255, 255, out float l, out float a, out float bb);
        Assert.InRange(l, 99f, 101f);
        Assert.InRange(a, -2f, 2f);
        Assert.InRange(bb, -2f, 2f);
        Pix.ConvertRGBToLAB(30, 120, 200, out l, out a, out bb);
        Pix.ConvertLABToRGB(l, a, bb, out int r, out int g, out int b);
        Assert.InRange(r, 29, 31);
        Assert.InRange(g, 119, 121);
        Assert.InRange(b, 199, 201);
    }

    [Fact]
    public void ImageHsvRoundTripAndChannels()
    {
        Pix pix = Pix.Create(3, 1, 32);
        pix.SetRGBPixel(0, 0, 255, 0, 0);
        pix.SetRGBPixel(1, 0, 0, 255, 0);
        pix.SetRGBPixel(2, 0, 10, 20, 30);
        Pix hue = pix.ConvertRGBToHue();
        Pix sat = pix.ConvertRGBToSaturation();
        Pix val = pix.ConvertRGBToValue();
        Assert.Equal(0u, hue.GetPixel(0, 0));
        Assert.Equal(80u, hue.GetPixel(1, 0));
        Assert.Equal(255u, sat.GetPixel(0, 0));
        Assert.Equal(30u, val.GetPixel(2, 0));
        Pix hsv = Pix.ConvertRGBToHSV(null, pix);
        Pix back = Pix.ConvertHSVToRGB(null, hsv);
        for (int i = 0; i < 3; i++)
            Assert.Equal(pix.GetPixel(i, 0), back.GetPixel(i, 0));
    }

    [Fact]
    public void RangeMaskHsSelectsRedAndExcludeInverts()
    {
        Pix pix = Pix.Create(2, 1, 32);
        pix.SetRGBPixel(0, 0, 255, 0, 0);
        pix.SetRGBPixel(1, 0, 0, 0, 255);
        Pix m1 = pix.MakeRangeMaskHS(0, 10, 255, 5, L.L_INCLUDE_REGION);
        Assert.Equal(1u, m1.GetPixel(0, 0));
        Assert.Equal(0u, m1.GetPixel(1, 0));
        Pix m2 = pix.MakeRangeMaskHS(0, 10, 255, 5, L.L_EXCLUDE_REGION);
        Assert.Equal(0u, m2.GetPixel(0, 0));
        Assert.Equal(1u, m2.GetPixel(1, 0));
    }

    [Fact]
    public void HistoHsCountsPixels()
    {
        Pix pix = Pix.Create(4, 4, 32);
        pix.SetAllArbitrary((100u << 24) | (50u << 16) | (200u << 8));
        Pix h = pix.MakeHistoHS(1, out Numa nahue, out Numa nasat);
        Assert.Equal(16u, h.GetPixel(50, 100));
        Assert.Equal(16f, nahue.GetFValue(100));
        Assert.Equal(16f, nasat.GetFValue(50));
    }

    [Fact]
    public void GamutHasExpectedSize()
    {
        Pix pix = Pix.MakeGamutRGB(2);
        Assert.Equal(32, pix.Depth);
        Assert.True(pix.Width > 0 && pix.Height > 0);
    }
}
