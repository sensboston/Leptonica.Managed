using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// rasteropip_reg.c: in-place rasterop with overlapping source and
/// destination, and the mirrored border that uses it.
/// </summary>
public class RasteropIpRegTests
{
    [RegFact]
    public void RasteropIp()
    {
        int i, j;
        Pix pixt, pixd;
        var rp = new RegTest("rasteropip");

        Pix pixs = TestPaths.ReadInput("test8.jpg");
        pixt = Pix.Copy(null, pixs);

            /* Copy, in-place and one COLUMN at a time, from the right
               side to the left side. */
        for (j = 0; j < 200; j++)
            pixs.Rasterop(20 + j, 20, 1, 250, L.PIX_SRC, pixs, 250 + j, 20);

            /* Copy, in-place and one ROW at a time, from the right
               side to the left side. */
        for (i = 0; i < 250; i++)
            pixt.Rasterop(20, 20 + i, 200, 1, L.PIX_SRC, pixt, 250, 20 + i);

            /* Test */
        rp.ComparePix(pixs, pixt);   /* 0 */

            /* Show the mirrored border, which uses the general
               pixRasterop() on an image in-place.  */
        pixs = TestPaths.ReadInput("test8.jpg");
        pixt = pixs.RemoveBorder(40);
        pixd = pixt.AddMirroredBorder(40, 40, 40, 40);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 1 */

        Assert.Equal(1, rp.Index);
        rp.Cleanup();
    }
}
