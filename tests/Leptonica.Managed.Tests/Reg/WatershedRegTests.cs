using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// watershed_reg.c: watershed transform of two synthetic images.
/// </summary>
public class WatershedRegTests
{
    [RegFact]
    public void Watershed()
    {
        int i, j;
        float f;
        Pix pix1, pix2;
        var rp = new RegTest("watershed");

        pix1 = Pix.Create(500, 500, 8);
        pix2 = Pix.Create(500, 500, 8);
        for (i = 0; i < 500; i++) {
            for (j = 0; j < 500; j++) {
                f = (float)(128.0 + 26.3 * Math.Sin(0.0438 * (float)i));
                f = (float)(f + 33.4 * Math.Cos(0.0712 * (float)i));
                f = (float)(f + 18.6 * Math.Sin(0.0561 * (float)j));
                f = (float)(f + 23.6 * Math.Cos(0.0327 * (float)j));
                pix1.SetPixel(j, i, (uint)(int)f);
                f = (float)(128.0 + 26.3 * Math.Sin(0.0238 * (float)i));
                f = (float)(f + 33.4 * Math.Cos(0.0312 * (float)i));
                f = (float)(f + 18.6 * Math.Sin(0.0261 * (float)j));
                f = (float)(f + 23.6 * Math.Cos(0.0207 * (float)j));
                pix2.SetPixel(j, i, (uint)(int)f);
            }
        }
        DoWatershed(rp, pix1);  /* 0 - 11 */
        DoWatershed(rp, pix2);  /* 12 - 23 */

        Assert.Equal(23, rp.Index);
        rp.Cleanup();
    }

    private static void DoWatershed(RegTest rp, Pix pixs)
    {
        uint redval, greenval;
        L_WShed wshed;
        Pix pixc, pix1, pix2, pix3, pix4, pix5, pix6, pix7, pix8, pix9;
        Pixa pixa;
        Pta pta;

            /* Find local extrema */
        pixa = Pixa.Create(0);
        pixs.GetDimensions(out int w, out int h, out _);
        rp.WritePixAndCheck(pixs, L.IFF_PNG);  /* 0 */
        pixa.AddPix(pixs, L.L_COPY);
        pixs.LocalExtrema(0, 0, out pix1, out pix2);
        pix1.SetOrClearBorder(2, 2, 2, 2, L.PIX_CLR);
        Pix.ComposeRGBPixel(255, 0, 0, out redval);
        Pix.ComposeRGBPixel(0, 255, 0, out greenval);
        pixc = pixs.ConvertTo32();
        pixc.PaintThroughMask(pix2, 0, 0, greenval);
        pixc.PaintThroughMask(pix1, 0, 0, redval);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 1 */
        pixa.AddPix(pixc, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 2 */
        pixa.AddPix(pix1, L.L_COPY);

            /* Generate seeds for watershed */
        pixs.SelectMinInConnComp(pix1, out pta, out _);
        pix3 = Pix.GenerateFromPta(pta, w, h);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 3 */
        pixa.AddPix(pix3, L.L_COPY);
        pix4 = pixs.ConvertTo32();
        pix4.PaintThroughMask(pix3, 0, 0, greenval);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 4 */
        pixa.AddPix(pix4, L.L_COPY);
        pix5 = Pix.RemoveSeededComponents(null, pix3, pix1, 8, 2);
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 5 */
        pixa.AddPix(pix5, L.L_COPY);
        bool empty = pix5.Zero();
        rp.CompareValues(1, empty ? 1 : 0, 0.0f);  /* 6 */

            /* Make and display watershed */
        wshed = L_WShed.Create(pixs, pix3, 10, 0);
        wshed.Apply();
        pix6 = wshed.Pixad!.DisplayRandomCmap(w, h);
        rp.WritePixAndCheck(pix6, L.IFF_PNG);  /* 7 */
        pixa.AddPix(pix6, L.L_COPY);
        byte[] data = wshed.Nalevels!.WriteMem();
        rp.WriteDataAndCheck(data, "na");  /* 8 */
        pix7 = wshed.RenderFill();
        rp.WritePixAndCheck(pix7, L.IFF_PNG);  /* 9 */
        pixa.AddPix(pix7, L.L_COPY);
        pix8 = wshed.RenderColors();
        rp.WritePixAndCheck(pix8, L.IFF_PNG);  /* 10 */
        pixa.AddPix(pix8, L.L_COPY);

        pix9 = pixa.DisplayTiledInColumns(3, 1.0f, 20, 0);
        rp.WritePixAndCheck(pix9, L.IFF_PNG);  /* 11 */
    }
}
