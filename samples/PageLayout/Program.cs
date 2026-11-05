// Finds the layout of a newspaper page: pictures, text blocks and text lines.
//
//   dotnet run --project samples/PageLayout [1 bpp page image] [output folder]
//
// Without arguments it analyses images/newspaper.png (a 1 bpp page with text and a halftone
// picture) and writes the three masks and an overview image into ./output/layout. In the
// overview (overview.png) the halftone regions are tinted red, the text blocks are framed in
// blue and the text lines in green.

using System.Diagnostics;
using Leptonica;

string input = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "images", "newspaper.png");
string outDir = args.Length > 1 ? args[1] : Path.Combine("output", "layout");
Directory.CreateDirectory(outDir);

// Optional: start the GPU (if there is one) now, so that its one-time start-up cost is not
// counted in the first step that uses it.
Step("GPU start-up", () => LeptGpu.Warmup());

var total = Stopwatch.StartNew();
Pix page = Step("read", () => Pix.Read(input));
Console.WriteLine($"  {Path.GetFileName(input)}: {page.Width} x {page.Height}, {page.Depth} bpp");

// 1. Segment the page. The input must be a 1 bpp image of about 300 to 400 ppi (it is best
//    to deskew it first). The result is three 1 bpp masks of the same size as the page.
Pix halftone = null!, textLines = null!, textBlocks = null!;
Step("segment", () => { page.GetRegionsBinary(out halftone, out textLines, out textBlocks, null); return 0; });
Save(halftone, "1-halftone-mask.png");
Save(textLines, "2-textline-mask.png");
Save(textBlocks, "3-textblock-mask.png");

// 2. Turn each mask into rectangles: every connected component of a mask is one region,
//    and its bounding box is what an application needs (to cut it out, to send only the
//    text blocks to OCR, and so on). 8-connectivity merges diagonal neighbours.
Boxa pictureBoxes = Step("halftone boxes", () => halftone.ConnComp(out _, 8));
Boxa lineBoxes = Step("textline boxes", () => textLines.ConnComp(out _, 8));
Boxa blockBoxes = Step("textblock boxes", () => textBlocks.ConnComp(out _, 8));

// 3. Draw the overview on a color copy of the page. The page itself is converted to gray
//    first, then:
//    - a red tint over every halftone box (blended, so the picture stays visible),
//    - blue frames around the text blocks, 6 pixels wide,
//    - green frames around the text lines, 2 pixels wide.
Pix overview = Step("overview", () =>
{
    Pix rgb = page.ConvertTo8(false).ConvertTo32();
    for (int i = 0; i < pictureBoxes.Count; i++)
        rgb.BlendInRect(pictureBoxes.GetBox(i, L.L_CLONE), 0xff000000, 0.35f);
    rgb.RenderBoxaArb(lineBoxes, 2, 0, 170, 0);
    rgb.RenderBoxaArb(blockBoxes, 6, 0, 0, 255);
    return rgb;
});
Save(overview, "overview.png");

Console.WriteLine($"halftone regions: {pictureBoxes.Count}");
Console.WriteLine($"text blocks:      {blockBoxes.Count}");
Console.WriteLine($"text lines:       {lineBoxes.Count}");
Console.WriteLine($"done in {total.ElapsedMilliseconds} ms; the images are in {Path.GetFullPath(outDir)}");
Console.WriteLine($"GPU: {(LeptGpu.IsHardwareGpuAvailable ? LeptGpu.ActiveDeviceName : "none, everything ran on the CPU")}");

T Step<T>(string name, Func<T> action)
{
    var sw = Stopwatch.StartNew();
    T result = action();
    Console.WriteLine($"{name,-16}{sw.Elapsed.TotalMilliseconds,8:F1} ms");
    return result;
}

void Save(Pix pix, string name) => Pix.Write(Path.Combine(outDir, name), pix, L.IFF_PNG);
