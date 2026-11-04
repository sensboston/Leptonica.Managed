using System.IO;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// numa2_reg.c: numa windowed stats, numa extraction from pix on a line,
/// and pixel averages and variances.
/// </summary>
public class Numa2RegTests
{
    // NOTE: the C goldens were made without gnuplot, so the 9 checks of the plots made
    // by gplotSimple1() (C indices 0 - 4 and 6 - 9) did not count there.  The checks
    // that remain keep their C index comments; the golden file index is the C index
    // minus the number of those checks before it, so the last index is 33 (C: 42).

    [RegFact]
    public void Numa2()
    {
        int i, j;
        int w, h, bw, bh, wpls, rval, gval, bval;
        uint pixel, avergb;
        uint[] datas;
        float sum1, sum2, ave1, ave2, ave3, ave4, diff1, diff2;
        float var1, var2, var3;
        Box box1, box2, box3;
        Numa na, na1, na2, na3, na4;
        Pix pixs, pix1, pix2, pix3, pix4, pix5, pix6, pixg;
        var rp = new RegTest("numa2");

        Utils.LeptMkdir("lept/numa2");

        /* -------------------------------------------------------------------*
         *                         Numa-windowed stats                        *
         * -------------------------------------------------------------------*/
        na = Numa.Read(Path.Combine(TestPaths.ProgDir, "lyra.5.na"));
        na.WindowedStats(5, out na1, out na2, out na3, out na4);

        /* -------------------------------------------------------------------*
         *                        Extraction on a line                        *
         * -------------------------------------------------------------------*/
            /* First, make a pretty image */
        w = h = 200;
        pixs = Pix.Create(w, h, 32);
        wpls = pixs.Wpl;
        datas = pixs.Data;
        for (i = 0; i < 200; i++) {
            int lines = i * wpls;
            for (j = 0; j < 200; j++) {
                rval = (int)((255.0 * j) / w + (255.0 * i) / h);
                gval = (int)((255.0 * 2 * j) / w + (255.0 * 2 * i) / h) % 255;
                bval = (int)((255.0 * 4 * j) / w + (255.0 * 4 * i) / h) % 255;
                Pix.ComposeRGBPixel(rval, gval, bval, out pixel);
                datas[lines + j] = pixel;
            }
        }
        pixg = pixs.ConvertTo8(false);  /* and a grayscale version */
        rp.WritePixAndCheck(pixg, L.IFF_PNG);  /* 5 */

        na1 = pixg.ExtractOnLine(20, 20, 180, 20, 1);
        na2 = pixg.ExtractOnLine(40, 30, 40, 170, 1);
        na3 = pixg.ExtractOnLine(20, 170, 180, 30, 1);
        na4 = pixg.ExtractOnLine(20, 190, 180, 10, 1);

        /* -------------------------------------------------------------------*
         *                     Row and column pixel sums                      *
         * -------------------------------------------------------------------*/
            /* Sum by columns in two halves (left and right) */
        pixs = TestPaths.ReadInput("test8.jpg");
        pixs.GetDimensions(out w, out h, out _);
        box1 = Box.Create(0, 0, w / 2, h);
        box2 = Box.Create(w / 2, 0, w - 2 / 2, h);
        na1 = pixs.AverageByColumn(box1, L.L_BLACK_IS_MAX);
        na2 = pixs.AverageByColumn(box2, L.L_BLACK_IS_MAX);
        na1.Join(na2, 0, -1);
        na3 = pixs.AverageByColumn(null, L.L_BLACK_IS_MAX);
        bool same = na1.Similar(na3, 0.0f);  /* for columns */
        rp.CompareValues(1, same ? 1 : 0, 0);  /* 10 */
        pix1 = pixs.ConvertTo32();
        Pix.RenderPlotFromNumaGen(ref pix1, na3, L.L_HORIZONTAL_LINE, 3, h / 2, 80, 1,
                                  0xff000000);
        Pix.RenderPlotFromNuma(ref pix1, na3, L.L_PLOT_AT_BOT, 3, 80, 0xff000000);

            /* Sum by rows in two halves (top and bottom) */
        box1 = Box.Create(0, 0, w, h / 2);
        box2 = Box.Create(0, h / 2, w, h - h / 2);
        na1 = pixs.AverageByRow(box1, L.L_WHITE_IS_MAX);
        na2 = pixs.AverageByRow(box2, L.L_WHITE_IS_MAX);
        na1.Join(na2, 0, -1);
        na3 = pixs.AverageByRow(null, L.L_WHITE_IS_MAX);
        same = na1.Similar(na3, 0.0f);  /* for rows */
        rp.CompareValues(1, same ? 1 : 0, 0);  /* 11 */
        Pix.RenderPlotFromNumaGen(ref pix1, na3, L.L_VERTICAL_LINE, 3, w / 2, 80, 1,
                                  0x00ff0000);
        Pix.RenderPlotFromNuma(ref pix1, na3, L.L_PLOT_AT_RIGHT, 3, 80, 0x00ff0000);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 12 */

            /* Average left by rows; right by columns; compare totals */
        box1 = Box.Create(0, 0, w / 2, h);
        box2 = Box.Create(w / 2, 0, w - 2 / 2, h);
        na1 = pixs.AverageByRow(box1, L.L_WHITE_IS_MAX);
        na2 = pixs.AverageByColumn(box2, L.L_WHITE_IS_MAX);
        sum1 = na1.GetSum();  /* sum of averages of left box */
        sum2 = na2.GetSum();  /* sum of averages of right box */
        ave1 = sum1 / h;
        ave2 = 2.0f * sum2 / w;
        ave3 = 0.5f * (ave1 + ave2);  /* average over both halves */
        rp.CompareValues(189.59f, ave1, 0.01f);  /* 13 */
        rp.CompareValues(207.89f, ave2, 0.01f);  /* 14 */

        pixs.AverageInRect(null, null, 0, 255, 1, out ave4);  /* entire image */
        diff1 = ave4 - ave3;
        diff2 = w * h * ave4 - (0.5f * w * sum1 + h * sum2);
        rp.CompareValues(0.0f, diff1, 0.001f);  /* 15 */
        rp.CompareValues(10.0f, diff2, 10.0f);  /* 16 */

            /* Variance left and right halves.  Variance doesn't average
             * in a simple way, unlike pixel sums. */
        var1 = pixs.VarianceInRect(box1);  /* entire image */
        var2 = pixs.VarianceInRect(box2);  /* entire image */
        var3 = pixs.VarianceInRect(null);  /* entire image */
        rp.CompareValues(82.06f, 0.5f * (var1 + var2), 0.01f);  /* 17 */
        rp.CompareValues(82.66f, var3, 0.01f);  /* 18 */

        /* -------------------------------------------------------------------*
         *                     Row and column variances                       *
         * -------------------------------------------------------------------*/
            /* Display variance by rows and columns */
        box1 = Box.Create(415, 0, 130, 425);
        box1.GetGeometry(out _, out _, out bw, out bh);
        na1 = pixs.VarianceByRow(box1);
        na2 = pixs.VarianceByColumn(box1);
        pix1 = pixs.ConvertTo32();
        pix2 = Pix.Copy(null, pix1);
        Pix.RenderPlotFromNumaGen(ref pix1, na1, L.L_VERTICAL_LINE, 3, 415, 100, 1,
                                  0xff000000);
        Pix.RenderPlotFromNumaGen(ref pix1, na2, L.L_HORIZONTAL_LINE, 3, bh / 2, 100, 1,
                                  0x00ff0000);
        Pix.RenderPlotFromNuma(ref pix2, na1, L.L_PLOT_AT_LEFT, 3, 60, 0x00ff0000);
        Pix.RenderPlotFromNuma(ref pix2, na1, L.L_PLOT_AT_MID_VERT, 3, 60, 0x0000ff00);
        Pix.RenderPlotFromNuma(ref pix2, na1, L.L_PLOT_AT_RIGHT, 3, 60, 0xff000000);
        Pix.RenderPlotFromNuma(ref pix2, na2, L.L_PLOT_AT_TOP, 3, 60, 0x0000ff00);
        Pix.RenderPlotFromNuma(ref pix2, na2, L.L_PLOT_AT_MID_HORIZ, 3, 60, 0xff000000);
        Pix.RenderPlotFromNuma(ref pix2, na2, L.L_PLOT_AT_BOT, 3, 60, 0x00ff0000);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 19 */
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 20 */

            /* Again on a different image */
        pix1 = TestPaths.ReadInput("boxedpage.jpg");
        pix2 = pix1.ConvertTo8(false);
        pix2.GetDimensions(out w, out h, out _);
        na1 = pix2.VarianceByRow(null);
        pix3 = pix1.ConvertTo32();
        Pix.RenderPlotFromNumaGen(ref pix3, na1, L.L_VERTICAL_LINE, 3, 0, 70, 1,
                                  0xff000000);
        na2 = pix2.VarianceByColumn(null);
        Pix.RenderPlotFromNumaGen(ref pix3, na2, L.L_HORIZONTAL_LINE, 3, bh - 1, 70, 1,
                                  0x00ff0000);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 21 */

            /* Again, with an erosion */
        pix4 = pix2.ErodeGray(3, 21);
        na1 = pix4.VarianceByRow(null);
        pix5 = pix1.ConvertTo32();
        Pix.RenderPlotFromNumaGen(ref pix5, na1, L.L_VERTICAL_LINE, 3, 30, 70, 1,
                                  0xff000000);
        na2 = pix4.VarianceByColumn(null);
        Pix.RenderPlotFromNumaGen(ref pix5, na2, L.L_HORIZONTAL_LINE, 3, bh - 1, 70, 1,
                                  0x00ff0000);
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 22 */

        /* -------------------------------------------------------------------*
         *                    Windowed variance along a line                  *
         * -------------------------------------------------------------------*/
        pix1 = TestPaths.ReadInput("boxedpage.jpg");
        pix2 = pix1.ConvertTo8(false);
        pix2.GetDimensions(out w, out h, out _);
        pix3 = Pix.Copy(null, pix1);

            /* Plot along horizontal line */
        na1 = pix2.WindowedVarianceOnLine(L.L_HORIZONTAL_LINE, h / 2 - 30, 0, w, 5);
        Pix.RenderPlotFromNumaGen(ref pix1, na1, L.L_HORIZONTAL_LINE, 3, h / 2 - 30,
                                  80, 1, 0xff000000);
        Pix.RenderPlotFromNuma(ref pix3, na1, L.L_PLOT_AT_TOP, 3, 60, 0x00ff0000);
        Pix.RenderPlotFromNuma(ref pix3, na1, L.L_PLOT_AT_BOT, 3, 60, 0x0000ff00);

            /* Plot along vertical line */
        na2 = pix2.WindowedVarianceOnLine(L.L_VERTICAL_LINE, (int)(0.78 * w), 0, h, 5);
        Pix.RenderPlotFromNumaGen(ref pix1, na2, L.L_VERTICAL_LINE, 3, (int)(0.78 * w), 60,
                                  1, 0x00ff0000);
        Pix.RenderPlotFromNuma(ref pix3, na2, L.L_PLOT_AT_LEFT, 3, 60, 0xff000000);
        Pix.RenderPlotFromNuma(ref pix3, na2, L.L_PLOT_AT_RIGHT, 3, 60, 0x00ff0000);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 23 */
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 24 */

        /* -------------------------------------------------------------------*
         *               Test pixel average function for gray                 *
         * -------------------------------------------------------------------*/
        pix1 = TestPaths.ReadInput("lyra.005.jpg");
        pix2 = pix1.ConvertRGBToLuminance();
        box1 = Box.Create(20, 150, 700, 515);
        pix3 = pix2.ClipRectangle(box1, out _)!;
            /* No mask, no box, different subsampling */
        pix3.AverageInRect(null, null, 0, 255, 1, out ave1);  /* no mask */
        rp.CompareValues(176.97f, ave1, 0.1f);  /* 25 */
        pix3.AverageInRect(null, null, 0, 255, 2, out ave2);  /* no mask */
        rp.CompareValues(ave1, ave2, 0.1f);  /* 26 */
            /* Mask allows bg, no box */
        pix4 = pix3.ThresholdToBinary(80);  /* use light pixels */
        pix3.AverageInRect(pix4, null, 0, 255, 1, out ave1);  /* mask bg */
        rp.CompareValues(187.58f, ave1, 0.1f);  /* 27 */
            /* Mask allows fg, no box */
        Pix.Invert(pix4, pix4);  /* use dark pixels */
        pix3.AverageInRect(pix4, null, 0, 255, 1, out ave1);  /* mask fg */
        rp.CompareValues(46.37f, ave1, 0.1f);  /* 28 */
            /* Mask allows fg, no box, restricted range with samples */
        pix3.AverageInRect(pix4, null, 50, 60, 1, out ave1);  /* mask fg */
        rp.CompareValues(55.18f, ave1, 0.1f);  /* 29 */
            /* Mask allows fg, no box, restricted range without samples */
        pix3.AverageInRect(pix4, null, 100, 255, 1, out ave1);
        rp.CompareValues(0.0f, ave1, 0.0f);  /* 30 */  /* mask fg */
            /* No mask, use box */
        box2 = Box.Create(100, 100, 200, 150);
        pix3.AverageInRect(null, box2, 0, 255, 1, out ave1);  /* no mask */
        rp.CompareValues(165.63f, ave1, 0.1f);  /* 31 */
            /* No mask, pix cropped to box */
        Pix.Invert(pix4, pix4);  /* use light pixels */
        pix5 = pix3.ClipRectangle(box2, out _)!;
        pix5.AverageInRect(null, null, 0, 255, 1, out ave2);  /* no mask */
        rp.CompareValues(ave1, ave2, 0.1f);  /* 32 */
            /* Mask allows bg, use box */
        pix3.AverageInRect(pix4, box2, 0, 255, 1, out ave1);  /* mask bg */
        rp.CompareValues(175.65f, ave1, 0.1f);  /* 33 */
            /* Cropped mask allows bg, pix cropped to box */
        pix6 = pix5.ThresholdToBinary(80);  /* use light pixels */
        pix5.AverageInRect(pix6, null, 0, 255, 1, out ave2);
        rp.CompareValues(ave1, ave2, 0.1f);  /* 34 */
            /* Mask allows bg, use box, restricted range */
        pix3.AverageInRect(pix4, box2, 100, 125, 1, out ave1);
        rp.CompareValues(112.20f, ave1, 0.1f);  /* 35 */
            /* Cropped mask allows bg, pix cropped to box, restricted range */
        pix5.AverageInRect(pix6, null, 100, 125, 1, out ave2);
        rp.CompareValues(ave1, ave2, 0.1f);  /* 36 */

        /* -------------------------------------------------------------------*
         *               Test pixel average function for color                *
         * -------------------------------------------------------------------*/
        pix1 = TestPaths.ReadInput("lyra.005.jpg");
        box1 = Box.Create(20, 150, 700, 515);
        pix2 = pix1.ClipRectangle(box1, out _)!;
            /* No mask, no box, different subsampling */
        pix2.AverageInRectRGB(null, null, 1, out avergb);
        rp.CompareValues(0xc7b09000u, avergb, 0);  /* 37 */
        pix2.AverageInRectRGB(null, null, 10, out avergb);
        rp.CompareValues(0xc7af8f00u, avergb, 0);  /* 38 */
            /* Mask allows bg, no box */
        pix3 = pix2.ConvertTo1(128);  /* use light pixels */
        pix2.AverageInRectRGB(pix3, null, 1, out avergb);
        rp.CompareValues(0xd5bf9d00u, avergb, 0);  /* 39 */
            /* Mask allows fg, no box */
        Pix.Invert(pix3, pix3);  /* use dark pixels */
        pix2.AverageInRectRGB(pix3, null, 1, out avergb);
        rp.CompareValues(0x5c3b2800u, avergb, 0);  /* 40 */
            /* Mask allows bg, box at lower left */
        Pix.Invert(pix3, pix3);  /* use light pixels */
        box2 = Box.Create(20, 400, 100, 100);
        pix2.AverageInRectRGB(pix3, box2, 1, out avergb);
        rp.CompareValues(0xbba48500u, avergb, 0);  /* 41 */
            /* Mask allows bg, box at upper right */
        box3 = Box.Create(600, 20, 100, 100);
        pix2.AverageInRectRGB(pix3, box3, 1, out avergb);
        rp.CompareValues(0xfdfddd00u, avergb, 0);  /* 42 */

        Assert.Equal(33, rp.Index);
        rp.Cleanup();
    }
}
