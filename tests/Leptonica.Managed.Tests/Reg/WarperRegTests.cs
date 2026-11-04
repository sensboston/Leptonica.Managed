using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// warper_reg.c: random harmonic warping and captchas.
/// </summary>
public class WarperRegTests
{
    private const int size = 4;
    private static readonly float[] xmag = {3.0f, 4.0f, 5.0f, 7.0f};
    private static readonly float[] ymag = {5.0f, 6.0f, 8.0f, 10.0f};
    private static readonly float[] xfreq = {0.11f, 0.10f, 0.10f, 0.12f};
    private static readonly float[] yfreq = {0.11f, 0.13f, 0.13f, 0.15f};
    private static readonly int[] nx = {4, 3, 2, 1};
    private static readonly int[] ny = {4, 3, 2, 1};

    [RegFact]
    public void Warper()
    {
        int i, k, newline;
        Pix pixs, pixt, pixg, pixd;
        Pixa pixac;
        var rp = new RegTest("warper");

        pixs = TestPaths.ReadInput("feyn-word.tif");
        pixt = pixs.AddBorder(25, 0);
        pixg = pixt.ConvertTo8(false);

        for (k = 0; k < size; k++) {
            pixac = Pixa.Create(0);
            for (i = 0; i < 50; i++) {
                pixd = pixg.RandomHarmonicWarp(xmag[k], ymag[k], xfreq[k],
                                               yfreq[k], nx[k], ny[k], (uint)(7 * i), 255);
                newline = (i % 10 == 0) ? 1 : 0;
                DisplayResult(pixac, pixd, newline);
            }
            pixd = pixac.DisplayTiledInColumns(10, 1.0f, 20, 0);
            rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 0 - 3 */
        }

        for (k = 1; k <= 4; k++) {
            pixac = Pixa.Create(0);
            for (i = 0; i < 50; i++) {
                newline = (i % 10 == 0) ? 1 : 0;
                DisplayCaptcha(pixac, pixs, k, (uint)(7 * i), newline);
            }
            pixd = pixac.DisplayTiledInColumns(10, 1.0f, 20, 0);
            rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 4 - 7 */
        }

        Assert.Equal(7, rp.Index);
        rp.Cleanup();
    }

    private static void DisplayResult(Pixa pixac, Pix pixd, int newline)
    {
        uint color = 0;
        color = (uint)(((CRand.Next() >> 16) & 0xff) << L.L_RED_SHIFT |
                       ((CRand.Next() >> 16) & 0xff) << L.L_GREEN_SHIFT |
                       ((CRand.Next() >> 16) & 0xff) << L.L_BLUE_SHIFT);
        Pix pix1 = pixd.ColorizeGray(color, 0);
        pixac.AddPix(pix1, L.L_INSERT);
    }

    private static void DisplayCaptcha(Pixa pixac, Pix pixs, int nterms, uint seed, int newline)
    {
        uint color = 0;
        color = (uint)(((CRand.Next() >> 16) & 0xff) << L.L_RED_SHIFT |
                       ((CRand.Next() >> 16) & 0xff) << L.L_GREEN_SHIFT |
                       ((CRand.Next() >> 16) & 0xff) << L.L_BLUE_SHIFT);
        Pix pixd = pixs.SimpleCaptcha(25, nterms, seed, color, 0);
        pixac.AddPix(pixd, L.L_INSERT);
    }
}
