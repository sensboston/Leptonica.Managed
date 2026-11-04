using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// threshnorm_reg.c: adaptive threshold spread normalization with Sobel and
/// two-sided edge filters, followed by binarization at several thresholds.
/// </summary>
public class ThreshNormRegTests
{
    [RegFact]
    public void ThreshNorm()
    {
        var rp = new RegTest("threshnorm");

        Pix pixs = TestPaths.ReadInput("stampede2.jpg");
        Pixa pixa = Pixa.Create(0);

        AddTestSet(pixa, pixs, L.L_SOBEL_EDGE, 18, 40, 40, 0.7f, -25, 280, 128);
        AddTestSet(pixa, pixs, L.L_TWO_SIDED_EDGE, 18, 40, 40, 0.7f, -25, 280, 128);
        AddTestSet(pixa, pixs, L.L_SOBEL_EDGE, 10, 40, 40, 0.7f, -15, 305, 128);
        AddTestSet(pixa, pixs, L.L_TWO_SIDED_EDGE, 10, 40, 40, 0.7f, -15, 305, 128);
        AddTestSet(pixa, pixs, L.L_SOBEL_EDGE, 15, 40, 40, 0.6f, -45, 285, 158);
        AddTestSet(pixa, pixs, L.L_TWO_SIDED_EDGE, 15, 40, 40, 0.6f, -45, 285, 158);

        Pix pixd = pixa.DisplayTiledInColumns(6, 1.0f, 20, 0);
        rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 0 */

        Assert.Equal(0, rp.Index);
        rp.Cleanup();
    }

    private static void AddTestSet(Pixa pixa, Pix pixs, int filtertype, int edgethresh,
                                   int smoothx, int smoothy, float gamma, int minval,
                                   int maxval, int targetthresh)
    {
        Pix pix3;

        pixs.ThresholdSpreadNorm(filtertype, edgethresh, smoothx, smoothy, gamma, minval,
                                 maxval, targetthresh, true, false, true,
                                 out Pix? pix1, out _, out Pix? pix2);
        pixa.AddPix(pix1!, L.L_INSERT);
        pixa.AddPix(pix2!, L.L_INSERT);
        pix3 = pix2!.ThresholdToBinary(targetthresh - 20);
        pixa.AddPix(pix3, L.L_INSERT);
        pix3 = pix2.ThresholdToBinary(targetthresh);
        pixa.AddPix(pix3, L.L_INSERT);
        pix3 = pix2.ThresholdToBinary(targetthresh + 20);
        pixa.AddPix(pix3, L.L_INSERT);
        pix3 = pix2.ThresholdToBinary(targetthresh + 40);
        pixa.AddPix(pix3, L.L_INSERT);
    }
}
