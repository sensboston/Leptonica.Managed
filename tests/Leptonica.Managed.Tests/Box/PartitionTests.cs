using Leptonica;
using Leptonica.Managed.Tests.Reg;
using Xunit;

namespace Leptonica.Managed.Tests.BoxTests;

public class PartitionTests
{
    [RegFact]
    public void WhiteblocksAndPruneSortedOnOverlapBehaveCorrectlyOnRealPages()
    {
        CRand.Seed(1);
        Pix pix = TestPaths.ReadInput("test8.jpg");
        Pix pix1 = pix.ConvertTo1(128);
        Pix.DilateBrick(pix1, pix1, 5, 5);
        Boxa boxa1 = pix1.ConnComp(4);
        pix1.GetDimensions(out int w, out int h, out _);
        Box region = Box.Create(0, 0, w, h);
        Boxa.PermuteRandom(boxa1, boxa1);
        Boxa fg = boxa1.SelectBySize(500, 500, L.L_SELECT_IF_BOTH, L.L_SELECT_IF_LT, out _);

        Boxa wb0 = fg.GetWhiteblocks(region, L.L_SORT_BY_AREA, 20, 0.0f, 200, 0.15f, 20000);
        Assert.Equal(20, wb0.Count);

            /* Verify zero overlap with foreground boxes and zero pairwise overlap when maxoverlap = 0 */
        for (int i = 0; i < wb0.Count; i++) {
            Box bi = wb0.GetBox(i, L.L_CLONE);
            Assert.Equal(0, fg.IntersectsBoxCount(bi));
            for (int j = 0; j < i; j++) {
                Box bj = wb0.GetBox(j, L.L_CLONE);
                Assert.Equal(0.0f, bj.OverlapFraction(bi));
            }
        }

            /* With maxoverlap = 0.4, pruning to 0.0 shrinks or preserves count and eliminates overlap */
        Boxa wbOverlap = fg.GetWhiteblocks(region, L.L_SORT_BY_AREA, 20, 0.4f, 200, 0.15f, 20000);
        Assert.Equal(20, wbOverlap.Count);
        Boxa pruned = wbOverlap.PruneSortedOnOverlap(0.0f);
        Assert.True(pruned.Count >= 1 && pruned.Count <= wbOverlap.Count);
        for (int i = 0; i < pruned.Count; i++) {
            Box bi = pruned.GetBox(i, L.L_CLONE);
            for (int j = 0; j < i; j++)
                Assert.Equal(0.0f, pruned.GetBox(j, L.L_CLONE).OverlapFraction(bi));
        }
    }
}
