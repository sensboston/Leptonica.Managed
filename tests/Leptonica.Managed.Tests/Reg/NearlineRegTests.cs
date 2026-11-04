using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// nearline_reg.c: regression test for finding min or max values (and averages)
/// near a specified line.
/// </summary>
/// <remarks>
/// The gnuplot plots of the C test (gplotSimpleN, checks 7 and 9 in C) are dropped.
/// The golden files were made without gnuplot, where those two checks do not
/// advance the index, so the check of na6.na (index 8 in C) has the golden index 7.
/// </remarks>
public class NearlineRegTests
{
    [RegFact]
    public void Nearline()
    {
        int x1, y1;
        float minave, minave2, maxave;
        bool ok, similar;
        Numa na1, na2, na3, na4, na5, na6;
        Pix pixs, pix1, pix2;
        var rp = new RegTest("nearline");

        pixs = TestPaths.ReadInput("feyn.tif");
        pix1 = pixs.ScaleToGray6();

            /* Find averages of min and max along about 120 horizontal lines.
             * (Errors for lines without any sampled points are ignored, as in C.) */
        na1 = Numa.Create(0);
        na3 = Numa.Create(0);
        for (y1 = 40; y1 < 575; y1 += 5) {
            ok = TryMinMaxNearLine(pix1, 20, y1, 400, y1, 5, L.L_SCAN_BOTH,
                                   out _, out _, out minave, out maxave);
            if (ok) {
                na1.AddNumber((int)minave);
                na3.AddNumber((int)maxave);
            }
        }

            /* Find averages along about 120 vertical lines.  We've rotated
             * the image by 90 degrees, so the results should be nearly
             * identical to the first set.  Also generate a single-sided
             * scan (L_SCAN_NEGATIVE) for comparison with the double-sided scans. */
        pix2 = pix1.RotateOrth(3);
        na2 = Numa.Create(0);
        na4 = Numa.Create(0);
        na5 = Numa.Create(0);
        for (x1 = 40; x1 < 575; x1 += 5) {
            ok = TryMinMaxNearLine(pix2, x1, 20, x1, 400, 5, L.L_SCAN_BOTH,
                                   out _, out _, out minave, out maxave);
            TryMinMaxNearLine(pix2, x1, 20, x1, 400, 5, L.L_SCAN_NEGATIVE,
                              out _, out _, out minave2, out _);
            if (ok) {
                na2.AddNumber((int)minave);
                na4.AddNumber((int)maxave);
                na5.AddNumber((int)minave2);
            }
        }

        similar = na1.Similar(na2, 3.0f);  /* should be TRUE */
        rp.CompareValues(similar ? 1 : 0, 1, 0.0f);  /* 0 */
        similar = na3.Similar(na4, 1.0f);  /* should be TRUE */
        rp.CompareValues(similar ? 1 : 0, 1, 0.0f);  /* 1 */
        na1.Write("/tmp/lept/regout/na1.na");
        na2.Write("/tmp/lept/regout/na2.na");
        na3.Write("/tmp/lept/regout/na3.na");
        na4.Write("/tmp/lept/regout/na4.na");
        na5.Write("/tmp/lept/regout/na5.na");
        rp.CheckFile("/tmp/lept/regout/na1.na");  /* 2 */
        rp.CheckFile("/tmp/lept/regout/na2.na");  /* 3 */
        rp.CheckFile("/tmp/lept/regout/na3.na");  /* 4 */
        rp.CheckFile("/tmp/lept/regout/na4.na");  /* 5 */
        rp.CheckFile("/tmp/lept/regout/na5.na");  /* 6 */

            /* Plot minima along a single line, with different distances */
        pix1.MinMaxNearLine(20, 200, 400, 200, 2, L.L_SCAN_BOTH,
                            out na1!, out _, out _, out _);
        pix1.MinMaxNearLine(20, 200, 400, 200, 5, L.L_SCAN_BOTH,
                            out na2!, out _, out _, out _);
        pix1.MinMaxNearLine(20, 200, 400, 200, 15, L.L_SCAN_BOTH,
                            out na3!, out _, out _, out _);
        na1.Write("/tmp/lept/regout/na6.na");
        rp.CheckFile("/tmp/lept/regout/na6.na");  /* 7 (8 in C) */
        float fract = 100.0f / na1.Count;
        na4 = na1.Transform(0.0f, fract);
        na5 = na2.Transform(0.0f, fract);
        na6 = na3.Transform(0.0f, fract);
        na1 = na4.UniformSampling(100);
        na2 = na5.UniformSampling(100);
        na3 = na6.UniformSampling(100);

        Assert.Equal(7, rp.Index);
        rp.Cleanup();
    }

    private static bool TryMinMaxNearLine(Pix pix, int x1, int y1, int x2, int y2, int dist,
                                          int direction, out Numa? namin, out Numa? namax,
                                          out float minave, out float maxave)
    {
        try {
            pix.MinMaxNearLine(x1, y1, x2, y2, dist, direction,
                               out namin, out namax, out minave, out maxave);
            return true;
        } catch (LeptonicaException) {
            namin = namax = null;
            minave = maxave = 0.0f;
            return false;
        }
    }
}
