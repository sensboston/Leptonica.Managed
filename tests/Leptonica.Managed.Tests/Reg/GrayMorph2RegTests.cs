using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// graymorph2_reg.c: gray morphology with the 3-line fast implementations
/// against the general ones.
/// </summary>
public class GrayMorph2RegTests
{
    [RegFact]
    public void GrayMorph2()
    {
        Pix pix1, pix2;
        var rp = new RegTest("graymorph2");

        Pix pixs = TestPaths.ReadInput("test8.jpg");

            /* Dilation */
        pix1 = pixs.DilateGray3(3, 1);
        pix2 = pixs.DilateGray(3, 1);
        rp.ComparePix(pix1, pix2);  /* 0 */

        pix1 = pixs.DilateGray3(1, 3);
        pix2 = pixs.DilateGray(1, 3);
        rp.ComparePix(pix1, pix2);  /* 1 */

        pix1 = pixs.DilateGray3(3, 3);
        pix2 = pixs.DilateGray(3, 3);
        rp.ComparePix(pix1, pix2);  /* 2 */

            /* Erosion */
        pix1 = pixs.ErodeGray3(3, 1);
        pix2 = pixs.ErodeGray(3, 1);
        rp.ComparePix(pix1, pix2);  /* 3 */

        pix1 = pixs.ErodeGray3(1, 3);
        pix2 = pixs.ErodeGray(1, 3);
        rp.ComparePix(pix1, pix2);  /* 4 */

        pix1 = pixs.ErodeGray3(3, 3);
        pix2 = pixs.ErodeGray(3, 3);
        rp.ComparePix(pix1, pix2);  /* 5 */

            /* Opening */
        pix1 = pixs.OpenGray3(3, 1);
        pix2 = pixs.OpenGray(3, 1);
        rp.ComparePix(pix1, pix2);  /* 6 */

        pix1 = pixs.OpenGray3(1, 3);
        pix2 = pixs.OpenGray(1, 3);
        rp.ComparePix(pix1, pix2);  /* 7 */

        pix1 = pixs.OpenGray3(3, 3);
        pix2 = pixs.OpenGray(3, 3);
        rp.ComparePix(pix1, pix2);  /* 8 */

            /* Closing */
        pix1 = pixs.CloseGray3(3, 1);
        pix2 = pixs.CloseGray(3, 1);
        rp.ComparePix(pix1, pix2);  /* 9 */

        pix1 = pixs.CloseGray3(1, 3);
        pix2 = pixs.CloseGray(1, 3);
        rp.ComparePix(pix1, pix2);  /* 10 */

        pix1 = pixs.CloseGray3(3, 3);
        pix2 = pixs.CloseGray(3, 3);
        rp.ComparePix(pix1, pix2);  /* 11 */

        Assert.Equal(11, rp.Index);
        rp.Cleanup();
    }
}
