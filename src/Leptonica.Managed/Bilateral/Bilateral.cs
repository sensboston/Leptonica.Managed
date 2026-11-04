using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  bilateral.c: Approximate separable and exact grayscale or color bilateral filtering.
 */
public sealed partial class Pix
{
    /// <summary>The parameters and data of an approximate separable bilateral filter (bilateral.h).</summary>
    private sealed class LBilateral
    {
        public Pix pixs = null!;         /* clone of source pix */
        public Pix pixsc = null!;        /* downscaled pix with mirrored border */
        public float spatial_stdev;      /* stdev of spatial gaussian */
        public float range_stdev;        /* stdev of range gaussian */
        public float[] spatial = null!;  /* 1D gaussian spatial kernel */
        public float[] range = null!;    /* one-sided gaussian range kernel */
        public int minval;               /* min value in pixel image */
        public int maxval;               /* max value in pixel image */
        public int ncomps;               /* number of intermediate results */
        public int[] nc = null!;         /* set of k values (size ncomps) */
        public int[] kindex = null!;     /* mapping from intensity to lower k */
        public float[] kfract = null!;   /* mapping from intensity to fract of hi */
        public Pix[] pixac = null!;      /* intermediate result images (PBC) */
        public int reduction;            /* 1, 2 or 4 */
    }

    /*--------------------------------------------------------------------------*
     *  Top level approximate separable grayscale or color bilateral filtering  *
     *--------------------------------------------------------------------------*/
    // leptonica: pixBilateral (bilateral.c)
    /// <summary>Approximate separable bilateral filter of an 8 bpp gray or 32 bpp rgb image.</summary>
    public Pix Bilateral(float spatial_stdev, float range_stdev, int ncomps, int reduction)
    {
        const string func = "pixBilateral";

        if (Colormap != null)
            throw Lept.Arg(func, "pixs not defined or cmapped");
        GetDimensions(out int w, out int h, out int d);
        if (d != 8 && d != 32)
            throw Lept.Arg(func, "pixs not 8 or 32 bpp");
        if (reduction != 1 && reduction != 2 && reduction != 4)
            throw Lept.Arg(func, "reduction invalid");
        int filtersize = (int)(2.0 * spatial_stdev + 1.0 + 0.5);
        if (w < 2 * filtersize || h < 2 * filtersize) {
            Log.Warning(func, $"w = {w}, h = {h}; w or h < 2 * filtersize = {2 * filtersize}; returning copy");
            return Copy(null, this);
        }
        float sstdev = spatial_stdev / (float)reduction;  /* reduced spat. stdev */
        if (sstdev < 0.5)
            throw Lept.Arg(func, "sstdev < 0.5");
        if (range_stdev <= 5.0)
            throw Lept.Arg(func, "range_stdev <= 5.0");
        if (ncomps < 4 || ncomps > 30)
            throw Lept.Arg(func, "ncomps not in [4 ... 30]");
        if (ncomps * range_stdev < 100.0)
            throw Lept.Arg(func, "ncomps * range_stdev < 100.0");

        if (d == 8)
            return BilateralGray(spatial_stdev, range_stdev, ncomps, reduction);

        SplitRGBFast(out Pix pixr, out Pix pixg, out Pix pixb);
        Pix pixrm = pixr.BilateralGray(spatial_stdev, range_stdev, ncomps, reduction);
        Pix pixgm = pixg.BilateralGray(spatial_stdev, range_stdev, ncomps, reduction);
        Pix pixbm = pixb.BilateralGray(spatial_stdev, range_stdev, ncomps, reduction);
        return CombineRGBFast(pixrm, pixgm, pixbm);
    }

    // leptonica: pixBilateralGray (bilateral.c)
    /// <summary>Approximate separable bilateral filter of an 8 bpp gray image.</summary>
    public Pix BilateralGray(float spatial_stdev, float range_stdev, int ncomps, int reduction)
    {
        const string func = "pixBilateralGray";

        if (Colormap != null)
            throw Lept.Arg(func, "pixs not defined or cmapped");
        if (Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp gray");
        if (reduction != 1 && reduction != 2 && reduction != 4)
            throw Lept.Arg(func, "reduction invalid");
        float sstdev = spatial_stdev / (float)reduction;  /* reduced spat. stdev */
        if (sstdev < 0.5)
            throw Lept.Arg(func, "sstdev < 0.5");
        if (range_stdev <= 5.0)
            throw Lept.Arg(func, "range_stdev <= 5.0");
        if (ncomps < 4 || ncomps > 30)
            throw Lept.Arg(func, "ncomps not in [4 ... 30]");
        if (ncomps * range_stdev < 100.0)
            throw Lept.Arg(func, "ncomps * range_stdev < 100.0");

        LBilateral bil = BilateralCreate(this, spatial_stdev, range_stdev, ncomps, reduction);
        return BilateralApply(bil);
    }

    /*----------------------------------------------------------------------*
     *       Implementation of approximate separable bilateral filter       *
     *----------------------------------------------------------------------*/
    // leptonica: bilateralCreate (bilateral.c)
    private static LBilateral BilateralCreate(Pix pixs, float spatial_stdev, float range_stdev,
                                              int ncomps, int reduction)
    {
        Pix pix1;
        if (reduction == 1) {
            pix1 = pixs;
        } else if (reduction == 2) {
            pix1 = pixs.ScaleAreaMap2();
        } else {  /* reduction == 4 */
            Pix pix2 = pixs.ScaleAreaMap2();
            pix1 = pix2.ScaleAreaMap2();
        }

        float sstdev = spatial_stdev / (float)reduction;  /* reduced spat. stdev */
        int border = (int)(2 * sstdev + 1);
        Pix pixsc = pix1.AddMirroredBorder(border, border, border, border);
        pix1.GetExtremeValue(1, L.L_SELECT_MIN, out _, out _, out _, out int minval);
        pix1.GetExtremeValue(1, L.L_SELECT_MAX, out _, out _, out _, out int maxval);

        LBilateral bil = new LBilateral {
            spatial_stdev = sstdev,
            range_stdev = range_stdev,
            reduction = reduction,
            ncomps = ncomps,
            minval = minval,
            maxval = maxval,
            pixsc = pixsc,
            pixs = pixs
        };

        int[] nc = new int[ncomps];
        for (int i = 0; i < ncomps; i++)
            nc[i] = minval + i * (maxval - minval) / (ncomps - 1);
        bil.nc = nc;

        int[] kindex = new int[256];
        for (int i = minval, k = 0; i <= maxval && k < ncomps - 1; k++) {
            float fval2 = nc[k + 1];
            while (i < fval2) {
                kindex[i] = k;
                i++;
            }
        }
        kindex[maxval] = ncomps - 2;
        bil.kindex = kindex;

        float[] kfract = new float[256];
        for (int i = minval, k = 0; i <= maxval && k < ncomps - 1; k++) {
            float fval1 = nc[k];
            float fval2 = nc[k + 1];
            while (i < fval2) {
                kfract[i] = (float)(i - fval1) / (float)(fval2 - fval1);
                i++;
            }
        }
        kfract[maxval] = 1.0f;
        bil.kfract = kfract;

        int spatial_size = (int)(2 * sstdev + 1);
        float[] spatial = new float[spatial_size];
        float denom = (float)(2.0 * sstdev * sstdev);
        for (int i = 0; i < spatial_size; i++)
            spatial[i] = MathF.Exp(-(float)(i * i) / denom);
        bil.spatial = spatial;

        float[] range = new float[256];
        denom = (float)(2.0 * range_stdev * range_stdev);
        for (int i = 0; i < 256; i++)
            range[i] = MathF.Exp(-(float)(i * i) / denom);
        bil.range = range;

        pixsc.GetDimensions(out int ws, out int hs, out _);
        uint[] datas = pixsc.Data;
        int wpls = pixsc.Wpl;
        pixs.GetDimensions(out int w, out int h, out _);
        int wd = (w + reduction - 1) / reduction;
        int hd = (h + reduction - 1) / reduction;
        int halfwidth = (int)(2.0 * sstdev);
        int klen = 2 * halfwidth + 1;
        float[] spatSym = new float[klen];
        for (int k = -halfwidth; k <= halfwidth; k++)
            spatSym[k + halfwidth] = spatial[Math.Abs(k)];

        /* Unpack mirrored-border source image once into flat bytes for all ncomps components. */
        using TempBuffer<byte> flatSrcBuf = new TempBuffer<byte>(ws * hs);
        byte[] srcBytes = flatSrcBuf.Array;
        {
            ref uint baseS = ref MemoryMarshal.GetArrayDataReference(datas);
            ref byte baseB = ref MemoryMarshal.GetArrayDataReference(srcBytes);
            for (int r = 0; r < hs; r++)
                UnpackMorphRow(ref Unsafe.Add(ref baseS, (nuint)(r * wpls)), ws, ref Unsafe.Add(ref baseB, (nuint)(r * ws)));
        }

        Pix[] pixac = new Pix[ncomps];
        void ComputeComponent(int index)
        {
            int kval = nc[index];
            Span<float> rLut = stackalloc float[256];
            for (int v = 0; v < 256; v++)
                rLut[v] = range[Math.Abs(kval - v)];

            byte[] tmpBytes = ArrayPool<byte>.Shared.Rent(ws * hs + 32);
            try {
                srcBytes.AsSpan(0, ws * hs).CopyTo(tmpBytes);
                ref byte srcRef = ref MemoryMarshal.GetArrayDataReference(srcBytes);
                ref byte tmpRef = ref MemoryMarshal.GetArrayDataReference(tmpBytes);
                ref float spatRef = ref MemoryMarshal.GetArrayDataReference(spatSym);
                ref float rRef = ref MemoryMarshal.GetReference(rLut);

                /* Horizontal pass */
                for (int i = 0; i < hd; i++) {
                    int rowOff = (border + i) * ws + border;
                    for (int j = 0; j < wd; j++) {
                        int startOff = rowOff + j - halfwidth;
                        float sum = 0.0f;
                        float norm = 0.0f;
                        for (int m = 0; m < klen; m++) {
                            int nval = Unsafe.Add(ref srcRef, (nuint)(startOff + m));
                            float kern = Unsafe.Add(ref spatRef, (nuint)m) * Unsafe.Add(ref rRef, (nuint)nval);
                            sum += kern * nval;
                            norm += kern;
                        }
                        if (norm > 0.0)
                            Unsafe.Add(ref tmpRef, (nuint)(rowOff + j)) = (byte)((int)((sum / norm) + 0.5));
                    }
                }

                /* Vertical pass */
                Pix pixd = CreateNoInit(wd, hd, 8);
                uint[] datad = pixd.Data;
                int wpld = pixd.Wpl;
                bool clearTail = (wd & 3) != 0;
                ref uint dstBase = ref MemoryMarshal.GetArrayDataReference(datad);
                byte[] outRow = ArrayPool<byte>.Shared.Rent(wd + 32);
                try {
                    ref byte outRef = ref MemoryMarshal.GetArrayDataReference(outRow);
                    for (int i = 0; i < hd; i++) {
                        int centerRowOff = (border + i) * ws + border;
                        int topRowOff = (border + i - halfwidth) * ws + border;
                        for (int j = 0; j < wd; j++) {
                            float sum = 0.0f;
                            float norm = 0.0f;
                            int colPtr = topRowOff + j;
                            for (int m = 0; m < klen; m++, colPtr += ws) {
                                int nval = Unsafe.Add(ref tmpRef, (nuint)colPtr);
                                float kern = Unsafe.Add(ref spatRef, (nuint)m) * Unsafe.Add(ref rRef, (nuint)nval);
                                sum += kern * nval;
                                norm += kern;
                            }
                            int dval = norm > 0.0
                                ? (int)((sum / norm) + 0.5)
                                : Unsafe.Add(ref tmpRef, (nuint)(centerRowOff + j));
                            Unsafe.Add(ref outRef, (nuint)j) = (byte)dval;
                        }
                        ref uint dstRow = ref Unsafe.Add(ref dstBase, (nuint)(i * wpld));
                        if (clearTail)
                            Unsafe.Add(ref dstRow, (nuint)(wd >> 2)) = 0;
                        PackMorphRowSlice(ref outRef, ref dstRow, 0, wd);
                    }
                } finally {
                    ArrayPool<byte>.Shared.Return(outRow);
                }

                pixac[index] = pixd;
            } finally {
                ArrayPool<byte>.Shared.Return(tmpBytes);
            }
        }

        LeptParallel.ForRows(ncomps, (long)wd * hd * klen * 4L, (iStart, iEnd) => {
            for (int index = iStart; index < iEnd; index++)
                ComputeComponent(index);
        });

        bil.pixac = pixac;
        return bil;
    }

    // leptonica: bilateralApply (bilateral.c)
    private static Pix BilateralApply(LBilateral bil)
    {
        Pix pixs = bil.pixs;
        int ncomps = bil.ncomps;
        int[] kindex = bil.kindex;
        float[] kfract = bil.kfract;
        int reduction = bil.reduction;
        Pix[] pixac = bil.pixac;
        if (pixac.Length != ncomps)
            throw Lept.Arg("bilateralApply", "PBC images do not exist");

        Pix pixd = CreateTemplateNoInit(pixs);
        uint[] datas = pixs.Data;
        int wpls = pixs.Wpl;
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        pixs.GetDimensions(out int w, out int h, out _);

        uint[][] pbcData = new uint[ncomps][];
        for (int c = 0; c < ncomps; c++)
            pbcData[c] = pixac[c].Data;
        int pbcWpl = pixac[0].Wpl;
        int shift = reduction == 1 ? 0 : (reduction == 2 ? 1 : 2);
        bool clearTail = (w & 3) != 0;

        LeptParallel.ForRows(h, (long)w * 8L, (yStart, yEnd) => {
            ref uint baseS = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint baseD = ref MemoryMarshal.GetArrayDataReference(datad);
            ref int kIdxRef = ref MemoryMarshal.GetArrayDataReference(kindex);
            ref float kFrRef = ref MemoryMarshal.GetArrayDataReference(kfract);
            byte[] rowIn = ArrayPool<byte>.Shared.Rent(w * 2 + 64);
            try {
                ref byte inRef = ref MemoryMarshal.GetArrayDataReference(rowIn);
                ref byte outRef = ref Unsafe.Add(ref inRef, (nuint)w);
                for (int i = yStart; i < yEnd; i++) {
                    UnpackMorphRow(ref Unsafe.Add(ref baseS, (nuint)(i * wpls)), w, ref inRef);
                    int pbcRowOff = (i >> shift) * pbcWpl;
                    for (int j = 0; j < w; j++) {
                        int jred = j >> shift;
                        int vals = Unsafe.Add(ref inRef, (nuint)j);
                        int k = Unsafe.Add(ref kIdxRef, (nuint)vals);
                        int lowval = GetDataByteUnchecked(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(pbcData[k]), (nuint)pbcRowOff), jred);
                        int hival = GetDataByteUnchecked(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(pbcData[k + 1]), (nuint)pbcRowOff), jred);
                        float fract = Unsafe.Add(ref kFrRef, (nuint)vals);
                        Unsafe.Add(ref outRef, (nuint)j) = (byte)((int)((1.0 - fract) * lowval + fract * hival + 0.5));
                    }
                    ref uint dstRow = ref Unsafe.Add(ref baseD, (nuint)(i * wpld));
                    if (clearTail)
                        Unsafe.Add(ref dstRow, (nuint)(w >> 2)) = 0;
                    PackMorphRowSlice(ref outRef, ref dstRow, 0, w);
                }
            } finally {
                ArrayPool<byte>.Shared.Return(rowIn);
            }
        });

        return pixd;
    }

    /*----------------------------------------------------------------------*
     *    Exact implementation of grayscale or color bilateral filtering    *
     *----------------------------------------------------------------------*/
    // leptonica: pixBilateralExact (bilateral.c)
    /// <summary>Exact bilateral filter of an 8 bpp gray or 32 bpp rgb image.</summary>
    public Pix BilateralExact(L_Kernel spatial_kel, L_Kernel? range_kel)
    {
        const string func = "pixBilateralExact";

        if (Colormap != null)
            throw Lept.Arg(func, "pixs is cmapped");
        int d = Depth;
        if (d != 8 && d != 32)
            throw Lept.Arg(func, "pixs not 8 or 32 bpp");

        if (d == 8) {
            return BilateralGrayExact(spatial_kel, range_kel);
        } else {  /* d == 32 */
            if (range_kel != null &&
                LeptGpu.TryGetActive((long)Width * Height, spatial_kel.Sx * spatial_kel.Sy * 12, out IGpuAccelerator? gpu) &&
                gpu.TryBilateralExact(this, spatial_kel, range_kel, out Pix? gpuPix)) {
                return gpuPix;
            }
            SplitRGBFast(out Pix pixr, out Pix pixg, out Pix pixb);
            Pix pixrm = pixr.BilateralGrayExact(spatial_kel, range_kel);
            Pix pixgm = pixg.BilateralGrayExact(spatial_kel, range_kel);
            Pix pixbm = pixb.BilateralGrayExact(spatial_kel, range_kel);
            return CombineRGBFast(pixrm, pixgm, pixbm);
        }
    }

    // leptonica: pixBilateralGrayExact (bilateral.c)
    /// <summary>Exact bilateral filter of an 8 bpp gray image.</summary>
    public Pix BilateralGrayExact(L_Kernel spatial_kel, L_Kernel? range_kel)
    {
        const string func = "pixBilateralGrayExact";

        if (Depth != 8)
            throw Lept.Arg(func, "pixs must be gray");
        GetDimensions(out int w, out int h, out _);
        spatial_kel.GetParameters(out int sy, out int sx, out _, out _);
        if (w < 2 * sx + 1 || h < 2 * sy + 1) {
            Log.Warning(func, $"w = {w} < 2 * sx + 1 = {2 * sx + 1}, or h = {h} < 2 * sy + 1 = {2 * sy + 1}; returning copy");
            return Copy(null, this);
        }
        if (range_kel == null)
            return Convolve(spatial_kel, 8, 1);
        if (range_kel.Sx != 256 || range_kel.Sy != 1)
            throw Lept.Arg(func, "range kel not {256 x 1");

        if (Colormap == null &&
            LeptGpu.TryGetActive((long)w * h, sx * sy * 4, out IGpuAccelerator? gpu) &&
            gpu.TryBilateralGrayExact(this, spatial_kel, range_kel, out Pix? gpuPix)) {
            return gpuPix;
        }

        L_Kernel keli = spatial_kel.Invert();
        keli.GetParameters(out sy, out sx, out int cy, out int cx);
        Pix pixt = AddMirroredBorder(cx, sx - cx, cy, sy - cy);
        int wt = pixt.Width;
        int ht = pixt.Height;
        uint[] datat = pixt.Data;
        int wplt = pixt.Wpl;

        using TempBuffer<byte> flatSrc = new TempBuffer<byte>(wt * ht);
        byte[] srcBytes = flatSrc.Array;
        {
            ref uint baseT = ref MemoryMarshal.GetArrayDataReference(datat);
            ref byte baseB = ref MemoryMarshal.GetArrayDataReference(srcBytes);
            for (int r = 0; r < ht; r++)
                UnpackMorphRow(ref Unsafe.Add(ref baseT, (nuint)(r * wplt)), wt, ref Unsafe.Add(ref baseB, (nuint)(r * wt)));
        }

        float[] flatKel = keli.GetFlatData();
        float[] rangeRow = range_kel.Data[0];

        /* Precompute 256x256 range-difference table (256 KB) to eliminate Math.Abs(center - val) inside inner loop. */
        using TempBuffer<float> rangeLutBuf = new TempBuffer<float>(256 * 256);
        float[] rangeLut = rangeLutBuf.Array;
        for (int c = 0; c < 256; c++) {
            int rowOff = c << 8;
            for (int v = 0; v < 256; v++)
                rangeLut[rowOff + v] = rangeRow[Math.Abs(c - v)];
        }

        Pix pixd = CreateNoInit(w, h, 8);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        bool clearTail = (w & 3) != 0;

        LeptParallel.ForRows(h, (long)w * sy * sx * 4L, (idStart, idEnd) => {
            ref byte srcBase = ref MemoryMarshal.GetArrayDataReference(srcBytes);
            ref float kelBase = ref MemoryMarshal.GetArrayDataReference(flatKel);
            ref float lutBase = ref MemoryMarshal.GetArrayDataReference(rangeLut);
            ref uint dstBase = ref MemoryMarshal.GetArrayDataReference(datad);
            byte[] outRow = ArrayPool<byte>.Shared.Rent(w + 32);
            try {
                ref byte outRef = ref MemoryMarshal.GetArrayDataReference(outRow);
                for (int id = idStart; id < idEnd; id++) {
                    int centerRow = (id + cy) * wt + cx;
                    for (int jd = 0; jd < w; jd++) {
                        int center_val = Unsafe.Add(ref srcBase, (nuint)(centerRow + jd));
                        ref float rRow = ref Unsafe.Add(ref lutBase, (nuint)(center_val << 8));
                        float weight_sum = 0.0f;
                        float sum = 0.0f;
                        for (int k = 0; k < sy; k++) {
                            ref byte sRow = ref Unsafe.Add(ref srcBase, (nuint)((id + k) * wt + jd));
                            ref float kRow = ref Unsafe.Add(ref kelBase, (nuint)(k * sx));
                            for (int m = 0; m < sx; m++) {
                                int val = Unsafe.Add(ref sRow, (nuint)m);
                                float weight = Unsafe.Add(ref kRow, (nuint)m) * Unsafe.Add(ref rRow, (nuint)val);
                                weight_sum += weight;
                                sum += val * weight;
                            }
                        }
                        Unsafe.Add(ref outRef, (nuint)jd) = (byte)((int)(sum / weight_sum + 0.5));
                    }
                    ref uint dstRow = ref Unsafe.Add(ref dstBase, (nuint)(id * wpld));
                    if (clearTail)
                        Unsafe.Add(ref dstRow, (nuint)(w >> 2)) = 0;
                    PackMorphRowSlice(ref outRef, ref dstRow, 0, w);
                }
            } finally {
                ArrayPool<byte>.Shared.Return(outRow);
            }
        });

        return pixd;
    }

    // leptonica: pixBlockBilateralExact (bilateral.c)
    /// <summary>Exact bilateral filter, with the kernels given by standard deviations.</summary>
    public Pix BlockBilateralExact(float spatial_stdev, float range_stdev)
    {
        const string func = "pixBlockBilateralExact";

        int d = Depth;
        if (d != 8 && d != 32)
            throw Lept.Arg(func, "pixs not 8 or 32 bpp");
        if (Colormap != null)
            throw Lept.Arg(func, "pixs is cmapped");
        if (spatial_stdev <= 0.0)
            throw Lept.Arg(func, "invalid spatial stdev");
        if (range_stdev <= 0.0)
            throw Lept.Arg(func, "invalid range stdev");

        int halfwidth = (int)(2 * spatial_stdev);
        L_Kernel spatial_kel = L_Kernel.MakeGaussianKernel(halfwidth, halfwidth, spatial_stdev, 1.0f);
        L_Kernel range_kel = MakeRangeKernel(range_stdev);
        return BilateralExact(spatial_kel, range_kel);
    }

    /*----------------------------------------------------------------------*
     *                         Kernel helper function                       *
     *----------------------------------------------------------------------*/
    // leptonica: makeRangeKernel (bilateral.c)
    /// <summary>Makes a one-sided gaussian range kernel of size 1 x 256.</summary>
    public static L_Kernel MakeRangeKernel(float range_stdev)
    {
        const string func = "makeRangeKernel";

        if (range_stdev <= 0.0)
            throw Lept.Arg(func, "invalid stdev <= 0");

        float denom = (float)(2.0 * range_stdev * range_stdev);
        L_Kernel kel = L_Kernel.Create(1, 256);
        kel.SetOrigin(0, 0);
        for (int x = 0; x < 256; x++)
            kel.SetElement(0, x, MathF.Exp(-(float)(x * x) / denom));
        return kel;
    }
}
