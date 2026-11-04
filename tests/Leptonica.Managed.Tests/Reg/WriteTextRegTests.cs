using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// writetext_reg.c: regression test for writing a block of text in one of 4 locations
/// relative to a pix.  This tests writing on 8 different types of images.
/// </summary>
public class WriteTextRegTests
{
    private static readonly string[] textstr = {
        "This is a simple test of text writing: 8 bpp",
        "This is a simple test of text writing: 32 bpp",
        "This is a simple test of text writing: 8 bpp cmapped",
        "This is a simple test of text writing: 4 bpp cmapped",
        "This is a simple test of text writing: 4 bpp",
        "This is a simple test of text writing: 2 bpp cmapped",
        "This is a simple test of text writing: 2 bpp",
        "This is a simple test of text writing: 1 bpp"};

    private static readonly string[] topstr = {
        "Text is added above each image",
        "Text is added over the top of each image",
        "Text is added over the bottom of each image",
        "Text is added below each image"};

    private static readonly int[] loc = {1, 5, 6, 2};

    private static readonly uint[] colors = {0x4090e000, 0x40e09000, 0x9040e000, 0x90e04000,
                                             0xe0409000, 0xe0904000};

    [RegFact]
    public void WriteText()
    {
        int i;
        L_Bmf bmf, bmftop;
        Pix pixs, pixt, pixd;
        Pix pix1, pix2, pix3, pix4, pix5, pix6, pix7, pix8;
        Pixa pixa;
        Sarray sa;
        var rp = new RegTest("writetext");

        bmf = L_Bmf.Create("./fonts", 6);
        bmftop = L_Bmf.Create("./fonts", 10);
        pixs = TestPaths.ReadInput("lucasta.047.jpg");
        pix1 = pixs.Scale(0.4f, 0.4f);          /* 8 bpp grayscale */
        pix2 = pix1.ConvertTo32();              /* 32 bpp rgb */
        pix3 = pix1.ThresholdOn8bpp(12, true);  /* 8 bpp cmapped */
        pix4 = pix1.ThresholdTo4bpp(10, true);  /* 4 bpp cmapped */
        pix5 = pix1.ThresholdTo4bpp(10, false); /* 4 bpp not cmapped */
        pix6 = pix1.ThresholdTo2bpp(3, true);   /* 2 bpp cmapped */
        pix7 = pix1.ThresholdTo2bpp(3, false);  /* 2 bpp not cmapped */
        pix8 = pix1.ThresholdToBinary(160);     /* 1 bpp */

        for (i = 0; i < 4; i++) {
            pixa = Pixa.Create(0);
            AddTextAndSave(pixa, pix1, bmf, textstr[0], loc[i], 800);
            AddTextAndSave(pixa, pix2, bmf, textstr[1], loc[i], 0xff000000);
            AddTextAndSave(pixa, pix3, bmf, textstr[2], loc[i], 0x00ff0000);
            AddTextAndSave(pixa, pix4, bmf, textstr[3], loc[i], 0x0000ff00);
            AddTextAndSave(pixa, pix5, bmf, textstr[4], loc[i], 800);
            AddTextAndSave(pixa, pix6, bmf, textstr[5], loc[i], 0xff000000);
            AddTextAndSave(pixa, pix7, bmf, textstr[6], loc[i], 800);
            AddTextAndSave(pixa, pix8, bmf, textstr[7], loc[i], 800);
            pixt = pixa.DisplayTiledInColumns(4, 1.0f, 30, 2);
            pixd = pixt.AddSingleTextblock(bmftop, topstr[i], 0xff00ff00, L.L_ADD_ABOVE, out _);
            rp.WritePixAndCheck(pixd, L.IFF_PNG);  /*  0 - 3 */
        }

            /* Write multiple lines in different colors, filling up
             * the colormap and requesting even more colors. */
        pixs = TestPaths.ReadInput("weasel4.11c.png");
        pix1 = pixs.ConvertTo8(false);
        pix2 = pixs.Scale(8.0f, 8.0f);
        pix3 = pix2.QuantFromCmap(pixs.Colormap!, 4, 5, L.L_EUCLIDEAN_DISTANCE);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 4 */
        bmf = L_Bmf.Create("fonts", 10);
        sa = Sarray.Create(6);
        for (i = 0; i < 6; i++) {
            string buf = $"This is textline {i}\n";
            sa.AddString(buf, L.L_COPY);
        }
        for (i = 0; i < 6; i++) {
            pix3.SetTextline(bmf, sa.GetString(i, L.L_NOCOPY),
                             colors[i], 50, 120 + 60 * i, out _, out _);
        }
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 5 */

        Assert.Equal(5, rp.Index);
        rp.Cleanup();
    }

    private static void AddTextAndSave(Pixa pixa, Pix pixs, L_Bmf bmf, string textstr,
                                       int location, uint val)
    {
        Pix pix1 = pixs.AddSingleTextblock(bmf, textstr, val, location, out int ovf);
        int n = pixa.Count;
        pixa.AddPix(pix1, L.L_INSERT);
        if (ovf != 0) Console.Error.WriteLine($"Overflow writing text in image {n + 1}");
    }
}
