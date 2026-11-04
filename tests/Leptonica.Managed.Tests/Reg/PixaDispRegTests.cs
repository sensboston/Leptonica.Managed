using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// pixadisp_reg.c: various pixaDisplay*() functions.
/// </summary>
public class PixaDispRegTests
{
    private static readonly string[] files = {"brev.06.75.jpg", "brev.10.75.jpg", "brev.14.75.jpg",
                                              "brev.20.75.jpg", "brev.36.75.jpg", "brev.53.75.jpg",
                                              "brev.56.75.jpg"};

    // Checks 0 - 12 pass against the goldens; check 13 needs pixaDisplayPairTiledInColumns()
    // with text, which calls pixAddSingleTextblock() (textops.c).
    [RegFact]
    public void PixaDisp()
    {
        int i, ws, hs, ncols;
        string fname;
        Box box;
        Boxa? boxa;
        Pix pixs, pix32, pix1, pix2, pix3, pix4;
        Pixa pixa1, pixa2, pixa3;
        Sarray sa1, sa2;
        var rp = new RegTest("pixadisp");

        pix32 = TestPaths.ReadInput("marge.jpg");
        pixs = TestPaths.ReadInput("feyn.tif");
        box = Box.Create(683, 799, 970, 479);
        pix1 = pixs.ClipRectangle(box, out _)!;
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0 */

            /* Generate pixa2 from pixs and pixa3 from pix1 */
        boxa = pixs.ConnComp(out pixa1, 8);
        pixa2 = pixa1.SelectBySize(60, 60, L.L_SELECT_IF_BOTH, L.L_SELECT_IF_LTE, out _);
        boxa = pix1.ConnComp(out pixa3, 8);

            /* pixaDisplay() */
        ws = pixs.Width;
        hs = pixs.Height;
        pix2 = pixa2.Display(ws, hs);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 1 */

            /* pixaDisplayRandomCmap() */
        pix2 = pixa2.DisplayRandomCmap(ws, hs);  /* black bg */
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 2 */
        pix2.Colormap!.ResetColor(0, 255, 255, 255);  /* white bg */
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 3 */

            /* pixaDisplayOnLattice() */
        pix2 = pixa3.DisplayOnLattice(50, 50, out ncols, out boxa);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 4 */

            /* pixaDisplayUnsplit() */
        pixa1 = Pixa.SplitPix(pix32, 5, 7, 10, 0x0000ff00);
        pix2 = pixa1.DisplayUnsplit(5, 7, 10, 0x00ff0000);
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 5 */

            /* pixaDisplayTiled() */
        pix2 = pixa3.DisplayTiled(1000, 0, 10);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 6 */

            /* pixaDisplayTiledInRows() */
        pix2 = pixa3.DisplayTiledInRows(1, 1000, 1.0f, 0, 10, 2);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 7 */

            /* pixaDisplayTiledAndScaled() */
        pix2 = pixa3.DisplayTiledAndScaled(1, 25, 20, 0, 5, 0);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 8 */

        pixa1 = Pixa.Create(10);
        pix2 = pix32.RankFilter(8, 8, 0.5f);
        pixa1.AddPix(pix2, L.L_INSERT);
        pix3 = pix32.Scale(0.5f, 0.5f);
        pix2 = pix3.RankFilter(8, 8, 0.5f);
        pixa1.AddPix(pix2, L.L_INSERT);
        pix3 = pix32.Scale(0.25f, 0.25f);
        pix2 = pix3.RankFilter(8, 8, 0.5f);
        pixa1.AddPix(pix2, L.L_INSERT);
        pix2 = pixa1.DisplayTiledAndScaled(32, 500, 3, 0, 25, 0);
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 9 */

            /* pixaMakeFromTiledPix() and pixaDisplayOnLattice()  */
        pix1 = TestPaths.ReadInput("sevens.tif");
        pixa1 = Pixa.MakeFromTiledPix(pix1, 20, 30, 0, 0, null);
        pix2 = pixa1.DisplayOnLattice(20, 30, out _, out _);
        rp.ComparePix(pix1, pix2);  /* 10 */
        pix3 = pixa1.DisplayOnLattice(20, 30, out _, out boxa);
        pixa2 = Pixa.MakeFromTiledPix(pix3, 0, 0, 0, 0, boxa);
        pix4 = pixa2.DisplayOnLattice(20, 30, out _, out _);
        rp.ComparePix(pix2, pix4);  /* 11 */
        rp.WritePixAndCheck(pix4, L.IFF_JFIF_JPEG);  /* 12 */

            /* pixaDisplayPairTiledInColumns */
        sa1 = Sarray.Create(7);
        for (i = 0; i < 7; i++)
            sa1.AddString(files[i], L.L_COPY);
        pixa1 = Pixa.Create(7);
        pixa2 = Pixa.Create(7);
        sa2 = Sarray.Create(7);
        for (i = 0; i < 7; i++) {
            fname = sa1.GetString(i, L.L_NOCOPY);
            pix1 = TestPaths.ReadInput(fname);
            pix2 = pix1.ConvertTo8(false);
            pixa1.AddPix(pix1, L.L_INSERT);
            pixa2.AddPix(pix2, L.L_INSERT);
            sa2.AddString(fname, L.L_COPY);
        }
        pix1 = pixa1.DisplayPairTiledInColumns(pixa2, 4, 0.5f, 15, 15, 2, 2, 6, 0, sa2);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 13 */

        Assert.Equal(13, rp.Index);
        rp.Cleanup();
    }
}
