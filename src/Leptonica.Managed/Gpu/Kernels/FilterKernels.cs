using System.Runtime.CompilerServices;
using ILGPU;
using ILGPU.Runtime;

namespace Leptonica.Gpu.Kernels;

/// <summary>
/// ILGPU device kernels for 2D &amp; separable convolution, exact grayscale/color bilateral filtering,
/// arbitrary-window rank/median filtering, and separable brick grayscale/color morphology.
/// Every 8 bpp output kernel packs 4 pixels per thread into a single coalesced 32-bit word store
/// with zero atomic contention.
/// </summary>
internal static class FilterKernels
{
    // =========================================================================
    // 1. Exact Bilateral Filter (8 bpp Gray & 32 bpp RGB)
    // =========================================================================

    public static void BilateralGrayExactWordKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> dst,
        ArrayView1D<float, Stride1D.Dense> spatialKel,
        ArrayView1D<float, Stride1D.Dense> rangeKel,
        int w, int h, int wpls, int wpld,
        int sx, int sy, int cx, int cy)
    {
        int idx = index;
        int y = idx / wpld;
        int wordX = idx - y * wpld;
        int baseX = wordX << 2;

        int b0 = ComputeBilateralGrayPixel(src, spatialKel, rangeKel, w, h, wpls, sx, sy, cx, cy, baseX, y);
        int b1 = baseX + 1 < w ? ComputeBilateralGrayPixel(src, spatialKel, rangeKel, w, h, wpls, sx, sy, cx, cy, baseX + 1, y) : 0;
        int b2 = baseX + 2 < w ? ComputeBilateralGrayPixel(src, spatialKel, rangeKel, w, h, wpls, sx, sy, cx, cy, baseX + 2, y) : 0;
        int b3 = baseX + 3 < w ? ComputeBilateralGrayPixel(src, spatialKel, rangeKel, w, h, wpls, sx, sy, cx, cy, baseX + 3, y) : 0;

        dst[idx] = GpuPixelDevice.Pack4Bytes(b0, b1, b2, b3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComputeBilateralGrayPixel(
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<float, Stride1D.Dense> spatialKel,
        ArrayView1D<float, Stride1D.Dense> rangeKel,
        int w, int h, int wpls,
        int sx, int sy, int cx, int cy,
        int x, int y)
    {
        int centerVal = GpuPixelDevice.GetByte(src, y * wpls, x);
        float weightSum = 0.0f;
        float sum = 0.0f;

        for (int k = 0; k < sy; k++) {
            int syRow = GpuPixelDevice.MirrorCoord(y + k - cy, h) * wpls;
            int kOff = k * sx;
            for (int m = 0; m < sx; m++) {
                int sxCol = GpuPixelDevice.MirrorCoord(x + m - cx, w);
                int val = GpuPixelDevice.GetByte(src, syRow, sxCol);
                int diff = centerVal >= val ? centerVal - val : val - centerVal;
                float weight = GpuIeeeMath.MulF32(spatialKel[kOff + m], rangeKel[diff]);
                weightSum = GpuIeeeMath.AddF32(weightSum, weight);
                sum = GpuIeeeMath.AddF32(sum, GpuIeeeMath.MulF32((float)val, weight));
            }
        }

        float q = GpuIeeeMath.DivF32(sum, weightSum);
        return (int)GpuIeeeMath.AddF64((double)q, 0.5) & 0xff;
    }

    public static void BilateralColorExactKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> dst,
        ArrayView1D<float, Stride1D.Dense> spatialKel,
        ArrayView1D<float, Stride1D.Dense> rangeKel,
        int w, int h, int wpls, int wpld,
        int sx, int sy, int cx, int cy)
    {
        int idx = index;
        int y = idx / w;
        int x = idx - y * w;

        uint centerPx = src[y * wpls + x];
        int cr = (int)(centerPx >> 24);
        int cg = (int)((centerPx >> 16) & 0xffu);
        int cb = (int)((centerPx >> 8) & 0xffu);

        float wSumR = 0.0f, sumR = 0.0f;
        float wSumG = 0.0f, sumG = 0.0f;
        float wSumB = 0.0f, sumB = 0.0f;

        for (int k = 0; k < sy; k++) {
            int syRow = GpuPixelDevice.MirrorCoord(y + k - cy, h) * wpls;
            int kOff = k * sx;
            for (int m = 0; m < sx; m++) {
                float sWeight = spatialKel[kOff + m];
                uint px = src[syRow + GpuPixelDevice.MirrorCoord(x + m - cx, w)];
                int vr = (int)(px >> 24);
                int vg = (int)((px >> 16) & 0xffu);
                int vb = (int)((px >> 8) & 0xffu);

                float wr = GpuIeeeMath.MulF32(sWeight, rangeKel[cr >= vr ? cr - vr : vr - cr]);
                wSumR = GpuIeeeMath.AddF32(wSumR, wr);
                sumR = GpuIeeeMath.AddF32(sumR, GpuIeeeMath.MulF32((float)vr, wr));

                float wg = GpuIeeeMath.MulF32(sWeight, rangeKel[cg >= vg ? cg - vg : vg - cg]);
                wSumG = GpuIeeeMath.AddF32(wSumG, wg);
                sumG = GpuIeeeMath.AddF32(sumG, GpuIeeeMath.MulF32((float)vg, wg));

                float wb = GpuIeeeMath.MulF32(sWeight, rangeKel[cb >= vb ? cb - vb : vb - cb]);
                wSumB = GpuIeeeMath.AddF32(wSumB, wb);
                sumB = GpuIeeeMath.AddF32(sumB, GpuIeeeMath.MulF32((float)vb, wb));
            }
        }

        int rOut = (int)GpuIeeeMath.AddF64((double)GpuIeeeMath.DivF32(sumR, wSumR), 0.5);
        int gOut = (int)GpuIeeeMath.AddF64((double)GpuIeeeMath.DivF32(sumG, wSumG), 0.5);
        int bOut = (int)GpuIeeeMath.AddF64((double)GpuIeeeMath.DivF32(sumB, wSumB), 0.5);
        dst[y * wpld + x] = GpuPixelDevice.ComposeRgb(rOut, gOut, bOut);
    }

    // =========================================================================
    // 2. General 2D & Separable Convolution (8/16/32 bpp + RGB)
    // =========================================================================

    internal readonly struct ConvolveParams
    {
        public readonly int W, H, Wpls, SrcDepth;
        public readonly int Wd, Wpld, Sfx, Sfy;
        public readonly int Sx, Sy, Cx, Cy;

        public ConvolveParams(
            int w, int h, int wpls, int srcDepth,
            int wd, int wpld, int sfx, int sfy,
            int sx, int sy, int cx, int cy)
        {
            W = w; H = h; Wpls = wpls; SrcDepth = srcDepth;
            Wd = wd; Wpld = wpld; Sfx = sfx; Sfy = sfy;
            Sx = sx; Sy = sy; Cx = cx; Cy = cy;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float EvalConvolveSum(
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<float, Stride1D.Dense> kel,
        ConvolveParams p,
        int srcX, int srcY)
    {
        float sum = 0.0f;
        if (p.SrcDepth == 8) {
            for (int k = 0; k < p.Sy; k++) {
                int rOff = GpuPixelDevice.MirrorCoord(srcY + k - p.Cy, p.H) * p.Wpls;
                int kOff = k * p.Sx;
                for (int m = 0; m < p.Sx; m++) {
                    int val = GpuPixelDevice.GetByte(src, rOff, GpuPixelDevice.MirrorCoord(srcX + m - p.Cx, p.W));
                    sum = GpuIeeeMath.AddF32(sum, GpuIeeeMath.MulF32((float)val, kel[kOff + m]));
                }
            }
        } else if (p.SrcDepth == 16) {
            for (int k = 0; k < p.Sy; k++) {
                int rOff = GpuPixelDevice.MirrorCoord(srcY + k - p.Cy, p.H) * p.Wpls;
                int kOff = k * p.Sx;
                for (int m = 0; m < p.Sx; m++) {
                    int val = GpuPixelDevice.GetTwoBytes(src, rOff, GpuPixelDevice.MirrorCoord(srcX + m - p.Cx, p.W));
                    sum = GpuIeeeMath.AddF32(sum, GpuIeeeMath.MulF32((float)val, kel[kOff + m]));
                }
            }
        } else {
            for (int k = 0; k < p.Sy; k++) {
                int rOff = GpuPixelDevice.MirrorCoord(srcY + k - p.Cy, p.H) * p.Wpls;
                int kOff = k * p.Sx;
                for (int m = 0; m < p.Sx; m++) {
                    int val = (int)src[rOff + GpuPixelDevice.MirrorCoord(srcX + m - p.Cx, p.W)];
                    sum = GpuIeeeMath.AddF32(sum, GpuIeeeMath.MulF32((float)val, kel[kOff + m]));
                }
            }
        }
        if (sum < 0.0f) sum = -sum;
        return sum;
    }

    public static void ConvolveTo8WordKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> dst,
        ArrayView1D<float, Stride1D.Dense> kel,
        ConvolveParams p)
    {
        int idx = index;
        int id = idx / p.Wpld;
        int wordJd = idx - id * p.Wpld;
        int baseJd = wordJd << 2;
        int srcY = id * p.Sfy;

        int b0 = (int)GpuIeeeMath.AddF64((double)EvalConvolveSum(src, kel, p, baseJd * p.Sfx, srcY), 0.5);
        int b1 = baseJd + 1 < p.Wd ? (int)GpuIeeeMath.AddF64((double)EvalConvolveSum(src, kel, p, (baseJd + 1) * p.Sfx, srcY), 0.5) : 0;
        int b2 = baseJd + 2 < p.Wd ? (int)GpuIeeeMath.AddF64((double)EvalConvolveSum(src, kel, p, (baseJd + 2) * p.Sfx, srcY), 0.5) : 0;
        int b3 = baseJd + 3 < p.Wd ? (int)GpuIeeeMath.AddF64((double)EvalConvolveSum(src, kel, p, (baseJd + 3) * p.Sfx, srcY), 0.5) : 0;

        dst[idx] = GpuPixelDevice.Pack4Bytes(b0, b1, b2, b3);
    }

    public static void ConvolveTo16WordKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> dst,
        ArrayView1D<float, Stride1D.Dense> kel,
        ConvolveParams p)
    {
        int idx = index;
        int id = idx / p.Wpld;
        int wordJd = idx - id * p.Wpld;
        int baseJd = wordJd << 1;
        int srcY = id * p.Sfy;

        int s0 = (int)GpuIeeeMath.AddF64((double)EvalConvolveSum(src, kel, p, baseJd * p.Sfx, srcY), 0.5);
        int s1 = baseJd + 1 < p.Wd ? (int)GpuIeeeMath.AddF64((double)EvalConvolveSum(src, kel, p, (baseJd + 1) * p.Sfx, srcY), 0.5) : 0;

        dst[idx] = GpuPixelDevice.Pack2Shorts(s0, s1);
    }

    public static void ConvolveTo32Kernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> dst,
        ArrayView1D<float, Stride1D.Dense> kel,
        ConvolveParams p)
    {
        int idx = index;
        int id = idx / p.Wd;
        int jd = idx - id * p.Wd;
        float sum = EvalConvolveSum(src, kel, p, jd * p.Sfx, id * p.Sfy);
        dst[id * p.Wpld + jd] = (uint)GpuIeeeMath.AddF64((double)sum, 0.5);
    }

    public static void ConvolveRgbKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> dst,
        ArrayView1D<float, Stride1D.Dense> kel,
        int w, int h, int wpls,
        int wd, int wpld, int sfx, int sfy,
        int sx, int sy, int cx, int cy)
    {
        int idx = index;
        int id = idx / wd;
        int jd = idx - id * wd;
        int srcX = jd * sfx;
        int srcY = id * sfy;

        float sumR = 0.0f, sumG = 0.0f, sumB = 0.0f;
        for (int k = 0; k < sy; k++) {
            int rOff = GpuPixelDevice.MirrorCoord(srcY + k - cy, h) * wpls;
            int kOff = k * sx;
            for (int m = 0; m < sx; m++) {
                float kw = kel[kOff + m];
                uint px = src[rOff + GpuPixelDevice.MirrorCoord(srcX + m - cx, w)];
                sumR = GpuIeeeMath.AddF32(sumR, GpuIeeeMath.MulF32((float)(int)(px >> 24), kw));
                sumG = GpuIeeeMath.AddF32(sumG, GpuIeeeMath.MulF32((float)(int)((px >> 16) & 0xffu), kw));
                sumB = GpuIeeeMath.AddF32(sumB, GpuIeeeMath.MulF32((float)(int)((px >> 8) & 0xffu), kw));
            }
        }
        if (sumR < 0.0f) sumR = -sumR;
        if (sumG < 0.0f) sumG = -sumG;
        if (sumB < 0.0f) sumB = -sumB;

        int rOut = (int)GpuIeeeMath.AddF64((double)sumR, 0.5);
        int gOut = (int)GpuIeeeMath.AddF64((double)sumG, 0.5);
        int bOut = (int)GpuIeeeMath.AddF64((double)sumB, 0.5);
        dst[id * wpld + jd] = GpuPixelDevice.ComposeRgb(rOut, gOut, bOut);
    }

    public static void ConvolveRgbSepHorizKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> tmpR,
        ArrayView1D<uint, Stride1D.Dense> tmpG,
        ArrayView1D<uint, Stride1D.Dense> tmpB,
        ArrayView1D<float, Stride1D.Dense> kelx,
        int w, int h, int wpls,
        int wt, int sfx,
        int sx, int sy, int cx, int cy)
    {
        int idx = index;
        int id = idx / wt;
        int jd = idx - id * wt;
        int srcX = jd * sfx;
        int srcY = id;

        float sumR = 0.0f, sumG = 0.0f, sumB = 0.0f;
        for (int k = 0; k < sy; k++) {
            int rOff = GpuPixelDevice.MirrorCoord(srcY + k - cy, h) * wpls;
            int kOff = k * sx;
            for (int m = 0; m < sx; m++) {
                float kw = kelx[kOff + m];
                uint px = src[rOff + GpuPixelDevice.MirrorCoord(srcX + m - cx, w)];
                sumR = GpuIeeeMath.AddF32(sumR, GpuIeeeMath.MulF32((float)(int)(px >> 24), kw));
                sumG = GpuIeeeMath.AddF32(sumG, GpuIeeeMath.MulF32((float)(int)((px >> 16) & 0xffu), kw));
                sumB = GpuIeeeMath.AddF32(sumB, GpuIeeeMath.MulF32((float)(int)((px >> 8) & 0xffu), kw));
            }
        }
        if (sumR < 0.0f) sumR = -sumR;
        if (sumG < 0.0f) sumG = -sumG;
        if (sumB < 0.0f) sumB = -sumB;

        tmpR[idx] = (uint)GpuIeeeMath.AddF64((double)sumR, 0.5);
        tmpG[idx] = (uint)GpuIeeeMath.AddF64((double)sumG, 0.5);
        tmpB[idx] = (uint)GpuIeeeMath.AddF64((double)sumB, 0.5);
    }

    public static void ConvolveRgbSepVertKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> tmpR,
        ArrayView1D<uint, Stride1D.Dense> tmpG,
        ArrayView1D<uint, Stride1D.Dense> tmpB,
        ArrayView1D<uint, Stride1D.Dense> dst,
        ArrayView1D<float, Stride1D.Dense> kely,
        int wt, int ht,
        int wd, int wpld, int sfy,
        int sx, int sy, int cx, int cy)
    {
        int idx = index;
        int id = idx / wd;
        int jd = idx - id * wd;
        int srcX = jd;
        int srcY = id * sfy;

        float sumR = 0.0f, sumG = 0.0f, sumB = 0.0f;
        for (int k = 0; k < sy; k++) {
            int rOff = GpuPixelDevice.MirrorCoord(srcY + k - cy, ht) * wt;
            int kOff = k * sx;
            for (int m = 0; m < sx; m++) {
                float kw = kely[kOff + m];
                int col = rOff + GpuPixelDevice.MirrorCoord(srcX + m - cx, wt);
                sumR = GpuIeeeMath.AddF32(sumR, GpuIeeeMath.MulF32((float)(int)tmpR[col], kw));
                sumG = GpuIeeeMath.AddF32(sumG, GpuIeeeMath.MulF32((float)(int)tmpG[col], kw));
                sumB = GpuIeeeMath.AddF32(sumB, GpuIeeeMath.MulF32((float)(int)tmpB[col], kw));
            }
        }
        if (sumR < 0.0f) sumR = -sumR;
        if (sumG < 0.0f) sumG = -sumG;
        if (sumB < 0.0f) sumB = -sumB;

        int rOut = (int)GpuIeeeMath.AddF64((double)sumR, 0.5);
        int gOut = (int)GpuIeeeMath.AddF64((double)sumG, 0.5);
        int bOut = (int)GpuIeeeMath.AddF64((double)sumB, 0.5);
        dst[id * wpld + jd] = GpuPixelDevice.ComposeRgb(rOut, gOut, bOut);
    }

    // =========================================================================
    // 3. Exact 2-Pass Register-Resident Rank & Median Filter (8 bpp & 32 bpp)
    // =========================================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComputeRankGrayPixel(
        ArrayView1D<uint, Stride1D.Dense> src,
        int w, int h, int wpls,
        int wf, int hf, int halfW, int halfH, int rankloc,
        int x, int y)
    {
        Bins16 bins = default;

        // Pass 1: 4-bit high-nibble coarse histogram in 16 scalar registers
        for (int k = 0; k < hf; k++) {
            int rOff = GpuPixelDevice.MirrorCoord(y + k - halfH, h) * wpls;
            for (int m = 0; m < wf; m++) {
                int val = GpuPixelDevice.GetByte(src, rOff, GpuPixelDevice.MirrorCoord(x + m - halfW, w));
                bins.Increment(val >> 4);
            }
        }

        int hiNibble = bins.SelectBin(rankloc, out int prevSum);
        int remRank = rankloc - prevSum;

        // Pass 2: 4-bit low-nibble fine histogram within the winning coarse bin
        bins.Clear();
        for (int k = 0; k < hf; k++) {
            int rOff = GpuPixelDevice.MirrorCoord(y + k - halfH, h) * wpls;
            for (int m = 0; m < wf; m++) {
                int val = GpuPixelDevice.GetByte(src, rOff, GpuPixelDevice.MirrorCoord(x + m - halfW, w));
                if ((val >> 4) == hiNibble)
                    bins.Increment(val & 15);
            }
        }

        int loNibble = bins.SelectBin(remRank, out _);
        return (hiNibble << 4) | loNibble;
    }

    public static void RankFilterGrayWordKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> dst,
        int w, int h, int wpls, int wpld,
        int wf, int hf, int rankloc)
    {
        int idx = index;
        int y = idx / wpld;
        int wordX = idx - y * wpld;
        int baseX = wordX << 2;
        int halfW = wf >> 1;
        int halfH = hf >> 1;

        int b0 = ComputeRankGrayPixel(src, w, h, wpls, wf, hf, halfW, halfH, rankloc, baseX, y);
        int b1 = baseX + 1 < w ? ComputeRankGrayPixel(src, w, h, wpls, wf, hf, halfW, halfH, rankloc, baseX + 1, y) : 0;
        int b2 = baseX + 2 < w ? ComputeRankGrayPixel(src, w, h, wpls, wf, hf, halfW, halfH, rankloc, baseX + 2, y) : 0;
        int b3 = baseX + 3 < w ? ComputeRankGrayPixel(src, w, h, wpls, wf, hf, halfW, halfH, rankloc, baseX + 3, y) : 0;

        dst[idx] = GpuPixelDevice.Pack4Bytes(b0, b1, b2, b3);
    }

    public static void RankFilterRgbKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> dst,
        int w, int h, int wpls, int wpld,
        int wf, int hf, int rankloc)
    {
        int idx = index;
        int y = idx / w;
        int x = idx - y * w;
        int halfW = wf >> 1;
        int halfH = hf >> 1;

        Bins16 binsR = default, binsG = default, binsB = default;

        for (int k = 0; k < hf; k++) {
            int rOff = GpuPixelDevice.MirrorCoord(y + k - halfH, h) * wpls;
            for (int m = 0; m < wf; m++) {
                uint px = src[rOff + GpuPixelDevice.MirrorCoord(x + m - halfW, w)];
                binsR.Increment((int)(px >> 28));
                binsG.Increment((int)((px >> 20) & 0x0fu));
                binsB.Increment((int)((px >> 12) & 0x0fu));
            }
        }

        int hiR = binsR.SelectBin(rankloc, out int prevR);
        int hiG = binsG.SelectBin(rankloc, out int prevG);
        int hiB = binsB.SelectBin(rankloc, out int prevB);

        binsR.Clear();
        binsG.Clear();
        binsB.Clear();

        for (int k = 0; k < hf; k++) {
            int rOff = GpuPixelDevice.MirrorCoord(y + k - halfH, h) * wpls;
            for (int m = 0; m < wf; m++) {
                uint px = src[rOff + GpuPixelDevice.MirrorCoord(x + m - halfW, w)];
                int vr = (int)(px >> 24);
                int vg = (int)((px >> 16) & 0xffu);
                int vb = (int)((px >> 8) & 0xffu);
                if ((vr >> 4) == hiR) binsR.Increment(vr & 15);
                if ((vg >> 4) == hiG) binsG.Increment(vg & 15);
                if ((vb >> 4) == hiB) binsB.Increment(vb & 15);
            }
        }

        int rOut = (hiR << 4) | binsR.SelectBin(rankloc - prevR, out _);
        int gOut = (hiG << 4) | binsG.SelectBin(rankloc - prevG, out _);
        int bOut = (hiB << 4) | binsB.SelectBin(rankloc - prevB, out _);
        dst[y * wpld + x] = GpuPixelDevice.ComposeRgb(rOut, gOut, bOut);
    }

    // =========================================================================
    // 4. Separable Brick Grayscale & Color Morphology (Dilation & Erosion)
    // =========================================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int MorphHorizPixel(
        ArrayView1D<uint, Stride1D.Dense> src, int rowOff, int w, int rx, int dilateFlag, int x)
    {
        int x0 = x - rx;
        if (x0 < 0) x0 = 0;
        int x1 = x + rx;
        if (x1 >= w) x1 = w - 1;

        if (dilateFlag != 0) {
            int best = 0;
            for (int j = x0; j <= x1; j++) {
                int v = GpuPixelDevice.GetByte(src, rowOff, j);
                if (v > best) best = v;
            }
            return best;
        } else {
            int best = 255;
            for (int j = x0; j <= x1; j++) {
                int v = GpuPixelDevice.GetByte(src, rowOff, j);
                if (v < best) best = v;
            }
            return best;
        }
    }

    public static void MorphGrayHorizWordKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> dst,
        int w, int wpls, int wpld, int rx, int dilateFlag)
    {
        int idx = index;
        int y = idx / wpld;
        int wordX = idx - y * wpld;
        int baseX = wordX << 2;
        int rowOff = y * wpls;

        int b0 = MorphHorizPixel(src, rowOff, w, rx, dilateFlag, baseX);
        int b1 = baseX + 1 < w ? MorphHorizPixel(src, rowOff, w, rx, dilateFlag, baseX + 1) : 0;
        int b2 = baseX + 2 < w ? MorphHorizPixel(src, rowOff, w, rx, dilateFlag, baseX + 2) : 0;
        int b3 = baseX + 3 < w ? MorphHorizPixel(src, rowOff, w, rx, dilateFlag, baseX + 3) : 0;

        dst[idx] = GpuPixelDevice.Pack4Bytes(b0, b1, b2, b3);
    }

    public static void MorphGrayVertWordKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> dst,
        int w, int h, int wpls, int wpld, int ry, int dilateFlag)
    {
        int idx = index;
        int y = idx / wpld;
        int wordX = idx - y * wpld;
        int baseX = wordX << 2;

        int y0 = y - ry;
        if (y0 < 0) y0 = 0;
        int y1 = y + ry;
        if (y1 >= h) y1 = h - 1;

        int b0, b1, b2, b3;
        if (dilateFlag != 0) {
            b0 = 0; b1 = 0; b2 = 0; b3 = 0;
            for (int r = y0; r <= y1; r++) {
                uint word = src[r * wpls + wordX];
                int v0 = (int)(word >> 24);
                int v1 = (int)((word >> 16) & 0xffu);
                int v2 = (int)((word >> 8) & 0xffu);
                int v3 = (int)(word & 0xffu);
                if (v0 > b0) b0 = v0;
                if (v1 > b1) b1 = v1;
                if (v2 > b2) b2 = v2;
                if (v3 > b3) b3 = v3;
            }
        } else {
            b0 = 255; b1 = 255; b2 = 255; b3 = 255;
            for (int r = y0; r <= y1; r++) {
                uint word = src[r * wpls + wordX];
                int v0 = (int)(word >> 24);
                int v1 = (int)((word >> 16) & 0xffu);
                int v2 = (int)((word >> 8) & 0xffu);
                int v3 = (int)(word & 0xffu);
                if (v0 < b0) b0 = v0;
                if (v1 < b1) b1 = v1;
                if (v2 < b2) b2 = v2;
                if (v3 < b3) b3 = v3;
            }
        }
        if (baseX + 1 >= w) b1 = 0;
        if (baseX + 2 >= w) b2 = 0;
        if (baseX + 3 >= w) b3 = 0;

        dst[idx] = GpuPixelDevice.Pack4Bytes(b0, b1, b2, b3);
    }

    public static void MorphColorHorizKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> dst,
        int w, int wpls, int wpld, int rx, int dilateFlag)
    {
        int idx = index;
        int y = idx / w;
        int x = idx - y * w;
        int rowOff = y * wpls;
        int x0 = x - rx < 0 ? 0 : x - rx;
        int x1 = x + rx >= w ? w - 1 : x + rx;

        int br, bg, bb;
        if (dilateFlag != 0) {
            br = 0; bg = 0; bb = 0;
            for (int j = x0; j <= x1; j++) {
                uint px = src[rowOff + j];
                int vr = (int)(px >> 24);
                int vg = (int)((px >> 16) & 0xffu);
                int vb = (int)((px >> 8) & 0xffu);
                if (vr > br) br = vr;
                if (vg > bg) bg = vg;
                if (vb > bb) bb = vb;
            }
        } else {
            br = 255; bg = 255; bb = 255;
            for (int j = x0; j <= x1; j++) {
                uint px = src[rowOff + j];
                int vr = (int)(px >> 24);
                int vg = (int)((px >> 16) & 0xffu);
                int vb = (int)((px >> 8) & 0xffu);
                if (vr < br) br = vr;
                if (vg < bg) bg = vg;
                if (vb < bb) bb = vb;
            }
        }

        dst[y * wpld + x] = GpuPixelDevice.ComposeRgb(br, bg, bb);
    }

    public static void MorphColorVertKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> dst,
        int w, int h, int wpls, int wpld, int ry, int dilateFlag)
    {
        int idx = index;
        int y = idx / w;
        int x = idx - y * w;
        int y0 = y - ry < 0 ? 0 : y - ry;
        int y1 = y + ry >= h ? h - 1 : y + ry;

        int br, bg, bb;
        if (dilateFlag != 0) {
            br = 0; bg = 0; bb = 0;
            for (int r = y0; r <= y1; r++) {
                uint px = src[r * wpls + x];
                int vr = (int)(px >> 24);
                int vg = (int)((px >> 16) & 0xffu);
                int vb = (int)((px >> 8) & 0xffu);
                if (vr > br) br = vr;
                if (vg > bg) bg = vg;
                if (vb > bb) bb = vb;
            }
        } else {
            br = 255; bg = 255; bb = 255;
            for (int r = y0; r <= y1; r++) {
                uint px = src[r * wpls + x];
                int vr = (int)(px >> 24);
                int vg = (int)((px >> 16) & 0xffu);
                int vb = (int)((px >> 8) & 0xffu);
                if (vr < br) br = vr;
                if (vg < bg) bg = vg;
                if (vb < bb) bb = vb;
            }
        }

        dst[y * wpld + x] = GpuPixelDevice.ComposeRgb(br, bg, bb);
    }

    // =========================================================================
    // 5. VRAM Pipeline Pointwise Utility Kernels
    // =========================================================================

    public static void InvertWordsKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> data,
        int wpl, int lastWordMask, int clearAlphaMask)
    {
        int idx = index;
        int col = idx % wpl;
        uint val = ~data[idx];
        if (clearAlphaMask != 0)
            val &= 0xffffff00u;
        if (col == wpl - 1)
            val &= (uint)lastWordMask;
        data[idx] = val;
    }
}
