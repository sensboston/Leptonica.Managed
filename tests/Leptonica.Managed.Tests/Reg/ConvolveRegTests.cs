using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// convolve_reg.c: tests block, generic, biased, census, and windowed convolution functions.
/// </summary>
[Collection("ConvolveSampling")]
public class ConvolveRegTests
{
    private const string Kel1Str =
        " 20    50   80  50   20 " +
        " 50   100  140  100  50 " +
        " 90   160  200  160  90 " +
        " 50   100  140  100  50 " +
        " 20    50   80   50  20 ";

    private const string Kel2Str =
        " -20  -50  -80  -50  -20 " +
        " -50   50   80   50  -50 " +
        " -90   90  200   90  -90 " +
        " -50   50   80   50  -50 " +
        " -20  -50  -80  -50  -20 ";

    private const string Kel3XStr = " -70   40  100   40  -70 ";
    private const string Kel3YStr = "  20  -70   40  100   40  -70  20 ";

    [RegFact]
    public void Convolve()
    {
        var rp = new RegTest("convolve");

            /* Test pixBlockconvGray() on 8 bpp */
        Pix pixs = TestPaths.ReadInput("test8.jpg");
        Pix pixacc = pixs.BlockconvAccum();
        Pix pixd = pixs.BlockconvGray(pixacc, 3, 5);
        rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 0 */

            /* Test pixBlockconv() on 8 bpp */
        pixd = pixs.Blockconv(9, 8);
        rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 1 */

            /* Test pixBlockrank() on 1 bpp */
        pixs = TestPaths.ReadInput("test1.png");
        pixacc = pixs.BlockconvAccum();
        for (int i = 0; i < 3; i++) {
            pixd = pixs.Blockrank(pixacc, 4, 4, (float)(0.25 + 0.25 * i));
            rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 2 - 4 */
        }

            /* Test pixBlocksum() on 1 bpp */
        pixd = pixs.Blocksum(pixacc, 16, 16);
        rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 5 */

            /* Test pixCensusTransform() */
        pixs = TestPaths.ReadInput("test24.jpg");
        Pix pixg = pixs.ScaleRGBToGrayFast(2, L.COLOR_GREEN);
        pixd = pixg.CensusTransform(10, null);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 6 */

            /* Test generic convolution with kel1 */
        L_Kernel kel1 = L_Kernel.CreateFromString(5, 5, 2, 2, Kel1Str);
        pixd = pixg.Convolve(kel1, 8, 1);
        rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 7 */

            /* Test convolution with flat rectangular kel */
        L_Kernel kel2 = L_Kernel.Create(11, 11);
        kel2.SetOrigin(5, 5);
        for (int i = 0; i < 11; i++) {
            for (int j = 0; j < 11; j++)
                kel2.SetElement(i, j, 1);
        }
        pixd = pixg.Convolve(kel2, 8, 1);
        rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 8 */

            /* Test pixBlockconv() on 32 bpp */
        Pix pixt = pixs.ScaleBySampling(0.5f, 0.5f);
        pixd = pixt.Blockconv(4, 6);
        rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 9 */

            /* Test bias convolution non-separable with kel2 */
        pixs = TestPaths.ReadInput("marge.jpg");
        pixg = pixs.ScaleRGBToGrayFast(2, L.COLOR_GREEN);
        kel2 = L_Kernel.CreateFromString(5, 5, 2, 2, Kel2Str);
        pixd = pixg.ConvolveWithBias(kel2, null, 1, out _);
        rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 10 */

            /* Test bias convolution separable with kel3x and kel3y */
        L_Kernel kel3x = L_Kernel.CreateFromString(1, 5, 0, 2, Kel3XStr);
        L_Kernel kel3y = L_Kernel.CreateFromString(7, 1, 3, 0, Kel3YStr);
        pixd = pixg.ConvolveWithBias(kel3x, kel3y, 1, out _);
        rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 11 */

            /* Test pixWindowedMean() and pixWindowedMeanSquare() on 8 bpp */
        pixs = TestPaths.ReadInput("feyn-fract2.tif");
        pixg = pixs.ConvertTo8(false);
        int sizex = 5;
        int sizey = 20;
        Pix pixb = pixg.AddBorderGeneral(sizex + 1, sizex + 1,
                                         sizey + 1, sizey + 1, 0);
        Pix pixm = pixb.WindowedMean(sizex, sizey, 1, 1);
        Pix pixms = pixb.WindowedMeanSquare(sizex, sizey, 1);
        rp.WritePixAndCheck(pixm, L.IFF_JFIF_JPEG);  /* 12 */

            /* Test pixWindowedVariance() on 8 bpp */
        pixm.WindowedVariance(pixms, out FPix fpixv, out FPix fpixrv);
        Pix pixrv = fpixrv.ConvertToPix(8, L.L_CLIP_TO_ZERO, 1);
        rp.WritePixAndCheck(pixrv, L.IFF_JFIF_JPEG);  /* 13 */
        Pix pix1 = fpixv.DisplayMaxDynamicRange();
        Pix pix2 = fpixrv.DisplayMaxDynamicRange();
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 14 */
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 15 */

            /* Test again all windowed functions with simpler interface */
        pixg.WindowedStats(sizex, sizey, 0, out _, out _, out fpixv, out fpixrv);
        Pix pix3 = fpixv.DisplayMaxDynamicRange();
        Pix pix4 = fpixrv.DisplayMaxDynamicRange();
        rp.ComparePix(pix1, pix3);  /* 16 */
        rp.ComparePix(pix2, pix4);  /* 17 */

        Assert.Equal(17, rp.Index);
        rp.Cleanup();
    }
}
