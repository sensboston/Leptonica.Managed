# Changelog

## 1.0.1

- Faster JPEG decoding: a new baseline Huffman decoder, bit-exact with the previous one
  (decoding marge.jpg takes about 15% less time on one thread).
- Faster TIFF G4 decoding: a new CCITT Group 4 decoder, bit-exact with the previous one,
  messages and damaged-data behaviour included (reading a G4 page takes about a third less time).
- GPU kernels are compiled without loading debug symbols: no more first-chance
  `BadImageFormatException` messages in the debugger output when the GPU is used.
- Explorer sample: the window fits the screen's working area on small or scaled screens.

## 1.0.0

First release: a fully managed C# implementation of Leptonica 1.87.0 for .NET 8 and .NET 10.

- More than 97% of the public functions of Leptonica 1.87.0, with the same results as the C
  library; Leptonica's regression tests are ported and compared with the golden files of the C
  library in CI on Windows x64, Linux x64 and Linux ARM64.
- Managed image codecs: PNG, JPEG, TIFF (including G3, G4, LZW, ZIP, JPEG and multipage), GIF,
  BMP, PNM, WebP, JPEG 2000 and SPIX; PDF and PostScript output.
- SIMD (Vector128 and Vector256) and multithreading in the hot paths; GPU kernels through ILGPU
  (CUDA or OpenCL) for some operations, used automatically when a GPU is present.
- XML documentation for every public member.
