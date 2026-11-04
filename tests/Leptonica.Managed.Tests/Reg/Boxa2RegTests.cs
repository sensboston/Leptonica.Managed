using System;
using System.IO;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// boxa2_reg.c: low-level statistical operations that can be used to identify anomalous
/// sized boxes in a boxa, where the widths and heights of the boxes are expected to be
/// similar.  Also tests separating the boxes into subsets of different parity.
/// </summary>
/// <remarks>
/// The plot at the end of the C test (boxaPlotSizes, which runs gnuplot, and check 6)
/// is dropped: there is no golden file for it.
/// </remarks>
public class Boxa2RegTests
{
    [RegFact]
    public void Boxa2()
    {
        int i, n, val, ne, no, nbins, minw, maxw, minh, maxh;
        int mine, mino, maxe, maxo;
        int w_diff, h_diff, median_w_diff, median_h_diff;
        int noutw, nouth;
        float medwe, medhe, medwo, medho;
        Boxa boxa1, boxa2, boxae, boxao;
        Numa? na1;
        Numa nawe, nahe, nawo, naho;
        Numa nadiffw, nadiffh;  /* diff from median w and h */
        Numa naiw, naih;  /* indicator arrays for small outlier dimensions */
        Numa narbwe, narbhe, narbwo, narbho;  /* rank-binned w and h */
        var rp = new RegTest("boxa2");

        Utils.LeptMkdir("lept/boxa");
        boxa1 = Boxa.Read(Path.Combine(TestPaths.ProgDir, "boxa4.ba"));

            /* Fill invalid boxes */
        n = boxa1.Count;
        na1 = boxa1.FindInvalidBoxes();
        if (na1 != null)
            boxa2 = boxa1.FillSequence(L.L_USE_SAME_PARITY_BOXES, 0);
        else
            boxa2 = boxa1.Copy(L.L_CLONE);

            /* Get the widths and heights for even and odd parity */
        boxa2.SplitEvenOdd(0, out boxae, out boxao);
        boxae.GetSizes(out nawe, out nahe);
        boxao.GetSizes(out nawo, out naho);

            /* Find the medians */
        medwe = nawe.GetMedian();
        medhe = nahe.GetMedian();
        medwo = nawo.GetMedian();
        medho = naho.GetMedian();

            /* Find the median even/odd differences for width and height */
        median_w_diff = (int)Math.Abs(medwe - medwo);
        median_h_diff = (int)Math.Abs(medhe - medho);
        rp.CompareValues(210, median_w_diff, 0.0f);  /* 0 */
        rp.CompareValues(15, median_h_diff, 0.0f);  /* 1 */

            /* Find the differences of box width and height from the median */
        nadiffw = Numa.MakeConstant(0, n);
        nadiffh = Numa.MakeConstant(0, n);
        ne = nawe.Count;
        no = nawo.Count;
        for (i = 0; i < ne; i++) {
            val = nawe.GetIValue(i);
            nadiffw.SetValue(2 * i, Math.Abs(val - medwe));
            val = nahe.GetIValue(i);
            nadiffh.SetValue(2 * i, Math.Abs(val - medhe));
        }
        for (i = 0; i < no; i++) {
            val = nawo.GetIValue(i);
            nadiffw.SetValue(2 * i + 1, Math.Abs(val - medwo));
            val = naho.GetIValue(i);
            nadiffh.SetValue(2 * i + 1, Math.Abs(val - medho));
        }

            /* Don't count invalid boxes; set the diffs to 0 for them */
        if (na1 != null) {
            for (i = 0; i < n; i++) {
                val = na1.GetIValue(i);
                if (val == 1) {
                    nadiffw.SetValue(i, 0);
                    nadiffh.SetValue(i, 0);
                }
            }
        }

            /* Make an indicator array for boxes that differ from the
             * median by more than a threshold value for outliers */
        naiw = nadiffw.MakeThresholdIndicator(90, L.L_SELECT_IF_GT);
        naih = nadiffh.MakeThresholdIndicator(90, L.L_SELECT_IF_GT);
        noutw = naiw.GetCountRelativeToZero(L.L_GREATER_THAN_ZERO);
        nouth = naih.GetCountRelativeToZero(L.L_GREATER_THAN_ZERO);
        rp.CompareValues(24, noutw, 0.0f);  /* 2 */
        rp.CompareValues(0, nouth, 0.0f);  /* 3 */

            /* Find the rank bins for width and height */
        nbins = Math.Max(5, ne / 50);  // up to 50 pages/bin
        narbwe = nawe.GetRankBinValues(nbins);
        narbwo = nawo.GetRankBinValues(nbins);
        narbhe = nahe.GetRankBinValues(nbins);
        narbho = naho.GetRankBinValues(nbins);

            /* Find min and max binned widths and heights; get the max diffs */
        mine = narbwe.GetIValue(0);
        maxe = narbwe.GetIValue(nbins - 1);
        mino = narbwo.GetIValue(0);
        maxo = narbwo.GetIValue(nbins - 1);
        minw = Math.Min(mine, mino);
        maxw = Math.Max(maxe, maxo);
        w_diff = maxw - minw;
        mine = narbhe.GetIValue(0);
        maxe = narbhe.GetIValue(nbins - 1);
        mino = narbho.GetIValue(0);
        maxo = narbho.GetIValue(nbins - 1);
        minh = Math.Min(mine, mino);
        maxh = Math.Max(maxe, maxo);
        h_diff = maxh - minh;
        rp.CompareValues(409, w_diff, 0.0f);  /* 4 */
        rp.CompareValues(54, h_diff, 0.0f);  /* 5 */

        Assert.Equal(5, rp.Index);
        rp.Cleanup();
    }
}
