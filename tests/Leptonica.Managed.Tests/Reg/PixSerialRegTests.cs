using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>Port of <c>leptonica/prog/pixserial_reg.c</c>.</summary>
public class PixSerialRegTests
{
    private static readonly string[] Filename = {
        "feyn.tif",         /* 1 bpp */
        "dreyfus2.png",     /* 2 bpp cmapped */
        "dreyfus4.png",     /* 4 bpp cmapped */
        "weasel4.16c.png",  /* 4 bpp cmapped */
        "dreyfus8.png",     /* 8 bpp cmapped */
        "weasel8.240c.png", /* 8 bpp cmapped */
        "karen8.jpg",       /* 8 bpp, not cmapped */
        "test16.tif",       /* 8 bpp, not cmapped */
        "marge.jpg",        /* rgb */
        "test24.jpg"        /* rgb */
    };

    private static Pix ReadRegInput(string name)
    {
        if (name == "test16.tif")
            return TestPaths.ReadInput16(name);
        Pix pix = TestPaths.ReadInput(name);
            /* Input .jpg substitutes in testdata/inputs/*.jpg.png are read via
             * ReadStreamPng (which sets unused alpha byte = 255 for spp == 3),
             * whereas native pixReadStreamJpeg leaves the unused byte at 0, which
             * raw .spix serialization writes verbatim. */
        if (name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) &&
            pix.Depth == 32 && pix.Spp == 3)
            pix.SetComponentArbitrary(L.L_ALPHA_CHANNEL, 0);
        return pix;
    }

    [RegFact]
    public void PixSerial()
    {
        RegTest rp = new("pixserial");

        int nfiles = Filename.Length;

            /* Test basic serialization/deserialization */
        for (int i = 0; i < nfiles; i++) {
            Pix pixs = ReadRegInput(Filename[i]);
                /* Serialize to memory */
            byte[] data32 = pixs.SerializeToMemory();
                /* Just for fun, write and read back from file */
            Utils.BinaryWrite("/tmp/lept/regout/array", "w", data32);
            byte[] data32r = Utils.BinaryRead("/tmp/lept/regout/array");
                /* Deserialize */
            Pix pixd = Pix.DeserializeFromMemory(data32r);
            rp.ComparePix(pixs, pixd);  /* i */
        }

            /* Test read/write fileio interface */
        for (int i = 0; i < nfiles; i++) {
            Pix pixs = ReadRegInput(Filename[i]);
            pixs.GetDimensions(out int w, out int h, out _);
            Box box = Box.Create(0, 0, Math.Min(150, w), Math.Min(150, h));
            Pix pixt = pixs.ClipRectangle(box, out _)!;
            string buf = $"/tmp/lept/regout/pixs.{rp.Index + 1}.spix";
            Pix.Write(buf, pixt, L.IFF_SPIX);
            rp.CheckFile(buf);  /* nfiles + 2 * i */
            Pix pixt2 = Pix.Read(buf);
            rp.ComparePix(pixt, pixt2);  /* nfiles + 2 * i + 1 */
        }

            /* Test read header */
        for (int i = 0; i < nfiles; i++) {
            Pix pixs = ReadRegInput(Filename[i]);
            byte[] data = Pix.WriteMem(pixs, L.IFF_SPIX);
            Pix.SreadHeaderSpix(data, out int w2, out int h2, out int bps2, out _, out bool iscmap2);
            int expectedBps = pixs.Depth <= 16 ? pixs.Depth : 8;
            Assert.Equal((pixs.Width, pixs.Height, expectedBps, pixs.Colormap != null),
                         (w2, h2, bps2, iscmap2));
        }

        Assert.Equal(29, rp.Index);
        rp.Cleanup();
    }
}
