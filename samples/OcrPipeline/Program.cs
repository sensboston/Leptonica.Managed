// Prepares a scanned page for OCR: the classic Leptonica pipeline.
//
//   dotnet run --project samples/OcrPipeline [image] [output folder]
//
// Without arguments it processes images/old-page.png, a yellowed and slightly rotated book
// page, and writes every step as a PNG into ./output/ocr. The last image (5-clean.png) is what
// an OCR engine such as Tesseract should get.

using System.Diagnostics;
using Leptonica;

string input = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "images", "old-page.png");
string outDir = args.Length > 1 ? args[1] : Path.Combine("output", "ocr");
Directory.CreateDirectory(outDir);

// Optional: start the GPU (if there is one) now, so that its one-time start-up cost is not
// counted in the first step that uses it. Without this call the library starts the GPU by
// itself the first time an operation is large enough to run there.
Step("GPU start-up", () => LeptGpu.Warmup());

var total = Stopwatch.StartNew();
Pix page = Step("read", () => Pix.Read(input));
Console.WriteLine($"  {Path.GetFileName(input)}: {page.Width} x {page.Height}, {page.Depth} bpp");

// 1. Color to gray. Leptonica works on 8 bpp gray images for the analysis steps.
Pix gray = Step("gray", () => page.ConvertTo8(false));
Save(gray, "1-gray.png");

// 2. Flatten the background: uneven light and yellowed paper become a uniform white,
//    the text keeps its contrast.
Pix flat = Step("background", () => gray.BackgroundNormSimple(null, null));
Save(flat, "2-background.png");

// 3. Find the skew angle on a binarized copy and rotate the page straight.
float angle = 0, conf = 0;
Pix straight = Step("deskew", () => flat.FindSkewAndDeskew(2, out angle, out conf));
Console.WriteLine($"  skew {angle:F2} degrees (confidence {conf:F1})");
Save(straight, "3-deskew.png");

// 4. Binarize. After the background normalization the paper is a uniform white (200) and a
//    single global threshold separates the ink from it; pixels darker than 130 become black.
//    (For pages without background normalization use a locally adaptive method such as
//    OtsuAdaptiveThreshold or SauvolaBinarize instead.)
Pix binary = Step("binarize", () => straight.ThresholdToBinary(130));
Save(binary, "4-binary.png");

// 5. Remove the noise: keep only connected components that are at least 3 pixels wide or
//    high, so specks and dust disappear and the letters stay.
Pix clean = Step("denoise", () => binary.SelectBySize(3, 3, 8, L.L_SELECT_IF_EITHER, L.L_SELECT_IF_GTE, out _));
Save(clean, "5-clean.png");

Console.WriteLine($"done in {total.ElapsedMilliseconds} ms; the images are in {Path.GetFullPath(outDir)}");
Console.WriteLine($"GPU: {(LeptGpu.IsHardwareGpuAvailable ? LeptGpu.ActiveDeviceName : "none, everything ran on the CPU")}");

T Step<T>(string name, Func<T> action)
{
    var sw = Stopwatch.StartNew();
    T result = action();
    Console.WriteLine($"{name,-14}{sw.Elapsed.TotalMilliseconds,8:F1} ms");
    return result;
}

void Save(Pix pix, string name) => Pix.Write(Path.Combine(outDir, name), pix, L.IFF_PNG);
