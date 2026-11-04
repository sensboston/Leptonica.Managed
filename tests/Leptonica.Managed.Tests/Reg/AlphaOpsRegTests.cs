using System.IO;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// alphaops_reg.c: tests various functions that use the alpha layer.
/// </summary>
public class AlphaOpsRegTests
{
    [RegFact]
    public void AlphaOps()
    {
        var rp = new RegTest("alphaops");

        /* ------------------------ (1) ----------------------------*/
            /* Blend with a white background */
        Pix pix1 = TestPaths.ReadInput("books_logo.png");
        Pix pix2 = pix1.AlphaBlendUniform(0xffffff00u);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0 */
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 1 */

            /* Generate an alpha layer based on the white background */
        Pix pix3 = pix2.SetAlphaOverWhite();
        pix3.Spp = 3;
            /* without alpha */
        Pix.Write("/tmp/lept/regout/alphaops.2.png", pix3, L.IFF_PNG);
        rp.CheckFile("/tmp/lept/regout/alphaops.2.png");   /* 2 */
        pix3.Spp = 4;
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 3, with alpha */

            /* Render on a light yellow background */
        Pix pix4 = pix3.AlphaBlendUniform(0xffffe000u);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 4 */

        /* ------------------------ (2) ----------------------------*/
        Utils.LeptMkdir("lept/alpha");
        Pix pixs = TestPaths.ReadInput("feyn-fract.tif");
        pixs.GetDimensions(out int w, out int h, out _);
        Pix pixg = Pix.Convert1To8(null, pixs, 0, 255);
        Pix pixg2 = pixg.BlockconvGray(null, 1, 1);
        rp.WritePixAndCheck(pixg2, L.IFF_JFIF_JPEG);  /* 5 */

        Pix pixc = TestPaths.ReadInput("tetons.jpg");
        Pix pixcs1 = pixc.ScaleToSize(w, h);
        rp.WritePixAndCheck(pixcs1, L.IFF_JFIF_JPEG);  /* 6 */
        Pix pixb = Pix.CreateTemplate(pixcs1);  /* black */
        Pix pixd1 = pixb.BlendWithGrayMask(pixcs1, pixg2, 0, 0);
        rp.WritePixAndCheck(pixd1, L.IFF_JFIF_JPEG);  /* 7 */

        pixcs1.SetRGBComponent(pixg2, L.L_ALPHA_CHANNEL);
        Pix.Write("/tmp/lept/alpha/cs1.png", pixcs1, L.IFF_PNG);
        Pix pixcs2 = pixcs1.SetUnderTransparency(0, 0);
        Pix.Write("/tmp/lept/alpha/cs2.png", pixcs2, L.IFF_PNG);

        Pix pixd2 = pixb.BlendWithGrayMask(pixcs2, pixg2, 0, 0);
        rp.WritePixAndCheck(pixd2, L.IFF_JFIF_JPEG);  /* 8 */

        lock (TestPaths.PngReadLock) {
            pix1 = Pix.Read("/tmp/lept/alpha/cs1.png");  /* just pixcs1 */
            pix2 = Pix.Read("/tmp/lept/alpha/cs2.png");  /* cleaned under transparent */
        }
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 9 */
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 10 */

        Pixa pixa = Pixa.Create(0);
        pixa.AddPix(pixg2, L.L_INSERT);
        pixa.AddPix(pixcs1, L.L_INSERT);
        pixa.AddPix(pix1, L.L_INSERT);
        pixa.AddPix(pixd1, L.L_INSERT);
        pixa.AddPix(pixd2, L.L_INSERT);
        pixa.AddPix(pix2, L.L_INSERT);
        Pix pixd = pixa.DisplayTiledInColumns(1, 1.0f, 20, 2);
        rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);  /* 11 */

        /* ------------------------ (3) ----------------------------*/
        uint color = 0xffffa000u;
        float gamma = 1.0f;
        int minval = 0;
        int maxval = 200;
        Box box = Box.Create(0, 85, 600, 100);
        pixa = Pixa.Create(6);
        pixa.AddPix(TestPaths.ReadInput("blend-green1.jpg"), L.L_INSERT);
        pixa.AddPix(TestPaths.ReadInput("blend-green2.png"), L.L_INSERT);
        pixa.AddPix(TestPaths.ReadInput("blend-green3.png"), L.L_INSERT);
        pixa.AddPix(TestPaths.ReadInput("blend-orange.jpg"), L.L_INSERT);
        pixa.AddPix(TestPaths.ReadInput("blend-yellow.jpg"), L.L_INSERT);
        pixa.AddPix(TestPaths.ReadInput("blend-red.png"), L.L_INSERT);
        int n = pixa.Count;
        Pixa pixa2 = Pixa.Create(n);
        Pixa pixa3 = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            pix1 = pixa.GetPix(i, L.L_CLONE);
            pix2 = DoBlendTest(pix1, box, color, gamma, minval, maxval, 1);
            rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 12, 14, ... 22 */
            pixa2.AddPix(pix2, L.L_INSERT);
            pix2 = DoBlendTest(pix1, box, color, gamma, minval, maxval, 2);
            rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 13, 15, ... 23 */
            pixa3.AddPix(pix2, L.L_INSERT);
        }

        /* ------------------------ (4) ----------------------------*/
            /* Use one image as the alpha component for a second image */
        pix1 = TestPaths.ReadInput("test24.jpg");
        pix2 = TestPaths.ReadInput("marge.jpg");
        pix3 = pix2.Scale(1.9f, 2.2f);
        pix4 = pix3.ConvertTo8(false);
        pix1.SetRGBComponent(pix4, L.L_ALPHA_CHANNEL);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 24 */

            /* Set the alpha value in a colormap to bval */
        Pix pix5 = pix1.OctreeColorQuant(128, 0);
        PixColormap cmap = pix5.Colormap!;
        n = cmap.Count;
        for (int i = 0; i < n; i++) {
            cmap.GetColor(i, out _, out _, out int bval);
            cmap.SetAlpha(i, bval);
        }

            /* Test binary serialization/deserialization of colormap with alpha */
        byte[] data = cmap.SerializeToMemory(4, out int ncolors);
        PixColormap cmap2 = PixColormap.DeserializeFromMemory(data, 4, ncolors);
        bool equal = PixColormap.CmapEqual(cmap, cmap2, 4);
        rp.CompareValues(1, equal ? 1 : 0, 0.0f);  /* 25 */

            /* Test ascii serialization/deserialization of colormap with alpha */
        using (FileStream fp = Utils.FopenWriteStream("/tmp/lept/alpha/cmap.4", "wb"))
            cmap.WriteStream(fp);
        using (FileStream fp = Utils.FopenReadStream("/tmp/lept/alpha/cmap.4"))
            cmap2 = PixColormap.ReadStream(fp);
        equal = PixColormap.CmapEqual(cmap, cmap2, 4);
        rp.CompareValues(1, equal ? 1 : 0, 0.0f);  /* 26 */

            /* Test r/w for cmapped pix with non-opaque alpha */
        rp.WritePixAndCheck(pix5, L.IFF_PNG);  /* 27 */
        Pix.Write("/tmp/lept/alpha/fourcomp.png", pix5, L.IFF_PNG);
        Pix pix6;
        lock (TestPaths.PngReadLock)
            pix6 = Pix.Read("/tmp/lept/alpha/fourcomp.png");
        rp.ComparePix(pix5, pix6);  /* 28 */

        Assert.Equal(28, rp.Index);
        rp.Cleanup();
    }

    private static Pix DoBlendTest(Pix pix, Box box, uint color, float gamma,
                                   int minval, int maxval, int which)
    {
        Pixa pixa = Pixa.Create(5);
        Pix pix1 = pix.RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR);
        Pix pix2 = Pix.Copy(null, pix1);
        pixa.AddPix(pix2, L.L_COPY);
        Pix pix3 = which == 1
            ? Pix.BlendBackgroundToColor(null, pix2, box, color, gamma, minval, maxval)
            : Pix.MultiplyByColor(null, pix2, box, color);
        pixa.AddPix(pix3, L.L_INSERT);
        if (which == 1)
            Pix.BlendBackgroundToColor(pix2, pix2, box, color, gamma, minval, maxval);
        else
            Pix.MultiplyByColor(pix2, pix2, box, color);
        pixa.AddPix(pix2, L.L_INSERT);
        pix2 = Pix.Copy(null, pix1);
        pix3 = which == 1
            ? Pix.BlendBackgroundToColor(null, pix2, null, color, gamma, minval, maxval)
            : Pix.MultiplyByColor(null, pix2, null, color);
        pixa.AddPix(pix3, L.L_INSERT);
        if (which == 1)
            Pix.BlendBackgroundToColor(pix2, pix2, null, color, gamma, minval, maxval);
        else
            Pix.MultiplyByColor(pix2, pix2, null, color);
        pixa.AddPix(pix2, L.L_INSERT);

        return pixa.DisplayTiledInRows(32, 800, 1.0f, 0, 30, 2);
    }
}
