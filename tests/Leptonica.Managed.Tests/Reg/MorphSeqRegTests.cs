using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// morphseq_reg.c: simple regression test for binary morph sequence (interpreter),
/// showing display mode and rejection of invalid sequence components.
/// </summary>
public class MorphSeqRegTests
{
    private const string SEQUENCE1 = "O1.3 + C3.1 + R22 + D2.2 + X4";
    private const string SEQUENCE2 = "O2.13 + C5.23 + R22 + X4";
    private const string SEQUENCE3 = "e3.3 + d3.3 + tw5.5";
    private const string SEQUENCE4 = "O3.3 + C3.3";
    private const string SEQUENCE5 = "O5.5 + C5.5";
    private const string BAD_SEQUENCE = "O1.+D8 + E2.4 + e.4 + r25 + R + R.5 + X + x5 + y7.3";

    private const int DISPLAY_SEPARATION = 0;   /* use 250 to get images displayed */

    [RegFact]
    public void MorphSeq()
    {
        Pix pixs, pixg, pixc, pixd;

        Pix.LeptDebugOK = true;
        Utils.LeptMkdir("lept");
        pixs = TestPaths.ReadInput("feyn.tif");

            /* 1 bpp */
        pixd = pixs.MorphSequence(SEQUENCE1, -1);
        Assert.NotNull(pixd);
        pixd = pixs.MorphSequence(SEQUENCE1, DISPLAY_SEPARATION);
        Assert.NotNull(pixd);

        pixd = pixs.MorphCompSequence(SEQUENCE2, -2);
        Assert.NotNull(pixd);
        pixd = pixs.MorphCompSequence(SEQUENCE2, DISPLAY_SEPARATION);
        Assert.NotNull(pixd);

            /* DWA v23 Sel doesn't exist: error messages */
        pixd = pixs.MorphSequenceDwa(SEQUENCE2, -3);
        Assert.NotNull(pixd);
        pixd = pixs.MorphSequenceDwa(SEQUENCE2, DISPLAY_SEPARATION);
        Assert.NotNull(pixd);

        pixd = pixs.MorphCompSequenceDwa(SEQUENCE2, -4);
        Assert.NotNull(pixd);
        pixd = pixs.MorphCompSequenceDwa(SEQUENCE2, DISPLAY_SEPARATION);
        Assert.NotNull(pixd);

            /* 8 bpp */
        pixg = pixs.ScaleToGray(0.25f);
        pixd = pixg.GrayMorphSequence(SEQUENCE3, -5, 150);
        Assert.NotNull(pixd);
        pixd = pixg.GrayMorphSequence(SEQUENCE3, DISPLAY_SEPARATION, 150);
        Assert.NotNull(pixd);

        pixd = pixg.GrayMorphSequence(SEQUENCE4, -6, 300);
        Assert.NotNull(pixd);

            /* 32 bpp */
        pixc = TestPaths.ReadInput("wyom.jpg");
        pixd = pixc.ColorMorphSequence(SEQUENCE5, -7, 150);
        Assert.NotNull(pixd);
        pixd = pixc.ColorMorphSequence(SEQUENCE5, DISPLAY_SEPARATION, 450);
        Assert.NotNull(pixd);

            /* Syntax error handling: these fail (C returns null) */
        Assert.ThrowsAny<Exception>(() => pixs.MorphSequence(BAD_SEQUENCE, 50));
        Assert.ThrowsAny<Exception>(() => pixg.GrayMorphSequence(BAD_SEQUENCE, 50, 0));
    }
}
