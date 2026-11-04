using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// extrema_reg.c: tests procedure for locating extrema (minima and maxima)
/// of a sampled function.
/// </summary>
public class ExtremaRegTests
{
    [RegFact]
    public void Extrema()
    {
        byte[] data;
        int i, ival, n;
        float f, val;
        Numa na1, na2, na3;
        var rp = new RegTest("extrema");

        Utils.LeptMkdir("lept/extrema");

            /* Generate a 1D signal */
        na1 = Numa.Create(500);
        for (i = 0; i < 500; i++) {
            f = (float)(48.3 * Math.Sin(0.13 * (float)i));
            f += (float)(63.4 * Math.Cos(0.21 * (float)i));
            na1.AddNumber(f);
        }

            /* Find the local min and max.  The plot (gnuplot, check 1 in C) is
             * not available: there is no golden file for it. */
        na2 = na1.FindExtrema(38.3f, out _);
        n = na2.Count;
        na3 = Numa.Create(n);
        for (i = 0; i < n; i++) {
            ival = na2.GetIValue(i);
            val = na1.GetFValue(ival);
            na3.AddNumber(val);
        }

        data = na2.WriteMem();
        rp.WriteDataAndCheck(data, "na");  /* 0 */

        Assert.Equal(0, rp.Index);
        rp.Cleanup();
    }
}
