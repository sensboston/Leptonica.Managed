using Leptonica;

namespace Explorer;

/// <summary>A parameter of a demo, shown as a slider or a list in the right panel.</summary>
public abstract record Param(string Name);

/// <summary>A number between Min and Max; Step 1 gives whole numbers.</summary>
public sealed record Slider(string Name, double Min, double Max, double Default, double Step = 1) : Param(Name);

/// <summary>One of several named options; the value is the index of the option.</summary>
public sealed record Choice(string Name, string[] Options, int Default = 0) : Param(Name);

/// <summary>
/// One operation of the library: what it is, which picture it starts with, its parameters, and
/// the code that runs it. The code receives the input image and the parameter values by name.
/// </summary>
public sealed record Demo(string Category, string Name, string Image, string Description,
                          Param[] Params, Func<Pix, Values, Pix> Run);

/// <summary>Parameter values of a demo by name.</summary>
public sealed class Values(Dictionary<string, double> values)
{
    public int Int(string name) => (int)Math.Round(values[name]);
    public float Float(string name) => (float)values[name];
}

public static class Demos
{
    private const float Degrees = MathF.PI / 180f;

    public static readonly Demo[] All =
    {
        // ---- Documents -------------------------------------------------------------------
        new("Documents", "Background normalization", "old-page.png",
            "Uneven light and yellowed paper become a uniform white background, while the text keeps its contrast. "
            + "The usual first step before binarization (pixBackgroundNormSimple).",
            [new Choice("Output", ["Normalized", "Normalized and cleaned to white"])],
            (pix, v) =>
            {
                Pix gray = pix.ConvertTo8(false);
                return v.Int("Output") == 0
                    ? gray.BackgroundNormSimple(null, null)
                    : gray.CleanBackgroundToWhite(null, null, 1.0f, 70, 190);
            }),

        new("Documents", "Binarization", "old-page.png",
            "Turns a gray page into black and white. A global threshold is fast and works after background "
            + "normalization; Otsu and Sauvola choose the threshold locally and cope with uneven pages directly.",
            [new Choice("Method", ["Background normalization + threshold", "Adaptive Otsu", "Sauvola"]),
             new Slider("Threshold", 50, 250, 130), new Slider("Sauvola window", 3, 50, 12), new Slider("Sauvola factor", 0.05, 0.6, 0.34, 0.01)],
            (pix, v) =>
            {
                Pix gray = pix.ConvertTo8(false);
                switch (v.Int("Method"))
                {
                    case 0:
                        return gray.BackgroundNormSimple(null, null).ThresholdToBinary(v.Int("Threshold"));
                    case 1:
                        gray.OtsuAdaptiveThreshold(300, 300, 0, 0, 0.1f, out _, out Pix otsu);
                        return otsu;
                    default:
                        gray.SauvolaBinarize(v.Int("Sauvola window"), v.Float("Sauvola factor"), 1, out _, out _, out _, out Pix sauvola);
                        return sauvola;
                }
            }),

        new("Documents", "Deskew", "old-page.png",
            "Rotates the page by the chosen angle to simulate a skewed scan, then finds the skew angle and "
            + "rotates the page straight again (pixFindSkewAndDeskew). The found angle is shown below the result.",
            [new Slider("Skew (degrees)", -10, 10, 4, 0.5)],
            (pix, v) =>
            {
                Pix gray = pix.ConvertTo8(false).BackgroundNormSimple(null, null);
                Pix skewed = gray.Rotate(v.Float("Skew (degrees)") * Degrees, L.L_ROTATE_AREA_MAP, L.L_BRING_IN_WHITE, 0, 0);
                Pix straight = skewed.FindSkewAndDeskew(2, out float angle, out float conf);
                MainWindow.Note = $"skew found: {angle:F2} degrees (confidence {conf:F1})";
                return straight;
            }),

        new("Documents", "Dewarp", "curved-page.png",
            "Straightens the curved lines of a photographed book page: Leptonica fits the text lines, builds a "
            + "disparity model of the page and maps the page flat (dewarpSinglePage).",
            [],
            (pix, v) =>
            {
                int ret = Dewarp.SinglePage(pix, 0, 1, 1, 0, out Pix flat, 0);
                if (ret != 0 || flat == null)
                    throw new InvalidOperationException("no dewarp model could be built for this image");
                return flat;
            }),

        new("Documents", "Page segmentation", "newspaper.png",
            "Finds the pictures, text blocks and text lines of a page (pixGetRegionsBinary). Pictures are tinted "
            + "red, text blocks framed in blue, text lines underlined in green.",
            [],
            (pix, v) =>
            {
                Pix binary = pix.Depth == 1 ? pix : pix.ConvertTo1(128);
                binary.GetRegionsBinary(out Pix halftone, out Pix textlines, out Pix textblocks, null);
                Pix view = binary.ConvertTo8(false).ConvertTo32();
                if (halftone != null)
                {
                    Boxa pictures = halftone.ConnComp(out _, 8);
                    for (int i = 0; i < pictures.Count; i++)
                        view.BlendInRect(pictures.GetBox(i, L.L_CLONE), 0xff000000, 0.35f);
                }
                Frame(view, textblocks, 6, 0, 90, 255);
                Frame(view, textlines, 2, 0, 170, 60);
                return view;
            }),

        // ---- Morphology ------------------------------------------------------------------
        new("Morphology", "Binary morphology", "article.png",
            "Dilation, erosion, opening and closing with a rectangle, the basic tools of document analysis. "
            + "A wide closing joins the letters of a line into one block.",
            [new Choice("Operation", ["Dilate", "Erode", "Open", "Close"], 3), new Slider("Width", 1, 61, 21), new Slider("Height", 1, 61, 1)],
            (pix, v) =>
            {
                Pix b = pix.Depth == 1 ? pix : pix.ConvertTo1(128);
                int w = v.Int("Width"), h = v.Int("Height");
                return v.Int("Operation") switch
                {
                    0 => Pix.DilateBrick(null, b, w, h),
                    1 => Pix.ErodeBrick(null, b, w, h),
                    2 => Pix.OpenBrick(null, b, w, h),
                    _ => Pix.CloseBrick(null, b, w, h),
                };
            }),

        new("Morphology", "Gray morphology", "photo.png",
            "The same operations on gray images: the brightest or darkest value in a window. The top hat keeps "
            + "small bright details. Large windows run on the GPU when there is one.",
            [new Choice("Operation", ["Dilate", "Erode", "Open", "Close", "Top hat"], 4), new Slider("Size", 3, 41, 15, 2)],
            (pix, v) =>
            {
                Pix gray = pix.ConvertTo8(false);
                int s = v.Int("Size") | 1;
                return v.Int("Operation") switch
                {
                    0 => gray.DilateGray(s, s),
                    1 => gray.ErodeGray(s, s),
                    2 => gray.OpenGray(s, s),
                    3 => gray.CloseGray(s, s),
                    _ => Pix.Invert(null, gray.Tophat(s, s, L.L_TOPHAT_WHITE)),
                };
            }),

        new("Morphology", "Connected components", "article.png",
            "Every group of touching black pixels, one color each (pixConnComp). This is how letters, words and "
            + "pictures become objects that can be measured and selected.",
            [new Choice("Connectivity", ["4", "8"], 1)],
            (pix, v) =>
            {
                Pix b = pix.Depth == 1 ? pix : pix.ConvertTo1(128);
                b.ConnComp(out Pixa parts, v.Int("Connectivity") == 0 ? 4 : 8);
                MainWindow.Note = $"{parts.Count} components";
                Pix colored = parts.DisplayRandomCmap(b.Width, b.Height);
                colored.Colormap?.ResetColor(0, 255, 255, 255);   // white background
                return colored;
            }),

        // ---- Filters ---------------------------------------------------------------------
        new("Filters", "Blur", "photo.png",
            "A box blur of any size in constant time per pixel (pixBlockconv).",
            [new Slider("Radius", 1, 30, 5)],
            (pix, v) => pix.Blockconv(v.Int("Radius"), v.Int("Radius"))),

        new("Filters", "Sharpen", "photo.png",
            "Unsharp masking: adds back the difference between the image and its blurred copy (pixUnsharpMasking).",
            [new Slider("Half width", 1, 10, 3), new Slider("Amount", 0.1, 1.0, 0.5, 0.05)],
            (pix, v) => pix.UnsharpMasking(v.Int("Half width"), v.Float("Amount"))),

        new("Filters", "Median filter", "photo.png",
            "Replaces each pixel by the median of its window: removes speckle noise and keeps edges "
            + "(pixMedianFilter). Runs on the GPU when there is one.",
            [new Slider("Size", 3, 15, 5, 2)],
            (pix, v) => pix.MedianFilter(v.Int("Size") | 1, v.Int("Size") | 1)),

        new("Filters", "Bilateral filter", "photo.png",
            "Smooths flat regions and keeps edges sharp (pixBilateral).",
            [new Slider("Spatial sigma", 1, 20, 6, 0.5), new Slider("Range sigma", 5, 100, 30)],
            (pix, v) => pix.Bilateral(v.Float("Spatial sigma"), v.Float("Range sigma"), 10, 1)),

        new("Filters", "Edges", "photo.png",
            "The Sobel edge filter on the gray image, shown inverted: dark lines are strong edges (pixSobelEdgeFilter).",
            [new Choice("Direction", ["Horizontal", "Vertical", "All"], 2)],
            (pix, v) => Pix.Invert(null, pix.ConvertTo8(false).SobelEdgeFilter(v.Int("Direction")))),

        // ---- Geometry --------------------------------------------------------------------
        new("Geometry", "Scale", "photo.png",
            "Resizing with linear interpolation (enlarging), area mapping (reducing without aliasing) or plain "
            + "sampling (fastest, blocky). Enlarging runs on the GPU when there is one.",
            [new Slider("Factor", 0.1, 4, 2.3, 0.05), new Choice("Method", ["Best for the factor", "Linear interpolation", "Area mapping", "Sampling"])],
            (pix, v) =>
            {
                float f = v.Float("Factor");
                return v.Int("Method") switch
                {
                    1 => pix.ScaleLI(f, f),
                    2 => pix.ScaleAreaMap(f, f),
                    3 => pix.ScaleBySampling(f, f),
                    _ => pix.Scale(f, f),
                };
            }),

        new("Geometry", "Rotate", "photo.png",
            "Rotation by any angle: area mapping gives the best quality, shear is fast and exact for binary "
            + "images, sampling is the fastest.",
            [new Slider("Angle (degrees)", -45, 45, 12, 0.5), new Choice("Method", ["Area mapping", "Shear", "Sampling"])],
            (pix, v) =>
            {
                int type = v.Int("Method") switch { 0 => L.L_ROTATE_AREA_MAP, 1 => L.L_ROTATE_SHEAR, _ => L.L_ROTATE_SAMPLING };
                return pix.Rotate(v.Float("Angle (degrees)") * Degrees, type, L.L_BRING_IN_WHITE, 0, 0);
            }),

        // ---- Color -----------------------------------------------------------------------
        new("Color", "Color quantization", "photo.png",
            "Reduces the image to a palette of a few colors chosen from its octree of colors "
            + "(pixOctreeQuantNumColors for small palettes, pixOctreeColorQuant from 128 colors on).",
            [new Slider("Colors", 8, 240, 32), new Choice("Dithering (128 colors and more)", ["Off", "On"])],
            (pix, v) =>
            {
                int colors = v.Int("Colors");
                return colors >= 128
                    ? pix.OctreeColorQuant(colors, v.Int("Dithering (128 colors and more)"))
                    : pix.OctreeQuantNumColors(colors, 1);
            }),

        new("Color", "Gray and black-and-white", "photo.png",
            "Luminance from color, and 1 bpp versions of it: a plain threshold and Floyd-Steinberg dithering.",
            [new Choice("Output", ["Gray (luminance)", "Threshold", "Dithering"], 2), new Slider("Threshold", 20, 235, 128)],
            (pix, v) =>
            {
                Pix gray = pix.Depth == 32 ? pix.ConvertRGBToLuminance() : pix.ConvertTo8(false);
                return v.Int("Output") switch
                {
                    0 => gray,
                    1 => gray.ThresholdToBinary(v.Int("Threshold")),
                    _ => gray.DitherToBinary(),
                };
            }),
    };

    /// <summary>Draws the bounding box of every connected component of a mask onto a 32 bpp image.</summary>
    private static void Frame(Pix view, Pix? mask, int width, byte r, byte g, byte b)
    {
        if (mask == null)
            return;
        Boxa boxes = mask.ConnComp(out _, 8);
        view.RenderBoxaArb(boxes, width, r, g, b);
    }
}
