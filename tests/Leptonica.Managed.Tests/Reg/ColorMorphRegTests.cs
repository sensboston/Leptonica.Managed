using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// colormorph_reg.c: regression test for simple color morphological operations.
/// </summary>
public class ColorMorphRegTests
{
    private const int SIZE = 7;

    [RegFact]
    public void ColorMorph()
    {
        var rp = new RegTest("colormorph");

        Pix pixs = TestPaths.ReadInput("wyom.jpg");
        Pixa pixa = Pixa.Create(0);

        Pix pix1 = pixs.ColorMorph(L.L_MORPH_DILATE, SIZE, SIZE);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 0 */
        string buf = $"d{SIZE}.{SIZE}";
        Pix pix2 = pixs.ColorMorphSequence(buf, 0, 0);
        rp.ComparePix(pix1, pix2);  /* 1 */
        pixa.AddPix(pix1, L.L_INSERT);

        pix1 = pixs.ColorMorph(L.L_MORPH_ERODE, SIZE, SIZE);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 2 */
        buf = $"e{SIZE}.{SIZE}";
        pix2 = pixs.ColorMorphSequence(buf, 0, 0);
        rp.ComparePix(pix1, pix2);  /* 3 */
        pixa.AddPix(pix1, L.L_INSERT);

        pix1 = pixs.ColorMorph(L.L_MORPH_OPEN, SIZE, SIZE);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 4 */
        buf = $"o{SIZE}.{SIZE}";
        pix2 = pixs.ColorMorphSequence(buf, 0, 0);
        rp.ComparePix(pix1, pix2);  /* 5 */
        pixa.AddPix(pix1, L.L_INSERT);

        pix1 = pixs.ColorMorph(L.L_MORPH_CLOSE, SIZE, SIZE);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 6 */
        buf = $"c{SIZE}.{SIZE}";
        pix2 = pixs.ColorMorphSequence(buf, 0, 0);
        rp.ComparePix(pix1, pix2);  /* 7 */
        pixa.AddPix(pix1, L.L_INSERT);

        Assert.Equal(7, rp.Index);
        rp.Cleanup();
    }
}
