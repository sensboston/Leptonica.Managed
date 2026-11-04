<#
.SYNOPSIS
    Automated C vs C# (CPU 1T / CPU MT / GPU Auto) benchmark runner for Leptonica 1.87.0 and Leptonica.Managed.

.DESCRIPTION
    Place this script in a folder with `bench_native.exe` (+ its DLLs) and `bench_managed.exe`.
    Automatically detects the Windows computer name (or uses -Prefix), locates the `prog` image folder,
    collects hardware metadata (CPU, GPU, RAM, OS), runs native & managed benchmark passes, shows live
    per-case progress (highlighting [GPU] dispatches), and packs all CSVs + logs + sysinfo into a single .zip.

.EXAMPLE
    .\run.ps1
    # Uses $env:COMPUTERNAME as prefix, runs full benchmark (1000 ms/case), produces <COMPUTERNAME>_bench_results.zip

.EXAMPLE
    .\run.ps1 RTX4080_PC
    # Uses "RTX4080_PC" as prefix -> RTX4080_PC_bench_results.zip

.EXAMPLE
    .\run.ps1 Laptop -MinTimeMs 100
    # Quick ~45-second smoke run across all 4 modes
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0, HelpMessage = "Result file prefix (defaults to Windows computer name)")]
    [string]$Prefix = "",

    [Parameter(HelpMessage = "Path to Leptonica prog directory with feyn.tif, test8.jpg, marge.jpg, weasel-64g.png")]
    [string]$ProgDir = "",

    [Parameter(HelpMessage = "Minimum cumulative measured time per case in ms (default: 1000)")]
    [int]$MinTimeMs = 1000,

    [Parameter(HelpMessage = "Optional case name substring filter (e.g. 'gray' or 'scale')")]
    [string]$Filter = "",

    [Parameter(HelpMessage = "Skip the single-threaded managed CPU pass (--threads 1)")]
    [switch]$SkipSingleThreadCpu,

    [Parameter(HelpMessage = "Also run a 5th pass with --gpu force (ignores PCIe size threshold)")]
    [switch]$IncludeForceGpu,

    [Parameter(HelpMessage = "Directory where the output folder and .zip will be written (defaults to script folder)")]
    [string]$OutDir = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScriptDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ScriptDir)) {
    $ScriptDir = (Get-Location).Path
}

# 1. Resolve prefix (explicit parameter or Windows computer name)
if ([string]::IsNullOrWhiteSpace($Prefix)) {
    $Prefix = $env:COMPUTERNAME
    if ([string]::IsNullOrWhiteSpace($Prefix)) {
        $Prefix = [System.Net.Dns]::GetHostName()
    }
}
$SafePrefix = ($Prefix.Trim() -replace '[^A-Za-z0-9._-]+', '_')
if ([string]::IsNullOrWhiteSpace($SafePrefix)) {
    $SafePrefix = "pc"
}

# 2. Locate executables
$NativeExe  = Join-Path $ScriptDir "bench_native.exe"
$ManagedExe = Join-Path $ScriptDir "bench_managed.exe"

if (-not (Test-Path -LiteralPath $NativeExe)) {
    throw "bench_native.exe not found in '$ScriptDir'. Place run.ps1 in the same folder as bench_native.exe and bench_managed.exe."
}
if (-not (Test-Path -LiteralPath $ManagedExe)) {
    throw "bench_managed.exe not found in '$ScriptDir'. Place run.ps1 in the same folder as bench_native.exe and bench_managed.exe."
}

# 3. Auto-detect Leptonica 'prog' test images directory if not explicitly passed
if ([string]::IsNullOrWhiteSpace($ProgDir)) {
    $Candidates = @(
        (Join-Path $ScriptDir "prog"),
        (Join-Path $ScriptDir "..\testdata\prog"),
        (Join-Path $ScriptDir "..\prog"),
        (Join-Path $ScriptDir "..\artifacts\native\leptonica\prog")
    )
    foreach ($cand in $Candidates) {
        if (Test-Path -LiteralPath (Join-Path $cand "feyn.tif")) {
            $ProgDir = (Resolve-Path -LiteralPath $cand).Path
            break
        }
    }
} else {
    if (-not (Test-Path -LiteralPath $ProgDir)) {
        throw "Specified -ProgDir does not exist: '$ProgDir'"
    }
    $ProgDir = (Resolve-Path -LiteralPath $ProgDir).Path
}

if ([string]::IsNullOrWhiteSpace($ProgDir)) {
    throw ("Could not find Leptonica 'prog' image directory (expected feyn.tif, test8.jpg, marge.jpg, weasel-64g.png). " +
           "Copy testdata\prog into '$ScriptDir\prog' (recommended for portable multi-PC runs) or pass -ProgDir <path>.")
}

foreach ($reqImg in @("feyn.tif", "test8.jpg", "marge.jpg", "weasel-64g.png")) {
    $imgPath = Join-Path $ProgDir $reqImg
    if (-not (Test-Path -LiteralPath $imgPath)) {
        throw "Required test image missing: '$imgPath'"
    }
}

# 4. Prepare output workspace and target .zip path
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = $ScriptDir
}
if (-not (Test-Path -LiteralPath $OutDir)) {
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
}
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path

$RunFolderName = "${SafePrefix}_bench_results"
$WorkDir       = Join-Path $OutDir $RunFolderName
$ZipPath       = Join-Path $OutDir "${RunFolderName}.zip"

if (Test-Path -LiteralPath $WorkDir) {
    Remove-Item -LiteralPath $WorkDir -Recurse -Force
}
New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null

# 5. Collect hardware & OS provenance into <prefix>_sysinfo.json
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host " Leptonica 1.87.0 Benchmark Suite  |  Host Prefix: $SafePrefix" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan

$CpuList = @()
$GpuList = @()
$OsCaption = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
$TotalRamGb = 0.0

try {
    $procs = @(Get-CimInstance -ClassName Win32_Processor -ErrorAction Stop)
    foreach ($p in $procs) {
        $CpuList += [ordered]@{
            name                = ($p.Name -replace '\s+', ' ').Trim()
            cores               = $p.NumberOfCores
            logical_processors  = $p.NumberOfLogicalProcessors
            max_clock_mhz       = $p.MaxClockSpeed
        }
    }
} catch {}

try {
    $gpus = @(Get-CimInstance -ClassName Win32_VideoController -ErrorAction Stop)
    foreach ($g in $gpus) {
        $vramMb = if ($g.AdapterRAM) { [math]::Round(($g.AdapterRAM / 1MB), 0) } else { 0 }
        $GpuList += [ordered]@{
            name           = $g.Name
            driver_version = $g.DriverVersion
            status         = $g.Status
            adapter_ram_mb = $vramMb
        }
    }
} catch {}

try {
    $osObj = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop
    if ($osObj.Caption) {
        $OsCaption = "$($osObj.Caption) ($($osObj.Version), Build $($osObj.BuildNumber))"
    }
    if ($osObj.TotalVisibleMemorySize) {
        $TotalRamGb = [math]::Round(($osObj.TotalVisibleMemorySize / 1MB), 1)
    }
} catch {}

$SysInfo = [ordered]@{
    prefix              = $SafePrefix
    computer_name       = $env:COMPUTERNAME
    timestamp_utc       = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    timestamp_local     = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    os                  = $OsCaption
    ram_gb              = $TotalRamGb
    logical_processors  = [Environment]::ProcessorCount
    cpus                = $CpuList
    gpus                = $GpuList
    min_time_ms         = $MinTimeMs
    filter              = $Filter
}

$SysInfoPath = Join-Path $WorkDir "${SafePrefix}_sysinfo.json"
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($SysInfoPath, ($SysInfo | ConvertTo-Json -Depth 6), $Utf8NoBom)

if ($CpuList.Count -gt 0) {
    Write-Host ("  CPU       : {0} ({1}C/{2}T)" -f $CpuList[0].name, $CpuList[0].cores, $CpuList[0].logical_processors)
}
if ($GpuList.Count -gt 0) {
    $gpuNames = ($GpuList | ForEach-Object { $_.name }) -join "; "
    Write-Host ("  GPU(s)    : {0}" -f $gpuNames)
} else {
    Write-Host "  GPU(s)    : (none reported by WMI)"
}
Write-Host ("  OS / RAM  : {0} | {1} GB RAM" -f $OsCaption, $TotalRamGb)
Write-Host ("  Images    : {0}" -f $ProgDir)
Write-Host ("  MinTimeMs : {0} ms per case" -f $MinTimeMs)
if (-not [string]::IsNullOrWhiteSpace($Filter)) {
    Write-Host ("  Filter    : {0}" -f $Filter)
}
Write-Host ""

# Helper to quote CLI arguments safely for ProcessStartInfo.Arguments (compatible with PS 5.1 & PS 7)
function Format-CliArg([string]$s) {
    if ($null -eq $s) { return '""' }
    if ($s.Length -gt 0 -and $s -notmatch '[\s"]') {
        return $s
    }
    $escaped = $s -replace '(\\*)"', '$1$1\"'
    $escaped = $escaped -replace '(\\+)$', '$1$1'
    return '"' + $escaped + '"'
}

function Invoke-BenchPass {
    param(
        [string]$Title,
        [string]$Tag,
        [string]$ExePath,
        [string[]]$ExtraArgs
    )

    $CsvPath    = Join-Path $WorkDir "${SafePrefix}_${Tag}.csv"
    $StdoutPath = Join-Path $WorkDir "${SafePrefix}_${Tag}.stdout.log"
    $StderrPath = Join-Path $WorkDir "${SafePrefix}_${Tag}.stderr.log"

    $ArgList = New-Object System.Collections.Generic.List[string]
    $ArgList.Add($ProgDir)
    $ArgList.Add("--out")
    $ArgList.Add($CsvPath)
    $ArgList.Add("--min-time-ms")
    $ArgList.Add($MinTimeMs.ToString([System.Globalization.CultureInfo]::InvariantCulture))
    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        $ArgList.Add("--filter")
        $ArgList.Add($Filter)
    }
    foreach ($a in $ExtraArgs) {
        $ArgList.Add($a)
    }

    $ArgString = ($ArgList | ForEach-Object { Format-CliArg $_ }) -join " "

    Write-Host "----------------------------------------------------------------------" -ForegroundColor Yellow
    Write-Host ">>> [$Tag] $Title" -ForegroundColor Yellow
    Write-Host "    $(Split-Path -Leaf $ExePath) $ArgString" -ForegroundColor DarkGray
    Write-Host "----------------------------------------------------------------------" -ForegroundColor Yellow

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName               = $ExePath
    $psi.Arguments              = $ArgString
    $psi.WorkingDirectory       = $ScriptDir
    $psi.UseShellExecute        = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError  = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding  = [System.Text.Encoding]::UTF8

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    [void]$proc.Start()

    # Drain stdout asynchronously to prevent OS pipe deadlock while streaming stderr live
    $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
    $stderrLines = New-Object System.Collections.Generic.List[string]

    while ($null -ne ($line = $proc.StandardError.ReadLine())) {
        $stderrLines.Add($line)
        if ($line -match '\[GPU') {
            Write-Host $line -ForegroundColor Green
        } elseif ($line -match 'FAILED|error:|fatal:') {
            Write-Host $line -ForegroundColor Red
        } else {
            Write-Host $line -ForegroundColor Gray
        }
    }

    $proc.WaitForExit()
    $sw.Stop()
    $stdoutText = $stdoutTask.GetAwaiter().GetResult()

    [System.IO.File]::WriteAllText($StdoutPath, $stdoutText, $Utf8NoBom)
    [System.IO.File]::WriteAllLines($StderrPath, $stderrLines, $Utf8NoBom)

    if ($proc.ExitCode -ne 0) {
        Write-Host $stdoutText -ForegroundColor Red
        throw "Benchmark pass '$Tag' exited with code $($proc.ExitCode). See '$StdoutPath' and '$StderrPath'."
    }

    # Print key summary lines from stdout (gpu-device, gpu summary, wrote ...)
    foreach ($sLine in ($stdoutText -split "\r?\n")) {
        if ($sLine -match '^\s*gpu-(mode|device)\s*:' -or $sLine -match '^gpu summary:' -or $sLine -match '^  gpu cases :' -or $sLine -match '^wrote ') {
            $color = if ($sLine -match '^gpu summary:\s+USED\b') { "Green" } else { "Cyan" }
            Write-Host "  $($sLine.Trim())" -ForegroundColor $color
        }
    }
    Write-Host ("  completed in {0:F1} s" -f $sw.Elapsed.TotalSeconds) -ForegroundColor DarkCyan
    Write-Host ""

    return [ordered]@{
        tag         = $Tag
        title       = $Title
        csv_file    = (Split-Path -Leaf $CsvPath)
        elapsed_sec = [math]::Round($sw.Elapsed.TotalSeconds, 2)
    }
}

# 6. Execute benchmark passes
$TotalSw = [System.Diagnostics.Stopwatch]::StartNew()
$PassSummaries = @()

$PassSummaries += Invoke-BenchPass `
    -Title "Native C Leptonica 1.87.0 (1 thread CPU)" `
    -Tag   "native" `
    -ExePath $NativeExe `
    -ExtraArgs @()

if (-not $SkipSingleThreadCpu) {
    $PassSummaries += Invoke-BenchPass `
        -Title "Leptonica.Managed (.NET, GPU OFF, 1 CPU thread -- core-for-core vs C)" `
        -Tag   "managed_cpu_1t" `
        -ExePath $ManagedExe `
        -ExtraArgs @("--no-gpu", "--threads", "1")
}

$PassSummaries += Invoke-BenchPass `
    -Title "Leptonica.Managed (.NET, GPU OFF, multi-thread CPU)" `
    -Tag   "managed_cpu_mt" `
    -ExePath $ManagedExe `
    -ExtraArgs @("--no-gpu", "--threads", "auto")

$PassSummaries += Invoke-BenchPass `
    -Title "Leptonica.Managed (.NET, GPU AUTO -- hardware GPU when faster, else CPU)" `
    -Tag   "managed_gpu_auto" `
    -ExePath $ManagedExe `
    -ExtraArgs @("--gpu", "auto", "--threads", "auto")

if ($IncludeForceGpu) {
    $PassSummaries += Invoke-BenchPass `
        -Title "Leptonica.Managed (.NET, GPU FORCE -- offload all GPU-capable kernels)" `
        -Tag   "managed_gpu_force" `
        -ExePath $ManagedExe `
        -ExtraArgs @("--gpu", "force", "--threads", "auto")
}

$TotalSw.Stop()

# Update sysinfo.json with completed pass manifest
$SysInfo["total_elapsed_sec"] = [math]::Round($TotalSw.Elapsed.TotalSeconds, 2)
$SysInfo["passes"] = $PassSummaries
[System.IO.File]::WriteAllText($SysInfoPath, ($SysInfo | ConvertTo-Json -Depth 6), $Utf8NoBom)

# 7. Pack everything into <prefix>_bench_results.zip
if (Test-Path -LiteralPath $ZipPath) {
    Remove-Item -LiteralPath $ZipPath -Force
}
Get-ChildItem -LiteralPath $WorkDir | Compress-Archive -DestinationPath $ZipPath -CompressionLevel Optimal -Force

$ZipSizeKb = [math]::Round(((Get-Item -LiteralPath $ZipPath).Length / 1KB), 1)

Write-Host "======================================================================" -ForegroundColor Green
Write-Host (" ALL BENCHMARKS COMPLETED IN {0:F1} MIN" -f $TotalSw.Elapsed.TotalMinutes) -ForegroundColor Green
Write-Host (" Results folder : {0}" -f $WorkDir) -ForegroundColor Green
Write-Host (" Ready ZIP file : {0} ({1} KB)" -f $ZipPath, $ZipSizeKb) -ForegroundColor Green
Write-Host "======================================================================" -ForegroundColor Green
