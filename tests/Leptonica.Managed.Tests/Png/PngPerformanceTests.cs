using System;
using System.Diagnostics;
using System.IO;
using Leptonica.Internal.Png;
using Xunit.Abstractions;

namespace Leptonica.Managed.Tests.Png;

/* Wall-clock measurements: run alone, not next to the parallel test classes. */
[CollectionDefinition("Performance", DisableParallelization = true)]
public class PerformanceCollection
{
}

[Collection("Performance")]
public class PngPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public PngPerformanceTests(ITestOutputHelper output) { _output = output; }

    [Fact]
    public void FourThousandSquareRgbaRoundTripIsFast()
    {
        const int n = 4000;
        var rows = new byte[n * n * 4];
        // Smooth gradients with a little noise, like a photograph.
        var rng = new Random(42);
        var noise = new byte[n * 4];
        for (int y = 0; y < n; y++)
        {
            rng.NextBytes(noise);
            Span<byte> row = rows.AsSpan(y * n * 4, n * 4);
            for (int x = 0; x < n; x++)
            {
                row[4 * x] = (byte)((x >> 4) + y / 9 + (noise[4 * x] & 3));
                row[4 * x + 1] = (byte)((x >> 5) + y / 7 + (noise[4 * x + 1] & 3));
                row[4 * x + 2] = (byte)((x >> 3) + (y >> 4) + (noise[4 * x + 2] & 3));
                row[4 * x + 3] = 255;
            }
        }
        // Level 1: the time of the default level is dominated by zlib itself (several seconds
        // for 64 MiB of noisy data, as with libpng); this test checks the codec around it.
        var info = new PngWriteInfo { Width = n, Height = n, BitDepth = 8, ColorType = 6, CompressionLevel = 1 };

        // Two runs: the first also pays for JIT compilation and first-touch page faults of
        // the 64 MiB buffers on a shared machine; the better one is compared with the limit.
        long best = long.MaxValue;
        for (int run = 0; run < 2; run++)
        {
            var (encodeMs, totalMs, size, alloc) = TimedRoundTrip(info, rows);
            _output.WriteLine($"run {run}: encode {encodeMs} ms, total {totalMs} ms, png {size} bytes, allocated {alloc >> 20} MiB");

            // Output pixels (64 MiB), compressed data (a few MiB, growing buffer) and a few row
            // buffers; per-pixel allocations would be orders of magnitude more.
            Assert.True(alloc < 4L * rows.Length, $"allocated {alloc >> 20} MiB");
            best = Math.Min(best, totalMs);
            // The test assembly is a Debug build, which keeps dead locals alive; collect the
            // buffers of this run so that the next one reuses the memory.
            GC.Collect();
        }
        // Debug build under the parallel load of the whole test run; measured alone it is
        // well under 2 s. Real performance work is stage 9 (BenchmarkDotNet, Release).
        Assert.True(best < 4000, $"round trip took {best} ms");
    }

    private static (long EncodeMs, long TotalMs, long Size, long Alloc) TimedRoundTrip(PngWriteInfo info, byte[] rows)
    {
        long alloc0 = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        var ms = new MemoryStream();
        PngEncoder.Write(ms, info, rows);
        long encodeMs = sw.ElapsedMilliseconds;
        long size = ms.Length;
        ms.Position = 0;
        PngImage img = PngDecoder.Read(ms, false);
        sw.Stop();
        long alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
        Assert.True(rows.AsSpan().SequenceEqual(img.Pixels));
        return (encodeMs, sw.ElapsedMilliseconds, size, alloc);
    }
}
