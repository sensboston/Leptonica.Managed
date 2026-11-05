using Leptonica;

namespace WebApi;

/// <summary>
/// Starts the GPU (if there is one) in the background when the server starts, so that its
/// one-time start-up cost of about a second does not fall on the first request. Requests that
/// arrive before it is ready simply run on the CPU or wait for the GPU start, with the same results.
/// </summary>
public sealed class GpuWarmupService(ILogger<GpuWarmupService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.Run(() =>
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool gpu = LeptGpu.Warmup();
        if (gpu)
            logger.LogInformation("GPU {Device} ready in {Ms} ms", LeptGpu.ActiveDeviceName, sw.ElapsedMilliseconds);
        else
            logger.LogInformation("No GPU: all operations run on the CPU");
    }, stoppingToken);
}
