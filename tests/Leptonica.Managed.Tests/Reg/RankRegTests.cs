using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// rank_reg.c: tests grayscale and color rank functions:
/// pixRankFilterGray, pixRankFilterRGB, pixScaleGrayRank2, pixScaleGrayRankCascade.
/// </summary>
public class RankRegTests
{
    [RegFact]
    public void Rank()
    {
        var rp = new RegTest("rank");

        Utils.LeptMkdir("lept/rank");

        Pix pixs = TestPaths.ReadInput("lucasta.150.jpg");
        pixs.GetDimensions(out int w, out int h, out _);

        Pix pix1 = pixs.RankFilterGray(15, 15, 0.4f);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0 */

            /* ---------- Compare grayscale morph with rank operator ---------- */
            /* Get results for dilation */
        pix1 = pixs.DilateGray(15, 15);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 1 */

            /* Get results for erosion */
        Pix pix2 = pixs.ErodeGray(15, 15);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 2 */

            /* Get results using the rank filter for rank = 0.0 and 1.0.
             * Don't use 0.0 or 1.0, because those are dispatched
             * automatically to erosion and dilation! */
        Pix pix3 = pixs.RankFilterGray(15, 15, 0.0001f);
        Pix pix4 = pixs.RankFilterGray(15, 15, 0.9999f);
        rp.ComparePix(pix1, pix4);  /* 3 */
        rp.ComparePix(pix2, pix3);  /* 4 */

            /* ------------- Timing and filter size experiments --------- */
        Box box = Box.Create(20, 200, 500, 125);
        Pix pix0 = pixs.ClipRectangle(box, out _)!;
        Numa nax = Numa.MakeSequence(1, 1, 20);
        Numa nay1 = Numa.Create(20);
        Numa nay2 = Numa.Create(20);
        Gplot gplot = Gplot.Create("/tmp/lept/rank/plots", L.GPLOT_PNG,
                                   "sec/MPix vs filter size", "size", "time");
        Pixa pixa = Pixa.Create(20);
        for (int i = 1; i <= 20; i++) {
            pix1 = pix0.RankFilterGray(i, 21, 0.5f);
            pix1 = pix0.RankFilterGray(21, i, 0.5f);
            pixa.AddPix(pix1, L.L_CLONE);
            nay1.AddNumber(1.0f);
            nay2.AddNumber(1.0f);
        }
        gplot.AddPlot(nax, nay1, L.GPLOT_LINES, "vertical");
        gplot.AddPlot(nax, nay2, L.GPLOT_LINES, "horizontal");
        gplot.MakeOutput();
        _ = pixa.DisplayTiledAndScaled(8, 250, 5, 0, 25, 2);

            /* ------------------     Gray tests    ------------------ */
        pixs = TestPaths.ReadInput("test8.jpg");
        pixa = Pixa.Create(4);
        for (int i = 1; i <= 4; i++) {
            pix1 = pixs.ScaleGrayRank2(i);
            pixa.AddPix(pix1, L.L_INSERT);
        }
        pix1 = pixa.DisplayTiledInRows(8, 1500, 1.0f, 0, 20, 2);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 5 */

        pixs = TestPaths.ReadInput("test24.jpg");
        pix1 = pixs.ConvertRGBToLuminance();
        pix2 = pix1.Scale(1.5f, 1.5f);
        pixa = Pixa.Create(5);
        for (int i = 1; i <= 4; i++) {
            for (int j = 1; j <= 4; j++) {
                pix3 = pix2.ScaleGrayRankCascade(i, j, 0, 0);
                pixa.AddPix(pix3, L.L_INSERT);
            }
        }
        pix4 = pixa.DisplayTiledInRows(8, 1500, 0.7f, 0, 20, 2);
        rp.WritePixAndCheck(pix4, L.IFF_JFIF_JPEG);  /* 6 */

            /* ---------- Compare color morph with rank operator ---------- */
        pixs = TestPaths.ReadInput("wyom.jpg");
        box = Box.Create(400, 220, 300, 250);
        pix0 = pixs.ClipRectangle(box, out _)!;
        pix1 = pix0.ColorMorph(L.L_MORPH_DILATE, 11, 11);
        pix2 = pix0.ColorMorph(L.L_MORPH_ERODE, 11, 11);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 7 */
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 8 */

            /* Get results using the rank filter for rank = 0.0 and 1.0. */
        pix3 = pix0.RankFilter(11, 11, 0.0001f);
        pix4 = pix0.RankFilter(11, 11, 0.9999f);
        rp.ComparePix(pix1, pix4);  /* 9 */
        rp.ComparePix(pix2, pix3);  /* 10 */

        Assert.Equal(10, rp.Index);
        rp.Cleanup();
    }
}
