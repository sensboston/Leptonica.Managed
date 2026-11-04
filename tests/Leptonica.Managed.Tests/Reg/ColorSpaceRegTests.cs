using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// colorspace_reg.c: conversions between HSV and both RGB and colormapped images;
/// global linear color mapping and extraction of color magnitude.
/// </summary>
public class ColorSpaceRegTests
{
    // NOTE: the C goldens were made without gnuplot, so the two checks of the plots
    // (C indices 10 and 11) did not count there; the last index is 9.  The plot data
    // (color magnitude fractions) is not checked and is not computed here.

    [RegFact]
    public void ColorSpace()
    {
        int i;
        uint pixel;
        Pix pixs, pix1, pix2, pix3;
        Pix pixr, pixg, pixb;  /* for color content extraction */
        Pixa pixat;
        PixColormap cmap;
        var rp = new RegTest("colorspace");

            /* Generate colors by sampling hue with max sat and value.
             * This image has been saved as 19-colors.png.  */
        pixat = Pixa.Create(19);
        for (i = 0; i < 19; i++) {
            Pix.ConvertHSVToRGB((240 * i / 18), 255, 255, out int rval, out int gval, out int bval);
            Pix.ComposeRGBPixel(rval, gval, bval, out pixel);
            pix1 = Pix.Create(50, 100, 32);
            pix1.SetAllArbitrary(pixel);
            pixat.AddPix(pix1, L.L_INSERT);
        }
        pix2 = pixat.DisplayTiledInRows(32, 1100, 1.0f, 0, 0, 0);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 0 */

            /* Colorspace conversion in rgb */
        pixs = TestPaths.ReadInput("wyom.jpg");
        pix3 = Pix.ConvertRGBToHSV(null, pixs);
        rp.WritePixAndCheck(pix3, L.IFF_JFIF_JPEG);  /* 1 */
        Pix.ConvertHSVToRGB(pix3, pix3);
        rp.WritePixAndCheck(pix3, L.IFF_JFIF_JPEG);  /* 2 */

            /* Colorspace conversion on a colormap */
        pix3 = pixs.OctreeQuantNumColors(25, 0);
        rp.WritePixAndCheck(pix3, L.IFF_JFIF_JPEG);  /* 3 */
        cmap = pix3.Colormap!;
        cmap.ConvertRGBToHSV();
        rp.WritePixAndCheck(pix3, L.IFF_JFIF_JPEG);  /* 4 */
        cmap.ConvertHSVToRGB();
        rp.WritePixAndCheck(pix3, L.IFF_JFIF_JPEG);  /* 5 */

            /* Color content extraction */
        pixs.ColorContent(0, 0, 0, 0, out pixr, out pixg, out pixb);
        rp.WritePixAndCheck(pixr, L.IFF_JFIF_JPEG);  /* 6 */
        rp.WritePixAndCheck(pixg, L.IFF_JFIF_JPEG);  /* 7 */
        rp.WritePixAndCheck(pixb, L.IFF_JFIF_JPEG);  /* 8 */

            /* Color content measurement.  This tests the global
             * mapping of (r,g,b) --> (white), for 20 different
             * values of (r,g,b). */
        pixat = Pixa.Create(20);
        for (i = 0; i < 20; i++) {
            int rwhite = 100 + 5 * i;
            int gwhite = 200 - 5 * i;
            int bwhite = 150;
            Pix pix0 = Pix.GlobalNormRGB(null, pixs, rwhite, gwhite, bwhite, 255);
            pixat.AddPix(pix0, L.L_INSERT);
        }
        pix1 = pixat.DisplayTiledAndScaled(32, 250, 4, 0, 10, 2);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 9 */

        Assert.Equal(9, rp.Index);
        rp.Cleanup();
    }
}
