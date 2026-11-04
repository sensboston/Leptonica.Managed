using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>skew_reg.c: skew detection.</summary>
public class SkewRegTests
{
    private const int BORDER = 150;

    [RegFact]
    public void Skew()
    {
        int w, h, wd, hd;
        float deg2rad, angle, conf;
        Pix pixs, pixb1, pixb2, pixr, pixf, pixd, pixc;
        Pixa pixa;
        var rp = new RegTest("skew");

        deg2rad = (float)(3.1415926535 / 180.0);

        pixa = Pixa.Create(0);
        pixs = TestPaths.ReadInput("feyn.tif");
        pixs.SetOrClearBorder(100, 250, 100, 0, L.PIX_CLR);
        pixb1 = pixs.ReduceRankBinaryCascade(2, 2, 0, 0);
        rp.WritePixAndCheck(pixb1, L.IFF_PNG);  /* 0 */

            /* Add a border and locate and deskew a 40 degree rotation */
        pixb2 = pixb1.AddBorder(BORDER, 0);
        pixb2.GetDimensions(out w, out h, out _);
        pixa.AddPix(pixb2, L.L_COPY);
        pixr = pixb2.RotateBySampling(w / 2, h / 2, (float)(deg2rad * 40.0), L.L_BRING_IN_WHITE);
        rp.WritePixAndCheck(pixr, L.IFF_PNG);  /* 1 */
        pixa.AddPix(pixr, L.L_INSERT);
        pixr.FindSkewSweepAndSearchScorePivot(out angle, out conf, out _, 1, 1,
                                              0.0f, 45.0f, 2.0f, 0.03f,
                                              L.L_SHEAR_ABOUT_CENTER);
        pixf = pixr.RotateBySampling(w / 2, h / 2, (float)(deg2rad * angle), L.L_BRING_IN_WHITE);
        pixd = pixf.RemoveBorder(BORDER);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 2 */
        pixa.AddPix(pixd, L.L_INSERT);

            /* Do a rotation larger than 90 degrees using embedding;
             * Use 2 sets of measurements at 90 degrees to scan the
             * full range of possible rotation angles. */
        pixb1.GetDimensions(out w, out h, out _);
        pixr = pixb1.Rotate((float)(deg2rad * 37.0), L.L_ROTATE_SAMPLING,
                            L.L_BRING_IN_WHITE, w, h);
        rp.WritePixAndCheck(pixr, L.IFF_PNG);  /* 3 */
        pixa.AddPix(pixr, L.L_INSERT);
        pixr.FindSkewOrthogonalRange(out angle, out conf, 2, 1,
                                     47.0f, 1.0f, 0.03f, 0.0f);
        pixd = pixr.Rotate((float)(deg2rad * angle), L.L_ROTATE_SAMPLING,
                           L.L_BRING_IN_WHITE, w, h);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 4 */
        pixd.GetDimensions(out wd, out hd, out _);
        pixc = Pix.Create(w, h, 1);
        pixc.Rasterop(0, 0, w, h, L.PIX_SRC, pixd, (wd - w) / 2, (hd - h) / 2);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 5 */
        pixa.AddPix(pixc, L.L_INSERT);

        pixd = pixa.DisplayTiledInColumns(3, 0.5f, 20, 3);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 6 */

        Assert.Equal(6, rp.Index);
        rp.Cleanup();
    }
}
