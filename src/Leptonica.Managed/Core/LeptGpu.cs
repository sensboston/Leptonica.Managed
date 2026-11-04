using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Leptonica.Gpu;

namespace Leptonica;

/// <summary>
/// Execution backend family reported by an <see cref="IGpuAccelerator"/> implementation.
/// </summary>
internal enum GpuBackendKind
{
    /// <summary>No accelerator active.</summary>
    None = 0,
    /// <summary>Managed/SIMD CPU fallback accelerator (useful for testing and debugging GPU kernels).</summary>
    Cpu = 1,
    /// <summary>OpenCL GPU accelerator (AMD, Intel, Apple, or NVIDIA via OpenCL).</summary>
    OpenCL = 2,
    /// <summary>NVIDIA CUDA GPU accelerator.</summary>
    Cuda = 3,
}

/// <summary>
/// Library-wide GPU execution policy, set with <see cref="LeptGpu.Mode"/>.
/// </summary>
public enum GpuMode
{
    /// <summary>
    /// Default policy: probes for a hardware GPU (NVIDIA CUDA or AMD/Intel/Apple OpenCL) the
    /// first time an operation is large enough for the GPU to pay off, and uses it for such
    /// operations; everything else, and everything on a machine without a GPU, runs on the CPU.
    /// The results are the same either way.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Disables GPU acceleration completely: never loads CUDA/OpenCL drivers and executes
    /// all operations exclusively on CPU, matching native C Leptonica behavior.
    /// </summary>
    CpuOnly = 1,

    /// <summary>
    /// Forces GPU kernel execution regardless of image size thresholds (using a hardware GPU
    /// if present, or falling back to ILGPU's CPU accelerator for deterministic verification).
    /// </summary>
    ForceGpu = 2,
}

/// <summary>
/// Accelerator contract allowing built-in ILGPU kernels (or a custom backend) to accelerate
/// compute-bound raster operations while preserving automatic fallback to the SIMD/parallel
/// CPU implementation in <c>Leptonica.Managed</c>.
/// </summary>
internal interface IGpuAccelerator : IDisposable
{
    /// <summary>Human-readable accelerator device name.</summary>
    string DeviceName { get; }

    /// <summary>Underlying backend kind (CUDA, OpenCL, or CPU).</summary>
    GpuBackendKind BackendKind { get; }

    /// <summary>True when backed by a discrete or integrated hardware GPU (CUDA or OpenCL).</summary>
    bool IsHardwareGpu { get; }

    // --- Batch 09: Filtering & Morphological Kernels ---
    // Every Try* method returns false, and leaves its output null, when the accelerator cannot
    // handle the arguments; the caller then runs the CPU implementation.

    /// <summary>Tries to run the exact bilateral filter on an 8 bpp gray image.</summary>
    /// <param name="pixs">The 8 bpp source image without a colormap.</param>
    /// <param name="spatialKel">The spatial Gaussian kernel.</param>
    /// <param name="rangeKel">The range kernel, a 256 by 1 kernel indexed by pixel difference.</param>
    /// <param name="pixd">Receives the filtered image when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the accelerator produced the result; otherwise <see langword="false"/>.</returns>
    bool TryBilateralGrayExact(Pix pixs, L_Kernel spatialKel, L_Kernel rangeKel, [NotNullWhen(true)] out Pix? pixd);

    /// <summary>Tries to run the exact bilateral filter on an 8 bpp gray or 32 bpp color image.</summary>
    /// <param name="pixs">The 8 bpp or 32 bpp source image without a colormap.</param>
    /// <param name="spatialKel">The spatial Gaussian kernel.</param>
    /// <param name="rangeKel">The range kernel, a 256 by 1 kernel indexed by pixel difference.</param>
    /// <param name="pixd">Receives the filtered image when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the accelerator produced the result; otherwise <see langword="false"/>.</returns>
    bool TryBilateralExact(Pix pixs, L_Kernel spatialKel, L_Kernel rangeKel, [NotNullWhen(true)] out Pix? pixd);

    /// <summary>Tries to convolve a gray image with a kernel.</summary>
    /// <param name="pixs">The 8, 16 or 32 bpp source image without a colormap.</param>
    /// <param name="kel">The convolution kernel.</param>
    /// <param name="outdepth">Depth of the output image: 8, 16 or 32 bpp.</param>
    /// <param name="normflag">Nonzero to normalize the kernel so that its elements sum to 1.</param>
    /// <param name="sfx">Horizontal subsampling factor; it must be at least 1.</param>
    /// <param name="sfy">Vertical subsampling factor; it must be at least 1.</param>
    /// <param name="pixd">Receives the convolved image when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the accelerator produced the result; otherwise <see langword="false"/>.</returns>
    bool TryConvolve(Pix pixs, L_Kernel kel, int outdepth, int normflag, int sfx, int sfy, [NotNullWhen(true)] out Pix? pixd);

    /// <summary>Tries to convolve each channel of a 32 bpp color image with a kernel.</summary>
    /// <param name="pixs">The 32 bpp source image.</param>
    /// <param name="kel">The convolution kernel.</param>
    /// <param name="sfx">Horizontal subsampling factor; it must be at least 1.</param>
    /// <param name="sfy">Vertical subsampling factor; it must be at least 1.</param>
    /// <param name="pixd">Receives the convolved image when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the accelerator produced the result; otherwise <see langword="false"/>.</returns>
    bool TryConvolveRGB(Pix pixs, L_Kernel kel, int sfx, int sfy, [NotNullWhen(true)] out Pix? pixd);

    /// <summary>Tries to convolve a gray image with a separable pair of kernels.</summary>
    /// <param name="pixs">The 8, 16 or 32 bpp source image without a colormap.</param>
    /// <param name="kelx">The horizontal kernel.</param>
    /// <param name="kely">The vertical kernel.</param>
    /// <param name="outdepth">Depth of the output image: 8, 16 or 32 bpp.</param>
    /// <param name="normflag">Nonzero to normalize the kernels so that their elements sum to 1.</param>
    /// <param name="sfx">Horizontal subsampling factor; it must be at least 1.</param>
    /// <param name="sfy">Vertical subsampling factor; it must be at least 1.</param>
    /// <param name="pixd">Receives the convolved image when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the accelerator produced the result; otherwise <see langword="false"/>.</returns>
    bool TryConvolveSep(Pix pixs, L_Kernel kelx, L_Kernel kely, int outdepth, int normflag, int sfx, int sfy, [NotNullWhen(true)] out Pix? pixd);

    /// <summary>Tries to convolve each channel of a 32 bpp color image with a separable pair of kernels.</summary>
    /// <param name="pixs">The 32 bpp source image.</param>
    /// <param name="kelx">The horizontal kernel.</param>
    /// <param name="kely">The vertical kernel.</param>
    /// <param name="sfx">Horizontal subsampling factor; it must be at least 1.</param>
    /// <param name="sfy">Vertical subsampling factor; it must be at least 1.</param>
    /// <param name="pixd">Receives the convolved image when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the accelerator produced the result; otherwise <see langword="false"/>.</returns>
    bool TryConvolveRGBSep(Pix pixs, L_Kernel kelx, L_Kernel kely, int sfx, int sfy, [NotNullWhen(true)] out Pix? pixd);

    /// <summary>Tries to apply a rank filter to an 8 bpp gray image.</summary>
    /// <param name="pixs">The 8 bpp source image.</param>
    /// <param name="wf">Width of the filter window.</param>
    /// <param name="hf">Height of the filter window.</param>
    /// <param name="rank">Rank in the range 0.0 to 1.0; 0.0 is a minimum filter and 1.0 is a maximum filter.</param>
    /// <param name="pixd">Receives the filtered image when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the accelerator produced the result; otherwise <see langword="false"/>.</returns>
    bool TryRankFilterGray(Pix pixs, int wf, int hf, float rank, [NotNullWhen(true)] out Pix? pixd);

    /// <summary>Tries to apply a rank filter to each channel of a 32 bpp color image.</summary>
    /// <param name="pixs">The 32 bpp source image.</param>
    /// <param name="wf">Width of the filter window.</param>
    /// <param name="hf">Height of the filter window.</param>
    /// <param name="rank">Rank in the range 0.0 to 1.0; 0.0 is a minimum filter and 1.0 is a maximum filter.</param>
    /// <param name="pixd">Receives the filtered image when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the accelerator produced the result; otherwise <see langword="false"/>.</returns>
    bool TryRankFilterRGB(Pix pixs, int wf, int hf, float rank, [NotNullWhen(true)] out Pix? pixd);

    /// <summary>Tries to dilate or erode an 8 bpp gray image with a rectangular structuring element.</summary>
    /// <param name="pixs">The 8 bpp source image.</param>
    /// <param name="hsize">Width of the structuring element.</param>
    /// <param name="vsize">Height of the structuring element.</param>
    /// <param name="dilate"><see langword="true"/> to dilate; <see langword="false"/> to erode.</param>
    /// <param name="pixd">Receives the result when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the accelerator produced the result; otherwise <see langword="false"/>.</returns>
    bool TryMorphGrayBrick(Pix pixs, int hsize, int vsize, bool dilate, [NotNullWhen(true)] out Pix? pixd);

    /// <summary>Tries to open or close an 8 bpp gray image with a rectangular structuring element.</summary>
    /// <param name="pixs">The 8 bpp source image.</param>
    /// <param name="hsize">Width of the structuring element.</param>
    /// <param name="vsize">Height of the structuring element.</param>
    /// <param name="open"><see langword="true"/> to open; <see langword="false"/> to close.</param>
    /// <param name="pixd">Receives the result when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the accelerator produced the result; otherwise <see langword="false"/>.</returns>
    bool TryOpenCloseGrayBrick(Pix pixs, int hsize, int vsize, bool open, [NotNullWhen(true)] out Pix? pixd);

    /// <summary>Tries to apply a morphological operation to each channel of a 32 bpp color image.</summary>
    /// <param name="pixs">The 32 bpp source image.</param>
    /// <param name="type">The operation: <see cref="L.L_MORPH_DILATE"/>, <see cref="L.L_MORPH_ERODE"/>, <see cref="L.L_MORPH_OPEN"/> or <see cref="L.L_MORPH_CLOSE"/>.</param>
    /// <param name="hsize">Width of the structuring element.</param>
    /// <param name="vsize">Height of the structuring element.</param>
    /// <param name="pixd">Receives the result when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the accelerator produced the result; otherwise <see langword="false"/>.</returns>
    bool TryColorMorph(Pix pixs, int type, int hsize, int vsize, [NotNullWhen(true)] out Pix? pixd);

    // --- Batch 10: Geometric Warping, Dewarp & OCR Kernels ---

    /// <summary>Tries to apply an affine transform to an image.</summary>
    /// <param name="pixs">The source image.</param>
    /// <param name="vc">The six coefficients of the transform.</param>
    /// <param name="incolor">Color brought in from outside the image: <see cref="L.L_BRING_IN_WHITE"/> or <see cref="L.L_BRING_IN_BLACK"/>.</param>
    /// <param name="sampled"><see langword="true"/> to use sampling; <see langword="false"/> to use interpolation.</param>
    /// <param name="pixd">Receives the transformed image when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the accelerator produced the result; otherwise <see langword="false"/>.</returns>
    bool TryAffine(Pix pixs, float[] vc, int incolor, bool sampled, [NotNullWhen(true)] out Pix? pixd);

    /// <summary>Tries to apply a bilinear transform to an image.</summary>
    /// <param name="pixs">The source image.</param>
    /// <param name="vc">The eight coefficients of the transform.</param>
    /// <param name="incolor">Color brought in from outside the image: <see cref="L.L_BRING_IN_WHITE"/> or <see cref="L.L_BRING_IN_BLACK"/>.</param>
    /// <param name="sampled"><see langword="true"/> to use sampling; <see langword="false"/> to use interpolation.</param>
    /// <param name="pixd">Receives the transformed image when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the accelerator produced the result; otherwise <see langword="false"/>.</returns>
    bool TryBilinear(Pix pixs, float[] vc, int incolor, bool sampled, [NotNullWhen(true)] out Pix? pixd);

    /// <summary>Tries to apply a projective transform to an image.</summary>
    /// <param name="pixs">The source image.</param>
    /// <param name="vc">The eight coefficients of the transform.</param>
    /// <param name="incolor">Color brought in from outside the image: <see cref="L.L_BRING_IN_WHITE"/> or <see cref="L.L_BRING_IN_BLACK"/>.</param>
    /// <param name="sampled"><see langword="true"/> to use sampling; <see langword="false"/> to use interpolation.</param>
    /// <param name="pixd">Receives the transformed image when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the accelerator produced the result; otherwise <see langword="false"/>.</returns>
    bool TryProjective(Pix pixs, float[] vc, int incolor, bool sampled, [NotNullWhen(true)] out Pix? pixd);

    /// <summary>Tries to rotate an image about its center by area mapping.</summary>
    /// <param name="pixs">The source image.</param>
    /// <param name="angle">Rotation angle in radians; positive is clockwise.</param>
    /// <param name="incolor">Color brought in from outside the image: <see cref="L.L_BRING_IN_WHITE"/> or <see cref="L.L_BRING_IN_BLACK"/>.</param>
    /// <param name="pixd">Receives the rotated image when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the accelerator produced the result; otherwise <see langword="false"/>.</returns>
    bool TryRotateAM(Pix pixs, float angle, int incolor, [NotNullWhen(true)] out Pix? pixd);

    /// <summary>Tries to scale an image by linear interpolation.</summary>
    /// <param name="pixs">The source image.</param>
    /// <param name="scalex">Horizontal scale factor.</param>
    /// <param name="scaley">Vertical scale factor.</param>
    /// <param name="pixd">Receives the scaled image when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the accelerator produced the result; otherwise <see langword="false"/>.</returns>
    bool TryScaleLI(Pix pixs, float scalex, float scaley, [NotNullWhen(true)] out Pix? pixd);

    /// <summary>Tries to apply vertical and horizontal disparity arrays to an image, as done in dewarping.</summary>
    /// <param name="pixs">The source image.</param>
    /// <param name="vdisp">The vertical disparity array, or <see langword="null"/> for none.</param>
    /// <param name="hdisp">The horizontal disparity array, or <see langword="null"/> for none.</param>
    /// <param name="grayin">Gray value, 0 to 255, brought in from outside the image.</param>
    /// <param name="pixd">Receives the corrected image when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the accelerator produced the result; otherwise <see langword="false"/>.</returns>
    bool TryDewarpApplyDisparity(Pix pixs, FPix? vdisp, FPix? hdisp, int grayin, [NotNullWhen(true)] out Pix? pixd);

    /// <summary>Tries to compute the Sauvola local threshold from the local mean and mean square.</summary>
    /// <param name="pixm">The 8 bpp local mean image.</param>
    /// <param name="pixms">The 32 bpp local mean of squares image, of the same size as <paramref name="pixm"/>.</param>
    /// <param name="factor">The Sauvola factor; it must not be negative.</param>
    /// <param name="wantsd"><see langword="true"/> to also compute the local standard deviation.</param>
    /// <param name="pixd">Receives the 8 bpp threshold image when the method returns <see langword="true"/>.</param>
    /// <param name="pixsd">Receives the local standard deviation image when <paramref name="wantsd"/> is <see langword="true"/> and the method succeeds; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the accelerator produced the result; otherwise <see langword="false"/>.</returns>
    bool TrySauvolaGetThreshold(Pix pixm, Pix pixms, float factor, bool wantsd, [NotNullWhen(true)] out Pix? pixd, out Pix? pixsd);

    /// <summary>Tries to binarize an 8 bpp image by comparing it with a threshold image.</summary>
    /// <param name="pixs">The 8 bpp source image.</param>
    /// <param name="pixth">The 8 bpp threshold image, of the same size as <paramref name="pixs"/>.</param>
    /// <param name="pixd">Receives the 1 bpp result when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the accelerator produced the result; otherwise <see langword="false"/>.</returns>
    bool TryApplyLocalThreshold(Pix pixs, Pix pixth, [NotNullWhen(true)] out Pix? pixd);

    /// <summary>Tries to find the skew angle of a 1 bpp image by a sweep search.</summary>
    /// <param name="pixs">The 1 bpp source image.</param>
    /// <param name="reduction">Reduction factor used before the search.</param>
    /// <param name="sweepRange">Half the range of angles to search, in degrees.</param>
    /// <param name="sweepDelta">Step between the angles searched, in degrees.</param>
    /// <param name="angle">Receives the skew angle in degrees when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the accelerator produced the result; otherwise <see langword="false"/>.</returns>
    bool TryFindSkewSweep(Pix pixs, int reduction, float sweepRange, float sweepDelta, out float angle);

    /// <summary>Tries to find the translation that gives the best correlation between two 1 bpp images.</summary>
    /// <param name="pix1">The first 1 bpp image.</param>
    /// <param name="pix2">The second 1 bpp image.</param>
    /// <param name="area1">Number of ON pixels in <paramref name="pix1"/>.</param>
    /// <param name="area2">Number of ON pixels in <paramref name="pix2"/>.</param>
    /// <param name="etransx">Estimated x translation of the centroids of the two images.</param>
    /// <param name="etransy">Estimated y translation of the centroids of the two images.</param>
    /// <param name="maxshift">Maximum shift, in pixels, from the estimated translation in each direction.</param>
    /// <param name="tab8">A table that gives the number of ON bits in a byte, or <see langword="null"/> to use the default table.</param>
    /// <param name="delx">Receives the x translation of the best correlation.</param>
    /// <param name="dely">Receives the y translation of the best correlation.</param>
    /// <param name="score">Receives the correlation score at the best translation.</param>
    /// <returns><see langword="true"/> if the accelerator produced the result; otherwise <see langword="false"/>.</returns>
    bool TryBestCorrelation(
        Pix pix1, Pix pix2, int area1, int area2,
        int etransx, int etransy, int maxshift, int[]? tab8,
        out int delx, out int dely, out float score);
}

/// <summary>
/// Built-in GPU configuration, lazy hardware auto-discovery, and PCIe cost-model dispatcher.
/// Defaults to <see cref="GpuMode.Auto"/>: uses a hardware GPU (CUDA / OpenCL) automatically
/// when present and profitable, or runs strictly on CPU when no GPU is present or when
/// <see cref="Mode"/> is set to <see cref="GpuMode.CpuOnly"/>.
/// </summary>
public static class LeptGpu
{
    private static readonly object s_initLock = new();

    private static volatile GpuMode s_mode = ParseInitialMode();
    private static volatile GpuBackendPreference s_backendPreference = GpuBackendPreference.Auto;
    private static long s_maxPooledVramBytes = 256L * 1024 * 1024;
    private static long s_minPixels = 16_384; // e.g. 128x128 minimum unless ForceGpu is set
    private static long s_minTotalWork = 8_000_000L; // pixelCount * opsPerPixel break-even over PCIe DMA
    private static long s_dispatchCount;

    // Explicit user-supplied accelerator (if set directly via Accelerator property)
    private static volatile IGpuAccelerator? s_customAccelerator;

    // Lazily discovered built-in hardware accelerator for GpuMode.Auto (0 = unprobed, 1 = available, -1 = none)
    private static volatile int s_hwProbeState;
    private static volatile IlGpuAccelerator? s_autoHwAccelerator;

    // Lazily created fallback accelerator when GpuMode.ForceGpu is requested on a machine without a hardware GPU
    private static volatile IlGpuAccelerator? s_forceCpuFallbackAccelerator;
    private static bool s_processExitRegistered;

    // AsyncLocal scope support for thread-safe isolated testing without affecting concurrent CPU threads
    private static volatile int s_activeScopes;
    private static readonly AsyncLocal<ScopedGpuState?> s_scopedState = new();

    private sealed record ScopedGpuState(IGpuAccelerator? Accelerator, GpuMode Mode, ScopedGpuState? Previous);

    private sealed class GpuScopeHandle : IDisposable
    {
        private readonly ScopedGpuState? _previous;
        private int _disposed;

        public GpuScopeHandle(ScopedGpuState? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) {
                s_scopedState.Value = _previous;
                Interlocked.Decrement(ref s_activeScopes);
            }
        }
    }

    private static GpuMode ParseInitialMode()
    {
        string? env = Environment.GetEnvironmentVariable("LEPT_GPU_MODE");
        if (string.IsNullOrWhiteSpace(env))
            return GpuMode.Auto;

        return env.Trim().ToLowerInvariant() switch {
            "0" or "auto" => GpuMode.Auto,
            "1" or "off" or "false" or "none" or "cpu" or "cpuonly" or "disabled" => GpuMode.CpuOnly,
            "2" or "force" or "forcegpu" => GpuMode.ForceGpu,
            _ => GpuMode.Auto,
        };
    }

    /// <summary>
    /// Global GPU execution mode. Defaults to <see cref="GpuMode.Auto"/>.
    /// Set to <see cref="GpuMode.CpuOnly"/> at application startup to run exclusively on CPU
    /// without probing CUDA or OpenCL drivers.
    /// </summary>
    public static GpuMode Mode
    {
        get => s_activeScopes != 0 && s_scopedState.Value is { } scope ? scope.Mode : s_mode;
        set => s_mode = value;
    }

    /// <summary>
    /// Preferred hardware backend when auto-initializing ILGPU (<see cref="GpuBackendPreference.Auto"/>
    /// tries NVIDIA CUDA first, then OpenCL).
    /// </summary>
    internal static GpuBackendPreference BackendPreference
    {
        get => s_backendPreference;
        set {
            lock (s_initLock) {
                if (s_backendPreference != value) {
                    s_backendPreference = value;
                    ResetAutoAcceleratorsNoLock();
                }
            }
        }
    }

    /// <summary>
    /// Configures GPU execution policy and cost-model thresholds in a single call at startup.
    /// </summary>
    internal static void Configure(
        GpuMode mode = GpuMode.Auto,
        GpuBackendPreference backend = GpuBackendPreference.Auto,
        long minPixels = 16_384,
        long minTotalWork = 8_000_000L,
        long maxPooledVramBytes = 256L * 1024 * 1024)
    {
        lock (s_initLock) {
            bool resetPool = s_backendPreference != backend || s_maxPooledVramBytes != maxPooledVramBytes;
            s_mode = mode;
            s_backendPreference = backend;
            s_minPixels = Math.Max(1, minPixels);
            s_minTotalWork = Math.Max(1, minTotalWork);
            s_maxPooledVramBytes = Math.Max(4L * 1024 * 1024, maxPooledVramBytes);
            if (resetPool || mode == GpuMode.CpuOnly)
                ResetAutoAcceleratorsNoLock();
        }
    }

    /// <summary>
    /// Convenience helper equivalent to <c>LeptGpu.Mode = GpuMode.CpuOnly</c>: disables GPU usage
    /// so all operations run strictly on CPU like native C Leptonica.
    /// </summary>
    internal static void Disable() => Mode = GpuMode.CpuOnly;

    /// <summary>
    /// Convenience helper equivalent to <c>LeptGpu.Mode = GpuMode.Auto</c>: enables automatic
    /// hardware GPU acceleration when available and faster than CPU.
    /// </summary>
    internal static void EnableAuto() => Mode = GpuMode.Auto;

    /// <summary>
    /// Disposes any lazily initialized built-in GPU contexts/buffers and resets auto-discovery state.
    /// </summary>
    public static void Shutdown()
    {
        lock (s_initLock) {
            ResetAutoAcceleratorsNoLock();
        }
    }

    /// <summary>
    /// Creates an <see cref="AsyncLocal{T}"/>-isolated GPU execution scope for the current async/thread flow.
    /// Ideal for deterministic testing without affecting concurrent threads.
    /// </summary>
    internal static IDisposable BeginScope(IGpuAccelerator? accelerator, GpuMode mode = GpuMode.ForceGpu)
    {
        ScopedGpuState? prev = s_scopedState.Value;
        Interlocked.Increment(ref s_activeScopes);
        s_scopedState.Value = new ScopedGpuState(accelerator, mode, prev);
        return new GpuScopeHandle(prev);
    }

    /// <summary>
    /// Probes (if not yet probed) and returns <c>true</c> if a hardware CUDA or OpenCL GPU is available.
    /// Does not throw if drivers are absent.
    /// </summary>
    public static bool IsHardwareGpuAvailable
    {
        get {
            IGpuAccelerator? custom = EffectiveCustomAccelerator;
            if (custom != null)
                return custom.IsHardwareGpu;
            return EnsureAutoHardwareAccelerator() != null;
        }
    }

    /// <summary>
    /// Name of the active GPU accelerator device, or <c>null</c> when running on CPU.
    /// </summary>
    public static string? ActiveDeviceName
    {
        get {
            if (Mode == GpuMode.CpuOnly)
                return null;
            IGpuAccelerator? custom = EffectiveCustomAccelerator;
            if (custom != null)
                return custom.DeviceName;
            if (Mode == GpuMode.ForceGpu)
                return (EnsureAutoHardwareAccelerator() ?? EnsureForceFallbackAccelerator()).DeviceName;
            return s_autoHwAccelerator?.DeviceName;
        }
    }

    /// <summary>
    /// Backend kind of the currently active GPU accelerator (<see cref="GpuBackendKind.Cuda"/>,
    /// <see cref="GpuBackendKind.OpenCL"/>, <see cref="GpuBackendKind.Cpu"/>, or <see cref="GpuBackendKind.None"/>).
    /// </summary>
    internal static GpuBackendKind ActiveBackendKind
    {
        get {
            if (Mode == GpuMode.CpuOnly)
                return GpuBackendKind.None;
            IGpuAccelerator? custom = EffectiveCustomAccelerator;
            if (custom != null)
                return custom.BackendKind;
            if (Mode == GpuMode.ForceGpu)
                return (EnsureAutoHardwareAccelerator() ?? EnsureForceFallbackAccelerator()).BackendKind;
            return s_autoHwAccelerator?.BackendKind ?? GpuBackendKind.None;
        }
    }

    /// <summary>
    /// Total number of operations successfully executed on an active <see cref="IGpuAccelerator"/>
    /// since process start or the last call to <see cref="ResetDispatchCount"/>.
    /// </summary>
    internal static long DispatchCount => Interlocked.Read(ref s_dispatchCount);

    /// <summary>
    /// Resets <see cref="DispatchCount"/> to zero.
    /// </summary>
    internal static void ResetDispatchCount() => Interlocked.Exchange(ref s_dispatchCount, 0);

    internal static void RecordDispatch() => Interlocked.Increment(ref s_dispatchCount);

    /// <summary>
    /// Eagerly initializes the GPU accelerator according to <see cref="Mode"/> (so one-time CUDA/OpenCL
    /// driver initialization happens ahead of benchmark or latency-sensitive loops) and returns <c>true</c>
    /// when an accelerator is ready for use. Returns <c>false</c> in <see cref="GpuMode.CpuOnly"/> or when
    /// <see cref="GpuMode.Auto"/> finds no hardware GPU.
    /// </summary>
    public static bool Warmup()
    {
        GpuMode mode = Mode;
        if (mode == GpuMode.CpuOnly)
            return false;

        IGpuAccelerator? custom = EffectiveCustomAccelerator;
        if (custom != null)
            return mode == GpuMode.ForceGpu || custom.IsHardwareGpu;

        if (mode == GpuMode.ForceGpu) {
            _ = EnsureAutoHardwareAccelerator() ?? EnsureForceFallbackAccelerator();
            return true;
        }

        return EnsureAutoHardwareAccelerator() != null;
    }

    /// <summary>
    /// Explicit custom GPU accelerator override, or <c>null</c> to use built-in <see cref="Mode"/> auto-management.
    /// </summary>
    internal static IGpuAccelerator? Accelerator
    {
        get => EffectiveCustomAccelerator ?? s_autoHwAccelerator;
        set => s_customAccelerator = value;
    }

    /// <summary>
    /// Master switch mapping to <see cref="Mode"/> (<c>true</c> when not <see cref="GpuMode.CpuOnly"/>).
    /// </summary>
    internal static bool Enabled
    {
        get => Mode != GpuMode.CpuOnly;
        set => s_mode = value ? (s_mode == GpuMode.ForceGpu ? GpuMode.ForceGpu : GpuMode.Auto) : GpuMode.CpuOnly;
    }

    /// <summary>
    /// Maps to <see cref="GpuMode.ForceGpu"/>: when <c>true</c>, bypasses PCIe size thresholds and allows
    /// offloading to CPU-emulated <see cref="IGpuAccelerator"/> backends.
    /// </summary>
    internal static bool ForceOffload
    {
        get => Mode == GpuMode.ForceGpu;
        set => s_mode = value ? GpuMode.ForceGpu : (s_mode == GpuMode.CpuOnly ? GpuMode.CpuOnly : GpuMode.Auto);
    }

    /// <summary>
    /// Minimum pixel count required before transparent single-op GPU offload is considered in <see cref="GpuMode.Auto"/>.
    /// </summary>
    internal static long MinPixels
    {
        get => Interlocked.Read(ref s_minPixels);
        set => Interlocked.Exchange(ref s_minPixels, Math.Max(1, value));
    }

    /// <summary>
    /// Minimum estimated work (<c>pixelCount * opsPerPixel</c>) required to amortize host↔device transfer in <see cref="GpuMode.Auto"/>.
    /// </summary>
    internal static long MinTotalWork
    {
        get => Interlocked.Read(ref s_minTotalWork);
        set => Interlocked.Exchange(ref s_minTotalWork, Math.Max(1, value));
    }

    /// <summary>
    /// Evaluates whether an operation with <paramref name="pixelCount"/> pixels and
    /// <paramref name="opsPerPixel"/> arithmetic operations per pixel will be dispatched to the GPU.
    /// </summary>
    internal static bool ShouldOffload(long pixelCount, int opsPerPixel)
        => TryGetActive(pixelCount, opsPerPixel, out _);

    /// <summary>
    /// Fast path used by Tier-1 <see cref="Pix"/> methods to obtain an active accelerator when
    /// <see cref="Mode"/> and the PCIe cost model approve GPU offload.
    /// </summary>
    internal static bool TryGetActive(long pixelCount, int opsPerPixel, [NotNullWhen(true)] out IGpuAccelerator? accelerator)
    {
        if (pixelCount <= 0) {
            accelerator = null;
            return false;
        }

        GpuMode mode;
        IGpuAccelerator? custom;
        if (s_activeScopes != 0 && s_scopedState.Value is { } scope) {
            mode = scope.Mode;
            custom = scope.Accelerator ?? s_customAccelerator;
        } else {
            mode = s_mode;
            custom = s_customAccelerator;
        }

        if (mode == GpuMode.CpuOnly) {
            accelerator = null;
            return false;
        }

        if (mode == GpuMode.ForceGpu) {
            accelerator = custom
                ?? (IGpuAccelerator?)EnsureAutoHardwareAccelerator()
                ?? EnsureForceFallbackAccelerator();
            return true;
        }

        // GpuMode.Auto: check cost thresholds FIRST so lightweight calls never trigger GPU driver probing!
        if (pixelCount < s_minPixels || pixelCount * Math.Max(1, opsPerPixel) < s_minTotalWork) {
            accelerator = null;
            return false;
        }

        if (custom != null) {
            if (custom.IsHardwareGpu) {
                accelerator = custom;
                return true;
            }
            accelerator = null;
            return false;
        }

        int state = s_hwProbeState;
        if (state < 0) {
            accelerator = null;
            return false;
        }

        IlGpuAccelerator? hw = state > 0 ? s_autoHwAccelerator : EnsureAutoHardwareAccelerator();
        if (hw != null) {
            accelerator = hw;
            return true;
        }

        accelerator = null;
        return false;
    }

    private static IGpuAccelerator? EffectiveCustomAccelerator
        => s_activeScopes != 0 && s_scopedState.Value is { Accelerator: not null } scope
            ? scope.Accelerator
            : s_customAccelerator;

    private static IlGpuAccelerator? EnsureAutoHardwareAccelerator()
    {
        int state = s_hwProbeState;
        if (state > 0)
            return s_autoHwAccelerator;
        if (state < 0)
            return null;

        lock (s_initLock) {
            if (s_hwProbeState > 0)
                return s_autoHwAccelerator;
            if (s_hwProbeState < 0)
                return null;

            RegisterProcessExitNoLock();
            IlGpuAccelerator? created = IlGpuAccelerator.TryCreateHardwareOnly(s_backendPreference, s_maxPooledVramBytes);
            if (created != null) {
                s_autoHwAccelerator = created;
                s_hwProbeState = 1;
                return created;
            }

            s_hwProbeState = -1;
            return null;
        }
    }

    private static IlGpuAccelerator EnsureForceFallbackAccelerator()
    {
        IlGpuAccelerator? existing = s_forceCpuFallbackAccelerator;
        if (existing != null)
            return existing;

        lock (s_initLock) {
            if (s_forceCpuFallbackAccelerator != null)
                return s_forceCpuFallbackAccelerator;

            RegisterProcessExitNoLock();
            s_forceCpuFallbackAccelerator = IlGpuAccelerator.Create(GpuBackendPreference.Cpu, s_maxPooledVramBytes);
            return s_forceCpuFallbackAccelerator;
        }
    }

    private static void RegisterProcessExitNoLock()
    {
        if (!s_processExitRegistered) {
            AppDomain.CurrentDomain.ProcessExit += static (_, _) => Shutdown();
            s_processExitRegistered = true;
        }
    }

    private static void ResetAutoAcceleratorsNoLock()
    {
        IlGpuAccelerator? hw = s_autoHwAccelerator;
        IlGpuAccelerator? cpu = s_forceCpuFallbackAccelerator;
        s_autoHwAccelerator = null;
        s_forceCpuFallbackAccelerator = null;
        s_hwProbeState = 0;
        hw?.Dispose();
        cpu?.Dispose();
    }
}

/// <summary>Miscellaneous utility functions of the library, such as file name handling, library information and the GPU policy.</summary>
public static partial class Utils
{
    /// <summary>
    /// Sets the library-wide GPU execution policy (<see cref="GpuMode.Auto"/> by default,
    /// or <see cref="GpuMode.CpuOnly"/> to force pure CPU execution like native C Leptonica)
    /// and returns the previous <see cref="GpuMode"/>.
    /// </summary>
    internal static GpuMode SetGpuMode(GpuMode mode)
    {
        GpuMode prev = LeptGpu.Mode;
        LeptGpu.Mode = mode;
        return prev;
    }

    /// <summary>
    /// Returns the current library-wide <see cref="GpuMode"/> (<see cref="GpuMode.Auto"/> by default).
    /// </summary>
    internal static GpuMode GetGpuMode() => LeptGpu.Mode;
}
