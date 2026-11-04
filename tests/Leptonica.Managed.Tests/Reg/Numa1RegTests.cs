using System;
using System.IO;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// numa1_reg.c: tests histograms, interpolation and integration/differentiation.
/// </summary>
/// <remarks>
/// The gnuplot plots of the C test (gplotGeneralPix1/2, gplotMakeOutputPix) and their
/// regTestWritePixAndCheck() calls (indices 0-3 and 11-19) are dropped: running
/// gnuplot is not part of the port, and there are no golden files for them. The
/// numerical computations that fed the plots are kept. All remaining checks are
/// value comparisons, which do not use golden files.
/// </remarks>
public class Numa1RegTests
{
    [RegFact]
    public void Numa1()
    {
        int i, n, binsize, binstart, nbins;
        float pi, val, angle, xval, yval, x0, y0, startval, fbinsize;
        float minval, maxval, meanval, median, variance, rankval, rank, rmsdev;
        Numa na, nahisto, nax, nay, nasx, nasy;
        Numa nadx, nady, nafx, nafy;
        Pix pixs;
        var rp = new RegTest("numa1");

        Utils.LeptMkdir("lept/numa1");

        /* -------------------------------------------------------------------*
         *                            Histograms                              *
         * -------------------------------------------------------------------*/
        pi = 3.1415926535f;
        na = Numa.Create(5000);
        for (i = 0; i < 500000; i++) {
            angle = (float)(0.02293 * i * (double)pi);
            val = (float)(999.0 * Math.Sin(angle));
            na.AddNumber(val);
        }

        nahisto = na.MakeHistogramClipped(6, 2000);
        nbins = nahisto.Count;
        nax = Numa.MakeSequence(0, 1, nbins);

        nahisto = na.MakeHistogram(1000, out binsize, out binstart);
        nbins = nahisto.Count;
        nax = Numa.MakeSequence(binstart, binsize, nbins);

        nahisto = na.MakeHistogram(1000, out binsize);
        nbins = nahisto.Count;
        nax = Numa.MakeSequence(0, binsize, nbins);

        nahisto = na.MakeHistogramAuto(1000);
        nbins = nahisto.Count;
        nahisto.GetParameters(out startval, out fbinsize);
        nax = Numa.MakeSequence(startval, fbinsize, nbins);

        na.GetStatsUsingHistogram(2000, out minval, out maxval, out meanval,
                                  out variance, out median, 0.80f, out rankval, out nahisto);
        rmsdev = (float)Math.Sqrt((double)variance);
        rank = nahisto.HistogramGetRankFromVal(rankval);
        rp.CompareValues(-999.00f, minval, 0.1f);    /* 4 (0 here) */
        rp.CompareValues(999.00f, maxval, 0.1f);     /* 5 */
        rp.CompareValues(0.055f, meanval, 0.001f);   /* 6 */
        rp.CompareValues(0.30f, median, 0.005f);     /* 7 */
        rp.CompareValues(706.41f, rmsdev, 0.1f);     /* 8 */
        rp.CompareValues(808.15f, rankval, 0.1f);    /* 9 */
        rp.CompareValues(0.800f, rank, 0.01f);       /* 10 */

        /* -------------------------------------------------------------------*
         *                            Interpolation                           *
         * -------------------------------------------------------------------*/
            /* Test numaInterpolateEqxInterval() */
        pixs = TestPaths.ReadInput("test8.jpg");
        na = pixs.GetGrayHistogramMasked(null, 0, 0, 1);
        nasy = na.GetPartialSums();
        Numa.InterpolateEqxInterval(0.0f, 1.0f, na, L.L_LINEAR_INTERP,
                                    0.0f, 255.0f, 15, out nax, out nay);

            /* Test numaInterpolateArbxInterval() */
        pixs = TestPaths.ReadInput("test8.jpg");
        na = pixs.GetGrayHistogramMasked(null, 0, 0, 1);
        nasy = na.GetPartialSums();
        nasy.InsertNumber(0, 0.0f);
        nasx = Numa.MakeSequence(0.0f, 1.0f, 257);
        nasx.InterpolateArbxInterval(nasy, L.L_LINEAR_INTERP,
                                     10.0f, 250.0f, 23, out nax, out nay);

            /* Test numaInterpolateArbxVal() */
        pixs = TestPaths.ReadInput("test8.jpg");
        na = pixs.GetGrayHistogramMasked(null, 0, 0, 1);
        nasy = na.GetPartialSums();
        nasy.InsertNumber(0, 0.0f);
        nasx = Numa.MakeSequence(0.0f, 1.0f, 257);
        nax = Numa.MakeSequence(15.0f, (250.0f - 15.0f) / 23.0f, 24);
        n = nax.Count;
        nay = Numa.Create(n);
        for (i = 0; i < n; i++) {
            xval = nax.GetFValue(i);
            yval = nasx.InterpolateArbxVal(nasy, L.L_QUADRATIC_INTERP, xval);
            nay.AddNumber(yval);
        }

            /* Test interpolation */
        nasx = Numa.Read(Path.Combine(TestPaths.ProgDir, "testangle.na"));
        nasy = Numa.Read(Path.Combine(TestPaths.ProgDir, "testscore.na"));
        nasx.InterpolateArbxInterval(nasy, L.L_LINEAR_INTERP,
                                     -2.00f, 0.0f, 50, out nax, out nay);
        nasx.InterpolateArbxInterval(nasy, L.L_QUADRATIC_INTERP,
                                     -2.00f, 0.0f, 50, out nax, out nay);
        nasx.InterpolateArbxInterval(nasy, L.L_LINEAR_INTERP,
                                     -1.2f, -0.8f, 50, out nax, out nay);
        nay.FitMax(out yval, nax, out xval);

        /* -------------------------------------------------------------------*
         *                   Integration and differentiation                  *
         * -------------------------------------------------------------------*/
            /* Test integration and differentiation */
        nasx = Numa.Read(Path.Combine(TestPaths.ProgDir, "testangle.na"));
        nasy = Numa.Read(Path.Combine(TestPaths.ProgDir, "testscore.na"));
        nasx.DifferentiateInterval(nasy, -2.0f, 0.0f, 50, out nadx, out nady);
        nasx.InterpolateArbxInterval(nasy, L.L_LINEAR_INTERP,
                                     -2.00f, 0.0f, 50, out nafx, out nafy);
        n = nadx.Count;
        x0 = nafx.GetFValue(0);
        y0 = nafy.GetFValue(0);
        nay = Numa.Create(n);
            /* (Note: this tests robustness of the integrator: we go from
             * i = 0, and choose to have only 1 point in the interpolation
             * there, which is too small and causes the function to bomb out.
             * Here that is an exception, which is ignored as C ignores the
             * error return.) */
        yval = 0.0f;
        for (i = 0; i < n; i++) {
            xval = nadx.GetFValue(i);
            try {
                yval = nadx.IntegrateInterval(nady, x0, xval, 2 * i + 1);
            } catch (ArgumentException) {
            }
            nay.AddNumber(y0 + yval);
        }

        Assert.Equal(6, rp.Index);
        rp.Cleanup();
    }
}
