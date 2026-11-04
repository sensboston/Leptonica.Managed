using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// quadtree_reg.c: tests quadtree region generation, statistics, and parent/child access.
/// </summary>
public class QuadTreeRegTests
{
    [RegFact]
    public void QuadTree()
    {
        var rp = new RegTest("quadtree");
        Utils.LeptMkdir("lept/quad");

            /* Test generation of quadtree regions */
        Boxaa baa = Boxaa.QuadtreeRegions(1000, 500, 3);
        byte[] data = baa.WriteMem();
        rp.WriteDataAndCheck(data, "baa");  /* 0 */

        baa = Boxaa.QuadtreeRegions(1001, 501, 3);
        data = baa.WriteMem();
        rp.WriteDataAndCheck(data, "baa");  /* 1 */

            /* Test quadtree stats generation */
        Pix pixs = TestPaths.ReadInput("rabi.png");
        Pix pixg = pixs.ScaleToGray4();
        FPixa fpixam = pixg.QuadtreeMean(8, null);
        Pix pix1 = fpixam.DisplayQuadtree(2, 10);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 2 */

        pixg.QuadtreeVariance(8, null, null, out FPixa fpixav, out FPixa fpixarv);
        Pix pix2 = fpixav.DisplayQuadtree(2, 10);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 3 */
        Pix pix3 = fpixarv.DisplayQuadtree(2, 10);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 4 */

            /* Compare with fixed-size tiling */
        Pix pix4 = pixg.GetAverageTiled(5, 6, L.L_MEAN_ABSVAL);
        Pix pix5 = pix4.ExpandReplicate(4);
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 5 */

        pix4 = pixg.GetAverageTiled(5, 6, L.L_STANDARD_DEVIATION);
        pix5 = pix4.ExpandReplicate(4);
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 6 */

            /* Test quadtree parent/child access */
        bool error = false;
        fpixam.GetFPixDimensions(4, out int w, out int h);
        for (int i = 0; i < w; i += 2) {
            for (int j = 0; j < h; j += 2) {
                Pix.QuadtreeGetParent(fpixam, 4, j, i, out float val1);
                fpixam.GetPixel(3, j / 2, i / 2, out float val2);
                if (val1 != val2) error = true;
            }
        }
        rp.CompareValues(0, error ? 1 : 0, 0.0f);  /* 7 */

        error = false;
        for (int i = 0; i < w; i++) {
            for (int j = 0; j < h; j++) {
                Pix.QuadtreeGetChildren(fpixam, 4, j, i,
                                        out float val00, out float val10,
                                        out float val01, out float val11);
                fpixam.GetPixel(5, 2 * j, 2 * i, out float valc00);
                fpixam.GetPixel(5, 2 * j + 1, 2 * i, out float valc10);
                fpixam.GetPixel(5, 2 * j, 2 * i + 1, out float valc01);
                fpixam.GetPixel(5, 2 * j + 1, 2 * i + 1, out float valc11);
                if (val00 != valc00 || val10 != valc10 ||
                    val01 != valc01 || val11 != valc11)
                    error = true;
            }
        }
        rp.CompareValues(0, error ? 1 : 0, 0.0f);  /* 8 */

        Assert.Equal(8, rp.Index);
        rp.Cleanup();
    }
}
