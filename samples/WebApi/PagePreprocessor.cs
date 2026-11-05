using Leptonica;
using Microsoft.Extensions.Options;

namespace WebApi;

/// <summary>Settings of the page pipeline, section "Preprocess" of appsettings.json.</summary>
public sealed class PreprocessOptions
{
    /// <summary>Flatten uneven light and yellowed paper before thresholding.</summary>
    public bool NormalizeBackground { get; set; } = true;

    /// <summary>Find the skew angle and rotate the page straight.</summary>
    public bool Deskew { get; set; } = true;

    /// <summary>Gray level below which a pixel becomes black (after background normalization the paper is at 200).</summary>
    public int Threshold { get; set; } = 130;

    /// <summary>Connected components smaller than this in both directions are removed as noise.</summary>
    public int MinComponentSize { get; set; } = 3;

    /// <summary>Images processed at the same time; every operation already uses all CPU cores.</summary>
    public int MaxConcurrency { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>Largest accepted upload.</summary>
    public long MaxImageBytes { get; set; } = 50 * 1024 * 1024;
}

public sealed record Region(int X, int Y, int Width, int Height);

public sealed record PageLayout(int Width, int Height, float SkewAngle,
                                IReadOnlyList<Region> TextBlocks, IReadOnlyList<Region> TextLines,
                                IReadOnlyList<Region> Images);

/// <summary>Page image processing used by the endpoints.</summary>
public interface IPagePreprocessor
{
    /// <summary>Cleans a scanned page for OCR and returns a 1 bpp PNG.</summary>
    Task<byte[]> PreprocessAsync(byte[] image, CancellationToken cancellationToken);

    /// <summary>Finds the text blocks, text lines and pictures of a page.</summary>
    Task<PageLayout> AnalyzeLayoutAsync(byte[] image, CancellationToken cancellationToken);
}

/// <summary>
/// The pipeline on Leptonica.Managed. Registered as a singleton: it holds no image state, and the
/// library can process different images on several threads at the same time.
/// </summary>
public sealed class LeptonicaPagePreprocessor : IPagePreprocessor, IDisposable
{
    private readonly PreprocessOptions _options;
    private readonly ILogger<LeptonicaPagePreprocessor> _logger;
    private readonly SemaphoreSlim _slots;

    public LeptonicaPagePreprocessor(IOptions<PreprocessOptions> options, ILogger<LeptonicaPagePreprocessor> logger)
    {
        _options = options.Value;
        _logger = logger;
        _slots = new SemaphoreSlim(_options.MaxConcurrency);
    }

    public Task<byte[]> PreprocessAsync(byte[] image, CancellationToken cancellationToken) =>
        RunAsync(image, cancellationToken, pix =>
        {
            Pix clean = Clean(pix, out _);
            return Pix.WriteMem(clean, L.IFF_PNG);
        });

    public Task<PageLayout> AnalyzeLayoutAsync(byte[] image, CancellationToken cancellationToken) =>
        RunAsync(image, cancellationToken, pix =>
        {
            Pix binary = Clean(pix, out float angle);
            // Page segmentation: masks of halftone pictures, text lines and text blocks.
            binary.GetRegionsBinary(out Pix? halftone, out Pix? textlines, out Pix? textblocks, null);
            return new PageLayout(binary.Width, binary.Height, angle,
                                  Regions(textblocks), Regions(textlines), Regions(halftone));
        });

    private async Task<T> RunAsync<T>(byte[] image, CancellationToken cancellationToken, Func<Pix, T> work)
    {
        if (image.Length == 0)
            throw new BadImageException("the request body is empty");
        if (image.Length > _options.MaxImageBytes)
            throw new BadImageException($"the image is larger than {_options.MaxImageBytes} bytes");

        await _slots.WaitAsync(cancellationToken);
        try
        {
            Pix pix;
            try
            {
                pix = Pix.ReadMem(image);
            }
            catch (Exception e) when (e is ArgumentException or LeptonicaException)
            {
                throw new BadImageException("the body is not an image in a supported format");
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            T result = work(pix);
            _logger.LogInformation("Processed {Width}x{Height} {Depth} bpp image in {Ms} ms",
                                   pix.Width, pix.Height, pix.Depth, sw.ElapsedMilliseconds);
            return result;
        }
        finally
        {
            _slots.Release();
        }
    }

    /// <summary>Gray, background normalization, deskew, threshold, noise removal.</summary>
    private Pix Clean(Pix pix, out float angle)
    {
        angle = 0;
        Pix binary;
        if (pix.Depth == 1)
        {
            binary = pix;
        }
        else
        {
            Pix gray = pix.ConvertTo8(false);
            if (_options.NormalizeBackground)
                gray = gray.BackgroundNormSimple(null, null);
            binary = gray.ThresholdToBinary(_options.Threshold);
        }
        if (_options.Deskew)
            binary = binary.FindSkewAndDeskew(2, out angle, out _);
        int s = _options.MinComponentSize;
        return s > 1 ? binary.SelectBySize(s, s, 8, L.L_SELECT_IF_EITHER, L.L_SELECT_IF_GTE, out _) : binary;
    }

    private static List<Region> Regions(Pix? mask)
    {
        var regions = new List<Region>();
        if (mask == null)
            return regions;
        Boxa boxes = mask.ConnComp(out _, 8);
        for (int i = 0; i < boxes.Count; i++)
        {
            Box b = boxes.GetBox(i, L.L_CLONE);
            regions.Add(new Region(b.X, b.Y, b.Width, b.Height));
        }
        return regions;
    }

    public void Dispose() => _slots.Dispose();
}

/// <summary>The upload is not a usable image; the endpoints answer 400.</summary>
public sealed class BadImageException(string message) : Exception(message);
