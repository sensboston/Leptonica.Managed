using Leptonica;
using Xunit.Abstractions;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// binmorph3_reg.c: dwa morphology with separable brick sels, by the
/// high-level functions, pixMorphDwa_1() and pixFMorphopGen_1(), against the
/// rasterop implementation; with asymmetric and symmetric boundary conditions.
/// </summary>
[Collection("MorphBoundaryCondition")]
public class BinMorph3RegTests
{
    private readonly ITestOutputHelper _output;

    public BinMorph3RegTests(ITestOutputHelper output) { _output = output; }

    [RegFact]
    public void BinMorph3()
    {
        var rp = new RegTest("binmorph3");

        Pix pixs = TestPaths.ReadInput("feyn-fract.tif");

        try {
            TestAll(rp, pixs, false);
            TestAll(rp, pixs, true);
        } finally {
            Pix.ResetMorphBoundaryCondition(L.ASYMMETRIC_MORPH_BC);
        }
        Assert.Equal(5, rp.Index);
        rp.Cleanup();
    }

    private void TestAll(RegTest rp, Pix pixs, bool symmetric)
    {
        string? selnameh, selnamev;
        bool ok, same;
        int w, h, i, extraborder;
        uint bordercolor;
        int[] width = {21, 1, 21};
        int[] height = {1, 7, 7};
        Pix pixref, pix0, pix1, pix2, pix3, pix4;
        Sel sel;
        Sela sela;

        if (symmetric) {
            Pix.ResetMorphBoundaryCondition(L.SYMMETRIC_MORPH_BC);
            _output.WriteLine("Testing with symmetric boundary conditions");
        } else {
            Pix.ResetMorphBoundaryCondition(L.ASYMMETRIC_MORPH_BC);
            _output.WriteLine("Testing with asymmetric boundary conditions");
        }

            /* Note that with the choice of width and height, there is
             * no situation where they are both 1 (a no-op).  */
        for (i = 0; i < 3; i++) {
            w = width[i];
            h = height[i];
            sel = Sel.CreateBrick(h, w, h / 2, w / 2, L.SEL_HIT);
            selnameh = null;
            selnamev = null;

                /* Get the selnames for horiz and vert */
            sela = Sela.AddBasic(null);
            if (w > 1) {
                if ((selnameh = sela.GetBrickName(w, 1)) == null)
                    Assert.Fail("dwa hor sel not defined");
            }
            if (h > 1) {
                if ((selnamev = sela.GetBrickName(1, h)) == null)
                    Assert.Fail("dwa vert sel not defined");
            }
            _output.WriteLine($"w = {w}, h = {h}, selh = {selnameh}, selv = {selnamev}");
            ok = true;

                /* ----------------- Dilation ----------------- */
            _output.WriteLine("Testing dilation");
            pixref = Pix.Dilate(null, pixs, sel);
            pix1 = Pix.DilateBrickDwa(null, pixs, w, h);
            same = Pix.Equal(pixref, pix1);
            if (!same) {
                _output.WriteLine("pixref != pix1 !"); ok = false;
            }

            if (w > 1)
                pix1 = Pix.MorphDwa_1(null, pixs, L.L_MORPH_DILATE, selnameh!);
            else
                pix1 = Pix.Clone(pixs);
            if (h > 1)
                pix2 = Pix.MorphDwa_1(null, pix1, L.L_MORPH_DILATE, selnamev!);
            else
                pix2 = Pix.Clone(pix1);
            same = Pix.Equal(pixref, pix2);
            if (!same) {
                _output.WriteLine("pixref != pix2 !"); ok = false;
            }

            pix1 = pixs.AddBorder(32, 0);
            if (w > 1)
                pix2 = Pix.FMorphopGen_1(null, pix1, L.L_MORPH_DILATE, selnameh!);
            else
                pix2 = Pix.Clone(pix1);
            if (h > 1)
                pix3 = Pix.FMorphopGen_1(null, pix2, L.L_MORPH_DILATE, selnamev!);
            else
                pix3 = Pix.Clone(pix2);
            pix4 = pix3.RemoveBorder(32);
            same = Pix.Equal(pixref, pix4);
            if (!same) {
                _output.WriteLine("pixref != pix4 !"); ok = false;
            }

                /* ----------------- Erosion ----------------- */
            _output.WriteLine("Testing erosion");
            pixref = Pix.Erode(null, pixs, sel);
            pix1 = Pix.ErodeBrickDwa(null, pixs, w, h);
            same = Pix.Equal(pixref, pix1);
            if (!same) {
                _output.WriteLine("pixref != pix1 !"); ok = false;
            }

            if (w > 1)
                pix1 = Pix.MorphDwa_1(null, pixs, L.L_MORPH_ERODE, selnameh!);
            else
                pix1 = Pix.Clone(pixs);
            if (h > 1)
                pix2 = Pix.MorphDwa_1(null, pix1, L.L_MORPH_ERODE, selnamev!);
            else
                pix2 = Pix.Clone(pix1);
            same = Pix.Equal(pixref, pix2);
            if (!same) {
                _output.WriteLine("pixref != pix2 !"); ok = false;
            }

            pix1 = pixs.AddBorder(32, 0);
            if (w > 1)
                pix2 = Pix.FMorphopGen_1(null, pix1, L.L_MORPH_ERODE, selnameh!);
            else
                pix2 = Pix.Clone(pix1);
            if (h > 1)
                pix3 = Pix.FMorphopGen_1(null, pix2, L.L_MORPH_ERODE, selnamev!);
            else
                pix3 = Pix.Clone(pix2);
            pix4 = pix3.RemoveBorder(32);
            same = Pix.Equal(pixref, pix4);
            if (!same) {
                _output.WriteLine("pixref != pix4 !"); ok = false;
            }

                /* ----------------- Opening ----------------- */
            _output.WriteLine("Testing opening");
            pixref = Pix.Open(null, pixs, sel);
            pix1 = Pix.OpenBrickDwa(null, pixs, w, h);
            same = Pix.Equal(pixref, pix1);
            if (!same) {
                _output.WriteLine("pixref != pix1 !"); ok = false;
            }

            if (h == 1)
                pix2 = Pix.MorphDwa_1(null, pixs, L.L_MORPH_OPEN, selnameh!);
            else if (w == 1)
                pix2 = Pix.MorphDwa_1(null, pixs, L.L_MORPH_OPEN, selnamev!);
            else {
                pix1 = Pix.MorphDwa_1(null, pixs, L.L_MORPH_ERODE, selnameh!);
                pix2 = Pix.MorphDwa_1(null, pix1, L.L_MORPH_ERODE, selnamev!);
                Pix.MorphDwa_1(pix1, pix2, L.L_MORPH_DILATE, selnameh!);
                Pix.MorphDwa_1(pix2, pix1, L.L_MORPH_DILATE, selnamev!);
            }
            same = Pix.Equal(pixref, pix2);
            if (!same) {
                _output.WriteLine("pixref != pix2 !"); ok = false;
            }

            pix1 = pixs.AddBorder(32, 0);
            if (h == 1)
                pix3 = Pix.FMorphopGen_1(null, pix1, L.L_MORPH_OPEN, selnameh!);
            else if (w == 1)
                pix3 = Pix.FMorphopGen_1(null, pix1, L.L_MORPH_OPEN, selnamev!);
            else {
                pix2 = Pix.FMorphopGen_1(null, pix1, L.L_MORPH_ERODE, selnameh!);
                pix3 = Pix.FMorphopGen_1(null, pix2, L.L_MORPH_ERODE, selnamev!);
                Pix.FMorphopGen_1(pix2, pix3, L.L_MORPH_DILATE, selnameh!);
                Pix.FMorphopGen_1(pix3, pix2, L.L_MORPH_DILATE, selnamev!);
            }
            pix4 = pix3.RemoveBorder(32);
            same = Pix.Equal(pixref, pix4);
            if (!same) {
                _output.WriteLine("pixref != pix4 !"); ok = false;
            }

                /* ----------------- Closing ----------------- */
            _output.WriteLine("Testing closing");
            pixref = Pix.Close(null, pixs, sel);

                /* Note: L_MORPH_CLOSE for h==1 or w==1 gives safe closing,
                 * so we can't use it here. */
            if (h == 1) {
                pix1 = Pix.MorphDwa_1(null, pixs, L.L_MORPH_DILATE, selnameh!);
                pix2 = Pix.MorphDwa_1(null, pix1, L.L_MORPH_ERODE, selnameh!);
            }
            else if (w == 1) {
                pix1 = Pix.MorphDwa_1(null, pixs, L.L_MORPH_DILATE, selnamev!);
                pix2 = Pix.MorphDwa_1(null, pix1, L.L_MORPH_ERODE, selnamev!);
            }
            else {
                pix1 = Pix.MorphDwa_1(null, pixs, L.L_MORPH_DILATE, selnameh!);
                pix2 = Pix.MorphDwa_1(null, pix1, L.L_MORPH_DILATE, selnamev!);
                Pix.MorphDwa_1(pix1, pix2, L.L_MORPH_ERODE, selnameh!);
                Pix.MorphDwa_1(pix2, pix1, L.L_MORPH_ERODE, selnamev!);
            }
            same = Pix.Equal(pixref, pix2);
            if (!same) {
                _output.WriteLine("pixref != pix2 !"); ok = false;
            }

                /* Note: by adding only 32 pixels of border, we get
                 * the normal closing operation, even when calling
                 * with L_MORPH_CLOSE, because it requires 32 pixels
                 * of border to be safe. */
            pix1 = pixs.AddBorder(32, 0);
            if (h == 1)
                pix3 = Pix.FMorphopGen_1(null, pix1, L.L_MORPH_CLOSE, selnameh!);
            else if (w == 1)
                pix3 = Pix.FMorphopGen_1(null, pix1, L.L_MORPH_CLOSE, selnamev!);
            else {
                pix2 = Pix.FMorphopGen_1(null, pix1, L.L_MORPH_DILATE, selnameh!);
                pix3 = Pix.FMorphopGen_1(null, pix2, L.L_MORPH_DILATE, selnamev!);
                Pix.FMorphopGen_1(pix2, pix3, L.L_MORPH_ERODE, selnameh!);
                Pix.FMorphopGen_1(pix3, pix2, L.L_MORPH_ERODE, selnamev!);
            }
            pix4 = pix3.RemoveBorder(32);
            same = Pix.Equal(pixref, pix4);
            if (!same) {
                _output.WriteLine("pixref != pix4 !"); ok = false;
            }

                /* ------------- Safe Closing ----------------- */
            _output.WriteLine("Testing safe closing");
            pixref = Pix.CloseSafe(null, pixs, sel);
            pix0 = Pix.CloseSafeBrick(null, pixs, w, h);
            same = Pix.Equal(pixref, pix0);
            if (!same) {
                _output.WriteLine("pixref != pix0 !"); ok = false;
            }

            pix1 = Pix.CloseBrickDwa(null, pixs, w, h);
            same = Pix.Equal(pixref, pix1);
            if (!same) {
                _output.WriteLine("pixref != pix1 !"); ok = false;
            }

            bordercolor = Pix.GetMorphBorderPixelColor(L.L_MORPH_ERODE, 1);
            if (bordercolor == 0)   /* asymmetric b.c. */
                extraborder = 32;
            else   /* symmetric b.c. */
                extraborder = 0;

                /* Note: for safe closing we need 64 border pixels.
                 * However, when we implement a separable Sel
                 * with pixMorphDwa_*(), we must do dilation and
                 * erosion explicitly, and these functions only
                 * add/remove a 32-pixel border.  Thus, for that
                 * case we must add an additional 32-pixel border
                 * before doing the operations.  That is the reason
                 * why the implementation in morphdwa.c adds the
                 * 64 bit border and then uses the lower-level
                 * pixFMorphopGen_*() functions. */
            if (h == 1)
                pix3 = Pix.MorphDwa_1(null, pixs, L.L_MORPH_CLOSE, selnameh!);
            else if (w == 1)
                pix3 = Pix.MorphDwa_1(null, pixs, L.L_MORPH_CLOSE, selnamev!);
            else {
                pix0 = pixs.AddBorder(extraborder, 0);
                pix1 = Pix.MorphDwa_1(null, pix0, L.L_MORPH_DILATE, selnameh!);
                pix2 = Pix.MorphDwa_1(null, pix1, L.L_MORPH_DILATE, selnamev!);
                Pix.MorphDwa_1(pix1, pix2, L.L_MORPH_ERODE, selnameh!);
                Pix.MorphDwa_1(pix2, pix1, L.L_MORPH_ERODE, selnamev!);
                pix3 = pix2.RemoveBorder(extraborder);
            }
            same = Pix.Equal(pixref, pix3);
            if (!same) {
                _output.WriteLine("pixref != pix3 !"); ok = false;
            }

            pix1 = pixs.AddBorder(32 + extraborder, 0);
            if (h == 1)
                pix3 = Pix.FMorphopGen_1(null, pix1, L.L_MORPH_CLOSE, selnameh!);
            else if (w == 1)
                pix3 = Pix.FMorphopGen_1(null, pix1, L.L_MORPH_CLOSE, selnamev!);
            else {
                pix2 = Pix.FMorphopGen_1(null, pix1, L.L_MORPH_DILATE, selnameh!);
                pix3 = Pix.FMorphopGen_1(null, pix2, L.L_MORPH_DILATE, selnamev!);
                Pix.FMorphopGen_1(pix2, pix3, L.L_MORPH_ERODE, selnameh!);
                Pix.FMorphopGen_1(pix3, pix2, L.L_MORPH_ERODE, selnamev!);
            }
            pix4 = pix3.RemoveBorder(32 + extraborder);
            same = Pix.Equal(pixref, pix4);
            if (!same) {
                _output.WriteLine("pixref != pix4 !"); ok = false;
            }

            rp.CompareValues(1, ok ? 1 : 0, 0);
            if (ok)
                _output.WriteLine("All morph tests OK!");
        }
    }
}
