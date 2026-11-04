using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// expand_reg.c: replicative and power-of-2 expansion (and corresponding reductions).
/// </summary>
public class ExpandRegTests
{
    private const string BINARY_IMAGE = "test1.png";
    private const string TWO_BPP_IMAGE_NO_CMAP = "weasel2.4g.png";
    private const string TWO_BPP_IMAGE_CMAP = "weasel2.4c.png";
    private const string FOUR_BPP_IMAGE_NO_CMAP = "weasel4.16g.png";
    private const string FOUR_BPP_IMAGE_CMAP = "weasel4.16c.png";
    private const string EIGHT_BPP_IMAGE_NO_CMAP = "weasel8.149g.png";
    private const string EIGHT_BPP_IMAGE_CMAP = "weasel8.240c.png";
    private const string RGB_IMAGE = "marge.jpg";

    [RegFact]
    public void Expand()
    {
        int i, w, h, format;
        string[] filename = {BINARY_IMAGE,
                             TWO_BPP_IMAGE_NO_CMAP, TWO_BPP_IMAGE_CMAP,
                             FOUR_BPP_IMAGE_NO_CMAP, FOUR_BPP_IMAGE_CMAP,
                             EIGHT_BPP_IMAGE_NO_CMAP, EIGHT_BPP_IMAGE_CMAP,
                             RGB_IMAGE};
        Box box;
        Pix pixs, pix1, pix2, pix3, pix4, pix5, pix6;
        Pixa pixa;
        var rp = new RegTest("expand");

        pixa = Pixa.Create(0);
        for (i = 0; i < 8; i++) {
            pixs = TestPaths.ReadInput(filename[i]);
            pix1 = pixs.ExpandReplicate(2);
            format = (i == 7) ? L.IFF_JFIF_JPEG : L.IFF_PNG;
            rp.WritePixAndCheck(pix1, format);  /* 0 - 7 */
            pixa.AddPix(pix1, L.L_INSERT);
        }
        for (i = 0; i < 8; i++) {
            pixs = TestPaths.ReadInput(filename[i]);
            pix1 = pixs.ExpandReplicate(3);
            format = (i == 7) ? L.IFF_JFIF_JPEG : L.IFF_PNG;
            rp.WritePixAndCheck(pix1, format);  /* 8 - 15 */
            pixa.AddPix(pix1, L.L_INSERT);
        }

        pixs = TestPaths.ReadInput("test1.png");
        w = pixs.Width;
        h = pixs.Height;
        for (i = 1; i <= 15; i++) {
            box = Box.Create(13 * i, 13 * i, w - 13 * i, h - 13 * i);
            pix1 = pixs.ClipRectangle(box, out _)!;
            pix2 = pix1.ExpandReplicate(3);
            rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 16 - 30 */
            pixa.AddPix(pix2, L.L_INSERT);
        }

        /* --------- Power of 2 expansion and reduction -------- */
        pixs = TestPaths.ReadInput("speckle.png");

            /* Test 2x expansion of 1 bpp */
        pix1 = pixs.ExpandBinaryPower2(2);
        pix2 = pix1.ReduceRankBinary2(4);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 31 */
        rp.ComparePix(pixs, pix2);  /* 32 */

            /* Test 2x expansion of 2 bpp */
        pix1 = Pix.Convert1To2(null, pixs, 3, 0);
        pix2 = pix1.ExpandReplicate(2);
        pix3 = pix2.ConvertTo8(false);
        pix4 = pix3.ThresholdToBinary(250);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 33 */
        pix5 = pix4.ReduceRankBinary2(4);
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 34 */
        rp.ComparePix(pixs, pix5);  /* 35 */
        pixa.AddPix(pix5, L.L_INSERT);
        pix6 = pix5.ExpandBinaryPower2(2);
        rp.WritePixAndCheck(pix6, L.IFF_PNG);  /* 36 */
        pixa.AddPix(pix6, L.L_INSERT);

            /* Test 4x expansion of 4 bpp */
        pix1 = Pix.Convert1To4(null, pixs, 15, 0);
        pix2 = pix1.ExpandReplicate(4);
        pix3 = pix2.ConvertTo8(false);
        pix4 = pix3.ThresholdToBinary(250);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 37 */
        pixa.AddPix(pix4, L.L_INSERT);
        pix5 = pix4.ReduceRankBinaryCascade(4, 4, 0, 0);
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 38 */
        rp.ComparePix(pixs, pix5);  /* 39 */
        pixa.AddPix(pix5, L.L_INSERT);

            /* Test 8x expansion of 8 bpp */
        pix1 = pixs.ConvertTo8(false);
        pix2 = pix1.ExpandReplicate(8);
        pix3 = pix2.ThresholdToBinary(250);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 40 */
        pixa.AddPix(pix3, L.L_INSERT);
        pix4 = pix3.ReduceRankBinaryCascade(4, 4, 4, 0);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 41 */
        rp.ComparePix(pixs, pix4);  /* 42 */
        pixa.AddPix(pix4, L.L_INSERT);

        Assert.Equal(42, rp.Index);
        rp.Cleanup();
    }
}
