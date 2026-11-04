using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// wordboxes_reg.c: functions that make word boxes, and the function that finds the
/// nearest box to a given box in a boxa.
/// </summary>
public class WordBoxesRegTests
{
    [RegFact]
    public void WordBoxes()
    {
        Box box1, box2;
        Boxa boxa1;
        Boxaa boxaa1;
        Pix pix1, pix2, pix3, pix4;
        var rp = new RegTest("wordboxes");

            /* Make word boxes using pixWordMaskByDilation() */
        pix1 = TestPaths.ReadInput("lucasta.150.jpg");
        MakeWordBoxes1(pix1, 1.0f, 140, 0, rp);  /* 0 */
        MakeWordBoxes1(pix1, 0.6f, 140, 1, rp);  /* 1 */

        pix1 = TestPaths.ReadInput("zanotti-78.jpg");
        MakeWordBoxes1(pix1, 1.0f, 140, 2, rp);  /* 2 */
        MakeWordBoxes1(pix1, 0.6f, 140, 3, rp);  /* 3 */

        pix1 = TestPaths.ReadInput("words.15.tif");
        MakeWordBoxes1(pix1, 1.0f, 140, 4, rp);  /* 4 */
        MakeWordBoxes1(pix1, 0.6f, 140, 5, rp);  /* 5 */

        pix1 = TestPaths.ReadInput("words.44.tif");
        MakeWordBoxes1(pix1, 1.0f, 140, 6, rp);  /* 6 */
        MakeWordBoxes1(pix1, 0.6f, 140, 7, rp);  /* 7 */

            /* Make word boxes using the higher-level functions
             * pixGetWordsInTextlines() and pixGetWordBoxesInTextlines() */
        pix1 = TestPaths.ReadInput("lucasta.150.jpg");
        MakeWordBoxes2(pix1, 0.7f, 140, rp);  /* 8, 9 */

        pix1 = TestPaths.ReadInput("zanotti-78.jpg");
        MakeWordBoxes2(pix1, 0.7f, 140, rp);  /* 10, 11 */

            /* Test boxa adjacency function */
        pix1 = TestPaths.ReadInput("lucasta.150.jpg");
        TestBoxaAdjacency(pix1, rp);  /* 12 - 15 */

            /* Test word and character box finding */
        pix1 = TestPaths.ReadInput("zanotti-78.jpg");
        box1 = Box.Create(0, 0, 1500, 700);
        pix2 = pix1.ClipRectangle(box1, out _)!;
        box2 = Box.Create(150, 130, 1500, 355);
        pix2.FindWordAndCharacterBoxes(box2, 130, out boxa1, out boxaa1, "/tmp/lept/testboxes");
        pix3 = Pix.Read("/tmp/lept/testboxes/words.png");
        pix4 = Pix.Read("/tmp/lept/testboxes/chars.png");
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 16 */
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 17 */

        Assert.Equal(17, rp.Index);
        rp.Cleanup();
    }

    private static void MakeWordBoxes1(Pix pixs, float scalefact, int thresh, int index, RegTest rp)
    {
        Boxa boxa1, boxa2;
        Pix pix1, pix2, pix3, pix4;
        Pixa pixa1;

        pix1 = pixs.Scale(scalefact, scalefact);
        pix2 = pix1.ConvertTo1(thresh);
        pixa1 = Pixa.Create(3);
        pix2.WordMaskByDilation(out pix3, out _, pixa1);
        boxa1 = pix3.ConnComp(8);
        boxa2 = boxa1.Transform(0, 0, (float)(1.0 / scalefact), (float)(1.0 / scalefact));
        pix4 = pixs.ConvertTo32();
        pix4.RenderBoxaArb(boxa2, 2, 255, 0, 0);
        rp.WritePixAndCheck(pix4, L.IFF_JFIF_JPEG);
    }

    private static void MakeWordBoxes2(Pix pixs, float scalefact, int thresh, RegTest rp)
    {
        int default_minwidth = 10;
        int default_minheight = 10;
        int default_maxwidth = 400;
        int default_maxheight = 70;
        int minwidth, minheight, maxwidth, maxheight;
        Boxa boxa1, boxa2;
        Pix pix1, pix2, pix3, pix4;

        minwidth = (int)(scalefact * default_minwidth);
        minheight = (int)(scalefact * default_minheight);
        maxwidth = (int)(scalefact * default_maxwidth);
        maxheight = (int)(scalefact * default_maxheight);

            /* Get the word boxes */
        pix1 = pixs.Scale(scalefact, scalefact);
        pix2 = pix1.ConvertTo1(thresh);
        pix2.GetWordsInTextlines(minwidth, minheight, maxwidth, maxheight,
                                 out boxa1, out _, out _);
        boxa2 = boxa1.Transform(0, 0, (float)(1.0 / scalefact), (float)(1.0 / scalefact));
        pix3 = pixs.ConvertTo32();
        pix3.RenderBoxaArb(boxa2, 2, 255, 0, 0);
        rp.WritePixAndCheck(pix3, L.IFF_JFIF_JPEG);

            /* Do it again with this interface.  The result should be the same. */
        pix2.GetWordBoxesInTextlines(minwidth, minheight, maxwidth, maxheight,
                                     out boxa1, out _);
        boxa2 = boxa1.Transform(0, 0, (float)(1.0 / scalefact), (float)(1.0 / scalefact));
        pix4 = pixs.ConvertTo32();
        pix4.RenderBoxaArb(boxa2, 2, 255, 0, 0);
        rp.ComparePix(pix3, pix4);
    }

    private static void TestBoxaAdjacency(Pix pixs, RegTest rp)
    {
        int i, j, k, n;
        Box box1, box2;
        Boxa boxa0, boxa1, boxa2;
        Pix pix0, pix1, pix2, pix3;
        Numaa naai, naad;

        pix0 = pixs.ConvertTo1(140);

            /* Make a word mask and remove small components */
        pix0.WordMaskByDilation(out pix1, out _, null);
        boxa0 = pix1.ConnComp(8);
        boxa1 = boxa0.SelectBySize(8, 8, L.L_SELECT_IF_BOTH, L.L_SELECT_IF_GT, out _);
        pix2 = pixs.ConvertTo32();
        pix2.RenderBoxaArb(boxa1, 2, 255, 0, 0);
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);

            /* Find the adjacent boxes and their distances */
        boxa1.FindNearestBoxes(L.L_NON_NEGATIVE, 0, out naai, out naad);
        naai.Write("/tmp/lept/regout/index.naa");
        rp.CheckFile("/tmp/lept/regout/index.naa");
        naad.Write("/tmp/lept/regout/dist.naa");
        rp.CheckFile("/tmp/lept/regout/dist.naa");

            /* For a few boxes, show the (up to 4) adjacent boxes */
        n = boxa1.Count;
        pix3 = pixs.ConvertTo32();
        for (i = 10; i < n; i += 25) {
            box1 = boxa1.GetBox(i, L.L_COPY);
            pix3.RenderBoxArb(box1, 2, 255, 0, 0);
            boxa2 = Boxa.Create(4);
            for (j = 0; j < 4; j++) {
                naai.GetValue(i, j, out _, out k);
                if (k >= 0) {
                    box2 = boxa1.GetBox(k, L.L_COPY);
                    boxa2.AddBox(box2, L.L_INSERT);
                }
            }
            pix3.RenderBoxaArb(boxa2, 2, 0, 255, 0);
        }
        rp.WritePixAndCheck(pix3, L.IFF_JFIF_JPEG);
    }
}
