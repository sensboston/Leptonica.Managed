using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// baseline_reg.c: finding a projective transform that deskews textlines in an image
/// with keystoning, finding baselines in a text image, and cleaning the background to
/// white in a dark and mottled text image.
/// </summary>
/// <remarks>
/// The C goldens were made without gnuplot, so the checks of plot images
/// (C indices 1, 2, 4 and 5: pixRead() of the gnuplot output returns null there, which
/// does not advance the index) are dropped, together with the plots.
/// The golden index is then: C 0 -> 0, C 3 -> 1, C 6 -> 2, C 7 -> 3, C 8 -> 4, ... C 16 -> 12.
/// </remarks>
public class BaselineRegTests
{
    [RegFact]
    public void Baseline()
    {
        Numa na;
        Pix pixs, pix1, pix2, pix3, pix4, pix5;
        Pixa pixadb;
        Pta? pta;
        var rp = new RegTest("baseline");

        Utils.LeptMkdir("lept/baseline");

        pixs = TestPaths.ReadInput("keystone.png");

            /* Test function for deskewing using projective transform
             * on linear approximation for local skew angle */
        pix1 = pixs.DeskewLocal(10, 0, 0, 0.0f, 0.0f, 0.0f);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0 */

            /* Test function for finding local skew angles */
        na = pixs.GetLocalSkewAngles(10, 0, 0, 0.0f, 0.0f, 0.0f, out _, out _, 1);

            /* Test baseline finder */
        pixadb = Pixa.Create(6);
        na = pix1.FindBaselines(out pta, pixadb)!;
        rp.CompareValues(23, na.Count, 0);  /* 3 */
        pix4 = Pix.Read("/tmp/lept/baseline/baselines.png");
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 6 */
        pix5 = pixadb.DisplayTiledInRows(32, 1500, 1.0f, 0, 30, 2);
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 7 */

            /* Another test for baselines, with dark image.
             * With minw = 60, the number at the top of the page is skipped. */
        pixadb = Pixa.Create(6);
        pixs = TestPaths.ReadInput("pedante.079.jpg");  /* 75 ppi */
        pix1 = pixs.RemoveBorder(30);
        pixadb.AddPix(pix1, L.L_COPY);
        pix2 = pix1.ConvertRGBToGray(0.33f, 0.34f, 0.33f);
        pix3 = pix2.Scale(4.0f, 4.0f);   /* scale up to 300 ppi */
        pix4 = pix3.CleanBackgroundToWhite(null, null, 1.0f, 70, 170);
        pix5 = pix4.ThresholdToBinary(170);
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 8 */
        pixadb.AddPix(pix5.Scale(0.25f, 0.25f), L.L_INSERT);
        pix1 = pix5.Deskew(2);
        na = pix1.FindBaselinesGen(50, out pta, pixadb)!;
        rp.CompareValues(35, na.Count, 0);  /* 9 */
        pix2 = pixadb.DisplayTiledInRows(32, 1500, 1.0f, 0, 30, 2);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 10 */

            /* Another test for baselines: very short textblock is removed */
        pixadb = Pixa.Create(6);
        pix1 = TestPaths.ReadInput("baseline1.png");
        na = pix1.FindBaselines(out pta, pixadb)!;
        rp.CompareValues(2, na.Count, 0);  /* 11 */
        pix2 = pixadb.DisplayTiledInRows(32, 1500, 1.0f, 0, 30, 2);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 12 */

            /* Another test for baselines: some very short lines */
        pixadb = Pixa.Create(6);
        pix1 = TestPaths.ReadInput("baseline2.tif");
        na = pix1.FindBaselinesGen(30, out pta, pixadb)!;
        rp.CompareValues(29, na.Count, 0);  /* 13 */
        pix2 = pixadb.DisplayTiledInRows(32, 1500, 1.0f, 0, 30, 2);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 14 */

            /* Another test for baselines: more short lines' */
        pixadb = Pixa.Create(6);
        pix1 = TestPaths.ReadInput("baseline3.tif");
        na = pix1.FindBaselinesGen(30, out pta, pixadb)!;
        rp.CompareValues(40, na.Count, 0);  /* 15 */
        pix2 = pixadb.DisplayTiledInRows(32, 1500, 1.0f, 0, 30, 2);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 16 */

        Assert.Equal(12, rp.Index);
        rp.Cleanup();
    }
}
