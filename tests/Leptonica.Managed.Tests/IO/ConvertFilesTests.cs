using System;
using System.IO;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.IOTests;

public class ConvertFilesTests
{
    [Fact]
    public void ConvertFilesTo1bppConvertsGrayColorAndBinaryPngsWithUpscaling()
    {
        string root = Path.Combine(Path.GetTempPath(), "lept_conv1bpp_" + Guid.NewGuid().ToString("N"));
        string dirIn = Path.Combine(root, "in");
        string dirOut1 = Path.Combine(root, "out1");
        string dirOut2 = Path.Combine(root, "out2");
        Directory.CreateDirectory(dirIn);
        Directory.CreateDirectory(dirOut1);
        Directory.CreateDirectory(dirOut2);

        try {
            Pix p8 = Pix.Create(16, 12, 8);
            p8.SetAllArbitrary(220);
            p8.Rasterop(4, 3, 6, 6, L.PIX_CLR, null, 0, 0);
            Pix.WritePng(Path.Combine(dirIn, "a_gray.png"), p8, 0.0f);

            Pix p32 = Pix.Create(10, 8, 32);
            p32.SetAllArbitrary(0xf0f0f000u);
            p32.SetRGBPixel(2, 2, 10, 20, 30);
            Pix.WritePng(Path.Combine(dirIn, "b_rgb.png"), p32, 0.0f);

            Pix.ConvertFilesTo1bpp(dirIn, null, 1, 0, 0, 0, dirOut1, L.IFF_PNG);
            Pix outA = Pix.Read(Path.Combine(dirOut1, "a_gray.png"));
            Pix outB = Pix.Read(Path.Combine(dirOut1, "b_rgb.png"));
            Assert.Equal(1, outA.Depth);
            Assert.Equal(16, outA.Width);
            Assert.Equal(12, outA.Height);
            Assert.Equal(1, outB.Depth);

            Pix.ConvertFilesTo1bpp(dirIn, "a_", 2, 128, 0, 1, dirOut2, L.IFF_PNG);
            Pix outA2 = Pix.Read(Path.Combine(dirOut2, "a_gray.png"));
            Assert.Equal(1, outA2.Depth);
            Assert.Equal(32, outA2.Width);
            Assert.Equal(24, outA2.Height);
            Assert.False(File.Exists(Path.Combine(dirOut2, "b_rgb.png")));

            Assert.Throws<ArgumentException>(() =>
                Pix.ConvertFilesTo1bpp(dirIn, null, 3, 0, 0, 0, dirOut1, L.IFF_PNG));
        } finally {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
