// A tour of basic image operations, each written as a PNG.
//
//   dotnet run --project samples/ImageBasics [image] [output folder]
//
// Without arguments it uses images/photo.png (576 x 432, color) and writes one PNG per
// operation into ./output/basics, followed by the same photo in several file formats.
// The operations are the everyday ones: scaling, rotation, sharpening, blur, color reduction
// and edge detection.

using System.Diagnostics;
using Leptonica;

string input = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "images", "photo.png");
string outDir = args.Length > 1 ? args[1] : Path.Combine("output", "basics");
Directory.CreateDirectory(outDir);

// Optional: start the GPU (if there is one) now, so that its one-time start-up cost is not
// counted in the first step that uses it.
Step("GPU start-up", () => LeptGpu.Warmup());

var total = Stopwatch.StartNew();
Pix photo = Step("read", () => Pix.Read(input));
Console.WriteLine($"  {Path.GetFileName(input)}: {photo.Width} x {photo.Height}, {photo.Depth} bpp");
Pix gray = photo.ConvertTo8(false);

// 1. Scale up 2x. Scale() picks the method by itself; for enlarging a color image this is
//    linear interpolation, which gives smooth results without blocky pixels.
Show("scale up 2x (linear)", Step("scale up", () => photo.ScaleColorLI(2.0f, 2.0f)), "1-scale-up.png");

// 2. Scale down to 37%. For strong reductions Scale() low-pass filters first, so thin
//    details do not turn into aliasing noise.
Show("scale down to 0.37", Step("scale down", () => photo.Scale(0.37f, 0.37f)), "2-scale-down.png");

// 3. Rotate 7 degrees about the center. Area mapping is the highest quality method (each
//    output pixel is a weighted mix of the input pixels it covers); the corners that come
//    into view are filled with white.
Show("rotate 7 degrees", Step("rotate", () => photo.Rotate(7.0f * MathF.PI / 180f, L.L_ROTATE_AREA_MAP, L.L_BRING_IN_WHITE, 0, 0)),
     "3-rotate.png");

// 4. Unsharp masking: subtract a blurred copy to emphasize edges. Half-width 3 is the
//    radius of the blur, 0.6 the strength of the effect.
Show("unsharp masking", Step("unsharp", () => photo.UnsharpMasking(3, 0.6f)), "4-unsharp.png");

// 5. Blur with a 11 x 11 box filter. The block convolution uses an accumulated image,
//    so its cost does not depend on the size of the filter.
Show("block blur 11 x 11", Step("blur", () => photo.Blockconv(5, 5)), "5-blur.png");

// 6. Gray, and 1 bpp with error-diffusion dithering: the gray levels survive as a
//    pattern of black and white dots, as in a newspaper picture.
Show("gray", gray, "6-gray.png");
Show("1 bpp, dithered", Step("dither", () => gray.DitherToBinary()), "7-dither.png");

// 7. Reduce the colors to at most 64 with octree quantization: the color cube is split
//    into octants and the busiest ones become the palette. The result has a colormap
//    (8 bpp), so the PNG is much smaller than the 24 bit original. The last argument is
//    the subsampling step used when counting colors (1 = look at every pixel).
Show("octree, 64 colors", Step("octree", () => photo.OctreeQuantNumColors(64, 1)), "8-octree.png");

// 8. Gray morphology: the white top hat keeps what is brighter than its surroundings
//    on a scale of 15 pixels and removes the smooth background.
Show("top hat (white, 15 x 15)", Step("top hat", () => gray.Tophat(15, 15, L.L_TOPHAT_WHITE)), "9-tophat.png");

// 9. Sobel edge filter: the strength of the gray level gradient in both directions;
//    bright means a sharp edge.
Show("Sobel edges", Step("sobel", () => gray.SobelEdgeFilter(L.L_ALL_EDGES)), "10-sobel.png");

// 10. The same photo in different file formats. PNG is lossless; JPEG and WebP trade a
//     little quality for size; TIFF with ZIP compression is lossless like PNG.
Console.WriteLine("file formats:");
Report("PNG", Pix.WriteMem(photo, L.IFF_PNG), "photo.png");
Report("JPEG (quality 85)", Pix.WriteMemJpeg(photo, 85, 0), "photo.jpg");
Report("WebP (quality 80)", Pix.WriteMemWebP(photo, 80, 0), "photo.webp");
Report("TIFF (ZIP)", Pix.WriteMem(photo, L.IFF_TIFF_ZIP), "photo.tif");

Console.WriteLine($"done in {total.ElapsedMilliseconds} ms; the images are in {Path.GetFullPath(outDir)}");
Console.WriteLine($"GPU: {(LeptGpu.IsHardwareGpuAvailable ? LeptGpu.ActiveDeviceName : "none, everything ran on the CPU")}");

T Step<T>(string name, Func<T> action)
{
    var sw = Stopwatch.StartNew();
    T result = action();
    Console.WriteLine($"{name,-14}{sw.Elapsed.TotalMilliseconds,8:F1} ms");
    return result;
}

void Show(string what, Pix pix, string file)
{
    Pix.Write(Path.Combine(outDir, file), pix, L.IFF_PNG);
    Console.WriteLine($"  -> {file}: {what}, {pix.Width} x {pix.Height}, {pix.Depth} bpp");
}

void Report(string name, byte[] data, string file)
{
    File.WriteAllBytes(Path.Combine(outDir, file), data);
    Console.WriteLine($"  {name,-18}{data.Length / 1024.0,8:F1} KB  {file}");
}
