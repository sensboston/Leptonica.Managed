using Leptonica;
using Xunit.Abstractions;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// binmorph4_reg.c: composite brick morphology (rasterop and dwa) against
/// non-composite, for sizes up to 63, with asymmetric and symmetric boundary
/// conditions.
/// </summary>
[Collection("MorphBoundaryCondition")]
public class BinMorph4RegTests
{
    private delegate Pix BrickOp(Pix? pixd, Pix pixs, int hsize, int vsize);

    private readonly ITestOutputHelper _output;

    public BinMorph4RegTests(ITestOutputHelper output) { _output = output; }

        /* Note: the symmetric case requires an extra border of size
         * approximately 40 to succeed for all SE up to size 64.  With
         * a smaller border the differences are small, and most of the
         * problem seems to be in the non-dwa code, because we are doing
         * sequential erosions without an extra border, and things aren't
         * being properly initialized.  To avoid these errors, add the border
         * in advance for symmetric b.c.
         * Note that asymmetric b.c. are recommended for document image
         * operations, and this test passes for asymmetric b.c. without
         * any added border. */

    [RegFact]
    public void BinMorph4()
    {
        var rp = new RegTest("binmorph4");

        Pix pixs = TestPaths.ReadInput("feyn-fract.tif");
        try {
            TestAll(rp, pixs, false);
            TestAll(rp, pixs, true);
        } finally {
            Pix.ResetMorphBoundaryCondition(L.ASYMMETRIC_MORPH_BC);
        }
        Assert.Equal(5135, rp.Index);
        rp.Cleanup();
    }

    private void TestAll(RegTest rp, Pix pixs, bool symmetric)
    {
        int i;
        Pix pix1, pix2, pix3, pix4, pix5, pix6;

        if (symmetric) {
                /* This works properly with an added border of 40 */
            Pix.ResetMorphBoundaryCondition(L.SYMMETRIC_MORPH_BC);
            Pix? pixb = pixs.AddBorder(40, 0);
            pixs.TransferAllData(ref pixb, false, false);
            _output.WriteLine("Testing with symmetric boundary conditions");
        } else {
            Pix.ResetMorphBoundaryCondition(L.ASYMMETRIC_MORPH_BC);
            _output.WriteLine("Testing with asymmetric boundary conditions");
        }

        pix1 = Pix.CreateTemplateNoInit(pixs);
        pix2 = Pix.CreateTemplateNoInit(pixs);
        pix3 = Pix.CreateTemplateNoInit(pixs);
        pix4 = Pix.CreateTemplateNoInit(pixs);
        pix5 = Pix.CreateTemplateNoInit(pixs);
        pix6 = Pix.CreateTemplateNoInit(pixs);
        Pix[] p = {pix1, pix2, pix3, pix4, pix5, pix6};

        for (i = 2; i < 64; i++) {

                /* Compare morph composite with morph non-composite */
            DoComparison(rp, pixs, p, i,
                Pix.DilateCompBrick, Pix.DilateBrick,
                Pix.ErodeCompBrick, Pix.ErodeBrick,
                Pix.OpenCompBrick, Pix.OpenBrick,
                Pix.CloseSafeCompBrick, Pix.CloseSafeBrick);

                /* Compare DWA non-composite with morph composite */
            if (i < 16)
                DoComparison(rp, pixs, p, i,
                    Pix.DilateBrickDwa, Pix.DilateCompBrick,
                    Pix.ErodeBrickDwa, Pix.ErodeCompBrick,
                    Pix.OpenBrickDwa, Pix.OpenCompBrick,
                    Pix.CloseBrickDwa, Pix.CloseSafeCompBrick);
                /* Compare DWA composite with DWA non-composite */
            if (i < 16)
                DoComparison(rp, pixs, p, i,
                    Pix.DilateCompBrickDwa, Pix.DilateBrickDwa,
                    Pix.ErodeCompBrickDwa, Pix.ErodeBrickDwa,
                    Pix.OpenCompBrickDwa, Pix.OpenBrickDwa,
                    Pix.CloseCompBrickDwa, Pix.CloseBrickDwa);
                /* Compare DWA composite with morph composite */
            DoComparison(rp, pixs, p, i,
                Pix.DilateCompBrickDwa, Pix.DilateCompBrick,
                Pix.ErodeCompBrickDwa, Pix.ErodeCompBrick,
                Pix.OpenCompBrickDwa, Pix.OpenCompBrick,
                Pix.CloseCompBrickDwa, Pix.CloseSafeCompBrick);
                /* Compare DWA composite with morph non-composite */
            DoComparison(rp, pixs, p, i,
                Pix.DilateCompBrickDwa, Pix.DilateBrick,
                Pix.ErodeCompBrickDwa, Pix.ErodeBrick,
                Pix.OpenCompBrickDwa, Pix.OpenBrick,
                Pix.CloseCompBrickDwa, Pix.CloseSafeBrick);
        }
    }

        /* The five DoComparisonDwa*() functions of the C test differ only in
         * the two functions applied for each of dilate, erode, open, close:
         * the first on pix1, pix3, pix5, the second on pix2, pix4, pix6. */
    private void DoComparison(RegTest rp, Pix pixs, Pix[] p, int isize,
                              BrickOp dilA, BrickOp dilB,
                              BrickOp eroA, BrickOp eroB,
                              BrickOp opnA, BrickOp opnB,
                              BrickOp clsA, BrickOp clsB)
    {
        Pix.SelectComposableSizes(isize, out int fact1, out int fact2);
        int size = fact1 * fact2;

        Step(rp, pixs, p, size, "dilate", dilA, dilB);
        Step(rp, pixs, p, size, "erode", eroA, eroB);
        Step(rp, pixs, p, size, "open", opnA, opnB);
        Step(rp, pixs, p, size, "close", clsA, clsB);
    }

    private void Step(RegTest rp, Pix pixs, Pix[] p, int size, string type, BrickOp a, BrickOp b)
    {
        a(p[0], pixs, size, 1);
        a(p[2], pixs, 1, size);
        a(p[4], pixs, size, size);
        b(p[1], pixs, size, 1);
        b(p[3], pixs, 1, size);
        b(p[5], pixs, size, size);
        PixCompareDwa(rp, size, type, p[0], p[1], p[2], p[3], p[4], p[5]);
    }

    private void PixCompareDwa(RegTest rp, int size, string type,
                               Pix pix1, Pix pix2, Pix pix3, Pix pix4, Pix pix5, Pix pix6)
    {
        bool same = Pix.Equal(pix1, pix2);
        rp.CompareValues(1, same ? 1 : 0, 0);
        if (!same)
            _output.WriteLine($"{type} ({size}, 1) not same");
        same = Pix.Equal(pix3, pix4);
        rp.CompareValues(1, same ? 1 : 0, 0);
        if (!same)
            _output.WriteLine($"{type} (1, {size}) not same");
        same = Pix.Equal(pix5, pix6);
        rp.CompareValues(1, same ? 1 : 0, 0);
        if (!same)
            _output.WriteLine($"{type} ({size}, {size}) not same");
    }
}
