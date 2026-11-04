using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// locminmax_reg.c: local minima and maxima of a synthetic and a smoothed
/// natural gray image, painted over the image.
/// </summary>
public class LocMinMaxRegTests
{
    [RegFact]
    public void LocMinMax()
    {
        int i, j;
        float f;
        var rp = new RegTest("locminmax");

        Pix pix1 = Pix.Create(500, 500, 8);
        for (i = 0; i < 500; i++) {
            for (j = 0; j < 500; j++) {
                f = (float)(128.0 + 26.3 * Math.Sin(0.0438 * (float)i));
                f = (float)(f + 33.4 * Math.Cos(0.0712 * (float)i));
                f = (float)(f + 18.6 * Math.Sin(0.0561 * (float)j));
                f = (float)(f + 23.6 * Math.Cos(0.0327 * (float)j));
                pix1.SetPixel(j, i, (uint)(int)f);
            }
        }
        Pix pix2 = TestPaths.ReadInput("karen8.jpg");
        Pix pix3 = pix2.Blockconv(10, 10);
        DoLocMinmax(rp, pix1, 0, 0);  /* 0 - 2 */
        DoLocMinmax(rp, pix3, 50, 100);  /* 3 - 5 */

        Assert.Equal(5, rp.Index);
        rp.Cleanup();
    }

    private static void DoLocMinmax(RegTest rp, Pix pixs, int minmax, int maxmin)
    {
        Pixa pixa = Pixa.Create(0);
        rp.WritePixAndCheck(pixs, L.IFF_PNG);  /* 0 */
        pixa.AddPix(pixs, L.L_COPY);
        pixs.LocalExtrema(minmax, maxmin, out Pix pix1, out Pix pix2);
        Pix.ComposeRGBPixel(255, 0, 0, out uint redval);
        Pix.ComposeRGBPixel(0, 255, 0, out uint greenval);
        Pix pixd = pixs.ConvertTo32();
        pixd.PaintThroughMask(pix2, 0, 0, greenval);
        pixd.PaintThroughMask(pix1, 0, 0, redval);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 1 */
        pixa.AddPix(pixd, L.L_INSERT);
        Pix pix3 = pixa.DisplayTiledInColumns(2, 1.0f, 25, 2);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 2 */
    }
}
