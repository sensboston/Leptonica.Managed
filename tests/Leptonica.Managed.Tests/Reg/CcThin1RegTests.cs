using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// ccthin1_reg.c: the "best" cc-preserving thinning functions, and the display
/// of all the strong cc-preserving 3x3 Sels.
/// </summary>
public class CcThin1RegTests
{
    /* The sel and its three orthogonal rotations, added to sela */
    private static void AddRotations(Sela sela, Sela sels, string name)
    {
        sels.FindSelByName(name, out _, out Sel? sel);
        Sel sel1 = sel!.RotateOrth(1);
        Sel sel2 = sel.RotateOrth(2);
        Sel sel3 = sel.RotateOrth(3);
        sela.AddSel(sel, null, L.L_COPY);
        sela.AddSel(sel1, name + "_90", L.L_INSERT);
        sela.AddSel(sel2, name + "_180", L.L_INSERT);
        sela.AddSel(sel3, name + "_270", L.L_INSERT);
    }

    [RegFact]
    public void CcThin1()
    {
        Box box;
        Pix pix1, pix2;
        Pixa pixa;
        Sela sela, sela4, sela8, sela48;
        var rp = new RegTest("ccthin1");

        pixa = Pixa.Create(0);

            /* Generate and display all of the 4-cc sels */
        sela4 = Sela.Thin4cc(null);
        pix1 = sela4.DisplayInPix(35, 3, 15, 3);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0 */
        pixa.AddPix(pix1, L.L_INSERT);

            /* Generate and display all of the 8-cc sels */
        sela8 = Sela.Thin8cc(null);
        pix1 = sela8.DisplayInPix(35, 3, 15, 3);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 1 */
        pixa.AddPix(pix1, L.L_INSERT);

            /* Generate and display all of the 4 and 8-cc preserving sels */
        sela48 = Sela.Thin4and8cc(null);
        pix1 = sela48.DisplayInPix(35, 3, 15, 4);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 2 */
        pixa.AddPix(pix1, L.L_INSERT);

            /* Generate and display three of the 4-cc sels and their rotations */
        sela = Sela.Thin4cc(null);
        sela4 = Sela.Create(0);
        AddRotations(sela4, sela, "sel_4_1");
        AddRotations(sela4, sela, "sel_4_2");
        AddRotations(sela4, sela, "sel_4_3");
        pix1 = sela4.DisplayInPix(35, 3, 15, 4);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 3 */
        pixa.AddPix(pix1, L.L_INSERT);

            /* Generate and display four of the 8-cc sels and their rotations */
        sela = Sela.Thin8cc(null);
        sela8 = Sela.Create(0);
        AddRotations(sela8, sela, "sel_8_2");
        AddRotations(sela8, sela, "sel_8_3");
        AddRotations(sela8, sela, "sel_8_5");
        AddRotations(sela8, sela, "sel_8_6");
        pix1 = sela8.DisplayInPix(35, 3, 15, 4);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 4 */
        pixa.AddPix(pix1, L.L_INSERT);

        pixa = Pixa.Create(0);

            /* Test the best 4 and 8 cc thinning */
        pix2 = TestPaths.ReadInput("feyn.tif");
        box = Box.Create(683, 799, 970, 479);
        pix1 = pix2.ClipRectangle(box, out _)!;
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 5 */
        pixa.AddPix(pix1, L.L_INSERT);

        pix2 = pix1.ThinConnected(L.L_THIN_FG, 4, 0);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 6 */
        pixa.AddPix(pix2, L.L_INSERT);
        pix2 = pix1.ThinConnected(L.L_THIN_BG, 4, 0);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 7 */
        pixa.AddPix(pix2, L.L_INSERT);

        pix2 = pix1.ThinConnected(L.L_THIN_FG, 8, 0);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 8 */
        pixa.AddPix(pix2, L.L_INSERT);
        pix2 = pix1.ThinConnected(L.L_THIN_BG, 8, 0);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 9 */
        pixa.AddPix(pix2, L.L_INSERT);

            /* Display tiled */
        pix1 = pixa.DisplayTiledAndScaled(8, 500, 1, 0, 25, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 10 */

        Assert.Equal(10, rp.Index);
        rp.Cleanup();
    }
}
