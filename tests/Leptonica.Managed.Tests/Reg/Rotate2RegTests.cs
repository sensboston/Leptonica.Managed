using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// rotate2_reg.c: rotation by shear, sampling and area mapping; results from
/// all the various types of rotations.
/// </summary>
public class Rotate2RegTests
{
    private const string BINARY_IMAGE = "test1.png";
    private const string TWO_BPP_IMAGE = "weasel2.4c.png";
    private const string FOUR_BPP_IMAGE1 = "weasel4.11c.png";
    private const string FOUR_BPP_IMAGE2 = "weasel4.16g.png";
    private const string EIGHT_BPP_IMAGE = "test8.jpg";
    private const string EIGHT_BPP_CMAP_IMAGE1 = "dreyfus8.png";
    private const string EIGHT_BPP_CMAP_IMAGE2 = "test24.jpg";
    private const string RGB_IMAGE = "marge.jpg";

    private const float ANGLE1 = (float)(3.14159265 / 30.0);
    private const float ANGLE2 = (float)(3.14159265 / 7.0);

    [RegFact]
    public void Rotate2()
    {
        Pix pixs, pixd;
        var rp = new RegTest("rotate2");

        pixs = TestPaths.ReadInput(BINARY_IMAGE);
        RotateTest(pixs, 1.0f, rp);

        pixs = TestPaths.ReadInput(TWO_BPP_IMAGE);
        RotateTest(pixs, 1.0f, rp);

        pixs = TestPaths.ReadInput(FOUR_BPP_IMAGE1);
        RotateTest(pixs, 1.0f, rp);

        pixs = TestPaths.ReadInput(FOUR_BPP_IMAGE2);
        RotateTest(pixs, 1.0f, rp);

        pixs = TestPaths.ReadInput(EIGHT_BPP_IMAGE);
        RotateTest(pixs, 1.0f, rp);

        pixs = TestPaths.ReadInput(EIGHT_BPP_CMAP_IMAGE1);
        RotateTest(pixs, 1.0f, rp);

        pixs = TestPaths.ReadInput(EIGHT_BPP_CMAP_IMAGE2);
        pixd = pixs.OctreeColorQuant(200, 0);
        RotateTest(pixd, 0.5f, rp);

        pixs = TestPaths.ReadInput(RGB_IMAGE);
        RotateTest(pixs, 0.25f, rp);

        Assert.Equal(15, rp.Index);
        rp.Cleanup();
    }

    private static void RotateTest(Pix pixs, float scale, RegTest rp)
    {
        int w, h, d, outformat;
        Pix pix1, pix2, pix3, pixd;
        Pixa pixa;

        pixs.GetDimensions(out w, out h, out d);
        outformat = (d == 8 || d == 32) ? L.IFF_JFIF_JPEG : L.IFF_PNG;

        pixa = Pixa.Create(0);
        foreach (float angle in new[] { ANGLE1, ANGLE2 }) {
            pixa.AddPix(pixs.Rotate(angle, L.L_ROTATE_SHEAR, L.L_BRING_IN_WHITE, w, h), L.L_INSERT);
            pixa.AddPix(pixs.Rotate(angle, L.L_ROTATE_SHEAR, L.L_BRING_IN_BLACK, w, h), L.L_INSERT);
            pixa.AddPix(pixs.Rotate(angle, L.L_ROTATE_SHEAR, L.L_BRING_IN_WHITE, 0, 0), L.L_INSERT);
            pixa.AddPix(pixs.Rotate(angle, L.L_ROTATE_SHEAR, L.L_BRING_IN_BLACK, 0, 0), L.L_INSERT);
        }
        pixd = pixa.DisplayTiledInColumns(2, scale, 20, 0);
        rp.WritePixAndCheck(pixd, outformat);

        pixa = Pixa.Create(0);
        pixa.AddPix(pixs.Rotate(ANGLE2, L.L_ROTATE_SAMPLING, L.L_BRING_IN_WHITE, w, h), L.L_INSERT);
        pixa.AddPix(pixs.Rotate(ANGLE2, L.L_ROTATE_SAMPLING, L.L_BRING_IN_BLACK, w, h), L.L_INSERT);
        pixa.AddPix(pixs.Rotate(ANGLE2, L.L_ROTATE_SAMPLING, L.L_BRING_IN_WHITE, 0, 0), L.L_INSERT);
        pixa.AddPix(pixs.Rotate(ANGLE2, L.L_ROTATE_SAMPLING, L.L_BRING_IN_BLACK, 0, 0), L.L_INSERT);

        if (pixs.Depth == 1)
            pix1 = pixs.ScaleToGray2();
        else
            pix1 = Pix.Clone(pixs);
        pix2 = pix1.Rotate(ANGLE2, L.L_ROTATE_AREA_MAP, L.L_BRING_IN_WHITE, w, h);
        pixa.AddPix(pix2, L.L_INSERT);
        pix3 = pix1.Rotate(ANGLE2, L.L_ROTATE_AREA_MAP, L.L_BRING_IN_BLACK, w, h);
        pixa.AddPix(pix3, L.L_INSERT);
        pix2 = pix1.Rotate(ANGLE2, L.L_ROTATE_AREA_MAP, L.L_BRING_IN_WHITE, 0, 0);
        pixa.AddPix(pix2, L.L_INSERT);
        pix3 = pix1.Rotate(ANGLE2, L.L_ROTATE_AREA_MAP, L.L_BRING_IN_BLACK, 0, 0);
        pixa.AddPix(pix3, L.L_INSERT);
        pixd = pixa.DisplayTiledInColumns(2, scale, 20, 0);
        rp.WritePixAndCheck(pixd, outformat);
    }
}
