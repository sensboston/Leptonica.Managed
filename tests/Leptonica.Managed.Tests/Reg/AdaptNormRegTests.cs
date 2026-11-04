using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// adaptnorm_reg.c: image normalization for two extreme cases: variable and low
/// contrast, and good contrast but fast varying background.
/// </summary>
public class AdaptNormRegTests
{
    [RegFact]
    public void AdaptNorm()
    {
        var rp = new RegTest("adaptnorm");

        /* ---------------------------------------------------------- *
         *     Normalize by adaptively expanding the dynamic range    *
         * ---------------------------------------------------------- */
        Pixa pixa1 = Pixa.Create(0);
        Pix pixs = TestPaths.ReadInput("lighttext.jpg");
        rp.WritePixAndCheck(pixs, L.IFF_JFIF_JPEG);  /* 0 */
        pixa1.AddPix(pixs, L.L_INSERT);
        Pix pix1 = Pix.ContrastNorm(null, pixs, 10, 10, 40, 2, 2);
        pixa1.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 1 */

            /* Apply a gamma to clean up the remaining background */
        Pix pix2 = Pix.GammaTRC(null, pix1, 1.5f, 50, 235);
        pixa1.AddPix(pix2, L.L_INSERT);
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 2 */

            /* Here are two possible output display images; a dithered
             * 2 bpp image and a 7 level thresholded 4 bpp image */
        Pix pix3 = pix2.DitherTo2bpp(true);
        pixa1.AddPix(pix3, L.L_INSERT);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 3 */
        Pix pix4 = pix2.ThresholdTo4bpp(7, true);
        pixa1.AddPix(pix4, L.L_INSERT);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 4 */

            /* Binary image produced from 8 bpp normalized ones,
             * before and after the gamma correction. */
        Pix pix5 = pix1.ThresholdToBinary(180);
        pixa1.AddPix(pix5, L.L_INSERT);
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 5 */
        Pix pix6 = pix2.ThresholdToBinary(200);
        pixa1.AddPix(pix6, L.L_INSERT);
        rp.WritePixAndCheck(pix6, L.IFF_PNG);  /* 6 */

        Pix pix7 = pixa1.DisplayTiledInColumns(3, 1.0f, 30, 2);
        rp.WritePixAndCheck(pix7, L.IFF_JFIF_JPEG);  /* 7 */

        /* ---------------------------------------------------------- *
         *          Normalize for rapidly varying background          *
         * ---------------------------------------------------------- */
        pixa1 = Pixa.Create(0);
        pixs = TestPaths.ReadInput("w91frag.jpg");
        pixa1.AddPix(pixs, L.L_INSERT);
        rp.WritePixAndCheck(pixs, L.IFF_JFIF_JPEG);  /* 8 */
        pix1 = pixs.BackgroundNormFlex(7, 7, 1, 1, 10);
        pixa1.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 9 */

            /* Now do it again in several steps */
        pix2 = pixs.ScaleSmooth(1.0f / 7.0f, 1.0f / 7.0f);
        pix3 = pix2.Scale(7.0f, 7.0f);
        pixa1.AddPix(pix3, L.L_INSERT);
        rp.WritePixAndCheck(pix3, L.IFF_JFIF_JPEG);  /* 10 */
        pix2.LocalExtrema(0, 0, out Pix pixmin, out _);  /* 1's at minima */
        pix4 = pixmin.ExpandBinaryReplicate(7, 7);
        pixa1.AddPix(pix4, L.L_INSERT);
        rp.WritePixAndCheck(pix4, L.IFF_JFIF_JPEG);  /* 11 */
        pix5 = pixmin.SeedfillGrayBasin(pix2, 10, 4);
        pix6 = pix5.ExtendByReplication(1, 1);
        rp.WritePixAndCheck(pix6, L.IFF_JFIF_JPEG);  /* 12 */
        Pix pix7b = pix6.GetInvBackgroundMap(200, 1, 1);  /* smoothing incl. */
        Pix pix8 = pixs.ApplyInvBackgroundGrayMap(pix7b, 7, 7);
        pixa1.AddPix(pix8, L.L_INSERT);
        rp.WritePixAndCheck(pix8, L.IFF_JFIF_JPEG);  /* 13 */

            /* Process the result for gray and binary output */
        Pix pix9 = Pix.GammaTRCMasked(null, pix1, null, 1.0f, 100, 175);
        pixa1.AddPix(pix9, L.L_INSERT);
        rp.WritePixAndCheck(pix9, L.IFF_JFIF_JPEG);  /* 14 */
        Pix pix10 = pix9.ThresholdTo4bpp(10, true);
        pixa1.AddPix(pix10, L.L_INSERT);
        rp.WritePixAndCheck(pix10, L.IFF_JFIF_JPEG);  /* 15 */
        Pix pix11 = pix9.ThresholdToBinary(190);
        pixa1.AddPix(pix11, L.L_INSERT);
        rp.WritePixAndCheck(pix11, L.IFF_JFIF_JPEG);  /* 16 */

        pix2 = pixa1.DisplayTiledInColumns(3, 1.0f, 30, 2);
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 17 */

        Assert.Equal(17, rp.Index);
        rp.Cleanup();
    }
}
