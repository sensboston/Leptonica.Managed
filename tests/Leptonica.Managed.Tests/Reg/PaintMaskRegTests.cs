using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// paintmask_reg.c: painting through a mask onto images of various depths, using
/// pixClipMasked(), starting from a 32 bpp RGB image and its derivatives, and
/// finally a clip/mask operation on 1 bpp sources.
/// </summary>
public class PaintMaskRegTests
{
    [RegFact]
    public void PaintMask()
    {
        Box box;
        Pix pixs, pixs8, pixm, pixt1, pixt2, pixd;
        Pixa pixa;
        var rp = new RegTest("paintmask");

        pixa = Pixa.Create(0);

            /* Start with a 32 bpp image and a mask.  Use the
             * same mask for all clip/masked operations. */
        pixs = TestPaths.ReadInput("test24.jpg");
        pixt1 = TestPaths.ReadInput("rabi.png");
        box = Box.Create(303, 1983, 800, 500);
        pixm = pixt1.ClipRectangle(box, out _)!;
        Pix.Invert(pixm, pixm);
        box = Box.Create(100, 100, 800, 500);  /* clips on pixs and derivatives */
        pixt2 = pixs.ClipRectangle(box, out _)!;
        rp.WritePixAndCheck(pixt2, L.IFF_JFIF_JPEG);  /* 0 */
        pixa.AddPix(pixt2, L.L_INSERT);

            /* Clip 32 bpp RGB */
        pixd = pixs.ClipMasked(pixm, 100, 100, 0x03c08000)!;
        rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 1 */
        pixa.AddPix(pixd, L.L_INSERT);

            /* Clip 8 bpp colormapped */
        pixt1 = pixs.MedianCutQuant(0);
        pixt2 = pixt1.ClipRectangle(box, out _)!;
        rp.WritePixAndCheck(pixt2, L.IFF_PNG);  /* 2 */
        pixa.AddPix(pixt2, L.L_INSERT);
        pixd = pixt1.ClipMasked(pixm, 100, 100, 0x03c08000)!;
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 3 */
        pixa.AddPix(pixd, L.L_INSERT);

            /* Clip 4 bpp colormapped */
        pixt1 = pixs.OctreeQuantNumColors(16, 1);
        pixt2 = pixt1.ClipRectangle(box, out _)!;
        rp.WritePixAndCheck(pixt2, L.IFF_PNG);  /* 4 */
        pixa.AddPix(pixt2, L.L_INSERT);
        pixd = pixt1.ClipMasked(pixm, 100, 100, 0x03c08000)!;
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 5 */
        pixa.AddPix(pixd, L.L_INSERT);

            /* Clip 2 bpp colormapped */
        pixt1 = pixs.MedianCutQuantGeneral(0, 2, 4, 5, 1, 1);
        pixt2 = pixt1.ClipRectangle(box, out _)!;
        rp.WritePixAndCheck(pixt2, L.IFF_PNG);  /* 6 */
        pixa.AddPix(pixt2, L.L_INSERT);
        pixd = pixt1.ClipMasked(pixm, 100, 100, 0x03608000)!;
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 7 */
        pixa.AddPix(pixd, L.L_INSERT);

            /* Clip 8 bpp gray */
        pixs8 = pixs.ConvertRGBToLuminance();
        pixt2 = pixs8.ClipRectangle(box, out _)!;
        rp.WritePixAndCheck(pixt2, L.IFF_JFIF_JPEG);  /* 8 */
        pixa.AddPix(pixt2, L.L_INSERT);
        pixd = pixs8.ClipMasked(pixm, 100, 100, 90)!;
        rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 9 */
        pixa.AddPix(pixd, L.L_INSERT);

            /* Clip 4 bpp gray */
        pixt1 = pixs8.ThresholdTo4bpp(16, false);
        pixt2 = pixt1.ClipRectangle(box, out _)!;
        rp.WritePixAndCheck(pixt2, L.IFF_PNG);  /* 10 */
        pixa.AddPix(pixt2, L.L_INSERT);
        pixd = pixt1.ClipMasked(pixm, 100, 100, 0)!;
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 11 */
        pixa.AddPix(pixd, L.L_INSERT);
        pixd = pixt1.ClipMasked(pixm, 100, 100, 5)!;
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 12 */
        pixa.AddPix(pixd, L.L_INSERT);
        pixd = pixt1.ClipMasked(pixm, 100, 100, 15)!;
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 13 */
        pixa.AddPix(pixd, L.L_INSERT);

            /* Clip 4 bpp gray, colormapped */
        pixt1 = pixs8.ThresholdTo4bpp(16, true);
        pixt2 = pixt1.ClipRectangle(box, out _)!;
        rp.WritePixAndCheck(pixt2, L.IFF_PNG);  /* 14 */
        pixa.AddPix(pixt2, L.L_INSERT);
        pixd = pixt1.ClipMasked(pixm, 100, 100, 0x55555500)!;
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 15 */
        pixa.AddPix(pixd, L.L_INSERT);

            /* Clip 2 bpp gray */
        pixt1 = pixs8.ThresholdTo2bpp(4, false);
        pixt2 = pixt1.ClipRectangle(box, out _)!;
        rp.WritePixAndCheck(pixt2, L.IFF_PNG);  /* 16 */
        pixa.AddPix(pixt2, L.L_INSERT);
        pixd = pixt1.ClipMasked(pixm, 100, 100, 1)!;
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 17 */
        pixa.AddPix(pixd, L.L_INSERT);

            /* Clip 2 bpp gray, colormapped */
        pixt1 = pixs8.ThresholdTo2bpp(4, true);
        pixt2 = pixt1.ClipRectangle(box, out _)!;
        pixd = pixt1.ClipMasked(pixm, 100, 100, 0x55555500)!;
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 18 */
        pixa.AddPix(pixd, L.L_INSERT);

            /* Finally, do the 1 bpp painting through clipped region.
             * We start with two 1 bpp text sources, use the inverse
             * of the 2nd for the mask (so we take all of the 1st
             * pixels under this mask), and for the remainder, which
             * are the fg pixels in the 2nd, we paint them black (1).
             * So this is a simple and fast blending of two 1 bpp pix. */
        pixs = TestPaths.ReadInput("feyn.tif");
        box = Box.Create(670, 827, 800, 500);
        pixt2 = pixs.ClipRectangle(box, out _)!;
        rp.WritePixAndCheck(pixt2, L.IFF_PNG);  /* 19 */
        pixa.AddPix(pixt2, L.L_INSERT);
        pixt1 = TestPaths.ReadInput("rabi.png");
        box = Box.Create(303, 1983, 800, 500);
        pixm = pixt1.ClipRectangle(box, out _)!;
        Pix.Invert(pixm, pixm);
        rp.WritePixAndCheck(pixm, L.IFF_PNG);  /* 20 */
        pixa.AddPix(pixm, L.L_INSERT);
        pixd = pixs.ClipMasked(pixm, 670, 827, 1)!;
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 21 */
        pixa.AddPix(pixd, L.L_INSERT);

        Assert.Equal(21, rp.Index);
        rp.Cleanup();
    }
}
