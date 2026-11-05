// An interactive tour of Leptonica.Managed: pick an operation on the left, change its
// parameters on the right, and see the result and the time it took.
//
//   dotnet run --project samples/Explorer
//
// Runs on Windows, Linux and macOS (Avalonia).

using Avalonia;

namespace Explorer;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Also used by the XAML previewer of the IDE.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
                  .UsePlatformDetect()
                  .WithInterFont()
                  .LogToTrace();
}
