// A web service that prepares scanned pages for OCR and analyzes their layout, with
// Leptonica.Managed behind a service registered in the dependency injection container.
//
//   dotnet run --project samples/WebApi
//
// then send requests from WebApi.http (Visual Studio, or VS Code with the REST Client extension)
// or with curl:
//
//   curl --data-binary @samples/images/old-page.png -o clean.png http://localhost:5080/preprocess
//   curl --data-binary @samples/images/newspaper.png http://localhost:5080/layout
//
// The pipeline settings are in appsettings.json, section "Preprocess".

using Leptonica;
using WebApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PreprocessOptions>(builder.Configuration.GetSection("Preprocess"));
builder.Services.AddSingleton<IPagePreprocessor, LeptonicaPagePreprocessor>();
builder.Services.AddHostedService<GpuWarmupService>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// What the service runs on.
app.MapGet("/info", () => new
{
    leptonica = Utils.GetLeptonicaVersion(),
    package = typeof(Pix).Assembly.GetName().Version?.ToString(),
    runtime = Environment.Version.ToString(),
    gpu = LeptGpu.IsHardwareGpuAvailable ? LeptGpu.ActiveDeviceName : null,
    gpuMode = LeptGpu.Mode.ToString(),
    cpuThreads = Environment.ProcessorCount,
});

// The body is an image (PNG, JPEG, TIFF, ...); the answer is the cleaned 1 bpp page as PNG.
app.MapPost("/preprocess", async (HttpRequest request, IPagePreprocessor pages, CancellationToken ct) =>
{
    byte[] image = await ReadBodyAsync(request, ct);
    return await Handle(() => pages.PreprocessAsync(image, ct), png => Results.File(png, "image/png"));
});

// The body is an image; the answer is JSON with the boxes of text blocks, text lines and pictures.
app.MapPost("/layout", async (HttpRequest request, IPagePreprocessor pages, CancellationToken ct) =>
{
    byte[] image = await ReadBodyAsync(request, ct);
    return await Handle(() => pages.AnalyzeLayoutAsync(image, ct), layout => Results.Ok(layout));
});

app.Run();

static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken ct)
{
    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer, ct);
    return buffer.ToArray();
}

static async Task<IResult> Handle<T>(Func<Task<T>> work, Func<T, IResult> ok)
{
    try
    {
        return ok(await work());
    }
    catch (BadImageException e)
    {
        return Results.Problem(e.Message, statusCode: StatusCodes.Status400BadRequest);
    }
}
