# Runs every Leptonica regression test of the native build (tools/native/build.ps1)
# in "generate" mode and collects the golden files into testdata/golden/.
# Leptonica maps /tmp to %TEMP% on Windows. Per-test status goes to
# artifacts/native/goldens.log.
param(
    [int]$TimeoutSec = 600
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$work = Join-Path $root "artifacts\native"
$bin = Join-Path $work "build\bin\Release"
$prog = Join-Path $work "leptonica\prog"
$lept = Join-Path $env:TEMP "lept"
$out = Join-Path $root "testdata\golden"
$log = Join-Path $work "goldens.log"

if (-not (Test-Path $bin)) { throw "native build not found; run tools/native/build.ps1 first" }
$env:PATH = "$bin;$env:PATH"
if (Test-Path $lept) { Remove-Item -Recurse -Force $lept }
New-Item -ItemType Directory -Force $lept | Out-Null
Set-Content $log "test`tstatus`tseconds"

# These do not use regutils (no "generate" mode, no goldens): they compare
# implementations against each other and are ported as self-consistency tests.
$noGoldens = "alltests_reg", "binmorph2_reg", "dwamorph2_reg", "fmorphauto_reg", "morphseq_reg"
$tests = Get-ChildItem $bin -Filter "*_reg.exe" | Where-Object { $noGoldens -notcontains $_.BaseName } | Sort-Object Name
foreach ($t in $tests) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $t.FullName -ArgumentList "generate" -WorkingDirectory $prog `
        -NoNewWindow -PassThru `
        -RedirectStandardOutput (Join-Path $lept "..\lept_$($t.BaseName).out") `
        -RedirectStandardError (Join-Path $lept "..\lept_$($t.BaseName).err")
    if (-not $p.WaitForExit($TimeoutSec * 1000)) {
        $p.Kill()
        $status = "timeout"
    } else {
        $status = "exit $($p.ExitCode)"
    }
    $line = "$($t.BaseName)`t$status`t$([math]::Round($sw.Elapsed.TotalSeconds, 1))"
    Add-Content $log $line
    Write-Host $line
}

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item (Join-Path $lept "golden\*") $out
$files = Get-ChildItem $out
Write-Host ("golden files: {0}, {1:N1} MB" -f $files.Count, (($files | Measure-Object Length -Sum).Sum / 1MB))
