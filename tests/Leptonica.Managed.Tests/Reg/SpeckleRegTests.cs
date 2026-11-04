using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// speckle_reg.c: removal of speckle noise of up to 2x2 and 3x3 pixels
/// from a binarized, background-normalized image, by hit-miss transform.
/// </summary>
public class SpeckleRegTests
{
        /* HMT (with just misses) for speckle up to 2x2 */
    private const string selstr2 = "oooo" +
                                   "oC o" +
                                   "o  o" +
                                   "oooo";
        /* HMT (with just misses) for speckle up to 3x3 */
    private const string selstr3 = "ooooo" +
                                   "oC  o" +
                                   "o   o" +
                                   "o   o" +
                                   "ooooo";

    [RegFact]
    public void Speckle()
    {
        Pix pix1, pix2, pix3, pix4, pix5, pix6, pix7, pix8, pix9, pix10;
        Pixa pixa1;
        Sel sel1, sel2, sel3, sel4;
        var rp = new RegTest("speckle");

            /*  Normalize for rapidly varying background */
        pixa1 = Pixa.Create(0);
        Pix pixs = TestPaths.ReadInput("w91frag.jpg");
        pixa1.AddPix(pixs, L.L_INSERT);
        rp.WritePixAndCheck(pixs, L.IFF_JFIF_JPEG);  /* 0 */
        pix1 = pixs.BackgroundNormFlex(7, 7, 1, 1, 10);
        pixa1.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 1 */

            /* Remove the background */
        pix2 = Pix.GammaTRCMasked(null, pix1, null, 1.0f, 100, 175);
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 2 */

            /* Binarize */
        pix3 = pix2.ThresholdToBinary(180);
        pixa1.AddPix(pix3, L.L_INSERT);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 3 */

            /* Remove the speckle noise up to 2x2 */
        sel1 = Sel.CreateFromString(selstr2, 4, 4, "speckle2");
        pix4 = Pix.HMT(null, pix3, sel1);
        pixa1.AddPix(pix4, L.L_INSERT);
        sel2 = Sel.CreateBrick(2, 2, 0, 0, L.SEL_HIT);
        pix5 = Pix.Dilate(null, pix4, sel2);
        pixa1.AddPix(pix5, L.L_INSERT);
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 4 */
        pix6 = Pix.Subtract(null, pix3, pix5);
        pixa1.AddPix(pix6, L.L_INSERT);
        rp.WritePixAndCheck(pix6, L.IFF_PNG);  /* 5 */

            /* Remove the speckle noise up to 3x3 */
        sel3 = Sel.CreateFromString(selstr3, 5, 5, "speckle3");
        pix7 = Pix.HMT(null, pix3, sel3);
        pixa1.AddPix(pix7, L.L_INSERT);
        sel4 = Sel.CreateBrick(3, 3, 0, 0, L.SEL_HIT);
        pix8 = Pix.Dilate(null, pix7, sel4);
        pixa1.AddPix(pix8, L.L_INSERT);
        rp.WritePixAndCheck(pix8, L.IFF_PNG);  /* 6 */
        pix9 = Pix.Subtract(null, pix3, pix8);
        pixa1.AddPix(pix9, L.L_INSERT);
        rp.WritePixAndCheck(pix9, L.IFF_PNG);  /* 7 */

        pix10 = pixa1.DisplayTiledInColumns(3, 1.0f, 30, 2);
        rp.WritePixAndCheck(pix10, L.IFF_JFIF_JPEG);  /* 8 */

        Assert.Equal(8, rp.Index);
        rp.Cleanup();
    }
}
