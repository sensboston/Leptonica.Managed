using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// psioseg_reg.c: tests PostScript output for images with mixed text and halftone images
/// across depths with and without colormaps.
/// </summary>
public class PsIoSegRegTests
{
    [RegFact]
    public void PsIoSeg()
    {
        var rp = new RegTest("psioseg");

        PrepareImages(rp, out Pix pixs, out Pix pixht, out Pix pixtxt,
                      out Pix pixcs2, out Pix pixmfull, out Pix pix32,
                      out Pix pix8g, out Pix pix8c, out Pix pix4c);

        Utils.LeptMkdir("lept/psio");
        Pix.Write("/tmp/lept/psio/image_001.tif", pixs, L.IFF_TIFF_G4);
        Pix.Write("/tmp/lept/psio/image_002.tif", pixht, L.IFF_TIFF_G4);
        Pix.Write("/tmp/lept/psio/image_003.tif", pixtxt, L.IFF_TIFF_G4);
        Pix.Write("/tmp/lept/psio/image_004.jpg", pixcs2, L.IFF_JFIF_JPEG);
        Pix.Write("/tmp/lept/psio/mask_004.tif", pixmfull, L.IFF_TIFF_G4);
        Pix.Write("/tmp/lept/psio/image_005.jpg", pix32, L.IFF_JFIF_JPEG);
        Pix.Write("/tmp/lept/psio/mask_005.tif", pixht, L.IFF_TIFF_G4);
        Pix.Write("/tmp/lept/psio/image_006.jpg", pix8g, L.IFF_JFIF_JPEG);
        Pix.Write("/tmp/lept/psio/mask_006.tif", pixht, L.IFF_TIFF_G4);
        Pix.Write("/tmp/lept/psio/image_007.png", pix8c, L.IFF_PNG);
        Pix.Write("/tmp/lept/psio/mask_007.tif", pixht, L.IFF_TIFF_G4);
        Pix.Write("/tmp/lept/psio/image_008.png", pix4c, L.IFF_PNG);
        Pix.Write("/tmp/lept/psio/mask_008.tif", pixht, L.IFF_TIFF_G4);

        PsIo.ConvertSegmentedPagesToPS("/tmp/lept/psio", "image_", 6, "/tmp/lept/psio",
                                       "mask_", 5, 0, 10, 2.0f, 0.15f, 190,
                                       "/tmp/lept/regout/psioseg.5.ps");
        rp.CheckFile("/tmp/lept/regout/psioseg.5.ps");  /* 5 */
        /* Note: optional external ps2pdf callSystemDebug in C is omitted */

        Assert.Equal(5, rp.Index);
        rp.Cleanup();
    }

    [RegFact]
    public void PsIoSegImages()
    {
        var rp = new RegTest("psioseg");

        PrepareImages(rp, out _, out _, out _, out _, out _, out _, out _, out _, out _);

        Assert.Equal(4, rp.Index);
        rp.Cleanup();
    }

    private static void PrepareImages(RegTest rp,
                                      out Pix pixs, out Pix pixht, out Pix pixtxt,
                                      out Pix pixcs2, out Pix pixmfull, out Pix pix32,
                                      out Pix pix8g, out Pix pix8c, out Pix pix4c)
    {
        pixs = TestPaths.ReadInput("pageseg2.tif");   /* 1 bpp */
        Pix pixc = TestPaths.ReadInput("tetons.jpg"); /* 32 bpp */

        pixs.GetRegionsBinary(out pixht, out _, out _, null);
        pixtxt = Pix.Subtract(null, pixs, pixht);

        pixs.GetDimensions(out int w, out int h, out _);
        pixc.GetDimensions(out int wc, out _, out _);
        float scalefactor = (float)w / (float)wc;
        Pix pixcs = pixc.Scale(scalefactor, scalefactor);
        pixcs.GetDimensions(out wc, out int hc, out _);
        pixcs2 = Pix.Create(w, h, 32);
        pixcs2.Rasterop(0, 0, w, hc, L.PIX_SRC, pixcs, 0, 0);
        pixcs2.Rasterop(0, hc, w, hc, L.PIX_SRC, pixcs, 0, 0);
        rp.WritePixAndCheck(pixcs2, L.IFF_JFIF_JPEG);  /* 0 */

        pixmfull = Pix.Create(w, h, 1);
        pixmfull.SetAll();

        pix32 = pixtxt.ConvertTo32();
        pix32.CombineMasked(pixcs2, pixht);
        rp.WritePixAndCheck(pix32, L.IFF_JFIF_JPEG);  /* 1 */

        pix8g = pix32.ConvertRGBToLuminance();
        rp.WritePixAndCheck(pix8g, L.IFF_JFIF_JPEG);  /* 2 */

        pix8c = pix32.OctreeColorQuant(240, 0);
        rp.WritePixAndCheck(pix8c, L.IFF_PNG);  /* 3 */

        pix4c = pix32.OctreeQuantNumColors(16, 4);
        rp.WritePixAndCheck(pix4c, L.IFF_PNG);  /* 4 */
    }
}
