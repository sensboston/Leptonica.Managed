# Converts the non-png input images of the regression tests (prog/ of the native
# source) to png with the native build, into testdata/inputs/<name>.png
# (e.g. feyn-fract.tif.png). The decoded pixels are exactly the ones the C tests
# read. Failures go to artifacts/native/inputs.log.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$work = Join-Path $root "artifacts\native"
$bin = Join-Path $work "build\bin\Release"
$prog = Join-Path $work "leptonica\prog"
$out = Join-Path $root "testdata\inputs"
$log = Join-Path $work "inputs.log"

if (-not (Test-Path $bin)) { throw "native build not found; run tools/native/build.ps1 first" }
$env:PATH = "$bin;$env:PATH"
New-Item -ItemType Directory -Force $out | Out-Null
Set-Content $log "file`tstatus"
$exts = ".tif", ".tiff", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".pnm", ".pbm", ".pgm", ".ppm", ".jp2"
$files = Get-ChildItem $prog -File | Where-Object { $exts -contains $_.Extension.ToLower() } | Sort-Object Name
$ok = 0
foreach ($f in $files) {
    $dst = Join-Path $out ($f.Name + ".png")
    & (Join-Path $bin "convertformat.exe") $f.FullName $dst PNG 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0 -and (Test-Path $dst)) { $ok++; $s = "ok" } else { $s = "failed" }
    Add-Content $log "$($f.Name)`t$s"
}
Write-Host "$ok of $($files.Count) converted to $out"
