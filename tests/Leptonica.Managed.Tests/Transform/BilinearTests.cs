using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Transform;

public class BilinearTests
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
        float[] vc = Pix.GetBilinearXformCoeffs(p, p);
        float[] expect = {1, 0, 0, 0, 0, 1, 0, 0};
        for (int i = 0; i < 8; i++)
            Assert.InRange(vc[i], expect[i] - 1e-4f, expect[i] + 1e-4f);
        Pix.BilinearXformSampledPt(vc, 3, 7, out int x, out int y);
        Assert.Equal((3, 7), (x, y));
        Pix.BilinearXformPt(vc, 3, 7, out float fx, out float fy);
        Assert.InRange(fx, 2.999f, 3.001f);
        Assert.InRange(fy, 6.999f, 7.001f);
    }

    [Fact]
    public void TranslationMapsCorners()
    {
        Pta s = Quad(0, 0, 10, 0, 0, 10, 10, 10);
        Pta d = Quad(5, 3, 15, 3, 5, 13, 15, 13);
        float[] vc = Pix.GetBilinearXformCoeffs(s, d);
        Pix.BilinearXformPt(vc, 10, 10, out float x, out float y);
        Assert.InRange(x, 14.999f, 15.001f);
        Assert.InRange(y, 12.999f, 13.001f);
    }

    [Fact]
    public void IdentityTransformKeepsImage()
    {
        Pix pix = Pix.Create(20, 20, 8);
        for (int i = 0; i < 20; i++)
            for (int j = 0; j < 20; j++)
                pix.SetPixel(j, i, (uint)((i * 13 + j * 7) & 0xff));
        Pta p = Quad(0, 0, 19, 0, 0, 19, 19, 19);
        Pix s = pix.BilinearSampledPta(p, p, L.L_BRING_IN_WHITE);
        Assert.True(Pix.Equal(pix, s));
        Pix g = pix.BilinearPta(p, p, L.L_BRING_IN_WHITE);
        Assert.Equal(8, g.Depth);
        Assert.Equal(pix.GetPixel(5, 5), g.GetPixel(5, 5));
    }

    [Fact]
    public void ShiftBringsInColor()
    {
        Pix pix = Pix.Create(16, 16, 1);
        pix.SetAll();
        Pta s = Quad(0, 0, 15, 0, 0, 15, 15, 15);
        Pta d = Quad(4, 0, 19, 0, 4, 15, 19, 15);
        Pix white = pix.BilinearSampledPta(d, s, L.L_BRING_IN_WHITE);
        Pix black = pix.BilinearSampledPta(d, s, L.L_BRING_IN_BLACK);
        Assert.Equal(0u, white.GetPixel(0, 5));
        Assert.Equal(1u, black.GetPixel(0, 5));
    }

    [Fact]
    public void InvalidPointCountThrows()
    {
        Pix pix = Pix.Create(8, 8, 8);
        Pta three = Pta.Create(3);
        three.AddPt(0, 0); three.AddPt(1, 0); three.AddPt(0, 1);
        Assert.Throws<System.ArgumentException>(() => pix.BilinearPta(three, three, L.L_BRING_IN_WHITE));
    }
}
