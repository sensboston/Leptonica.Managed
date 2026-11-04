using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text;
using Leptonica;
using Leptonica.Gpu;

namespace Bench;

internal sealed class BenchCtx
{
    public Pix Feyn = null!;
    public Pix FeynT3 = null!;
    public Pix FeynRopDst = null!;
    public Pix FeynXorDst = null!;
    public Pix Gray8 = null!;
    public Pix GrayPage = null!;
    public Pix? GrayLucasta;
    public Pix Marge = null!;
    public Pix? Test24;
    public Pix Cmapped = null!;
    public Pix SeedBin = null!;
    public Pix SeedGray = null!;
    public Pix MaskGray = null!;
    public Sel SelHmt = null!;
    public L_Kernel KelGauss = null!;
    public Pta Ptas = null!;
    public Pta Ptad = null!;
    public byte[] Png32 = [];
    public byte[] TiffG4 = [];
    public byte[] JpgBytes = [];

    // Extended codec buffers (initialized only when --extended-codecs is passed)
    public byte[] TiffZip32 = [];
    public byte[] TiffLzw8 = [];
    public byte[] Gif8 = [];
    public byte[] WebpLossy32 = [];
    public byte[] WebpLossless32 = [];
    public byte[] Jp2k32 = [];

    public bool Fail;
}

internal sealed record BenchCase(
    string Name,
    string Function,
    string Input,
    string Params,
    string Category,
    bool IsOcr,
    Action<BenchCtx> Run,
    Func<BenchCtx, Pix?> GetPrimaryPix);

internal readonly record struct CaseResult(
    bool Failed,
    int Runs,
    double MinMs,
    double MedianMs,
    double MeanMs,
    double MadMs,
    bool GpuUsed,
    int GpuDispatchesPerRun);

internal static class Program
{
    public static int Main(string[] args)
    {
        // Silence Leptonica diagnostics and debug file output before anything else (matches bench_native.c).
        Utils.SetMsgSeverity(L.L_SEVERITY_NONE);
        WriteFile.SetLeptDebugOK(false);

        string? progDir = null;
        string outPath = "results_managed.csv";
        string? filter = null;
        double minTimeMs = 1000.0;
        bool extendedCodecs = false;
        bool ocrOnly = false;
        GpuMode gpuMode = GpuMode.Auto;
        GpuBackendPreference gpuBackendPref = GpuBackendPreference.Auto;
        int requestedThreads = -1; // -1 = auto (all logical cores via LeptParallel)

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "-h" or "--help")
            {
                Usage();
                return 0;
            }
            if (arg == "--out" && i + 1 < args.Length)
            {
                outPath = args[++i];
            }
            else if (arg == "--filter" && i + 1 < args.Length)
            {
                filter = args[++i];
            }
            else if (arg == "--min-time-ms" && i + 1 < args.Length)
            {
                if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out minTimeMs) ||
                    !(minTimeMs > 0.0))
                {
                    Console.Error.WriteLine("error: --min-time-ms must be > 0");
                    return 2;
                }
            }
            else if (arg == "--no-gpu")
            {
                gpuMode = GpuMode.CpuOnly;
            }
            else if (arg == "--gpu" && i + 1 < args.Length)
            {
                string gm = args[++i].Trim();
                if (gm.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    gpuMode = GpuMode.Auto;
                }
                else if (gm.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                         gm.Equals("cpu", StringComparison.OrdinalIgnoreCase) ||
                         gm.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                         gm.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                         gm.Equals("disabled", StringComparison.OrdinalIgnoreCase) ||
                         gm == "0")
                {
                    gpuMode = GpuMode.CpuOnly;
                }
                else if (gm.Equals("force", StringComparison.OrdinalIgnoreCase) ||
                         gm.Equals("forcegpu", StringComparison.OrdinalIgnoreCase))
                {
                    gpuMode = GpuMode.ForceGpu;
                }
                else
                {
                    Console.Error.WriteLine($"error: --gpu expects auto|off|force (got '{gm}')");
                    return 2;
                }
            }
            else if (arg == "--gpu-backend" && i + 1 < args.Length)
            {
                string gb = args[++i].Trim();
                if (gb.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    gpuBackendPref = GpuBackendPreference.Auto;
                }
                else if (gb.Equals("cuda", StringComparison.OrdinalIgnoreCase))
                {
                    gpuBackendPref = GpuBackendPreference.Cuda;
                }
                else if (gb.Equals("opencl", StringComparison.OrdinalIgnoreCase))
                {
                    gpuBackendPref = GpuBackendPreference.OpenCL;
                }
                else if (gb.Equals("cpu", StringComparison.OrdinalIgnoreCase))
                {
                    gpuBackendPref = GpuBackendPreference.Cpu;
                }
                else
                {
                    Console.Error.WriteLine($"error: --gpu-backend expects auto|cuda|opencl (got '{gb}')");
                    return 2;
                }
            }
            else if (arg == "--threads" && i + 1 < args.Length)
            {
                string ts = args[++i].Trim();
                if (ts.Equals("auto", StringComparison.OrdinalIgnoreCase) || ts == "0" || ts == "-1")
                {
                    requestedThreads = -1;
                }
                else if (int.TryParse(ts, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedThreads) &&
                         parsedThreads >= 1)
                {
                    requestedThreads = parsedThreads;
                }
                else
                {
                    Console.Error.WriteLine($"error: --threads expects auto or an integer >= 1 (got '{ts}')");
                    return 2;
                }
            }
            else if (arg == "--extended-codecs")
            {
                extendedCodecs = true;
            }
            else if (arg == "--ocr-only")
            {
                ocrOnly = true;
            }
            else if (arg.StartsWith('-'))
            {
                Console.Error.WriteLine($"error: unknown option {arg}");
                Usage();
                return 2;
            }
            else if (progDir is null)
            {
                progDir = arg;
            }
            else
            {
                Console.Error.WriteLine($"error: unexpected argument {arg}");
                Usage();
                return 2;
            }
        }

        if (progDir is null)
        {
            Usage();
            return 2;
        }

        // Apply CPU thread-pool degree of parallelism and GPU mode BEFORE warmup & input loading.
        LeptParallel.MaxDegreeOfParallelism = requestedThreads;
        LeptGpu.Configure(mode: gpuMode, backend: gpuBackendPref);

        // Pre-initialize GPU accelerator ahead of timed loops (in Auto/ForceGpu modes) so driver/context
        // startup is never charged to the first benchmark case and device info is known immediately.
        bool gpuReady = LeptGpu.Warmup();
        bool hwGpuDetected = gpuMode != GpuMode.CpuOnly && LeptGpu.IsHardwareGpuAvailable;
        string? activeGpuDevice = LeptGpu.ActiveDeviceName;
        GpuBackendKind activeGpuBackend = LeptGpu.ActiveBackendKind;

        int effectiveThreads = requestedThreads <= 0
            ? Environment.ProcessorCount
            : Math.Min(requestedThreads, Environment.ProcessorCount);
        string threadsDesc = requestedThreads <= 0
            ? $"{effectiveThreads} (auto)"
            : $"{effectiveThreads} (explicit)";

        string gpuDeviceLabel = gpuMode == GpuMode.CpuOnly
            ? "disabled"
            : gpuReady && activeGpuDevice is not null
                ? $"{activeGpuDevice} [{activeGpuBackend}]"
                : "none (no CUDA/OpenCL GPU found -> CPU fallback)";

        string date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        string cpu = GetCpuName();
        string os = $"{RuntimeInformation.OSDescription.Trim()} {RuntimeInformation.OSArchitecture}";
        string runtime = RuntimeInformation.FrameworkDescription;
        string leptVer = Utils.GetLeptonicaVersion();

        string startupBanner =
            $"# date={date}; cpu={cpu}; os={os}; compiler={runtime}; leptonica={leptVer} (Leptonica.Managed); " +
            $"threads={threadsDesc}; gpu_mode={gpuMode}; gpu_device={gpuDeviceLabel}";

        Console.WriteLine(startupBanner);
        Console.WriteLine($"bench_managed: fixed-operation benchmark for Leptonica.Managed ({leptVer})");
        Console.WriteLine($"  prog_dir      : {progDir}");
        Console.WriteLine($"  out           : {outPath}");
        Console.WriteLine($"  filter        : {filter ?? "(none)"}{(ocrOnly ? " [ocr-only]" : "")}");
        Console.WriteLine($"  min-time-ms   : {minTimeMs.ToString("F1", CultureInfo.InvariantCulture)}");
        Console.WriteLine($"  threads       : {threadsDesc}");
        Console.WriteLine($"  gpu-mode      : {gpuMode}");
        Console.WriteLine($"  gpu-device    : {gpuDeviceLabel}");
        if (extendedCodecs)
            Console.WriteLine($"  ext-codecs    : true (+12 managed codec cases)");
        Console.WriteLine();

        // Load inputs strictly on CPU so setup never affects GPU dispatch counters.
        BenchCtx ctx;
        using (LeptGpu.BeginScope(null, GpuMode.CpuOnly))
        {
            ctx = LoadInputs(progDir, extendedCodecs);
        }
        LeptGpu.ResetDispatchCount();
        PrintInputs(ctx, extendedCodecs);

        List<BenchCase> allCases = BuildCases(extendedCodecs);
        List<BenchCase> selected = new(allCases.Count);
        foreach (BenchCase bc in allCases)
        {
            if (ocrOnly && !bc.IsOcr)
                continue;
            if (filter is not null && !bc.Name.Contains(filter, StringComparison.Ordinal))
                continue;
            selected.Add(bc);
        }

        if (selected.Count == 0)
        {
            Console.Error.WriteLine($"error: no case matches filter '{filter}'");
            return 1;
        }

        Console.WriteLine(
            $"\nrunning {selected.Count} of {allCases.Count} cases (3 warm-up runs each, then >= 10 measured runs covering >= {minTimeMs.ToString("F0", CultureInfo.InvariantCulture)} ms):");

        var results = new CaseResult[selected.Count];
        int nFailed = 0;
        var gpuCaseNames = new List<string>();

        for (int i = 0; i < selected.Count; i++)
        {
            CaseResult r = RunCase(ctx, selected[i], minTimeMs);
            results[i] = r;
            if (r.Failed)
            {
                nFailed++;
                Console.Error.WriteLine($"  {selected[i].Name,-22} FAILED");
            }
            else
            {
                if (r.GpuUsed)
                    gpuCaseNames.Add(selected[i].Name);

                string execBadge = r.GpuUsed
                    ? (r.GpuDispatchesPerRun > 1 ? $"[GPU x{r.GpuDispatchesPerRun}]" : "[GPU]")
                    : "[CPU]";

                Console.Error.WriteLine(
                    string.Create(CultureInfo.InvariantCulture,
                        $"  {selected[i].Name,-22} {r.Runs,5} runs, median {r.MedianMs,9:F3} ms  {execBadge}"));
            }
        }

        PrintResults(ctx, selected, results);

        bool anyGpuUsed = gpuCaseNames.Count > 0;
        string csvBanner =
            $"{startupBanner}; gpu_hw={(hwGpuDetected ? "yes" : "no")}; " +
            $"gpu_used={(anyGpuUsed ? "yes" : "no")}; gpu_cases={gpuCaseNames.Count}/{selected.Count}";

        WriteCsv(outPath, csvBanner, ctx, selected, results);

        Console.WriteLine();
        PrintGpuSummary(gpuMode, hwGpuDetected, activeGpuDevice, activeGpuBackend, selected.Count, gpuCaseNames);
        Console.WriteLine($"wrote {outPath} ({selected.Count} cases, {nFailed} failed, {gpuCaseNames.Count} used GPU)");
        return 0;
    }

    private static void PrintGpuSummary(
        GpuMode gpuMode,
        bool hwGpuDetected,
        string? activeGpuDevice,
        GpuBackendKind backendKind,
        int totalCases,
        List<string> gpuCaseNames)
    {
        if (gpuMode == GpuMode.CpuOnly)
        {
            Console.WriteLine(
                $"gpu summary: DISABLED (--gpu off) — GPU was NOT used (0 of {totalCases} cases; all {totalCases} ran on CPU)");
            return;
        }

        if (gpuCaseNames.Count > 0)
        {
            string dev = activeGpuDevice is not null ? $"{activeGpuDevice} [{backendKind}]" : backendKind.ToString();
            Console.WriteLine(
                $"gpu summary: USED — {dev} accelerated {gpuCaseNames.Count} of {totalCases} cases " +
                $"({totalCases - gpuCaseNames.Count} ran on CPU)");
            Console.WriteLine($"  gpu cases : {string.Join(", ", gpuCaseNames)}");
            return;
        }

        if (!hwGpuDetected)
        {
            Console.WriteLine(
                $"gpu summary: NOT USED — no hardware CUDA/OpenCL GPU detected in {gpuMode} mode " +
                $"(0 of {totalCases} cases; all {totalCases} ran on CPU)");
        }
        else
        {
            Console.WriteLine(
                $"gpu summary: NOT USED — {activeGpuDevice} [{backendKind}] was detected, " +
                $"but none of the {totalCases} selected cases exceeded the GPU offload threshold");
        }
    }

    private static void Usage()
    {
        Console.Error.WriteLine(
            "usage: bench_managed <prog_dir> [--out results_managed.csv]\n" +
            "                     [--gpu auto|off|force] [--no-gpu]\n" +
            "                     [--gpu-backend auto|cuda|opencl]\n" +
            "                     [--threads auto|N] [--filter <substring>]\n" +
            "                     [--ocr-only] [--extended-codecs] [--min-time-ms N]\n" +
            "\n" +
            "  <prog_dir>            directory with the Leptonica test images\n" +
            "                        (e.g. testdata/prog)\n" +
            "  --out PATH            CSV output path (default: results_managed.csv)\n" +
            "  --gpu auto|off|force  GPU execution mode (default: auto):\n" +
            "                          auto  = use hardware GPU (CUDA/OpenCL) when present\n" +
            "                                  and faster than CPU; otherwise stay on CPU\n" +
            "                          off   = disable GPU completely (pure CPU comparison)\n" +
            "                          force = force GPU offload regardless of size threshold\n" +
            "  --no-gpu              shorthand for --gpu off\n" +
            "  --gpu-backend KIND    preferred GPU driver: auto (default), cuda, or opencl\n" +
            "  --threads auto|N      CPU worker threads: auto = all cores (default),\n" +
            "                        1 = single-threaded CPU (matches single-core C)\n" +
            "  --filter SUBSTR       run only cases whose name contains SUBSTR\n" +
            "  --ocr-only            run only the OCR pipeline subset of cases\n" +
            "  --extended-codecs     append 12 extra managed codec cases (80 total)\n" +
            "  --min-time-ms N       minimum measured time per case in ms (default: 1000)\n");
    }

    private static string GetCpuName()
    {
        // 1. Hardware CPUID brand string on x86/x64 (works identically on Windows, Linux, and macOS).
        if (X86Base.IsSupported)
        {
            (int maxExt, _, _, _) = X86Base.CpuId(unchecked((int)0x80000000), 0);
            if ((uint)maxExt >= 0x80000004u)
            {
                Span<int> regs = stackalloc int[12];
                (regs[0], regs[1], regs[2], regs[3]) = X86Base.CpuId(unchecked((int)0x80000002), 0);
                (regs[4], regs[5], regs[6], regs[7]) = X86Base.CpuId(unchecked((int)0x80000003), 0);
                (regs[8], regs[9], regs[10], regs[11]) = X86Base.CpuId(unchecked((int)0x80000004), 0);

                ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(regs);
                int len = bytes.IndexOf((byte)0);
                if (len < 0)
                    len = bytes.Length;
                string brand = Encoding.ASCII.GetString(bytes[..len]).Trim();
                if (brand.Length > 0)
                    return brand;
            }
        }

        // 2. Linux /proc/cpuinfo fallback (e.g. ARM64 Linux).
        if (File.Exists("/proc/cpuinfo"))
        {
            foreach (string line in File.ReadLines("/proc/cpuinfo"))
            {
                if (line.StartsWith("model name", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Model", StringComparison.Ordinal))
                {
                    int idx = line.IndexOf(':');
                    if (idx >= 0 && idx + 2 <= line.Length)
                    {
                        string val = line[(idx + 2)..].Trim();
                        if (val.Length > 0)
                            return val;
                    }
                }
            }
        }

        return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Consume(BenchCtx c, object? obj)
    {
        if (obj is null)
            c.Fail = true;
        GC.KeepAlive(obj);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ConsumeBytes(BenchCtx c, byte[]? data)
    {
        if (data is null || data.Length == 0)
            c.Fail = true;
        GC.KeepAlive(data);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckOk(BenchCtx c, bool ok)
    {
        if (!ok)
            c.Fail = true;
    }

    private static Pix? LoadPix(string dir, string fname, bool required)
    {
        string path = Path.Combine(dir, fname);
        try
        {
            return Pix.Read(path);
        }
        catch (Exception ex)
        {
            if (required)
            {
                Console.Error.WriteLine($"error: cannot read required input {path}: {ex.Message}");
                Environment.Exit(2);
            }
            Console.Error.WriteLine($"warning: cannot read optional input {path}");
            return null;
        }
    }

    private static BenchCtx LoadInputs(string dir, bool extendedCodecs)
    {
        var g = new BenchCtx();

        g.Feyn = LoadPix(dir, "feyn.tif", required: true)!;
        g.FeynT3 = Pix.Translate(null, g.Feyn, 3, 3, L.L_BRING_IN_WHITE);
        g.FeynRopDst = Pix.Copy(null, g.Feyn);
        g.FeynXorDst = Pix.Copy(null, g.Feyn);

        Pix pix1 = LoadPix(dir, "test8.jpg", required: true)!;
        g.Gray8 = pix1.ConvertTo8(false);

        g.Marge = LoadPix(dir, "marge.jpg", required: true)!;
        if (g.Marge.Depth != 32)
            Console.Error.WriteLine($"warning: marge.jpg is {g.Marge.Depth} bpp, expected 32");
        Pix lum = g.Marge.ConvertRGBToLuminance();
        g.GrayPage = lum.ScaleGrayLI(2.0f, 2.0f);

        Pix? luc = LoadPix(dir, "lucasta.150.jpg", required: false);
        if (luc is not null)
            g.GrayLucasta = luc.ConvertTo8(false);

        g.Test24 = LoadPix(dir, "test24.jpg", required: false);
        g.Cmapped = LoadPix(dir, "weasel-64g.png", required: true)!;

        int w = g.Feyn.Width;
        int h = g.Feyn.Height;
        g.SeedBin = Pix.Create(w, h, 1);
        g.SeedBin.SetPixel(200, 200, 1);
        g.SeedBin.SetPixel(1300, 1700, 1);
        g.SeedBin.SetPixel(2500, 3200, 1);

        g.SeedGray = Pix.Create(w, h, 8);
        g.SeedGray.SetPixel(200, 200, 64);
        g.SeedGray.SetPixel(1300, 1700, 64);
        g.SeedGray.SetPixel(2500, 3200, 64);
        g.MaskGray = Pix.Convert1To8(null, g.Feyn, 128, 255);

        g.SelHmt = Sel.CreateFromString("xxxxXxxoo", 3, 3, "corner3x3");
        g.KelGauss = L_Kernel.MakeGaussianKernel(3, 3, 1.0f, 1.0f);

        int gw = g.Gray8.Width;
        int gh = g.Gray8.Height;
        g.Ptas = Pta.Create(3);
        g.Ptas.AddPt(0.0f, 0.0f);
        g.Ptas.AddPt(gw - 1, 0.0f);
        g.Ptas.AddPt(0.0f, gh - 1);
        g.Ptad = Pta.Create(3);
        g.Ptad.AddPt((float)(0.02 * gw), 0.0f);
        g.Ptad.AddPt(gw - 1, (float)(0.02 * gw));
        g.Ptad.AddPt(0.0f, gh - 1);

        g.Png32 = Pix.WriteMem(g.Marge, L.IFF_PNG);
        g.TiffG4 = Pix.WriteMem(g.Feyn, L.IFF_TIFF_G4);
        g.JpgBytes = File.ReadAllBytes(Path.Combine(dir, "marge.jpg"));

        if (extendedCodecs)
        {
            g.TiffZip32 = Pix.WriteMem(g.Marge, L.IFF_TIFF_ZIP);
            g.TiffLzw8 = Pix.WriteMem(g.Gray8, L.IFF_TIFF_LZW);
            g.Gif8 = Pix.WriteMem(g.Cmapped, L.IFF_GIF);
            g.WebpLossy32 = Pix.WriteMemWebP(g.Marge, 80, 0);
            g.WebpLossless32 = Pix.WriteMemWebP(g.Marge, 100, 1);
            g.Jp2k32 = Pix.WriteMemJp2k(g.Marge, 34, 5, 0, 0);
        }

        return g;
    }

    private static void PrintInputs(BenchCtx g, bool extendedCodecs)
    {
        Console.WriteLine("inputs (loaded once, never inside the timed loops):");
        Console.WriteLine($"  {"label",-13} {"source",-20} {"w",6} {"h",6} {"d",3}");
        static void PrintPix(string lbl, string src, Pix? p)
        {
            if (p is not null)
                Console.WriteLine($"  {lbl,-13} {src,-20} {p.Width,6} {p.Height,6} {p.Depth,3}");
            else
                Console.WriteLine($"  {lbl,-13} {src,-20} {"(not",6} {"loaded)",6} {"",3}");
        }

        PrintPix("feyn", "feyn.tif", g.Feyn);
        PrintPix("feyn_t3", "feyn+(3,3)", g.FeynT3);
        PrintPix("gray8", "test8.jpg", g.Gray8);
        PrintPix("gray_page", "marge.jpg lum x2", g.GrayPage);
        PrintPix("gray_lucasta", "lucasta.150.jpg", g.GrayLucasta);
        PrintPix("marge", "marge.jpg", g.Marge);
        PrintPix("test24", "test24.jpg", g.Test24);
        PrintPix("cmapped", "weasel-64g.png", g.Cmapped);
        PrintPix("seed_bin", "(derived)", g.SeedBin);
        PrintPix("seed_gray", "(derived)", g.SeedGray);
        PrintPix("mask_gray", "(derived)", g.MaskGray);
        Console.WriteLine($"  {"png32",-13} {"(setup buffer)",-20} {g.Png32.Length,19} bytes");
        Console.WriteLine($"  {"tiffg4",-13} {"(setup buffer)",-20} {g.TiffG4.Length,19} bytes");
        Console.WriteLine($"  {"jpg",-13} {"marge.jpg bytes",-20} {g.JpgBytes.Length,19} bytes");

        if (extendedCodecs)
        {
            Console.WriteLine($"  {"tiffzip32",-13} {"(setup buffer)",-20} {g.TiffZip32.Length,19} bytes");
            Console.WriteLine($"  {"tifflzw8",-13} {"(setup buffer)",-20} {g.TiffLzw8.Length,19} bytes");
            Console.WriteLine($"  {"gif8",-13} {"(setup buffer)",-20} {g.Gif8.Length,19} bytes");
            Console.WriteLine($"  {"webp_lossy",-13} {"(setup buffer)",-20} {g.WebpLossy32.Length,19} bytes");
            Console.WriteLine($"  {"webp_lossless",-13} {"(setup buffer)",-20} {g.WebpLossless32.Length,19} bytes");
            Console.WriteLine($"  {"jp2k32",-13} {"(setup buffer)",-20} {g.Jp2k32.Length,19} bytes");
        }
    }

    private static List<BenchCase> BuildCases(bool extendedCodecs)
    {
        var cases = new List<BenchCase>(80)
        {
            // Rasterop / logical (1 bpp feyn)
            new("rop_src", "pixRasterop", "feyn",
                "dx=100, dy=100, w=2000, h=2000, op=PIX_SRC, sx=0, sy=0",
                "rasterop", true,
                c => c.FeynRopDst.Rasterop(100, 100, 2000, 2000, L.PIX_SRC, c.Feyn, 0, 0),
                c => c.Feyn),
            new("rop_xor_shift", "pixRasterop", "feyn",
                "dx=1, dy=1, w=W-2, h=H-2, op=PIX_SRC^PIX_DST, sx=0, sy=0",
                "rasterop", false,
                c => c.FeynXorDst.Rasterop(1, 1, c.Feyn.Width - 2, c.Feyn.Height - 2, L.PIX_SRC ^ L.PIX_DST, c.Feyn, 0, 0),
                c => c.Feyn),
            new("invert_1", "pixInvert", "feyn", "",
                "rasterop", true,
                c => Consume(c, Pix.Invert(null, c.Feyn)),
                c => c.Feyn),
            new("and_1", "pixAnd", "feyn+feyn_t3",
                "pixs2=feyn translated by (3,3), incolor=L_BRING_IN_WHITE",
                "rasterop", true,
                c => Consume(c, Pix.And(null, c.Feyn, c.FeynT3)),
                c => c.Feyn),

            // Binary morphology (1 bpp feyn — OCR layout & textline/glyph analysis)
            new("dilate_brick_5", "pixDilateBrick", "feyn", "hsize=5, vsize=5",
                "morph_bin", true,
                c => Consume(c, Pix.DilateBrick(null, c.Feyn, 5, 5)),
                c => c.Feyn),
            new("erode_brick_15x1", "pixErodeBrick", "feyn", "hsize=15, vsize=1",
                "morph_bin", true,
                c => Consume(c, Pix.ErodeBrick(null, c.Feyn, 15, 1)),
                c => c.Feyn),
            new("open_brick_5", "pixOpenBrick", "feyn", "hsize=5, vsize=5",
                "morph_bin", true,
                c => Consume(c, Pix.OpenBrick(null, c.Feyn, 5, 5)),
                c => c.Feyn),
            new("close_brick_21", "pixCloseBrick", "feyn", "hsize=21, vsize=21",
                "morph_bin", true,
                c => Consume(c, Pix.CloseBrick(null, c.Feyn, 21, 21)),
                c => c.Feyn),
            new("dilate_comp_21", "pixDilateCompBrick", "feyn", "hsize=21, vsize=21",
                "morph_bin", true,
                c => Consume(c, Pix.DilateCompBrick(null, c.Feyn, 21, 21)),
                c => c.Feyn),
            new("dilate_dwa_21", "pixDilateBrickDwa", "feyn", "hsize=21, vsize=21",
                "morph_bin", true,
                c => Consume(c, Pix.DilateBrickDwa(null, c.Feyn, 21, 21)),
                c => c.Feyn),
            new("close_comp_dwa_51", "pixCloseCompBrickDwa", "feyn", "hsize=51, vsize=51",
                "morph_bin", true,
                c => Consume(c, Pix.CloseCompBrickDwa(null, c.Feyn, 51, 51)),
                c => c.Feyn),
            new("morphseq", "pixMorphSequence", "feyn",
                "sequence=\"o5.5 + c21.1 + d3.3\", display=0",
                "morph_bin", true,
                c => Consume(c, c.Feyn.MorphSequence("o5.5 + c21.1 + d3.3", 0)),
                c => c.Feyn),
            new("hmt_sel", "pixHMT", "feyn",
                "sel=selCreateFromString(\"xxxxXxxoo\", 3, 3, \"corner3x3\") (rows xxx/xXx/xoo, X = origin at center, no separators)",
                "morph_bin", true,
                c => Consume(c, Pix.HMT(null, c.Feyn, c.SelHmt)),
                c => c.Feyn),

            // Gray morphology (8 bpp page — OCR background / illumination morphology)
            new("dilate_gray_5", "pixDilateGray", "gray_page", "hsize=5, vsize=5",
                "morph_gray", true,
                c => Consume(c, c.GrayPage.DilateGray(5, 5)),
                c => c.GrayPage),
            new("erode_gray_15", "pixErodeGray", "gray_page", "hsize=15, vsize=15",
                "morph_gray", true,
                c => Consume(c, c.GrayPage.ErodeGray(15, 15)),
                c => c.GrayPage),
            new("tophat_white_15", "pixTophat", "gray_page",
                "hsize=15, vsize=15, type=L_TOPHAT_WHITE",
                "morph_gray", true,
                c => Consume(c, c.GrayPage.Tophat(15, 15, L.L_TOPHAT_WHITE)),
                c => c.GrayPage),

            // Scaling
            new("scale_1_0.5", "pixScale", "feyn", "scalex=0.5, scaley=0.5",
                "scale", true,
                c => Consume(c, c.Feyn.Scale(0.5f, 0.5f)),
                c => c.Feyn),
            new("scale_8_0.37", "pixScale", "gray8", "scalex=0.37, scaley=0.37",
                "scale", false,
                c => Consume(c, c.Gray8.Scale(0.37f, 0.37f)),
                c => c.Gray8),
            new("scale_8_2.3", "pixScale", "gray8", "scalex=2.3, scaley=2.3",
                "scale", true,
                c => Consume(c, c.Gray8.Scale(2.3f, 2.3f)),
                c => c.Gray8),
            new("scale_32_0.5", "pixScale", "marge", "scalex=0.5, scaley=0.5",
                "scale", false,
                c => Consume(c, c.Marge.Scale(0.5f, 0.5f)),
                c => c.Marge),
            new("scale_32_1.6", "pixScale", "marge", "scalex=1.6, scaley=1.6",
                "scale", false,
                c => Consume(c, c.Marge.Scale(1.6f, 1.6f)),
                c => c.Marge),
            new("scale_li_8_2x", "pixScaleGrayLI", "gray8", "scalex=2.0, scaley=2.0",
                "scale", true,
                c => Consume(c, c.Gray8.ScaleGrayLI(2.0f, 2.0f)),
                c => c.Gray8),
            new("scale_area_8_0.3", "pixScaleAreaMap", "gray8", "scalex=0.3, scaley=0.3",
                "scale", true,
                c => Consume(c, c.Gray8.ScaleAreaMap(0.3f, 0.3f)),
                c => c.Gray8),
            new("scale_to_gray_4", "pixScaleToGray4", "feyn", "",
                "scale", true,
                c => Consume(c, c.Feyn.ScaleToGray4()),
                c => c.Feyn),
            new("scale_sampling", "pixScaleBySampling", "marge", "scalex=0.71, scaley=0.71",
                "scale", false,
                c => Consume(c, c.Marge.ScaleBySampling(0.71f, 0.71f)),
                c => c.Marge),

            // Depth conversion & binarization
            new("rgb_to_gray", "pixConvertRGBToGray", "marge", "rwt=0.3, gwt=0.5, bwt=0.2",
                "depth_quant", true,
                c => Consume(c, c.Marge.ConvertRGBToGray(0.3f, 0.5f, 0.2f)),
                c => c.Marge),
            new("rgb_to_lum", "pixConvertRGBToLuminance", "marge", "",
                "depth_quant", true,
                c => Consume(c, c.Marge.ConvertRGBToLuminance()),
                c => c.Marge),
            new("conv_1_to_8", "pixConvert1To8", "feyn", "val0=255, val1=0",
                "depth_quant", true,
                c => Consume(c, Pix.Convert1To8(null, c.Feyn, 255, 0)),
                c => c.Feyn),
            new("conv_8_to_32", "pixConvert8To32", "gray8", "",
                "depth_quant", false,
                c => Consume(c, c.Gray8.Convert8To32()),
                c => c.Gray8),
            new("remove_cmap", "pixRemoveColormap", "cmapped", "type=REMOVE_CMAP_BASED_ON_SRC",
                "depth_quant", true,
                c => Consume(c, c.Cmapped.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC)),
                c => c.Cmapped),
            new("thresh_8_to_1", "pixThresholdToBinary", "gray8", "threshold=128",
                "depth_quant", true,
                c => Consume(c, c.Gray8.ThresholdToBinary(128)),
                c => c.Gray8),
            new("dither_8_to_1", "pixDitherToBinary", "gray8", "",
                "depth_quant", false,
                c => Consume(c, c.Gray8.DitherToBinary()),
                c => c.Gray8),
            new("otsu_adaptive", "pixOtsuAdaptiveThreshold", "gray8",
                "sx=200, sy=200, smoothx=0, smoothy=0, scorefract=0.1, ppixth=&pixth, ppixd=&pixd",
                "depth_quant", true,
                c =>
                {
                    c.Gray8.OtsuAdaptiveThreshold(200, 200, 0, 0, 0.1f, out Pix th, out Pix d);
                    Consume(c, th);
                    Consume(c, d);
                },
                c => c.Gray8),
            new("octree_quant", "pixOctreeColorQuant", "marge", "colors=128, ditherflag=0",
                "depth_quant", false,
                c => Consume(c, c.Marge.OctreeColorQuant(128, 0)),
                c => c.Marge),
            new("median_cut", "pixMedianCutQuant", "marge", "ditherflag=0",
                "depth_quant", false,
                c => Consume(c, c.Marge.MedianCutQuant(0)),
                c => c.Marge),

            // Filtering / convolution / background normalization
            new("blockconv_8_5", "pixBlockconv", "gray8", "wc=5, hc=5",
                "filter", true,
                c => Consume(c, c.Gray8.Blockconv(5, 5)),
                c => c.Gray8),
            new("blockconv_32_5", "pixBlockconv", "marge", "wc=5, hc=5",
                "filter", false,
                c => Consume(c, c.Marge.Blockconv(5, 5)),
                c => c.Marge),
            new("convolve_kernel", "pixConvolve", "gray8",
                "kel=makeGaussianKernel(3, 3, 1.0, 1.0), outdepth=8, normflag=1",
                "filter", true,
                c => Consume(c, c.Gray8.Convolve(c.KelGauss, 8, 1)),
                c => c.Gray8),
            new("unsharp", "pixUnsharpMasking", "marge", "halfwidth=3, fract=0.5",
                "filter", true,
                c => Consume(c, c.Marge.UnsharpMasking(3, 0.5f)),
                c => c.Marge),
            new("bilateral_fast", "pixBilateral", "gray8",
                "spatial_stdev=5.0, range_stdev=10.0, ncomps=10, reduction=1",
                "filter", false,
                c => Consume(c, c.Gray8.Bilateral(5.0f, 10.0f, 10, 1)),
                c => c.Gray8),
            new("median_filter_3", "pixMedianFilter", "gray8", "wf=3, hf=3",
                "filter", true,
                c => Consume(c, c.Gray8.MedianFilter(3, 3)),
                c => c.Gray8),
            new("edge_sobel", "pixSobelEdgeFilter", "gray8", "orientflag=L_ALL_EDGES",
                "filter", true,
                c => Consume(c, c.Gray8.SobelEdgeFilter(L.L_ALL_EDGES)),
                c => c.Gray8),
            new("bg_norm", "pixBackgroundNormSimple", "marge", "pixim=NULL, pixg=NULL",
                "filter", true,
                c => Consume(c, c.Marge.BackgroundNormSimple(null, null)),
                c => c.Marge),

            // Seedfill / connected components / distance
            new("seedfill_binary", "pixSeedfillBinary", "seed_bin+feyn",
                "pixs=seed_bin (3 pixels), pixm=feyn, connectivity=8",
                "conncomp", true,
                c => Consume(c, Pix.SeedfillBinary(null, c.SeedBin, c.Feyn, 8)),
                c => c.SeedBin),
            new("conncomp_8", "pixConnComp", "feyn", "ppixa=NULL, connectivity=8",
                "conncomp", true,
                c => Consume(c, c.Feyn.ConnComp(8)),
                c => c.Feyn),
            new("conncomp_pixa", "pixConnComp", "feyn", "ppixa=&pixa, connectivity=8",
                "conncomp", true,
                c =>
                {
                    Boxa b = c.Feyn.ConnComp(out Pixa pa, 8);
                    Consume(c, b);
                    Consume(c, pa);
                },
                c => c.Feyn),
            new("count_conncomp", "pixCountConnComp", "feyn", "connectivity=8, pcount=&n",
                "conncomp", true,
                c => CheckOk(c, c.Feyn.CountConnComp(8) >= 0),
                c => c.Feyn),
            new("distance_fn", "pixDistanceFunction", "feyn",
                "connectivity=8, outdepth=8, boundcond=L_BOUNDARY_BG",
                "conncomp", true,
                c => Consume(c, c.Feyn.DistanceFunction(8, 8, L.L_BOUNDARY_BG)),
                c => c.Feyn),
            new("seedfill_gray", "pixSeedfillGray", "seed_gray+mask_gray",
                "connectivity=4; per run: pixCopy(NULL, seed_gray) then in-place fill (copy is timed)",
                "conncomp", false,
                c =>
                {
                    Pix d = Pix.Copy(null, c.SeedGray);
                    d.SeedfillGray(c.MaskGray, 4);
                    Consume(c, d);
                },
                c => c.SeedGray),

            // Geometry & page skew/deskew
            new("rotate_shear_1", "pixRotate", "feyn",
                "angle=0.05, type=L_ROTATE_SHEAR, incolor=L_BRING_IN_WHITE, width=0, height=0",
                "geometry", true,
                c => Consume(c, c.Feyn.Rotate(0.05f, L.L_ROTATE_SHEAR, L.L_BRING_IN_WHITE, 0, 0)),
                c => c.Feyn),
            new("rotate_area_8", "pixRotate", "gray8",
                "angle=0.05, type=L_ROTATE_AREA_MAP, incolor=L_BRING_IN_WHITE, width=0, height=0",
                "geometry", true,
                c => Consume(c, c.Gray8.Rotate(0.05f, L.L_ROTATE_AREA_MAP, L.L_BRING_IN_WHITE, 0, 0)),
                c => c.Gray8),
            new("rotate_area_32", "pixRotate", "marge",
                "angle=0.05, type=L_ROTATE_AREA_MAP, incolor=L_BRING_IN_WHITE, width=0, height=0",
                "geometry", false,
                c => Consume(c, c.Marge.Rotate(0.05f, L.L_ROTATE_AREA_MAP, L.L_BRING_IN_WHITE, 0, 0)),
                c => c.Marge),
            new("rotate_90_1", "pixRotate90", "feyn", "direction=1 (clockwise)",
                "geometry", true,
                c => Consume(c, c.Feyn.Rotate90(1)),
                c => c.Feyn),
            new("rotate_90_32", "pixRotate90", "marge", "direction=1 (clockwise)",
                "geometry", false,
                c => Consume(c, c.Marge.Rotate90(1)),
                c => c.Marge),
            new("flip_lr_8", "pixFlipLR", "gray8", "",
                "geometry", false,
                c => Consume(c, Pix.FlipLR(null, c.Gray8)),
                c => c.Gray8),
            new("find_skew", "pixFindSkew", "feyn", "pangle, pconf",
                "geometry", true,
                c => CheckOk(c, c.Feyn.FindSkew(out _, out _)),
                c => c.Feyn),
            new("deskew", "pixDeskew", "feyn", "redsearch=0",
                "geometry", true,
                c => Consume(c, c.Feyn.Deskew(0)),
                c => c.Feyn),
            new("affine_sampled", "pixAffineSampledPta", "gray8",
                "incolor=L_BRING_IN_WHITE, ptas=[(0,0),(W-1,0),(0,H-1)], ptad=[(0.02*W,0),(W-1,0.02*W),(0,H-1)]",
                "geometry", true,
                c => Consume(c, c.Gray8.AffineSampledPta(c.Ptad, c.Ptas, L.L_BRING_IN_WHITE)),
                c => c.Gray8),

            // Statistics & rank filtering
            new("histogram_8", "pixGetGrayHistogram", "gray8", "factor=1",
                "stats", true,
                c => Consume(c, c.Gray8.GetGrayHistogram(1)),
                c => c.Gray8),
            new("count_pixels_1", "pixCountPixels", "feyn", "tab8=NULL",
                "stats", true,
                c => CheckOk(c, c.Feyn.CountPixels(null) >= 0),
                c => c.Feyn),
            new("rank_filter_8", "pixRankFilterGray", "gray8", "wf=5, hf=5, rank=0.5",
                "stats", true,
                c => Consume(c, c.Gray8.RankFilterGray(5, 5, 0.5f)),
                c => c.Gray8),

            // I/O (in memory, no disk)
            new("png_encode_8", "pixWriteMem", "gray8", "format=IFF_PNG",
                "io", false,
                c => ConsumeBytes(c, Pix.WriteMem(c.Gray8, L.IFF_PNG)),
                c => c.Gray8),
            new("png_encode_32", "pixWriteMem", "marge", "format=IFF_PNG",
                "io", false,
                c => ConsumeBytes(c, Pix.WriteMem(c.Marge, L.IFF_PNG)),
                c => c.Marge),
            new("png_decode_32", "pixReadMem", "png32(marge)", "buffer=PNG of marge encoded at setup",
                "io", true,
                c => Consume(c, Pix.ReadMem(c.Png32)),
                c => c.Marge),
            new("jpeg_decode_32", "pixReadMem", "marge.jpg bytes", "buffer=raw file bytes of marge.jpg",
                "io", true,
                c => Consume(c, Pix.ReadMem(c.JpgBytes)),
                c => c.Marge),
            new("jpeg_encode_32", "pixWriteMem", "marge", "format=IFF_JFIF_JPEG",
                "io", false,
                c => ConsumeBytes(c, Pix.WriteMem(c.Marge, L.IFF_JFIF_JPEG)),
                c => c.Marge),
            new("tiff_g4_encode", "pixWriteMem", "feyn", "format=IFF_TIFF_G4",
                "io", true,
                c => ConsumeBytes(c, Pix.WriteMem(c.Feyn, L.IFF_TIFF_G4)),
                c => c.Feyn),
            new("tiff_g4_decode", "pixReadMem", "tiffg4(feyn)", "buffer=G4 TIFF of feyn encoded at setup",
                "io", true,
                c => Consume(c, Pix.ReadMem(c.TiffG4)),
                c => c.Feyn)
        };

        if (extendedCodecs)
        {
            cases.AddRange(new BenchCase[]
            {
                new("tiff_zip_encode_32", "pixWriteMemTiff", "marge", "format=IFF_TIFF_ZIP",
                    "io_ext", false,
                    c => ConsumeBytes(c, Pix.WriteMem(c.Marge, L.IFF_TIFF_ZIP)),
                    c => c.Marge),
                new("tiff_zip_decode_32", "pixReadMemTiff", "tiffzip32(marge)", "buffer=ZIP TIFF of marge",
                    "io_ext", true,
                    c => Consume(c, Pix.ReadMemTiff(c.TiffZip32, 0)),
                    c => c.Marge),
                new("tiff_lzw_encode_8", "pixWriteMemTiff", "gray8", "format=IFF_TIFF_LZW",
                    "io_ext", false,
                    c => ConsumeBytes(c, Pix.WriteMem(c.Gray8, L.IFF_TIFF_LZW)),
                    c => c.Gray8),
                new("tiff_lzw_decode_8", "pixReadMemTiff", "tifflzw8(gray8)", "buffer=LZW TIFF of gray8",
                    "io_ext", true,
                    c => Consume(c, Pix.ReadMemTiff(c.TiffLzw8, 0)),
                    c => c.Gray8),
                new("gif_encode_8", "pixWriteMemGif", "cmapped", "format=IFF_GIF",
                    "io_ext", false,
                    c => ConsumeBytes(c, Pix.WriteMem(c.Cmapped, L.IFF_GIF)),
                    c => c.Cmapped),
                new("gif_decode_8", "pixReadMemGif", "gif8(cmapped)", "buffer=GIF of weasel-64g",
                    "io_ext", false,
                    c => Consume(c, Pix.ReadMemGif(c.Gif8)),
                    c => c.Cmapped),
                new("webp_lossy_encode_32", "pixWriteMemWebP", "marge", "quality=80, lossless=0",
                    "io_ext", false,
                    c => ConsumeBytes(c, Pix.WriteMemWebP(c.Marge, 80, 0)),
                    c => c.Marge),
                new("webp_lossy_decode_32", "pixReadMemWebP", "webp_lossy(marge)", "buffer=lossy WebP q=80 of marge",
                    "io_ext", false,
                    c => Consume(c, Pix.ReadMemWebP(c.WebpLossy32)),
                    c => c.Marge),
                new("webp_lossless_enc_32", "pixWriteMemWebP", "marge", "quality=100, lossless=1",
                    "io_ext", false,
                    c => ConsumeBytes(c, Pix.WriteMemWebP(c.Marge, 100, 1)),
                    c => c.Marge),
                new("webp_lossless_dec_32", "pixReadMemWebP", "webp_lossless(marge)", "buffer=lossless WebP of marge",
                    "io_ext", false,
                    c => Consume(c, Pix.ReadMemWebP(c.WebpLossless32)),
                    c => c.Marge),
                new("jp2k_encode_32", "pixWriteMemJp2k", "marge", "quality=34, nlevels=5",
                    "io_ext", false,
                    c => ConsumeBytes(c, Pix.WriteMemJp2k(c.Marge, 34, 5, 0, 0)),
                    c => c.Marge),
                new("jp2k_decode_32", "pixReadMemJp2k", "jp2k32(marge)", "buffer=JP2K q=34 of marge",
                    "io_ext", true,
                    c => Consume(c, Pix.ReadMemJp2k(c.Jp2k32, 1, null, 0, 0)),
                    c => c.Marge),
            });
        }

        return cases;
    }

    private static CaseResult RunCase(BenchCtx ctx, BenchCase bc, double minTimeMs)
    {
        // 3 unmeasured warm-up runs (compiles RyuJIT methods and any ILGPU kernels before timing).
        try
        {
            for (int i = 0; i < 3; i++)
            {
                ctx.Fail = false;
                bc.Run(ctx);
                if (ctx.Fail)
                    return new CaseResult(true, 0, 0, 0, 0, 0, false, 0);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [{bc.Name}] exception in warmup: {ex.Message}");
            return new CaseResult(true, 0, 0, 0, 0, 0, false, 0);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var samples = new List<double>(64);
        long gpuBefore = LeptGpu.DispatchCount;
        long caseStart = Stopwatch.GetTimestamp();
        try
        {
            do
            {
                ctx.Fail = false;
                long t0 = Stopwatch.GetTimestamp();
                bc.Run(ctx);
                double dt = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                if (ctx.Fail)
                    return new CaseResult(true, 0, 0, 0, 0, 0, false, 0);
                samples.Add(dt);
            } while (samples.Count < 10 || Stopwatch.GetElapsedTime(caseStart).TotalMilliseconds < minTimeMs);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [{bc.Name}] exception: {ex.Message}");
            return new CaseResult(true, 0, 0, 0, 0, 0, false, 0);
        }

        long gpuDelta = LeptGpu.DispatchCount - gpuBefore;
        int n = samples.Count;
        bool gpuUsed = gpuDelta > 0;
        int gpuDispatchesPerRun = gpuUsed ? Math.Max(1, (int)Math.Round((double)gpuDelta / n)) : 0;

        double sum = 0.0;
        for (int i = 0; i < n; i++)
            sum += samples[i];
        double meanMs = sum / n;

        double[] sorted = samples.ToArray();
        Array.Sort(sorted);
        double minMs = sorted[0];
        double medianMs = MedianSorted(sorted);

        double[] devs = new double[n];
        for (int i = 0; i < n; i++)
            devs[i] = Math.Abs(sorted[i] - medianMs);
        Array.Sort(devs);
        double madMs = MedianSorted(devs);

        return new CaseResult(false, n, minMs, medianMs, meanMs, madMs, gpuUsed, gpuDispatchesPerRun);
    }

    private static double MedianSorted(double[] v)
    {
        int n = v.Length;
        return (n & 1) != 0 ? v[n / 2] : 0.5 * (v[n / 2 - 1] + v[n / 2]);
    }

    private static string CsvField(string s)
    {
        if (s.IndexOfAny([',', '"', '\n']) >= 0)
            return $"\"{s.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        return s;
    }

    private static void WriteCsv(
        string path,
        string banner,
        BenchCtx ctx,
        List<BenchCase> sel,
        CaseResult[] res)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var sb = new StringBuilder(12288);
        sb.AppendLine(banner);
        sb.AppendLine(
            "case,function,input,width,height,depth,params,runs,min_ms,median_ms,mean_ms,mad_ms,gpu_used,gpu_dispatches,category,ocr");

        for (int i = 0; i < sel.Count; i++)
        {
            BenchCase bc = sel[i];
            CaseResult r = res[i];
            Pix? pix = bc.GetPrimaryPix(ctx);
            int w = pix?.Width ?? 0;
            int h = pix?.Height ?? 0;
            int d = pix?.Depth ?? 0;
            int ocrFlag = bc.IsOcr ? 1 : 0;

            sb.Append(CsvField(bc.Name)).Append(',')
              .Append(CsvField(bc.Function)).Append(',')
              .Append(CsvField(bc.Input)).Append(',')
              .Append(w.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(h.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(d.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(CsvField(bc.Params)).Append(',');

            if (r.Failed)
            {
                sb.AppendLine($"0,,,,,,{CsvField(bc.Category)},{ocrFlag}");
            }
            else
            {
                string gpuStr = r.GpuUsed ? "yes" : "no";
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"{r.Runs},{r.MinMs:F3},{r.MedianMs:F3},{r.MeanMs:F3},{r.MadMs:F3},{gpuStr},{r.GpuDispatchesPerRun},{CsvField(bc.Category)},{ocrFlag}"));
            }
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void PrintResults(BenchCtx ctx, List<BenchCase> sel, CaseResult[] res)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"{"case",-22} {"function",-25} {"input",-18} {"w",5} {"h",5} {"d",3} {"gpu",5} {"runs",6} {"min_ms",10} {"median_ms",10} {"mean_ms",10} {"mad_ms",10}");

        for (int i = 0; i < sel.Count; i++)
        {
            BenchCase bc = sel[i];
            CaseResult r = res[i];
            Pix? pix = bc.GetPrimaryPix(ctx);
            int w = pix?.Width ?? 0;
            int h = pix?.Height ?? 0;
            int d = pix?.Depth ?? 0;

            if (r.Failed)
            {
                Console.WriteLine($"{bc.Name,-22} {bc.Function,-25} {bc.Input,-18} {w,5} {h,5} {d,3} {"-",5}    FAILED");
            }
            else
            {
                string gpuLabel = r.GpuUsed
                    ? (r.GpuDispatchesPerRun > 1 ? $"YES*{r.GpuDispatchesPerRun}" : "YES")
                    : "no";
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{bc.Name,-22} {bc.Function,-25} {bc.Input,-18} {w,5} {h,5} {d,3} {gpuLabel,5} {r.Runs,6} {r.MinMs,10:F3} {r.MedianMs,10:F3} {r.MeanMs,10:F3} {r.MadMs,10:F3}"));
            }
        }
    }
}
