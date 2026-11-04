using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Transform;

public class ProjectiveTests
{
    private static Pta Quad(float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4)
    {
        Pta pta = Pta.Create(4);
        pta.AddPt(x1, y1);
        pta.AddPt(x2, y2);
        pta.AddPt(x3, y3);
        pta.AddPt(x4, y4);
        return pta;
    }

    [Fact]
    public void IdentityCoefficients()
    {
        Pta p = Quad(0, 0, 10, 0, 0, 10, 10, 10);
        float[] vc = Pix.GetProjectiveXformCoeffs(p, p);
        float[] expect = {1, 0, 0, 0, 1, 0, 0, 0};
        for (int i = 0; i < 8; i++)
            Assert.InRange(vc[i], expect[i] - 1e-4f, expect[i] + 1e-4f);
        Pix.ProjectiveXformSampledPt(vc, 4, 6, out int x, out int y);
        Assert.Equal((4, 6), (x, y));
        Pix.ProjectiveXformPt(vc, 4, 6, out float fx, out float fy);
        Assert.InRange(fx, 3.999f, 4.001f);
        Assert.InRange(fy, 5.999f, 6.001f);
    }

    [Fact]
    public void KeystoneMapsCorners()
    {
        Pta s = Quad(0, 0, 100, 0, 0, 100, 100, 100);
        Pta d = Quad(10, 0, 90, 0, 0, 100, 100, 100);
        float[] vc = Pix.GetProjectiveXformCoeffs(s, d);
        Pix.ProjectiveXformPt(vc, 100, 0, out float x, out float y);
        Assert.InRange(x, 89.99f, 90.01f);
        Assert.InRange(y, -0.01f, 0.01f);
        Pix.ProjectiveXformPt(vc, 0, 100, out x, out y);
        Assert.InRange(x, -0.01f, 0.01f);
        Assert.InRange(y, 99.99f, 100.01f);
    }

    [Fact]
    public void IdentityTransformKeepsImage()
    {
        Pix pix = Pix.Create(20, 20, 32);
        for (int i = 0; i < 20; i++)
            for (int j = 0; j < 20; j++)
                pix.SetRGBPixel(j, i, i * 10, j * 10, (i + j) * 5);
        Pta p = Quad(0, 0, 19, 0, 0, 19, 19, 19);
        Pix s = pix.ProjectiveSampledPta(p, p, L.L_BRING_IN_WHITE);
        Assert.True(Pix.Equal(pix, s));
        Pix c = pix.ProjectivePta(p, p, L.L_BRING_IN_WHITE);
        Assert.Equal(pix.GetPixel(7, 9), c.GetPixel(7, 9));
    }

    [Fact]
    public void WithAlphaAddsFourthSample()
    {
        Pix pix = Pix.Create(30, 30, 32);
        pix.SetAllArbitrary(0x40608000);
        Pta p = Quad(0, 0, 29, 0, 0, 29, 29, 29);
        Pix pixd = pix.ProjectivePtaWithAlpha(p, p, null, 1.0f, 4);
        Assert.Equal(4, pixd.Spp);
        Assert.Equal(38, pixd.Width);
    }

    [Fact]
    public void DepthAndCountChecks()
    {
        Pix pix = Pix.Create(8, 8, 8);
        Pta three = Pta.Create(3);
        three.AddPt(0, 0); three.AddPt(1, 0); three.AddPt(0, 1);
        Assert.Throws<System.ArgumentException>(() => pix.ProjectivePta(three, three, L.L_BRING_IN_WHITE));
        Pta p = Quad(0, 0, 7, 0, 0, 7, 7, 7);
        Assert.Throws<System.ArgumentException>(() => pix.ProjectivePtaColor(p, p, 0));
    }
}
