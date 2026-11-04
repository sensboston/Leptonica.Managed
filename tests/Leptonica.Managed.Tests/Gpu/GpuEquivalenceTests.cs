using System;
using Leptonica;
using Leptonica.Gpu;
using Xunit;

namespace Leptonica.Managed.Tests.Gpu;

[CollectionDefinition("GpuSequential", DisableParallelization = true)]
public sealed class GpuSequentialCollection { }

/// <summary>
/// Deterministic fixture creating a single ILGPU CPU-backend accelerator for
/// reproducible CPU-vs-GPU equivalence testing across any host machine.
/// </summary>
public sealed class GpuTestFixture : IDisposable
{
    public IlGpuAccelerator Accel { get; }

    public GpuTestFixture()
    {
        Accel = IlGpuAccelerator.Create(GpuBackendPreference.Cpu);
    }

    public void Dispose()
    {
        Accel.Dispose();
    }
}

[Collection("GpuSequential")]
public sealed class GpuEquivalenceTests : IClassFixture<GpuTestFixture>
{
    private readonly IlGpuAccelerator _gpu;

    public GpuEquivalenceTests(GpuTestFixture fixture)
    {
        _gpu = fixture.Accel;
    }

    private IDisposable CpuScope() => LeptGpu.BeginScope(null, GpuMode.CpuOnly);

    private IDisposable GpuScope() => LeptGpu.BeginScope(_gpu, GpuMode.ForceGpu);

    private static Pix MakeGrayTestPix(int w, int h, int seed = 42)
    {
        var rng = new Random(seed);
        Pix pix = Pix.Create(w, h, 8);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int val = ((x * 7 + y * 13) ^ rng.Next(0, 64)) & 0xFF;
                pix.SetPixel(x, y, (uint)val);
            }
        }
        return pix;
    }

    private static Pix MakeRgbTestPix(int w, int h, int seed = 99)
    {
        var rng = new Random(seed);
        Pix pix = Pix.Create(w, h, 32);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int r = (x * 5 + y * 3 + rng.Next(0, 32)) & 0xFF;
                int g = (x * 11 + y * 7 + rng.Next(0, 32)) & 0xFF;
                int b = (x * 3 + y * 17 + rng.Next(0, 32)) & 0xFF;
                Pix.ComposeRGBPixel(r, g, b, out uint pixel);
                pix.SetPixel(x, y, pixel);
            }
        }
        return pix;
    }

    private static Pix MakeBinaryTextPix(int w, int h)
    {
        Pix pix = Pix.Create(w, h, 1);
        for (int line = 0; line < 6; line++)
        {
            int y0 = 15 + line * 18;
            if (y0 + 6 >= h) break;
            for (int x = 12; x < w - 12; x++)
            {
                if ((x % 11) < 8)
                {
                    for (int dy = 0; dy < 5; dy++)
                    {
                        pix.SetPixel(x, y0 + dy, 1);
                    }
                }
            }
        }
        return pix;
    }

    private static void AssertMaxDiff(Pix expected, Pix actual, int maxChannelDiff)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Depth, actual.Depth);

        int maxSeen = 0;
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                expected.GetPixel(x, y, out uint p1);
                actual.GetPixel(x, y, out uint p2);
                if (expected.Depth == 8)
                {
                    int d = Math.Abs((int)p1 - (int)p2);
                    if (d > maxSeen) maxSeen = d;
                }
                else if (expected.Depth == 32)
                {
                    Pix.ExtractRGBValues(p1, out int r1, out int g1, out int b1);
                    Pix.ExtractRGBValues(p2, out int r2, out int g2, out int b2);
                    maxSeen = Math.Max(maxSeen, Math.Max(Math.Abs(r1 - r2), Math.Max(Math.Abs(g1 - g2), Math.Abs(b1 - b2))));
                }
                else
                {
                    if (p1 != p2) maxSeen = Math.Max(maxSeen, 1);
                }
            }
        }

        Assert.True(maxSeen <= maxChannelDiff,
            $"Max per-channel difference {maxSeen} exceeded tolerance {maxChannelDiff}.");
    }

    [Fact]
    public void BilateralExact_GrayAndRgb_MatchesCpu()
    {
        Pix gray = MakeGrayTestPix(65, 49);
        Pix rgb = MakeRgbTestPix(65, 49);
        L_Kernel spatial = L_Kernel.MakeGaussianKernel(2, 2, 1.5f, 1.0f);
        L_Kernel range = Pix.MakeRangeKernel(25.0f);

        Pix cpuGray, cpuRgb;
        using (CpuScope())
        {
            cpuGray = gray.BilateralGrayExact(spatial, range);
            cpuRgb = rgb.BilateralExact(spatial, range);
        }

        using (GpuScope())
        {
            Pix gpuGray = gray.BilateralGrayExact(spatial, range);
            Pix gpuRgb = rgb.BilateralExact(spatial, range);

            AssertMaxDiff(cpuGray, gpuGray, maxChannelDiff: 1);
            AssertMaxDiff(cpuRgb, gpuRgb, maxChannelDiff: 1);
        }
    }

    [Fact]
    public void Convolve_2D_And_Separable_GrayAndRgb_MatchesCpu()
    {
        Pix gray = MakeGrayTestPix(73, 53);
        Pix rgb = MakeRgbTestPix(73, 53);
        L_Kernel kel2d = L_Kernel.MakeGaussianKernel(2, 2, 1.4f, 1.0f);
        L_Kernel.MakeGaussianKernelSep(2, 2, 1.4f, 1.0f, out L_Kernel kx, out L_Kernel ky);

        Pix cpuGray2d, cpuGraySep, cpuRgb2d, cpuRgbSep;
        using (CpuScope())
        {
            cpuGray2d = gray.Convolve(kel2d, 8, 1);
            cpuGraySep = gray.ConvolveSep(kx, ky, 8, 1);
            cpuRgb2d = rgb.ConvolveRGB(kel2d);
            cpuRgbSep = rgb.ConvolveRGBSep(kx, ky);
        }

        using (GpuScope())
        {
            Pix gpuGray2d = gray.Convolve(kel2d, 8, 1);
            Pix gpuGraySep = gray.ConvolveSep(kx, ky, 8, 1);
            Pix gpuRgb2d = rgb.ConvolveRGB(kel2d);
            Pix gpuRgbSep = rgb.ConvolveRGBSep(kx, ky);

            AssertMaxDiff(cpuGray2d, gpuGray2d, maxChannelDiff: 1);
            AssertMaxDiff(cpuGraySep, gpuGraySep, maxChannelDiff: 1);
            AssertMaxDiff(cpuRgb2d, gpuRgb2d, maxChannelDiff: 1);
            AssertMaxDiff(cpuRgbSep, gpuRgbSep, maxChannelDiff: 1);
        }
    }

    [Fact]
    public void RankFilter_GrayAndRgb_MatchesCpuBitExact()
    {
        Pix gray = MakeGrayTestPix(70, 55);
        Pix rgb = MakeRgbTestPix(70, 55);

        Pix cpuMedGray, cpuMinGray, cpuMaxGray, cpuMedRgb;
        using (CpuScope())
        {
            cpuMedGray = gray.RankFilterGray(5, 5, 0.5f);
            cpuMinGray = gray.RankFilterGray(7, 3, 0.0f);
            cpuMaxGray = gray.RankFilterGray(3, 7, 1.0f);
            cpuMedRgb = rgb.RankFilterRGB(5, 5, 0.5f);
        }

        using (GpuScope())
        {
            Pix gpuMedGray = gray.RankFilterGray(5, 5, 0.5f);
            Pix gpuMinGray = gray.RankFilterGray(7, 3, 0.0f);
            Pix gpuMaxGray = gray.RankFilterGray(3, 7, 1.0f);
            Pix gpuMedRgb = rgb.RankFilterRGB(5, 5, 0.5f);

            Assert.True(Pix.Equal(cpuMedGray, gpuMedGray));
            Assert.True(Pix.Equal(cpuMinGray, gpuMinGray));
            Assert.True(Pix.Equal(cpuMaxGray, gpuMaxGray));
            Assert.True(Pix.Equal(cpuMedRgb, gpuMedRgb));
        }
    }

    [Fact]
    public void GrayAndColorMorph_MatchesCpuBitExact()
    {
        Pix gray = MakeGrayTestPix(77, 58);
        Pix rgb = MakeRgbTestPix(64, 48);

        Pix cpuErode, cpuDilate, cpuOpenGray, cpuCloseGray, cpuTophat, cpuOpenRgb, cpuCloseRgb;
        using (CpuScope())
        {
            cpuErode = gray.ErodeGray(5, 3);
            cpuDilate = gray.DilateGray(5, 3);
            cpuOpenGray = gray.OpenGray(5, 5);
            cpuCloseGray = gray.CloseGray(3, 5);
            cpuTophat = gray.Tophat(5, 5, L.L_TOPHAT_WHITE);
            cpuOpenRgb = rgb.ColorMorph(L.L_MORPH_OPEN, 5, 5);
            cpuCloseRgb = rgb.ColorMorph(L.L_MORPH_CLOSE, 3, 5);
        }

        using (GpuScope())
        {
            Pix gpuErode = gray.ErodeGray(5, 3);
            Pix gpuDilate = gray.DilateGray(5, 3);
            Pix gpuOpenGray = gray.OpenGray(5, 5);
            Pix gpuCloseGray = gray.CloseGray(3, 5);
            Pix gpuTophat = gray.Tophat(5, 5, L.L_TOPHAT_WHITE);
            Pix gpuOpenRgb = rgb.ColorMorph(L.L_MORPH_OPEN, 5, 5);
            Pix gpuCloseRgb = rgb.ColorMorph(L.L_MORPH_CLOSE, 3, 5);

            Assert.True(Pix.Equal(cpuErode, gpuErode));
            Assert.True(Pix.Equal(cpuDilate, gpuDilate));
            Assert.True(Pix.Equal(cpuOpenGray, gpuOpenGray));
            Assert.True(Pix.Equal(cpuCloseGray, gpuCloseGray));
            Assert.True(Pix.Equal(cpuTophat, gpuTophat));
            Assert.True(Pix.Equal(cpuOpenRgb, gpuOpenRgb));
            Assert.True(Pix.Equal(cpuCloseRgb, gpuCloseRgb));
        }
    }

    [Fact]
    public void GeometricWarps_AffineBilinearProjectiveRotateAM_MatchCpuBitExact()
    {
        Pix gray = MakeGrayTestPix(90, 70);
        Pix rgb = MakeRgbTestPix(90, 70);
        Pix bin = MakeBinaryTextPix(96, 72);

        float[] vcAff = [0.96f, -0.08f, 3.5f, 0.07f, 1.02f, -2.0f];
        float[] vcBil = [1.01f, 0.02f, -0.0003f, 2.0f, -0.02f, 0.98f, 0.0002f, 1.5f];
        float[] vcProj = [0.99f, 0.03f, 1.5f, -0.02f, 1.01f, 2.0f, 0.0004f, -0.0003f];

        Pix cpuAffG, cpuAffC, cpuAffBin, cpuBilG, cpuBilC, cpuProjG, cpuProjC, cpuRotG, cpuRotC;
        using (CpuScope())
        {
            cpuAffG = gray.Affine(vcAff, L.L_BRING_IN_WHITE);
            cpuAffC = rgb.Affine(vcAff, L.L_BRING_IN_BLACK);
            cpuAffBin = bin.AffineSampled(vcAff, L.L_BRING_IN_WHITE);
            cpuBilG = gray.Bilinear(vcBil, L.L_BRING_IN_WHITE);
            cpuBilC = rgb.BilinearSampled(vcBil, L.L_BRING_IN_WHITE);
            cpuProjG = gray.Projective(vcProj, L.L_BRING_IN_BLACK);
            cpuProjC = rgb.Projective(vcProj, L.L_BRING_IN_WHITE);
            cpuRotG = gray.RotateAM(0.12f, L.L_BRING_IN_WHITE);
            cpuRotC = rgb.RotateAM(-0.09f, L.L_BRING_IN_BLACK);
        }

        using (GpuScope())
        {
            Pix gpuAffG = gray.Affine(vcAff, L.L_BRING_IN_WHITE);
            Pix gpuAffC = rgb.Affine(vcAff, L.L_BRING_IN_BLACK);
            Pix gpuAffBin = bin.AffineSampled(vcAff, L.L_BRING_IN_WHITE);
            Pix gpuBilG = gray.Bilinear(vcBil, L.L_BRING_IN_WHITE);
            Pix gpuBilC = rgb.BilinearSampled(vcBil, L.L_BRING_IN_WHITE);
            Pix gpuProjG = gray.Projective(vcProj, L.L_BRING_IN_BLACK);
            Pix gpuProjC = rgb.Projective(vcProj, L.L_BRING_IN_WHITE);
            Pix gpuRotG = gray.RotateAM(0.12f, L.L_BRING_IN_WHITE);
            Pix gpuRotC = rgb.RotateAM(-0.09f, L.L_BRING_IN_BLACK);

            Assert.True(Pix.Equal(cpuAffG, gpuAffG));
            Assert.True(Pix.Equal(cpuAffC, gpuAffC));
            Assert.True(Pix.Equal(cpuAffBin, gpuAffBin));
            Assert.True(Pix.Equal(cpuBilG, gpuBilG));
            Assert.True(Pix.Equal(cpuBilC, gpuBilC));
            Assert.True(Pix.Equal(cpuProjG, gpuProjG));
            Assert.True(Pix.Equal(cpuProjC, gpuProjC));
            Assert.True(Pix.Equal(cpuRotG, gpuRotG));
            Assert.True(Pix.Equal(cpuRotC, gpuRotC));
        }
    }

    private static string DescribePixDifference(Pix expected, Pix actual)
    {
        int maxWidth = Math.Min(expected.Width, actual.Width);
        int maxHeight = Math.Min(expected.Height, actual.Height);
        int differingPixels = 0;
        int maxChannelDelta = 0;
        int sampleCount = 0;
        string samples = string.Empty;

        for (int y = 0; y < maxHeight; y++)
        {
            for (int x = 0; x < maxWidth; x++)
            {
                expected.GetPixel(x, y, out uint e);
                actual.GetPixel(x, y, out uint a);
                if (e == a)
                    continue;

                differingPixels++;
                Pix.ExtractRGBValues(e, out int er, out int eg, out int eb);
                Pix.ExtractRGBValues(a, out int ar, out int ag, out int ab);
                int delta = Math.Max(Math.Abs(er - ar),
                    Math.Max(Math.Abs(eg - ag), Math.Abs(eb - ab)));
                maxChannelDelta = Math.Max(maxChannelDelta, delta);

                if (sampleCount < 8)
                {
                    if (sampleCount > 0)
                        samples += "; ";
                    samples += $"({x},{y}) expected=0x{e:X8} RGB({er},{eg},{eb}), " +
                               $"actual=0x{a:X8} RGB({ar},{ag},{ab})";
                    sampleCount++;
                }
            }
        }

        return $"Pix.Equal=false; expected={expected.Width}x{expected.Height}x{expected.Depth}, " +
               $"actual={actual.Width}x{actual.Height}x{actual.Depth}; " +
               $"differing visible pixels={differingPixels}, max RGB channel delta={maxChannelDelta}; " +
               $"first samples: {(sampleCount == 0 ? "none (check row padding/metadata)" : samples)}";
    }

    [Fact]
    public void ScaleLI_GrayAndColor_MatchesCpuBitExact()
    {
        Pix gray = MakeGrayTestPix(67, 51);
        Pix rgb = MakeRgbTestPix(67, 51);

        Pix cpuScaleG, cpuScaleC;
        using (CpuScope())
        {
            cpuScaleG = gray.ScaleGrayLI(1.75f, 1.35f);
            cpuScaleC = rgb.ScaleColorLI(1.6f, 2.1f);
        }

        using (GpuScope())
        {
            Pix gpuScaleG = gray.ScaleGrayLI(1.75f, 1.35f);
            Pix gpuScaleC = rgb.ScaleColorLI(1.6f, 2.1f);

            Assert.True(Pix.Equal(cpuScaleG, gpuScaleG));
            bool colorEqual = Pix.Equal(cpuScaleC, gpuScaleC);
            Assert.True(colorEqual, colorEqual ? string.Empty :
                DescribePixDifference(cpuScaleC, gpuScaleC));
        }
    }

    [Fact]
    public void DewarpDisparity_1bpp_8bpp_32bpp_MatchesCpuBitExact()
    {
        int w = 480, h = 420;
        Pix curvedBin = Pix.Create(w, h, 1);
        for (int line = 0; line < 18; line++)
        {
            int baseY = 30 + line * 20;
            for (int x = 30; x < w - 30; x++)
            {
                if ((x % 13) < 10)
                {
                    int curve = (int)MathF.Round(6.0f * MathF.Sin((x - 30) * MathF.PI / (w - 60)));
                    for (int dy = 0; dy < 5; dy++)
                    {
                        curvedBin.SetPixel(x, baseY + curve + dy, 1);
                    }
                }
            }
        }

        Pix src8 = MakeGrayTestPix(w, h);
        Pix src32 = MakeRgbTestPix(w, h);

        Dewarpa dewa = Dewarpa.Create(2, 15, 1, 8, 30);
        Dewarp dew = Dewarp.Create(curvedBin, 0);
        dewa.UseBothArrays(1);
        dewa.InsertDewarp(dew);
        Assert.Equal(0, dew.BuildPageModel(null));

        Pix cpu1, cpu8, cpu32;
        using (CpuScope())
        {
            Assert.Equal(0, dewa.ApplyDisparity(0, curvedBin, 255, 0, 0, out cpu1, null));
            Assert.Equal(0, dewa.ApplyDisparity(0, src8, 255, 0, 0, out cpu8, null));
            Assert.Equal(0, dewa.ApplyDisparity(0, src32, 200, 0, 0, out cpu32, null));
        }

        using (GpuScope())
        {
            Assert.Equal(0, dewa.ApplyDisparity(0, curvedBin, 255, 0, 0, out Pix gpu1, null));
            Assert.Equal(0, dewa.ApplyDisparity(0, src8, 255, 0, 0, out Pix gpu8, null));
            Assert.Equal(0, dewa.ApplyDisparity(0, src32, 200, 0, 0, out Pix gpu32, null));

            Assert.True(Pix.Equal(cpu1, gpu1));
            Assert.True(Pix.Equal(cpu8, gpu8));
            Assert.True(Pix.Equal(cpu32, gpu32));
        }
    }

    [Fact]
    public void SauvolaBinarize_MatchesCpuBitExact()
    {
        Pix gray = MakeGrayTestPix(110, 84);

        Pix cpuBin;
        using (CpuScope())
        {
            gray.SauvolaBinarize(7, 0.34f, 1, out _, out _, out _, out cpuBin);
        }

        using (GpuScope())
        {
            gray.SauvolaBinarize(7, 0.34f, 1, out _, out _, out _, out Pix gpuBin);
            Assert.True(Pix.Equal(cpuBin, gpuBin));
        }
    }

    [Fact]
    public void FindSkewSweep_And_BestCorrelation_MatchCpuExact()
    {
        Pix baseBin = MakeBinaryTextPix(160, 120);
        Pix skewed = baseBin.Rotate(0.055f, L.L_ROTATE_AREA_MAP, L.L_BRING_IN_WHITE, 0, 0);

        float cpuAngle;
        using (CpuScope())
        {
            skewed.FindSkewSweep(out cpuAngle, 1, 7.0f, 0.25f);
        }

        using (GpuScope())
        {
            skewed.FindSkewSweep(out float gpuAngle, 1, 7.0f, 0.25f);
            Assert.Equal(cpuAngle, gpuAngle, precision: 5);
        }

        Pix glyph1 = MakeBinaryTextPix(48, 36);
        Pix glyph2 = Pix.Create(48, 36, 1);
        glyph2.Rasterop(3, 2, 42, 30, L.PIX_SRC, glyph1, 0, 0);
        int area1 = glyph1.CountPixels(null);
        int area2 = glyph2.CountPixels(null);

        int cpuDelX, cpuDelY;
        float cpuScore;
        using (CpuScope())
        {
            glyph1.BestCorrelation(glyph2, area1, area2, 2, 1, 6, null,
                out cpuDelX, out cpuDelY, out cpuScore, 0);
        }

        using (GpuScope())
        {
            glyph1.BestCorrelation(glyph2, area1, area2, 2, 1, 6, null,
                out int gpuDelX, out int gpuDelY, out float gpuScore, 0);

            Assert.Equal(cpuScore, gpuScore, precision: 6);
            Assert.Equal(cpuDelX, gpuDelX);
            Assert.Equal(cpuDelY, gpuDelY);
        }
    }

    [Fact]
    public void GpuPix_MultiStageVramPipeline_ZeroIntermediateHostCopies()
    {
        Pix src = MakeGrayTestPix(96, 72);
        L_Kernel.MakeGaussianKernelSep(2, 2, 1.2f, 1.0f, out L_Kernel kx, out L_Kernel ky);

        using GpuPix g0 = _gpu.Upload(src);
        using GpuPix g1 = g0.ConvolveSep(kx, ky, 8, 1);
        using GpuPix g2 = g1.RankFilter(3, 3, 0.5f);
        using GpuPix g3 = g2.DilateBrick(3, 3);
        using GpuPix g4 = g3.RotateAM(0.04f, L.L_BRING_IN_WHITE);
        Pix gpuFinal = g4.Download();

        Pix cpuFinal;
        using (CpuScope())
        {
            Pix c1 = src.ConvolveSep(kx, ky, 8, 1);
            Pix c2 = c1.RankFilterGray(3, 3, 0.5f);
            Pix c3 = c2.DilateGray(3, 3);
            cpuFinal = c3.RotateAM(0.04f, L.L_BRING_IN_WHITE);
        }

        AssertMaxDiff(cpuFinal, gpuFinal, maxChannelDiff: 2);
    }

    [Fact]
    public void CostModel_ThresholdAndGracefulCpuFallback_WorkTransparently()
    {
        Pix small = MakeGrayTestPix(64, 64);

        Pix r1;
        using (LeptGpu.BeginScope(_gpu, GpuMode.Auto))
        {
            Assert.False(LeptGpu.ShouldOffload((long)small.Width * small.Height, 64));
            r1 = small.RankFilterGray(5, 5, 0.5f);
        }

        Pix r2;
        using (CpuScope())
        {
            r2 = small.RankFilterGray(5, 5, 0.5f);
        }
        Assert.True(Pix.Equal(r1, r2));

        // 4-bpp RotateAM is declined by TryRotateAM (returns false) and falls back gracefully to CPU inside RotateAM hook:
        Pix gray4 = Pix.Create(64, 48, 4);
        for (int y = 0; y < 48; y++)
            for (int x = 0; x < 64; x++)
                gray4.SetPixel(x, y, (uint)((x + y) & 0xF));

        Assert.False(_gpu.TryRotateAM(gray4, 0.1f, L.L_BRING_IN_WHITE, out _));

        Pix rotGpuHook;
        using (GpuScope())
        {
            rotGpuHook = gray4.RotateAM(0.1f, L.L_BRING_IN_WHITE);
        }

        Pix rotCpu;
        using (CpuScope())
        {
            rotCpu = gray4.RotateAM(0.1f, L.L_BRING_IN_WHITE);
        }
        Assert.True(Pix.Equal(rotCpu, rotGpuHook));
    }

    [Fact]
    public void DirectAcceleratorTryMethods_AllReturnTrueWithoutSilentFallback()
    {
        Pix gray = MakeGrayTestPix(80, 64);
        Pix rgb = MakeRgbTestPix(80, 64);
        Pix bin = MakeBinaryTextPix(80, 64);
        L_Kernel kel = L_Kernel.MakeGaussianKernel(2, 2, 1.4f, 1.0f);
        L_Kernel.MakeGaussianKernelSep(2, 2, 1.4f, 1.0f, out L_Kernel kx, out L_Kernel ky);
        L_Kernel range = Pix.MakeRangeKernel(25.0f);

        Assert.True(_gpu.TryBilateralGrayExact(gray, kel, range, out _));
        Assert.True(_gpu.TryBilateralExact(rgb, kel, range, out _));
        Assert.True(_gpu.TryConvolve(gray, kel, 8, 1, 1, 1, out _));
        Assert.True(_gpu.TryConvolveRGB(rgb, kel, 1, 1, out _));
        Assert.True(_gpu.TryConvolveSep(gray, kx, ky, 8, 1, 1, 1, out _));
        Assert.True(_gpu.TryConvolveRGBSep(rgb, kx, ky, 1, 1, out _));
        Assert.True(_gpu.TryRankFilterGray(gray, 5, 5, 0.5f, out _));
        Assert.True(_gpu.TryRankFilterRGB(rgb, 5, 5, 0.5f, out _));
        Assert.True(_gpu.TryMorphGrayBrick(gray, 5, 3, dilate: true, out _));
        Assert.True(_gpu.TryColorMorph(rgb, L.L_MORPH_OPEN, 3, 3, out _));

        float[] vc6 = [0.98f, -0.05f, 2.0f, 0.05f, 0.99f, 1.0f];
        float[] vc8 = [1.0f, 0.01f, -0.0002f, 1.0f, -0.01f, 1.0f, 0.0002f, 1.0f];
        Assert.True(_gpu.TryAffine(gray, vc6, L.L_BRING_IN_WHITE, sampled: false, out _));
        Assert.True(_gpu.TryBilinear(rgb, vc8, L.L_BRING_IN_WHITE, sampled: false, out _));
        Assert.True(_gpu.TryProjective(gray, vc8, L.L_BRING_IN_BLACK, sampled: false, out _));
        Assert.True(_gpu.TryRotateAM(rgb, 0.08f, L.L_BRING_IN_WHITE, out _));
        Assert.True(_gpu.TryScaleLI(rgb, 1.5f, 1.4f, out _));

        FPix vdisp = FPix.Create(80, 64);
        Assert.True(_gpu.TryDewarpApplyDisparity(gray, vdisp, null, 255, out _));
        Assert.True(_gpu.TryApplyLocalThreshold(gray, gray, out _));
        Assert.True(_gpu.TryFindSkewSweep(bin, 1, 5.0f, 0.5f, out _));
        Assert.True(_gpu.TryBestCorrelation(bin, bin, bin.CountPixels(null), bin.CountPixels(null), 0, 0, 3, null, out _, out _, out _));
    }
}
