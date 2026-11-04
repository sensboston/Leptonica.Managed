using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// edge_reg.c (edgetest.c): sobel edge filter.
/// </summary>
public class EdgeRegTests
{
    [RegFact]
    public void Edge()
    {
        Pix pixs, pix1, pix2, pix3, pix4;
        var rp = new RegTest("edge");

        pixs = TestPaths.ReadInput("test8.jpg");

            /* Horiz and vert sobel edges (1 bpp) */
        pix1 = pixs.SobelEdgeFilter(L.L_HORIZONTAL_EDGES);
        pix2 = pix1.ThresholdToBinary(60);
        Pix.Invert(pix2, pix2);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 0 */
        pix3 = pixs.SobelEdgeFilter(L.L_VERTICAL_EDGES);
        pix4 = pix3.ThresholdToBinary(60);
        Pix.Invert(pix4, pix4);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 1 */
        Pix.Or(pix4, pix4, pix2);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 2 */

            /* Horizontal and vertical sobel edges (8 bpp) */
        Pix.MinOrMax(pix1, pix1, pix3, L.L_CHOOSE_MAX);
        Pix.Invert(pix1, pix1);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 3 */

        Assert.Equal(3, rp.Index);
        rp.Cleanup();
    }
}
