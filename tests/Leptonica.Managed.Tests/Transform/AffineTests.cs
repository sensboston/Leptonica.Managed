using System;
using Leptonica;

namespace Leptonica.Managed.Tests.TransformTests;

public class AffineTests
{
    private static Pta MakePta(float x1, float y1, float x2, float y2, float x3, float y3)
    {
        Pta pta = Pta.Create(3);
        pta.AddPt(x1, y1);
        pta.AddPt(x2, y2);
        pta.AddPt(x3, y3);
        return pta;
    }

    private static Pix MakePix(int w, int h, int d)
    {
        Pix pix = Pix.Create(w, h, d);
        uint mask = d == 32 ? 0xffffffffu : (1u << d) - 1;
        for (int y = 0; y < h; y++) {
            for (int x = 0; x < w; x++) {
                uint v = d == 32 ? (uint)(x * 7 + y * 13) * 0x01010101u << 8
                                 : (uint)(x * 7 + y * 13 + x * y) & mask;
                pix.SetPixel(x, y, v);
            }
        }
        return pix;
    }

    /* The interpolating functions read row 0 instead of the last row of the
     * source (see NOTE(upstream) in LinearInterpolatePixelGray()), so the
     * last row is skipped when comparing their results. */
    private static void AssertSamePixels(Pix expected, Pix actual, bool skipLastRow = false)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Depth, actual.Depth);
        for (int y = 0; y < expected.Height - (skipLastRow ? 1 : 0); y++) {
            for (int x = 0; x < expected.Width; x++) {
                expected.GetPixel(x, y, out uint e);
                actual.GetPixel(x, y, out uint a);
                Assert.True(e == a, $"pixel ({x},{y}): expected {e}, actual {a}");
            }
        }
    }

    private static void AssertCoeffs(float[] expected, float[] actual, float tol = 1e-4f)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
            Assert.True(MathF.Abs(expected[i] - actual[i]) <= tol,
                        $"coeff {i}: expected {expected[i]}, actual {actual[i]}");
    }

    /*--------------------------- gaussjordan ---------------------------*/

    [Fact]
    public void GaussJordan_SolvesKnownSystem()
    {
        /* 2x + y = 5; x + 3y = 10  ->  x = 1, y = 3 */
        float[][] a = { new[] { 2f, 1f }, new[] { 1f, 3f } };
        float[] b = { 5f, 10f };
        Assert.True(Pix.GaussJordan(a, b, 2));
        Assert.Equal(1f, b[0], 1e-5f);
        Assert.Equal(3f, b[1], 1e-5f);
    }

    [Fact]
    public void GaussJordan_ReplacesMatrixByInverse()
    {
        float[][] a = { new[] { 2f, 1f }, new[] { 1f, 3f } };
        float[] b = { 0f, 0f };
        Assert.True(Pix.GaussJordan(a, b, 2));
        /* inverse of [[2,1],[1,3]] is 1/5 * [[3,-1],[-1,2]] */
        Assert.Equal(0.6f, a[0][0], 1e-5f);
        Assert.Equal(-0.2f, a[0][1], 1e-5f);
        Assert.Equal(-0.2f, a[1][0], 1e-5f);
        Assert.Equal(0.4f, a[1][1], 1e-5f);
    }

    [Fact]
    public void GaussJordan_Solves3x3WithPivoting()
    {
        /* x + 2y + 3z = 14; 4y + z = 9; 2x + z = 5 ... needs a row swap
         * because a[0][0] is not the largest; solution is (1, 2, 3) */
        float[][] a = {
            new[] { 1f, 2f, 3f },
            new[] { 0f, 4f, 1f },
            new[] { 2f, 0f, 1f },
        };
        float[] b = { 14f, 11f, 5f };
        Assert.True(Pix.GaussJordan(a, b, 3));
        Assert.Equal(1f, b[0], 1e-4f);
        Assert.Equal(2f, b[1], 1e-4f);
        Assert.Equal(3f, b[2], 1e-4f);
    }

    [Fact]
    public void GaussJordan_SingularMatrixFails()
    {
        float[][] a = { new[] { 1f, 2f }, new[] { 2f, 4f } };
        float[] b = { 1f, 2f };
        Assert.False(Pix.GaussJordan(a, b, 2));

        float[][] z = { new[] { 0f, 0f }, new[] { 0f, 0f } };
        Assert.False(Pix.GaussJordan(z, new[] { 1f, 1f }, 2));
    }

    /*------------------------ coefficients and points ------------------------*/

    [Fact]
    public void GetAffineXformCoeffs_Identity()
    {
        Pta pta = MakePta(0, 0, 100, 0, 0, 100);
        float[] vc = Pix.GetAffineXformCoeffs(pta, pta);
        AssertCoeffs(new[] { 1f, 0f, 0f, 0f, 1f, 0f }, vc);
    }

    [Fact]
    public void GetAffineXformCoeffs_Translation()
    {
        Pta ptas = MakePta(0, 0, 100, 0, 0, 100);
        Pta ptad = MakePta(5, 7, 105, 7, 5, 107);
        float[] vc = Pix.GetAffineXformCoeffs(ptas, ptad);
        AssertCoeffs(new[] { 1f, 0f, 5f, 0f, 1f, 7f }, vc);
    }

    [Fact]
    public void GetAffineXformCoeffs_ScaleAndShear()
    {
        /* x' = 2x + 0.5y + 3; y' = 0.25x + 3y - 1 */
        Pta ptas = MakePta(0, 0, 40, 0, 0, 20);
        Pta ptad = MakePta(3, -1, 83, 9, 13, 59);
        float[] vc = Pix.GetAffineXformCoeffs(ptas, ptad);
        AssertCoeffs(new[] { 2f, 0.5f, 3f, 0.25f, 3f, -1f }, vc);
    }

    [Fact]
    public void GetAffineXformCoeffs_MapsSourcePointsToDest()
    {
        Pta ptas = MakePta(10, 20, 200, 30, 40, 180);
        Pta ptad = MakePta(15, 25, 190, 60, 30, 170);
        float[] vc = Pix.GetAffineXformCoeffs(ptas, ptad);
        for (int i = 0; i < 3; i++) {
            ptas.GetIPt(i, out int x, out int y);
            ptad.GetPt(i, out float xd, out float yd);
            Pix.AffineXformPt(vc, x, y, out float xp, out float yp);
            Assert.Equal(xd, xp, 1e-2f);
            Assert.Equal(yd, yp, 1e-2f);
        }
    }

    [Fact]
    public void AffineInvertXform_RoundTrip()
    {
        float[] vc = { 2f, 0.5f, 3f, 0.25f, 3f, -1f };
        float[] vci = Pix.AffineInvertXform(vc);
        float[] back = Pix.AffineInvertXform(vci);
        AssertCoeffs(vc, back);

        Pix.AffineXformPt(vc, 17, 29, out float xp, out float yp);
        /* apply the inverse to the (non integer) transformed point */
        float x = vci[0] * xp + vci[1] * yp + vci[2];
        float y = vci[3] * xp + vci[4] * yp + vci[5];
        Assert.Equal(17f, x, 1e-3f);
        Assert.Equal(29f, y, 1e-3f);
    }

    [Fact]
    public void AffineInvertXform_Translation()
    {
        float[] vci = Pix.AffineInvertXform(new[] { 1f, 0f, 5f, 0f, 1f, 7f });
        AssertCoeffs(new[] { 1f, 0f, -5f, 0f, 1f, -7f }, vci);
    }

    [Fact]
    public void AffineXformPt_AndSampledPt()
    {
        float[] vc = { 1f, 0f, 2.4f, 0f, 1f, 2.6f };
        Pix.AffineXformPt(vc, 10, 20, out float xp, out float yp);
        Assert.Equal(12.4f, xp);
        Assert.Equal(22.6f, yp);
        Pix.AffineXformSampledPt(vc, 10, 20, out int xs, out int ys);
        Assert.Equal((12, 23), (xs, ys));
    }

    /*------------------------------ images ------------------------------*/

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(32)]
    public void AffineSampled_Identity(int depth)
    {
        Pix pix = MakePix(37, 23, depth);
        float[] vc = { 1f, 0f, 0f, 0f, 1f, 0f };
        AssertSamePixels(pix, pix.AffineSampled(vc, L.L_BRING_IN_WHITE));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(32)]
    public void Affine_Identity(int depth)
    {
        Pix pix = MakePix(37, 23, depth);
        float[] vc = { 1f, 0f, 0f, 0f, 1f, 0f };
        AssertSamePixels(pix, pix.Affine(vc, L.L_BRING_IN_BLACK), true);
    }

    [Fact]
    public void AffineSampledPta_Identity()
    {
        Pix pix = MakePix(40, 30, 8);
        Pta pta = MakePta(0, 0, 30, 0, 0, 20);
        AssertSamePixels(pix, pix.AffineSampledPta(pta, pta, L.L_BRING_IN_WHITE));
        AssertSamePixels(pix, pix.AffinePta(pta, pta, L.L_BRING_IN_WHITE), true);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(32)]
    public void AffineSampledPta_Translation(int depth)
    {
        const int tx = 5, ty = 3;
        Pix pix = MakePix(37, 23, depth);
        Pta ptas = MakePta(0, 0, 30, 0, 0, 20);
        Pta ptad = MakePta(tx, ty, 30 + tx, ty, tx, 20 + ty);
        Pix pixd = pix.AffineSampledPta(ptad, ptas, L.L_BRING_IN_BLACK);

        uint black = depth == 1 ? 1u : 0u;
        for (int y = 0; y < pix.Height; y++) {
            for (int x = 0; x < pix.Width; x++) {
                pixd.GetPixel(x, y, out uint v);
                /* sampling truncates toward zero, so source coordinate -1
                 * (= -0.5 after the rounding offset) still maps to pixel 0 */
                int sx = x - tx, sy = y - ty;
                if (sx < -1 || sy < -1) {
                    Assert.Equal(black, v);
                } else {
                    pix.GetPixel(Math.Max(sx, 0), Math.Max(sy, 0), out uint e);
                    Assert.Equal(e, v);
                }
            }
        }
    }

    [Fact]
    public void AffineSampledPta_BringInWhite()
    {
        Pix pix = MakePix(20, 20, 8);
        Pta ptas = MakePta(0, 0, 10, 0, 0, 10);
        Pta ptad = MakePta(4, 4, 14, 4, 4, 14);
        Pix pixd = pix.AffineSampledPta(ptad, ptas, L.L_BRING_IN_WHITE);
        pixd.GetPixel(0, 0, out uint v);
        Assert.Equal(255u, v);
        pixd.GetPixel(2, 10, out v);
        Assert.Equal(255u, v);
        pixd.GetPixel(4, 4, out uint e);
        pix.GetPixel(0, 0, out uint s);
        Assert.Equal(s, e);
    }

    [Fact]
    public void AffinePta_IntegerTranslation_Gray()
    {
        Pix pix = MakePix(37, 23, 8);
        /* exact dest -> src coefficients; those from the points are rounded in float */
        float[] vc = { 1f, 0f, -6f, 0f, 1f, -2f };
        Pix interp = pix.Affine(vc, L.L_BRING_IN_WHITE);
        for (int y = 0; y < pix.Height; y++) {
            for (int x = 0; x < pix.Width; x++) {
                interp.GetPixel(x, y, out uint a);
                if (x < 6 || y < 2) {
                    Assert.Equal(255u, a);
                } else {
                    pix.GetPixel(x - 6, y - 2, out uint e);
                    Assert.Equal(e, a);
                }
            }
        }
    }

    [Fact]
    public void AffinePta_IntegerTranslation_Color()
    {
        Pix pix = MakePix(37, 23, 32);
        float[] vc = { 1f, 0f, -2f, 0f, 1f, -6f };
        Pix interp = pix.Affine(vc, L.L_BRING_IN_BLACK);
        for (int y = 0; y < pix.Height; y++) {
            for (int x = 0; x < pix.Width; x++) {
                interp.GetPixel(x, y, out uint a);
                if (x < 2 || y < 6) {
                    Assert.Equal(0u, a);
                } else {
                    pix.GetPixel(x - 2, y - 6, out uint e);
                    /* interpolation does not carry the low (alpha) byte */
                    Assert.Equal(e & 0xffffff00u, a & 0xffffff00u);
                }
            }
        }
    }

    [Fact]
    public void Affine_HalfPixelShiftAverages()
    {
        /* x' = x + 0.5: dest pixel j takes the average of src j and j+1 */
        Pix pix = Pix.Create(8, 2, 8);
        for (int x = 0; x < 8; x++) {
            pix.SetPixel(x, 0, (uint)(x * 16));
            pix.SetPixel(x, 1, (uint)(x * 16));
        }
        float[] vc = { 1f, 0f, 0.5f, 0f, 1f, 0f };
        Pix pixd = pix.Affine(vc, L.L_BRING_IN_BLACK);
        for (int x = 0; x < 7; x++) {
            pixd.GetPixel(x, 0, out uint v);
            Assert.Equal((uint)(x * 16 + 8), v);
        }
    }

    [Fact]
    public void Affine_ColormapIsRemoved()
    {
        Pix pix = Pix.Create(10, 10, 8);
        PixColormap cmap = PixColormap.CreateLinear(8, 256);
        pix.SetColormap(cmap);
        float[] vc = { 1f, 0f, 0f, 0f, 1f, 0f };
        Pix pixd = pix.Affine(vc, L.L_BRING_IN_WHITE);
        Assert.Null(pixd.Colormap);
        Assert.Equal(8, pixd.Depth);
    }

    [Fact]
    public void AffinePtaWithAlpha_BuildsAlphaChannel()
    {
        Pix pix = MakePix(30, 30, 32);
        Pta pta = MakePta(0, 0, 20, 0, 0, 20);
        Pix pixd = pix.AffinePtaWithAlpha(pta, pta, null, 1.0f, 0);
        Assert.Equal(4, pixd.Spp);
        Assert.Equal(32, pixd.Depth);
        pixd.GetPixel(15, 15, out uint v);
        Assert.Equal(255u, v & 0xff);
        pixd.GetPixel(0, 0, out v);
        Assert.Equal(0u, v & 0xff);
    }

    /*------------------------------- errors -------------------------------*/

    [Fact]
    public void InvalidArguments_Throw()
    {
        Pix pix = MakePix(10, 10, 8);
        Pta good = MakePta(0, 0, 5, 0, 0, 5);
        Pta bad = Pta.Create(2);
        bad.AddPt(0, 0);
        bad.AddPt(1, 1);
        Assert.Throws<ArgumentException>(() => pix.AffineSampledPta(good, good, 0));
        Assert.Throws<ArgumentException>(() => pix.AffineSampledPta(bad, good, L.L_BRING_IN_WHITE));
        Assert.Throws<ArgumentException>(() => pix.AffinePta(good, bad, L.L_BRING_IN_WHITE));
        Assert.Throws<ArgumentException>(() => pix.AffinePtaColor(good, good, 0));
        Assert.Throws<ArgumentException>(() => MakePix(10, 10, 32).AffinePtaGray(good, good, 0));
        Assert.Throws<ArgumentException>(() => MakePix(10, 10, 16).AffineSampled(
            new[] { 1f, 0f, 0f, 0f, 1f, 0f }, L.L_BRING_IN_WHITE));
    }
}
