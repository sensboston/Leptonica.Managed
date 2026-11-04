using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using ILGPU.Runtime.Cuda;
using ILGPU.Runtime.OpenCL;
using Leptonica.Gpu.Kernels;

namespace Leptonica.Gpu;

/// <summary>
/// Preferred backend selection policy when creating an <see cref="IlGpuAccelerator"/>.
/// </summary>
public enum GpuBackendPreference
{
    /// <summary>Prefer NVIDIA CUDA, then OpenCL (AMD / Intel / Apple), then ILGPU CPU fallback.</summary>
    Auto = 0,
    /// <summary>Require NVIDIA CUDA device.</summary>
    Cuda = 1,
    /// <summary>Require OpenCL device.</summary>
    OpenCL = 2,
    /// <summary>Use ILGPU managed CPU accelerator (ideal for CI testing and deterministic verification).</summary>
    Cpu = 3,
}

/// <summary>
/// Cross-platform <see cref="IGpuAccelerator"/> implementation backed by <c>ILGPU</c>
/// (supporting NVIDIA CUDA, AMD/Intel OpenCL, and CPU fallback) with pooled VRAM buffers and
/// VRAM-resident <see cref="GpuPix"/> pipeline support.
/// </summary>
public sealed partial class IlGpuAccelerator : IGpuAccelerator
{
    private readonly Context _context;
    private readonly Accelerator _accelerator;
    private readonly GpuBufferPool _pool;
    private readonly object _sync = new();
    private bool _disposed;

    // Lazily compiled & cached Batch 09 filter kernels
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, int, int, int, int, int, int, int, int>? _bilateralGrayKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, int, int, int, int, int, int, int, int>? _bilateralColorKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, FilterKernels.ConvolveParams>? _convTo8Kernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, FilterKernels.ConvolveParams>? _convTo16Kernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, FilterKernels.ConvolveParams>? _convTo32Kernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, int, int, int, int, int, int, int, int, int, int, int>? _convRgbKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, int, int, int, int, int, int, int, int, int>? _convRgbSepHKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, int, int, int, int, int, int, int, int, int>? _convRgbSepVKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, int, int, int, int, int, int, int>? _rankGrayKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, int, int, int, int, int, int, int>? _rankRgbKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, int, int, int, int, int>? _morphGrayHKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, int, int, int, int, int, int>? _morphGrayVKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, int, int, int, int, int>? _morphColorHKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, int, int, int, int, int, int>? _morphColorVKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, int, int, int>? _invertWordsKernel;

    private IlGpuAccelerator(Context context, Accelerator accelerator, GpuBackendKind backendKind, long maxPooledVramBytes)
    {
        _context = context;
        _accelerator = accelerator;
        BackendKind = backendKind;
        DeviceName = accelerator.Name;
        _pool = new GpuBufferPool(accelerator, maxPooledVramBytes);
    }

    /// <inheritdoc/>
    public string DeviceName { get; }
    /// <inheritdoc/>
    public GpuBackendKind BackendKind { get; }
    /// <inheritdoc/>
    public bool IsHardwareGpu => BackendKind is GpuBackendKind.Cuda or GpuBackendKind.OpenCL;

    /// <summary>
    /// Creates an <see cref="IlGpuAccelerator"/> using the requested backend discovery policy.
    /// In <see cref="GpuBackendPreference.Auto"/> mode, probes CUDA → OpenCL → CPU fallback.
    /// </summary>
    public static IlGpuAccelerator Create(
        GpuBackendPreference preference = GpuBackendPreference.Auto,
        long maxPooledVramBytes = 256L * 1024 * 1024)
    {
        Context context = Context.Create(builder => {
            builder.Default();
            builder.Math(MathMode.Default);
            builder.Optimize(OptimizationLevel.O1);
        });

        try {
            Accelerator? acc = null;
            GpuBackendKind kind = GpuBackendKind.None;

            if (preference is GpuBackendPreference.Auto or GpuBackendPreference.Cuda) {
                var cudaDevices = context.GetCudaDevices();
                if (cudaDevices.Count > 0) {
                    try {
                        acc = cudaDevices[0].CreateAccelerator(context);
                        kind = GpuBackendKind.Cuda;
                    } catch when (preference == GpuBackendPreference.Auto) {
                        acc = null;
                    }
                } else if (preference == GpuBackendPreference.Cuda) {
                    throw new InvalidOperationException("No NVIDIA CUDA device was detected by ILGPU.");
                }
            }

            if (acc == null && preference is GpuBackendPreference.Auto or GpuBackendPreference.OpenCL) {
                var clDevices = context.GetCLDevices();
                if (clDevices.Count > 0) {
                    try {
                        acc = clDevices[0].CreateAccelerator(context);
                        kind = GpuBackendKind.OpenCL;
                    } catch when (preference == GpuBackendPreference.Auto) {
                        acc = null;
                    }
                } else if (preference == GpuBackendPreference.OpenCL) {
                    throw new InvalidOperationException("No OpenCL device was detected by ILGPU.");
                }
            }

            if (acc == null) {
                var cpuDevice = context.GetCPUDevice(0);
                acc = cpuDevice.CreateAccelerator(context);
                kind = GpuBackendKind.Cpu;
            }

            return new IlGpuAccelerator(context, acc, kind, maxPooledVramBytes);
        } catch {
            context.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Probes for a real hardware GPU (NVIDIA CUDA or AMD/Intel/Apple OpenCL) without falling back
    /// to CPU emulation. Returns <c>null</c> cleanly (disposing any temporary ILGPU context) if no
    /// hardware GPU or driver is available in the current environment.
    /// </summary>
    public static IlGpuAccelerator? TryCreateHardwareOnly(
        GpuBackendPreference preference = GpuBackendPreference.Auto,
        long maxPooledVramBytes = 256L * 1024 * 1024)
    {
        if (preference == GpuBackendPreference.Cpu)
            return null;

        Context? context = null;
        try {
            context = Context.Create(builder => {
                builder.Default();
                builder.Math(MathMode.Default);
                builder.Optimize(OptimizationLevel.O1);
            });

            Accelerator? acc = null;
            GpuBackendKind kind = GpuBackendKind.None;

            if (preference is GpuBackendPreference.Auto or GpuBackendPreference.Cuda) {
                try {
                    var cudaDevices = context.GetCudaDevices();
                    if (cudaDevices.Count > 0) {
                        acc = cudaDevices[0].CreateAccelerator(context);
                        kind = GpuBackendKind.Cuda;
                    }
                } catch {
                    acc = null;
                }
            }

            if (acc == null && preference is GpuBackendPreference.Auto or GpuBackendPreference.OpenCL) {
                try {
                    var clDevices = context.GetCLDevices();
                    if (clDevices.Count > 0) {
                        acc = clDevices[0].CreateAccelerator(context);
                        kind = GpuBackendKind.OpenCL;
                    }
                } catch {
                    acc = null;
                }
            }

            if (acc == null) {
                context.Dispose();
                return null;
            }

            var instance = new IlGpuAccelerator(context, acc, kind, maxPooledVramBytes);
            context = null;
            return instance;
        } catch {
            context?.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Creates an <see cref="IlGpuAccelerator"/> and registers it as <see cref="LeptGpu.Accelerator"/>.
    /// Disposes any previously registered accelerator if different.
    /// </summary>
    public static IlGpuAccelerator RegisterDefault(
        GpuBackendPreference preference = GpuBackendPreference.Auto,
        long maxPooledVramBytes = 256L * 1024 * 1024)
    {
        IlGpuAccelerator acc = Create(preference, maxPooledVramBytes);
        IGpuAccelerator? prev = LeptGpu.Accelerator;
        LeptGpu.Accelerator = acc;
        prev?.Dispose();
        return acc;
    }

    // =========================================================================
    // Host <-> VRAM Transfer & GpuPix Pipeline Primitives
    // =========================================================================

    /// <summary>
    /// Uploads a non-colormapped <see cref="Pix"/> into pooled VRAM and returns a <see cref="GpuPix"/> handle.
    /// </summary>
    public GpuPix Upload(Pix pix)
    {
        ArgumentNullException.ThrowIfNull(pix);
        if (pix.Colormap != null)
            throw new ArgumentException("Colormapped Pix must be expanded before GPU upload.", nameof(pix));

        int words = pix.Wpl * pix.Height;
        PooledGpuBuffer<uint> buf = _pool.RentUInt32(words);
        try {
            lock (_sync) {
                CopyFromHost(buf.View, pix.Data, words);
                _accelerator.Synchronize();
            }
            return new GpuPix(this, buf, pix.Width, pix.Height, pix.Depth, pix.Wpl, pix.Spp, pix.XRes, pix.YRes, pix.InputFormat);
        } catch {
            buf.Dispose();
            throw;
        }
    }

    internal void DownloadWords(ArrayView1D<uint, Stride1D.Dense> srcView, uint[] dstHost, int wordCount)
    {
        lock (_sync) {
            CopyToHost(srcView, dstHost, wordCount);
            _accelerator.Synchronize();
        }
    }

    internal GpuPix CloneGpuPix(GpuPix src)
    {
        PooledGpuBuffer<uint> dstBuf = _pool.RentUInt32(src.WordCount);
        try {
            lock (_sync) {
                dstBuf.View.CopyFrom(src.View);
                _accelerator.Synchronize();
            }
            return new GpuPix(this, dstBuf, src.Width, src.Height, src.Depth, src.Wpl, src.Spp, src.XRes, src.YRes, src.Informat);
        } catch {
            dstBuf.Dispose();
            throw;
        }
    }

    // The managed array is pinned for the whole transfer: ILGPU copies from the raw address of the
    // first element, and without pinning a compacting GC can move a small-object-heap array while the
    // driver still reads or writes it.
    internal static unsafe void CopyFromHost<T>(ArrayView1D<T, Stride1D.Dense> dst, T[] src, long count)
        where T : unmanaged
    {
        fixed (T* p = src)
            dst.SubView(0, count).CopyFromCPU(ref *p, count);
    }

    internal static unsafe void CopyToHost<T>(ArrayView1D<T, Stride1D.Dense> src, T[] dst, long count)
        where T : unmanaged
    {
        fixed (T* p = dst)
            src.SubView(0, count).CopyToCPU(ref *p, count);
    }

    private PooledGpuBuffer<float> UploadFloatArrayLocked(float[] values)
    {
        PooledGpuBuffer<float> buf = _pool.RentFloat32(values.Length);
        CopyFromHost(buf.View, values, values.Length);
        return buf;
    }

    internal static int ComputeWpl(int width, int depth) => ((width * depth) + 31) >> 5;

    /// <summary>
    /// True on backends whose device kernels execute IEEE-754 round-to-nearest-even arithmetic
    /// without FMA contraction (NVIDIA CUDA via PTX <c>.rn</c> inline assembly and ILGPU CPU).
    /// OpenCL compiles kernels without strict FP flags (permitting FMA contraction and 2.5–3 ULP
    /// <c>div</c>/<c>sqrt</c>) and may lack <c>cl_khr_fp64</c>, so floating-point kernels decline
    /// <c>Try*</c> dispatch on OpenCL to preserve bit-exact CPU parity while keeping integer-only
    /// GPU kernels active.
    /// </summary>
    private bool HasBitExactDeviceFloat => BackendKind != GpuBackendKind.OpenCL;

    private static bool MirroredBorderFits(L_Kernel kel, int w, int h)
    {
        kel.GetParameters(out int sy, out int sx, out int cy, out int cx);
        // L_Kernel.Invert() maps (cx, cy) -> (sx - 1 - cx, sy - 1 - cy), so inverted borders are:
        int invCx = sx - 1 - cx;
        int invCy = sy - 1 - cy;
        int right = sx - invCx;
        int bot = sy - invCy;
        return sx >= 1 && sy >= 1 &&
               invCx >= 0 && right >= 0 && invCy >= 0 && bot >= 0 &&
               invCx <= w && right <= w && invCy <= h && bot <= h;
    }

    /// <summary>
    /// Downloads a result as a freshly created raster without any metadata, matching CPU paths that
    /// build their output with <c>CreateNoInit</c> (default Spp, no resolution, format or text).
    /// </summary>
    private Pix DownloadPlain(GpuPix dst)
    {
        Pix pix = Pix.CreateNoInit(dst.Width, dst.Height, dst.Depth);
        DownloadWords(dst.DeviceBuffer.View, pix.Data, dst.WordCount);
        return pix;
    }

    private static Pix DownloadAsTemplate(GpuPix dst, Pix template)
    {
        Pix pix = dst.Download();
        pix.CopyResolution(template);
        pix.CopyInputFormat(template);
        pix.CopyText(template);
        if (pix.Depth == 32 && template.Depth == 32)
            pix.Spp = template.Spp;
        return pix;
    }

    internal void InvertInPlace(GpuPix pix)
    {
        int remBits = (pix.Width * pix.Depth) & 31;
        uint lastMask = remBits == 0 ? 0xffffffffu : (0xffffffffu << (32 - remBits));
        int clearAlpha = (pix.Depth == 32 && pix.Spp == 3) ? 1 : 0;

        lock (_sync) {
            _invertWordsKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView1D<uint, Stride1D.Dense>, int, int, int>(FilterKernels.InvertWordsKernel);
            _invertWordsKernel(pix.WordCount, pix.View, pix.Wpl, unchecked((int)lastMask), clearAlpha);
            _accelerator.Synchronize();
        }
    }

    // =========================================================================
    // VRAM-Resident Filter Operations (used by both GpuPix and IGpuAccelerator)
    // =========================================================================

    internal GpuPix BilateralExactGpu(GpuPix src, L_Kernel spatialKel, L_Kernel rangeKel)
    {
        if (src.Depth != 8 && src.Depth != 32)
            throw new ArgumentException("BilateralExactGpu requires 8 or 32 bpp.", nameof(src));
        if (rangeKel.Sx != 256 || rangeKel.Sy != 1)
            throw new ArgumentException("rangeKel must be 256x1.", nameof(rangeKel));

        L_Kernel keli = spatialKel.Invert();
        keli.GetParameters(out int sy, out int sx, out int cy, out int cx);
        if (src.Width < 2 * sx + 1 || src.Height < 2 * sy + 1)
            return CloneGpuPix(src);

        float[] flatSpatial = keli.GetFlatData();
        float[] flatRange = rangeKel.Data[0];

        int wpld = src.Wpl;
        PooledGpuBuffer<uint> dstBuf = _pool.RentUInt32(wpld * src.Height);
        try {
            lock (_sync) {
                using PooledGpuBuffer<float> sKelBuf = UploadFloatArrayLocked(flatSpatial);
                using PooledGpuBuffer<float> rKelBuf = UploadFloatArrayLocked(flatRange);

                if (src.Depth == 8) {
                    _bilateralGrayKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                        Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                        ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                        int, int, int, int, int, int, int, int>(FilterKernels.BilateralGrayExactWordKernel);
                    _bilateralGrayKernel(
                        wpld * src.Height, src.View, dstBuf.View, sKelBuf.View, rKelBuf.View,
                        src.Width, src.Height, src.Wpl, wpld, sx, sy, cx, cy);
                } else {
                    _bilateralColorKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                        Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                        ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                        int, int, int, int, int, int, int, int>(FilterKernels.BilateralColorExactKernel);
                    _bilateralColorKernel(
                        src.Width * src.Height, src.View, dstBuf.View, sKelBuf.View, rKelBuf.View,
                        src.Width, src.Height, src.Wpl, wpld, sx, sy, cx, cy);
                }
                _accelerator.Synchronize();
            }
            return new GpuPix(this, dstBuf, src.Width, src.Height, src.Depth, wpld, src.Depth == 32 ? 3 : 1, 0, 0, 0);
        } catch {
            dstBuf.Dispose();
            throw;
        }
    }

    internal GpuPix ConvolveGpu(GpuPix src, L_Kernel kel, int outdepth, int normflag, int sfx, int sfy)
    {
        if (src.Depth != 8 && src.Depth != 16 && src.Depth != 32)
            throw new ArgumentException("ConvolveGpu requires 8, 16, or 32 bpp.", nameof(src));
        if (outdepth != 8 && outdepth != 16 && outdepth != 32)
            throw new ArgumentException("outdepth must be 8, 16, or 32.", nameof(outdepth));

        L_Kernel keli = kel.Invert();
        L_Kernel keln = normflag != 0 ? keli.Normalize(1.0f) : keli;
        keln.GetParameters(out int sy, out int sx, out int cy, out int cx);
        float[] flatKel = keln.GetFlatData();

        int wd = (src.Width + sfx - 1) / sfx;
        int hd = (src.Height + sfy - 1) / sfy;
        int wpld = ComputeWpl(wd, outdepth);
        PooledGpuBuffer<uint> dstBuf = _pool.RentUInt32(wpld * hd);
        try {
            lock (_sync) {
                using PooledGpuBuffer<float> kelBuf = UploadFloatArrayLocked(flatKel);
                DispatchConvolveLocked(
                    src.View, dstBuf.View, kelBuf.View,
                    src.Width, src.Height, src.Wpl, src.Depth,
                    wd, hd, wpld, outdepth, sfx, sfy, sx, sy, cx, cy);
                _accelerator.Synchronize();
            }
            return new GpuPix(this, dstBuf, wd, hd, outdepth, wpld, 1, 0, 0, 0);
        } catch {
            dstBuf.Dispose();
            throw;
        }
    }

    private void DispatchConvolveLocked(
        ArrayView1D<uint, Stride1D.Dense> srcView,
        ArrayView1D<uint, Stride1D.Dense> dstView,
        ArrayView1D<float, Stride1D.Dense> kelView,
        int w, int h, int wpls, int srcDepth,
        int wd, int hd, int wpld, int outdepth,
        int sfx, int sfy, int sx, int sy, int cx, int cy)
    {
        var p = new FilterKernels.ConvolveParams(w, h, wpls, srcDepth, wd, wpld, sfx, sfy, sx, sy, cx, cy);
        if (outdepth == 8) {
            _convTo8Kernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                ArrayView1D<float, Stride1D.Dense>, FilterKernels.ConvolveParams>(FilterKernels.ConvolveTo8WordKernel);
            _convTo8Kernel(wpld * hd, srcView, dstView, kelView, p);
        } else if (outdepth == 16) {
            _convTo16Kernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                ArrayView1D<float, Stride1D.Dense>, FilterKernels.ConvolveParams>(FilterKernels.ConvolveTo16WordKernel);
            _convTo16Kernel(wpld * hd, srcView, dstView, kelView, p);
        } else {
            _convTo32Kernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                ArrayView1D<float, Stride1D.Dense>, FilterKernels.ConvolveParams>(FilterKernels.ConvolveTo32Kernel);
            _convTo32Kernel(wd * hd, srcView, dstView, kelView, p);
        }
    }

    internal GpuPix ConvolveSepGpu(GpuPix src, L_Kernel kelx, L_Kernel kely, int outdepth, int normflag, int sfx, int sfy)
    {
        L_Kernel kxUse = normflag != 0 ? kelx.Normalize(1000.0f) : kelx;
        L_Kernel kyUse = normflag != 0 ? kely.Normalize(0.001f) : kely;
        L_Kernel kxi = kxUse.Invert();
        L_Kernel kyi = kyUse.Invert();
        kxi.GetParameters(out int sy1, out int sx1, out int cy1, out int cx1);
        kyi.GetParameters(out int sy2, out int sx2, out int cy2, out int cx2);
        float[] flatKx = kxi.GetFlatData();
        float[] flatKy = kyi.GetFlatData();

        int wt = (src.Width + sfx - 1) / sfx;
        int ht = src.Height;
        int wd = wt;
        int hd = (ht + sfy - 1) / sfy;
        int wpld = ComputeWpl(wd, outdepth);

        PooledGpuBuffer<uint> dstBuf = _pool.RentUInt32(wpld * hd);
        try {
            lock (_sync) {
                using PooledGpuBuffer<uint> tmpBuf = _pool.RentUInt32(wt * ht);
                using PooledGpuBuffer<float> kxBuf = UploadFloatArrayLocked(flatKx);
                using PooledGpuBuffer<float> kyBuf = UploadFloatArrayLocked(flatKy);

                DispatchConvolveLocked(
                    src.View, tmpBuf.View, kxBuf.View,
                    src.Width, src.Height, src.Wpl, src.Depth,
                    wt, ht, wt, 32, sfx, 1, sx1, sy1, cx1, cy1);
                DispatchConvolveLocked(
                    tmpBuf.View, dstBuf.View, kyBuf.View,
                    wt, ht, wt, 32,
                    wd, hd, wpld, outdepth, 1, sfy, sx2, sy2, cx2, cy2);
                _accelerator.Synchronize();
            }
            return new GpuPix(this, dstBuf, wd, hd, outdepth, wpld, 1, 0, 0, 0);
        } catch {
            dstBuf.Dispose();
            throw;
        }
    }

    internal GpuPix ConvolveRgbGpu(GpuPix src, L_Kernel kel, int sfx, int sfy)
    {
        if (src.Depth != 32)
            throw new ArgumentException("ConvolveRgbGpu requires 32 bpp.", nameof(src));

        L_Kernel keln = kel.Invert().Normalize(1.0f);
        keln.GetParameters(out int sy, out int sx, out int cy, out int cx);
        float[] flatKel = keln.GetFlatData();

        int wd = (src.Width + sfx - 1) / sfx;
        int hd = (src.Height + sfy - 1) / sfy;
        int wpld = wd;
        PooledGpuBuffer<uint> dstBuf = _pool.RentUInt32(wpld * hd);
        try {
            lock (_sync) {
                using PooledGpuBuffer<float> kelBuf = UploadFloatArrayLocked(flatKel);
                _convRgbKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                    Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                    int, int, int, int, int, int, int, int, int, int, int>(FilterKernels.ConvolveRgbKernel);
                _convRgbKernel(wd * hd, src.View, dstBuf.View, kelBuf.View, src.Width, src.Height, src.Wpl, wd, wpld, sfx, sfy, sx, sy, cx, cy);
                _accelerator.Synchronize();
            }
            return new GpuPix(this, dstBuf, wd, hd, 32, wpld, 3, 0, 0, 0);
        } catch {
            dstBuf.Dispose();
            throw;
        }
    }

    internal GpuPix ConvolveRgbSepGpu(GpuPix src, L_Kernel kelx, L_Kernel kely, int sfx, int sfy)
    {
        if (src.Depth != 32)
            throw new ArgumentException("ConvolveRgbSepGpu requires 32 bpp.", nameof(src));

        L_Kernel kxi = kelx.Normalize(1000.0f).Invert();
        L_Kernel kyi = kely.Normalize(0.001f).Invert();
        kxi.GetParameters(out int sy1, out int sx1, out int cy1, out int cx1);
        kyi.GetParameters(out int sy2, out int sx2, out int cy2, out int cx2);
        float[] flatKx = kxi.GetFlatData();
        float[] flatKy = kyi.GetFlatData();

        int wt = (src.Width + sfx - 1) / sfx;
        int ht = src.Height;
        int wd = wt;
        int hd = (ht + sfy - 1) / sfy;
        int wpld = wd;

        PooledGpuBuffer<uint> dstBuf = _pool.RentUInt32(wpld * hd);
        try {
            lock (_sync) {
                using PooledGpuBuffer<uint> tmpR = _pool.RentUInt32(wt * ht);
                using PooledGpuBuffer<uint> tmpG = _pool.RentUInt32(wt * ht);
                using PooledGpuBuffer<uint> tmpB = _pool.RentUInt32(wt * ht);
                using PooledGpuBuffer<float> kxBuf = UploadFloatArrayLocked(flatKx);
                using PooledGpuBuffer<float> kyBuf = UploadFloatArrayLocked(flatKy);

                _convRgbSepHKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                    Index1D, ArrayView1D<uint, Stride1D.Dense>,
                    ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                    ArrayView1D<float, Stride1D.Dense>, int, int, int, int, int, int, int, int, int>(FilterKernels.ConvolveRgbSepHorizKernel);
                _convRgbSepVKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                    Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                    ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                    int, int, int, int, int, int, int, int, int>(FilterKernels.ConvolveRgbSepVertKernel);

                _convRgbSepHKernel(wt * ht, src.View, tmpR.View, tmpG.View, tmpB.View, kxBuf.View, src.Width, src.Height, src.Wpl, wt, sfx, sx1, sy1, cx1, cy1);
                _convRgbSepVKernel(wd * hd, tmpR.View, tmpG.View, tmpB.View, dstBuf.View, kyBuf.View, wt, ht, wd, wpld, sfy, sx2, sy2, cx2, cy2);
                _accelerator.Synchronize();
            }
            return new GpuPix(this, dstBuf, wd, hd, 32, wpld, 3, 0, 0, 0);
        } catch {
            dstBuf.Dispose();
            throw;
        }
    }

    internal GpuPix RankFilterGpu(GpuPix src, int wf, int hf, float rank)
    {
        if (src.Depth != 8 && src.Depth != 32)
            throw new ArgumentException("RankFilterGpu requires 8 or 32 bpp.", nameof(src));
        if (wf < 1 || hf < 1)
            throw new ArgumentOutOfRangeException(nameof(wf));
        if (wf == 1 && hf == 1)
            return CloneGpuPix(src);

        if ((wf & 1) != 0 && (hf & 1) != 0) {
            if (rank == 0.0f) return MorphBrickGpu(src, wf, hf, dilate: false);
            if (rank == 1.0f) return MorphBrickGpu(src, wf, hf, dilate: true);
        }

        float rUse = rank;
        if (rUse <= 0.0f) rUse = 0.0001f;
        else if (rUse >= 1.0f) rUse = 0.9999f;
        int rankloc = (int)(rUse * wf * hf);

        int wpld = src.Wpl;
        PooledGpuBuffer<uint> dstBuf = _pool.RentUInt32(wpld * src.Height);
        try {
            lock (_sync) {
                if (src.Depth == 8) {
                    _rankGrayKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                        Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                        int, int, int, int, int, int, int>(FilterKernels.RankFilterGrayWordKernel);
                    _rankGrayKernel(wpld * src.Height, src.View, dstBuf.View, src.Width, src.Height, src.Wpl, wpld, wf, hf, rankloc);
                } else {
                    _rankRgbKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                        Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                        int, int, int, int, int, int, int>(FilterKernels.RankFilterRgbKernel);
                    _rankRgbKernel(src.Width * src.Height, src.View, dstBuf.View, src.Width, src.Height, src.Wpl, wpld, wf, hf, rankloc);
                }
                _accelerator.Synchronize();
            }
            return new GpuPix(this, dstBuf, src.Width, src.Height, src.Depth, wpld, src.Depth == 32 ? 3 : 1, src.XRes, src.YRes, src.Informat);
        } catch {
            dstBuf.Dispose();
            throw;
        }
    }

    internal GpuPix MorphBrickGpu(GpuPix src, int hsize, int vsize, bool dilate)
    {
        if (src.Depth != 8 && src.Depth != 32)
            throw new ArgumentException("MorphBrickGpu requires 8 or 32 bpp.", nameof(src));
        if ((hsize & 1) == 0) hsize++;
        if ((vsize & 1) == 0) vsize++;
        if (hsize <= 1 && vsize <= 1)
            return CloneGpuPix(src);

        int rx = hsize >> 1;
        int ry = vsize >> 1;
        int dilateFlag = dilate ? 1 : 0;
        int wpld = src.Wpl;
        int words = wpld * src.Height;

        PooledGpuBuffer<uint> dstBuf = _pool.RentUInt32(words);
        try {
            lock (_sync) {
                if (src.Depth == 8) {
                    _morphGrayHKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                        Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                        int, int, int, int, int>(FilterKernels.MorphGrayHorizWordKernel);
                    _morphGrayVKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                        Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                        int, int, int, int, int, int>(FilterKernels.MorphGrayVertWordKernel);

                    if (vsize == 1) {
                        _morphGrayHKernel(words, src.View, dstBuf.View, src.Width, src.Wpl, wpld, rx, dilateFlag);
                    } else if (hsize == 1) {
                        _morphGrayVKernel(words, src.View, dstBuf.View, src.Width, src.Height, src.Wpl, wpld, ry, dilateFlag);
                    } else {
                        using PooledGpuBuffer<uint> tmpBuf = _pool.RentUInt32(words);
                        _morphGrayHKernel(words, src.View, tmpBuf.View, src.Width, src.Wpl, wpld, rx, dilateFlag);
                        _morphGrayVKernel(words, tmpBuf.View, dstBuf.View, src.Width, src.Height, wpld, wpld, ry, dilateFlag);
                    }
                } else {
                    int pxCount = src.Width * src.Height;
                    _morphColorHKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                        Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                        int, int, int, int, int>(FilterKernels.MorphColorHorizKernel);
                    _morphColorVKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                        Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                        int, int, int, int, int, int>(FilterKernels.MorphColorVertKernel);

                    if (vsize == 1) {
                        _morphColorHKernel(pxCount, src.View, dstBuf.View, src.Width, src.Wpl, wpld, rx, dilateFlag);
                    } else if (hsize == 1) {
                        _morphColorVKernel(pxCount, src.View, dstBuf.View, src.Width, src.Height, src.Wpl, wpld, ry, dilateFlag);
                    } else {
                        using PooledGpuBuffer<uint> tmpBuf = _pool.RentUInt32(words);
                        _morphColorHKernel(pxCount, src.View, tmpBuf.View, src.Width, src.Wpl, wpld, rx, dilateFlag);
                        _morphColorVKernel(pxCount, tmpBuf.View, dstBuf.View, src.Width, src.Height, wpld, wpld, ry, dilateFlag);
                    }
                }
                _accelerator.Synchronize();
            }
            return new GpuPix(this, dstBuf, src.Width, src.Height, src.Depth, wpld, src.Depth == 32 ? 3 : 1, src.XRes, src.YRes, src.Informat);
        } catch {
            dstBuf.Dispose();
            throw;
        }
    }

    // =========================================================================
    // IGpuAccelerator Contract — Batch 09 Filter Implementations
    // =========================================================================

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static bool TrackDispatch(bool ok)
    {
        if (ok)
            LeptGpu.RecordDispatch();
        return ok;
    }

    /// <inheritdoc/>
    public bool TryBilateralGrayExact(Pix pixs, L_Kernel spatialKel, L_Kernel rangeKel, [NotNullWhen(true)] out Pix? pixd)
    {
        pixd = null;
        if (_disposed || !HasBitExactDeviceFloat || pixs.Depth != 8 || pixs.Colormap != null)
            return false;
        spatialKel.GetParameters(out int sy, out int sx, out _, out _);
        if (pixs.Width < 2 * sx + 1 || pixs.Height < 2 * sy + 1)
            return false;
        if (!MirroredBorderFits(spatialKel, pixs.Width, pixs.Height))
            return false;
        if (rangeKel.Sx != 256 || rangeKel.Sy != 1)
            return false;

        try {
            using GpuPix gSrc = Upload(pixs);
            using GpuPix gDst = BilateralExactGpu(gSrc, spatialKel, rangeKel);
            pixd = DownloadPlain(gDst);
            return TrackDispatch(true);
        } catch (Exception ex) {
            Log.Warning("IlGpuAccelerator", $"TryBilateralGrayExact fallback to CPU: {ex.Message}");
            pixd = null;
            return false;
        }
    }

    /// <inheritdoc/>
    public bool TryBilateralExact(Pix pixs, L_Kernel spatialKel, L_Kernel rangeKel, [NotNullWhen(true)] out Pix? pixd)
    {
        pixd = null;
        if (_disposed || !HasBitExactDeviceFloat || (pixs.Depth != 8 && pixs.Depth != 32) || pixs.Colormap != null)
            return false;
        spatialKel.GetParameters(out int sy, out int sx, out _, out _);
        if (pixs.Width < 2 * sx + 1 || pixs.Height < 2 * sy + 1)
            return false;
        if (!MirroredBorderFits(spatialKel, pixs.Width, pixs.Height))
            return false;
        if (rangeKel.Sx != 256 || rangeKel.Sy != 1)
            return false;

        try {
            using GpuPix gSrc = Upload(pixs);
            using GpuPix gDst = BilateralExactGpu(gSrc, spatialKel, rangeKel);
            pixd = DownloadPlain(gDst);
            return TrackDispatch(true);
        } catch (Exception ex) {
            Log.Warning("IlGpuAccelerator", $"TryBilateralExact fallback to CPU: {ex.Message}");
            pixd = null;
            return false;
        }
    }

    /// <inheritdoc/>
    public bool TryConvolve(Pix pixs, L_Kernel kel, int outdepth, int normflag, int sfx, int sfy, [NotNullWhen(true)] out Pix? pixd)
    {
        pixd = null;
        if (_disposed || !HasBitExactDeviceFloat || pixs.Colormap != null || sfx < 1 || sfy < 1)
            return false;
        if (pixs.Depth != 8 && pixs.Depth != 16 && pixs.Depth != 32)
            return false;
        if (outdepth != 8 && outdepth != 16 && outdepth != 32)
            return false;
        if (!MirroredBorderFits(kel, pixs.Width, pixs.Height))
            return false;

        try {
            using GpuPix gSrc = Upload(pixs);
            using GpuPix gDst = ConvolveGpu(gSrc, kel, outdepth, normflag, sfx, sfy);
            pixd = DownloadPlain(gDst);
            return TrackDispatch(true);
        } catch (Exception ex) {
            Log.Warning("IlGpuAccelerator", $"TryConvolve fallback to CPU: {ex.Message}");
            pixd = null;
            return false;
        }
    }

    /// <inheritdoc/>
    public bool TryConvolveRGB(Pix pixs, L_Kernel kel, int sfx, int sfy, [NotNullWhen(true)] out Pix? pixd)
    {
        pixd = null;
        if (_disposed || !HasBitExactDeviceFloat || pixs.Depth != 32 || pixs.Colormap != null || sfx < 1 || sfy < 1)
            return false;
        if (!MirroredBorderFits(kel, pixs.Width, pixs.Height))
            return false;

        try {
            using GpuPix gSrc = Upload(pixs);
            using GpuPix gDst = ConvolveRgbGpu(gSrc, kel, sfx, sfy);
            pixd = DownloadPlain(gDst);
            return TrackDispatch(true);
        } catch (Exception ex) {
            Log.Warning("IlGpuAccelerator", $"TryConvolveRGB fallback to CPU: {ex.Message}");
            pixd = null;
            return false;
        }
    }

    /// <inheritdoc/>
    public bool TryConvolveSep(Pix pixs, L_Kernel kelx, L_Kernel kely, int outdepth, int normflag, int sfx, int sfy, [NotNullWhen(true)] out Pix? pixd)
    {
        pixd = null;
        if (_disposed || !HasBitExactDeviceFloat || pixs.Colormap != null || sfx < 1 || sfy < 1)
            return false;
        if (pixs.Depth != 8 && pixs.Depth != 16 && pixs.Depth != 32)
            return false;
        if (outdepth != 8 && outdepth != 16 && outdepth != 32)
            return false;
        int wt = (pixs.Width + sfx - 1) / sfx;
        if (!MirroredBorderFits(kelx, pixs.Width, pixs.Height) || !MirroredBorderFits(kely, wt, pixs.Height))
            return false;

        try {
            using GpuPix gSrc = Upload(pixs);
            using GpuPix gDst = ConvolveSepGpu(gSrc, kelx, kely, outdepth, normflag, sfx, sfy);
            pixd = DownloadPlain(gDst);
            return TrackDispatch(true);
        } catch (Exception ex) {
            Log.Warning("IlGpuAccelerator", $"TryConvolveSep fallback to CPU: {ex.Message}");
            pixd = null;
            return false;
        }
    }

    /// <inheritdoc/>
    public bool TryConvolveRGBSep(Pix pixs, L_Kernel kelx, L_Kernel kely, int sfx, int sfy, [NotNullWhen(true)] out Pix? pixd)
    {
        pixd = null;
        if (_disposed || !HasBitExactDeviceFloat || pixs.Depth != 32 || pixs.Colormap != null || sfx < 1 || sfy < 1)
            return false;
        int wt = (pixs.Width + sfx - 1) / sfx;
        if (!MirroredBorderFits(kelx, pixs.Width, pixs.Height) || !MirroredBorderFits(kely, wt, pixs.Height))
            return false;

        try {
            using GpuPix gSrc = Upload(pixs);
            using GpuPix gDst = ConvolveRgbSepGpu(gSrc, kelx, kely, sfx, sfy);
            pixd = DownloadPlain(gDst);
            return TrackDispatch(true);
        } catch (Exception ex) {
            Log.Warning("IlGpuAccelerator", $"TryConvolveRGBSep fallback to CPU: {ex.Message}");
            pixd = null;
            return false;
        }
    }

    /// <inheritdoc/>
    public bool TryRankFilterGray(Pix pixs, int wf, int hf, float rank, [NotNullWhen(true)] out Pix? pixd)
    {
        pixd = null;
        if (_disposed || pixs.Depth != 8 || pixs.Colormap != null)
            return false;
        if (wf < 1 || hf < 1 || rank < 0.0f || rank > 1.0f)
            return false;
        if (wf / 2 > pixs.Width || hf / 2 > pixs.Height)
            return false;

        try {
            using GpuPix gSrc = Upload(pixs);
            using GpuPix gDst = RankFilterGpu(gSrc, wf, hf, rank);
            pixd = DownloadAsTemplate(gDst, pixs);
            return TrackDispatch(true);
        } catch (Exception ex) {
            Log.Warning("IlGpuAccelerator", $"TryRankFilterGray fallback to CPU: {ex.Message}");
            pixd = null;
            return false;
        }
    }

    /// <inheritdoc/>
    public bool TryRankFilterRGB(Pix pixs, int wf, int hf, float rank, [NotNullWhen(true)] out Pix? pixd)
    {
        pixd = null;
        if (_disposed || pixs.Depth != 32 || pixs.Colormap != null)
            return false;
        if (wf < 1 || hf < 1 || rank < 0.0f || rank > 1.0f)
            return false;
        if (wf / 2 > pixs.Width || hf / 2 > pixs.Height)
            return false;

        try {
            using GpuPix gSrc = Upload(pixs);
            using GpuPix gDst = RankFilterGpu(gSrc, wf, hf, rank);
            pixd = gDst.Download();
            return TrackDispatch(true);
        } catch (Exception ex) {
            Log.Warning("IlGpuAccelerator", $"TryRankFilterRGB fallback to CPU: {ex.Message}");
            pixd = null;
            return false;
        }
    }

    /// <inheritdoc/>
    public bool TryMorphGrayBrick(Pix pixs, int hsize, int vsize, bool dilate, [NotNullWhen(true)] out Pix? pixd)
    {
        pixd = null;
        if (_disposed || pixs.Depth != 8 || pixs.Colormap != null || hsize < 1 || vsize < 1)
            return false;

        try {
            using GpuPix gSrc = Upload(pixs);
            using GpuPix gDst = MorphBrickGpu(gSrc, hsize, vsize, dilate);
            pixd = DownloadAsTemplate(gDst, pixs);
            return TrackDispatch(true);
        } catch (Exception ex) {
            Log.Warning("IlGpuAccelerator", $"TryMorphGrayBrick fallback to CPU: {ex.Message}");
            pixd = null;
            return false;
        }
    }

    /// <inheritdoc/>
    public bool TryOpenCloseGrayBrick(Pix pixs, int hsize, int vsize, bool open, [NotNullWhen(true)] out Pix? pixd)
    {
        pixd = null;
        if (_disposed || pixs.Depth != 8 || pixs.Colormap != null || hsize < 1 || vsize < 1)
            return false;

        try {
            using GpuPix gSrc = Upload(pixs);
            using GpuPix gDst = open ? gSrc.OpenBrick(hsize, vsize) : gSrc.CloseBrick(hsize, vsize);
            pixd = DownloadAsTemplate(gDst, pixs);
            return TrackDispatch(true);
        } catch (Exception ex) {
            Log.Warning("IlGpuAccelerator", $"TryOpenCloseGrayBrick fallback to CPU: {ex.Message}");
            pixd = null;
            return false;
        }
    }

    /// <inheritdoc/>
    public bool TryColorMorph(Pix pixs, int type, int hsize, int vsize, [NotNullWhen(true)] out Pix? pixd)
    {
        pixd = null;
        if (_disposed || pixs.Depth != 32 || pixs.Colormap != null || hsize < 1 || vsize < 1)
            return false;

        try {
            using GpuPix gSrc = Upload(pixs);
            using GpuPix gDst = type switch {
                L.L_MORPH_DILATE => gSrc.DilateBrick(hsize, vsize),
                L.L_MORPH_ERODE => gSrc.ErodeBrick(hsize, vsize),
                L.L_MORPH_OPEN => gSrc.OpenBrick(hsize, vsize),
                L.L_MORPH_CLOSE => gSrc.CloseBrick(hsize, vsize),
                _ => throw new ArgumentOutOfRangeException(nameof(type)),
            };
            pixd = gDst.Download();
            return TrackDispatch(true);
        } catch (Exception ex) {
            Log.Warning("IlGpuAccelerator", $"TryColorMorph fallback to CPU: {ex.Message}");
            pixd = null;
            return false;
        }
    }

    // =========================================================================
    // Partial hooks for Batch 10 Geometry & OCR Kernels
    // =========================================================================

    partial void DisposeGeometryKernels();

    /// <inheritdoc/>
    public bool TryAffine(Pix pixs, float[] vc, int incolor, bool sampled, [NotNullWhen(true)] out Pix? pixd) =>
        TrackDispatch(TryAffineCore(pixs, vc, incolor, sampled, out pixd));

    /// <inheritdoc/>
    public bool TryBilinear(Pix pixs, float[] vc, int incolor, bool sampled, [NotNullWhen(true)] out Pix? pixd) =>
        TrackDispatch(TryBilinearCore(pixs, vc, incolor, sampled, out pixd));

    /// <inheritdoc/>
    public bool TryProjective(Pix pixs, float[] vc, int incolor, bool sampled, [NotNullWhen(true)] out Pix? pixd) =>
        TrackDispatch(TryProjectiveCore(pixs, vc, incolor, sampled, out pixd));

    /// <inheritdoc/>
    public bool TryRotateAM(Pix pixs, float angle, int incolor, [NotNullWhen(true)] out Pix? pixd) =>
        TrackDispatch(TryRotateAMCore(pixs, angle, incolor, out pixd));

    /// <inheritdoc/>
    public bool TryScaleLI(Pix pixs, float scalex, float scaley, [NotNullWhen(true)] out Pix? pixd) =>
        TrackDispatch(TryScaleLICore(pixs, scalex, scaley, out pixd));

    /// <inheritdoc/>
    public bool TryDewarpApplyDisparity(Pix pixs, FPix? vdisp, FPix? hdisp, int grayin, [NotNullWhen(true)] out Pix? pixd) =>
        TrackDispatch(TryDewarpApplyDisparityCore(pixs, vdisp, hdisp, grayin, out pixd));

    /// <inheritdoc/>
    public bool TrySauvolaGetThreshold(Pix pixm, Pix pixms, float factor, bool wantsd, [NotNullWhen(true)] out Pix? pixd, out Pix? pixsd) =>
        TrackDispatch(TrySauvolaGetThresholdCore(pixm, pixms, factor, wantsd, out pixd, out pixsd));

    /// <inheritdoc/>
    public bool TryApplyLocalThreshold(Pix pixs, Pix pixth, [NotNullWhen(true)] out Pix? pixd) =>
        TrackDispatch(TryApplyLocalThresholdCore(pixs, pixth, out pixd));

    /// <inheritdoc/>
    public bool TryFindSkewSweep(Pix pixs, int reduction, float sweepRange, float sweepDelta, out float angle) =>
        TrackDispatch(TryFindSkewSweepCore(pixs, reduction, sweepRange, sweepDelta, out angle));

    /// <inheritdoc/>
    public bool TryBestCorrelation(
        Pix pix1, Pix pix2, int area1, int area2,
        int etransx, int etransy, int maxshift, int[]? tab8,
        out int delx, out int dely, out float score) =>
        TrackDispatch(TryBestCorrelationCore(pix1, pix2, area1, area2, etransx, etransy, maxshift, tab8, out delx, out dely, out score));

    /// <summary>Releases the ILGPU context, the accelerator, the buffer pool and all compiled kernels.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (ReferenceEquals(LeptGpu.Accelerator, this))
            LeptGpu.Accelerator = null;
        DisposeGeometryKernels();
        _pool.Dispose();
        _accelerator.Dispose();
        _context.Dispose();
    }
}
