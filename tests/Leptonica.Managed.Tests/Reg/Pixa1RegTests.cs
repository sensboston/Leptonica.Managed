using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>pixa1_reg.c: removal of connected components.</summary>
public class Pixa1RegTests
{
    private const int CONNECTIVITY = 8;

    [RegFact]
    public void Pixa1()
    {
        int size, i, n, n0;
        Boxa boxa;
        Numa nax, nay1, nay2;
        Pix pixs, pixd;
        var rp = new RegTest("pixa1");

        pixs = TestPaths.ReadInput("feyn-fract.tif");
        Utils.LeptMkdir("lept/pixa");

        /* ----------------  Remove small components --------------- */
        boxa = pixs.ConnComp(8);
        n0 = boxa.Count;
        nax = Numa.MakeSequence(0, 2, 51);
        nay1 = Numa.Create(51);
        nay2 = Numa.Create(51);

        nay1.AddNumber(n0);
        for (i = 1; i <= 50; i++) {
            size = 2 * i;
            pixd = pixs.SelectBySize(size, size, CONNECTIVITY,
                                     L.L_SELECT_IF_BOTH, L.L_SELECT_IF_GTE, out _);
            boxa = pixd.ConnComp(8);
            n = boxa.Count;
            nay1.AddNumber(n);
        }

        nay2.AddNumber(n0);
        for (i = 1; i <= 50; i++) {
            size = 2 * i;
            pixd = pixs.SelectBySize(size, size, CONNECTIVITY,
                                     L.L_SELECT_IF_EITHER, L.L_SELECT_IF_GTE, out _);
            boxa = pixd.ConnComp(8);
            n = boxa.Count;
            nay2.AddNumber(n);
        }
        Assert.Equal(51, nay1.Count);
        Assert.Equal(51, nay2.Count);

            /* The plot (gplotMakeOutputPix) needs gnuplot, which is out of scope
             *; the C goldens were made without it, so there is
             * no check here either. */

        /* ----------------  Remove large components --------------- */
        nay1.Empty();
        nay2.Empty();

        nay1.AddNumber(0);
        for (i = 1; i <= 50; i++) {
            size = 2 * i;
            pixd = pixs.SelectBySize(size, size, CONNECTIVITY,
                                     L.L_SELECT_IF_BOTH, L.L_SELECT_IF_LTE, out _);
            boxa = pixd.ConnComp(8);
            n = boxa.Count;
            nay1.AddNumber(n);
        }

        nay2.AddNumber(0);
        for (i = 1; i <= 50; i++) {
            size = 2 * i;
            pixd = pixs.SelectBySize(size, size, CONNECTIVITY,
                                     L.L_SELECT_IF_EITHER, L.L_SELECT_IF_LTE, out _);
            boxa = pixd.ConnComp(8);
            n = boxa.Count;
            nay2.AddNumber(n);
        }
        Assert.Equal(51, nay1.Count);
        Assert.Equal(51, nay2.Count);

        Assert.Equal(-1, rp.Index);
        rp.Cleanup();
    }
}
