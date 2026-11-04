using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

[module: SkipLocalsInit]

namespace Leptonica;

/// <summary>
/// Error raised for a failure that is not caused by invalid arguments
/// (the C library returns an error code or a null result in these cases).
/// </summary>
public class LeptonicaException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="LeptonicaException"/> class with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public LeptonicaException(string message) : base(message) { }
}

/// <summary>
/// Message severity, as in environ.h (L_SEVERITY_*).
/// </summary>
public enum LogSeverity
{
    /// <summary>All messages are reported.</summary>
    All = 1,
    /// <summary>Debugging messages.</summary>
    Debug = 2,
    /// <summary>Informational messages.</summary>
    Info = 3,
    /// <summary>Warnings about unusual but recoverable conditions.</summary>
    Warning = 4,
    /// <summary>Errors.</summary>
    Error = 5,
    /// <summary>No messages are reported.</summary>
    None = 6,
}

/// <summary>
/// Replacement for the L_INFO / L_WARNING / L_ERROR macros of the C library.
/// Messages are dropped unless a sink is installed.
/// </summary>
public static class Log
{
    /// <summary>Messages below this severity are dropped.</summary>
    public static LogSeverity MinSeverity { get; set; } = LogSeverity.Info;

    /// <summary>Receives formatted messages; null drops them.</summary>
    public static Action<LogSeverity, string>? Sink { get; set; }

    /// <summary>Reports an informational message.</summary>
    /// <param name="func">Name of the function that reports the message.</param>
    /// <param name="msg">The message text.</param>
    public static void Info(string func, string msg) => Write(LogSeverity.Info, "Info", func, msg);
    /// <summary>Reports a warning.</summary>
    /// <param name="func">Name of the function that reports the message.</param>
    /// <param name="msg">The message text.</param>
    public static void Warning(string func, string msg) => Write(LogSeverity.Warning, "Warning", func, msg);
    /// <summary>Reports an error message; this does not throw.</summary>
    /// <param name="func">Name of the function that reports the message.</param>
    /// <param name="msg">The message text.</param>
    public static void Error(string func, string msg) => Write(LogSeverity.Error, "Error", func, msg);

    private static void Write(LogSeverity severity, string kind, string func, string msg)
    {
        Action<LogSeverity, string>? sink = Sink;
        if (sink != null && severity >= MinSeverity)
            sink(severity, $"{kind} in {func}: {msg}");
    }
}

/// <summary>
/// Controls CPU multi-threading across independent image bands and parameter sweeps.
/// </summary>
public static class LeptParallel
{
    /// <summary>
    /// Maximum number of worker threads to use for CPU parallel loops.
    /// Set to -1 (default) to use <see cref="Environment.ProcessorCount"/>, or 1 to disable multi-threading.
    /// </summary>
    public static int MaxDegreeOfParallelism { get; set; } = -1;

    /// <summary>
    /// Minimum estimated work units (<c>rows * opsPerRow</c>) required before partitioning across threads.
    /// Prevents thread-pool scheduling overhead on small images or lightweight operations.
    /// </summary>
    public static long MinWorkThreshold { get; set; } = 1_500_000L;

    internal static int EffectiveDop
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            int max = MaxDegreeOfParallelism;
            int procs = Environment.ProcessorCount;
            if (max <= 0 || max > procs)
                return procs;
            return max;
        }
    }

    /// <summary>
    /// Executes <paramref name="body"/> over contiguous row bands <c>[yStart, yEnd)</c> of <c>[0, height)</c>,
    /// running sequentially on the caller thread when estimated work is below <see cref="MinWorkThreshold"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void ForRows<TState>(int height, long opsPerRow, TState state, Action<int, int, TState> body)
    {
        if (height <= 0)
            return;

        int dop = EffectiveDop;
        if (dop <= 1 || height < 4 || (long)height * opsPerRow < MinWorkThreshold)
        {
            body(0, height, state);
            return;
        }

        RunChunked(height, dop, state, body);
    }

    /// <summary>
    /// Executes <paramref name="body"/> over contiguous row bands <c>[yStart, yEnd)</c> of <c>[0, height)</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void ForRows(int height, long opsPerRow, Action<int, int> body)
    {
        if (height <= 0)
            return;

        int dop = EffectiveDop;
        if (dop <= 1 || height < 4 || (long)height * opsPerRow < MinWorkThreshold)
        {
            body(0, height);
            return;
        }

        RunChunkedSimple(height, dop, body);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunChunked<TState>(int height, int dop, TState state, Action<int, int, TState> body)
    {
        int numChunks = Math.Min(height, dop * 2);
        ParallelOptions opts = new ParallelOptions { MaxDegreeOfParallelism = dop };
        Parallel.For(0, numChunks, opts, chunk =>
        {
            int yStart = (int)((long)chunk * height / numChunks);
            int yEnd = (int)((long)(chunk + 1) * height / numChunks);
            if (yStart < yEnd)
                body(yStart, yEnd, state);
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunChunkedSimple(int height, int dop, Action<int, int> body)
    {
        int numChunks = Math.Min(height, dop * 2);
        ParallelOptions opts = new ParallelOptions { MaxDegreeOfParallelism = dop };
        Parallel.For(0, numChunks, opts, chunk =>
        {
            int yStart = (int)((long)chunk * height / numChunks);
            int yEnd = (int)((long)(chunk + 1) * height / numChunks);
            if (yStart < yEnd)
                body(yStart, yEnd);
        });
    }
}

/// <summary>
/// Helpers that build exceptions for the ERROR_INT / ERROR_PTR paths of the C library.
/// The message has the same text as the C one: "Error in {func}: {msg}".
/// </summary>
internal static class Lept
{
    /// <summary>Invalid input (bad parameter values or combinations).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ArgumentException Arg(string func, string msg) => new($"Error in {func}: {msg}");

    /// <summary>Failure that is not about the arguments.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static LeptonicaException Fail(string func, string msg) => new($"Error in {func}: {msg}");

    /// <summary>Stub for a function whose source file is not ported yet.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static NotImplementedException NotPorted(string cname) => new($"leptonica: {cname}");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowArg(string func, string msg) => throw new ArgumentException($"Error in {func}: {msg}");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowFail(string func, string msg) => throw new LeptonicaException($"Error in {func}: {msg}");
}
