using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Transform;

public class AffineComposeTests
{
    [Fact]
    public void MatrixCreators()
    {
        Assert.Equal(new float[] {1, 0, 5, 0, 1, -3, 0, 0, 1}, Pix.CreateMatrix2dTranslate(5, -3));
        Assert.Equal(new float[] {2, 0, 0, 0, 3, 0, 0, 0, 1}, Pix.CreateMatrix2dScale(2, 3));
        float[] r = Pix.CreateMatrix2dRotate(10, 20, 0);
        Assert.Equal(new float[] {1, 0, 0, 0, 1, 0, 0, 0, 1}, r);
    }

    [Fact]
    public void RotateAboutCenterKeepsCenter()
    {
        float[] r = Pix.CreateMatrix2dRotate(10, 20, 0.5f);
        float[] vs = {10, 20, 1};
        float[] vd = new float[3];
        Pix.ProductMatVec(r, vs, vd, 3);
        Assert.InRange(vd[0], 9.999f, 10.001f);
        Assert.InRange(vd[1], 19.999f, 20.001f);
    }

    [Fact]
    public void ProductsAreAssociative()
    {
        float[] a = Pix.CreateMatrix2dTranslate(3, 4);
        float[] b = Pix.CreateMatrix2dScale(2, 5);
        float[] c = Pix.CreateMatrix2dRotate(1, 2, 0.3f);
        float[] ab = new float[9], abc1 = new float[9], abc2 = new float[9], t = new float[9];
        Pix.ProductMat2(a, b, ab, 3);
        Pix.ProductMat2(ab, c, abc1, 3);
        Pix.ProductMat3(a, b, c, abc2, 3);
        Assert.Equal(abc1, abc2);
        float[] d4 = new float[9];
        Pix.ProductMat2(c, a, t, 3);
        Pix.ProductMat4(a, b, c, a, d4, 3);
        float[] expect = new float[9];
        Pix.ProductMat2(abc1, a, expect, 3);
        Assert.Equal(expect, d4);
    }

    [Fact]
    public void PtaAndBoxaTransforms()
    {
        Pta pta = Pta.Create(2);
        pta.AddPt(1, 2);
        pta.AddPt(3, 4);
        Pta t = pta.Translate(10, 20);
        t.GetPt(1, out float x, out float y);
        Assert.Equal((13f, 24f), (x, y));
        Pta s = pta.Scale(2, 3);
        s.GetPt(1, out x, out y);
        Assert.Equal((6f, 12f), (x, y));
        Pta r = pta.Rotate(1, 2, 0);
        r.GetPt(1, out x, out y);
        Assert.Equal((3f, 4f), (x, y));
        Pta a = pta.AffineTransform(Pix.CreateMatrix2dTranslate(1, 1));
        a.GetPt(0, out x, out y);
        Assert.Equal((2f, 3f), (x, y));

        Boxa boxa = Boxa.Create(1);
        boxa.AddBox(Box.Create(10, 20, 30, 40), L.L_INSERT);
        Boxa bt = boxa.Translate(5, -5);
        bt.GetBoxGeometry(0, out int bx, out int by, out int bw, out int bh);
        Assert.Equal((15, 15, 30, 40), (bx, by, bw, bh));
        Boxa bs = boxa.Scale(2, 2);
        bs.GetBoxGeometry(0, out bx, out by, out bw, out bh);
        /* corners (x+w-1, y+h-1) are scaled, so the far edge is 2 * 39 = 78 */
        Assert.Equal((20, 40, 59, 79), (bx, by, bw, bh));
    }
}
