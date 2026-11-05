// Builds a compact multipage PDF archive from scanned pages.
//
//   dotnet run --project samples/ScansToPdf [image or folder] [output folder]
//
// Without arguments it takes four bundled pages (a magazine article, two newspaper pages and a
// yellowed book page), cleans each one up and writes them as ONE PDF with G4 (CCITT fax)
// compression into ./output/pdf. With a folder argument it uses every PNG/JPEG/TIFF in it.
// The report at the end compares the PDF size with the size of the input files.
//
// Note: Leptonica can WRITE PDF files, but it can neither read nor render them.

using System.Diagnostics;
using Leptonica;

string imageDir = Path.Combine(AppContext.BaseDirectory, "images");
string[] inputs = args.Length > 0 && Directory.Exists(args[0])
    ? Directory.GetFiles(args[0]).Where(f => new[] { ".png", ".jpg", ".jpeg", ".tif", ".tiff" }
          .Contains(Path.GetExtension(f).ToLowerInvariant())).Order().ToArray()
    : args.Length > 0 ? new[] { args[0] }
    : new[] { "article.png", "newspaper.png", "newspaper-2.png", "old-page.png" }
          .Select(n => Path.Combine(imageDir, n)).ToArray();
string outDir = args.Length > 1 ? args[1] : Path.Combine("output", "pdf");
Directory.CreateDirectory(outDir);

// Optional: start the GPU (if there is one) now, so that its one-time start-up cost is not
// counted in the first step that uses it.
Step("GPU start-up", () => LeptGpu.Warmup());

var total = Stopwatch.StartNew();
var pages = Pixa.Create(inputs.Length);
long inputBytes = 0;

foreach (string file in inputs)
{
    inputBytes += new FileInfo(file).Length;
    Pix page = Step($"read {Path.GetFileName(file)}", () => Pix.Read(file));
    Console.WriteLine($"  {page.Width} x {page.Height}, {page.Depth} bpp");

    // 1. A page that is already black and white is kept as it is: it is exactly what G4
    //    compression is made for, and any processing could only damage the scan.
    //    Gray and color pages are first flattened and then reduced to 1 bpp.
    Pix bw;
    if (page.Depth == 1)
    {
        bw = page;
    }
    else
    {
        // 2. Flatten the background: uneven light and yellowed paper become a uniform white
        //    while the text keeps its contrast. A single global threshold then works.
        Pix flat = Step("  background", () => page.ConvertTo8(false).BackgroundNormSimple(null, null));

        // 3. Threshold to 1 bpp: pixels darker than 130 become black ink. The 1 bpp page is
        //    typically 20 to 50 times smaller than the color scan.
        bw = Step("  threshold", () => flat.ThresholdToBinary(130));
    }

    // 4. Straighten the page. A scan is rarely perfectly aligned, and a straight page is
    //    easier to read and compresses a little better. FindSkewAndDeskew rotates only when
    //    the measured angle is significant, so a straight page is returned unchanged.
    float angle = 0, conf = 0;
    Pix straight = Step("  deskew", () => bw.FindSkewAndDeskew(2, out angle, out conf));
    Console.WriteLine($"  skew {angle:F2} degrees (confidence {conf:F1})");

    // 5. Tell the PDF writer the resolution. Pages without a stored resolution would get
    //    a huge, arbitrary page size; 300 ppi is the usual value for archive scans.
    straight.SetResolution(300, 300);
    pages.AddPix(straight, L.L_INSERT);
}

// 6. Write all pages into one PDF. L_G4_ENCODE compresses every page with CCITT Group 4,
//    the standard lossless coding of black and white documents. A scale factor of 1.0 and
//    resolution 0 mean "keep the pages as they are".
string pdfPath = Path.Combine(outDir, "archive.pdf");
Step("write pdf", () => { pages.ConvertToPdf(0, 1.0f, L.L_G4_ENCODE, 0, "Scanned archive", pdfPath); return 0; });

long pdfBytes = new FileInfo(pdfPath).Length;
Console.WriteLine($"{pages.Count} pages: inputs {inputBytes / 1024} KB, pdf {pdfBytes / 1024} KB " +
                  $"({100.0 * pdfBytes / inputBytes:F0}% of the input)");
Console.WriteLine($"done in {total.ElapsedMilliseconds} ms; the PDF is {Path.GetFullPath(pdfPath)}");
Console.WriteLine($"GPU: {(LeptGpu.IsHardwareGpuAvailable ? LeptGpu.ActiveDeviceName : "none, everything ran on the CPU")}");

T Step<T>(string name, Func<T> action)
{
    var sw = Stopwatch.StartNew();
    T result = action();
    Console.WriteLine($"{name,-24}{sw.Elapsed.TotalMilliseconds,8:F1} ms");
    return result;
}
