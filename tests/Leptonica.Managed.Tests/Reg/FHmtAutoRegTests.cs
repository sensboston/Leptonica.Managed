using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// fhmtauto_reg.c: basic regression test for hit-miss transform: rasterops &amp; dwa.
/// </summary>
public class FHmtAutoRegTests
{
    [RegFact]
    public void FHmtAuto()
    {
        var rp = new RegTest("fhmtauto");

        Pix pixs = TestPaths.ReadInput("feyn.tif");
        Sela sela = Sela.AddHitMiss(null);
        int nsels = sela.Count;

        for (int i = 0; i < nsels; i++) {
            Sel sel = sela.GetSel(i);
            string selname = sel.Name!;

            Pix pixref = Pix.HMT(null, pixs, sel);
            Pix pix1 = pixs.AddBorder(32, 0);
            Pix pix2 = Pix.FHMTGen_1(null, pix1, selname);
            Pix pix3 = pix2.RemoveBorder(32);
            Pix pix4 = Pix.HMTDwa_1(null, pixs, selname);
            rp.ComparePix(pixref, pix3);  /* 0, 2, ... 18 */
            rp.ComparePix(pixref, pix4);  /* 1, 3, ... 19 */
        }

        Assert.Equal(19, rp.Index);
        rp.Cleanup();
    }
}
