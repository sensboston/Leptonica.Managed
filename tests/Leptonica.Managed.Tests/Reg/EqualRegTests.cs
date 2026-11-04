using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// equal_reg.c: pixEqual() in many situations, and the quantization of grayscale
/// and color images (to generate a colormapped image), and removal of the
/// colormap to either RGB or grayscale.
/// </summary>
public class EqualRegTests
{
        /* use this set */
    private const string FEYN1 = "feyn.tif";         /* 1 bpp */
    private const string DREYFUS2 = "dreyfus2.png";  /* 2 bpp cmapped */
    private const string DREYFUS4 = "dreyfus4.png";  /* 4 bpp cmapped */
    private const string DREYFUS8 = "dreyfus8.png";  /* 8 bpp cmapped */
    private const string KAREN8 = "karen8.jpg";      /* 8 bpp, not cmapped */
    private const string MARGE32 = "marge.jpg";      /* rgb */

    [RegFact]
    public void Equal()
    {
        Pix pixs, pix1, pix2, pix3, pix4;
        var rp = new RegTest("equal");

        Utils.LeptMkdir("lept/equal");

        pixs = TestPaths.ReadInput(FEYN1);
        Pix.WritePng("/tmp/lept/equal/junkfeyn.png", pixs, 0.0f);
        pix1 = Pix.Read(Utils.GenPathname("/tmp/lept/equal/junkfeyn.png", null));
        rp.ComparePix(pixs, pix1);  /* 0 */

        pixs = TestPaths.ReadInput(DREYFUS2);
        pix1 = pixs.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        pix2 = pixs.RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR);
        pix3 = pix2.OctreeQuantNumColors(64, 1);
        pix4 = pix2.ConvertRGBToColormap(1);
        rp.ComparePix(pixs, pix1);  /* 1 */
        rp.ComparePix(pixs, pix2);  /* 2 */
        rp.ComparePix(pixs, pix3);  /* 3 */
        rp.ComparePix(pixs, pix4);  /* 4 */

        pixs = TestPaths.ReadInput(DREYFUS4);
        pix1 = pixs.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        pix2 = pixs.RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR);
        pix3 = pix2.OctreeQuantNumColors(256, 1);
        pix4 = pix2.ConvertRGBToColormap(1);
        rp.ComparePix(pixs, pix1);  /* 5 */
        rp.ComparePix(pixs, pix2);  /* 6 */
        rp.ComparePix(pixs, pix3);  /* 7 */
        rp.ComparePix(pixs, pix4);  /* 8 */

        pixs = TestPaths.ReadInput(DREYFUS8);
        pix1 = pixs.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        pix2 = pixs.RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR);
        pix3 = pix2.ConvertRGBToColormap(1);
        rp.ComparePix(pixs, pix1);  /* 9 */
        rp.ComparePix(pixs, pix2);  /* 10 */

        pixs = TestPaths.ReadInput(KAREN8);
        pix1 = pixs.ThresholdTo4bpp(16, true);
        pix2 = pix1.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        pix3 = pix1.RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR);
        pix4 = pix3.ConvertRGBToColormap(1);
        rp.ComparePix(pix1, pix2);  /* 11 */
        rp.ComparePix(pix1, pix3);  /* 12 */
        rp.ComparePix(pix1, pix4);  /* 13 */

        pixs = TestPaths.ReadInput(MARGE32);
        pix1 = pixs.OctreeQuantNumColors(32, 0);
        pix2 = pix1.RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR);
        pix3 = pix2.ConvertRGBToColormap(1);
        pix4 = pix2.OctreeQuantNumColors(64, 0);
        rp.ComparePix(pix1, pix2);  /* 14 */
        rp.ComparePix(pix1, pix3);  /* 15 */
        rp.ComparePix(pix1, pix4);  /* 16 */

        Assert.Equal(16, rp.Index);
        rp.Cleanup();
    }
}
