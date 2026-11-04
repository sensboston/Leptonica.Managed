using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// flipdetect_reg.c: tests the high-level text orientation interface, the 90 degree
/// orientation of text and whether the text is mirror reversed.
/// </summary>
public class FlipDetectRegTests
{
    [RegFact]
    public void FlipDetect()
    {
        int orient, rotation;
        float upconf, leftconf, conf;
        Pix pix, pixs, pix1, pix2;
        Pixa pixa;
        var rp = new RegTest("flipdetect");

        pix = TestPaths.ReadInput("feyn.tif");
        pixs = pix.Scale(0.5f, 0.5f);

            /* Test high-level interface */
        pix1 = pixs.RotateOrth(3);
        pix2 = pix1.OrientCorrect(0.0f, 0.0f, out upconf, out leftconf, out rotation, 0);
        rp.CompareValues(upconf, 2.543f, 0.1f);  /* 0 */
        rp.CompareValues(leftconf, 15.431f, 0.1f);  /* 1 */
        rp.CompareValues(rotation, 90, 0.0f);  /* 2 */
        rp.ComparePix(pixs, pix2);  /* 3 */

            /* Test orientation detection */
        pixa = Pixa.Create(4);
        pix1 = Pix.Copy(null, pixs);
        pix1.OrientDetect(out upconf, out leftconf, 0, 0);
        orient = Pix.MakeOrientDecision(upconf, leftconf, 0, 0, 1);
        rp.CompareValues(upconf, 15.431f, 0.1f);  /* 4 */
        rp.CompareValues(orient, 1, 0.0f);  /* 5 */
        pixa.AddPix(pix1, L.L_INSERT);
        pix2 = pix1.Rotate90(1);
        pix1 = pix2;
        pix1.OrientDetect(out upconf, out leftconf, 0, 0);
        orient = Pix.MakeOrientDecision(upconf, leftconf, 0, 0, 1);
        rp.CompareValues(leftconf, -15.702f, 0.1f);  /* 6 */
        rp.CompareValues(orient, 4, 0.0f);  /* 7 */
        pixa.AddPix(pix1, L.L_INSERT);
        pix2 = pix1.Rotate90(1);
        pix1 = pix2;
        pix1.OrientDetect(out upconf, out leftconf, 0, 0);
        orient = Pix.MakeOrientDecision(upconf, leftconf, 0, 0, 1);
        rp.CompareValues(upconf, -15.702f, 0.1f);  /* 8 */
        rp.CompareValues(orient, 3, 0.0f);  /* 9 */
        pixa.AddPix(pix1, L.L_INSERT);
        pix2 = pix1.Rotate90(1);
        pix1 = pix2;
        pix1.OrientDetect(out upconf, out leftconf, 0, 0);
        orient = Pix.MakeOrientDecision(upconf, leftconf, 0, 0, 1);
        rp.CompareValues(leftconf, 15.431f, 0.1f);  /* 10 */
        rp.CompareValues(orient, 2, 0.0f);  /* 11 */
        pixa.AddPix(pix1, L.L_INSERT);

        pix2 = pixa.DisplayTiledInColumns(2, 0.25f, 20, 2);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 12 */

        conf = pixs.MirrorDetect(0, rp.Display ? 1 : 0);
        rp.CompareValues(conf, 4.128f, 0.1f);  /* 13 */

        Assert.Equal(13, rp.Index);
        rp.Cleanup();
    }
}
