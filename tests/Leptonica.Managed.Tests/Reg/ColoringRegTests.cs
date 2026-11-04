using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// coloring_reg.c: tests simple coloring functions.
/// </summary>
public class ColoringRegTests
{
    private static readonly int[] RVal = { 255, 255, 255, 235 };
    private static readonly int[] GVal = { 255, 245, 235, 245 };
    private static readonly int[] BVal = { 235, 235, 245, 255 };

    [RegFact]
    public void Coloring()
    {
        var rp = new RegTest("coloring");
        L_Bmf bmf = L_Bmf.Create("fonts", 8);

            /* Get the input image (100 ppi resolution) */
        Pix pix0 = TestPaths.ReadInput("harmoniam100-11.png");
        PixColormap cmap = pix0.Colormap!;
        Pixa pixa = Pixa.Create(0);

            /* Do cmapped coloring on the white pixels only */
        cmap.GetIndex(255, 255, 255, out int index);
        for (int i = 0; i < 4; i++) {
            cmap.ResetColor(index, RVal[i], GVal[i], BVal[i]);
            string buf = $"(rval, bval, gval) = ({RVal[i]}, {GVal[i]}, {BVal[i]})";
            Pix pix1 = pix0.AddSingleTextblock(bmf, buf, 0xff000000u, L.L_ADD_AT_BOT, out _);
            pixa.AddPix(pix1, L.L_INSERT);
        }

            /* Do cmapped background coloring on all the pixels */
        for (int i = 0; i < 4; i++) {
            uint scolor = 0xffffff00u;
            Pix.ComposeRGBPixel(RVal[i], GVal[i], BVal[i], out uint dcolor);
            Pix pix1 = Pix.ShiftByComponent(null, pix0, scolor, dcolor);
            string buf = $"(rval, bval, gval) = ({RVal[i]}, {GVal[i]}, {BVal[i]})";
            Pix pix2 = pix1.AddSingleTextblock(bmf, buf, 0xff000000u, L.L_ADD_AT_BOT, out _);
            pixa.AddPix(pix2, L.L_INSERT);
        }

            /* Do background coloring on rgb */
        Pix pixRgb = pix0.ConvertTo32();
        for (int i = 0; i < 4; i++) {
            uint scolor = 0xffffff00u;
            Pix.ComposeRGBPixel(RVal[i], GVal[i], BVal[i], out uint dcolor);
            Pix pix2 = Pix.ShiftByComponent(null, pixRgb, scolor, dcolor);
            string buf = $"(rval, bval, gval) = ({RVal[i]}, {GVal[i]}, {BVal[i]})";
            Pix pix3 = pix2.AddSingleTextblock(bmf, buf, 0xff000000u, L.L_ADD_AT_BOT, out _);
            pixa.AddPix(pix3, L.L_INSERT);
        }

            /* Compare cmapped & rgb foreground coloring */
        {
            uint scolor = 0x0u;
            Pix.ComposeRGBPixel(200, 30, 150, out uint dcolor);
            Pix pix1 = Pix.ShiftByComponent(null, pix0, scolor, dcolor);
            string buf = "(rval, bval, gval) = (200, 100, 50)";
            Pix pix2 = pix1.AddSingleTextblock(bmf, buf, 0xff000000u, L.L_ADD_AT_BOT, out _);
            pixa.AddPix(pix2, L.L_INSERT);
            Pix pix3 = pix0.ConvertTo32();
            Pix pix4 = Pix.ShiftByComponent(null, pix3, scolor, dcolor);
            Pix pix5 = pix4.AddSingleTextblock(bmf, buf, 0xff000000u, L.L_ADD_AT_BOT, out _);
            pixa.AddPix(pix5, L.L_INSERT);
            rp.ComparePix(pix1, pix4);  /* 0 */
            rp.ComparePix(pix2, pix5);  /* 1 */
        }

            /* Log all the results */
        int n = pixa.Count;
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixa.GetPix(i, L.L_CLONE);
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 2 - 15 */
        }

        Assert.Equal(15, rp.Index);
        rp.Cleanup();
    }
}
