using System;
using System.IO;
using Leptonica;
using Leptonica.Managed.Tests.Reg;
using Xunit;

namespace Leptonica.Managed.Tests.IOTests;

public class SpixIoTests
{
    [RegFact]
    public void AllTenInputsAndNativeGoldenSpixFilesMatchBitIdentically()
    {
        (string FileName, int GoldenIdx)[] cases = {
            ("feyn.tif", 10),
            ("dreyfus2.png", 12),
            ("dreyfus4.png", 14),
            ("weasel4.16c.png", 16),
            ("dreyfus8.png", 18),
            ("weasel8.240c.png", 20),
            ("karen8.jpg", 22),
            ("test16.tif", 24),
            ("marge.jpg", 26),
            ("test24.jpg", 28),
        };

        foreach ((string name, int goldenIndex) in cases) {
            string path = Path.Combine(TestPaths.GoldenDir!, $"pixserial_golden.{goldenIndex:D2}.spix");
            byte[] golden = File.ReadAllBytes(path);

                /* 1. Deserialize golden and re-serialize: bit-identical */
            Pix fromGolden = Pix.ReadMemSpix(golden);
            Assert.Equal(golden, Pix.WriteMemSpix(fromGolden));

            using (FileStream fp = File.OpenRead(path)) {
                Pix fromStream = Pix.ReadStreamSpix(fp);
                Assert.True(Pix.Equal(fromGolden, fromStream));
            }

                /* 2. Header reader matches */
            Pix.ReadHeaderSpix(path, out int hw, out int hh, out int bps, out int spp, out bool iscmap);
            int expectedBps = fromGolden.Depth <= 16 ? fromGolden.Depth : 8;
            int expectedSpp = fromGolden.Depth <= 16 ? 1 : fromGolden.Depth / 8;
            Assert.Equal((fromGolden.Width, fromGolden.Height, expectedBps, expectedSpp, fromGolden.Colormap != null),
                         (hw, hh, bps, spp, iscmap));

                /* 3. Clip input image and compare against native golden */
            Pix pixs = name == "test16.tif" ? TestPaths.ReadInput16(name) : TestPaths.ReadInput(name);
            pixs.GetDimensions(out int w, out int h, out _);
            Box box = Box.Create(0, 0, Math.Min(150, w), Math.Min(150, h));
            Pix pixt = pixs.ClipRectangle(box, out _)!;
            Assert.True(Pix.Equal(fromGolden, pixt));
            if (pixt.Depth == 32 && pixt.Spp == 3 && Path.GetExtension(name) == ".jpg")
                pixt.SetComponentArbitrary(L.L_ALPHA_CHANNEL, 0); /* jpegio leaves alpha = 0; png conversion sets 255 */
            byte[] actual = Pix.WriteMem(pixt, L.IFF_SPIX);
            Assert.Equal(golden, actual);
        }
    }

    [Fact]
    public void WriteStreamSpixAndSerializeAllDepthsRoundTrip()
    {
        foreach (int d in new[] { 1, 2, 4, 8, 16, 32 }) {
            Pix src = Pix.Create(19, 7, d);
            for (int y = 0; y < 7; y++)
                for (int x = 0; x < 19; x++)
                    src.SetPixel(x, y, d == 32 ? 0x123456ffu : (uint)((x + y) & ((1 << Math.Min(d, 16)) - 1)));

            using MemoryStream ms = new();
            Pix.WriteStreamSpix(ms, src);
            ms.Position = 0;
            Pix dst = Pix.ReadStreamSpix(ms);
            Assert.True(Pix.Equal(src, dst));
        }
    }

    [Fact]
    public void InvalidSpixBuffersAndStreamsThrow()
    {
        Assert.Throws<ArgumentException>(() => Pix.ReadStreamSpix(null!));
        Assert.Throws<ArgumentException>(() => Pix.WriteMemSpix(null!));
        Assert.Throws<LeptonicaException>(() => Pix.ReadMemSpix(new byte[16]));
        Assert.Throws<LeptonicaException>(() => Pix.SreadHeaderSpix(new byte[28], out _, out _, out _, out _, out _));

        Pix src = Pix.Create(8, 8, 8);
        byte[] valid = src.SerializeToMemory();
        Assert.Throws<LeptonicaException>(() => Pix.DeserializeFromMemory(valid.AsSpan(0, valid.Length - 4)));
    }
}
