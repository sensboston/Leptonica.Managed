using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// grayfill_reg.c: inverse and standard grayscale seed filling, basin fill
/// from the minima, and hybrid against simple iterative fills.
/// </summary>
public class GrayFillRegTests
{
    [RegFact]
    public void GrayFill()
    {
        int i, j;
        Pix pixm, pixmi, pixs1, pixs1_8, pix1;
        Pix pixs2, pixs2_8, pixs3, pixs3_8;
        Pix pixb1, pixb2, pixb3, pixmin;
        Pixa pixa;
        var rp = new RegTest("grayfill");

            /* Mask */
        pixm = Pix.Create(200, 200, 8);
        for (i = 0; i < 200; i++)
            for (j = 0; j < 200; j++)
                pixm.SetPixel(j, i, (uint)(20 + Math.Abs((100 - i) * (100 - j)) / 50));
        pixmi = Pix.Invert(null, pixm);

            /* Seed1 */
        pixs1 = Pix.Create(200, 200, 8);
        for (i = 99; i <= 101; i++)
            for (j = 99; j <= 101; j++)
                pixs1.SetPixel(j, i, (uint)(50 - i/100 - j/100));
        pixs1_8 = Pix.Copy(null, pixs1);

            /* Seed2 */
        pixs2 = Pix.Create(200, 200, 8);
        for (i = 99; i <= 101; i++)
            for (j = 99; j <= 101; j++)
                pixs2.SetPixel(j, i, (uint)(205 - i/100 - j/100));
        pixs2_8 = Pix.Copy(null, pixs2);

            /* Inverse grayscale fill */
        pixa = Pixa.Create(0);
        pixa.AddPix(pixm, L.L_COPY);
        rp.WritePixAndCheck(pixm, L.IFF_PNG);  /* 0 */
        pixa.AddPix(pixs1, L.L_COPY);
        rp.WritePixAndCheck(pixs1, L.IFF_PNG);  /* 1 */
        pixs1.SeedfillGrayInv(pixm, 4);
        pixs1_8.SeedfillGrayInv(pixm, 8);
        pixa.AddPix(pixs1, L.L_COPY);
        rp.WritePixAndCheck(pixs1, L.IFF_PNG);  /* 2 */
        pixa.AddPix(pixs1_8, L.L_COPY);
        rp.WritePixAndCheck(pixs1_8, L.IFF_PNG);  /* 3 */
        pixb1 = pixs1.ThresholdToBinary(20);
        pixa.AddPix(pixb1, L.L_COPY);
        rp.WritePixAndCheck(pixb1, L.IFF_PNG);  /* 4 */
        pixs1.CombineMasked(pixm, pixb1);
        pixa.AddPix(pixs1, L.L_COPY);
        rp.WritePixAndCheck(pixs1, L.IFF_PNG);  /* 5 */
        pix1 = pixa.DisplayTiledInColumns(6, 1.0f, 15, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 6 */

            /* Standard grayscale fill */
        pixa = Pixa.Create(0);
        pixa.AddPix(pixmi, L.L_COPY);
        rp.WritePixAndCheck(pixmi, L.IFF_PNG);  /* 7 */
        pixa.AddPix(pixs2, L.L_COPY);
        rp.WritePixAndCheck(pixs2, L.IFF_PNG);  /* 8 */
        pixs2.SeedfillGray(pixmi, 4);
        pixs2_8.SeedfillGray(pixmi, 8);
        pixa.AddPix(pixs2, L.L_COPY);
        rp.WritePixAndCheck(pixs2, L.IFF_PNG);  /* 9 */
        pixa.AddPix(pixs2_8, L.L_COPY);
        rp.WritePixAndCheck(pixs2_8, L.IFF_PNG);  /* 10 */
        pixb2 = pixs2.ThresholdToBinary(205);
        rp.WritePixAndCheck(pixb2, L.IFF_PNG);  /* 11 */
        pixa.AddPix(pixb2, L.L_INSERT);
        pix1 = pixa.DisplayTiledInColumns(5, 1.0f, 15, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 12 */

            /* Basin fill from minima as seed */
        pixa = Pixa.Create(0);
        pixa.AddPix(pixm, L.L_COPY);
        rp.WritePixAndCheck(pixm, L.IFF_PNG);  /* 13 */
        pixm.LocalExtrema(0, 0, out pixmin, out _);
        pixa.AddPix(pixmin, L.L_COPY);
        rp.WritePixAndCheck(pixmin, L.IFF_PNG);  /* 14 */
        pixs3 = pixmin.SeedfillGrayBasin(pixm, 30, 4);
        pixs3_8 = pixmin.SeedfillGrayBasin(pixm, 30, 8);
        pixa.AddPix(pixs3, L.L_INSERT);
        rp.WritePixAndCheck(pixs3, L.IFF_PNG);  /* 15 */
        pixa.AddPix(pixs3_8, L.L_INSERT);
        rp.WritePixAndCheck(pixs3_8, L.IFF_PNG);  /* 15 */
        pixb3 = pixs3.ThresholdToBinary(60);
        rp.WritePixAndCheck(pixb3, L.IFF_PNG);  /* 17 */
        pixa.AddPix(pixb3, L.L_INSERT);
        pix1 = pixa.DisplayTiledInColumns(5, 1.0f, 15, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 18 */

            /* Compare hybrid and iterative gray seedfills */
        pixs1 = Pix.Copy(null, pixm);
        pixs2 = Pix.Copy(null, pixm);
        pixs1.AddConstantGray(-30);
        pixs2.AddConstantGray(60);

        PixTestEqual(rp, pixs1, pixs2, pixm, 1, 4);  /* 19 - 22 */
        PixTestEqual(rp, pixs1, pixs2, pixm, 2, 8);  /* 23 - 26 */
        PixTestEqual(rp, pixs2, pixs1, pixm, 3, 4);  /* 27 - 30 */
        PixTestEqual(rp, pixs2, pixs1, pixm, 4, 8);  /* 31 - 34 */

        Assert.Equal(34, rp.Index);
        rp.Cleanup();
    }

    private static void PixTestEqual(RegTest rp, Pix pixs1, Pix pixs2, Pix pixm,
                                     int set, int connectivity)
    {
        Pix pixc11, pixc12, pixc21, pixc22;

        pixc11 = Pix.Copy(null, pixs1);
        pixc12 = Pix.Copy(null, pixs1);
        pixc21 = Pix.Copy(null, pixs2);
        pixc22 = Pix.Copy(null, pixs2);

            /* Test inverse seed filling */
        pixc11.SeedfillGrayInv(pixm, connectivity);
        rp.WritePixAndCheck(pixc11, L.IFF_PNG);  /* '1' */
        pixc12.SeedfillGrayInvSimple(pixm, connectivity);
        rp.ComparePix(pixc11, pixc12);  /* '2' */

            /* Test seed filling */
        pixc21.SeedfillGray(pixm, connectivity);
        rp.WritePixAndCheck(pixc21, L.IFF_PNG);  /* '3' */
        pixc22.SeedfillGraySimple(pixm, connectivity);
        rp.ComparePix(pixc21, pixc22);  /* '4' */
    }
}
