using Leptonica;
using Xunit.Abstractions;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// binmorph1_reg.c: dilation, erosion, opening, closing and safe closing with a
/// brick sel, by rasterop, brick, separable and composite sequences and dwa; all
/// results must be the same, with asymmetric and symmetric boundary conditions.
/// </summary>
[Collection("MorphBoundaryCondition")]
public class BinMorph1RegTests
{
    /* set these ad lib. */
    private const int WIDTH = 21;    /* brick sel width */
    private const int HEIGHT = 15;   /* brick sel height */

    private readonly ITestOutputHelper _output;

    public BinMorph1RegTests(ITestOutputHelper output) { _output = output; }

    [RegFact]
    public void BinMorph1()
    {
        var rp = new RegTest("binmorph1");

        Pix pixs = TestPaths.ReadInput("feyn-fract.tif");
        try {
            TestAll(rp, pixs, false);
            TestAll(rp, pixs, true);
        } finally {
            Pix.ResetMorphBoundaryCondition(L.ASYMMETRIC_MORPH_BC);
        }
        Assert.Equal(1, rp.Index);
        rp.Cleanup();
    }

    private void TestAll(RegTest rp, Pix pixs, bool symmetric)
    {
        bool ok = true;
        string sequence;
        Pix pixref;
        Pix pix1, pix2, pix3, pix4, pix5, pix6, pix7, pix8, pix9, pix10, pix11, pix12, pix13, pix14;
        Sel sel;

        void Check(Pix pixref, Pix pix, string name) {
            if (!Pix.Equal(pixref, pix)) {
                _output.WriteLine($"pixref != {name} !");
                ok = false;
            }
        }

        if (symmetric) {
                /* This works properly if there is an added border */
            Pix.ResetMorphBoundaryCondition(L.SYMMETRIC_MORPH_BC);
            Pix? pixb = pixs.AddBorder(32, 0);
            pixs.TransferAllData(ref pixb, false, false);
            _output.WriteLine("Testing with symmetric boundary conditions");
        } else {
            Pix.ResetMorphBoundaryCondition(L.ASYMMETRIC_MORPH_BC);
            _output.WriteLine("Testing with asymmetric boundary conditions");
        }

            /* This is our test sel */
        sel = Sel.CreateBrick(HEIGHT, WIDTH, HEIGHT / 2, WIDTH / 2, L.SEL_HIT);

            /* Dilation */
        _output.WriteLine("  Testing dilation");
        pixref = Pix.Dilate(null, pixs, sel);   /* new one */
        pix1 = Pix.CreateTemplate(pixs);
        Pix.Dilate(pix1, pixs, sel);           /* existing one */
        Check(pixref, pix1, "pix1");
        pix2 = Pix.Copy(null, pixs);
        Pix.Dilate(pix2, pix2, sel);          /* in-place */
        Check(pixref, pix2, "pix2");
        sequence = $"d{WIDTH}.{HEIGHT}";
        pix3 = pixs.MorphSequence(sequence, 0);    /* sequence, atomic */
        Check(pixref, pix3, "pix3");
        sequence = $"d{WIDTH}.1 + d1.{HEIGHT}";
        pix4 = pixs.MorphSequence(sequence, 0);    /* sequence, separable */
        Check(pixref, pix4, "pix4");
        pix5 = Pix.DilateBrick(null, pixs, WIDTH, HEIGHT);  /* new one */
        Check(pixref, pix5, "pix5");
        pix6 = Pix.CreateTemplate(pixs);
        Pix.DilateBrick(pix6, pixs, WIDTH, HEIGHT);  /* existing one */
        Check(pixref, pix6, "pix6");
        pix7 = Pix.Copy(null, pixs);
        Pix.DilateBrick(pix7, pix7, WIDTH, HEIGHT);  /* in-place */
        Check(pixref, pix7, "pix7");
        pix8 = Pix.DilateBrickDwa(null, pixs, WIDTH, HEIGHT);  /* new one */
        Check(pixref, pix8, "pix8");
        pix9 = Pix.CreateTemplate(pixs);
        Pix.DilateBrickDwa(pix9, pixs, WIDTH, HEIGHT);  /* existing one */
        Check(pixref, pix9, "pix9");
        pix10 = Pix.Copy(null, pixs);
        Pix.DilateBrickDwa(pix10, pix10, WIDTH, HEIGHT);  /* in-place */
        Check(pixref, pix10, "pix10");
        pix11 = Pix.CreateTemplate(pixs);
        Pix.DilateCompBrickDwa(pix11, pixs, WIDTH, HEIGHT);  /* existing one */
        Check(pixref, pix11, "pix11");
        sequence = $"d{WIDTH}.{HEIGHT}";
        pix12 = pixs.MorphCompSequence(sequence, 0);    /* comp sequence */
        Check(pixref, pix12, "pix12");
        pix13 = pixs.MorphSequenceDwa(sequence, 0);    /* dwa sequence */
        Check(pixref, pix13, "pix13");

            /* Erosion */
        _output.WriteLine("  Testing erosion");
        pixref = Pix.Erode(null, pixs, sel);   /* new one */
        pix1 = Pix.CreateTemplate(pixs);
        Pix.Erode(pix1, pixs, sel);           /* existing one */
        Check(pixref, pix1, "pix1");
        pix2 = Pix.Copy(null, pixs);
        Pix.Erode(pix2, pix2, sel);          /* in-place */
        Check(pixref, pix2, "pix2");
        sequence = $"e{WIDTH}.{HEIGHT}";
        pix3 = pixs.MorphSequence(sequence, 0);    /* sequence, atomic */
        Check(pixref, pix3, "pix3");
        sequence = $"e{WIDTH}.1 + e1.{HEIGHT}";
        pix4 = pixs.MorphSequence(sequence, 0);    /* sequence, separable */
        Check(pixref, pix4, "pix4");
        pix5 = Pix.ErodeBrick(null, pixs, WIDTH, HEIGHT);  /* new one */
        Check(pixref, pix5, "pix5");
        pix6 = Pix.CreateTemplate(pixs);
        Pix.ErodeBrick(pix6, pixs, WIDTH, HEIGHT);  /* existing one */
        Check(pixref, pix6, "pix6");
        pix7 = Pix.Copy(null, pixs);
        Pix.ErodeBrick(pix7, pix7, WIDTH, HEIGHT);  /* in-place */
        Check(pixref, pix7, "pix7");
        pix8 = Pix.ErodeBrickDwa(null, pixs, WIDTH, HEIGHT);  /* new one */
        Check(pixref, pix8, "pix8");
        pix9 = Pix.CreateTemplate(pixs);
        Pix.ErodeBrickDwa(pix9, pixs, WIDTH, HEIGHT);  /* existing one */
        Check(pixref, pix9, "pix9");
        pix10 = Pix.Copy(null, pixs);
        Pix.ErodeBrickDwa(pix10, pix10, WIDTH, HEIGHT);  /* in-place */
        Check(pixref, pix10, "pix10");
        pix11 = Pix.CreateTemplate(pixs);
        Pix.ErodeCompBrickDwa(pix11, pixs, WIDTH, HEIGHT);  /* existing one */
        Check(pixref, pix11, "pix11");

        sequence = $"e{WIDTH}.{HEIGHT}";
        pix12 = pixs.MorphCompSequence(sequence, 0);    /* comp sequence */
        Check(pixref, pix12, "pix12");
        pix13 = pixs.MorphSequenceDwa(sequence, 0);    /* dwa sequence */
        Check(pixref, pix13, "pix13");

            /* Opening */
        _output.WriteLine("  Testing opening");
        pixref = Pix.Open(null, pixs, sel);   /* new one */
        pix1 = Pix.CreateTemplate(pixs);
        Pix.Open(pix1, pixs, sel);           /* existing one */
        Check(pixref, pix1, "pix1");
        pix2 = Pix.Copy(null, pixs);
        Pix.Open(pix2, pix2, sel);          /* in-place */
        Check(pixref, pix2, "pix2");
        sequence = $"o{WIDTH}.{HEIGHT}";
        pix3 = pixs.MorphSequence(sequence, 0);    /* sequence, atomic */
        Check(pixref, pix3, "pix3");
        sequence = $"e{WIDTH}.{HEIGHT} + d{WIDTH}.{HEIGHT}";
        pix4 = pixs.MorphSequence(sequence, 0);    /* sequence, separable */
        Check(pixref, pix4, "pix4");
        sequence = $"e{WIDTH}.1 + e1.{HEIGHT} + d{WIDTH}.1 + d1.{HEIGHT}";
        pix5 = pixs.MorphSequence(sequence, 0);    /* sequence, separable^2 */
        Check(pixref, pix5, "pix5");
        pix6 = Pix.OpenBrick(null, pixs, WIDTH, HEIGHT);  /* new one */
        Check(pixref, pix6, "pix6");
        pix7 = Pix.CreateTemplate(pixs);
        Pix.OpenBrick(pix7, pixs, WIDTH, HEIGHT);  /* existing one */
        Check(pixref, pix7, "pix7");
        pix8 = Pix.Copy(null, pixs);  /* in-place */
        Pix.OpenBrick(pix8, pix8, WIDTH, HEIGHT);  /* existing one */
        Check(pixref, pix8, "pix8");
        pix9 = Pix.OpenBrickDwa(null, pixs, WIDTH, HEIGHT);  /* new one */
        Check(pixref, pix9, "pix9");
        pix10 = Pix.CreateTemplate(pixs);
        Pix.OpenBrickDwa(pix10, pixs, WIDTH, HEIGHT);  /* existing one */
        Check(pixref, pix10, "pix10");
        pix11 = Pix.Copy(null, pixs);
        Pix.OpenBrickDwa(pix11, pix11, WIDTH, HEIGHT);  /* in-place */
        Check(pixref, pix11, "pix11");
        sequence = $"o{WIDTH}.{HEIGHT}";
        pix12 = pixs.MorphCompSequence(sequence, 0);    /* comp sequence */
        Check(pixref, pix12, "pix12");

        pix13 = pixs.MorphSequenceDwa(sequence, 0);    /* dwa sequence */
        Check(pixref, pix13, "pix13");
        pix14 = Pix.CreateTemplate(pixs);
        Pix.OpenCompBrickDwa(pix14, pixs, WIDTH, HEIGHT);  /* existing one */
        Check(pixref, pix14, "pix14");

            /* Closing */
        _output.WriteLine("  Testing closing");
        pixref = Pix.Close(null, pixs, sel);   /* new one */
        pix1 = Pix.CreateTemplate(pixs);
        Pix.Close(pix1, pixs, sel);           /* existing one */
        Check(pixref, pix1, "pix1");
        pix2 = Pix.Copy(null, pixs);
        Pix.Close(pix2, pix2, sel);          /* in-place */
        Check(pixref, pix2, "pix2");
        sequence = $"d{WIDTH}.{HEIGHT} + e{WIDTH}.{HEIGHT}";
        pix3 = pixs.MorphSequence(sequence, 0);    /* sequence, separable */
        Check(pixref, pix3, "pix3");
        sequence = $"d{WIDTH}.1 + d1.{HEIGHT} + e{WIDTH}.1 + e1.{HEIGHT}";
        pix4 = pixs.MorphSequence(sequence, 0);    /* sequence, separable^2 */
        Check(pixref, pix4, "pix4");
        pix5 = Pix.CloseBrick(null, pixs, WIDTH, HEIGHT);  /* new one */
        Check(pixref, pix5, "pix5");
        pix6 = Pix.CreateTemplate(pixs);
        Pix.CloseBrick(pix6, pixs, WIDTH, HEIGHT);  /* existing one */
        Check(pixref, pix6, "pix6");
        pix7 = Pix.Copy(null, pixs);  /* in-place */
        Pix.CloseBrick(pix7, pix7, WIDTH, HEIGHT);  /* existing one */
        Check(pixref, pix7, "pix7");

            /* Safe closing (using pix, not pixs) */
        _output.WriteLine("  Testing safe closing");
        pixref = Pix.CloseSafe(null, pixs, sel);   /* new one */
        pix1 = Pix.CreateTemplate(pixs);
        Pix.CloseSafe(pix1, pixs, sel);           /* existing one */
        Check(pixref, pix1, "pix1");
        pix2 = Pix.Copy(null, pixs);
        Pix.CloseSafe(pix2, pix2, sel);          /* in-place */
        Check(pixref, pix2, "pix2");
        sequence = $"c{WIDTH}.{HEIGHT}";
        pix3 = pixs.MorphSequence(sequence, 0);    /* sequence, atomic */
        Check(pixref, pix3, "pix3");
        sequence = $"b32 + d{WIDTH}.{HEIGHT} + e{WIDTH}.{HEIGHT}";
        pix4 = pixs.MorphSequence(sequence, 0);    /* sequence, separable */
        Check(pixref, pix4, "pix4");
        sequence = $"b32 + d{WIDTH}.1 + d1.{HEIGHT} + e{WIDTH}.1 + e1.{HEIGHT}";
        pix5 = pixs.MorphSequence(sequence, 0);    /* sequence, separable^2 */
        Check(pixref, pix5, "pix5");
        pix6 = Pix.CloseSafeBrick(null, pixs, WIDTH, HEIGHT);  /* new one */
        Check(pixref, pix6, "pix6");
        pix7 = Pix.CreateTemplate(pixs);
        Pix.CloseSafeBrick(pix7, pixs, WIDTH, HEIGHT);  /* existing one */
        Check(pixref, pix7, "pix7");
        pix8 = Pix.Copy(null, pixs);  /* in-place */
        Pix.CloseSafeBrick(pix8, pix8, WIDTH, HEIGHT);  /* existing one */
        Check(pixref, pix8, "pix8");
        pix9 = Pix.CloseBrickDwa(null, pixs, WIDTH, HEIGHT);  /* new one */
        Check(pixref, pix9, "pix9");
        pix10 = Pix.CreateTemplate(pixs);
        Pix.CloseBrickDwa(pix10, pixs, WIDTH, HEIGHT);  /* existing one */
        Check(pixref, pix10, "pix10");
        pix11 = Pix.Copy(null, pixs);
        Pix.CloseBrickDwa(pix11, pix11, WIDTH, HEIGHT);  /* in-place */
        Check(pixref, pix11, "pix11");
        sequence = $"c{WIDTH}.{HEIGHT}";
        pix12 = pixs.MorphCompSequence(sequence, 0);    /* comp sequence */
        Check(pixref, pix12, "pix12");
        pix13 = pixs.MorphSequenceDwa(sequence, 0);    /* dwa sequence */
        Check(pixref, pix13, "pix13");
        pix14 = Pix.CreateTemplate(pixs);
        Pix.CloseCompBrickDwa(pix14, pixs, WIDTH, HEIGHT);  /* existing one */
        Check(pixref, pix14, "pix14");

        rp.CompareValues(1, ok ? 1 : 0, 0);
        if (ok)
            _output.WriteLine("  All morph tests OK!");
        else
            _output.WriteLine("  Some morph tests failed!");
    }
}
