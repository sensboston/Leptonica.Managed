using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// dither_reg.c: dithering to 1 and 2 bpp, and 2x and 4x upscaling with dither.
/// </summary>
public class DitherRegTests
{
    [RegFact]
    public void Dither()
    {
        Pix pix1, pix2;
        var rp = new RegTest("dither");

        Pix pix = TestPaths.ReadInput("test8.jpg");
        Pix pixs = Pix.GammaTRC(null, pix, 1.3f, 0, 255);  /* gamma of 1.3, for fun */

            /* Dither to 1 bpp */
        pix1 = pixs.DitherToBinary();
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0 */

             /* Dither to 2 bpp, with colormap */
        pix1 = pixs.DitherTo2bpp(true);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 1 */

             /* Dither to 2 bpp, without colormap */
        pix2 = pixs.DitherTo2bpp(false);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 2 */
        rp.ComparePix(pix1, pix2);   /* 3 */

            /* Dither 2x upscale to 1 bpp */
        pix1 = pixs.ScaleGray2xLIDither();
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 4 */

            /* Dither 4x upscale to 1 bpp */
        pix1 = pixs.ScaleGray4xLIDither();
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 5 */

        Assert.Equal(5, rp.Index);
        rp.Cleanup();
    }
}
