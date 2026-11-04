using System;
using Leptonica;
using Leptonica.Gpu;
using Xunit;

namespace Leptonica.Managed.Tests.Gpu;

/// <summary>
/// Strict 0-ULP bit-exact parity tests between pure CPU Leptonica execution (<see cref="GpuMode.CpuOnly"/>)
/// and forced GPU kernel execution (<see cref="GpuMode.ForceGpu"/>) across floating-point evaluation order,
/// rounding modes, boundary conditions, and Pix metadata preservation.
/// </summary>
[Collection("GpuSequential")]
public sealed class GpuBitExactRegressionTests : IClassFixture<GpuTestFixture>
{
    private readonly IlGpuAccelerator _cpuBackendGpu;

    public GpuBitExactRegressionTests(GpuTestFixture fixture)
    {
        _cpuBackendGpu = fixture.Accel;
    }

    private static void AssertBitExactPix(Pix expected, Pix actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Depth, actual.Depth);
        Assert.Equal(expected.Spp, actual.Spp);
        Assert.Equal(expected.XRes, actual.XRes);
        Assert.Equal(expected.YRes, actual.YRes);
        Assert.Equal(expected.InputFormat, actual.InputFormat);
        Assert.Equal(expected.Text, actual.Text);
        Assert.True(Pix.Equal(expected, actual),
            $"Expected 0-ULP bit-exact raster match ({expected.Width}x{expected.Height}x{expected.Depth}bpp).");
    }

    private void AssertCpuVsGpuBitExact(Func<Pix> runOp)
    {
        Pix cpuPix;
        using (LeptGpu.BeginScope(null, GpuMode.CpuOnly))
        {
            cpuPix = runOp();
        }

        using (LeptGpu.BeginScope(_cpuBackendGpu, GpuMode.ForceGpu))
        {
            Pix gpuPix = runOp();
            AssertBitExactPix(cpuPix, gpuPix);
        }

        using IlGpuAccelerator? hw = IlGpuAccelerator.TryCreateHardwareOnly();
        if (hw != null)
        {
            using (LeptGpu.BeginScope(hw, GpuMode.ForceGpu))
            {
                Pix hwPix = runOp();
                AssertBitExactPix(cpuPix, hwPix);
            }
        }
    }

    private static Pix MakeDeterministicPix(int w, int h, int depth, int seed = 1337)
    {
        var rng = new Random(seed);
        Pix pix = Pix.Create(w, h, depth);
        pix.XRes = 300;
        pix.YRes = 600;
        pix.InputFormat = L.IFF_PNG;
        pix.Text = "gpu-parity-meta";

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                uint val = depth switch
                {
                    1 => (uint)(rng.Next(0, 100) < 37 ? 1 : 0),
                    8 => (uint)rng.Next(0, 256),
                    16 => (uint)rng.Next(0, 65536),
                    32 => (((uint)rng.Next(0, 256) << 24) |
                           ((uint)rng.Next(0, 256) << 16) |
                           ((uint)rng.Next(0, 256) << 8)),
                    _ => throw new ArgumentOutOfRangeException(nameof(depth)),
                };
                pix.SetPixel(x, y, val);
            }
        }
        return pix;
    }

    [Fact]
    public void SauvolaGetThreshold_FloatVsDoubleBoundaryPairs_AreBitExactWithCpu()
    {
        // Construct an image containing (mv, variance) boundary pairs where float vs double
        // evaluation of mv * (1.0 - factor * (1.0 - sd / 128.0)) at factor = 0.35f diverges,
        // plus > 100k random local-window mean/mean-square pairs (exercising LargeImageUsesSqrtTable).
        const int w = 321;
        const int h = 320;
        Pix pixm = Pix.Create(w, h, 8);
        Pix pixms = Pix.Create(w, h, 32);
        pixm.XRes = 300;
        pixm.YRes = 300;

        (int mv, int variance)[] criticalPairs =
        [
            (90, 251), (105, 57), (105, 2941), (107, 4663),
            (150, 423), (150, 912), (249, 1471), (255, 0), (0, 0),
        ];

        var rng = new Random(2026);
        int k = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++, k++)
            {
                int mv, ms;
                if (k < criticalPairs.Length)
                {
                    mv = criticalPairs[k].mv;
                    ms = mv * mv + criticalPairs[k].variance;
                }
                else
                {
                    mv = rng.Next(0, 256);
                    int maxVar = Math.Min(65025 - mv * mv, 16384);
                    int variance = maxVar > 0 ? rng.Next(0, maxVar + 1) : 0;
                    ms = mv * mv + variance;
                }
                pixm.SetPixel(x, y, (uint)mv);
                pixms.SetPixel(x, y, (uint)ms);
            }
        }

        foreach (float factor in new[] { 0.35f, 0.2f, 0.0f })
        {
            Pix cpuTh, gpuTh;
            Pix? cpuSd, gpuSd;
            using (LeptGpu.BeginScope(null, GpuMode.CpuOnly))
            {
                cpuTh = Pix.SauvolaGetThreshold(pixm, pixms, factor, true, out cpuSd);
            }
            using (LeptGpu.BeginScope(_cpuBackendGpu, GpuMode.ForceGpu))
            {
                gpuTh = Pix.SauvolaGetThreshold(pixm, pixms, factor, true, out gpuSd);
            }

            Assert.NotNull(cpuSd);
            Assert.NotNull(gpuSd);
            AssertBitExactPix(cpuTh, gpuTh);
            AssertBitExactPix(cpuSd!, gpuSd!);

            using IlGpuAccelerator? hw = IlGpuAccelerator.TryCreateHardwareOnly();
            if (hw != null)
            {
                using (LeptGpu.BeginScope(hw, GpuMode.ForceGpu))
                {
                    Pix hwTh = Pix.SauvolaGetThreshold(pixm, pixms, factor, true, out Pix? hwSd);
                    Assert.NotNull(hwSd);
                    AssertBitExactPix(cpuTh, hwTh);
                    AssertBitExactPix(cpuSd!, hwSd!);
                }
            }
        }

        Pix grayDoc = MakeDeterministicPix(131, 97, 8, seed: 2027);
        AssertCpuVsGpuBitExact(() =>
        {
            grayDoc.SauvolaBinarize(7, 0.35f, 1, out _, out _, out _, out Pix binOut);
            return binOut;
        });
    }

    [Fact]
    public void ScaleLI_DimensionsResolutionAndMetadata_AreBitExactWithCpu()
    {
        Pix gray = MakeDeterministicPix(73, 59, 8, seed: 401);
        Pix rgb = MakeDeterministicPix(67, 53, 32, seed: 402);

        (float sx, float sy)[] scales =
        [
            (0.83f, 0.78f),
            (1.7f, 2.3f),
            (1.13f, 0.91f),
            (2.5f, 1.5f),
        ];

        foreach (var (sx, sy) in scales)
        {
            AssertCpuVsGpuBitExact(() => gray.ScaleGrayLI(sx, sy));
            AssertCpuVsGpuBitExact(() => rgb.ScaleColorLI(sx, sy));
            AssertCpuVsGpuBitExact(() => gray.ScaleLI(sx, sy));
            AssertCpuVsGpuBitExact(() => rgb.ScaleLI(sx, sy));
        }
    }

    [Fact]
    public void Warps_AffineBilinearProjective_AllDepthsAndLastRowEdge_AreBitExactWithCpu()
    {
        // Non-word-aligned dimensions (w % 32 != 0 for 1bpp, w % 4 != 0 for 8bpp)
        Pix bin = MakeDeterministicPix(77, 61, 1, seed: 501);
        Pix gray = MakeDeterministicPix(75, 59, 8, seed: 502);
        Pix rgb = MakeDeterministicPix(71, 53, 32, seed: 503);

        // Include fractional translation landing on last source row (h - 1 <= y < h) and half-integer ties
        float[] affineVc = [0.97f, 0.04f, 0.5f, -0.03f, 0.98f, 0.75f];
        float[] bilinearVc = [0.98f, 0.02f, 0.0003f, 0.5f, -0.02f, 0.99f, -0.0002f, 0.5f];
        float[] projVc = [0.96f, 0.03f, 1.25f, -0.02f, 0.97f, 0.75f, 0.0007f, -0.0005f];

        foreach (int incolor in new[] { L.L_BRING_IN_WHITE, L.L_BRING_IN_BLACK })
        {
            AssertCpuVsGpuBitExact(() => bin.AffineSampled(affineVc, incolor));
            AssertCpuVsGpuBitExact(() => gray.AffineSampled(affineVc, incolor));
            AssertCpuVsGpuBitExact(() => rgb.AffineSampled(affineVc, incolor));
            AssertCpuVsGpuBitExact(() => gray.Affine(affineVc, incolor));
            AssertCpuVsGpuBitExact(() => rgb.Affine(affineVc, incolor));

            AssertCpuVsGpuBitExact(() => bin.BilinearSampled(bilinearVc, incolor));
            AssertCpuVsGpuBitExact(() => gray.BilinearSampled(bilinearVc, incolor));
            AssertCpuVsGpuBitExact(() => rgb.BilinearSampled(bilinearVc, incolor));
            AssertCpuVsGpuBitExact(() => gray.Bilinear(bilinearVc, incolor));
            AssertCpuVsGpuBitExact(() => rgb.Bilinear(bilinearVc, incolor));

            AssertCpuVsGpuBitExact(() => bin.ProjectiveSampled(projVc, incolor));
            AssertCpuVsGpuBitExact(() => gray.ProjectiveSampled(projVc, incolor));
            AssertCpuVsGpuBitExact(() => rgb.ProjectiveSampled(projVc, incolor));
            AssertCpuVsGpuBitExact(() => gray.Projective(projVc, incolor));
            AssertCpuVsGpuBitExact(() => rgb.Projective(projVc, incolor));
        }
    }

    [Fact]
    public void RotateAM_GrayAndColor_AnglesAndOddSizes_AreBitExactWithCpu()
    {
        Pix gray = MakeDeterministicPix(79, 61, 8, seed: 601);
        Pix rgb = MakeDeterministicPix(73, 57, 32, seed: 602);

        foreach (float angle in new[] { 0.11f, -0.23f, 0.37f, 0.0005f })
        {
            foreach (int incolor in new[] { L.L_BRING_IN_WHITE, L.L_BRING_IN_BLACK })
            {
                AssertCpuVsGpuBitExact(() => gray.RotateAM(angle, incolor));
                AssertCpuVsGpuBitExact(() => rgb.RotateAM(angle, incolor));
            }
        }
    }

    [Fact]
    public void ConvolveAndBilateral_AllDepthsNegativeWeightsAndSubsampling_AreBitExactWithCpu()
    {
        Pix gray8 = MakeDeterministicPix(75, 58, 8, seed: 701);
        Pix gray16 = MakeDeterministicPix(69, 51, 16, seed: 702);
        Pix rgb32 = MakeDeterministicPix(66, 50, 32, seed: 703);

        // Asymmetric kernel with negative weights and off-center origin
        L_Kernel kel = L_Kernel.Create(3, 5);
        kel.SetOrigin(2, 0);
        float[] weights =
        [
            -0.35f,  0.80f,  1.25f, -0.15f,  0.45f,
             0.60f, -0.50f,  2.10f,  0.95f, -0.25f,
             0.15f,  0.40f, -0.75f,  0.55f,  0.30f,
        ];
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 5; x++)
                kel.SetElement(y, x, weights[y * 5 + x]);

        L_Kernel kelx = L_Kernel.MakeGaussianKernel(2, 0, 1.3f, 1.0f);
        L_Kernel kely = L_Kernel.MakeGaussianKernel(0, 2, 1.1f, 1.0f);

        try
        {
            foreach (var (sfx, sfy) in new[] { (1, 1), (2, 3) })
            {
                Pix.SetConvolveSampling(sfx, sfy);

                AssertCpuVsGpuBitExact(() => gray8.Convolve(kel, 8, 1));
                AssertCpuVsGpuBitExact(() => gray8.Convolve(kel, 16, 0));
                AssertCpuVsGpuBitExact(() => gray8.Convolve(kel, 32, 0));
                AssertCpuVsGpuBitExact(() => gray16.Convolve(kel, 16, 1));
                AssertCpuVsGpuBitExact(() => gray16.Convolve(kel, 32, 0));

                AssertCpuVsGpuBitExact(() => gray8.ConvolveSep(kelx, kely, 8, 1));
                AssertCpuVsGpuBitExact(() => gray8.ConvolveSep(kelx, kely, 16, 0));
                AssertCpuVsGpuBitExact(() => gray8.ConvolveSep(kelx, kely, 32, 0));

                AssertCpuVsGpuBitExact(() => rgb32.ConvolveRGB(kelx));
                AssertCpuVsGpuBitExact(() => rgb32.ConvolveRGBSep(kelx, kely));
            }
        }
        finally
        {
            Pix.SetConvolveSampling(1, 1);
        }

        L_Kernel spatial = L_Kernel.MakeGaussianKernel(2, 2, 1.5f, 1.0f);
        L_Kernel range = Pix.MakeRangeKernel(25.0f);
        AssertCpuVsGpuBitExact(() => gray8.BilateralGrayExact(spatial, range));
        AssertCpuVsGpuBitExact(() => rgb32.BilateralExact(spatial, range));
    }

    [Fact]
    public void Dewarp_SkewSweep_And_BestCorrelation_AreBitExactWithCpu()
    {
        const int dw = 483, dh = 421;
        Pix curvedBin = Pix.Create(dw, dh, 1);
        curvedBin.XRes = 300;
        curvedBin.YRes = 300;
        curvedBin.InputFormat = L.IFF_PNG;
        curvedBin.Text = "dewarp-meta";
        for (int line = 0; line < 18; line++)
        {
            int baseY = 30 + line * 20;
            for (int x = 30; x < dw - 30; x++)
            {
                if ((x % 13) < 10)
                {
                    int curve = (int)MathF.Round(6.0f * MathF.Sin((x - 30) * MathF.PI / (dw - 60)));
                    for (int dy = 0; dy < 5; dy++)
                        curvedBin.SetPixel(x, baseY + curve + dy, 1);
                }
            }
        }

        Pix src8 = MakeDeterministicPix(dw, dh, 8, seed: 802);
        Pix src32 = MakeDeterministicPix(dw, dh, 32, seed: 803);

        foreach (int useBoth in new[] { 0, 1 })
        {
            Dewarpa dewa = Dewarpa.Create(2, 15, 1, 8, 30);
            Dewarp dew = Dewarp.Create(curvedBin, 0);
            dewa.UseBothArrays(useBoth);
            dewa.InsertDewarp(dew);
            Assert.Equal(0, dew.BuildPageModel(null));

            foreach (int grayin in new[] { -1, 64, 255 })
            {
                AssertCpuVsGpuBitExact(() =>
                {
                    Assert.Equal(0, dewa.ApplyDisparity(0, curvedBin, grayin, 0, 0, out Pix? outBin, null));
                    return outBin!;
                });
            }

            foreach (int grayin in new[] { -1, 128, 255 })
            {
                AssertCpuVsGpuBitExact(() =>
                {
                    Assert.Equal(0, dewa.ApplyDisparity(0, src8, grayin, 0, 0, out Pix? outGray, null));
                    return outGray!;
                });
            }

            AssertCpuVsGpuBitExact(() =>
            {
                Assert.Equal(0, dewa.ApplyDisparity(0, src32, 200, 0, 0, out Pix? outRgb, null));
                return outRgb!;
            });
        }

        // SkewSweep bit-exact angle parity
        Pix textSkew = Pix.Create(192, 144, 1);
        for (int line = 0; line < 7; line++)
        {
            int baseY = 20 + line * 16;
            for (int x = 16; x < 176; x++)
            {
                int y = baseY + (int)MathF.Round((x - 96) * 0.045f);
                if ((x % 9) < 6)
                {
                    for (int dy = 0; dy < 4; dy++)
                        textSkew.SetPixel(x, y + dy, 1);
                }
            }
        }

        foreach (int red in new[] { 1, 2, 4 })
        {
            bool cpuOk, gpuOk;
            float cpuAngle, gpuAngle;
            using (LeptGpu.BeginScope(null, GpuMode.CpuOnly))
                cpuOk = textSkew.FindSkewSweep(out cpuAngle, red, 7.0f, 0.5f);
            using (LeptGpu.BeginScope(_cpuBackendGpu, GpuMode.ForceGpu))
                gpuOk = textSkew.FindSkewSweep(out gpuAngle, red, 7.0f, 0.5f);

            Assert.Equal(cpuOk, gpuOk);
            Assert.Equal(BitConverter.SingleToInt32Bits(cpuAngle), BitConverter.SingleToInt32Bits(gpuAngle));
        }

        // BestCorrelation bit-exact tie-breaking and float score parity
        Pix p1 = Pix.Create(96, 64, 1);
        Pix p2 = Pix.Create(96, 64, 1);
        p1.Rasterop(20, 20, 24, 16, L.PIX_SET, null, 0, 0);
        p2.Rasterop(22, 20, 20, 16, L.PIX_SET, null, 0, 0); // multiple horizontal shifts tie at identical overlap
        int area1 = p1.CountPixels(null);
        int area2 = p2.CountPixels(null);

        int cpuDx, cpuDy, gpuDx, gpuDy;
        float cpuScore, gpuScore;
        using (LeptGpu.BeginScope(null, GpuMode.CpuOnly))
            p1.BestCorrelation(p2, area1, area2, 0, 0, 5, null, out cpuDx, out cpuDy, out cpuScore, 0);
        using (LeptGpu.BeginScope(_cpuBackendGpu, GpuMode.ForceGpu))
            p1.BestCorrelation(p2, area1, area2, 0, 0, 5, null, out gpuDx, out gpuDy, out gpuScore, 0);

        Assert.Equal(cpuDx, gpuDx);
        Assert.Equal(cpuDy, gpuDy);
        Assert.Equal(BitConverter.SingleToInt32Bits(cpuScore), BitConverter.SingleToInt32Bits(gpuScore));
    }
}
