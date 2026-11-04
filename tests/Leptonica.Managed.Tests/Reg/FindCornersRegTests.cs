using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// findcorners_reg.c: extraction of deskewed tickets via barcode morphology and
/// corner detection using HMT Sela.
/// </summary>
public class FindCornersRegTests
{
    private const string SelCross =
        "     xxx     " +
        "     xxx     " +
        "     xxx     " +
        "     xxx     " +
        "     xxx     " +
        "xxxxxxxxxxxxx" +
        "xxxxxxXxxxxxx" +
        "xxxxxxxxxxxxx" +
        "     xxx     " +
        "     xxx     " +
        "     xxx     " +
        "     xxx     " +
        "     xxx     ";

    [RegFact]
    public void FindCorners()
    {
        var rp = new RegTest("findcorners");

        Pix pixs = TestPaths.ReadInput("tickets.tif");
        Boxa boxa = LocateBarcodes(pixs, out Pix? pixdNullable, 0);
        Pix pixd = pixdNullable!;
        rp.WritePixAndCheck(pixd, L.IFF_TIFF_G4);  /* 0 */

        int n = boxa.Count;
        float deg2rad = (float)(3.14159265 / 180.0);
        for (int i = 0; i < n; i++) {
            Box box1 = boxa.GetBox(i, L.L_CLONE);
            Box box2 = Box.AdjustSides(null, box1, -266, 346, -1560, 182);
            Pix pix1 = pixs.ClipRectangle(box2, out _)!;
            pix1.FindSkew(out float angle, out _);
            Pix pix2 = pix1.Rotate(deg2rad * angle, L.L_ROTATE_SAMPLING,
                                   L.L_BRING_IN_WHITE, 0, 0);
            Boxa boxa2 = LocateBarcodes(pix2, out _, 0);
            Box box3 = boxa2.GetBox(0, L.L_CLONE);
            Box box4 = Box.AdjustSides(null, box3, -141, 221, -1535, 157);
            Pix pix3 = pix2.ClipRectangle(box4, out _)!;
            rp.WritePixAndCheck(pix3, L.IFF_TIFF_G4);  /* 1 - 9 */
        }

            /* Downscale by 2x and locate corners */
        Pix pixDown = pixd.Scale(0.5f, 0.5f);
        rp.WritePixAndCheck(pixDown, L.IFF_TIFF_G4);  /* 10 */

            /* Find corners and blit a cross onto each (4 to each barcode) */
        Sela sela = GetCornerSela();
        Pix pixCorners = pixDown.UnionOfMorphOps(sela, L.L_MORPH_HMT);
        Sel sel = Sel.CreateFromString(SelCross, 13, 13, "sel_cross");
        Pix pixOut = Pix.Dilate(null, pixCorners, sel);
        Pix.Xor(pixOut, pixOut, pixDown);
        rp.WritePixAndCheck(pixOut, L.IFF_TIFF_G4);  /* 11 */

        Assert.Equal(11, rp.Index);
        rp.Cleanup();
    }

    private static Boxa LocateBarcodes(Pix pixs, out Pix? ppixd, int flag)
    {
        Pix pix1 = pixs.Scale(0.5f, 0.5f);
        Pix pix2 = pix1.MorphSequence("o1.5 + c15.1 + o10.15 + c20.20", flag);
        Boxa boxa1 = pix2.ConnComp(8);
        Boxa boxa2 = boxa1.SelectBySize(300, 0, L.L_SELECT_WIDTH, L.L_SELECT_IF_GT, out _);
        Boxa boxad = boxa2.Transform(0, 0, 2.0f, 2.0f);
        Pix pix3 = pix2.SelectBySize(300, 0, 8, L.L_SELECT_WIDTH, L.L_SELECT_IF_GT, out _);
        ppixd = pix3.Scale(2.0f, 2.0f);
        return boxad;
    }

    private static Sela GetCornerSela()
    {
        Sela sela1 = Sela.AddHitMiss(null);
        Sela sela2 = Sela.Create(4);
        sela1.FindSelByName("sel_ulc", out _, out Sel? sel);
        sela2.AddSel(sel!, null, 1);
        sela1.FindSelByName("sel_urc", out _, out sel);
        sela2.AddSel(sel!, null, 1);
        sela1.FindSelByName("sel_llc", out _, out sel);
        sela2.AddSel(sel!, null, 1);
        sela1.FindSelByName("sel_lrc", out _, out sel);
        sela2.AddSel(sel!, null, 1);
        return sela2;
    }
}
