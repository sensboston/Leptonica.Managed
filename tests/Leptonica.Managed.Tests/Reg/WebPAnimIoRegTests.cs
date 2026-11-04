using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// webpanimio_reg.c: regression test for animated WebP writing.
/// </summary>
public class WebPAnimIoRegTests
{
    [RegFact]
    public void WebPAnimIo()
    {
        var rp = new RegTest("webpanimio");

        Utils.LeptRmdir("lept/webpanim");
        Utils.LeptMkdir("lept/webpanim");

        const int niters = 5;
        const int duration = 250;
        Pix pix1 = Pix.Read(TestPaths.ProgFile("marge.jpg"));
        Pix pix2 = Pix.Rotate180(null, pix1);
        Pixa pixa = Pixa.Create(6);
        pixa.AddPix(pix1, L.L_COPY);
        pixa.AddPix(pix2, L.L_COPY);
        Pixa.WriteWebPAnim("/tmp/lept/webpanim/margeanim.webp", pixa, niters, duration, 80, 0);
        rp.CheckFile("/tmp/lept/webpanim/margeanim.webp");

        rp.Cleanup();
    }
}
