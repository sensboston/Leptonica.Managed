using System.Runtime.CompilerServices;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;

namespace Leptonica.Gpu.Kernels;

/// <summary>
/// IEEE-754 round-to-nearest-even (<c>.rn</c>) non-contracting floating-point primitives for ILGPU kernels.
/// On CUDA, ILGPU emits <c>add.f32</c>/<c>sub.f32</c>/<c>mul.f32</c> (and <c>.f64</c>) without an explicit
/// rounding modifier, allowing <c>ptxas</c> to fuse adjacent multiply and add/sub instructions into
/// <c>fma.rn</c> (<c>FFMA</c>/<c>DFMA</c>), which differs by 1 ULP from sequential IEEE-754 CPU operations.
/// Emitting explicit <c>.rn</c> PTX instructions forbids FMA contraction and guarantees bit-exact parity.
/// </summary>
internal static class GpuIeeeMath
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float AddF32(float a, float b)
    {
        if (CudaAsm.IsSupported) {
            CudaAsm.Emit("add.rn.f32 %0, %1, %2;", out float r, a, b);
            return r;
        }
        return a + b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SubF32(float a, float b)
    {
        if (CudaAsm.IsSupported) {
            CudaAsm.Emit("sub.rn.f32 %0, %1, %2;", out float r, a, b);
            return r;
        }
        return a - b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float MulF32(float a, float b)
    {
        if (CudaAsm.IsSupported) {
            CudaAsm.Emit("mul.rn.f32 %0, %1, %2;", out float r, a, b);
            return r;
        }
        return a * b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DivF32(float a, float b)
    {
        if (CudaAsm.IsSupported) {
            CudaAsm.Emit("div.rn.f32 %0, %1, %2;", out float r, a, b);
            return r;
        }
        return a / b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SqrtF32(float a)
    {
        if (CudaAsm.IsSupported) {
            CudaAsm.Emit("sqrt.rn.f32 %0, %1;", out float r, a);
            return r;
        }
        return XMath.Sqrt(a);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double AddF64(double a, double b)
    {
        if (CudaAsm.IsSupported) {
            CudaAsm.Emit("add.rn.f64 %0, %1, %2;", out double r, a, b);
            return r;
        }
        return a + b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double SubF64(double a, double b)
    {
        if (CudaAsm.IsSupported) {
            CudaAsm.Emit("sub.rn.f64 %0, %1, %2;", out double r, a, b);
            return r;
        }
        return a - b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double MulF64(double a, double b)
    {
        if (CudaAsm.IsSupported) {
            CudaAsm.Emit("mul.rn.f64 %0, %1, %2;", out double r, a, b);
            return r;
        }
        return a * b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double DivF64(double a, double b)
    {
        if (CudaAsm.IsSupported) {
            CudaAsm.Emit("div.rn.f64 %0, %1, %2;", out double r, a, b);
            return r;
        }
        return a / b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double SqrtF64(double a)
    {
        if (CudaAsm.IsSupported) {
            CudaAsm.Emit("sqrt.rn.f64 %0, %1;", out double r, a);
            return r;
        }
        return XMath.Sqrt(a);
    }
}

/// <summary>
/// Pure register-resident 16-bin histogram structure for allocation-free, shared-memory-free
/// exact two-pass 8-bit order-statistic (rank / median) selection on CUDA, OpenCL, and CPU.
/// </summary>
internal struct Bins16
{
    public int B0, B1, B2, B3, B4, B5, B6, B7;
    public int B8, B9, B10, B11, B12, B13, B14, B15;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        B0 = 0; B1 = 0; B2 = 0; B3 = 0; B4 = 0; B5 = 0; B6 = 0; B7 = 0;
        B8 = 0; B9 = 0; B10 = 0; B11 = 0; B12 = 0; B13 = 0; B14 = 0; B15 = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Increment(int bin)
    {
        switch (bin) {
            case 0: B0++; break;
            case 1: B1++; break;
            case 2: B2++; break;
            case 3: B3++; break;
            case 4: B4++; break;
            case 5: B5++; break;
            case 6: B6++; break;
            case 7: B7++; break;
            case 8: B8++; break;
            case 9: B9++; break;
            case 10: B10++; break;
            case 11: B11++; break;
            case 12: B12++; break;
            case 13: B13++; break;
            case 14: B14++; break;
            default: B15++; break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int SelectBin(int targetRank, out int prevSum)
    {
        int s = 0;
        if (s + B0 > targetRank) { prevSum = s; return 0; } s += B0;
        if (s + B1 > targetRank) { prevSum = s; return 1; } s += B1;
        if (s + B2 > targetRank) { prevSum = s; return 2; } s += B2;
        if (s + B3 > targetRank) { prevSum = s; return 3; } s += B3;
        if (s + B4 > targetRank) { prevSum = s; return 4; } s += B4;
        if (s + B5 > targetRank) { prevSum = s; return 5; } s += B5;
        if (s + B6 > targetRank) { prevSum = s; return 6; } s += B6;
        if (s + B7 > targetRank) { prevSum = s; return 7; } s += B7;
        if (s + B8 > targetRank) { prevSum = s; return 8; } s += B8;
        if (s + B9 > targetRank) { prevSum = s; return 9; } s += B9;
        if (s + B10 > targetRank) { prevSum = s; return 10; } s += B10;
        if (s + B11 > targetRank) { prevSum = s; return 11; } s += B11;
        if (s + B12 > targetRank) { prevSum = s; return 12; } s += B12;
        if (s + B13 > targetRank) { prevSum = s; return 13; } s += B13;
        if (s + B14 > targetRank) { prevSum = s; return 14; } s += B14;
        prevSum = s;
        return 15;
    }
}

/// <summary>
/// Big-endian-within-word pixel extraction/packing and coordinate boundary helpers for ILGPU kernels,
/// matching Leptonica's exact <c>uint[]</c> raster representation.
/// </summary>
internal static class GpuPixelDevice
{
    /// <summary>
    /// Matches <c>Pix.AddMirroredBorder</c> half-sample reflection:
    /// <c>-1 -&gt; 0</c>, <c>-k -&gt; k - 1</c>, <c>n -&gt; n - 1</c>, <c>n + k -&gt; n - 1 - k</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int MirrorCoord(int c, int limit)
    {
        if (c < 0)
            c = -1 - c;
        else if (c >= limit)
            c = 2 * limit - 1 - c;
        if (c < 0)
            return 0;
        if (c >= limit)
            return limit - 1;
        return c;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ClampCoord(int c, int limit) =>
        c < 0 ? 0 : (c >= limit ? limit - 1 : c);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ClampByte(int v) =>
        v < 0 ? 0 : (v > 255 ? 255 : v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ClampUShort(int v) =>
        v < 0 ? 0 : (v > 65535 ? 65535 : v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetByte(ArrayView1D<uint, Stride1D.Dense> data, int rowOffset, int x)
    {
        uint word = data[rowOffset + (x >> 2)];
        int shift = 24 - ((x & 3) << 3);
        return (int)((word >> shift) & 0xffu);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetTwoBytes(ArrayView1D<uint, Stride1D.Dense> data, int rowOffset, int x)
    {
        uint word = data[rowOffset + (x >> 1)];
        int shift = 16 - ((x & 1) << 4);
        return (int)((word >> shift) & 0xffffu);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetQuad(ArrayView1D<uint, Stride1D.Dense> data, int rowOffset, int x)
    {
        uint word = data[rowOffset + (x >> 3)];
        int shift = 28 - ((x & 7) << 2);
        return (int)((word >> shift) & 0x0fu);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetDibit(ArrayView1D<uint, Stride1D.Dense> data, int rowOffset, int x)
    {
        uint word = data[rowOffset + (x >> 4)];
        int shift = 30 - ((x & 15) << 1);
        return (int)((word >> shift) & 0x03u);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetBit(ArrayView1D<uint, Stride1D.Dense> data, int rowOffset, int x)
    {
        uint word = data[rowOffset + (x >> 5)];
        int shift = 31 - (x & 31);
        return (int)((word >> shift) & 1u);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Pack4Bytes(int b0, int b1, int b2, int b3) =>
        (uint)(((b0 & 0xff) << 24) | ((b1 & 0xff) << 16) | ((b2 & 0xff) << 8) | (b3 & 0xff));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Pack2Shorts(int s0, int s1) =>
        (uint)(((s0 & 0xffff) << 16) | (s1 & 0xffff));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ComposeRgb(int r, int g, int b) =>
        (uint)(((r & 0xff) << 24) | ((g & 0xff) << 16) | ((b & 0xff) << 8));
}
