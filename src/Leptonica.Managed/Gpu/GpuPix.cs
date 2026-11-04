using System;
using ILGPU;
using ILGPU.Runtime;

namespace Leptonica.Gpu;

/// <summary>
/// Device-resident raster image stored in pooled GPU VRAM (<see cref="PooledGpuBuffer{T}"/>),
/// enabling multi-stage image-processing pipelines (<c>Upload once → Chain kernels in VRAM → Download once</c>)
/// without intermediate PCIe host↔device round-trips.
/// </summary>
public sealed partial class GpuPix : IDisposable
{
    private PooledGpuBuffer<uint>? _buffer;

    internal GpuPix(
        IlGpuAccelerator accelerator,
        PooledGpuBuffer<uint> buffer,
        int width,
        int height,
        int depth,
        int wpl,
        int spp,
        int xres,
        int yres,
        int informat)
    {
        Accelerator = accelerator;
        _buffer = buffer;
        Width = width;
        Height = height;
        Depth = depth;
        Wpl = wpl;
        Spp = spp;
        XRes = xres;
        YRes = yres;
        Informat = informat;
    }

    /// <summary>Gets the accelerator that owns the device buffer of this image.</summary>
    public IlGpuAccelerator Accelerator { get; }
    /// <summary>Gets the image width in pixels.</summary>
    public int Width { get; }
    /// <summary>Gets the image height in pixels.</summary>
    public int Height { get; }
    /// <summary>Gets the image depth in bits per pixel.</summary>
    public int Depth { get; }
    /// <summary>Gets the number of 32-bit words per raster line.</summary>
    public int Wpl { get; }
    /// <summary>Gets the number of samples per pixel.</summary>
    public int Spp { get; }
    /// <summary>Gets the horizontal resolution in pixels per inch.</summary>
    public int XRes { get; }
    /// <summary>Gets the vertical resolution in pixels per inch.</summary>
    public int YRes { get; }
    /// <summary>Gets the format of the file the image was read from, as an <c>IFF_*</c> constant.</summary>
    public int Informat { get; }

    internal PooledGpuBuffer<uint> DeviceBuffer =>
        _buffer ?? throw new ObjectDisposedException(nameof(GpuPix));

    internal ArrayView1D<uint, Stride1D.Dense> View => DeviceBuffer.View;

    /// <summary>Total 32-bit words (<c>Wpl * Height</c>) occupied by the raster.</summary>
    public int WordCount => Wpl * Height;

    /// <summary>
    /// Downloads the VRAM-resident raster back into a managed <see cref="Pix"/> in a single DMA transfer.
    /// </summary>
    public Pix Download()
    {
        PooledGpuBuffer<uint> buf = DeviceBuffer;
        Pix pixd = Pix.CreateNoInit(Width, Height, Depth);
        pixd.Spp = Spp;
        pixd.XRes = XRes;
        pixd.YRes = YRes;
        pixd.InputFormat = Informat;
        Accelerator.DownloadWords(buf.View, pixd.Data, WordCount);
        return pixd;
    }

    /// <summary>Creates a device-to-device copy in VRAM without touching host memory.</summary>
    public GpuPix CloneGpu() => Accelerator.CloneGpuPix(this);

    /// <summary>Bitwise inverts all pixels in-place in VRAM and returns <c>this</c> for fluent chaining.</summary>
    public GpuPix InvertInPlace()
    {
        Accelerator.InvertInPlace(this);
        return this;
    }

    /// <summary>Executes 2D convolution in VRAM and returns a newly rented <see cref="GpuPix"/>.</summary>
    public GpuPix Convolve(L_Kernel kel, int outdepth = 0, int normflag = 1)
    {
        if (Depth == 32 && outdepth == 0)
            return Accelerator.ConvolveRgbGpu(this, kel, 1, 1);
        int targetDepth = outdepth == 0 ? Depth : outdepth;
        return Accelerator.ConvolveGpu(this, kel, targetDepth, normflag, 1, 1);
    }

    /// <summary>Executes 2-pass separable convolution entirely in VRAM without host round-trips.</summary>
    public GpuPix ConvolveSep(L_Kernel kelx, L_Kernel kely, int outdepth = 0, int normflag = 1)
    {
        if (Depth == 32 && outdepth == 0)
            return Accelerator.ConvolveRgbSepGpu(this, kelx, kely, 1, 1);
        int targetDepth = outdepth == 0 ? Depth : outdepth;
        return Accelerator.ConvolveSepGpu(this, kelx, kely, targetDepth, normflag, 1, 1);
    }

    /// <summary>Executes exact bilateral filtering (8 bpp gray or 32 bpp RGB) in VRAM.</summary>
    public GpuPix BilateralExact(L_Kernel spatialKel, L_Kernel rangeKel) =>
        Accelerator.BilateralExactGpu(this, spatialKel, rangeKel);

    /// <summary>Executes exact 2-pass register-resident order-statistic rank filter in VRAM.</summary>
    public GpuPix RankFilter(int wf, int hf, float rank) =>
        Accelerator.RankFilterGpu(this, wf, hf, rank);

    /// <summary>Executes exact median filter (<c>rank = 0.5f</c>) in VRAM.</summary>
    public GpuPix MedianFilter(int wf, int hf) =>
        Accelerator.RankFilterGpu(this, wf, hf, 0.5f);

    /// <summary>Executes separable brick dilation (8 bpp gray or 32 bpp RGB) in VRAM.</summary>
    public GpuPix DilateBrick(int hsize, int vsize) =>
        Accelerator.MorphBrickGpu(this, hsize, vsize, dilate: true);

    /// <summary>Executes separable brick erosion (8 bpp gray or 32 bpp RGB) in VRAM.</summary>
    public GpuPix ErodeBrick(int hsize, int vsize) =>
        Accelerator.MorphBrickGpu(this, hsize, vsize, dilate: false);

    /// <summary>Executes morphological opening (erosion followed by dilation) entirely in VRAM.</summary>
    public GpuPix OpenBrick(int hsize, int vsize)
    {
        using GpuPix eroded = ErodeBrick(hsize, vsize);
        return eroded.DilateBrick(hsize, vsize);
    }

    /// <summary>Executes morphological closing (dilation followed by erosion) entirely in VRAM.</summary>
    public GpuPix CloseBrick(int hsize, int vsize)
    {
        using GpuPix dilated = DilateBrick(hsize, vsize);
        return dilated.ErodeBrick(hsize, vsize);
    }

    /// <summary>Returns the device buffer of this image to the pool.</summary>
    public void Dispose()
    {
        PooledGpuBuffer<uint>? buf = _buffer;
        if (buf != null) {
            _buffer = null;
            buf.Dispose();
        }
    }
}
