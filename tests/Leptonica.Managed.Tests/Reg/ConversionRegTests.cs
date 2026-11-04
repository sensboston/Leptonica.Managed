using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// conversion_reg.c: depth conversion functions, including some of the octcube
/// quantization.
/// </summary>
public class ConversionRegTests
{
    [RegFact]
    public void Conversion()
    {
        Pix pixs1, pixs2, pixs4, pixs8, pixs16, pixs32;
        Pix pixc2, pixc4, pixc8;
        Pix pix1, pix2, pix3, pix4, pix5, pix6;
        Pixa pixa;
        PixColormap cmap;
        var rp = new RegTest("conversion");

        pixs1 = TestPaths.ReadInput("test1.png");
        pixs2 = TestPaths.ReadInput("dreyfus2.png");
        pixc2 = TestPaths.ReadInput("weasel2.4c.png");
        pixs4 = TestPaths.ReadInput("weasel4.16g.png");
        pixc4 = TestPaths.ReadInput("weasel4.11c.png");
        pixs8 = TestPaths.ReadInput("karen8.jpg");
        pixc8 = TestPaths.ReadInput("weasel8.240c.png");
        pixs16 = TestPaths.ReadInput16("test16.tif");
        pixs32 = TestPaths.ReadInput("marge.jpg");

            /* Conversion: 1 bpp --> 8 bpp --> 1 bpp */
        pix1 = pixs1.ConvertTo8(false);
        pix2 = pix1.Threshold8(1, 0, 0);
        rp.ComparePix(pixs1, pix2);  /* 0 */

            /* Conversion: 2 bpp --> 8 bpp --> 2 bpp */
            /* Conversion: 2 bpp cmap --> 8 bpp cmap --> 2 bpp cmap */
        pix1 = pixs2.RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        pix2 = pix1.Threshold8(2, 4, 0);
        pix3 = pix2.ConvertTo8(false);
        pix4 = pix3.Threshold8(2, 4, 0);
        rp.ComparePix(pix2, pix4);  /* 1 */
        pix5 = pixs2.ConvertTo8(true);
        pix6 = pix5.Threshold8(2, 4, 1);
        rp.ComparePix(pixs2, pix6);  /* 2 */

            /* Conversion: 4 bpp --> 8 bpp --> 4 bpp */
            /* Conversion: 4 bpp cmap --> 8 bpp cmap --> 4 bpp cmap */
        pix1 = pixs4.RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        pix2 = pix1.Threshold8(4, 16, 0);
        pix3 = pix2.ConvertTo8(false);
        pix4 = pix3.Threshold8(4, 16, 0);
        rp.ComparePix(pix2, pix4);  /* 3 */
        pix5 = pixs4.ConvertTo8(true);
        pix6 = pix5.Threshold8(4, 16, 1);
        rp.ComparePix(pixs4, pix6);  /* 4 */

            /* Conversion: 2 bpp cmap --> 2 bpp --> 2 bpp cmap --> 2 bpp */
        pix1 = pixs2.RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        pix2 = pix1.ConvertGrayToColormap();
        pix3 = pix2.RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        pix4 = pix3.ThresholdTo2bpp(4, true);
        rp.ComparePix(pix1, pix4);  /* 5 */

            /* Conversion: 4 bpp cmap --> 4 bpp --> 4 bpp cmap --> 4 bpp */
        pix1 = pixs4.RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        pix2 = pix1.ConvertGrayToColormap();
        pix3 = pix2.RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        pix4 = pix3.ThresholdTo4bpp(16, true);
        rp.ComparePix(pix1, pix4);  /* 6 */

            /* Conversion: 8 bpp --> 8 bpp cmap --> 8 bpp */
        pix1 = pixs8.ConvertTo8(true);
        pix2 = pix1.ConvertTo8(false);
        rp.ComparePix(pixs8, pix2);  /* 7 */

            /* Conversion: 2 bpp cmap --> 32 bpp --> 2 bpp cmap */
        pix1 = pixc2.ConvertTo8(true);
        pix2 = pix1.ConvertTo32();
        pix3 = pixc2.ConvertTo32();
        rp.ComparePix(pix2, pix3);  /* 8 */
        cmap = pixc2.Colormap!;
        pix4 = pix3.OctcubeQuantFromCmap(cmap, 2, 4, L.L_EUCLIDEAN_DISTANCE);
        rp.ComparePix(pixc2, pix4);  /* 9 */

            /* Conversion: 4 bpp cmap --> 32 bpp --> 4 bpp cmap */
        pix1 = pixc4.ConvertTo8(true);
        pix2 = pix1.ConvertTo32();
        pix3 = pixc4.ConvertTo32();
        rp.ComparePix(pix2, pix3);  /* 10 */
        cmap = pixc4.Colormap!;
        pix4 = pix3.OctcubeQuantFromCmap(cmap, 2, 4, L.L_EUCLIDEAN_DISTANCE);
        rp.ComparePix(pixc4, pix4);  /* 11 */

            /* Conversion: 8 bpp --> 32 bpp --> 8 bpp */
        pix1 = pixs8.ConvertTo32();
        pix2 = pix1.ConvertTo8(false);
        rp.ComparePix(pixs8, pix2);  /* 12 */

            /* Conversion: 8 bpp --> 16 bpp --> 8 bpp */
        pix1 = pixs8.Convert8To16(8);
        pix2 = pix1.ConvertTo8(false);
        rp.ComparePix(pixs8, pix2);  /* 13 */

            /* Conversion: 16 bpp --> 8 bpp --> 16 bpp */
        pix1 = pixs16.Convert16To8(1);
        pix2 = pix1.ConvertTo16();
        rp.ComparePix(pixs16, pix2);  /* 14 */

            /* Conversion: 8 bpp cmap --> 32 bpp --> 8 bpp cmap */
            /* Required to go to level 6 of octcube to get identical result */
        pix1 = pixc8.ConvertTo32();
        cmap = pixc8.Colormap!;
        pix2 = pix1.OctcubeQuantFromCmap(cmap, 2, 6, L.L_EUCLIDEAN_DISTANCE);
        rp.ComparePix(pixc8, pix2);  /* 15 */

            /* General onversion to 2 bpp */
        pixa = Pixa.Create(0);
        pix1 = pixs1.ConvertTo2();
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 16 */
        pixa.AddPix(pix1, L.L_INSERT);
        pix1 = pixs2.ConvertTo2();
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 17 */
        pixa.AddPix(pix1, L.L_INSERT);
        pix1 = pixc2.ConvertTo2();
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 18 */
        pixa.AddPix(pix1, L.L_INSERT);
        pix1 = pixs4.ConvertTo2();
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 19 */
        pixa.AddPix(pix1, L.L_INSERT);
        pix1 = pixc4.ConvertTo2();
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 20 */
        pixa.AddPix(pix1, L.L_INSERT);
        pix1 = pixs8.ConvertTo2();
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 21 */
        pixa.AddPix(pix1, L.L_INSERT);
        pix1 = pixc8.ConvertTo2();
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 22 */
        pixa.AddPix(pix1, L.L_INSERT);
        pix1 = pixs32.ConvertTo2();
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 23 */
        pixa.AddPix(pix1, L.L_INSERT);

            /* General onversion to 4 bpp */
        pixa = Pixa.Create(0);
        pix1 = pixs1.ConvertTo4();
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 24 */
        pixa.AddPix(pix1, L.L_INSERT);
        pix1 = pixs2.ConvertTo4();
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 25 */
        pixa.AddPix(pix1, L.L_INSERT);
        pix1 = pixc2.ConvertTo4();
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 26 */
        pixa.AddPix(pix1, L.L_INSERT);
        pix1 = pixs4.ConvertTo4();
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 27 */
        pixa.AddPix(pix1, L.L_INSERT);
        pix1 = pixc4.ConvertTo4();
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 28 */
        pixa.AddPix(pix1, L.L_INSERT);
        pix1 = pixs8.ConvertTo4();
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 29 */
        pixa.AddPix(pix1, L.L_INSERT);
        pix1 = pixc8.ConvertTo4();
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 30 */
        pixa.AddPix(pix1, L.L_INSERT);
        pix1 = pixs32.ConvertTo4();
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 31 */
        pixa.AddPix(pix1, L.L_INSERT);

        Assert.Equal(31, rp.Index);
        rp.Cleanup();
    }
}
