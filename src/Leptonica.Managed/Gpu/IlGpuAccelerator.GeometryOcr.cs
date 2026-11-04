using System;
using System.Diagnostics.CodeAnalysis;
using ILGPU;
using ILGPU.Runtime;
using Leptonica.Gpu.Kernels;

namespace Leptonica.Gpu;

public sealed partial class IlGpuAccelerator
{
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, GeometryOcrKernels.WarpParams>? _warpBinaryKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, GeometryOcrKernels.WarpParams>? _warpGrayKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, GeometryOcrKernels.WarpParams>? _warpColorKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, int, int, int, int, float, float, int>? _rotateAMGrayKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, int, int, int, int, float, float, uint>? _rotateAMColorKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, int, int, int, int, int, float, float>? _scaleLIGrayKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, int, int, int, int, int, float, float>? _scaleLIColorKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, GeometryOcrKernels.DewarpParams>? _dewarpBinaryKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, GeometryOcrKernels.DewarpParams>? _dewarpGrayKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, GeometryOcrKernels.DewarpParams>? _dewarpColorKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, int, int, int, int, int, float>? _sauvolaThreshKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, int, int, int, int>? _applyLocalThreshKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, int, int, int, int, int>? _skewRowCountsKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, int>? _skewVarReduceKernel;
    private Action<Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>, int, int, int, int, int, int, int, int, int>? _bestCorrelKernel;

    // =========================================================================
    // VRAM-Resident Geometry & OCR Operations
    // =========================================================================

    internal GpuPix WarpGpu(GpuPix src, float[] vc, int mode, int incolor, bool sampled)
    {
        ArgumentNullException.ThrowIfNull(vc);
        int reqLen = mode == GeometryOcrKernels.WarpModeAffine ? 6 : 8;
        if (vc.Length < reqLen)
            throw new ArgumentException($"Transform coefficient vector must have at least {reqLen} elements.", nameof(vc));
        if (src.Depth != 1 && src.Depth != 8 && src.Depth != 32)
            throw new ArgumentException("WarpGpu requires 1, 8, or 32 bpp.", nameof(src));

        bool useSampled = sampled || src.Depth == 1;
        uint fillVal;
        if (src.Depth == 1) {
            fillVal = incolor == L.L_BRING_IN_WHITE ? 0u : 1u;
        } else if (src.Depth == 8) {
            fillVal = incolor == L.L_BRING_IN_WHITE ? 255u : 0u;
        } else {
            fillVal = incolor == L.L_BRING_IN_WHITE ? (useSampled ? 0xffffffffu : 0xffffff00u) : 0u;
        }

        var p = new GeometryOcrKernels.WarpParams(
            src.Width, src.Height, src.Wpl, src.Wpl,
            mode, useSampled ? 1 : 0, fillVal,
            vc[0], vc[1], vc[2], vc[3], vc[4], vc[5],
            reqLen > 6 ? vc[6] : 0f, reqLen > 7 ? vc[7] : 0f);

        int words = src.Wpl * src.Height;
        PooledGpuBuffer<uint> dstBuf = _pool.RentUInt32(words);
        try {
            lock (_sync) {
                if (src.Depth == 1) {
                    _warpBinaryKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                        Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                        GeometryOcrKernels.WarpParams>(GeometryOcrKernels.WarpBinaryWordKernel);
                    _warpBinaryKernel(words, src.View, dstBuf.View, p);
                } else if (src.Depth == 8) {
                    _warpGrayKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                        Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                        GeometryOcrKernels.WarpParams>(GeometryOcrKernels.WarpGrayWordKernel);
                    _warpGrayKernel(words, src.View, dstBuf.View, p);
                } else {
                    _warpColorKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                        Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                        GeometryOcrKernels.WarpParams>(GeometryOcrKernels.WarpColorKernel);
                    _warpColorKernel(src.Width * src.Height, src.View, dstBuf.View, p);
                }
                _accelerator.Synchronize();
            }
            return new GpuPix(this, dstBuf, src.Width, src.Height, src.Depth, src.Wpl, src.Spp, src.XRes, src.YRes, src.Informat);
        } catch {
            dstBuf.Dispose();
            throw;
        }
    }

    internal GpuPix RotateAMGpu(GpuPix src, float angle, int incolor)
    {
        if (src.Depth != 8 && src.Depth != 32)
            throw new ArgumentException("RotateAMGpu requires 8 or 32 bpp.", nameof(src));
        if (MathF.Abs(angle) < 0.001f || src.Width < 2 || src.Height < 2)
            return CloneGpuPix(src);

        float sina = (float)(16.0 * Math.Sin(angle));
        float cosa = (float)(16.0 * Math.Cos(angle));
        int words = src.Wpl * src.Height;
        PooledGpuBuffer<uint> dstBuf = _pool.RentUInt32(words);
        try {
            lock (_sync) {
                if (src.Depth == 8) {
                    int fillGray = incolor == L.L_BRING_IN_WHITE ? 255 : 0;
                    _rotateAMGrayKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                        Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                        int, int, int, int, float, float, int>(GeometryOcrKernels.RotateAMGrayWordKernel);
                    _rotateAMGrayKernel(words, src.View, dstBuf.View, src.Width, src.Height, src.Wpl, src.Wpl, sina, cosa, fillGray);
                } else {
                    uint fillColor = incolor == L.L_BRING_IN_WHITE ? 0xffffff00u : 0u;
                    _rotateAMColorKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                        Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                        int, int, int, int, float, float, uint>(GeometryOcrKernels.RotateAMColorKernel);
                    _rotateAMColorKernel(src.Width * src.Height, src.View, dstBuf.View, src.Width, src.Height, src.Wpl, src.Wpl, sina, cosa, fillColor);
                }
                _accelerator.Synchronize();
            }
            return new GpuPix(this, dstBuf, src.Width, src.Height, src.Depth, src.Wpl, src.Depth == 32 ? 3 : 1, src.XRes, src.YRes, src.Informat);
        } catch {
            dstBuf.Dispose();
            throw;
        }
    }

    private static int ScaleResolutionComponent(int res, float scale)
    {
        const double maxRes = 100000000.0;
        double scaled = (double)scale * (float)res + 0.5;
        return (int)(uint)Math.Min(scaled, maxRes);
    }

    private static bool IsProjectiveDenomNonZeroOnRect(float vc6, float vc7, int w, int h)
    {
        if (w <= 0 || h <= 0) return true;
        float xm = w - 1;
        float ym = h - 1;
        float c00 = 1.0f;
        float c10 = vc6 * xm + 1.0f;
        float c01 = vc7 * ym + 1.0f;
        float c11 = vc6 * xm + vc7 * ym + 1.0f;
        const float eps = 1e-5f;
        return (c00 > eps && c10 > eps && c01 > eps && c11 > eps) ||
               (c00 < -eps && c10 < -eps && c01 < -eps && c11 < -eps);
    }

    private static float NormalizeAngleForShearHost(float radang, float mindif)
    {
        const float pi2 = 3.14159265f / 2.0f;
        if (radang < -pi2 || radang > pi2)
            radang = radang - (int)(radang / pi2) * pi2;
        if (radang > pi2 - mindif)
            radang = pi2 - mindif;
        else if (radang < -pi2 + mindif)
            radang = -pi2 + mindif;
        return radang;
    }

    internal GpuPix ScaleLIGpu(GpuPix src, float scalex, float scaley)
    {
        if (src.Depth != 8 && src.Depth != 32)
            throw new ArgumentException("ScaleLIGpu requires 8 or 32 bpp.", nameof(src));
        if (scalex <= 0f || scaley <= 0f)
            throw new ArgumentOutOfRangeException(nameof(scalex));

        int wd = Math.Max(1, (int)(scalex * (float)src.Width + 0.5));
        int hd = Math.Max(1, (int)(scaley * (float)src.Height + 0.5));
        int wpld = ComputeWpl(wd, src.Depth);
        float scx = 16.0f * src.Width / wd;
        float scy = 16.0f * src.Height / hd;

        PooledGpuBuffer<uint> dstBuf = _pool.RentUInt32(wpld * hd);
        try {
            lock (_sync) {
                if (src.Depth == 8) {
                    _scaleLIGrayKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                        Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                        int, int, int, int, int, float, float>(GeometryOcrKernels.ScaleLIGrayWordKernel);
                    _scaleLIGrayKernel(wpld * hd, src.View, dstBuf.View, src.Width, src.Height, src.Wpl, wd, wpld, scx, scy);
                } else {
                    _scaleLIColorKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                        Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                        int, int, int, int, int, float, float>(GeometryOcrKernels.ScaleLIColorKernel);
                    _scaleLIColorKernel(wd * hd, src.View, dstBuf.View, src.Width, src.Height, src.Wpl, wd, wpld, scx, scy);
                }
                _accelerator.Synchronize();
            }
            int xres = ScaleResolutionComponent(src.XRes, scalex);
            int yres = ScaleResolutionComponent(src.YRes, scaley);
            return new GpuPix(this, dstBuf, wd, hd, src.Depth, wpld, src.Depth == 32 ? 3 : 1, xres, yres, src.Informat);
        } catch {
            dstBuf.Dispose();
            throw;
        }
    }

    internal GpuPix DewarpApplyDisparityGpu(GpuPix src, FPix vdisp, FPix? hdisp, int grayin)
    {
        ArgumentNullException.ThrowIfNull(vdisp);
        if (src.Depth != 1 && src.Depth != 8 && src.Depth != 32)
            throw new ArgumentException("DewarpApplyDisparityGpu requires 1, 8, or 32 bpp.", nameof(src));

        vdisp.GetDimensions(out int vfw, out int vfh);
        if (vfw < src.Width || vfh < src.Height)
            throw new ArgumentException("vdisp dimensions smaller than src.", nameof(vdisp));

        bool hasHoriz = hdisp != null;
        int wplh = 0;
        if (hasHoriz) {
            hdisp!.GetDimensions(out int hfw, out int hfh);
            if (hfw < src.Width || hfh < src.Height)
                throw new ArgumentException("hdisp dimensions smaller than src.", nameof(hdisp));
            wplh = hdisp.Wpl;
        }

        var p = new GeometryOcrKernels.DewarpParams(
            src.Width, src.Height, src.Wpl, src.Wpl,
            vdisp.Wpl, wplh, hasHoriz ? 1 : 0, grayin);

        int words = src.Wpl * src.Height;
        PooledGpuBuffer<uint> dstBuf = _pool.RentUInt32(words);
        try {
            lock (_sync) {
                using PooledGpuBuffer<float> vBuf = _pool.RentFloat32(vdisp.Data.Length);
                CopyFromHost(vBuf.View, vdisp.Data, vdisp.Data.Length);
                using PooledGpuBuffer<float> hBuf = _pool.RentFloat32(hasHoriz ? hdisp!.Data.Length : 1);
                if (hasHoriz)
                    CopyFromHost(hBuf.View, hdisp!.Data, hdisp.Data.Length);

                if (src.Depth == 1) {
                    _dewarpBinaryKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                        Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                        ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                        GeometryOcrKernels.DewarpParams>(GeometryOcrKernels.DewarpBinaryWordKernel);
                    _dewarpBinaryKernel(words, src.View, dstBuf.View, vBuf.View, hBuf.View, p);
                } else if (src.Depth == 8) {
                    _dewarpGrayKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                        Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                        ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                        GeometryOcrKernels.DewarpParams>(GeometryOcrKernels.DewarpGrayWordKernel);
                    _dewarpGrayKernel(words, src.View, dstBuf.View, vBuf.View, hBuf.View, p);
                } else {
                    _dewarpColorKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                        Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                        ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                        GeometryOcrKernels.DewarpParams>(GeometryOcrKernels.DewarpColorKernel);
                    _dewarpColorKernel(src.Width * src.Height, src.View, dstBuf.View, vBuf.View, hBuf.View, p);
                }
                _accelerator.Synchronize();
            }
            return new GpuPix(this, dstBuf, src.Width, src.Height, src.Depth, src.Wpl, src.Spp, src.XRes, src.YRes, src.Informat);
        } catch {
            dstBuf.Dispose();
            throw;
        }
    }

    internal GpuPix ApplyLocalThresholdGpu(GpuPix src, GpuPix threshMap)
    {
        if (src.Depth != 8 || threshMap.Depth != 8)
            throw new ArgumentException("ApplyLocalThresholdGpu requires 8 bpp source and threshold map.");
        if (src.Width != threshMap.Width || src.Height != threshMap.Height)
            throw new ArgumentException("Source and threshold map dimensions must match.");

        int wpld = ComputeWpl(src.Width, 1);
        int words = wpld * src.Height;
        PooledGpuBuffer<uint> dstBuf = _pool.RentUInt32(words);
        try {
            lock (_sync) {
                _applyLocalThreshKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                    Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                    ArrayView1D<uint, Stride1D.Dense>, int, int, int, int>(GeometryOcrKernels.ApplyLocalThresholdWordKernel);
                _applyLocalThreshKernel(words, src.View, threshMap.View, dstBuf.View, src.Width, src.Wpl, threshMap.Wpl, wpld);
                _accelerator.Synchronize();
            }
            return new GpuPix(this, dstBuf, src.Width, src.Height, 1, wpld, 1, 0, 0, 0);
        } catch {
            dstBuf.Dispose();
            throw;
        }
    }

    // =========================================================================
    // IGpuAccelerator Contract — Batch 10 Implementations
    // =========================================================================

    private bool TryAffineCore(Pix pixs, float[] vc, int incolor, bool sampled, [NotNullWhen(true)] out Pix? pixd) =>
        TryWarpCommon(pixs, vc, GeometryOcrKernels.WarpModeAffine, incolor, sampled, out pixd);

    private bool TryBilinearCore(Pix pixs, float[] vc, int incolor, bool sampled, [NotNullWhen(true)] out Pix? pixd) =>
        TryWarpCommon(pixs, vc, GeometryOcrKernels.WarpModeBilinear, incolor, sampled, out pixd);

    private bool TryProjectiveCore(Pix pixs, float[] vc, int incolor, bool sampled, [NotNullWhen(true)] out Pix? pixd)
    {
        pixd = null;
        if (vc == null || vc.Length < 8 || !IsProjectiveDenomNonZeroOnRect(vc[6], vc[7], pixs.Width, pixs.Height))
            return false;
        return TryWarpCommon(pixs, vc, GeometryOcrKernels.WarpModeProjective, incolor, sampled, out pixd);
    }

    private bool TryWarpCommon(Pix pixs, float[] vc, int mode, int incolor, bool sampled, [NotNullWhen(true)] out Pix? pixd)
    {
        pixd = null;
        if (_disposed || !HasBitExactDeviceFloat || pixs.Colormap != null || pixs.Spp == 4)
            return false;
        if (pixs.Depth != 1 && pixs.Depth != 8 && pixs.Depth != 32)
            return false;
        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            return false;

        try {
            using GpuPix gSrc = Upload(pixs);
            using GpuPix gDst = WarpGpu(gSrc, vc, mode, incolor, sampled);
            pixd = DownloadAsTemplate(gDst, pixs);
            return true;
        } catch (Exception ex) {
            Log.Warning("IlGpuAccelerator", $"TryWarpCommon fallback to CPU: {ex.Message}");
            pixd = null;
            return false;
        }
    }

    private bool TryRotateAMCore(Pix pixs, float angle, int incolor, [NotNullWhen(true)] out Pix? pixd)
    {
        pixd = null;
        if (_disposed || !HasBitExactDeviceFloat || pixs.Colormap != null || pixs.Spp == 4)
            return false;
        if (pixs.Depth != 8 && pixs.Depth != 32)
            return false;
        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            return false;
        if (Math.Abs(angle) >= 0.001f && (pixs.Width < 2 || pixs.Height < 2))
            return false;

        try {
            using GpuPix gSrc = Upload(pixs);
            using GpuPix gDst = RotateAMGpu(gSrc, angle, incolor);
            pixd = DownloadAsTemplate(gDst, pixs);
            return true;
        } catch (Exception ex) {
            Log.Warning("IlGpuAccelerator", $"TryRotateAM fallback to CPU: {ex.Message}");
            pixd = null;
            return false;
        }
    }

    private bool TryScaleLICore(Pix pixs, float scalex, float scaley, [NotNullWhen(true)] out Pix? pixd)
    {
        pixd = null;
        if (_disposed || !HasBitExactDeviceFloat || pixs.Colormap != null || pixs.Spp == 4)
            return false;
        if (pixs.Depth != 8 && pixs.Depth != 32)
            return false;
        if (scalex <= 0f || scaley <= 0f || pixs.Width < 2 || pixs.Height < 2)
            return false;

        try {
            using GpuPix gSrc = Upload(pixs);
            using GpuPix gDst = ScaleLIGpu(gSrc, scalex, scaley);
            Pix dst = gDst.Download();
            dst.CopyResolution(pixs);
            dst.ScaleResolution(scalex, scaley);
            dst.CopyInputFormat(pixs);
            if (pixs.Depth == 8)
                dst.CopyText(pixs);
            pixd = dst;
            return true;
        } catch (Exception ex) {
            Log.Warning("IlGpuAccelerator", $"TryScaleLI fallback to CPU: {ex.Message}");
            pixd = null;
            return false;
        }
    }

    private bool TryDewarpApplyDisparityCore(Pix pixs, FPix? vdisp, FPix? hdisp, int grayin, [NotNullWhen(true)] out Pix? pixd)
    {
        pixd = null;
        if (_disposed || !HasBitExactDeviceFloat || vdisp == null || pixs.Colormap != null)
            return false;
        if (pixs.Depth != 1 && pixs.Depth != 8 && pixs.Depth != 32)
            return false;

        try {
            using GpuPix gSrc = Upload(pixs);
            using GpuPix gDst = DewarpApplyDisparityGpu(gSrc, vdisp, hdisp, grayin);
            pixd = DownloadAsTemplate(gDst, pixs);
            return true;
        } catch (Exception ex) {
            Log.Warning("IlGpuAccelerator", $"TryDewarpApplyDisparity fallback to CPU: {ex.Message}");
            pixd = null;
            return false;
        }
    }

    private bool TrySauvolaGetThresholdCore(Pix pixm, Pix pixms, float factor, bool wantsd, [NotNullWhen(true)] out Pix? pixd, out Pix? pixsd)
    {
        pixd = null;
        pixsd = null;
        if (_disposed || !HasBitExactDeviceFloat || pixm.Depth != 8 || pixms.Depth != 32)
            return false;
        if (pixm.Colormap != null || pixms.Colormap != null || pixm.Width != pixms.Width || pixm.Height != pixms.Height || factor < 0f)
            return false;

        int w = pixm.Width;
        int h = pixm.Height;
        int wpld = pixm.Wpl;
        int words = wpld * h;

        try {
            using GpuPix gm = Upload(pixm);
            using GpuPix gms = Upload(pixms);
            using PooledGpuBuffer<uint> dBuf = _pool.RentUInt32(words);
            using PooledGpuBuffer<uint> sdBuf = _pool.RentUInt32(wantsd ? words : 1);

            lock (_sync) {
                _sauvolaThreshKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                    Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                    ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                    int, int, int, int, int, float>(GeometryOcrKernels.SauvolaThresholdWordKernel);
                _sauvolaThreshKernel(words, gm.View, gms.View, dBuf.View, sdBuf.View, w, gm.Wpl, gms.Wpl, wpld, wantsd ? 1 : 0, factor);
                _accelerator.Synchronize();
            }

            Pix thPix = Pix.CreateNoInit(w, h, 8);
            DownloadWords(dBuf.View, thPix.Data, words);
            if (wantsd) {
                Pix sPix = Pix.CreateNoInit(w, h, 8);
                DownloadWords(sdBuf.View, sPix.Data, words);
                pixsd = sPix;
            }
            pixd = thPix;
            return true;
        } catch (Exception ex) {
            Log.Warning("IlGpuAccelerator", $"TrySauvolaGetThreshold fallback to CPU: {ex.Message}");
            pixd = null;
            pixsd = null;
            return false;
        }
    }

    private bool TryApplyLocalThresholdCore(Pix pixs, Pix pixth, [NotNullWhen(true)] out Pix? pixd)
    {
        pixd = null;
        if (_disposed || pixs.Depth != 8 || pixs.Colormap != null || pixth.Depth != 8 || pixth.Colormap != null)
            return false;
        if (pixs.Width != pixth.Width || pixs.Height != pixth.Height)
            return false;

        try {
            using GpuPix gSrc = Upload(pixs);
            using GpuPix gTh = Upload(pixth);
            using GpuPix gDst = ApplyLocalThresholdGpu(gSrc, gTh);
            pixd = gDst.Download();
            return true;
        } catch (Exception ex) {
            Log.Warning("IlGpuAccelerator", $"TryApplyLocalThreshold fallback to CPU: {ex.Message}");
            pixd = null;
            return false;
        }
    }

    private bool TryFindSkewSweepCore(Pix pixs, int reduction, float sweepRange, float sweepDelta, out float angle)
    {
        angle = 0f;
        if (_disposed || !HasBitExactDeviceFloat || pixs.Depth != 1 || pixs.Colormap != null || sweepDelta <= 0f)
            return false;
        if (reduction != 1 && reduction != 2 && reduction != 4 && reduction != 8)
            return false;

        try {
            Pix pix = reduction switch {
                1 => pixs,
                2 => pixs.ReduceRankBinaryCascade(1, 0, 0, 0),
                4 => pixs.ReduceRankBinaryCascade(1, 1, 0, 0),
                _ => pixs.ReduceRankBinaryCascade(1, 1, 2, 0),
            };

            if (pix.Zero())
                return false;

            int w = pix.Width;
            int h = pix.Height;
            int skiph = (int)(0.05 * w);
            int skip = Math.Min(h / 10, skiph);
            int nskip = Math.Max(skip / 2, 1);
            int endRow = h - nskip;
            if (nskip >= endRow)
                return false;

            int yMin = nskip - 1;
            int nRows = endRow - nskip + 1;
            int nangles = (int)((2.0 * sweepRange) / sweepDelta + 1);
            if (nangles <= 0)
                return false;

            const float deg2rad = 3.1415926535f / 180.0f;
            const float minDiffFromHalfPi = 0.04f;
            float[] invAngles = new float[nangles];
            uint[] signs = new uint[nangles];
            for (int a = 0; a < nangles; a++) {
                float theta = -sweepRange + a * sweepDelta;
                float rad = NormalizeAngleForShearHost(deg2rad * theta, minDiffFromHalfPi);
                double tanVal = Math.Tan(rad);
                if (rad == 0f || tanVal == 0.0) {
                    signs[a] = 0;
                    invAngles[a] = 0f;
                } else {
                    float tanAngle = (float)tanVal;
                    signs[a] = unchecked((uint)(rad < 0f ? -1 : 1));
                    invAngles[a] = (float)Math.Abs(1.0 / tanAngle);
                }
            }

            float[] scores = new float[nangles];
            using GpuPix gPix = Upload(pix);
            lock (_sync) {
                using PooledGpuBuffer<float> invBuf = UploadFloatArrayLocked(invAngles);
                using PooledGpuBuffer<uint> signBuf = _pool.RentUInt32(nangles);
                CopyFromHost(signBuf.View, signs, nangles);
                using PooledGpuBuffer<uint> countsBuf = _pool.RentUInt32((long)nangles * nRows);
                using PooledGpuBuffer<float> scoreBuf = _pool.RentFloat32(nangles);

                _skewRowCountsKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                    Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                    ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                    int, int, int, int, int>(GeometryOcrKernels.SkewSweepRowCountsKernel);
                _skewVarReduceKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                    Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, int>(
                    GeometryOcrKernels.SkewSweepVarianceReduceKernel);

                _skewRowCountsKernel(nangles * nRows, gPix.View, invBuf.View, signBuf.View, countsBuf.View, w, h, gPix.Wpl, yMin, nRows);
                _skewVarReduceKernel(nangles, countsBuf.View, scoreBuf.View, nRows);
                CopyToHost(scoreBuf.View, scores, nangles);
                _accelerator.Synchronize();
            }

            Numa natheta = Numa.Create(nangles);
            Numa nascore = Numa.Create(nangles);
            for (int i = 0; i < nangles; i++) {
                natheta.AddNumber(-sweepRange + i * sweepDelta);
                nascore.AddNumber(scores[i]);
            }
            nascore.FitMax(out _, natheta, out float maxAngle);
            angle = maxAngle;
            return true;
        } catch (Exception ex) {
            Log.Warning("IlGpuAccelerator", $"TryFindSkewSweep fallback to CPU: {ex.Message}");
            angle = 0f;
            return false;
        }
    }

    private bool TryBestCorrelationCore(
        Pix pix1, Pix pix2, int area1, int area2,
        int etransx, int etransy, int maxshift, int[]? tab8,
        out int delx, out int dely, out float score)
    {
        _ = tab8;
        delx = 0;
        dely = 0;
        score = 0f;
        if (_disposed || pix1.Depth != 1 || pix2.Depth != 1 || area1 <= 0 || area2 <= 0 || maxshift < 0)
            return false;

        try {
            int gridSide = 2 * maxshift + 1;
            int totalShifts = gridSide * gridSide;
            int baseDelX = etransx - maxshift;
            int baseDelY = etransy - maxshift;
            uint[] counts = new uint[totalShifts];

            using GpuPix g1 = Upload(pix1);
            using GpuPix g2 = Upload(pix2);
            lock (_sync) {
                using PooledGpuBuffer<uint> outBuf = _pool.RentUInt32(totalShifts);
                _bestCorrelKernel ??= _accelerator.LoadAutoGroupedStreamKernel<
                    Index1D, ArrayView1D<uint, Stride1D.Dense>, ArrayView1D<uint, Stride1D.Dense>,
                    ArrayView1D<uint, Stride1D.Dense>, int, int, int, int, int, int, int, int, int>(
                    GeometryOcrKernels.BestCorrelationShiftKernel);
                _bestCorrelKernel(
                    totalShifts, g1.View, g2.View, outBuf.View,
                    pix1.Width, pix1.Height, pix1.Wpl,
                    pix2.Width, pix2.Height, pix2.Wpl,
                    baseDelX, baseDelY, gridSide);
                CopyToHost(outBuf.View, counts, totalShifts);
                _accelerator.Synchronize();
            }

            // Match Compare.cs & CorrelScore.cs single-precision score evaluation and raster tie-breaking order
            float denom = (float)area1 * (float)area2;
            float maxScore = 0f;
            int bestDx = etransx;
            int bestDy = etransy;
            for (int sy = 0; sy < gridSide; sy++) {
                for (int sx = 0; sx < gridSide; sx++) {
                    float cf = (float)(int)counts[sy * gridSide + sx];
                    float sc = cf * cf / denom;
                    if (sc > maxScore) {
                        maxScore = sc;
                        bestDx = baseDelX + sx;
                        bestDy = baseDelY + sy;
                    }
                }
            }

            delx = bestDx;
            dely = bestDy;
            score = maxScore;
            return true;
        } catch (Exception ex) {
            Log.Warning("IlGpuAccelerator", $"TryBestCorrelation fallback to CPU: {ex.Message}");
            delx = 0;
            dely = 0;
            score = 0f;
            return false;
        }
    }
}
