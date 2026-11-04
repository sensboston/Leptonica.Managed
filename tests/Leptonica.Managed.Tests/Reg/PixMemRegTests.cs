using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// pixmem_reg.c: pixCopy() with internal resizing, pixTransferAllData(),
/// pixExtractData() and pixFreeAndSetData().
/// </summary>
/// <remarks>
/// NOTE: the managed Pix has no reference count: Pix.Clone()
/// returns the same object, and pixTransferAllData() and pixExtractData() always
/// take the refcount == 1 branch, moving the data out of the source.  The three
/// cases of the C test that run those functions on a cloned pix (checks 8, 9
/// and 11) are the refcount > 1 branches, which copy the data and leave the
/// source intact; they are emulated here by passing a Pix.Copy() instead of the
/// clone.
/// </remarks>
public class PixMemRegTests
{
    [RegFact]
    public void PixMem()
    {
        Pix pix1, pix2, pix3, pix4, pix5;
        Pix? tmp;
        uint[] data;
        Pixa pixa;
        var rp = new RegTest("pixmem");

        pixa = Pixa.Create(0);

            /* Copy with internal resizing: onto a cmapped image */
        pix1 = TestPaths.ReadInput("weasel4.16c.png");
        pix2 = TestPaths.ReadInput("feyn-fract.tif");
        pix3 = TestPaths.ReadInput("lucasta.150.jpg");
        Pix.Copy(pix3, pix2);
        rp.ComparePix(pix2, pix3);  /* 0 */
        pixa.AddPix(pix3, L.L_INSERT);
        Pix.Copy(pix1, pix3);
        rp.ComparePix(pix1, pix2);  /* 1 */
        pixa.AddPix(pix1, L.L_INSERT);

            /* Copy with internal resizing: from a cmapped image */
        pix1 = TestPaths.ReadInput("weasel4.16c.png");
        pix2 = TestPaths.ReadInput("feyn-fract.tif");
        pix3 = TestPaths.ReadInput("lucasta.150.jpg");
        Pix.Copy(pix2, pix1);
        rp.ComparePix(pix1, pix2);  /* 2 */
        pixa.AddPix(pix2, L.L_INSERT);
        Pix.Copy(pix3, pix2);
        rp.ComparePix(pix2, pix3);  /* 3 */
        pixa.AddPix(pix3, L.L_INSERT);

            /* Transfer of data pixs --> pixd, when pixs is not cloned.
             * pixs is destroyed.  */
        pix1 = TestPaths.ReadInput("weasel4.16c.png");
        pix2 = TestPaths.ReadInput("feyn-fract.tif");
        pix3 = TestPaths.ReadInput("lucasta.150.jpg");
        pix4 = Pix.Copy(null, pix1);
        tmp = pix1;
        pix2.TransferAllData(ref tmp, false, false);
        rp.ComparePix(pix2, pix4);  /* 4 */
        pixa.AddPix(pix2, L.L_COPY);
        tmp = pix2;
        pix3.TransferAllData(ref tmp, false, false);
        rp.ComparePix(pix3, pix4);  /* 5 */
        pixa.AddPix(pix3, L.L_INSERT);

            /* Another transfer of data pixs --> pixd, when pixs is not cloned.
             * pixs is destroyed. */
        pix1 = TestPaths.ReadInput("weasel4.16c.png");
        pix2 = TestPaths.ReadInput("feyn-fract.tif");
        pix3 = TestPaths.ReadInput("lucasta.150.jpg");
        pix4 = Pix.Copy(null, pix1);
        tmp = pix4;
        pix2.TransferAllData(ref tmp, false, false);
        rp.ComparePix(pix1, pix2);  /* 6 */
        pixa.AddPix(pix2, L.L_COPY);
        tmp = pix2;
        pix3.TransferAllData(ref tmp, false, false);
        rp.ComparePix(pix1, pix3);  /* 7 */
        pixa.AddPix(pix3, L.L_INSERT);

            /* Transfer of data pixs --> pixd, when pixs is cloned.
             * pixs has its refcount reduced by 1. */
        pix1 = TestPaths.ReadInput("weasel4.16c.png");
        pix2 = TestPaths.ReadInput("feyn-fract.tif");
        pix3 = TestPaths.ReadInput("lucasta.150.jpg");
        tmp = Pix.Copy(null, pix1);  /* C: pix4 = pixClone(pix1) */
        pix2.TransferAllData(ref tmp, false, false);
        rp.ComparePix(pix1, pix2);  /* 8 */
        pixa.AddPix(pix2, L.L_INSERT);
            /* C: pix5 = pixClone(pix2) is taken before the transfer above, but a clone
             * is the same object, so it sees the data that pix2 has now; the transfer
             * from a clone (refcount 2) copies that data and leaves pix2 intact. */
        pix5 = Pix.Copy(null, pix2);
        tmp = pix5;
        pix3.TransferAllData(ref tmp, false, false);
        rp.ComparePix(pix1, pix3);  /* 9 */
        pixa.AddPix(pix3, L.L_INSERT);

            /* Extraction of data when pixs is not cloned, putting
             * the data into a new template of pixs. */
        pix2 = TestPaths.ReadInput("feyn-fract.tif");
        pix3 = Pix.Copy(null, pix2);  /* for later reference */
        data = pix2.ExtractData();
        pix4 = Pix.CreateTemplateNoInit(pix2);
        pix4.FreeAndSetData(data);
        rp.ComparePix(pix3, pix4);  /* 10 */
        pixa.AddPix(pix4, L.L_INSERT);

            /* Extraction of data when pixs is cloned, putting
             * a copy of the data into a new template of pixs. */
        pix1 = TestPaths.ReadInput("weasel4.16c.png");
        pix2 = Pix.Copy(null, pix1);  /* C: pix2 = pixClone(pix1), refcount 2 */
        data = pix2.ExtractData();  /* C: pixExtractData(pix1) makes a copy of data */
        pix3 = Pix.CreateTemplateNoInit(pix1);
        pix3.FreeAndSetData(data);
        rp.ComparePix(pix1, pix3);  /* 11 */
        pixa.AddPix(pix3, L.L_INSERT);

        pix1 = pixa.DisplayTiledInRows(32, 1500, 0.5f, 0, 30, 2);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 12 */

        Assert.Equal(12, rp.Index);
        rp.Cleanup();
    }
}
