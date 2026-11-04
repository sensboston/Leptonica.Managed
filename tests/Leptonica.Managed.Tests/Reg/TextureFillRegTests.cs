using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// texturefill_reg.c: filling in a region using nearby patches and mirrored
/// tiling to generate the texture.  Designed to work with amoris.2.150.jpg.
/// </summary>
public class TextureFillRegTests
{
    [RegFact]
    public void TextureFill()
    {
        int bx, by, bw, bh;
        uint pixval;
        Box box1, box2;
        Boxa boxa;
        Pix pixs, pixm, pixd;
        Pix pix0, pix1, pix2, pix3, pix4, pix5, pix6;
        var rp = new RegTest("texturefill");

            /* Find a mask for repainting pixels */
        pixs = TestPaths.ReadInput("amoris.2.150.jpg");
        pix1 = MakeReplacementMask(pixs);
        boxa = pix1.ConnCompBB(8);
        box1 = boxa.GetBox(0, L.L_COPY);

        /*--------------------------------------------------------*
         *                Show the individual steps               *
         *--------------------------------------------------------*/
            /* Locate a good tile to use */
        pixs.FindRepCloseTile(box1, L.L_VERT, 20, 30, 7, out box2!, 1);
        pix0 = Pix.Copy(null, pix1);
        pix0.RenderBox(box2, 2, L.L_SET_PIXELS);

            /* Make a patch using this tile */
        box1.GetGeometry(out bx, out by, out bw, out bh);
        pix2 = pixs.ClipRectangle(box2, out _)!;
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 0 */
        pix3 = pix2.MirroredTiling(bw, bh);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 1 */

            /* Paint the patch through the mask */
        pixd = Pix.Copy(null, pixs);
        pixm = pix1.ClipRectangle(box1, out _)!;
        pixd.CombineMaskedGeneral(pix3, pixm, bx, by);
        rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 2 */

            /* Blend two patches and then overlay.  Use the previous
             * tile found vertically and a new one found horizontally. */
        pixs.FindRepCloseTile(box1, L.L_HORIZ, 20, 30, 7, out box2!, 1);
        pix0.RenderBox(box2, 2, L.L_SET_PIXELS);
        rp.WritePixAndCheck(pix0, L.IFF_TIFF_G4);  /* 3 */
        pix2 = pixs.ClipRectangle(box2, out _)!;
        pix4 = pix2.MirroredTiling(bw, bh);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 4 */
        pix5 = pix3.Blend(pix4, 0, 0, 0.5f)!;
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 5 */
        pix6 = pix1.ClipRectangle(box1, out _)!;
        pixd = Pix.Copy(null, pixs);
        pixd.CombineMaskedGeneral(pix5, pix6, bx, by);
        rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 6 */

        /*--------------------------------------------------------*
         *          Show painting from a color near region        *
         *--------------------------------------------------------*/
        pix2 = Pix.Copy(null, pixs);
        pix2.GetColorNearMaskBoundary(pix1, box1, 20, out pixval, 0);
        pix3 = pix1.ClipRectangle(box1, out _)!;
        box1.GetGeometry(out bx, out by, out _, out _);
        pix2.SetMaskedGeneral(pix3, pixval, bx, by);
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 7 */

        /*--------------------------------------------------------*
         *             Use the higher-level function              *
         *--------------------------------------------------------*/
            /* Use various tile selections and tile blending with one component */
        pix2 = Pix.Copy(null, pixs);
        pix3 = Pix.Copy(null, pixs);
        pix4 = Pix.Copy(null, pixs);
        pix2.PaintSelfThroughMask(pix1, 0, 0, L.L_HORIZ, 30, 50, 5, 10);
        pix3.PaintSelfThroughMask(pix1, 0, 0, L.L_VERT, 30, 50, 5, 0);
        pixs.PaintSelfThroughMask(pix1, 0, 0, L.L_BOTH_DIRECTIONS, 30, 50, 5, 20);
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 8 */
        rp.WritePixAndCheck(pix3, L.IFF_JFIF_JPEG);  /* 9 */
        rp.WritePixAndCheck(pixs, L.IFF_JFIF_JPEG);  /* 10 */

            /* Test with two components; */
        pix5 = Pix.FlipLR(null, pix1);
        Pix.Or(pix5, pix5, pix1);
        pix4.PaintSelfThroughMask(pix5, 0, 0, L.L_BOTH_DIRECTIONS, 50, 100, 5, 9);
        rp.WritePixAndCheck(pix4, L.IFF_JFIF_JPEG);  /* 11 */

        Assert.Equal(11, rp.Index);
        rp.Cleanup();
    }

    private static Pix MakeReplacementMask(Pix pixs)
    {
        Pix pix1, pix2, pix3, pix4;

        pix1 = pixs.MaskOverColorPixels(95, 3);
        pix2 = pix1.MorphSequence("o15.15", 0);
        Pix.SeedfillBinary(pix2, pix2, pix1, 8);
        pix3 = pix2.MorphSequence("c15.15 + d61.31", 0);
        pix4 = pix3.RemoveBorderConnComps(8);
        Pix.Xor(pix4, pix4, pix3);
        return pix4;
    }
}
