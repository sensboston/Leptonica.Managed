using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>dna_reg.c: tests basic functioning of L_Dna (number array of doubles).</summary>
public class DnaRegTests
{
    [RegFact]
    public void Dna()
    {
        int i, nbins, ival;
        double pi, angle, val, sum;
        L_Dna da1, da2, da3, da4, da5;
        L_Dnaa daa1, daa2;
        Numa na, nahisto, nax;
        var rp = new RegTest("dna");

        pi = 3.1415926535;
        da1 = L_Dna.Create(50);
        for (i = 0; i < 5000; i++) {
            angle = 0.02293 * i * pi;
            val = 999.0 * Math.Sin(angle);
            da1.AddNumber(val);
        }

            /* Conversion to Numa; I/O for Dna */
        na = da1.ConvertToNuma();
        da2 = na.ConvertToDna();
        da1.Write("/tmp/lept/regout/dna1.da");
        da2.Write("/tmp/lept/regout/dna2.da");
        da3 = L_Dna.Read("/tmp/lept/regout/dna2.da");
        da3.Write("/tmp/lept/regout/dna3.da");
        rp.CheckFile("/tmp/lept/regout/dna1.da");  /* 0 */
        rp.CheckFile("/tmp/lept/regout/dna2.da");  /* 1 */
        rp.CheckFile("/tmp/lept/regout/dna3.da");  /* 2 */
        rp.CompareFiles(1, 2);  /* 3 */

            /* I/O for Dnaa */
        daa1 = L_Dnaa.Create(3);
        daa1.AddDna(da1, L.L_INSERT);
        daa1.AddDna(da2, L.L_INSERT);
        daa1.AddDna(da3, L.L_INSERT);
        daa1.Write("/tmp/lept/regout/dnaa1.daa");
        daa2 = L_Dnaa.Read("/tmp/lept/regout/dnaa1.daa");
        daa2.Write("/tmp/lept/regout/dnaa2.daa");
        rp.CheckFile("/tmp/lept/regout/dnaa1.daa");  /* 4 */
        rp.CheckFile("/tmp/lept/regout/dnaa2.daa");  /* 5 */
        rp.CompareFiles(4, 5);  /* 6 */

            /* Just for fun -- is the numa ok?  The gnuplot output (check 7 in C)
             * is not available: gplotMakeOutput() runs gnuplot, there is no golden
             * file for it, and no later check depends on its index. */
        nahisto = na.MakeHistogramClipped(12, 2000);
        nbins = nahisto.Count;
        nax = Numa.MakeSequence(0, 1, nbins);

            /* Handling precision of int32 in double */
        da4 = L_Dna.Create(25);
        for (i = 0; i < 1000; i++)
            da4.AddNumber(1928374 * i);
        da4.Write("/tmp/lept/regout/dna4.da");
        da5 = L_Dna.Read("/tmp/lept/regout/dna4.da");
        sum = 0;
        for (i = 0; i < 1000; i++) {
            ival = da5.GetIValue(i);
            sum += Math.Abs(ival - i * 1928374);  /* we better be adding 0 each time */
        }
        rp.CompareValues((float)sum, 0.0f, 0.0f);  /* 8 (7 here) */

        Assert.Equal(7, rp.Index);
        rp.Cleanup();
    }
}
