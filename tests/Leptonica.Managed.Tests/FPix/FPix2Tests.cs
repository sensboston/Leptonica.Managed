using System;
using Leptonica;

namespace Leptonica.Managed.Tests;

/// <summary>Tests of fpix2.c on small synthetic images.</summary>
public class FPix2Tests
{
    private static FPix Ramp(int w, int h)
    {
        FPix fpix = FPix.Create(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                fpix.SetPixel(x, y, x + 10 * y);
        return fpix;
    }

    private static float At(FPix f, int x, int y)
    {
        f.GetPixel(x, y, out float v);
        return v;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void PixToFPix_AndBack_IsLossless(int d)
    {
        Pix pix = Pix.Create(9, 4, d);
        uint max = d == 32 ? 1000000u : (1u << d) - 1;
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 9; x++)
                pix.SetPixel(x, y, (uint)((x * 7 + y * 13) % (max + 1)));
        FPix fpix = pix.ConvertToFPix(1);
        fpix.GetDimensions(out int w, out int h);
        Assert.Equal((9, 4), (w, h));
        pix.GetPixel(5, 2, out uint v);
        Assert.Equal((float)v, At(fpix, 5, 2));
        Pix back = fpix.ConvertToPix(d <= 8 ? 8 : d, L.L_CLIP_TO_ZERO, 0);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 9; x++) {
                pix.GetPixel(x, y, out uint a);
                back.GetPixel(x, y, out uint b);
                Assert.Equal(a, b);
            }
        DPix dpix = pix.ConvertToDPix(1);
        dpix.GetPixel(5, 2, out double dv);
        Assert.Equal((double)v, dv);
    }

    [Fact]
    public void PixToFPix_RgbGivesLuminance()
    {
        Pix pix = Pix.Create(2, 1, 32);
        pix.SetPixel(0, 0, 0xff000000u);   /* pure red */
        FPix fpix = pix.ConvertToFPix(3);
        Assert.Equal(77.0f, At(fpix, 0, 0));
        FPix raw = pix.ConvertToFPix(1);
        Assert.Equal(4278190080.0f, At(raw, 0, 0));
    }

    [Fact]
    public void ConvertToPix_ClipsAndAdaptsDepth()
    {
        FPix fpix = FPix.Create(4, 1);
        fpix.SetPixel(0, 0, -3.4f);
        fpix.SetPixel(1, 0, 2.5f);
        fpix.SetPixel(2, 0, 300.2f);
        fpix.SetPixel(3, 0, 70000.0f);
        Pix p0 = fpix.ConvertToPix(0, L.L_CLIP_TO_ZERO, 1);
        Assert.Equal(32, p0.Depth);
        p0.GetPixel(0, 0, out uint v);
        Assert.Equal(0u, v);
        Pix p8 = fpix.ConvertToPix(8, L.L_TAKE_ABSVAL, 0);
        p8.GetPixel(0, 0, out v);
        Assert.Equal(3u, v);
        p8.GetPixel(1, 0, out v);
        Assert.Equal(3u, v);
        p8.GetPixel(3, 0, out v);
        Assert.Equal(255u, v);
        Assert.Throws<ArgumentException>(() => fpix.ConvertToPix(4, L.L_CLIP_TO_ZERO, 0));
        Assert.Throws<ArgumentException>(() => fpix.ConvertToPix(8, 7, 0));
    }

    [Fact]
    public void DisplayMaxDynamicRange_ScalesToMax()
    {
        FPix fpix = FPix.Create(3, 1);
        fpix.SetPixel(0, 0, -5.0f);
        fpix.SetPixel(1, 0, 50.0f);
        fpix.SetPixel(2, 0, 100.0f);
        Pix pix = fpix.DisplayMaxDynamicRange();
        pix.GetPixel(0, 0, out uint v);
        Assert.Equal(0u, v);
        pix.GetPixel(1, 0, out v);
        Assert.Equal(128u, v);
        pix.GetPixel(2, 0, out v);
        Assert.Equal(255u, v);
        Assert.All(FPix.Create(3, 3).DisplayMaxDynamicRange().Data, w => Assert.Equal(0u, w));
    }

    [Fact]
    public void GetMinMax_ReportValuesAndLocations()
    {
        FPix fpix = Ramp(5, 4);
        fpix.SetPixel(3, 2, -7.0f);
        fpix.SetPixel(1, 3, 99.0f);
        fpix.GetMin(out float mn, out int xn, out int yn);
        fpix.GetMax(out float mx, out int xx, out int yx);
        Assert.Equal((-7.0f, 3, 2), (mn, xn, yn));
        Assert.Equal((99.0f, 1, 3), (mx, xx, yx));
        DPix dpix = fpix.ConvertToDPix();
        dpix.GetMin(out double dmn, out xn, out yn);
        dpix.GetMax(out double dmx, out xx, out yx);
        Assert.Equal((-7.0, 3, 2), (dmn, xn, yn));
        Assert.Equal((99.0, 1, 3), (dmx, xx, yx));
        Assert.Equal(99.0f, dpix.ConvertToFPix().Data[3 * 5 + 1]);
    }

    [Fact]
    public void ScaleByInteger_InterpolatesLinearlyAndKeepsSamples()
    {
        FPix fpix = Ramp(4, 3);
        FPix big = fpix.ScaleByInteger(3);
        big.GetDimensions(out int w, out int h);
        Assert.Equal((10, 7), (w, h));
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 4; x++)
                Assert.Equal(At(fpix, x, y), At(big, 3 * x, 3 * y));
        Assert.Equal(1.0f / 3.0f, At(big, 1, 0), 5);
        Assert.Equal(10.0f / 3.0f, At(big, 0, 1), 5);
        Assert.Equal(11.0f / 3.0f, At(big, 1, 1), 5);
        DPix dbig = fpix.ConvertToDPix().ScaleByInteger(3);
        dbig.GetPixel(1, 1, out double dv);
        Assert.Equal(11.0 / 3.0, dv, 9);
    }

    [Fact]
    public void LinearCombination_And_AddMultConstant()
    {
        FPix a = Ramp(4, 3);
        FPix b = Ramp(3, 2);
        FPix c = FPix.LinearCombination(null, a, b, 2.0f, -1.0f);
        Assert.Equal(2 * At(a, 2, 1) - At(b, 2, 1), At(c, 2, 1));
        Assert.Equal(At(a, 3, 2), At(c, 3, 2));       /* beyond b: not modified */
        Assert.Equal(At(a, 2, 1), At(Ramp(4, 3), 2, 1));    /* a not modified */
        Assert.Same(a, FPix.LinearCombination(a, a, b, 1.0f, 1.0f));
        Assert.Throws<ArgumentException>(() => FPix.LinearCombination(b, a, b, 1.0f, 1.0f));

        FPix d = Ramp(3, 2);
        d.AddMultConstant(5.0f, 2.0f);
        Assert.Equal(2 * 11.0f + 5.0f, At(d, 1, 1));
        d.AddMultConstant(0.0f, 1.0f);
        Assert.Equal(27.0f, At(d, 1, 1));

        DPix dd = Ramp(4, 3).ConvertToDPix();
        dd.AddMultConstant(1.0, 3.0);
        dd.GetPixel(2, 1, out double v);
        Assert.Equal(3 * 12.0 + 1.0, v);
        DPix de = DPix.LinearCombination(null, dd, dd, 1.0f, 1.0f);
        de.GetPixel(2, 1, out v);
        Assert.Equal(2 * 37.0, v);
        dd.SetAllArbitrary(4.5);
        dd.GetPixel(3, 2, out v);
        Assert.Equal(4.5, v);
    }

    [Fact]
    public void Borders_AddRemoveMirrorContinueSlope()
    {
        FPix fpix = Ramp(4, 3);
        fpix.SetResolution(200, 100);
        FPix padded = fpix.AddBorder(2, 1, 1, 2);
        padded.GetDimensions(out int w, out int h);
        Assert.Equal((7, 6), (w, h));
        padded.GetResolution(out int xr, out _);
        Assert.Equal(200, xr);
        Assert.Equal(0.0f, At(padded, 0, 0));
        Assert.Equal(At(fpix, 1, 1), At(padded, 3, 2));
        FPix back = padded.RemoveBorder(2, 1, 1, 2);
        Assert.Equal(fpix.Data, back.Data);
        Assert.Throws<ArgumentException>(() => fpix.RemoveBorder(2, 2, 0, 0));

        FPix mir = fpix.AddMirroredBorder(2, 2, 1, 1);
        Assert.Equal(At(fpix, 0, 0), At(mir, 1, 1));
        Assert.Equal(At(fpix, 1, 0), At(mir, 0, 1));
        Assert.Equal(At(fpix, 3, 0), At(mir, 6, 1));
        Assert.Equal(At(fpix, 0, 0), At(mir, 2, 0));
        Assert.Equal(At(fpix, 2, 2), At(mir, 4, 4));

        FPix cont = fpix.AddContinuedBorder(2, 2, 1, 1);
        Assert.Equal(At(fpix, 0, 1), At(cont, 0, 2));
        Assert.Equal(At(fpix, 3, 2), At(cont, 7, 4));
        Assert.Equal(At(fpix, 0, 0), At(cont, 0, 0));

        FPix slope = fpix.AddSlopeBorder(2, 2, 1, 1);
        Assert.Equal(-2.0f, At(slope, 0, 1), 5);   /* row 0: 0,1,2,3 -> slope 1 */
        Assert.Equal(-1.0f, At(slope, 1, 1), 5);
        Assert.Equal(5.0f, At(slope, 7, 1), 5);
        Assert.Equal(-9.0f, At(slope, 3, 0), 5);   /* column slope 10: 1 - 10 */
    }

    [Fact]
    public void Rasterop_ClipsToBothImages()
    {
        FPix src = Ramp(4, 4);
        FPix dst = FPix.Create(5, 5);
        dst.Rasterop(-1, 3, 4, 4, src, 1, 0);
        Assert.Equal(At(src, 2, 0), At(dst, 0, 3));
        Assert.Equal(At(src, 3, 1), At(dst, 1, 4));
        Assert.Equal(0.0f, At(dst, 2, 3));
        dst.Rasterop(9, 9, 2, 2, src, 0, 0);    /* entirely outside: no-op */
    }

    [Fact]
    public void FlipsAndRotations()
    {
        FPix f = Ramp(4, 3);
        FPix lr = FPix.FlipLR(null, f);
        FPix tb = FPix.FlipTB(null, f);
        Assert.Equal(At(f, 0, 1), At(lr, 3, 1));
        Assert.Equal(At(f, 2, 0), At(tb, 2, 2));
        FPix r180 = FPix.Rotate180(null, f);
        Assert.Equal(At(f, 0, 0), At(r180, 3, 2));
        FPix cw = f.Rotate90(1);
        cw.GetDimensions(out int w, out int h);
        Assert.Equal((3, 4), (w, h));
        Assert.Equal(At(f, 0, 2), At(cw, 0, 0));
        Assert.Equal(At(f, 0, 0), At(cw, 2, 0));
        FPix ccw = f.Rotate90(-1);
        Assert.Equal(At(f, 3, 0), At(ccw, 0, 0));
        Assert.Equal(At(f, 0, 0), At(ccw, 0, 3));
        Assert.Equal(f.Data, f.RotateOrth(4 - 1).RotateOrth(1).Data);
        Assert.Equal(f.Data, f.RotateOrth(0).Data);
        Assert.Equal(r180.Data, f.RotateOrth(2).Data);
        Assert.Throws<ArgumentException>(() => f.Rotate90(2));
        Assert.Throws<ArgumentException>(() => f.RotateOrth(4));
    }

    [Fact]
    public void LinearInterpolatePixelFloat_Interpolates()
    {
        FPix f = Ramp(4, 3);
        float v = FPix.LinearInterpolatePixelFloat(f.Data, 4, 3, 1.5f, 0.5f, -1.0f);
        Assert.Equal(1.5f + 5.0f, v, 5);
        Assert.Equal(-1.0f, FPix.LinearInterpolatePixelFloat(f.Data, 4, 3, 2.5f, 0.0f, -1.0f));
        Assert.Equal(-1.0f, FPix.LinearInterpolatePixelFloat(f.Data, 4, 3, -0.5f, 0.0f, -1.0f));
    }

    [Fact]
    public void Affine_Translation_ShiftsValues()
    {
        FPix f = Ramp(8, 8);
        float[] vc = { 1f, 0f, 2f, 0f, 1f, 1f };    /* x' = x + 2, y' = y + 1 */
        FPix g = f.Affine(vc, 0.0f);
        Assert.Equal(At(f, 3, 2), At(g, 1, 1), 4);
        Assert.Equal(0.0f, At(g, 7, 7));
        Pta ptas = Pta.Create(3);
        Pta ptad = Pta.Create(3);
        ptas.AddPt(0, 0); ptas.AddPt(7, 0); ptas.AddPt(0, 7);
        ptad.AddPt(1, 1); ptad.AddPt(8, 1); ptad.AddPt(1, 8);
        FPix h = f.AffinePta(ptad, ptas, 2, 0.0f);
        Assert.Equal(At(f, 2, 2), At(h, 3, 3), 3);
    }

    [Fact]
    public void ThresholdToPix_SetsValuesAtOrBelow()
    {
        FPix f = Ramp(4, 2);
        Pix pix = f.ThresholdToPix(2.0f);
        Assert.Equal(1, pix.Depth);
        Assert.Equal(3, pix.CountPixels(null));       /* 0, 1, 2 */
        pix.GetPixel(3, 0, out uint v);
        Assert.Equal(0u, v);
    }

    [Fact]
    public void ComponentFunction_AllDenominatorCases()
    {
        Pix pix = Pix.Create(2, 1, 32);
        Pix.ComposeRGBPixel(10, 20, 40, out uint p0);
        pix.SetPixel(0, 0, p0);
        Pix.ComposeRGBPixel(0, 5, 0, out uint p1);
        pix.SetPixel(1, 0, p1);
        FPix zero = pix.ComponentFunction(1.0f, 2.0f, 3.0f, 0.0f, 0.0f, 0.0f);
        Assert.Equal(10 + 40 + 120, At(zero, 0, 0));
        FPix g = pix.ComponentFunction(1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f);
        Assert.Equal(10.0f / 20.0f, At(g, 0, 0), 5);
        FPix b = pix.ComponentFunction(0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f);
        Assert.Equal(20.0f / 40.0f, At(b, 0, 0), 5);
        Assert.Equal(256.0f * 5.0f, At(b, 1, 0));      /* zero denominator */
        FPix r = pix.ComponentFunction(0.0f, 1.0f, 0.0f, 1.0f, 0.0f, 0.0f);
        Assert.Equal(2.0f, At(r, 0, 0), 5);
        FPix gen = pix.ComponentFunction(1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0.0f);
        Assert.Equal(70.0f / 30.0f, At(gen, 0, 0), 5);
        Assert.Throws<ArgumentException>(() => Pix.Create(2, 2, 8).ComponentFunction(1, 1, 1, 0, 0, 0));
    }
}
