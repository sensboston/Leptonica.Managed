using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>shear1_reg.c: shear, both in place and to a new pix.</summary>
public class Shear1RegTests
{
    private const string BINARY_IMAGE = "test1.png";
    private const string TWO_BPP_IMAGE = "weasel2.4c.png";
    private const string FOUR_BPP_IMAGE1 = "weasel4.11c.png";
    private const string FOUR_BPP_IMAGE2 = "weasel4.16g.png";
    private const string EIGHT_BPP_IMAGE = "test8.jpg";
    private const string EIGHT_BPP_CMAP_IMAGE1 = "dreyfus8.png";
    private const string EIGHT_BPP_CMAP_IMAGE2 = "test24.jpg";
    private const string RGB_IMAGE = "marge.jpg";

    private const float ANGLE1 = (float)(3.14159265 / 12.0);

    [RegFact]
    public void Shear1()
    {
        int index;
        Pix pixs, pix1, pixc, pixd;
        PixColormap cmap;
        var rp = new RegTest("shear1");

        pixs = TestPaths.ReadInput(BINARY_IMAGE);
        pixd = ShearTest1(pixs, 1.0f);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 0 */

            /* We change the black to dark red so that we can see
             * that the IP shear does brings in that color.  It
             * can't bring in black because the cmap is filled. */
        pixs = TestPaths.ReadInput(TWO_BPP_IMAGE);
        cmap = pixs.Colormap!;
        cmap.GetIndex(40, 44, 40, out index);
        cmap.ResetColor(index, 100, 0, 0);
        pixd = ShearTest1(pixs, 1.0f);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 1 */

        pixs = TestPaths.ReadInput(FOUR_BPP_IMAGE1);
        pixd = ShearTest1(pixs, 1.0f);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 2 */

        pixs = TestPaths.ReadInput(FOUR_BPP_IMAGE2);
        pixd = ShearTest1(pixs, 1.0f);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 3 */

        pixs = TestPaths.ReadInput(EIGHT_BPP_IMAGE);
        pix1 = pixs.Scale(0.5f, 0.5f);
        pixd = ShearTest1(pixs, 1.0f);
        rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 4 */

        pixs = TestPaths.ReadInput(EIGHT_BPP_CMAP_IMAGE1);
        pixd = ShearTest1(pixs, 1.0f);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 5 */

        pixs = TestPaths.ReadInput(EIGHT_BPP_CMAP_IMAGE2);
        pix1 = pixs.Scale(0.3f, 0.3f);
        pixd = pix1.OctreeColorQuant(200, 0);
        pixc = ShearTest1(pixd, 1.0f);
        rp.WritePixAndCheck(pixc, L.IFF_JFIF_JPEG);  /* 6 */

        pixs = TestPaths.ReadInput(RGB_IMAGE);
        pix1 = pixs.Scale(0.3f, 0.3f);
        pixd = ShearTest1(pix1, 1.0f);
        rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 7 */

            /* In-place shear on 4 bpp cmapped image */
        pixs = TestPaths.ReadInput(FOUR_BPP_IMAGE1);
        pixd = ShearTest2(pixs, rp);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 12 */

        Assert.Equal(12, rp.Index);
        rp.Cleanup();
    }

    private static Pix ShearTest1(Pix pixs, float scale)
    {
        int w, h, d;
        Pix pix1, pix2, pixd;
        Pixa pixa;

        pixa = Pixa.Create(0);
        pixs.GetDimensions(out w, out h, out d);

        pix1 = Pix.HShear(null, pixs, 0, ANGLE1, L.L_BRING_IN_WHITE);
        pixa.AddPix(pix1, L.L_INSERT);
        pix2 = Pix.HShear(null, pixs, h / 2, ANGLE1, L.L_BRING_IN_WHITE);
        pixa.AddPix(pix2, L.L_INSERT);
        pix1 = Pix.HShear(null, pixs, 0, ANGLE1, L.L_BRING_IN_BLACK);
        pixa.AddPix(pix1, L.L_INSERT);
        pix2 = Pix.HShear(null, pixs, h / 2, ANGLE1, L.L_BRING_IN_BLACK);
        pixa.AddPix(pix2, L.L_INSERT);

        if (pixs.Colormap == null) {
            pix1 = Pix.Copy(null, pixs);
            pix1.HShearIP(0, ANGLE1, L.L_BRING_IN_WHITE);
            pixa.AddPix(pix1, L.L_INSERT);
            pix2 = Pix.Copy(null, pixs);
            pix2.HShearIP(h / 2, ANGLE1, L.L_BRING_IN_WHITE);
            pixa.AddPix(pix2, L.L_INSERT);
            pix1 = Pix.Copy(null, pixs);
            pix1.HShearIP(0, ANGLE1, L.L_BRING_IN_BLACK);
            pixa.AddPix(pix1, L.L_INSERT);
            pix2 = Pix.Copy(null, pixs);
            pix2.HShearIP(h / 2, ANGLE1, L.L_BRING_IN_BLACK);
            pixa.AddPix(pix2, L.L_INSERT);
        }

        if (d == 8 || d == 32 || pixs.Colormap != null) {
            pix1 = pixs.HShearLI(0, ANGLE1, L.L_BRING_IN_WHITE);
            pixa.AddPix(pix1, L.L_INSERT);
            pix2 = pixs.HShearLI(w / 2, ANGLE1, L.L_BRING_IN_WHITE);
            pixa.AddPix(pix2, L.L_INSERT);
            pix1 = pixs.HShearLI(0, ANGLE1, L.L_BRING_IN_BLACK);
            pixa.AddPix(pix1, L.L_INSERT);
            pix2 = pixs.HShearLI(w / 2, ANGLE1, L.L_BRING_IN_BLACK);
            pixa.AddPix(pix2, L.L_INSERT);
        }

        pix1 = Pix.VShear(null, pixs, 0, ANGLE1, L.L_BRING_IN_WHITE);
        pixa.AddPix(pix1, L.L_INSERT);
        pix2 = Pix.VShear(null, pixs, w / 2, ANGLE1, L.L_BRING_IN_WHITE);
        pixa.AddPix(pix2, L.L_INSERT);
        pix1 = Pix.VShear(null, pixs, 0, ANGLE1, L.L_BRING_IN_BLACK);
        pixa.AddPix(pix1, L.L_INSERT);
        pix2 = Pix.VShear(null, pixs, w / 2, ANGLE1, L.L_BRING_IN_BLACK);
        pixa.AddPix(pix2, L.L_INSERT);

        if (pixs.Colormap == null) {
            pix1 = Pix.Copy(null, pixs);
            pix1.VShearIP(0, ANGLE1, L.L_BRING_IN_WHITE);
            pixa.AddPix(pix1, L.L_INSERT);
            pix2 = Pix.Copy(null, pixs);
            pix2.VShearIP(w / 2, ANGLE1, L.L_BRING_IN_WHITE);
            pixa.AddPix(pix2, L.L_INSERT);
            pix1 = Pix.Copy(null, pixs);
            pix1.VShearIP(0, ANGLE1, L.L_BRING_IN_BLACK);
            pixa.AddPix(pix1, L.L_INSERT);
            pix2 = Pix.Copy(null, pixs);
            pix2.VShearIP(w / 2, ANGLE1, L.L_BRING_IN_BLACK);
            pixa.AddPix(pix2, L.L_INSERT);
        }

        if (d == 8 || d == 32 || pixs.Colormap != null) {
            pix1 = pixs.VShearLI(0, ANGLE1, L.L_BRING_IN_WHITE);
            pixa.AddPix(pix1, L.L_INSERT);
            pix2 = pixs.VShearLI(w / 2, ANGLE1, L.L_BRING_IN_WHITE);
            pixa.AddPix(pix2, L.L_INSERT);
            pix1 = pixs.VShearLI(0, ANGLE1, L.L_BRING_IN_BLACK);
            pixa.AddPix(pix1, L.L_INSERT);
            pix2 = pixs.VShearLI(w / 2, ANGLE1, L.L_BRING_IN_BLACK);
            pixa.AddPix(pix2, L.L_INSERT);
        }

        pixd = pixa.DisplayTiledInColumns(4, scale, 20, 0);
        return pixd;
    }

    private static Pix ShearTest2(Pix pixs, RegTest rp)
    {
        int w, h;
        Pix pix1, pix2, pixd;
        Pixa pixa;

        pixa = Pixa.Create(0);
        pixs.GetDimensions(out w, out h, out _);

        pix1 = Pix.HShear(null, pixs, h / 2, ANGLE1, L.L_BRING_IN_WHITE);
        pixa.AddPix(pix1, L.L_INSERT);
        pix2 = Pix.Copy(null, pixs);
        Pix.HShear(pix2, pix2, h / 2, ANGLE1, L.L_BRING_IN_WHITE);
        pixa.AddPix(pix2, L.L_INSERT);
        rp.ComparePix(pix1, pix2);  /* 8 */
        pix1 = Pix.HShear(null, pixs, h / 2, ANGLE1, L.L_BRING_IN_BLACK);
        pixa.AddPix(pix1, L.L_INSERT);
        pix2 = Pix.Copy(null, pixs);
        Pix.HShear(pix2, pix2, h / 2, ANGLE1, L.L_BRING_IN_BLACK);
        pixa.AddPix(pix2, L.L_INSERT);
        rp.ComparePix(pix1, pix2);  /* 9 */

        pix1 = Pix.VShear(null, pixs, w / 2, ANGLE1, L.L_BRING_IN_WHITE);
        pixa.AddPix(pix1, L.L_INSERT);
        pix2 = Pix.Copy(null, pixs);
        Pix.VShear(pix2, pix2, w / 2, ANGLE1, L.L_BRING_IN_WHITE);
        pixa.AddPix(pix2, L.L_INSERT);
        rp.ComparePix(pix1, pix2);  /* 10 */
        pix1 = Pix.VShear(null, pixs, w / 2, ANGLE1, L.L_BRING_IN_BLACK);
        pixa.AddPix(pix1, L.L_INSERT);
        pix2 = Pix.Copy(null, pixs);
        Pix.VShear(pix2, pix2, w / 2, ANGLE1, L.L_BRING_IN_BLACK);
        pixa.AddPix(pix2, L.L_INSERT);
        rp.ComparePix(pix1, pix2);  /* 11 */

        pixd = pixa.DisplayTiledInColumns(2, 1.0f, 20, 0);
        return pixd;
    }
}
