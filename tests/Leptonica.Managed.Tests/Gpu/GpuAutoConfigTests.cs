using System;
using System.Diagnostics.CodeAnalysis;
using Leptonica;
using Leptonica.Gpu;
using Xunit;

namespace Leptonica.Managed.Tests.Gpu;

[Collection("GpuSequential")]
public sealed class GpuAutoConfigTests
{
    private sealed class MockHardwareGpuAccelerator : IGpuAccelerator
    {
        public string DeviceName => "Mock Hardware GPU";
        public GpuBackendKind BackendKind => GpuBackendKind.Cuda;
        public bool IsHardwareGpu => true;
        public int ConvolveCalls { get; private set; }

        public void Dispose() { }

        public bool TryConvolve(Pix pixs, L_Kernel kel, int outdepth, int normflag, int sfx, int sfy, [NotNullWhen(true)] out Pix? pixd)
        {
            ConvolveCalls++;
            pixd = null;
            return false; // Fall through to CPU after recording that Auto mode dispatched here
        }

        public bool TryBilateralGrayExact(Pix pixs, L_Kernel spatialKel, L_Kernel rangeKel, [NotNullWhen(true)] out Pix? pixd) { pixd = null; return false; }
        public bool TryBilateralExact(Pix pixs, L_Kernel spatialKel, L_Kernel rangeKel, [NotNullWhen(true)] out Pix? pixd) { pixd = null; return false; }
        public bool TryConvolveRGB(Pix pixs, L_Kernel kel, int sfx, int sfy, [NotNullWhen(true)] out Pix? pixd) { pixd = null; return false; }
        public bool TryConvolveSep(Pix pixs, L_Kernel kelx, L_Kernel kely, int outdepth, int normflag, int sfx, int sfy, [NotNullWhen(true)] out Pix? pixd) { pixd = null; return false; }
        public bool TryConvolveRGBSep(Pix pixs, L_Kernel kelx, L_Kernel kely, int sfx, int sfy, [NotNullWhen(true)] out Pix? pixd) { pixd = null; return false; }
        public bool TryRankFilterGray(Pix pixs, int wf, int hf, float rank, [NotNullWhen(true)] out Pix? pixd) { pixd = null; return false; }
        public bool TryRankFilterRGB(Pix pixs, int wf, int hf, float rank, [NotNullWhen(true)] out Pix? pixd) { pixd = null; return false; }
        public bool TryMorphGrayBrick(Pix pixs, int hsize, int vsize, bool dilate, [NotNullWhen(true)] out Pix? pixd) { pixd = null; return false; }
        public bool TryOpenCloseGrayBrick(Pix pixs, int hsize, int vsize, bool open, [NotNullWhen(true)] out Pix? pixd) { pixd = null; return false; }
        public bool TryColorMorph(Pix pixs, int type, int hsize, int vsize, [NotNullWhen(true)] out Pix? pixd) { pixd = null; return false; }
        public bool TryAffine(Pix pixs, float[] vc, int incolor, bool sampled, [NotNullWhen(true)] out Pix? pixd) { pixd = null; return false; }
        public bool TryBilinear(Pix pixs, float[] vc, int incolor, bool sampled, [NotNullWhen(true)] out Pix? pixd) { pixd = null; return false; }
        public bool TryProjective(Pix pixs, float[] vc, int incolor, bool sampled, [NotNullWhen(true)] out Pix? pixd) { pixd = null; return false; }
        public bool TryRotateAM(Pix pixs, float angle, int incolor, [NotNullWhen(true)] out Pix? pixd) { pixd = null; return false; }
        public bool TryScaleLI(Pix pixs, float scalex, float scaley, [NotNullWhen(true)] out Pix? pixd) { pixd = null; return false; }
        public bool TryDewarpApplyDisparity(Pix pixs, FPix? vdisp, FPix? hdisp, int grayin, [NotNullWhen(true)] out Pix? pixd) { pixd = null; return false; }
        public bool TrySauvolaGetThreshold(Pix pixm, Pix pixms, float factor, bool wantsd, [NotNullWhen(true)] out Pix? pixd, out Pix? pixsd) { pixd = null; pixsd = null; return false; }
        public bool TryApplyLocalThreshold(Pix pixs, Pix pixth, [NotNullWhen(true)] out Pix? pixd) { pixd = null; return false; }
        public bool TryFindSkewSweep(Pix pixs, int reduction, float sweepRange, float sweepDelta, out float angle) { angle = 0; return false; }
        public bool TryBestCorrelation(Pix pix1, Pix pix2, int area1, int area2, int etransx, int etransy, int maxshift, int[]? tab8, out int delx, out int dely, out float score)
        { delx = 0; dely = 0; score = 0; return false; }
    }

    [Fact]
    public void DefaultMode_IsAuto_And_StartupConfigurationSwitchesCleanly()
    {
        GpuMode original = Utils.GetGpuMode();
        try
        {
            LeptGpu.EnableAuto();
            Assert.Equal(GpuMode.Auto, LeptGpu.Mode);
            Assert.Equal(GpuMode.Auto, Utils.GetGpuMode());
            Assert.True(LeptGpu.Enabled);

            GpuMode prev = Utils.SetGpuMode(GpuMode.CpuOnly);
            Assert.Equal(GpuMode.Auto, prev);
            Assert.Equal(GpuMode.CpuOnly, LeptGpu.Mode);
            Assert.False(LeptGpu.Enabled);
            Assert.Null(LeptGpu.ActiveDeviceName);

            LeptGpu.Configure(mode: GpuMode.Auto, backend: GpuBackendPreference.Auto);
            Assert.Equal(GpuMode.Auto, LeptGpu.Mode);
        }
        finally
        {
            Utils.SetGpuMode(original);
        }
    }

    [Fact]
    public void AutoMode_DispatchesOnlyProfitableWorkloads_And_CpuOnlyAlwaysStaysOnCpu()
    {
        var mockHw = new MockHardwareGpuAccelerator();

        // 1. In Auto mode, small images stay on CPU (below MinPixels / MinTotalWork),
        //    whereas heavy workloads automatically dispatch to the hardware GPU:
        using (LeptGpu.BeginScope(mockHw, GpuMode.Auto))
        {
            Assert.False(LeptGpu.ShouldOffload(pixelCount: 64 * 64, opsPerPixel: 25));
            Assert.True(LeptGpu.ShouldOffload(pixelCount: 1000 * 1000, opsPerPixel: 25));

            Pix small = Pix.Create(32, 32, 8);
            L_Kernel kel = L_Kernel.MakeGaussianKernel(2, 2, 1.0f, 1.0f);
            _ = small.Convolve(kel, 8, 1);
            Assert.Equal(0, mockHw.ConvolveCalls);

            Pix large = Pix.Create(800, 800, 8);
            _ = large.Convolve(kel, 8, 1);
            Assert.Equal(1, mockHw.ConvolveCalls);
        }

        // 2. In CpuOnly mode, even heavy workloads never touch the GPU:
        using (LeptGpu.BeginScope(mockHw, GpuMode.CpuOnly))
        {
            Assert.False(LeptGpu.ShouldOffload(pixelCount: 2000 * 2000, opsPerPixel: 100));
            Pix large = Pix.Create(800, 800, 8);
            L_Kernel kel = L_Kernel.MakeGaussianKernel(2, 2, 1.0f, 1.0f);
            _ = large.Convolve(kel, 8, 1);
            Assert.Equal(1, mockHw.ConvolveCalls); // unchanged
        }
    }

    [Fact]
    public void ForceGpu_BuiltInFallback_And_DispatchCountTelemetry_WorkOutOfTheBox()
    {
        using (LeptGpu.BeginScope(null, GpuMode.ForceGpu))
        {
            Assert.True(LeptGpu.Warmup());
            Assert.True(LeptGpu.ShouldOffload(pixelCount: 32 * 32, opsPerPixel: 9));
            Assert.NotNull(LeptGpu.ActiveDeviceName);
            Assert.NotEqual(GpuBackendKind.None, LeptGpu.ActiveBackendKind);

            LeptGpu.ResetDispatchCount();
            Assert.Equal(0, LeptGpu.DispatchCount);

            Pix src = Pix.Create(48, 40, 8);
            for (int y = 0; y < 40; y++)
                for (int x = 0; x < 48; x++)
                    src.SetPixel(x, y, (uint)((x * 5 + y * 7) & 0xFF));

            Pix filtered = src.RankFilterGray(3, 3, 0.5f);
            Assert.Equal(48, filtered.Width);
            Assert.Equal(40, filtered.Height);
            Assert.Equal(1, LeptGpu.DispatchCount);
        }

        using (LeptGpu.BeginScope(null, GpuMode.CpuOnly))
        {
            Assert.False(LeptGpu.Warmup());
            Assert.Equal(GpuBackendKind.None, LeptGpu.ActiveBackendKind);
        }
    }
}
