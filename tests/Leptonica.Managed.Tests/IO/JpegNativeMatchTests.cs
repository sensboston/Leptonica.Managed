using System.Collections.Generic;
using System.IO;
using Leptonica;
using Leptonica.Managed.Tests.Reg;
using Xunit;

namespace Leptonica.Managed.Tests.IOTests;

/// <summary>
/// The JPEG decoder must give the same pixels as the native Leptonica, which is
/// built with libjpeg-turbo: chroma is upsampled with the fancy (triangle) filter
/// after an 8x8 IDCT, and YCbCr is converted with the libjpeg 6b constants.
/// testdata/inputs holds every prog/*.jpg decoded by the native library and
/// written as png.
/// </summary>
public class JpegNativeMatchTests
{
    [RegFact]
    public void Decode_MatchesNativeForAllProgJpegs()
    {
        if (TestPaths.InputDir == null)
            return;
        var differ = new List<string>();
        int compared = 0;
        foreach (string jpg in Directory.GetFiles(TestPaths.ProgDir, "*.jpg")) {
            string png = Path.Combine(TestPaths.InputDir, Path.GetFileName(jpg) + ".png");
            if (!File.Exists(png))
                continue;
            Pix ours = Pix.Read(jpg);
            Pix native;
            lock (TestPaths.PngReadLock)
                native = Pix.Read(png);
            compared++;
            if (!Pix.Equal(ours, native))
                differ.Add(Path.GetFileName(jpg));
        }
        Assert.True(compared > 50, $"only {compared} jpeg files compared");
        Assert.Empty(differ);
    }
}
