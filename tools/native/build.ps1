# Builds Leptonica 1.87.0 (MSVC x64, Release) with all optional codecs from vcpkg,
# including the prog/ utilities and regression tests. This native build is the
# reference for the golden files and the baseline for the benchmarks.
#
# Source:  artifacts/native/leptonica   (cloned at tag 1.87.0 if missing)
# Output:  artifacts/native/build/bin/Release/*.dll, *_reg.exe
#
# Requires Visual Studio (C++ workload), CMake and vcpkg (VCPKG_ROOT).
param(
    [string]$Vcpkg = $env:VCPKG_ROOT,
    [string]$Triplet = "x64-windows"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$work = Join-Path $root "artifacts\native"
$src = Join-Path $work "leptonica"
$build = Join-Path $work "build"

if (-not $Vcpkg) { throw "VCPKG_ROOT is not set" }

if (-not (Test-Path $src)) {
    git clone --depth 1 --branch 1.87.0 https://github.com/DanBloomberg/leptonica.git $src
    if ($LASTEXITCODE) { throw "git clone failed" }
}

& "$Vcpkg\vcpkg.exe" install zlib libpng tiff libjpeg-turbo giflib libwebp openjpeg --triplet $Triplet
if ($LASTEXITCODE) { throw "vcpkg install failed" }

# The regression test patches are applied for the build only; the source stays pristine.
$patches = Get-ChildItem (Join-Path $PSScriptRoot "patches") -Filter "*.patch" | ForEach-Object FullName
foreach ($p in $patches) {
    git -C $src apply --whitespace=nowarn $p
    if ($LASTEXITCODE) { throw "patch failed: $p" }
}
try {
    cmake -S $src -B $build -A x64 `
        "-DCMAKE_TOOLCHAIN_FILE=$Vcpkg\scripts\buildsystems\vcpkg.cmake" `
        "-DVCPKG_TARGET_TRIPLET=$Triplet" `
        -DSW_BUILD=OFF -DBUILD_PROG=ON -DBUILD_SHARED_LIBS=ON
    if ($LASTEXITCODE) { throw "cmake configure failed" }

    cmake --build $build --config Release --parallel
    if ($LASTEXITCODE) { throw "cmake build failed" }
}
finally {
    foreach ($p in $patches) { git -C $src apply -R --whitespace=nowarn $p }
}
