using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// jbclass_reg.c: regression test for jbCorrelation and jbRankhaus.
/// </summary>
/// <remarks>
/// The input pages are written as png (not tiff g4) to /tmp/lept/class: the managed library
/// has no tiff codec yet, and the pages are 1 bpp, so the pixels are the same.
/// </remarks>
public class JbClassRegTests
{
    private const int COMPONENTS = L.JB_CONN_COMPS;
    /* Choose one of these */
    /* private const int COMPONENTS = L.JB_CHARACTERS; */
    /* private const int COMPONENTS = L.JB_WORDS; */

    [RegFact]
    public void JbClass()
    {
        int w, h, i;
        Box box;
        JbData data;
        JbClasser classer;
        Numa na;
        Sarray sa;
        Pix pix1, pix2;
        Pixa pixa1, pixa2;
        var rp = new RegTest("jbclass");

        Utils.LeptMkdir("lept/class");

            /* Set up the input data */
        pix1 = TestPaths.ReadInput("pageseg1.tif");
        pix1.GetDimensions(out w, out h, out _);
        box = Box.Create(0, 0, w, h / 2);
        pix2 = pix1.ClipRectangle(box, out _)!;
        Pix.Write("/tmp/lept/class/pix1.png", pix2, L.IFF_PNG);
        pix1 = TestPaths.ReadInput("pageseg4.tif");
        pix2 = pix1.ClipRectangle(box, out _)!;
        Pix.Write("/tmp/lept/class/pix2.png", pix2, L.IFF_PNG);
        sa = Sarray.Create(2);
        sa.AddString("/tmp/lept/class/pix1.png", L.L_COPY);
        sa.AddString("/tmp/lept/class/pix2.png", L.L_COPY);

        /*--------------------------------------------------------------*/

            /* Run the correlation-based classifier */
        classer = JbClasser.CorrelationInit(COMPONENTS, 0, 0, 0.8f, 0.6f);
        classer.AddPages(sa);

            /* Save and write out the result */
        data = classer.DataSave();
        data.Write("/tmp/lept/class/corr");

        pix1 = Pix.Read("/tmp/lept/class/corr.templates.png");
        rp.WritePixAndCheck(pix1, L.IFF_TIFF_G4);  /* 0 */

            /* Render the pages from the classifier data.
             * Use debugflag == FALSE to omit outlines of each component. */
        pixa1 = data.Render(false);
        for (i = 0; i < 2; i++) {
            pix1 = pixa1.GetPix(i, L.L_CLONE);
            rp.WritePixAndCheck(pix1, L.IFF_TIFF_G4);  /* 1, 2 */
        }

            /* Display all instances, organized by template */
        pixa1 = classer.PixaA.FlattenToPixa(out na, L.L_CLONE);
        pixa2 = PixaOutlineTemplates(pixa1, na);
        pix1 = pixa2.DisplayTiledInColumns(40, 1.0f, 10, 0);
        rp.WritePixAndCheck(pix1, L.IFF_TIFF_G4);  /* 3 */

        /*--------------------------------------------------------------*/

        Utils.LeptMkdir("lept/class2");

            /* Run the rank hausdorff-based classifier */
        classer = JbClasser.RankHausInit(COMPONENTS, 0, 0, 2, 0.97f);
        classer.AddPages(sa);

            /* Save and write out the result */
        data = classer.DataSave();
        data.Write("/tmp/lept/class2/haus");

        pix1 = Pix.Read("/tmp/lept/class2/haus.templates.png");
        rp.WritePixAndCheck(pix1, L.IFF_TIFF_G4);  /* 4 */

            /* Render the pages from the classifier data.
             * Use debugflag == FALSE to omit outlines of each component. */
        pixa1 = data.Render(false);
        for (i = 0; i < 2; i++) {
            pix1 = pixa1.GetPix(i, L.L_CLONE);
            rp.WritePixAndCheck(pix1, L.IFF_TIFF_G4);  /* 5, 6 */
        }

            /* Display all instances, organized by template */
        pixa1 = classer.PixaA.FlattenToPixa(out na, L.L_CLONE);
        pixa2 = PixaOutlineTemplates(pixa1, na);
        pix1 = pixa2.DisplayTiledInColumns(40, 1.0f, 10, 0);
        rp.WritePixAndCheck(pix1, L.IFF_TIFF_G4);  /* 7 */

        /*--------------------------------------------------------------*/

        Assert.Equal(7, rp.Index);
        rp.Cleanup();
    }

    private static Pixa PixaOutlineTemplates(Pixa pixas, Numa na)
    {
        int i, n, val, prev, curr;
        Numa nai;
        Pix pix1, pix2, pix3;
        Pixa pixad;

            /* Make an indicator array with a 1 for each template image */
        n = na.Count;
        nai = Numa.Create(n);
        prev = -1;
        for (i = 0; i < n; i++) {
            curr = na.GetIValue(i);
            if (curr != prev) {  /* index change */
                prev = curr;
                nai.AddNumber(1);
            } else {
                nai.AddNumber(0);
            }
        }

            /* Add a boundary of 3 white and 1 black pixels to templates */
        pixad = Pixa.Create(n);
        for (i = 0; i < n; i++) {
            pix1 = pixas.GetPix(i, L.L_CLONE);
            val = nai.GetIValue(i);
            if (val == 0) {
                pixad.AddPix(pix1, L.L_INSERT);
            } else {
                pix2 = pix1.AddBorder(3, 0);
                pix3 = pix2.AddBorder(1, 1);
                pixad.AddPix(pix3, L.L_INSERT);
            }
        }

        return pixad;
    }
}
