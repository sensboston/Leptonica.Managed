using System.IO;
using System.Linq;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// Reads every png golden file made by the C library and checks that a
/// write/read cycle through the managed png code preserves the pixels.
/// </summary>
public class GoldenPngTests
{
    [RegFact]
    public void AllGoldenPngsReadAndRoundTrip()
    {
        string[] files = Directory.GetFiles(TestPaths.GoldenDir!, "*.png");
        var failures = new System.Collections.Generic.List<string>();
        foreach (string f in files) {
            try {
                Pix pix1 = Pix.Read(f);
                Pix pix2 = Pix.ReadMemPng(Pix.WriteMemPng(pix1, 0.0f));
                if (!Pix.Equal(pix1, pix2))
                    failures.Add($"{Path.GetFileName(f)}: round trip differs");
            } catch (System.Exception e) {
                failures.Add($"{Path.GetFileName(f)}: {e.GetType().Name}: {e.Message}");
            }
        }
        Assert.True(failures.Count == 0,
            $"{failures.Count} of {files.Length} failed:\n" + string.Join("\n", failures.Take(40)));
    }
}
