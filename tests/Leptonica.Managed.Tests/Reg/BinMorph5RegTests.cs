using Leptonica;
using Xunit.Abstractions;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// binmorph5_reg.c: extended composite dwa morphology (sizes up to 240)
/// against composite and brick rasterop morphology.
/// </summary>
[Collection("MorphBoundaryCondition")]
public class BinMorph5RegTests
{
    private delegate Pix BrickOp(Pix? pixd, Pix pixs, int hsize, int vsize);

    private readonly ITestOutputHelper _output;

    public BinMorph5RegTests(ITestOutputHelper output) { _output = output; }

        /* Note: this fails on the symmetric case when the added border
         * is 64 pixels, but the differences are relatively small.
         * Most of the problem seems to be in the non-dwa code, because we
         * are doing sequential erosions without an extra border, and
         * things aren't being properly initialized.  To avoid these errors,
         * add a sufficiently large border for symmetric b.c.  The size of
         * the border needs to be half the size of the largest SE that is
         * being used.  Here we test up to size 240, and a border of 128
         * pixels is sufficient for symmetric b.c.  (For a SE of size 240
         * with its center in the middle at 120, the maximum translation will
         * be about 120.)
         * Note also that asymmetric b.c. are recommended for document image
         * operations, and this test passes with no added border for
         * asymmetric b.c. */

    [RegFact]
    public void BinMorph5()
    {
        var rp = new RegTest("binmorph5");

        Pix pixs = TestPaths.ReadInput("feyn-fract.tif");
        try {
            TestAll(rp, pixs, false);
            TestAll(rp, pixs, true);
        } finally {
            Pix.ResetMorphBoundaryCondition(L.ASYMMETRIC_MORPH_BC);
        }
        Assert.Equal(3767, rp.Index);
        rp.Cleanup();
    }

    private void TestAll(RegTest rp, Pix pixs, bool symmetric)
    {
        int i, n, rsize, fact1, fact2, extra;
        int size, lastsize;
        int[] dwasize = new int[256];
        int[] ropsize = new int[256];
        Pix pix1, pix2, pix3, pix4, pix5, pix6;

        if (symmetric) {
                /* This works properly with an added border of 128 */
            Pix.ResetMorphBoundaryCondition(L.SYMMETRIC_MORPH_BC);
            Pix? pixb = pixs.AddBorder(128, 0);
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

        /* ---------------------------------------------------------------- *
         *                  Faster test; testing fewer sizes                *
         * ---------------------------------------------------------------- */
            /* Compute the actual sizes used for each input size 'i' */
        for (i = 0; i < 256; i++) {
            dwasize[i] = 0;
            ropsize[i] = 0;
        }
        for (i = 65; i < 256; i++) {
            Pix.SelectComposableSizes(i, out fact1, out fact2);
            rsize = fact1 * fact2;
            ropsize[i] = rsize;
            Pix.GetExtendedCompositeParameters(i, out n, out extra, out dwasize[i]);
        }

            /* Use only values where the resulting sizes are equal */
        for (i = 65; i < 240; i++) {
            n = 1 + (int)((i - 63) / 62);
            extra = i - 63 - (n - 1) * 62 + 1;
            if (extra == 2) continue;  /* don't use this one (e.g., i == 126) */
            if (ropsize[i] == dwasize[i])
                DoComparisonDwa1(rp, pixs, p, i);
        }

        /* ---------------------------------------------------------------- *
         *          Slower test; testing maximum number of sizes            *
         * ---------------------------------------------------------------- */
        lastsize = 0;
        for (i = 65; i < 199; i++) {
            Pix.GetExtendedCompositeParameters(i, out n, out extra, out size);
            if (size == lastsize) continue;
            if (size == 126 || size == 188) continue;  /* deliberately off by one */
            lastsize = size;
            DoComparisonDwa2(rp, pixs, p, size);
        }
    }

    private void DoComparisonDwa1(RegTest rp, Pix pixs, Pix[] p, int isize)
    {
        Pix.SelectComposableSizes(isize, out int fact1, out int fact2);
        int size = fact1 * fact2;

        Step(rp, pixs, p, size, "dilate", Pix.DilateCompBrickExtendDwa, Pix.DilateCompBrick);
        Step(rp, pixs, p, size, "erode", Pix.ErodeCompBrickExtendDwa, Pix.ErodeCompBrick);
        Step(rp, pixs, p, size, "open", Pix.OpenCompBrickExtendDwa, Pix.OpenCompBrick);
        Step(rp, pixs, p, size, "close", Pix.CloseCompBrickExtendDwa, Pix.CloseSafeCompBrick);
    }

    private void DoComparisonDwa2(RegTest rp, Pix pixs, Pix[] p, int size)  /* exactly decomposable */
    {
        Step(rp, pixs, p, size, "dilate", Pix.DilateCompBrickExtendDwa, Pix.DilateBrick);
        Step(rp, pixs, p, size, "erode", Pix.ErodeCompBrickExtendDwa, Pix.ErodeBrick);
        Step(rp, pixs, p, size, "open", Pix.OpenCompBrickExtendDwa, Pix.OpenBrick);
        Step(rp, pixs, p, size, "close", Pix.CloseCompBrickExtendDwa, Pix.CloseSafeBrick);
    }

        /* The first function is applied to pix1, pix3, pix5, the second
         * to pix2, pix4, pix6. */
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
