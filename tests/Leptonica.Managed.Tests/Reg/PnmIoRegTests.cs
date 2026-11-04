using System.IO;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// pnmio_reg.c: tests read and write of both ascii and packed pnm, using
/// pix with 1, 2, 4, 8 and 32 bpp.
/// </summary>
public class PnmIoRegTests
{
    [RegFact]
    public void PnmIo()
    {
        byte[] data;
        Pix pix1, pix2, pix3, pix4, pix5;
        var rp = new RegTest("pnmio");

        Utils.LeptRmdir("lept/pnm");
        Utils.LeptMkdir("lept/pnm");

            /* Test 1 bpp (pbm) read/write */
        pix1 = TestPaths.ReadInput("char.tif");
        using (FileStream fp = Utils.FopenWriteStream("/tmp/lept/pnm/pix1.1.pnm", "wb"))
            Pix.WriteStreamAsciiPnm(fp, pix1);
        pix2 = Pix.Read("/tmp/lept/pnm/pix1.1.pnm");
        Pix.Write("/tmp/lept/pnm/pix2.1.pnm", pix2, L.IFF_PNM);
        pix3 = Pix.Read("/tmp/lept/pnm/pix2.1.pnm");
        rp.ComparePix(pix1, pix3);  /* 0 */
            /* write PAM */
        using (FileStream fp = Utils.FopenWriteStream("/tmp/lept/pnm/pix3.1.pnm", "wb"))
            Pix.WriteStreamPam(fp, pix1);
        pix4 = Pix.Read("/tmp/lept/pnm/pix3.1.pnm");
        rp.ComparePix(pix1, pix4);  /* 1 */

            /* Test 2, 4 and 8 bpp (pgm) read/write */
        pix1 = TestPaths.ReadInput("weasel8.png");
        pix2 = pix1.ThresholdTo2bpp(4, false);
        using (FileStream fp = Utils.FopenWriteStream("/tmp/lept/pnm/pix2.2.pnm", "wb"))
            Pix.WriteStreamAsciiPnm(fp, pix2);
        pix3 = Pix.Read("/tmp/lept/pnm/pix2.2.pnm");
        Pix.Write("/tmp/lept/pnm/pix3.2.pnm", pix3, L.IFF_PNM);
        pix4 = Pix.Read("/tmp/lept/pnm/pix3.2.pnm");
        rp.ComparePix(pix2, pix4);  /* 2 */
            /* write PAM */
        using (FileStream fp = Utils.FopenWriteStream("/tmp/lept/pnm/pix4.2.pnm", "wb"))
            Pix.WriteStreamPam(fp, pix2);
        pix5 = Pix.Read("/tmp/lept/pnm/pix4.2.pnm");
        rp.ComparePix(pix2, pix5);  /* 3 */

        pix2 = pix1.ThresholdTo4bpp(16, false);
        using (FileStream fp = Utils.FopenWriteStream("/tmp/lept/pnm/pix2.4.pnm", "wb"))
            Pix.WriteStreamAsciiPnm(fp, pix2);
        pix3 = Pix.Read("/tmp/lept/pnm/pix2.4.pnm");
        Pix.Write("/tmp/lept/pnm/pix3.4.pnm", pix3, L.IFF_PNM);
        pix4 = Pix.Read("/tmp/lept/pnm/pix3.4.pnm");
        rp.ComparePix(pix2, pix4);  /* 4 */
            /* write PAM */
        using (FileStream fp = Utils.FopenWriteStream("/tmp/lept/pnm/pix4.4.pnm", "wb"))
            Pix.WriteStreamPam(fp, pix2);
        pix5 = Pix.Read("/tmp/lept/pnm/pix4.4.pnm");
        rp.ComparePix(pix2, pix5);  /* 5 */

        using (FileStream fp = Utils.FopenWriteStream("/tmp/lept/pnm/pix1.8.pnm", "wb"))
            Pix.WriteStreamAsciiPnm(fp, pix1);
        pix2 = Pix.Read("/tmp/lept/pnm/pix1.8.pnm");
        Pix.Write("/tmp/lept/pnm/pix2.8.pnm", pix2, L.IFF_PNM);
        pix3 = Pix.Read("/tmp/lept/pnm/pix2.8.pnm");
        rp.ComparePix(pix1, pix3);  /* 6 */
            /* write PAM */
        using (FileStream fp = Utils.FopenWriteStream("/tmp/lept/pnm/pix3.8.pnm", "wb"))
            Pix.WriteStreamPam(fp, pix1);
        pix4 = Pix.Read("/tmp/lept/pnm/pix3.8.pnm");
        rp.ComparePix(pix1, pix4);  /* 7 */

            /* Test ppm (24 bpp rgb) read/write */
        pix1 = TestPaths.ReadInput("marge.jpg");
        using (FileStream fp = Utils.FopenWriteStream("/tmp/lept/pnm/pix1.24.pnm", "wb"))
            Pix.WriteStreamAsciiPnm(fp, pix1);
        pix2 = Pix.Read("/tmp/lept/pnm/pix1.24.pnm");
        Pix.Write("/tmp/lept/pnm/pix2.24.pnm", pix2, L.IFF_PNM);
        pix3 = Pix.Read("/tmp/lept/pnm/pix2.24.pnm");
        rp.ComparePix(pix1, pix3);  /* 8 */
            /* write mem pnm */
        data = Pix.WriteMemPnm(pix1);
        pix3 = Pix.ReadMemPnm(data);
        rp.ComparePix(pix1, pix3);  /* 9 */
            /* write pam */
        using (FileStream fp = Utils.FopenWriteStream("/tmp/lept/pnm/pix3.24.pnm", "wb"))
            Pix.WriteStreamPam(fp, pix1);
        pix4 = Pix.Read("/tmp/lept/pnm/pix3.24.pnm");
        rp.ComparePix(pix1, pix4);  /* 10 */

            /* Test pam (32 bpp rgba) read/write */
        pix1 = TestPaths.ReadInput("test32-alpha.png");
        using (FileStream fp = Utils.FopenWriteStream("/tmp/lept/pnm/pix1.32.pnm", "wb"))
            Pix.WriteStreamPam(fp, pix1);
        pix2 = Pix.Read("/tmp/lept/pnm/pix1.32.pnm");
        Pix.Write("/tmp/lept/pnm/pix2.32.pnm", pix2, L.IFF_PNM);
        pix3 = Pix.Read("/tmp/lept/pnm/pix2.32.pnm");
        rp.ComparePix(pix1, pix3);  /* 11 */

        Assert.Equal(11, rp.Index);
        rp.Cleanup();
    }
}
