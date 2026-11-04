using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// label_reg.c: earthmover distance, connected component labelling, connected
/// component area labelling and color coded transform of 1 bpp images.
/// </summary>
public class LabelRegTests
{
    [RegFact]
    public void Label()
    {
        float dist, distr, distg, distb;
        Numa na1, na2;
        Pix pix1, pix2, pix3, pix4, pix5, pix6;
        var rp = new RegTest("label");

            /* Test earthmover distance: extreme example */
        na1 = Numa.MakeConstant(0, 201);
        na2 = Numa.MakeConstant(0, 201);
        na1.SetValue(0, 100);
        na2.SetValue(200, 100);
        dist = na1.EarthMoverDistance(na2);
        rp.CompareValues(200.0f, dist, 0.0001f);  /* 0 */

            /* Test connected component labelling */
        pix1 = TestPaths.ReadInput("feyn-fract.tif");
        pix2 = pix1.ConnCompTransform(8, 8);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 1 */
        pix3 = pix1.ConnCompTransform(8, 16);
        pix4 = pix3.Convert16To8(L.L_LS_BYTE);
        rp.CompareSimilarPix(pix2, pix4, 3, 0.001f, 0);  /* 2 */
        pix5 = pix1.ConnCompTransform(8, 32);
        pix6 = pix5.Convert32To8(L.L_LS_TWO_BYTES, L.L_LS_BYTE);
        rp.CompareSimilarPix(pix2, pix6, 3, 0.001f, 0);  /* 3 */

            /* Test connected component area labelling */
        pix2 = pix1.ConnCompAreaTransform(8);
        pix3 = pix2.Convert32To8(L.L_LS_TWO_BYTES, L.L_CLIP_TO_FF);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 4 */
        pix2.MultConstantGray(0.3f);
        pix4 = pix2.Convert32To8(L.L_LS_TWO_BYTES, L.L_CLIP_TO_FF);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 5 */

            /* Test color transform: 4-fold symmetry */
        pix1 = TestPaths.ReadInput("form1.tif");
        pix2 = pix1.RotateOrth(1);
        pix3 = pix1.RotateOrth(2);
        pix4 = pix1.RotateOrth(3);
        pix5 = pix1.LocToColorTransform();
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 6 */
        pix6 = pix2.LocToColorTransform();
        rp.WritePixAndCheck(pix6, L.IFF_PNG);  /* 7 */
        FindEMD(pix5, pix6, out distr, out distg, out distb);
        rp.CompareValues(0.12f, distr, 0.01f);  /* 8 */
        rp.CompareValues(0.00f, distg, 0.01f);  /* 9 */
        rp.CompareValues(0.00f, distb, 0.01f);  /* 10 */
        pix6 = pix3.LocToColorTransform();
        rp.WritePixAndCheck(pix6, L.IFF_PNG);  /* 11 */
        FindEMD(pix5, pix6, out distr, out distg, out distb);
        rp.CompareValues(0.12f, distr, 0.01f);  /* 12 */
        rp.CompareValues(0.09f, distg, 0.01f);  /* 13 */
        rp.CompareValues(0.00f, distb, 0.01f);  /* 14 */
        pix6 = pix4.LocToColorTransform();
        rp.WritePixAndCheck(pix6, L.IFF_PNG);  /* 15 */
        FindEMD(pix5, pix6, out distr, out distg, out distb);
        rp.CompareValues(0.00f, distr, 0.01f);  /* 16 */
        rp.CompareValues(0.09f, distg, 0.01f);  /* 17 */
        rp.CompareValues(0.00f, distb, 0.01f);  /* 18 */

            /* Test color transform: same form with translation */
        pix1 = TestPaths.ReadInput("form1.tif");
        pix2 = pix1.LocToColorTransform();
        Pix.Translate(pix1, pix1, 10, 10, L.L_BRING_IN_WHITE);
        pix3 = pix1.LocToColorTransform();
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 19 */
        FindEMD(pix2, pix3, out distr, out distg, out distb);
        rp.CompareValues(1.76f, distr, 0.01f);  /* 20 */
        rp.CompareValues(2.65f, distg, 0.01f);  /* 21 */
        rp.CompareValues(2.03f, distb, 0.01f);  /* 22 */

            /* Test color transform: same form with small rotation */
        pix1 = TestPaths.ReadInput("form1.tif");
        pix2 = pix1.LocToColorTransform();
        pix1.RotateShearCenterIP(0.1f, L.L_BRING_IN_WHITE);
        pix3 = pix1.LocToColorTransform();
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 23 */
        FindEMD(pix2, pix3, out distr, out distg, out distb);
        rp.CompareValues(1.50f, distr, 0.01f);  /* 24 */
        rp.CompareValues(1.71f, distg, 0.01f);  /* 25 */
        rp.CompareValues(1.42f, distb, 0.01f);  /* 26 */

            /* Test color transform: 2 different forms */
        pix1 = TestPaths.ReadInput("form1.tif");
        pix2 = pix1.LocToColorTransform();
        pix3 = TestPaths.ReadInput("form2.tif");
        pix4 = pix3.LocToColorTransform();
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 27 */
        FindEMD(pix2, pix4, out distr, out distg, out distb);
        rp.CompareValues(6.10f, distr, 0.02f);  /* 28 */
        rp.CompareValues(11.13f, distg, 0.01f);  /* 29 */
        rp.CompareValues(10.53f, distb, 0.01f);  /* 30 */

        Assert.Equal(30, rp.Index);
        rp.Cleanup();
    }

    private static void FindEMD(Pix pix1, Pix pix2, out float pdistr, out float pdistg,
                                out float pdistb)
    {
        pix1.GetColorHistogram(1, out Numa nar1, out Numa nag1, out Numa nab1);
        pix2.GetColorHistogram(1, out Numa nar2, out Numa nag2, out Numa nab2);
        pdistr = nar1.EarthMoverDistance(nar2);
        pdistg = nag1.EarthMoverDistance(nag2);
        pdistb = nab1.EarthMoverDistance(nab2);
    }
}
