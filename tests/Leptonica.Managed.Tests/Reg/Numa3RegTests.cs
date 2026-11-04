using System.IO;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// numa3_reg.c: rank extraction, numa-morphology, finding a threshold from a numa,
/// and insertion in a sorted array.
/// </summary>
/// <remarks>
/// The C goldens were made without gnuplot, so the checks of the plots made by
/// gplotGeneralPix1/2() (C indices 0 - 8) did not count there and are dropped, together
/// with the plots. The numerical computations that fed the plots are kept. The
/// checks that remain keep their C index comments; the golden file index is the C index
/// minus 9 for the checks after the plots, so the last index is 3 (C: 12).
/// </remarks>
public class Numa3RegTests
{
    [RegFact]
    public void Numa3()
    {
        int i, hw, thresh, ival;
        float val, maxval, rank;
        bool same;
        Box box1;
        Numa na, nay, nap, nasy, na1, na2, na3, na4;
        Pix pixs, pix1, pix2, pix3, pixd;
        Pixa pixa;
        var rp = new RegTest("numa3");

        Utils.LeptMkdir("lept/numa3");

        /* -------------------------------------------------------------------*
         *                             Rank extraction                        *
         * -------------------------------------------------------------------*/
            /* Rank extraction with interpolation */
        pixs = TestPaths.ReadInput("test8.jpg");
        nasy = pixs.GetGrayHistogramMasked(null, 0, 0, 1);
        Numa.MakeRankFromHistogram(0.0f, 1.0f, nasy, 350, out Numa? nax, out nay);

            /* Rank extraction, point by point */
        pixs = TestPaths.ReadInput("test8.jpg");
        nap = Numa.Create(200);
        pixs.GetRankValueMasked(null, 0, 0, 2, 0.0f, out val, out na);
        for (i = 0; i < 101; i++) {
            rank = (float)(0.01 * i);
            val = na.HistogramGetValFromRank(rank);
            nap.AddNumber(val);
        }

        /* -------------------------------------------------------------------*
         *                           Numa-morphology                          *
         * -------------------------------------------------------------------*/
        na = Numa.Read(Path.Combine(TestPaths.ProgDir, "lyra.5.na"));
        na1 = na.Erode(21);
        na2 = na.Dilate(21);
        na3 = na.Open(21);
        na4 = na.Close(21);

        /* -------------------------------------------------------------------*
         *                   Find threshold from numa                         *
         * -------------------------------------------------------------------*/
        na1 = Numa.Read(Path.Combine(TestPaths.ProgDir, "two-peak-histo.na"));
        na4 = Numa.Create(0);
        pixa = Pixa.Create(0);
        for (hw = 2; hw < 21; hw += 2) {
            na2 = na1.WindowedMean(hw);  /* smoothing */
            na2.GetMax(out maxval, out _);
            na3 = na2.Transform(0.0f, 1.0f / maxval);
            na3.FindLocForThreshold(0, out thresh, out _);
            na4.AddNumber(thresh);
        }
        na4.Write("/tmp/lept/numa3/threshvals.na");
        rp.CheckFile("/tmp/lept/numa3/threshvals.na");  /* 9 */

        pixs = TestPaths.ReadInput("lyra.005.jpg");
        box1 = Box.Create(0, 173, 350, 580);
        pix1 = pixs.ClipRectangle(box1, out _)!;
        pix2 = pix1.RotateOrth(1);
        pix3 = pix2.ConvertTo8(false);
            /* The histogram plot (pix5 in C) needs gnuplot: as in the C run that made the
             * goldens, it is not produced and not added to the pixa */
        pix3.ThresholdByHisto(1, 0, 0, out ival, out Pix? pix4, out Numa? nahisto, false, out _);
        pixa = Pixa.Create(4);
        pixa.AddPix(pix2, L.L_INSERT);
        pixa.AddPix(pix3, L.L_INSERT);
        pixa.AddPix(pix4!, L.L_INSERT);
        pixd = pixa.DisplayTiledInColumns(1, 1.0f, 25, 2);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 10 */

        /* -------------------------------------------------------------------*
         *                      Insertion in a sorted array                   *
         * -------------------------------------------------------------------*/
        na1 = Numa.Create(0);
        CRand.Seed(5);
        na1.AddNumber(27);
        na1.AddNumber(13);
        for (i = 0; i < 70; i++) {
            ival = Utils.GenRandomIntOnInterval(0, 200, 0);
            na1.AddSorted(ival);
        }
        na2 = Numa.Sort(null, na1, L.L_SORT_INCREASING);
        Numa.Reverse(na2, na2);
        same = na1.Similar(na2, 0.0f);
        rp.CompareValues(1, same ? 1 : 0, 0.0f);  /* 11 */

        na1 = Numa.Create(0);
        CRand.Seed(6);
        na1.AddNumber(13);
        na1.AddNumber(27);
        for (i = 0; i < 70; i++) {
            ival = Utils.GenRandomIntOnInterval(0, 200, 0);
            na1.AddSorted(ival);
        }
        na2 = Numa.Sort(null, na1, L.L_SORT_DECREASING);
        Numa.Reverse(na2, na2);
        same = na1.Similar(na2, 0.0f);
        rp.CompareValues(1, same ? 1 : 0, 0.0f);  /* 12 */

        Assert.Equal(3, rp.Index);
        rp.Cleanup();
    }
}
