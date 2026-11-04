using System;
using System.IO;

namespace Leptonica.Managed.Tests.Png;

internal static class TestPaths
{
    public static string PngSuiteDir { get; } = FindPngSuite();

    private static string FindPngSuite()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            string candidate = Path.Combine(dir, "testdata", "pngsuite");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("testdata/pngsuite not found");
    }
}
