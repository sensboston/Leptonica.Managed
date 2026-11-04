using System;
using System.Collections.Generic;
using System.Numerics;
using ILGPU;
using ILGPU.Runtime;

namespace Leptonica.Gpu;

/// <summary>
/// Rentable 1D dense GPU buffer wrapper that returns its underlying <see cref="MemoryBuffer1D{T, TStride}"/>
/// to <see cref="GpuBufferPool"/> upon disposal.
/// </summary>
internal sealed class PooledGpuBuffer<T> : IDisposable
    where T : unmanaged
{
    private readonly GpuBufferPool _pool;
    private MemoryBuffer1D<T, Stride1D.Dense>? _buffer;
    private readonly int _bucketIndex;

    internal PooledGpuBuffer(GpuBufferPool pool, MemoryBuffer1D<T, Stride1D.Dense> buffer, long logicalLength, int bucketIndex)
    {
        _pool = pool;
        _buffer = buffer;
        LogicalLength = logicalLength;
        _bucketIndex = bucketIndex;
    }

    /// <summary>Requested element count (may be smaller than underlying power-of-two capacity).</summary>
    public long LogicalLength { get; }

    /// <summary>Underlying ILGPU memory buffer.</summary>
    public MemoryBuffer1D<T, Stride1D.Dense> Buffer =>
        _buffer ?? throw new ObjectDisposedException(nameof(PooledGpuBuffer<T>));

    /// <summary>View sliced to <see cref="LogicalLength"/> elements.</summary>
    public ArrayView1D<T, Stride1D.Dense> View => Buffer.View.SubView(0, LogicalLength);

    /// <summary>Returns the underlying buffer to the pool; further use of <see cref="Buffer"/> throws.</summary>
    public void Dispose()
    {
        MemoryBuffer1D<T, Stride1D.Dense>? buf = _buffer;
        if (buf != null) {
            _buffer = null;
            _pool.Return(buf, _bucketIndex);
        }
    }
}

/// <summary>
/// Thread-safe power-of-two bucketed VRAM pool for <c>uint</c> and <c>float</c> device buffers.
/// Eliminates repeated <c>cudaMalloc</c> / <c>clCreateBuffer</c> allocations across OCR pipelines.
/// </summary>
internal sealed class GpuBufferPool : IDisposable
{
    private const int MinBucketShift = 10; // 1024 elements min bucket
    private const int MaxBucketShift = 28; // 256M elements max pooled bucket
    private const int BucketCount = MaxBucketShift - MinBucketShift + 1;

    private readonly Accelerator _accelerator;
    private readonly long _maxPooledBytes;
    private readonly object _lock = new();
    private readonly Stack<MemoryBuffer1D<uint, Stride1D.Dense>>[] _uintBuckets;
    private readonly Stack<MemoryBuffer1D<float, Stride1D.Dense>>[] _floatBuckets;
    private long _pooledBytes;
    private bool _disposed;

    public GpuBufferPool(Accelerator accelerator, long maxPooledBytes = 256L * 1024 * 1024)
    {
        _accelerator = accelerator;
        _maxPooledBytes = Math.Max(0, maxPooledBytes);
        _uintBuckets = new Stack<MemoryBuffer1D<uint, Stride1D.Dense>>[BucketCount];
        _floatBuckets = new Stack<MemoryBuffer1D<float, Stride1D.Dense>>[BucketCount];
        for (int i = 0; i < BucketCount; i++) {
            _uintBuckets[i] = new Stack<MemoryBuffer1D<uint, Stride1D.Dense>>();
            _floatBuckets[i] = new Stack<MemoryBuffer1D<float, Stride1D.Dense>>();
        }
    }

    public PooledGpuBuffer<uint> RentUInt32(long length)
    {
        if (length <= 0) length = 1;
        int bucket = GetBucketIndex(length, out long capacity);
        MemoryBuffer1D<uint, Stride1D.Dense>? buf = null;
        if (bucket >= 0) {
            lock (_lock) {
                if (!_disposed && _uintBuckets[bucket].Count > 0) {
                    buf = _uintBuckets[bucket].Pop();
                    _pooledBytes -= capacity * sizeof(uint);
                }
            }
        }
        buf ??= _accelerator.Allocate1D<uint>(capacity);
        // Pooled and freshly allocated device memory is not guaranteed to be zero. Kernels that leave
        // padding bits or unused words untouched would otherwise expose data left by earlier operations.
        buf.MemSetToZero();
        return new PooledGpuBuffer<uint>(this, buf, length, bucket);
    }

    public PooledGpuBuffer<float> RentFloat32(long length)
    {
        if (length <= 0) length = 1;
        int bucket = GetBucketIndex(length, out long capacity);
        MemoryBuffer1D<float, Stride1D.Dense>? buf = null;
        if (bucket >= 0) {
            lock (_lock) {
                if (!_disposed && _floatBuckets[bucket].Count > 0) {
                    buf = _floatBuckets[bucket].Pop();
                    _pooledBytes -= capacity * sizeof(float);
                }
            }
        }
        buf ??= _accelerator.Allocate1D<float>(capacity);
        buf.MemSetToZero();
        return new PooledGpuBuffer<float>(this, buf, length, bucket);
    }

    internal void Return<T>(MemoryBuffer1D<T, Stride1D.Dense> buffer, int bucketIndex)
        where T : unmanaged
    {
        if (bucketIndex < 0) {
            buffer.Dispose();
            return;
        }

        long bytes = buffer.LengthInBytes;
        bool returned = false;
        lock (_lock) {
            if (!_disposed && _pooledBytes + bytes <= _maxPooledBytes) {
                if (typeof(T) == typeof(uint)) {
                    _uintBuckets[bucketIndex].Push((MemoryBuffer1D<uint, Stride1D.Dense>)(object)buffer);
                    _pooledBytes += bytes;
                    returned = true;
                } else if (typeof(T) == typeof(float)) {
                    _floatBuckets[bucketIndex].Push((MemoryBuffer1D<float, Stride1D.Dense>)(object)buffer);
                    _pooledBytes += bytes;
                    returned = true;
                }
            }
        }

        if (!returned)
            buffer.Dispose();
    }

    private static int GetBucketIndex(long length, out long capacity)
    {
        ulong minCap = 1UL << MinBucketShift;
        if ((ulong)length <= minCap) {
            capacity = (long)minCap;
            return 0;
        }
        ulong maxCap = 1UL << MaxBucketShift;
        if ((ulong)length > maxCap) {
            capacity = length;
            return -1;
        }
        ulong pow2 = BitOperations.RoundUpToPowerOf2((ulong)length);
        int shift = BitOperations.TrailingZeroCount(pow2);
        capacity = (long)pow2;
        return shift - MinBucketShift;
    }

    public void Dispose()
    {
        lock (_lock) {
            if (_disposed) return;
            _disposed = true;
            for (int i = 0; i < BucketCount; i++) {
                while (_uintBuckets[i].Count > 0)
                    _uintBuckets[i].Pop().Dispose();
                while (_floatBuckets[i].Count > 0)
                    _floatBuckets[i].Pop().Dispose();
            }
            _pooledBytes = 0;
        }
    }
}
