using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// blend3_reg.c: 42 results across 6 input image combinations * 7 blendings.
/// </summary>
public class Blend3RegTests
{
    private const int X = 140;
    private const int Y = 40;

    [RegFact]
    public void Blend3()
    {
        var rp = new RegTest("blend3");
        Pixa pixa = Pixa.Create(6);

        Pix pixt = BlendTest("marge.jpg", "feyn-word.tif", 0.5f);
        pixa.AddPix(pixt, L.L_INSERT);
        rp.WritePixAndCheck(pixt, L.IFF_JFIF_JPEG);  /* 0 */

        pixt = BlendTest("marge.jpg", "weasel8.png", 0.3f);
        pixa.AddPix(pixt, L.L_INSERT);
        rp.WritePixAndCheck(pixt, L.IFF_JFIF_JPEG);  /* 1 */

        pixt = BlendTest("marge.jpg", "weasel8.240c.png", 0.3f);
        pixa.AddPix(pixt, L.L_INSERT);
        rp.WritePixAndCheck(pixt, L.IFF_JFIF_JPEG);  /* 2 */

        pixt = BlendTest("test8.jpg", "feyn-word.tif", 0.5f);
        pixa.AddPix(pixt, L.L_INSERT);
        rp.WritePixAndCheck(pixt, L.IFF_JFIF_JPEG);  /* 3 */

        pixt = BlendTest("test8.jpg", "weasel8.png", 0.5f);
        pixa.AddPix(pixt, L.L_INSERT);
        rp.WritePixAndCheck(pixt, L.IFF_JFIF_JPEG);  /* 4 */

        pixt = BlendTest("test8.jpg", "weasel8.240c.png", 0.6f);
        pixa.AddPix(pixt, L.L_INSERT);
        rp.WritePixAndCheck(pixt, L.IFF_JFIF_JPEG);  /* 5 */

        Assert.Equal(5, rp.Index);
        rp.Cleanup();
    }

    private static Pix BlendTest(string file1, string file2, float fract)
    {
        Pix pixs1 = TestPaths.ReadInput(file1);
        Pix pixs2 = TestPaths.ReadInput(file2);
        int d1 = pixs1.Depth;
        int d2 = pixs2.Depth;
        Pixa pixa = Pixa.Create(7);

        Pix pixd;
        if (d1 == 1) {
            Pix pix1 = pixs1.Blend(pixs2, X, Y, fract)!;
            Pix pix2 = pix1.Blend(pixs2, X, Y + 60, fract)!;
            Pix pix3 = pix2.Blend(pixs2, X, Y + 120, fract)!;
            Pix pix4 = pix3.Blend(pixs2, X, Y + 180, fract)!;
            Pix pix5 = pix4.Blend(pixs2, X, Y + 240, fract)!;
            pixd = pix5.Blend(pixs2, X, Y + 300, fract)!;
        } else {
            Pix pix1 = pixs1.Blend(pixs2, X, Y, fract)!;
            Pix pix2 = pix1.Blend(pixs2, X + 80, Y + 80, fract)!;
            Pix pix3 = pix2.Blend(pixs2, X + 160, Y + 160, fract)!;
            Pix pix4 = pix3.Blend(pixs2, X + 240, Y + 240, fract)!;
            Pix pix5 = pix4.Blend(pixs2, X + 320, Y + 320, fract)!;
            pixd = pix5.Blend(pixs2, X + 360, Y + 360, fract)!;
        }
        pixa.AddPix(pixd, L.L_INSERT);

            /* Gray blend */
        if (d2 >= 8)
            Pix.SnapColor(pixs2, pixs2, 0xffu, 0xffu, 50);
        pixd = Pix.BlendGray(null, pixs1, pixs2, 200, 100, fract,
                             L.L_BLEND_GRAY, 1, 0xffu);
        Pix.BlendGray(pixd, pixd, pixs2, 200, 200, fract,
                      L.L_BLEND_GRAY, 1, 0xffu);
        Pix.BlendGray(pixd, pixd, pixs2, 200, 260, fract,
                      L.L_BLEND_GRAY, 1, 0xffu);
        Pix.BlendGray(pixd, pixd, pixs2, 200, 340, fract,
                      L.L_BLEND_GRAY, 1, 0xffu);
        pixa.AddPix(pixd, L.L_INSERT);

            /* Gray blend (with inverse) */
        if (d2 >= 8)
            Pix.SnapColor(pixs2, pixs2, 0xffu, 0xffu, 50);
        pixd = Pix.BlendGray(null, pixs1, pixs2, 200, 100, fract,
                             L.L_BLEND_GRAY_WITH_INVERSE, 1, 0xffu);
        Pix.BlendGray(pixd, pixd, pixs2, 200, 200, fract,
                      L.L_BLEND_GRAY_WITH_INVERSE, 1, 0xffu);
        Pix.BlendGray(pixd, pixd, pixs2, 200, 260, fract,
                      L.L_BLEND_GRAY_WITH_INVERSE, 1, 0xffu);
        Pix.BlendGray(pixd, pixd, pixs2, 200, 340, fract,
                      L.L_BLEND_GRAY_WITH_INVERSE, 1, 0xffu);
        pixa.AddPix(pixd, L.L_INSERT);

            /* Blend Gray for robustness */
        if (d2 >= 8)
            Pix.SnapColor(pixs2, pixs2, 0xffu, 0xffu, 50);
        pixd = Pix.BlendGrayInverse(null, pixs1, pixs2, 200, 100, fract);
        Pix.BlendGrayInverse(pixd, pixd, pixs2, 200, 200, fract);
        Pix.BlendGrayInverse(pixd, pixd, pixs2, 200, 260, fract);
        Pix.BlendGrayInverse(pixd, pixd, pixs2, 200, 340, fract);
        pixa.AddPix(pixd, L.L_INSERT);

            /* Blend Gray adapted */
        if (d2 >= 8)
            Pix.SnapColor(pixs2, pixs2, 0xffu, 0xffu, 50);
        pixd = Pix.BlendGrayAdapt(null, pixs1, pixs2, 200, 100, fract, 120);
        Pix.BlendGrayAdapt(pixd, pixd, pixs2, 200, 200, fract, 120);
        Pix.BlendGrayAdapt(pixd, pixd, pixs2, 200, 260, fract, 120);
        Pix.BlendGrayAdapt(pixd, pixd, pixs2, 200, 340, fract, 120);
        pixa.AddPix(pixd, L.L_INSERT);

            /* Blend color */
        if (d2 >= 8)
            Pix.SnapColor(pixs2, pixs2, 0xffffff00u, 0xffffff00u, 50);
        pixd = Pix.BlendColor(null, pixs1, pixs2, 200, 100, fract, 1, 0xffffff00u);
        Pix.BlendColor(pixd, pixd, pixs2, 200, 200, fract, 1, 0xffffff00u);
        Pix.BlendColor(pixd, pixd, pixs2, 200, 260, fract, 1, 0xffffff00u);
        Pix.BlendColor(pixd, pixd, pixs2, 200, 340, fract, 1, 0xffffff00u);
        pixa.AddPix(pixd, L.L_INSERT);

            /* Blend color by channel */
        if (d2 >= 8)
            Pix.SnapColor(pixs2, pixs2, 0xffffff00u, 0xffffff00u, 50);
        pixd = Pix.BlendColorByChannel(null, pixs1, pixs2, 200, 100,
                                       (float)(1.6 * fract), fract, (float)(0.5 * fract),
                                       1, 0xffffff00u);
        Pix.BlendColorByChannel(pixd, pixd, pixs2, 200, 200,
                                (float)(1.2 * fract), fract, (float)(0.2 * fract),
                                1, 0xffffff00u);
        Pix.BlendColorByChannel(pixd, pixd, pixs2, 200, 260,
                                (float)(1.6 * fract), (float)(1.8 * fract), (float)(0.3 * fract),
                                1, 0xffffff00u);
        Pix.BlendColorByChannel(pixd, pixd, pixs2, 200, 340,
                                (float)(0.4 * fract), (float)(1.3 * fract), (float)(1.8 * fract),
                                1, 0xffffff00u);
        pixa.AddPix(pixd, L.L_INSERT);

        return pixa.DisplayTiledInRows(32, 2500, 0.5f, 0, 20, 2);
    }
}
