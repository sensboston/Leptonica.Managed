using System.Runtime.CompilerServices;
using ILGPU;
using ILGPU.Runtime;

namespace Leptonica.Gpu.Kernels;

/// <summary>
/// ILGPU device kernels for backward-mapped geometric transformations (<c>Affine</c>, <c>Bilinear</c>,
/// <c>Projective</c>, <c>RotateAM</c>, <c>ScaleLI</c>, fused single-pass <c>DewarpApplyDisparity</c>),
/// Sauvola adaptive thresholding, multi-angle skew sweep, and binary template correlation.
/// </summary>
internal static class GeometryOcrKernels
{
    public const int WarpModeAffine = 0;
    public const int WarpModeBilinear = 1;
    public const int WarpModeProjective = 2;

    internal readonly struct WarpParams
    {
        public readonly int W, H, Wpls, Wpld;
        public readonly int Mode, Sampled;
        public readonly uint FillVal;
        public readonly float Vc0, Vc1, Vc2, Vc3, Vc4, Vc5, Vc6, Vc7;

        public WarpParams(
            int w, int h, int wpls, int wpld,
            int mode, int sampled, uint fillVal,
            float vc0, float vc1, float vc2, float vc3,
            float vc4, float vc5, float vc6, float vc7)
        {
            W = w; H = h; Wpls = wpls; Wpld = wpld;
            Mode = mode; Sampled = sampled; FillVal = fillVal;
            Vc0 = vc0; Vc1 = vc1; Vc2 = vc2; Vc3 = vc3;
            Vc4 = vc4; Vc5 = vc5; Vc6 = vc6; Vc7 = vc7;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int PopCount32(uint v)
    {
        v -= (v >> 1) & 0x55555555u;
        v = (v & 0x33333333u) + ((v >> 2) & 0x33333333u);
        return (int)((((v + (v >> 4)) & 0x0f0f0f0fu) * 0x01010101u) >> 24);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EvalWarpCoords(WarpParams p, int j, int i, out float sx, out float sy)
    {
        float jf = (float)j;
        float iFloat = (float)i;
        if (p.Mode == WarpModeAffine) {
            sx = GpuIeeeMath.AddF32(GpuIeeeMath.AddF32(GpuIeeeMath.MulF32(p.Vc0, jf), GpuIeeeMath.MulF32(p.Vc1, iFloat)), p.Vc2);
            sy = GpuIeeeMath.AddF32(GpuIeeeMath.AddF32(GpuIeeeMath.MulF32(p.Vc3, jf), GpuIeeeMath.MulF32(p.Vc4, iFloat)), p.Vc5);
            return 1;
        }
        if (p.Mode == WarpModeBilinear) {
            float sxLin = GpuIeeeMath.AddF32(GpuIeeeMath.MulF32(p.Vc0, jf), GpuIeeeMath.MulF32(p.Vc1, iFloat));
            float sxCross = GpuIeeeMath.MulF32(GpuIeeeMath.MulF32(p.Vc2, jf), iFloat);
            sx = GpuIeeeMath.AddF32(GpuIeeeMath.AddF32(sxLin, sxCross), p.Vc3);

            float syLin = GpuIeeeMath.AddF32(GpuIeeeMath.MulF32(p.Vc4, jf), GpuIeeeMath.MulF32(p.Vc5, iFloat));
            float syCross = GpuIeeeMath.MulF32(GpuIeeeMath.MulF32(p.Vc6, jf), iFloat);
            sy = GpuIeeeMath.AddF32(GpuIeeeMath.AddF32(syLin, syCross), p.Vc7);
            return 1;
        }
        float denom = GpuIeeeMath.AddF32(GpuIeeeMath.AddF32(GpuIeeeMath.MulF32(p.Vc6, jf), GpuIeeeMath.MulF32(p.Vc7, iFloat)), 1.0f);
        if (denom == 0.0f) {
            sx = -1.0f;
            sy = -1.0f;
            return 0;
        }
        float inv = (float)GpuIeeeMath.DivF64(1.0, (double)denom);
        float nx = GpuIeeeMath.AddF32(GpuIeeeMath.AddF32(GpuIeeeMath.MulF32(p.Vc0, jf), GpuIeeeMath.MulF32(p.Vc1, iFloat)), p.Vc2);
        float ny = GpuIeeeMath.AddF32(GpuIeeeMath.AddF32(GpuIeeeMath.MulF32(p.Vc3, jf), GpuIeeeMath.MulF32(p.Vc4, iFloat)), p.Vc5);
        sx = GpuIeeeMath.MulF32(inv, nx);
        sy = GpuIeeeMath.MulF32(inv, ny);
        return 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int RoundWarpSampledCoord(int mode, float coord) =>
        mode == WarpModeProjective
            ? (int)GpuIeeeMath.AddF32(coord, 0.5f)
            : (int)GpuIeeeMath.AddF64((double)coord, 0.5);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SampleOrInterpGray(
        ArrayView1D<uint, Stride1D.Dense> src, WarpParams p, int j, int i)
    {
        int fill = (int)(p.FillVal & 0xffu);
        if (EvalWarpCoords(p, j, i, out float x, out float y) == 0)
            return fill;

        if (p.Sampled != 0) {
            int xi = RoundWarpSampledCoord(p.Mode, x);
            int yi = RoundWarpSampledCoord(p.Mode, y);
            if ((uint)xi >= (uint)p.W || (uint)yi >= (uint)p.H)
                return fill;
            return GpuPixelDevice.GetByte(src, yi * p.Wpls, xi);
        }

        if (!(x >= 0.0f && y >= 0.0f && x < (float)p.W && y < (float)p.H))
            return fill;

        int xpm = (int)GpuIeeeMath.MulF64(16.0, (double)x);
        int ypm = (int)GpuIeeeMath.MulF64(16.0, (double)y);
        int xp = xpm >> 4;
        int xp2 = xp + 1 < p.W ? xp + 1 : xp;
        int yp = ypm >> 4;
        int wpls = yp + 1 >= p.H ? 0 : p.Wpls;
        int xf = xpm & 0x0f;
        int yf = ypm & 0x0f;
        int wx0 = 16 - xf;
        int wy0 = 16 - yf;

        // Match upstream C Leptonica / Affine.cs NOTE(upstream): when yp + 1 >= p.H, wpls == 0 so r0 == 0.
        int r0 = yp * wpls;
        int r1 = r0 + wpls;
        int v00 = wx0 * wy0 * GpuPixelDevice.GetByte(src, r0, xp);
        int v10 = xf * wy0 * GpuPixelDevice.GetByte(src, r0, xp2);
        int v01 = wx0 * yf * GpuPixelDevice.GetByte(src, r1, xp);
        int v11 = xf * yf * GpuPixelDevice.GetByte(src, r1, xp2);
        return (v00 + v01 + v10 + v11) >> 8;
    }

    // =========================================================================
    // 1. Unified Backward-Mapped Warps (Affine, Bilinear, Projective)
    // =========================================================================

    public static void WarpBinaryWordKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> dst,
        WarpParams p)
    {
        int idx = index;
        int i = idx / p.Wpld;
        int wordX = idx - i * p.Wpld;
        int baseX = wordX << 5;
        int fillBit = (int)(p.FillVal & 1u);

        uint outWord = fillBit != 0 ? 0xffffffffu : 0u;
        for (int b = 0; b < 32; b++) {
            int j = baseX + b;
            if (j >= p.W)
                break;
            int bit;
            if (EvalWarpCoords(p, j, i, out float x, out float y) == 0) {
                bit = fillBit;
            } else {
                int xi = RoundWarpSampledCoord(p.Mode, x);
                int yi = RoundWarpSampledCoord(p.Mode, y);
                if ((uint)xi >= (uint)p.W || (uint)yi >= (uint)p.H)
                    bit = fillBit;
                else
                    bit = GpuPixelDevice.GetBit(src, yi * p.Wpls, xi);
            }
            uint mask = 0x80000000u >> b;
            if (bit != 0)
                outWord |= mask;
            else
                outWord &= ~mask;
        }

        dst[idx] = outWord;
    }

    public static void WarpGrayWordKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> dst,
        WarpParams p)
    {
        int idx = index;
        int i = idx / p.Wpld;
        int wordX = idx - i * p.Wpld;
        int baseX = wordX << 2;
        int tailFill = (int)(p.FillVal & 0xffu);

        int b0 = SampleOrInterpGray(src, p, baseX, i);
        int b1 = baseX + 1 < p.W ? SampleOrInterpGray(src, p, baseX + 1, i) : tailFill;
        int b2 = baseX + 2 < p.W ? SampleOrInterpGray(src, p, baseX + 2, i) : tailFill;
        int b3 = baseX + 3 < p.W ? SampleOrInterpGray(src, p, baseX + 3, i) : tailFill;

        dst[idx] = GpuPixelDevice.Pack4Bytes(b0, b1, b2, b3);
    }

    public static void WarpColorKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> dst,
        WarpParams p)
    {
        int idx = index;
        int i = idx / p.W;
        int j = idx - i * p.W;

        if (EvalWarpCoords(p, j, i, out float x, out float y) == 0) {
            dst[i * p.Wpld + j] = p.FillVal;
            return;
        }

        if (p.Sampled != 0) {
            int xi = RoundWarpSampledCoord(p.Mode, x);
            int yi = RoundWarpSampledCoord(p.Mode, y);
            dst[i * p.Wpld + j] = ((uint)xi < (uint)p.W && (uint)yi < (uint)p.H)
                ? src[yi * p.Wpls + xi]
                : p.FillVal;
            return;
        }

        if (!(x >= 0.0f && y >= 0.0f && x < (float)p.W && y < (float)p.H)) {
            dst[i * p.Wpld + j] = p.FillVal;
            return;
        }

        int xpm = (int)GpuIeeeMath.MulF64(16.0, (double)x);
        int ypm = (int)GpuIeeeMath.MulF64(16.0, (double)y);
        int xp = xpm >> 4;
        int xp2 = xp + 1 < p.W ? xp + 1 : xp;
        int yp = ypm >> 4;
        int wpls = yp + 1 >= p.H ? 0 : p.Wpls;
        int xf = xpm & 0x0f;
        int yf = ypm & 0x0f;
        int wx0 = 16 - xf;
        int wy0 = 16 - yf;
        uint w00 = (uint)(wx0 * wy0);
        uint w10 = (uint)(xf * wy0);
        uint w01 = (uint)(wx0 * yf);
        uint w11 = (uint)(xf * yf);

        // Match upstream C Leptonica / Affine.cs NOTE(upstream): when yp + 1 >= p.H, wpls == 0 so lines == 0.
        int lines = yp * wpls;
        uint word00 = src[lines + xp];
        uint word10 = src[lines + xp2];
        uint word01 = src[lines + wpls + xp];
        uint word11 = src[lines + wpls + xp2];

        uint rval = (w00 * (word00 >> 24) + w10 * (word10 >> 24) + w01 * (word01 >> 24) + w11 * (word11 >> 24)) >> 8;
        uint gval = (w00 * ((word00 >> 16) & 0xffu) + w10 * ((word10 >> 16) & 0xffu) + w01 * ((word01 >> 16) & 0xffu) + w11 * ((word11 >> 16) & 0xffu)) >> 8;
        uint bval = (w00 * ((word00 >> 8) & 0xffu) + w10 * ((word10 >> 8) & 0xffu) + w01 * ((word01 >> 8) & 0xffu) + w11 * ((word11 >> 8) & 0xffu)) >> 8;
        dst[i * p.Wpld + j] = (rval << 24) | (gval << 16) | (bval << 8);
    }

    // =========================================================================
    // 2. Area-Mapped Center Rotation (RotateAM Gray & Color)
    // =========================================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EvalRotateAMGray(
        ArrayView1D<uint, Stride1D.Dense> src,
        int wpls, int xcen, int ycen, int wm2, int hm2,
        float cosa, float sina, float ydifSina, float negYdifCosa,
        int j, int fillGray)
    {
        int xdif = xcen - j;
        int xpm = (int)GpuIeeeMath.SubF32(GpuIeeeMath.MulF32((float)(-xdif), cosa), ydifSina);
        int ypm = (int)GpuIeeeMath.AddF32(negYdifCosa, GpuIeeeMath.MulF32((float)xdif, sina));
        int xp = xcen + (xpm >> 4);
        int yp = ycen + (ypm >> 4);
        if ((uint)xp > (uint)wm2 || (uint)yp > (uint)hm2)
            return fillGray;

        int xf = xpm & 0x0f;
        int yf = ypm & 0x0f;
        int wx0 = 16 - xf;
        int wy0 = 16 - yf;
        int r0 = yp * wpls;
        int r1 = r0 + wpls;
        int v00 = wx0 * wy0 * GpuPixelDevice.GetByte(src, r0, xp);
        int v10 = xf * wy0 * GpuPixelDevice.GetByte(src, r0, xp + 1);
        int v01 = wx0 * yf * GpuPixelDevice.GetByte(src, r1, xp);
        int v11 = xf * yf * GpuPixelDevice.GetByte(src, r1, xp + 1);
        return (v00 + v01 + v10 + v11 + 128) >> 8;
    }

    public static void RotateAMGrayWordKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> dst,
        int w, int h, int wpls, int wpld,
        float sina, float cosa, int fillGray)
    {
        int idx = index;
        int i = idx / wpld;
        int wordX = idx - i * wpld;
        int baseX = wordX << 2;

        int xcen = w >> 1;
        int ycen = h >> 1;
        int wm2 = w - 2;
        int hm2 = h - 2;
        int ydif = ycen - i;
        float ydifSina = GpuIeeeMath.MulF32((float)ydif, sina);
        float negYdifCosa = GpuIeeeMath.MulF32((float)(-ydif), cosa);

        int b0 = EvalRotateAMGray(src, wpls, xcen, ycen, wm2, hm2, cosa, sina, ydifSina, negYdifCosa, baseX, fillGray);
        int b1 = baseX + 1 < w ? EvalRotateAMGray(src, wpls, xcen, ycen, wm2, hm2, cosa, sina, ydifSina, negYdifCosa, baseX + 1, fillGray) : fillGray;
        int b2 = baseX + 2 < w ? EvalRotateAMGray(src, wpls, xcen, ycen, wm2, hm2, cosa, sina, ydifSina, negYdifCosa, baseX + 2, fillGray) : fillGray;
        int b3 = baseX + 3 < w ? EvalRotateAMGray(src, wpls, xcen, ycen, wm2, hm2, cosa, sina, ydifSina, negYdifCosa, baseX + 3, fillGray) : fillGray;

        dst[idx] = GpuPixelDevice.Pack4Bytes(b0, b1, b2, b3);
    }

    public static void RotateAMColorKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> dst,
        int w, int h, int wpls, int wpld,
        float sina, float cosa, uint colorval)
    {
        int idx = index;
        int i = idx / w;
        int j = idx - i * w;

        int xcen = w >> 1;
        int ycen = h >> 1;
        int wm2 = w - 2;
        int hm2 = h - 2;
        int xdif = xcen - j;
        int ydif = ycen - i;
        float ydifSina = GpuIeeeMath.MulF32((float)ydif, sina);
        float negYdifCosa = GpuIeeeMath.MulF32((float)(-ydif), cosa);
        int xpm = (int)GpuIeeeMath.SubF32(GpuIeeeMath.MulF32((float)(-xdif), cosa), ydifSina);
        int ypm = (int)GpuIeeeMath.AddF32(negYdifCosa, GpuIeeeMath.MulF32((float)xdif, sina));
        int xp = xcen + (xpm >> 4);
        int yp = ycen + (ypm >> 4);

        if ((uint)xp > (uint)wm2 || (uint)yp > (uint)hm2) {
            dst[i * wpld + j] = colorval;
            return;
        }

        int xf = xpm & 0x0f;
        int yf = ypm & 0x0f;
        int wx0 = 16 - xf;
        int wy0 = 16 - yf;
        uint w00 = (uint)(wx0 * wy0);
        uint w10 = (uint)(xf * wy0);
        uint w01 = (uint)(wx0 * yf);
        uint w11 = (uint)(xf * yf);

        int p0 = yp * wpls + xp;
        uint p00 = src[p0];
        uint p10 = src[p0 + 1];
        uint p01 = src[p0 + wpls];
        uint p11 = src[p0 + wpls + 1];

        uint rb = (w00 * ((p00 >> 8) & 0x00ff00ffu) +
                   w10 * ((p10 >> 8) & 0x00ff00ffu) +
                   w01 * ((p01 >> 8) & 0x00ff00ffu) +
                   w11 * ((p11 >> 8) & 0x00ff00ffu) + 0x00800080u) & 0xff00ff00u;
        uint g = ((w00 * ((p00 >> 16) & 0xffu) +
                   w10 * ((p10 >> 16) & 0xffu) +
                   w01 * ((p01 >> 16) & 0xffu) +
                   w11 * ((p11 >> 16) & 0xffu) + 128u) << 8) & 0x00ff0000u;
        dst[i * wpld + j] = rb | g;
    }

    // =========================================================================
    // 3. Linear-Interpolated Scaling (ScaleLI Gray & Color)
    // =========================================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EvalScaleLIGray(
        ArrayView1D<uint, Stride1D.Dense> src,
        int r0, int r1, int wm2, int wy0, int yf, float scx, int j)
    {
        int xpm = (int)GpuIeeeMath.MulF32(scx, (float)j);
        int xp = xpm >> 4;
        int xf = xpm & 0x0f;
        int wx0 = 16 - xf;
        int xp2 = xp > wm2 ? xp : xp + 1;
        int v00 = wx0 * wy0 * GpuPixelDevice.GetByte(src, r0, xp);
        int v10 = xf * wy0 * GpuPixelDevice.GetByte(src, r0, xp2);
        int v01 = wx0 * yf * GpuPixelDevice.GetByte(src, r1, xp);
        int v11 = xf * yf * GpuPixelDevice.GetByte(src, r1, xp2);
        return (v00 + v01 + v10 + v11 + 128) >> 8;
    }

    public static void ScaleLIGrayWordKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> dst,
        int ws, int hs, int wpls,
        int wd, int wpld, float scx, float scy)
    {
        int idx = index;
        int i = idx / wpld;
        int wordX = idx - i * wpld;
        int baseX = wordX << 2;

        int wm2 = ws - 2;
        int hm2 = hs - 2;
        int ypm = (int)GpuIeeeMath.MulF32(scy, (float)i);
        int yp = ypm >> 4;
        int yf = ypm & 0x0f;
        int wy0 = 16 - yf;
        int r0 = yp * wpls;
        int r1 = yp > hm2 ? r0 : r0 + wpls;

        int b0 = EvalScaleLIGray(src, r0, r1, wm2, wy0, yf, scx, baseX);
        int b1 = baseX + 1 < wd ? EvalScaleLIGray(src, r0, r1, wm2, wy0, yf, scx, baseX + 1) : 0;
        int b2 = baseX + 2 < wd ? EvalScaleLIGray(src, r0, r1, wm2, wy0, yf, scx, baseX + 2) : 0;
        int b3 = baseX + 3 < wd ? EvalScaleLIGray(src, r0, r1, wm2, wy0, yf, scx, baseX + 3) : 0;

        dst[idx] = GpuPixelDevice.Pack4Bytes(b0, b1, b2, b3);
    }

    public static void ScaleLIColorKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> dst,
        int ws, int hs, int wpls,
        int wd, int wpld, float scx, float scy)
    {
        int idx = index;
        int i = idx / wd;
        int j = idx - i * wd;

        int wm2 = ws - 2;
        int hm2 = hs - 2;
        int ypm = (int)GpuIeeeMath.MulF32(scy, (float)i);
        int yp = ypm >> 4;
        int yf = ypm & 0x0f;
        int wy0 = 16 - yf;
        int rowStride = yp > hm2 ? 0 : wpls;

        int xpm = (int)GpuIeeeMath.MulF32(scx, (float)j);
        int xp = xpm >> 4;
        int xf = xpm & 0x0f;
        int wx0 = 16 - xf;
        int colStride = xp > wm2 ? 0 : 1;

        int p0 = yp * wpls + xp;
        uint p00 = src[p0];
        uint p10 = src[p0 + colStride];
        uint p01 = src[p0 + rowStride];
        uint p11 = src[p0 + rowStride + colStride];

        uint w00 = (uint)(wx0 * wy0);
        uint w10 = (uint)(xf * wy0);
        uint w01 = (uint)(wx0 * yf);
        uint w11 = (uint)(xf * yf);

        uint rb = (w00 * ((p00 >> 8) & 0x00ff00ffu) +
                   w10 * ((p10 >> 8) & 0x00ff00ffu) +
                   w01 * ((p01 >> 8) & 0x00ff00ffu) +
                   w11 * ((p11 >> 8) & 0x00ff00ffu) + 0x00800080u) & 0xff00ff00u;
        uint g = ((w00 * ((p00 >> 16) & 0xffu) +
                   w10 * ((p10 >> 16) & 0xffu) +
                   w01 * ((p01 >> 16) & 0xffu) +
                   w11 * ((p11 >> 16) & 0xffu) + 128u) << 8) & 0x00ff0000u;
        dst[i * wpld + j] = rb | g;
    }

    // =========================================================================
    // 4. Fused Single-Pass Document Dewarping (Vertical + Horizontal Disparity)
    // =========================================================================

    internal readonly struct DewarpParams
    {
        public readonly int W, H, Wpls, Wpld;
        public readonly int Wplv, Wplh, HasHoriz, GrayIn;

        public DewarpParams(int w, int h, int wpls, int wpld, int wplv, int wplh, int hasHoriz, int grayIn)
        {
            W = w; H = h; Wpls = wpls; Wpld = wpld;
            Wplv = wplv; Wplh = wplh; HasHoriz = hasHoriz; GrayIn = grayIn;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ResolveDewarpCoords(
        ArrayView1D<float, Stride1D.Dense> vdisp,
        ArrayView1D<float, Stride1D.Dense> hdisp,
        DewarpParams p, int i, int j,
        out int isrc, out int jsrc)
    {
        if (p.HasHoriz != 0) {
            float jf = GpuIeeeMath.SubF32((float)j, hdisp[i * p.Wplh + j]);
            jsrc = (int)GpuIeeeMath.AddF64((double)jf, 0.5);
            if (p.GrayIn < 0)
                jsrc = GpuPixelDevice.ClampCoord(jsrc, p.W);
            else if ((uint)jsrc >= (uint)p.W) {
                isrc = -1;
                return 0;
            }
        } else {
            jsrc = j;
        }

        float iFloat = GpuIeeeMath.SubF32((float)i, vdisp[i * p.Wplv + jsrc]);
        isrc = (int)GpuIeeeMath.AddF64((double)iFloat, 0.5);
        if (p.GrayIn < 0) {
            isrc = GpuPixelDevice.ClampCoord(isrc, p.H);
            return 1;
        }
        return (uint)isrc < (uint)p.H ? 1 : 0;
    }

    public static void DewarpBinaryWordKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> dst,
        ArrayView1D<float, Stride1D.Dense> vdisp,
        ArrayView1D<float, Stride1D.Dense> hdisp,
        DewarpParams p)
    {
        int idx = index;
        // Match Dewarp3.cs / C dewarp3.c: when 0 <= grayin < 128, SetAllGray sets all bits to 1
        // and the disparity loop only ever calls SetDataBit (never clearing 1s).
        if (p.GrayIn >= 0 && p.GrayIn < 128) {
            dst[idx] = 0xffffffffu;
            return;
        }

        int i = idx / p.Wpld;
        int wordX = idx - i * p.Wpld;
        int baseX = wordX << 5;

        uint outWord = 0;
        for (int b = 0; b < 32; b++) {
            int j = baseX + b;
            if (j >= p.W)
                break;
            if (ResolveDewarpCoords(vdisp, hdisp, p, i, j, out int isrc, out int jsrc) != 0 &&
                GpuPixelDevice.GetBit(src, isrc * p.Wpls, jsrc) != 0) {
                outWord |= 0x80000000u >> b;
            }
        }

        dst[idx] = outWord;
    }

    public static void DewarpGrayWordKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> dst,
        ArrayView1D<float, Stride1D.Dense> vdisp,
        ArrayView1D<float, Stride1D.Dense> hdisp,
        DewarpParams p)
    {
        int idx = index;
        int i = idx / p.Wpld;
        int wordX = idx - i * p.Wpld;
        int baseX = wordX << 2;
        int fill = p.GrayIn >= 0 ? GpuPixelDevice.ClampByte(p.GrayIn) : 0;

        int b0 = ResolveDewarpCoords(vdisp, hdisp, p, i, baseX, out int i0, out int j0) != 0
            ? GpuPixelDevice.GetByte(src, i0 * p.Wpls, j0) : fill;
        int b1 = baseX + 1 < p.W
            ? (ResolveDewarpCoords(vdisp, hdisp, p, i, baseX + 1, out int i1, out int j1) != 0 ? GpuPixelDevice.GetByte(src, i1 * p.Wpls, j1) : fill)
            : fill;
        int b2 = baseX + 2 < p.W
            ? (ResolveDewarpCoords(vdisp, hdisp, p, i, baseX + 2, out int i2, out int j2) != 0 ? GpuPixelDevice.GetByte(src, i2 * p.Wpls, j2) : fill)
            : fill;
        int b3 = baseX + 3 < p.W
            ? (ResolveDewarpCoords(vdisp, hdisp, p, i, baseX + 3, out int i3, out int j3) != 0 ? GpuPixelDevice.GetByte(src, i3 * p.Wpls, j3) : fill)
            : fill;

        dst[idx] = GpuPixelDevice.Pack4Bytes(b0, b1, b2, b3);
    }

    public static void DewarpColorKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> src,
        ArrayView1D<uint, Stride1D.Dense> dst,
        ArrayView1D<float, Stride1D.Dense> vdisp,
        ArrayView1D<float, Stride1D.Dense> hdisp,
        DewarpParams p)
    {
        int idx = index;
        int i = idx / p.W;
        int j = idx - i * p.W;
        int g = p.GrayIn >= 0 ? GpuPixelDevice.ClampByte(p.GrayIn) : 0;
        uint fillColor = GpuPixelDevice.ComposeRgb(g, g, g);

        if (ResolveDewarpCoords(vdisp, hdisp, p, i, j, out int isrc, out int jsrc) != 0)
            dst[i * p.Wpld + j] = src[isrc * p.Wpls + jsrc];
        else
            dst[i * p.Wpld + j] = fillColor;
    }

    // =========================================================================
    // 5. Sauvola Local Thresholding & Binary Packing
    // =========================================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EvalSauvolaThresh(int mv, uint ms, float factor, out int sdByte)
    {
        int variance = (int)ms - mv * mv;
        if (variance < 0) {
            sdByte = 0;
            return 0;
        }
        float sd = GpuIeeeMath.SqrtF32((float)variance);
        sdByte = (int)sd & 0xff;
        // Match CPU Binarize.cs: (int)(mv * (1.0 - factor * (1.0 - sd / 128.0))) & 0xff
        double oneMinusSd = GpuIeeeMath.SubF64(1.0, GpuIeeeMath.DivF64((double)sd, 128.0));
        double factorTerm = GpuIeeeMath.MulF64((double)factor, oneMinusSd);
        double scale = GpuIeeeMath.SubF64(1.0, factorTerm);
        double prod = GpuIeeeMath.MulF64((double)mv, scale);
        return (int)prod & 0xff;
    }

    public static void SauvolaThresholdWordKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> pixm,
        ArrayView1D<uint, Stride1D.Dense> pixms,
        ArrayView1D<uint, Stride1D.Dense> pixd,
        ArrayView1D<uint, Stride1D.Dense> pixsd,
        int w, int wplm, int wplms, int wpld, int wantSd, float factor)
    {
        int idx = index;
        int i = idx / wpld;
        int wordX = idx - i * wpld;
        int baseX = wordX << 2;

        uint wm = pixm[i * wplm + wordX];
        int msRow = i * wplms;

        int mv0 = (int)(wm >> 24);
        int t0 = EvalSauvolaThresh(mv0, pixms[msRow + baseX], factor, out int sd0);

        int t1 = 0, sd1 = 0;
        if (baseX + 1 < w)
            t1 = EvalSauvolaThresh((int)((wm >> 16) & 0xffu), pixms[msRow + baseX + 1], factor, out sd1);

        int t2 = 0, sd2 = 0;
        if (baseX + 2 < w)
            t2 = EvalSauvolaThresh((int)((wm >> 8) & 0xffu), pixms[msRow + baseX + 2], factor, out sd2);

        int t3 = 0, sd3 = 0;
        if (baseX + 3 < w)
            t3 = EvalSauvolaThresh((int)(wm & 0xffu), pixms[msRow + baseX + 3], factor, out sd3);

        pixd[idx] = GpuPixelDevice.Pack4Bytes(t0, t1, t2, t3);
        if (wantSd != 0)
            pixsd[idx] = GpuPixelDevice.Pack4Bytes(sd0, sd1, sd2, sd3);
    }

    public static void ApplyLocalThresholdWordKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> pixs,
        ArrayView1D<uint, Stride1D.Dense> pixth,
        ArrayView1D<uint, Stride1D.Dense> pixd,
        int w, int wpls, int wplt, int wpld)
    {
        int idx = index;
        int i = idx / wpld;
        int wordX = idx - i * wpld;
        int baseX = wordX << 5;
        int sRow = i * wpls;
        int tRow = i * wplt;

        uint outWord = 0;
        for (int g = 0; g < 8; g++) {
            int x0 = baseX + (g << 2);
            if (x0 >= w)
                break;
            uint ws = pixs[sRow + (wordX << 3) + g];
            uint wt = pixth[tRow + (wordX << 3) + g];

            int bitBase = g << 2;
            if ((ws >> 24) < (wt >> 24))
                outWord |= 0x80000000u >> bitBase;
            if (x0 + 1 < w && ((ws >> 16) & 0xffu) < ((wt >> 16) & 0xffu))
                outWord |= 0x80000000u >> (bitBase + 1);
            if (x0 + 2 < w && ((ws >> 8) & 0xffu) < ((wt >> 8) & 0xffu))
                outWord |= 0x80000000u >> (bitBase + 2);
            if (x0 + 3 < w && (ws & 0xffu) < (wt & 0xffu))
                outWord |= 0x80000000u >> (bitBase + 3);
        }

        pixd[idx] = outWord;
    }

    // =========================================================================
    // 6. Multi-Angle Skew Sweep (2D Angle x Row PopCount + Variance Reduction)
    // =========================================================================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int StripRowPopCount(
        ArrayView1D<uint, Stride1D.Dense> data, int wpl, int w, int h,
        int x0, int width, int srcRow)
    {
        if ((uint)srcRow >= (uint)h)
            return 0;
        if (x0 < 0) {
            width += x0;
            x0 = 0;
        }
        if (x0 + width > w)
            width = w - x0;
        if (width <= 0)
            return 0;

        int x1 = x0 + width;
        int w0 = x0 >> 5;
        int w1 = (x1 - 1) >> 5;
        uint leftMask = 0xffffffffu >> (x0 & 31);
        int rightRem = x1 & 31;
        uint rightMask = rightRem == 0 ? 0xffffffffu : (0xffffffffu << (32 - rightRem));
        int rowBase = srcRow * wpl;

        if (w0 == w1)
            return PopCount32(data[rowBase + w0] & leftMask & rightMask);

        int sum = PopCount32(data[rowBase + w0] & leftMask);
        for (int k = w0 + 1; k < w1; k++)
            sum += PopCount32(data[rowBase + k]);
        sum += PopCount32(data[rowBase + w1] & rightMask);
        return sum;
    }

    public static void SkewSweepRowCountsKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> data,
        ArrayView1D<float, Stride1D.Dense> invAngles,
        ArrayView1D<uint, Stride1D.Dense> signs,
        ArrayView1D<uint, Stride1D.Dense> rowCounts,
        int w, int h, int wpl, int yMin, int nRows)
    {
        int idx = index;
        int angleIdx = idx / nRows;
        int k = idx - angleIdx * nRows;
        int yd = yMin + k;

        int sign = (int)signs[angleIdx];
        if (sign == 0) {
            int rem = w & 31;
            uint endMask = rem == 0 ? 0xffffffffu : (0xffffffffu << (32 - rem));
            int rowBase = yd * wpl;
            int sum0 = 0;
            for (int c = 0; c < wpl - 1; c++)
                sum0 += PopCount32(data[rowBase + c]);
            sum0 += PopCount32(data[rowBase + wpl - 1] & endMask);
            rowCounts[idx] = (uint)sum0;
            return;
        }

        double invangle = (double)invAngles[angleIdx];
        int initxincr = (int)GpuIeeeMath.DivF64(invangle, 2.0);
        int total = StripRowPopCount(data, wpl, w, h, -initxincr, 2 * initxincr, yd);

        for (int vshift = 1, x = initxincr; x < w; vshift++) {
            double vshiftHalf = GpuIeeeMath.AddF64((double)vshift, 0.5);
            int xincr = (int)GpuIeeeMath.AddF64(GpuIeeeMath.MulF64(invangle, vshiftHalf), 0.5) - x;
            if (w - x < xincr)
                xincr = w - x;
            total += StripRowPopCount(data, wpl, w, h, x, xincr, yd - sign * vshift);
            x += xincr;
        }

        rowCounts[idx] = (uint)total;
    }

    public static void SkewSweepVarianceReduceKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> rowCounts,
        ArrayView1D<float, Stride1D.Dense> scores,
        int nRows)
    {
        int angleIdx = index;
        int baseIdx = angleIdx * nRows;
        float sum = 0.0f;
        float prev = (float)rowCounts[baseIdx];
        for (int k = 1; k < nRows; k++) {
            float cur = (float)rowCounts[baseIdx + k];
            float diff = GpuIeeeMath.SubF32(cur, prev);
            sum = GpuIeeeMath.AddF32(sum, GpuIeeeMath.MulF32(diff, diff));
            prev = cur;
        }
        scores[angleIdx] = sum;
    }

    // =========================================================================
    // 7. 2D Shift-Grid Binary Template Correlation (BestCorrelation)
    // =========================================================================

    public static void BestCorrelationShiftKernel(
        Index1D index,
        ArrayView1D<uint, Stride1D.Dense> data1,
        ArrayView1D<uint, Stride1D.Dense> data2,
        ArrayView1D<uint, Stride1D.Dense> outCounts,
        int wi, int hi, int wpl1,
        int wt, int ht, int wpl2,
        int baseDelX, int baseDelY, int gridSide)
    {
        int idx = index;
        int sy = idx / gridSide;
        int sx = idx - sy * gridSide;
        int idelx = baseDelX + sx;
        int idely = baseDelY + sy;

        int lorow = idely > 0 ? idely : 0;
        int hirow = ht + idely < hi ? ht + idely : hi;
        if (lorow >= hirow) {
            outCounts[idx] = 0;
            return;
        }

        int row1 = wpl1 * lorow;
        int row2 = wpl2 * (lorow - idely);
        int rowwords2 = wpl2;
        int locol = idelx > 0 ? idelx : 0;
        int hicol = wt + idelx < wi ? wt + idelx : wi;

        if (idelx >= 32) {
            int skip = idelx >> 5;
            row1 += skip;
            locol -= skip << 5;
            hicol -= skip << 5;
            idelx &= 31;
        } else if (idelx <= -32) {
            int skip = -((idelx + 31) >> 5);
            row2 += skip;
            rowwords2 -= skip;
            idelx += skip << 5;
        }

        if (locol >= hicol) {
            outCounts[idx] = 0;
            return;
        }

        int rowwords1 = (hicol + 31) >> 5;
        int endbits = hicol & 31;
        uint lastMask = endbits == 0 ? 0xffffffffu : (0xffffffffu << (32 - endbits));
        int count = 0;

        if (idelx == 0) {
            int full = rowwords1 - 1;
            for (int y = lorow; y < hirow; y++, row1 += wpl1, row2 += wpl2) {
                for (int x = 0; x < full; x++)
                    count += PopCount32(data1[row1 + x] & data2[row2 + x]);
                count += PopCount32(data1[row1 + full] & data2[row2 + full] & lastMask);
            }
        } else if (idelx > 0) {
            int rshift = 32 - idelx;
            if (rowwords2 < rowwords1) {
                for (int y = lorow; y < hirow; y++, row1 += wpl1, row2 += wpl2) {
                    count += PopCount32(data1[row1] & (data2[row2] >> idelx));
                    int x;
                    for (x = 1; x < rowwords2; x++) {
                        uint w2 = (data2[row2 + x] >> idelx) | (data2[row2 + x - 1] << rshift);
                        count += PopCount32(data1[row1 + x] & w2);
                    }
                    uint wLast = data2[row2 + x - 1] << rshift;
                    count += PopCount32(data1[row1 + x] & wLast & lastMask);
                }
            } else {
                for (int y = lorow; y < hirow; y++, row1 += wpl1, row2 += wpl2) {
                    if (rowwords1 == 1) {
                        count += PopCount32(data1[row1] & (data2[row2] >> idelx) & lastMask);
                    } else {
                        count += PopCount32(data1[row1] & (data2[row2] >> idelx));
                        int last = rowwords1 - 1;
                        for (int x = 1; x < last; x++) {
                            uint w2 = (data2[row2 + x] >> idelx) | (data2[row2 + x - 1] << rshift);
                            count += PopCount32(data1[row1 + x] & w2);
                        }
                        uint wLast = (data2[row2 + last] >> idelx) | (data2[row2 + last - 1] << rshift);
                        count += PopCount32(data1[row1 + last] & wLast & lastMask);
                    }
                }
            }
        } else {
            int lshift = -idelx;
            int rshift = 32 + idelx;
            int last = rowwords1 - 1;
            if (rowwords1 < rowwords2) {
                for (int y = lorow; y < hirow; y++, row1 += wpl1, row2 += wpl2) {
                    for (int x = 0; x < last; x++) {
                        uint w2 = (data2[row2 + x] << lshift) | (data2[row2 + x + 1] >> rshift);
                        count += PopCount32(data1[row1 + x] & w2);
                    }
                    uint wLast = (data2[row2 + last] << lshift) | (data2[row2 + last + 1] >> rshift);
                    count += PopCount32(data1[row1 + last] & wLast & lastMask);
                }
            } else {
                for (int y = lorow; y < hirow; y++, row1 += wpl1, row2 += wpl2) {
                    for (int x = 0; x < last; x++) {
                        uint w2 = (data2[row2 + x] << lshift) | (data2[row2 + x + 1] >> rshift);
                        count += PopCount32(data1[row1 + x] & w2);
                    }
                    uint wLast = data2[row2 + last] << lshift;
                    count += PopCount32(data1[row1 + last] & wLast & lastMask);
                }
            }
        }

        outCounts[idx] = (uint)count;
    }
}
