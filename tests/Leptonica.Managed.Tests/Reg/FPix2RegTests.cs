using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// fpix2_reg.c: regression test for FPix orthogonal rotations and borders.
/// </summary>
public class FPix2RegTests
{
    [RegFact]
    public void FPix2()
    {
        var rp = new RegTest("fpix2");

            /* Test orthogonal rotations */
        Pix pix1 = TestPaths.ReadInput("marge.jpg");
        Pix pix2 = pix1.ConvertTo8(false);
        FPix fpix1 = pix2.ConvertToFPix(1);

        FPix fpix2 = fpix1.RotateOrth(1);
        Pix pix3 = fpix2.ConvertToPix(8, L.L_CLIP_TO_ZERO, 0);
        Pix pix4 = pix2.RotateOrth(1);
        rp.ComparePix(pix3, pix4);  /* 0 */

        FPix fpix3 = fpix1.RotateOrth(2);
        Pix pix5 = fpix3.ConvertToPix(8, L.L_CLIP_TO_ZERO, 0);
        Pix pix6 = pix2.RotateOrth(2);
        rp.ComparePix(pix5, pix6);  /* 1 */

        FPix fpix4 = fpix1.RotateOrth(3);
        Pix pix7 = fpix4.ConvertToPix(8, L.L_CLIP_TO_ZERO, 0);
        Pix pix8 = pix2.RotateOrth(3);
        rp.ComparePix(pix7, pix8);  /* 2 */

            /* Test adding various borders */
        pix1 = TestPaths.ReadInput("marge.jpg");
        pix2 = pix1.ConvertTo8(false);
        fpix1 = pix2.ConvertToFPix(1);

        fpix2 = fpix1.AddMirroredBorder(21, 21, 25, 25);
        pix3 = fpix2.ConvertToPix(8, L.L_CLIP_TO_ZERO, 0);
        pix4 = pix2.AddMirroredBorder(21, 21, 25, 25);
        rp.ComparePix(pix3, pix4);  /* 3 */

        fpix3 = fpix1.AddContinuedBorder(21, 21, 25, 25);
        pix5 = fpix3.ConvertToPix(8, L.L_CLIP_TO_ZERO, 0);
        pix6 = pix2.AddContinuedBorder(21, 21, 25, 25);
        rp.ComparePix(pix5, pix6);  /* 4 */

        Assert.Equal(4, rp.Index);
        rp.Cleanup();
    }
}
