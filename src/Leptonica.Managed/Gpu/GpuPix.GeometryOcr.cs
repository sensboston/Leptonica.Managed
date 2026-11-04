using System;
using Leptonica.Gpu.Kernels;

namespace Leptonica.Gpu;

internal sealed partial class GpuPix
{
    /// <summary>Executes backward-mapped affine warp in VRAM.</summary>
    public GpuPix Affine(float[] vc, int incolor = L.L_BRING_IN_WHITE, bool sampled = false) =>
        Accelerator.WarpGpu(this, vc, GeometryOcrKernels.WarpModeAffine, incolor, sampled);

    /// <summary>Executes backward-mapped bilinear warp in VRAM.</summary>
    public GpuPix Bilinear(float[] vc, int incolor = L.L_BRING_IN_WHITE, bool sampled = false) =>
        Accelerator.WarpGpu(this, vc, GeometryOcrKernels.WarpModeBilinear, incolor, sampled);

    /// <summary>Executes backward-mapped projective warp in VRAM.</summary>
    public GpuPix Projective(float[] vc, int incolor = L.L_BRING_IN_WHITE, bool sampled = false) =>
        Accelerator.WarpGpu(this, vc, GeometryOcrKernels.WarpModeProjective, incolor, sampled);

    /// <summary>Executes area-mapped center rotation (8 or 32 bpp) in VRAM.</summary>
    public GpuPix RotateAM(float angle, int incolor = L.L_BRING_IN_WHITE) =>
        Accelerator.RotateAMGpu(this, angle, incolor);

    /// <summary>Executes linear-interpolated scaling (8 or 32 bpp) in VRAM.</summary>
    public GpuPix ScaleLI(float scalex, float scaley) =>
        Accelerator.ScaleLIGpu(this, scalex, scaley);

    /// <summary>Executes fused single-pass vertical + optional horizontal disparity dewarping in VRAM.</summary>
    public GpuPix ApplyDisparity(FPix vdisp, FPix? hdisp = null, int grayin = 255) =>
        Accelerator.DewarpApplyDisparityGpu(this, vdisp, hdisp, grayin);

    /// <summary>Thresholds an 8 bpp VRAM image against a local 8 bpp threshold map into a 1 bpp <see cref="GpuPix"/>.</summary>
    public GpuPix ApplyLocalThreshold(GpuPix thresholdMap) =>
        Accelerator.ApplyLocalThresholdGpu(this, thresholdMap);
}
