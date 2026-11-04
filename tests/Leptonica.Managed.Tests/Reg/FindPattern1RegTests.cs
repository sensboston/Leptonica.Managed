using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// findpattern1_reg.c: uses pixGenerateSelBoundary() to generate hit-miss Sels
/// and match/remove patterns at 4x, 8x, and 16x reductions.
/// </summary>
public class FindPattern1RegTests
{
    private const uint HitColor = 0x33aa4400u;
    private const uint MissColor = 0xaa44bb00u;

    private static readonly string[] PatName = {
        "tribune-word.png",
        "tribune-t.png"
    };

    [RegFact]
    public void FindPattern1()
    {
        var rp = new RegTest("findpattern1");

        for (int patno = 0; patno < 2; patno++) {
            for (int red = 4; red <= 16; red *= 2) {
                if (patno == 1 && red == 16) continue;
                GeneratePattern(patno, red, rp);
            }
        }

        Assert.Equal(19, rp.Index);
        rp.Cleanup();
    }

    private static void GeneratePattern(int patno, int red, RegTest rp)
    {
        Pix pixs = TestPaths.ReadInput(PatName[patno]);

            /* Make a hit-miss sel at specified reduction factor */
        Pix pixt;
        Sel selhm;
        Pix? pixpNullable;
        if (red == 4) {
            pixt = pixs.ReduceRankBinaryCascade(4, 4, 0, 0);
            selhm = pixt.GenerateSelBoundary(2, 2, 20, 30, 1, 1, 0, 0, out pixpNullable);
        } else if (red == 8) {
            pixt = pixs.ReduceRankBinaryCascade(4, 4, 2, 0);
            selhm = pixt.GenerateSelBoundary(1, 2, 6, 12, 1, 1, 0, 0, out pixpNullable);
        } else {
            pixt = pixs.ReduceRankBinaryCascade(4, 4, 2, 2);
            selhm = pixt.GenerateSelBoundary(1, 1, 4, 8, 0, 0, 0, 0, out pixpNullable);
        }
        Pix pixp = pixpNullable!;

            /* Display the sel */
        Pix pixsel = pixp.DisplayHitMissSel(selhm, 7, HitColor, MissColor);
        Pixa pixa = Pixa.Create(2);
        pixa.AddPix(pixs, L.L_CLONE);
        pixa.AddPix(pixsel, L.L_CLONE);
        int width = patno == 0 ? 1200 : 400;
        Pix pixd = pixa.DisplayTiledAndScaled(32, width, 2, 0, 30, 2);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);

            /* Use the sel to find all instances in the page */
        Pix pix = TestPaths.ReadInput("tribune-page-4x.png");
        Pix pixr = red == 4
            ? Pix.Clone(pix)
            : red == 8
                ? pix.ReduceRankBinaryCascade(2, 0, 0, 0)
                : pix.ReduceRankBinaryCascade(2, 2, 0, 0);

        Pix pixhmt = Pix.HMT(null, pixr, selhm);

            /* Color each instance at full res */
        selhm.GetParameters(out _, out _, out int cy, out int cx);
        Pix pixc1 = pixr.DisplayMatchedPattern(pixp, pixhmt, cx, cy, 0x0000ff00u, 1.0f, 5)!;
        rp.WritePixAndCheck(pixc1, L.IFF_PNG);

            /* Color each instance at 0.5 scale */
        Pix pixc2 = pixr.DisplayMatchedPattern(pixp, pixhmt, cx, cy, 0x0000ff00u, 0.5f, 5)!;
        rp.WritePixAndCheck(pixc2, L.IFF_PNG);

            /* Remove each instance from the input image */
        Pix pixc3 = Pix.Copy(null, pixr);
        pixc3.RemoveMatchedPattern(pixp, pixhmt, cx, cy, 1);
        rp.WritePixAndCheck(pixc3, L.IFF_PNG);
    }
}
