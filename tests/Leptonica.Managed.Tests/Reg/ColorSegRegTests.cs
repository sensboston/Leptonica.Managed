using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// colorseg_reg.c: explores the space of the four parameters input for color
/// segmentation (maxdist, maxcolors, selsize, finalcolors).
/// </summary>
public class ColorSegRegTests
{
    private static readonly int[] MaxColors = {4, 8, 16};
    private static readonly int[] FinalColors = {4, 8, 16};

    [RegFact]
    public void ColorSeg()
    {
        int i, j, k, maxdist, maxcolors, selsize, finalcolors;
        int nc, rval, gval, bval;
        Pix pixs, pix1, pix2;
        Pixa pixa;
        PixColormap cmap, cmapr;
        var rp = new RegTest("colorseg");
        CRand.Seed(1);  /* a fresh C process starts with srand(1); the state is per thread */

        pixs = TestPaths.ReadInput("tetons.jpg");
        for (k = 0; k < 3; k++) {
            maxcolors = MaxColors[k];
            finalcolors = FinalColors[k];
            pixa = Pixa.Create(0);
            for (i = 1; i <= 9; i++) {
                maxdist = 20 * i;
                for (j = 0; j <= 6; j++) {
                    selsize = j;
                    pix1 = pixs.ColorSegment(maxdist, maxcolors, selsize,
                                             finalcolors, 0);
                    pixa.AddPix(pix1, L.L_INSERT);
                }
            }

            pix2 = pixa.DisplayTiledInColumns(7, 1.0f, 15, 2);
            rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 0, 1, 2 */
        }

        pixs = TestPaths.ReadInput("wyom.jpg");
        pix1 = pixs.ColorSegment(50, 6, 6, 6, 0);
        cmap = pix1.Colormap!;
        nc = cmap.Count;
        cmapr = PixColormap.CreateRandom(8, false, false);
        for (i = 0; i < nc; i++) {
            pix2 = pix1.MakeMaskFromVal(i);
            cmapr.GetColor(i, out rval, out gval, out bval);
            pixs.RenderHashMaskArb(pix2, 0, 0, 8, 3, i % 4, 0,
                                   rval, gval, bval);
        }
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 3 */
        rp.WritePixAndCheck(pixs, L.IFF_JFIF_JPEG);  /* 4 */

        Assert.Equal(4, rp.Index);
        rp.Cleanup();
    }
}
