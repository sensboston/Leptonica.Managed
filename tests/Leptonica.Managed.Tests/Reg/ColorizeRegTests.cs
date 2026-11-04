using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// colorize_reg.c: detection of red highlight color and colorized highlighting.
/// </summary>
public class ColorizeRegTests
{
    [RegFact]
    public void Colorize()
    {
        var rp = new RegTest("colorize");

        Pixa pixa = Pixa.Create(0);
        Pix pixs = TestPaths.ReadInput("breviar.38.150.jpg");
        pixa.AddPix(pixs, L.L_CLONE);
        rp.WritePixAndCheck(pixs, L.IFF_JFIF_JPEG);  /* 0 */

            /* Extract the blue component */
        Pix pix1 = pixs.GetRGBComponent(L.COLOR_BLUE);
        pixa.AddPix(pix1, L.L_CLONE);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 1 */

            /* Do a background normalization */
        Pix pix2 = pix1.BackgroundNormSimple(null, null);
        pixa.AddPix(pix2, L.L_COPY);
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 2 */

            /* Linear transform on the gray pixels, quantize to 7 gray levels */
        Pix.GammaTRC(pix2, pix2, 1.0f, 50, 160);
        Pix pix3 = pix2.ThresholdOn8bpp(7, true);
        pixa.AddPix(pix3, L.L_CLONE);
        rp.WritePixAndCheck(pix3, L.IFF_JFIF_JPEG);  /* 3 */

            /* Identify the regions of red text */
        FPix fpix = pixs.ComponentFunction(1.0f, 0.0f, -1.0f, 0.0f, 0.0f, 1.0f);
        Pix pix4 = fpix.ThresholdToPix(2.0f);
        Pix.Invert(pix4, pix4);  /* red plus some dark text */
        pixa.AddPix(pix4, L.L_CLONE);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 4 */

            /* Make a mask consisting of all the red and background pixels */
        Pix pix5 = pixs.GetRGBComponent(L.COLOR_RED);
        Pix pix6 = pix5.ThresholdToBinary(128);
        Pix.Invert(pix6, pix6);  /* red plus background (white) */

            /* Intersect the two masks to get a seed */
        Pix pix7 = Pix.And(null, pix4, pix6);  /* red only (seed) */
        pixa.AddPix(pix7, L.L_COPY);
        rp.WritePixAndCheck(pix7, L.IFF_PNG);  /* 5 */

            /* Make the clipping mask */
        Pix pix8 = pix2.ThresholdToBinary(230);  /* mask */
        pixa.AddPix(pix8, L.L_CLONE);
        rp.WritePixAndCheck(pix8, L.IFF_PNG);  /* 6 */

            /* Fill into the mask from the seed */
        Pix.SeedfillBinary(pix7, pix7, pix8, 8);  /* filled: red plus touching */
        rp.WritePixAndCheck(pix7, L.IFF_PNG);  /* 7 */

            /* Small closing on regions to be colored */
        Pix pix9 = pix7.MorphSequence("c5.1", 0);
        pixa.AddPix(pix9, L.L_CLONE);
        rp.WritePixAndCheck(pix9, L.IFF_PNG);  /* 8 */

            /* Sanity check on amount to be colored */
        float fgfract = pix9.ForegroundFraction();
        if (fgfract >= 0.10f) {
            pix9.ClearAll();
            pix9.SetPixel(0, 0, 1);
        }

            /* Representative highlight color */
        pixs.GetAverageMaskedRGB(pix7, 0, 0, 1, L.L_MEAN_ABSVAL,
                                 out float rval, out float gval, out float bval);
        float fract = 255.0f / rval;
        int irval = Utils.RoundFToI(fract * rval);
        int igval = Utils.RoundFToI((float)(fract * gval / 2.0));
        int ibval = Utils.RoundFToI((float)(fract * bval / 2.0));

            /* Test mask-based colorization on gray and cmapped gray */
        Pix pix10 = pix2.ColorGrayMasked(pix9, L.L_PAINT_DARK, 225, irval, igval, ibval);
        pixa.AddPix(pix10, L.L_CLONE);
        rp.WritePixAndCheck(pix10, L.IFF_PNG);  /* 9 */
        pixa.AddPix(pixs, L.L_CLONE);

        Pix pix11 = pix3.ColorGrayMasked(pix9, L.L_PAINT_DARK, 225, irval, igval, ibval);
        pixa.AddPix(pix11, L.L_CLONE);
        rp.WritePixAndCheck(pix11, L.IFF_PNG);  /* 10 */

            /* Get the bounding boxes of the mask components to be colored */
        Boxa boxa = pix9.ConnCompBB(8);

            /* Test region colorization on gray and cmapped gray */
        Pix pix12 = pix2.ColorGrayRegions(boxa, L.L_PAINT_DARK, 220, 0, 255, 0);
        pixa.AddPix(pix12, L.L_CLONE);
        rp.WritePixAndCheck(pix12, L.IFF_PNG);  /* 11 */

        Box box = Box.Create(200, 200, 250, 350);
        Pix pix13 = Pix.Copy(null, pix2);
        pix13.ColorGray(box, L.L_PAINT_DARK, 220, 0, 0, 255);
        pixa.AddPix(pix13, L.L_CLONE);
        rp.WritePixAndCheck(pix13, L.IFF_PNG);  /* 12 */

        Pix pix14 = pix2.ThresholdTo4bpp(6, true);
        Pix pix15 = pix14.ColorGrayRegions(boxa, L.L_PAINT_DARK, 220, 0, 0, 255);
        pixa.AddPix(pix15, L.L_CLONE);
        rp.WritePixAndCheck(pix15, L.IFF_PNG);  /* 13 */

        pix14.ColorGrayCmap(box, L.L_PAINT_DARK, 0, 255, 255);
        pixa.AddPix(pix14, L.L_CLONE);
        rp.WritePixAndCheck(pix14, L.IFF_PNG);  /* 14 */

            /* Test the color detector */
        pixa = Pixa.Create(7);
        L_Bmf bmf = L_Bmf.Create(null, 4);
        pixa.AddPix(TestForRedColor(rp, "brev.06.75.jpg", 1, bmf), L.L_INSERT);  /* 15 */
        pixa.AddPix(TestForRedColor(rp, "brev.10.75.jpg", 0, bmf), L.L_INSERT);  /* 16 */
        pixa.AddPix(TestForRedColor(rp, "brev.14.75.jpg", 1, bmf), L.L_INSERT);  /* 17 */
        pixa.AddPix(TestForRedColor(rp, "brev.20.75.jpg", 1, bmf), L.L_INSERT);  /* 18 */
        pixa.AddPix(TestForRedColor(rp, "brev.36.75.jpg", 0, bmf), L.L_INSERT);  /* 19 */
        pixa.AddPix(TestForRedColor(rp, "brev.53.75.jpg", 1, bmf), L.L_INSERT);  /* 20 */
        pixa.AddPix(TestForRedColor(rp, "brev.56.75.jpg", 1, bmf), L.L_INSERT);  /* 21 */

        Assert.Equal(21, rp.Index);
        rp.Cleanup();
    }

    private static Pix TestForRedColor(RegTest rp, string fname, float goldRed, L_Bmf bmf)
    {
        Pix pix1 = TestPaths.ReadInput(fname);
        pix1.HasHighlightRed(1, 0.0001f, 2.5f, out bool hasred, out float ratio, out _);
        rp.CompareValues(goldRed, hasred ? 1 : 0, 0.0f);
        string text = hasred
            ? CFormat.Sprintf("Has red: ratio = %6.1f", ratio)
            : CFormat.Sprintf("Does not have red: ratio = %6.1f", ratio);
        return pix1.AddSingleTextblock(bmf, text, 0x0000ff00u, L.L_ADD_BELOW, out _);
    }
}
