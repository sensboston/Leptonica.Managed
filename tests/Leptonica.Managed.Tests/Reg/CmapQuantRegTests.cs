using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// cmapquant_reg.c: tests quantization of rgb image to a specific colormap.
/// </summary>
public class CmapQuantRegTests
{
    private const int LEVEL = 3;
    private const int MIN_DEPTH = 4;

    [RegFact]
    public void CmapQuant()
    {
        var rp = new RegTest("cmapquant");

        Pix pixs = TestPaths.ReadInput("lucasta-frag.jpg");

            /* Convert to 4 bpp with 6 levels and a colormap */
        Pix pix1 = pixs.ThresholdTo4bpp(6, true);

            /* Color some non-white pixels, preserving antialiasing, and
             * adding these colors to the colormap */
        Box box = Box.Create(120, 30, 200, 200);
        pix1.ColorGray(box, L.L_PAINT_DARK, 220, 0, 0, 255);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0 */

            /* Scale up by 1.5; losing the colormap */
        Pix pix2 = pix1.Scale(1.5f, 1.5f);
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 1 */

            /* Octcube quantize using the same colormap */
        PixColormap cmap = pix1.Colormap!;
        Pix pix3 = pix2.OctcubeQuantFromCmap(cmap, MIN_DEPTH,
                                             LEVEL, L.L_EUCLIDEAN_DISTANCE);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 2 */

            /* Convert the quantized image to rgb */
        Pix pix4 = pix3.ConvertTo32();

            /* Re-quantize using median cut */
        Pix pix5 = pix4.MedianCutQuant(0);
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 3 */

            /* Re-quantize to few colors using median cut */
        Pix pix6 = pix4.FewColorsMedianCutQuantMixed(30, 30, 100, 0, 0, 0)!;
        rp.WritePixAndCheck(pix6, L.IFF_PNG);  /* 4 */

            /* Octcube quantize mixed with gray */
        Pix pix7 = pix2.OctcubeQuantMixedWithGray(4, 5, 5);
        rp.WritePixAndCheck(pix7, L.IFF_PNG);  /* 5 */

            /* Fixed octcube quantization */
        Pix pix8 = pix2.FixedOctcubeQuant256(0);
        rp.WritePixAndCheck(pix8, L.IFF_PNG);  /* 6 */

            /* Remove unused colors */
        Pix pix9 = Pix.Copy(null, pix8);
        pix9.RemoveUnusedColors();
        rp.WritePixAndCheck(pix9, L.IFF_PNG);  /* 7 */

            /* Compare before and after colors removed */
        rp.ComparePix(pix8, pix9);  /* 8 */

        Assert.Equal(8, rp.Index);
        rp.Cleanup();
    }
}
