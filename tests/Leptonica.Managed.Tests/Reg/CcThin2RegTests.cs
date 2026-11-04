using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// ccthin2_reg.c: tests pixThinConnectedBySet() and stroke width normalization.
/// </summary>
public class CcThin2RegTests
{
    [RegFact]
    public void CcThin2()
    {
        var rp = new RegTest("ccthin2");

            /* Clip to foreground to see if there are any boundary
             * artifacts from thinning and thickening.  (There are not.) */
        Pix pix1 = TestPaths.ReadInput("feyn.tif");
        Box box = Box.Create(683, 799, 970, 479);
        Pix pix2 = pix1.ClipRectangle(box, out _)!;
        pix2.ClipToForeground(out Pix? pixsNullable, out _);
        Pix pixs = pixsNullable!;

        Pixa pixa1 = Pixa.Create(0);

        for (int s = 1; s <= 9; s++) {
            Sela sela = Sela.MakeThinSets(s, 0);
            pix1 = pixs.ThinConnectedBySet(L.L_THIN_FG, sela, 0);
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0 - 8 */
            pixa1.AddPix(pix1, L.L_INSERT);
        }

        for (int s = 10; s <= 11; s++) {
            Sela sela = Sela.MakeThinSets(s, 0);
            pix1 = pixs.ThinConnectedBySet(L.L_THIN_BG, sela, 5);
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 9 - 10 */
            pixa1.AddPix(pix1, L.L_INSERT);
        }

            /* Display the thinning results */
        pix1 = pixa1.DisplayTiledAndScaled(8, 500, 1, 0, 25, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 11 */

            /* Show thinning using width normalization */
        Pixaa paa = Pixaa.Create(3);
        pixa1 = BootNumGen.Gen3();
        Pixa pixa2 = pixa1.ScaleToSize(0, 36);
        paa.AddPixa(pixa2, L.L_INSERT);
        Pixa pixa3 = pixa2.ScaleToSizeRel(-4, 0);
        paa.AddPixa(pixa3, L.L_INSERT);
        pixa3 = pixa2.ScaleToSizeRel(4, 0);
        paa.AddPixa(pixa3, L.L_INSERT);
        Pixa pixa5 = Pixa.Create(6);
        for (int i = 0; i < 3; i++) {
            pixa3 = paa.GetPixa(i, L.L_CLONE);
            pix1 = pixa3.DisplayTiledInColumns(15, 1.0f, 10, 1);
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 12, 14, 16 */
            pixa5.AddPix(pix1, L.L_INSERT);
            Pixa pixa4 = pixa3.SetStrokeWidth(5, 1, 8);
            pix1 = pixa4.DisplayTiledInColumns(15, 1.0f, 10, 1);
            rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 13, 15, 17 */
            pixa5.AddPix(pix1, L.L_INSERT);
        }
        pix1 = pixa5.DisplayTiledInColumns(2, 1.0f, 25, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 18 */

        Assert.Equal(18, rp.Index);
        rp.Cleanup();
    }
}
