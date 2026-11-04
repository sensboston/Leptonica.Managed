using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// rotate1_reg.c: rotation by shear and area mapping; results when images are
/// rotated sequentially multiple times.
/// </summary>
public class Rotate1RegTests
{
    private const string BINARY_IMAGE = "test1.png";
    private const string TWO_BPP_IMAGE = "weasel2.4c.png";
    private const string FOUR_BPP_IMAGE1 = "weasel4.11c.png";
    private const string FOUR_BPP_IMAGE2 = "weasel4.16g.png";
    private const string EIGHT_BPP_IMAGE = "test8.jpg";
    private const string EIGHT_BPP_CMAP_IMAGE1 = "dreyfus8.png";
    private const string EIGHT_BPP_CMAP_IMAGE2 = "test24.jpg";
    private const string RGB_IMAGE = "marge.jpg";

    private const int MODSIZE = 11;  /* set to 11 for display */

    private const float ANGLE1 = (float)(3.14159265 / 12.0);
    private const float ANGLE2 = (float)(3.14159265 / 120.0);
    private const int NTIMES = 24;

    [RegFact]
    public void Rotate1()
    {
        Pix pixs, pixd;
        var rp = new RegTest("rotate1");

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
        RotateTest(pixs, 0.25f, rp);

        pixs = TestPaths.ReadInput(RGB_IMAGE);
        RotateTest(pixs, 1.0f, rp);

        Assert.Equal(67, rp.Index);
        rp.Cleanup();
    }

    private static void RotateTest(Pix pixs, float scale, RegTest rp)
    {
        int w, h, d, i, outformat;
        Pix pixt, pixd;
        Pixa pixa;

        pixa = Pixa.Create(0);
        pixs.GetDimensions(out w, out h, out d);
        outformat = (d == 8 || d == 32) ? L.IFF_JFIF_JPEG : L.IFF_PNG;
        foreach (int type in new[] { L.L_ROTATE_SHEAR, L.L_ROTATE_SAMPLING, L.L_ROTATE_AREA_MAP }) {
            pixd = pixs.Rotate(ANGLE1, type, L.L_BRING_IN_WHITE, w, h);
            for (i = 1; i < NTIMES; i++) {
                if ((i % MODSIZE) == 0) {
                    pixa.AddPix(pixd, L.L_COPY);
                    rp.WritePixAndCheck(pixd, outformat);
                }
                pixt = pixd.Rotate(ANGLE1, type, L.L_BRING_IN_WHITE, w, h);
                pixd = pixt;
            }
        }

        pixd = pixs.RotateAMCorner(ANGLE2, L.L_BRING_IN_WHITE);
        for (i = 1; i < NTIMES; i++) {
            if ((i % MODSIZE) == 0) {
                pixa.AddPix(pixd, L.L_COPY);
                rp.WritePixAndCheck(pixd, outformat);
            }
            pixt = pixd.RotateAMCorner(ANGLE2, L.L_BRING_IN_WHITE);
            pixd = pixt;
        }

        if (d == 32) {
            pixd = pixs.RotateAMColorFast(ANGLE1, 0xb0ffb000);
            for (i = 1; i < NTIMES; i++) {
                if ((i % MODSIZE) == 0) {
                    pixa.AddPix(pixd, L.L_COPY);
                    rp.WritePixAndCheck(pixd, outformat);
                }
                pixt = pixd.RotateAMColorFast(ANGLE1, 0xb0ffb000);
                pixd = pixt;
            }
        }

        pixd = pixa.DisplayTiledInColumns(2, scale, 20, 0);
    }
}
