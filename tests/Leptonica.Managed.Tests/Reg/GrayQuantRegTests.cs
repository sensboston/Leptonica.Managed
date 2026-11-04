using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// grayquant_reg.c: gray thresholding to 1, 2 and 4 bpp, with and without colormaps.
/// </summary>
public class GrayQuantRegTests
{
    private const int THRESHOLD = 130;
        /* nlevels for 4 bpp output; anything between 2 and 16 is allowed */
    private const int NLEVELS = 4;

    [RegFact]
    public void GrayQuant()
    {
        string str;
        int index, w, h;
        Box box;
        Pix pixs, pix1, pix2, pix3, pix4, pix5, pix6;
        Pixa pixa;
        PixColormap cmap;
        var rp = new RegTest("grayquant");

        /* ------------------------------------------------------------- */

        pixs = TestPaths.ReadInput("test8.jpg");
        pixa = Pixa.Create(0);
        pixa.AddPix(pixs, L.L_INSERT);

            /* threshold to 1 bpp */
        pix1 = pixs.ThresholdToBinary(THRESHOLD);
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0 */

            /* dither to 2 bpp, with and without colormap */
        pix1 = pixs.DitherTo2bpp(true);
        pix2 = pixs.DitherTo2bpp(false);
        pix3 = pix2.ConvertGrayToColormap();
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 1 */
        pixa.AddPix(pix2, L.L_INSERT);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 2 */
        pixa.AddPix(pix3, L.L_INSERT);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 3 */
        rp.ComparePix(pix1, pix3);      /* 4 */

            /* threshold to 2 bpp, with and without colormap */
        pix1 = pixs.ThresholdTo2bpp(4, true);
        pix2 = pixs.ThresholdTo2bpp(4, false);
        pix3 = pix2.ConvertGrayToColormap();
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 5 */
        pixa.AddPix(pix2, L.L_INSERT);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 6 */
        pixa.AddPix(pix3, L.L_INSERT);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 7 */
        rp.ComparePix(pix1, pix3);      /* 8 */

        pix1 = pixs.ThresholdTo2bpp(3, true);
        pix2 = pixs.ThresholdTo2bpp(3, false);
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 9 */
        pixa.AddPix(pix2, L.L_INSERT);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 10 */

            /* threshold to 4 bpp, with and without colormap */
        pix1 = pixs.ThresholdTo4bpp(9, true);
        pix2 = pixs.ThresholdTo4bpp(9, false);
        pix3 = pix2.ConvertGrayToColormap();
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 11 */
        pixa.AddPix(pix2, L.L_INSERT);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 12 */
        pixa.AddPix(pix3, L.L_INSERT);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 13 */

            /* threshold on 8 bpp, with and without colormap */
        pix1 = pixs.ThresholdOn8bpp(9, true);
        pix2 = pixs.ThresholdOn8bpp(9, false);
        pix3 = pix2.ConvertGrayToColormap();
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 14 */
        pixa.AddPix(pix2, L.L_INSERT);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 15 */
        pixa.AddPix(pix3, L.L_INSERT);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 16 */
        rp.ComparePix(pix1, pix3);      /* 17 */

        /* ------------------------------------------------------------- */

        pixs = TestPaths.ReadInput("test8.jpg");
        pixa = Pixa.Create(0);
        pixa.AddPix(pixs, L.L_INSERT);

            /* Highlight 2 bpp with colormap */
        pix1 = pixs.ThresholdTo2bpp(3, true);
        pixa.AddPix(pix1, L.L_COPY);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 18 */
        cmap = pix1.Colormap!;
        box = Box.Create(278, 35, 122, 50);
        pix1.SetSelectCmap(box, 2, 255, 255, 100);
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 19 */

            /* Test pixThreshold8() */
        pix1 = pixs.Threshold8(1, 2, 1);  /* cmap */
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 20 */

        pix1 = pixs.Threshold8(1, 2, 0);  /* no cmap */
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 21 */

        pix1 = pixs.Threshold8(2, 3, 1);  /* highlight one box */
        box = Box.Create(278, 35, 122, 50);
        pix1.SetSelectCmap(box, 2, 255, 255, 100);
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 22 */
        cmap = pix1.Colormap!;


        pix1 = pixs.Threshold8(2, 4, 0);  /* no cmap */
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 23 */

        pix1 = pixs.Threshold8(4, 6, 1);  /* highlight one box */
        box = Box.Create(278, 35, 122, 50);
        pix1.SetSelectCmap(box, 5, 255, 255, 100);
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 24 */
        cmap = pix1.Colormap!;

        pix1 = pixs.Threshold8(4, 6, 0);  /* no cmap */
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 25 */

            /* Highlight 4 bpp with 2 colormap entries */
            /* Note: We use 5 levels (0-4) for gray.           */
            /*       5 & 6 are used for highlight color.       */
        pix1 = pixs.ThresholdTo4bpp(5, true);
        cmap = pix1.Colormap!;
        cmap.GetIndex(255, 255, 255, out index);
        box = Box.Create(278, 35, 122, 50);
        pix1.SetSelectCmap(box, index, 255, 255, 100);  /* use index 5 */
        pixa.AddPix(pix1, L.L_COPY);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 26 */
        box = Box.Create(4, 6, 157, 33);
        pix1.SetSelectCmap(box, index, 100, 255, 255);  /* use index 6 */
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 27 */

        /* ------------------------------------------------------------- */

        pixs = TestPaths.ReadInput("feyn.tif");
        pixa = Pixa.Create(0);

            /* Comparison 8 bpp jpeg with 2 bpp (highlight) */
        pix1 = pixs.ScaleToGray4();
        pix2 = pixs.ReduceRankBinaryCascade(2, 2, 0, 0);
        pix3 = pix1.ThresholdTo2bpp(3, true);
        box = Box.Create(175, 208, 228, 88);
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 28 */
        pixa.AddPix(pix2, L.L_INSERT);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 29 */
        pix3.SetSelectCmap(box, 2, 255, 255, 100);
        pixa.AddPix(pix3, L.L_INSERT);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 30 */
        cmap = pix3.Colormap!;

            /* Thresholding to 4 bpp (highlight); use pix1 from above */
        pix2 = pix1.ThresholdTo4bpp(NLEVELS, true);
        box = Box.Create(175, 208, 228, 83);
        pix2.SetSelectCmap(box, NLEVELS - 1, 255, 255, 100);
        box = Box.Create(232, 298, 110, 25);
        pix2.SetSelectCmap(box, NLEVELS - 1, 100, 255, 255);
        box = Box.Create(21, 698, 246, 82);
        pix2.SetSelectCmap(box, NLEVELS - 1, 225, 100, 255);
        pixa.AddPix(pix2, L.L_INSERT);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 31 */
        cmap = pix2.Colormap!;
        pix3 = pixs.ReduceRankBinaryCascade(2, 2, 0, 0);
        pixa.AddPix(pix3, L.L_INSERT);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 32 */

           /* Thresholding to 4 bpp at 2, 3, 4, 5 and 6 levels */
        box = Box.Create(25, 202, 136, 37);
        pix2 = pix1.ClipRectangle(box, out _)!;
        pix3 = pix2.Scale(6.0f, 6.0f);
        pixa.AddPix(pix3, L.L_INSERT);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 33 */
        pix3.GetDimensions(out w, out h, out _);
        pix4 = Pix.Create(w, 6 * h, 8);
        pix4.Rasterop(0, 0, w, h, L.PIX_SRC, pix3, 0, 0);

        pix5 = pix3.ThresholdTo4bpp(6, true);
        pix6 = pix5.RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        pix4.Rasterop(0, h, w, h, L.PIX_SRC, pix6, 0, 0);
        pixa.AddPix(pix5, L.L_INSERT);
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 34 */

        pix5 = pix3.ThresholdTo4bpp(5, true);
        pix6 = pix5.RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        pix4.Rasterop(0, 2 * h, w, h, L.PIX_SRC, pix6, 0, 0);
        pixa.AddPix(pix5, L.L_INSERT);
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 35 */

        pix5 = pix3.ThresholdTo4bpp(4, true);
        pix6 = pix5.RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        pix4.Rasterop(0, 3 * h, w, h, L.PIX_SRC, pix6, 0, 0);
        pixa.AddPix(pix5, L.L_INSERT);
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 36 */

        pix5 = pix3.ThresholdTo4bpp(3, true);
        pix6 = pix5.RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        pix4.Rasterop(0, 4 * h, w, h, L.PIX_SRC, pix6, 0, 0);
        pixa.AddPix(pix5, L.L_INSERT);
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 37 */

        pix5 = pix3.ThresholdTo4bpp(2, true);
        pix6 = pix5.RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        pix4.Rasterop(0, 5 * h, w, h, L.PIX_SRC, pix6, 0, 0);
        pixa.AddPix(pix5, L.L_INSERT);
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 38 */
        pixa.AddPix(pix4, L.L_INSERT);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 39 */

        /* ------------------------------------------------------------- */

           /* Thresholding with fixed and arbitrary bin boundaries */
        pixs = TestPaths.ReadInput("stampede2.jpg");
        pixa = Pixa.Create(0);

        pixa.AddPix(pixs, L.L_INSERT);
        rp.WritePixAndCheck(pixs, L.IFF_PNG);  /* 40 */
        pix1 = pixs.ThresholdTo4bpp(5, true);
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 41 */
        pix1 = pixs.ThresholdTo4bpp(7, true);
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 42 */
        cmap = pix1.Colormap!;
        pix1 = pixs.ThresholdTo4bpp(11, true);
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 43 */

        str = "45 75 115 185";
        pix1 = pixs.ThresholdGrayArb(str, 8, false, false, false);
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 44 */
        str = "38 65 85 115 160 210";
        pix1 = pixs.ThresholdGrayArb(str, 8, false, true, true);
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 45 */
        cmap = pix1.Colormap!;
        str = "38 60 75 90 110 130 155 185 208 239";
        pix1 = pixs.ThresholdGrayArb(str, 8, false, false, false);
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 46 */
        str = "45 75 115 185";
        pix1 = pixs.ThresholdGrayArb(str, 0, true, false, true);
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 47 */
        str = "38 65 85 115 160 210";
        pix1 = pixs.ThresholdGrayArb(str, 0, true, false, true);
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 48 */
        cmap = pix1.Colormap!;
        str = "38 60 75 90 110 130 155 185 208 239";
        pix1 = pixs.ThresholdGrayArb(str, 4, true, false, true);
        pixa.AddPix(pix1, L.L_INSERT);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 49 */

        Assert.Equal(49, rp.Index);
        rp.Cleanup();
    }
}
