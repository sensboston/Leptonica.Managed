using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// lowsat_reg.c: functions that identify and modify image pixels that have low
/// saturation (i.e., are essentially gray).
/// </summary>
public class LowSatRegTests
{
    [RegFact]
    public void LowSat()
    {
        int i, j, wpl, val;
        uint gray32;
        uint[] data;
        Pix pix1, pix2, pix3, pix4, pix5, pix6, pix7;
        Pixa pixa;
        var rp = new RegTest("lowsat");

        Utils.LeptMkdir("lept/lowsat");
        pixa = Pixa.Create(0);
        pix1 = TestPaths.ReadInput("zier.jpg");
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0 */
        pixa.AddPix(pix1, L.L_INSERT);

            /* Embed the image in a varying gray background */
        pix2 = Pix.Create(400, 580, 32);
        data = pix2.Data;
        wpl = pix2.Wpl;
        for (i = 0; i < 580; i++) {
            int line = i * wpl;
            val = 150 + 50 * i / 580;
            for (j = 0; j < 400; j++) {
                Pix.ComposeRGBPixel(val, val, val, out gray32);
                data[line + j] = gray32;
            }
        }
        pix2.Rasterop(70, 90, 270, 400, L.PIX_SRC, pix1, 0, 0);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 1 */
        pixa.AddPix(pix2, L.L_COPY);

            /* Darken the gray pixels, leaving most of the
             * the others unaffected.  */
        pix3 = Pix.DarkenGray(null, pix2, 220, 10);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 2 */
        pixa.AddPix(pix3, L.L_COPY);

            /* We can also generate a mask over the gray pixels,
             * eliminating noise from very dark pixels morphologically. */
        pix4 = pix2.MaskOverGrayPixels(220, 10);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 3 */
        pixa.AddPix(pix4, L.L_INSERT);
        pix5 = pix4.MorphSequence("o20.20", 0);  /* remove noise */
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 4 */
        pixa.AddPix(pix5, L.L_COPY);
        Pix.Invert(pix5, pix5);
        pix6 = pix5.ConvertTo32();
        pix7 = pix2.AddRGB(pix6);
        rp.WritePixAndCheck(pix7, L.IFF_PNG);  /* 5 */
        pixa.AddPix(pix7, L.L_INSERT);

        Assert.Equal(5, rp.Index);
        rp.Cleanup();
    }
}
