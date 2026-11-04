using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// lineremoval_reg.c: deskew and removal of the lines of a scanned form,
/// using gray morphology.
/// </summary>
public class LineRemovalRegTests
{
    [RegFact]
    public void LineRemoval()
    {
        float angle, conf, deg2rad;
        Pix pixs, pix1, pix2, pix3, pix4, pix5, pix6, pix7, pix8, pix9;
        Pixa pixa;
        var rp = new RegTest("lineremoval");

        deg2rad = (float)(3.14159 / 180.0);
        pixs = TestPaths.ReadInput("dave-orig.png");
        pixa = Pixa.Create(0);

            /* Threshold to binary, extracting much of the lines */
        pix1 = pixs.ThresholdToBinary(170);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0 */
        pixa.AddPix(pix1, L.L_INSERT);

            /* Find the skew angle and deskew using an interpolated
             * rotator for anti-aliasing (to avoid jaggies) */
        pix1.FindSkew(out angle, out conf);
        pix2 = pixs.RotateAMGray(deg2rad * angle, 255);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 1 */
        pixa.AddPix(pix2, L.L_INSERT);

            /* Extract the lines to be removed */
        pix3 = pix2.CloseGray(51, 1);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 2 */
        pixa.AddPix(pix3, L.L_INSERT);

            /* Solidify the lines to be removed */
        pix4 = pix3.ErodeGray(1, 5);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 3 */
        pixa.AddPix(pix4, L.L_INSERT);

            /* Clean the background of those lines */
        pix5 = Pix.ThresholdToValue(null, pix4, 210, 255);
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 4 */
        pixa.AddPix(pix5, L.L_INSERT);

        pix6 = Pix.ThresholdToValue(null, pix5, 200, 0);
        rp.WritePixAndCheck(pix6, L.IFF_PNG);  /* 5 */
        pixa.AddPix(pix6, L.L_COPY);

            /* Get paint-through mask for changed pixels */
        pix7 = pix6.ThresholdToBinary(210);
        rp.WritePixAndCheck(pix7, L.IFF_PNG);  /* 6 */
        pixa.AddPix(pix7, L.L_INSERT);

            /* Add the inverted, cleaned lines to orig.  Because
             * the background was cleaned, the inversion is 0,
             * so when you add, it doesn't lighten those pixels.
             * It only lightens (to white) the pixels in the lines! */
        Pix.Invert(pix6, pix6);
        pix8 = Pix.AddGray(null, pix2, pix6);
        rp.WritePixAndCheck(pix8, L.IFF_PNG);  /* 7 */
        pixa.AddPix(pix8, L.L_COPY);

        pix9 = pix8.OpenGray(1, 9);
        rp.WritePixAndCheck(pix9, L.IFF_PNG);  /* 8 */
        pixa.AddPix(pix9, L.L_INSERT);
        pix8.CombineMasked(pix9, pix7);
        rp.WritePixAndCheck(pix8, L.IFF_PNG);  /* 9 */
        pixa.AddPix(pix8, L.L_INSERT);

        Assert.Equal(9, rp.Index);
        rp.Cleanup();
    }
}
