# Leptonica.Managed

A fully managed C# implementation of [Leptonica](https://github.com/DanBloomberg/leptonica)
1.87.0, the image processing and image analysis library written by Dan Bloomberg.

- **No P/Invoke, no native binaries.** Every function is translated to C#, including the image
  codecs, so the package runs wherever .NET runs.
- **The same results as the C library.** Leptonica's own regression tests, ported to C#, compare
  the results with golden files produced by the C library.
- **Made for .NET.** SIMD, multithreading, and GPU acceleration through ILGPU for some operations
  when a CUDA or OpenCL device is present; without a GPU everything runs on the CPU.

```csharp
using Leptonica;

Pix page = Pix.Read("page.jpg");
Pix gray = page.ConvertTo8(false);
Pix flat = gray.BackgroundNormSimple(null, null);
Pix straight = flat.Deskew(0);
straight.OtsuAdaptiveThreshold(200, 200, 0, 0, 0.1f, out _, out Pix binary);
Pix.Write("page-binary.png", binary, L.IFF_PNG);
```

The C documentation applies: `pixDeskew(pixs, 0)` is `pixs.Deskew(0)`, `pixRead(name)` is
`Pix.Read(name)`, output arguments are `out` parameters and errors are exceptions.

Documentation, the mapping from the C API and benchmarks:
[github.com/sensboston/Leptonica.Managed](https://github.com/sensboston/Leptonica.Managed).

Leptonica is the work of Dan Bloomberg; this package exists thanks to it. License: BSD 2-Clause,
like Leptonica; the licenses of the included codec libraries are in THIRD-PARTY-NOTICES.txt.
