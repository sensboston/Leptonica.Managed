// Straightens the curved text lines of a photographed book page.
//
//   dotnet run --project samples/Dewarp [image] [output folder]
//
// Without arguments it processes images/curved-page.png, a photo of an open book whose text
// lines bend towards the binding. It writes the input, the straightened page and a
// side-by-side comparison (input on the left, result on the right) into ./output/dewarp.
// If the page has too few text lines to build a model, the program says so.

using System.Diagnostics;
using Leptonica;

string input = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "images", "curved-page.png");
string outDir = args.Length > 1 ? args[1] : Path.Combine("output", "dewarp");
Directory.CreateDirectory(outDir);

// Optional: start the GPU (if there is one) now, so that its one-time start-up cost is not
// counted in the first step that uses it.
Step("GPU start-up", () => LeptGpu.Warmup());

var total = Stopwatch.StartNew();
Pix page = Step("read", () => Pix.Read(input));
Console.WriteLine($"  {Path.GetFileName(input)}: {page.Width} x {page.Height}, {page.Depth} bpp");
Save(page, "1-input.png");

// 1. Dewarp the page in one call. Internally the library binarizes a copy of the page, finds
//    the text lines, measures how far each line is from straight (the "vertical disparity")
//    and fits a smooth surface through these measurements. The surface is then used to remap
//    the ORIGINAL image, so the result keeps its color and gray levels.
//    Arguments: threshold 0 and adaptive = 1 (binarize with a locally adaptive threshold,
//    which copes with the shading of a photo), useboth = 1 (also correct the horizontal
//    disparity, i.e. the compression of the text towards the binding), checkColumns = 0
//    (do not give up on pages with several text columns), debug = 0.
Pix? straight = null;
Dewarpa? model = null;
int status = Step("dewarp", () => Dewarp.SinglePage(page, 0, 1, 1, 0, out straight, out model, 0));

// 2. Check the outcome. On a page without enough text lines (a photo, a title page) no model
//    can be built; the library then returns a plain copy of the input and a failure code.
Dewarp? dew = model?.GetDewarp(0);
bool ok = status == 0 && dew != null && (dew.VSuccess || dew.HSuccess);
if (!ok || straight == null)
{
    Console.WriteLine("No dewarping model could be built for this image: it needs a page of text with" +
                      $" at least {dew?.MinLines ?? 6} reasonably long, roughly horizontal text lines.");
    return;
}
Console.WriteLine($"  model: {dew!.NumLines} text lines, vertical {(dew.VSuccess ? "ok" : "failed")}, " +
                  $"horizontal {(dew.HSuccess ? "ok" : "failed")}");
Save(straight, "2-dewarped.png");

// 3. Put the input and the result next to each other on one canvas (a light gray gap in
//    between), so that the improvement can be judged at a glance.
Pix a = page.ConvertTo32();
Pix b = straight.ConvertTo32();
const int gap = 20;
Pix both = Step("side by side", () =>
{
    Pix canvas = Pix.Create(a.Width + gap + b.Width, Math.Max(a.Height, b.Height), 32);
    canvas.SetAll();
    canvas.Rasterop(0, 0, a.Width, a.Height, L.PIX_SRC, a, 0, 0);
    canvas.Rasterop(a.Width + gap, 0, b.Width, b.Height, L.PIX_SRC, b, 0, 0);
    return canvas;
});
Save(both, "3-side-by-side.png");

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
