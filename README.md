# Leptonica.Managed

A fully managed C# implementation of [Leptonica](http://www.leptonica.org) 1.87.0, the
image processing and image analysis library, for .NET 8 and later.

```
dotnet add package Leptonica.Managed
```

## Leptonica

Leptonica is the work of **Dan Bloomberg**, who has written and maintained it for more than
twenty years. It is a large, carefully designed and thoroughly documented library for
document image analysis: binarization, morphology, connected components, skew detection,
scaling, rotation, color quantization, page segmentation and much more, along with readers
and writers for the common image formats. Among many other programs, the Tesseract OCR engine
is built on it.

Everything in this package comes from that work: the algorithms, the structure of the API, the
documentation of every function and the regression tests that check the results. The original
library lives at [github.com/DanBloomberg/leptonica](https://github.com/DanBloomberg/leptonica).
Thank you, Dan, for Leptonica and for making it free for everyone to use.

## Why a managed version

This package is not meant to compete with the C library. Its purpose is to bring Leptonica to
places where a native library is hard to use, so that more people can benefit from it: .NET
applications that run on several operating systems and processor architectures, containers,
cloud functions, and any project where building, shipping and loading native binaries is a
burden.

- **Not a wrapper.** There is no P/Invoke and no native binary in the package. Every function
  is translated to C#, including the image codecs (PNG, JPEG, TIFF, GIF, WebP, JPEG 2000 and
  others), so the package runs wherever .NET runs.
- **The same results.** The port follows the C code function by function and keeps its integer
  and floating-point arithmetic. Leptonica's own regression tests, ported to C#, compare the
  results with golden files produced by the C library; images are compared pixel by pixel.
- **Made for .NET.** Hot paths use SIMD (`Vector128`/`Vector256`, so they are also fast on ARM64)
  and multithreading where the work splits into independent bands. Some operations can run on
  a GPU through [ILGPU](https://github.com/m4rs-mt/ILGPU) (CUDA or OpenCL). In synthetic
  benchmarks a part of the operations is faster than the native library, and a part is slower;
  see [Performance](#performance).
- **.NET conventions.** Errors are exceptions, outputs are return values or `out` parameters,
  memory is managed by the garbage collector, and every public member has XML documentation.

## Quick start

```csharp
using Leptonica;

// Read a scanned page, clean it up and binarize it for OCR.
Pix page = Pix.Read("page.jpg");
Pix gray = page.ConvertTo8(false);
Pix flat = gray.BackgroundNormSimple(null, null);
Pix straight = flat.Deskew(0);
straight.OtsuAdaptiveThreshold(200, 200, 0, 0, 0.1f, out _, out Pix binary);
Pix.Write("page-binary.png", binary, L.IFF_PNG);

// Connected components of the binary image.
Boxa boxes = binary.ConnComp(out Pixa components, 8);
Console.WriteLine($"{boxes.Count} components");
```

## From the C API to C#

The C documentation applies almost unchanged. Names follow a few simple rules:

| C | C# |
|---|---|
| `pixRead("a.png")` | `Pix.Read("a.png")` |
| `pixScale(pixs, 0.5, 0.5)` | `pixs.Scale(0.5f, 0.5f)` |
| `pixDilateBrick(NULL, pixs, 5, 5)` | `Pix.DilateBrick(null, pixs, 5, 5)` (functions with a `pixd` argument stay static) |
| `pixFindSkew(pixs, &angle, &conf)` | `pixs.FindSkew(out float angle, out float conf)` |
| `numaGetCount(na)`, `boxaGetCount(boxa)` | `na.Count`, `boxa.Count` |
| `IFF_PNG`, `L_SELECT_WIDTH`, ... | `L.IFF_PNG`, `L.L_SELECT_WIDTH`, ... |
| error code or `NULL` result | `ArgumentException` for invalid arguments, `LeptonicaException` for other failures |
| `pixDestroy(&pix)` | not needed: the garbage collector releases images |

Each ported function carries a `// leptonica: pixFoo (file.c)` marker in the source, so the C
name of any C# method is easy to find, and the other way round. Messages that the C library
prints to stderr go to `Log.Sink` (dropped unless a sink is installed).

## GPU and threads

Nothing has to be configured. With the default `GpuMode.Auto` the library looks for a CUDA or
OpenCL device the first time an operation is large enough to benefit from it, and uses it for
the operations that have GPU kernels; on a machine without a GPU everything runs on the CPU.
The results are the same in both cases.

```csharp
LeptGpu.Mode = GpuMode.CpuOnly;            // never use the GPU (or set LEPT_GPU_MODE=cpu)
LeptParallel.MaxDegreeOfParallelism = 1;   // single-threaded, like the C library
```

## Image formats

| Format | Read | Write |
|---|:---:|:---:|
| PNG | yes | yes |
| JPEG | yes | yes |
| TIFF (uncompressed, G3, G4, LZW, ZIP, JPEG, multipage) | yes | yes |
| GIF | yes | yes |
| BMP | yes | yes |
| PNM (PBM, PGM, PPM, PAM) | yes | yes |
| WebP | yes | yes |
| JPEG 2000 | yes | yes |
| SPIX (Leptonica serialization) | yes | yes |
| PDF, PostScript | | yes |

## Performance

The benchmark runs 68 fixed operations on the C library and on this package and reports the
speedup `T(native) / T(managed)` (the geometric mean over all operations; above 1 means the
managed code is faster). The native library is the regular Release build of Leptonica 1.87.0
with its codecs from vcpkg (MSVC, x64). The benchmark programs are in [benchmarks](benchmarks);
the results for several machines will be published here.

## Status

- More than 97% of the public C functions (about 2,700) are ported. What is left are mostly
  functions that have no use in .NET, such as displaying images with external viewers.
- Tested on Windows x64 and on Linux x64 and ARM64, with .NET 8 and .NET 10.
- The regression tests of the C library are ported and run in CI against the golden files of
  the C library.

## Building and testing

```
dotnet build
dotnet test
```

The regression tests need the golden files of the C library in `testdata/golden`. They are not
stored in the repository; `tools/native/build.ps1` builds the C library (Windows, Visual Studio
and vcpkg) and `tools/native/goldens.ps1` generates them. Without them the regression tests are
skipped and the other tests run.

## License

BSD 2-Clause, the same license as Leptonica; see [LICENSE](LICENSE). The package also contains
C# versions of other open source libraries (LibJpeg.Net, LibTiff.Net, CoreJ2K and a WebP codec);
their licenses are in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
