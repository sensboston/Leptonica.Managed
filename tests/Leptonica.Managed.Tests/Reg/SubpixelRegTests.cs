using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// subpixel_reg.c: regression test for subpixel scaling.
/// </summary>
[Collection("ConvolveSampling")]
public class SubpixelRegTests
{
    private static readonly string[] TextStr = {
        "Downscaled with sharpening",
        "Subpixel scaling; horiz R-G-B",
        "Subpixel scaling; horiz B-G-R",
        "Subpixel scaling; vert R-G-B",
        "Subpixel scaling; vert B-G-R"
    };

    [RegFact]
    public void Subpixel()
    {
        var rp = new RegTest("subpixel");

        try {
                /* ----------------- Test on 8 bpp grayscale ---------------------*/
            Pixa pixa = Pixa.Create(5);
            L_Bmf bmf = L_Bmf.Create("./fonts", 6);
            L_Bmf bmftop = L_Bmf.Create("./fonts", 10);
            Pix pixs = TestPaths.ReadInput("lucasta.047.jpg");
            Pix pixg = pixs.Scale(0.4f, 0.4f);  /* 8 bpp grayscale */
            Pix pix1 = pixg.ConvertTo32();  /* 32 bpp rgb */
            AddTextAndSave(pixa, pix1, bmf, TextStr[0], L.L_ADD_BELOW, 0xff000000);
            Pix pix2 = pixs.ConvertGrayToSubpixelRGB(0.4f, 0.4f, L.L_SUBPIXEL_ORDER_RGB);
            AddTextAndSave(pixa, pix2, bmf, TextStr[1], L.L_ADD_BELOW, 0x00ff0000);
            Pix pix3 = pixs.ConvertGrayToSubpixelRGB(0.4f, 0.4f, L.L_SUBPIXEL_ORDER_BGR);
            AddTextAndSave(pixa, pix3, bmf, TextStr[2], L.L_ADD_BELOW, 0x0000ff00);
            Pix pix4 = pixs.ConvertGrayToSubpixelRGB(0.4f, 0.4f, L.L_SUBPIXEL_ORDER_VRGB);
            AddTextAndSave(pixa, pix4, bmf, TextStr[3], L.L_ADD_BELOW, 0x00ff0000);
            Pix pix5 = pixs.ConvertGrayToSubpixelRGB(0.4f, 0.4f, L.L_SUBPIXEL_ORDER_VBGR);
            AddTextAndSave(pixa, pix5, bmf, TextStr[4], L.L_ADD_BELOW, 0x0000ff00);

            Pix pixt = pixa.DisplayTiledInColumns(5, 1.0f, 30, 2);
            Pix pixd = pixt.AddSingleTextblock(bmftop,
                                               "Regression test for subpixel scaling: gray",
                                               0xff00ff00, L.L_ADD_ABOVE, out _);
            rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 0 */

                /* ----------------- Test on 32 bpp rgb ---------------------*/
            pixa = Pixa.Create(5);
            pixs = TestPaths.ReadInput("fish24.jpg");
            pix1 = pixs.Scale(0.4f, 0.4f);
            AddTextAndSave(pixa, pix1, bmf, TextStr[0], L.L_ADD_BELOW, 0xff000000);
            pix2 = pixs.ConvertToSubpixelRGB(0.4f, 0.4f, L.L_SUBPIXEL_ORDER_RGB)!;
            AddTextAndSave(pixa, pix2, bmf, TextStr[1], L.L_ADD_BELOW, 0x00ff0000);
            pix3 = pixs.ConvertToSubpixelRGB(0.4f, 0.35f, L.L_SUBPIXEL_ORDER_BGR)!;
            AddTextAndSave(pixa, pix3, bmf, TextStr[2], L.L_ADD_BELOW, 0x0000ff00);
            pix4 = pixs.ConvertToSubpixelRGB(0.4f, 0.45f, L.L_SUBPIXEL_ORDER_VRGB)!;
            AddTextAndSave(pixa, pix4, bmf, TextStr[3], L.L_ADD_BELOW, 0x00ff0000);
            pix5 = pixs.ConvertToSubpixelRGB(0.4f, 0.4f, L.L_SUBPIXEL_ORDER_VBGR)!;
            AddTextAndSave(pixa, pix5, bmf, TextStr[4], L.L_ADD_BELOW, 0x0000ff00);

            pixt = pixa.DisplayTiledInColumns(5, 1.0f, 30, 2);
            pixd = pixt.AddSingleTextblock(bmftop,
                                           "Regression test for subpixel scaling: color",
                                           0xff00ff00, L.L_ADD_ABOVE, out _);
            rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 1 */

                /* --------------- Test on images that are initially 1 bpp ------------*/
                /*   For these, it is better to apply a lowpass filter before scaling  */
                /* Normal scaling of 8 bpp grayscale */
            float scalefact = 800.0f / 2320.0f;
            pixs = TestPaths.ReadInput("patent.png");  /* sharp, 300 ppi, 1 bpp image */
            pix1 = pixs.ConvertTo8(false);  /* use 8 bpp input */
            pix2 = pix1.Scale(scalefact, scalefact);
            rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 2 */

                /* Subpixel scaling; bad because there is very little aliasing. */
            pix3 = pix1.ConvertToSubpixelRGB(scalefact, scalefact, L.L_SUBPIXEL_ORDER_RGB)!;
            rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 3 */

                /* Get same (bad) result doing subpixel rendering on RGB input */
            pix4 = pixs.ConvertTo32();
            pix5 = pix4.ConvertToSubpixelRGB(scalefact, scalefact, L.L_SUBPIXEL_ORDER_RGB)!;
            rp.ComparePix(pix3, pix5);  /* 4 */
            rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 5 */

                /* Now apply a small lowpass filter before scaling. */
            Pix.SetConvolveSampling(1, 1);
            L_Kernel.MakeGaussianKernelSep(2, 2, 1.0f, 1.0f, out L_Kernel kelx, out L_Kernel kely);
            Pix pix6 = pix1.ConvolveSep(kelx, kely, 8, 1);  /* normalized */
            rp.WritePixAndCheck(pix6, L.IFF_PNG);  /* 6 */

                /* Get same lowpass result with non-separated convolution */
            L_Kernel kel = L_Kernel.MakeGaussianKernel(2, 2, 1.0f, 1.0f);
            Pix pix7 = pix1.Convolve(kel, 8, 1);  /* normalized */
            rp.ComparePix(pix6, pix7);  /* 7 */

                /* Now do the subpixel scaling on this slightly blurred image */
            Pix pix8 = pix6.ConvertToSubpixelRGB(scalefact, scalefact, L.L_SUBPIXEL_ORDER_RGB)!;
            rp.WritePixAndCheck(pix8, L.IFF_PNG);  /* 8 */
        } finally {
            Pix.SetConvolveSampling(1, 1);
        }

        Assert.Equal(8, rp.Index);
        rp.Cleanup();
    }

    private static void AddTextAndSave(Pixa pixa, Pix pixs, L_Bmf bmf, string textstr,
                                       int location, uint val)
    {
        Pix pixt = pixs.AddSingleTextblock(bmf, textstr, val, location, out _);
        pixa.AddPix(pixt, L.L_INSERT);
    }
}
