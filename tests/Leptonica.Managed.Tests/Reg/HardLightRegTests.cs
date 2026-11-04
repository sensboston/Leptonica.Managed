using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// hardlight_reg.c: hard light blending, not-in-place and in-place, with and
/// without colormaps.
/// </summary>
public class HardLightRegTests
{
    [RegFact]
    public void HardLight()
    {
        var rp = new RegTest("hardlight");

        TestHardlight("hardlight1_1.jpg", "hardlight1_2.jpg", rp);
        TestHardlight("hardlight2_1.jpg", "hardlight2_2.jpg", rp);

        Assert.Equal(17, rp.Index);
        rp.Cleanup();
    }

    private static void TestHardlight(string file1, string file2, RegTest rp)
    {
        Pix pixs1, pixs2, pix1, pix2, pixd;
        Pixa pixa;
        Pixaa paa;

            /* Read in images */
        pixs1 = TestPaths.ReadInput(file1);
        pixs2 = TestPaths.ReadInput(file2);
        paa = Pixaa.Create(0);

            /* ---------- Test not-in-place; no colormaps ----------- */
        pixa = Pixa.Create(0);
        pixa.AddPix(pixs1, L.L_COPY);
        pixa.AddPix(pixs2, L.L_COPY);
        paa.AddPixa(pixa, L.L_INSERT);
        pixd = Pix.BlendHardLight(null, pixs1, pixs2, 0, 0, 1.0f);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 0, 9 */
        pixa = Pixa.Create(0);
        pixa.AddPix(pixd, L.L_INSERT);

        pix2 = pixs2.ConvertTo32();
        pixd = Pix.BlendHardLight(null, pixs1, pix2, 0, 0, 1.0f);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 1, 10 */
        pixa.AddPix(pixd, L.L_INSERT);

        pixd = Pix.BlendHardLight(null, pixs2, pixs1, 0, 0, 1.0f);
        pixa.AddPix(pixd, L.L_INSERT);
        paa.AddPixa(pixa, L.L_INSERT);

            /* ---------- Test not-in-place; colormaps ----------- */
        pixa = Pixa.Create(0);
        pix1 = pixs1.MedianCutQuant(0);
        if (pixs2.Depth == 8)
            pix2 = pixs2.ConvertGrayToColormap8(8);
        else
            pix2 = pixs2.MedianCutQuant(0);
        pixa.AddPix(pix1, L.L_COPY);
        pixa.AddPix(pix2, L.L_COPY);
        paa.AddPixa(pixa, L.L_INSERT);

        pixa = Pixa.Create(0);
        pixd = Pix.BlendHardLight(null, pix1, pixs2, 0, 0, 1.0f);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 2, 11 */
        pixa.AddPix(pixd, L.L_INSERT);

        pixd = Pix.BlendHardLight(null, pix1, pix2, 0, 0, 1.0f);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 3, 12 */
        pixa.AddPix(pixd, L.L_INSERT);

        pixd = Pix.BlendHardLight(null, pix2, pix1, 0, 0, 1.0f);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 4, 13 */
        pixa.AddPix(pixd, L.L_INSERT);
        paa.AddPixa(pixa, L.L_INSERT);

            /* ---------- Test in-place; no colormaps ----------- */
        pixa = Pixa.Create(0);
        Pix.BlendHardLight(pixs1, pixs1, pixs2, 0, 0, 1.0f);
        rp.WritePixAndCheck(pixs1, L.IFF_PNG);  /* 5, 14 */
        pixa.AddPix(pixs1, L.L_INSERT);

        pixs1 = TestPaths.ReadInput(file1);
        pix2 = pixs2.ConvertTo32();
        Pix.BlendHardLight(pixs1, pixs1, pix2, 0, 0, 1.0f);
        rp.WritePixAndCheck(pixs1, L.IFF_PNG);  /* 6, 15 */
        pixa.AddPix(pixs1, L.L_INSERT);

        pixs1 = TestPaths.ReadInput(file1);
        Pix.BlendHardLight(pixs2, pixs2, pixs1, 0, 0, 1.0f);
        rp.WritePixAndCheck(pixs2, L.IFF_PNG);  /* 7, 16 */
        pixa.AddPix(pixs2, L.L_INSERT);
        paa.AddPixa(pixa, L.L_INSERT);

        pixd = paa.DisplayByPixa(4, 1.0f, 20, 20, 0);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 8, 17 */
    }
}
