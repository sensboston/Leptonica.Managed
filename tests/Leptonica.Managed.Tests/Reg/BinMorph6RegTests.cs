using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// binmorph6_reg.c: morphology with a sel made from a pix.
/// </summary>
public class BinMorph6RegTests
{
    [RegFact]
    public void BinMorph6()
    {
        Pix pix2, pix3, pix4, pix5, pix6, pix7, pix8;
        var rp = new RegTest("binmorph6");

            /* Test making sel from a pix */
        Pixa pixa = Pixa.Create(10);
        Pix pix1 = TestPaths.ReadInput("feyn-fract.tif");
        Box box1 = Box.Create(507, 65, 60, 36);
        pix2 = pix1.ClipRectangle(box1, out _)!;
        Sel sel = Sel.CreateFromPix(pix2, 6, 6, "life");  /* 610 hits */

            /* Note how the closing tries to put the negative
             * of the sel, inverted spatially, in the background.  */
        pix3 = Pix.Dilate(null, pix1, sel);  /* note the small holes */
        pix4 = Pix.Open(null, pix1, sel);  /* just the sel */
        pix5 = Pix.CloseSafe(null, pix1, sel);  /* expands small holes in dilate */
        pix6 = Pix.Subtract(null, pix3, pix1);
        pix7 = Pix.Subtract(null, pix1, pix5);  /* no pixels because closing
                                                 * is extensive */
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 0 */
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 1 */
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 2 */
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 3 */
        rp.WritePixAndCheck(pix6, L.IFF_PNG);  /* 4 */
        rp.WritePixAndCheck(pix7, L.IFF_PNG);  /* 5 */
        pixa.AddPix(pix1, L.L_INSERT);
        pixa.AddPix(pix3, L.L_INSERT);
        pixa.AddPix(pix4, L.L_INSERT);
        pixa.AddPix(pix5, L.L_INSERT);
        pixa.AddPix(pix6, L.L_INSERT);
        pixa.AddPix(pix7, L.L_INSERT);

        pix8 = pixa.DisplayTiledInColumns(2, 0.75f, 20, 2);
        rp.WritePixAndCheck(pix8, L.IFF_PNG);  /* 6 */

        Assert.Equal(6, rp.Index);
        rp.Cleanup();
    }
}
