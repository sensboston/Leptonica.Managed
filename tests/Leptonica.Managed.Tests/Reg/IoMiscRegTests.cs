using System.IO;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// iomisc_reg.c: tests 16 bpp PNG strip, JPEG chroma sampling, PNG/PNM alpha,
/// colormap serialization/regeneration, and TIFF compressions.
/// </summary>
public class IoMiscRegTests
{
    private static readonly int[] TiffSize = { 65674, 34872, 20482, 20998, 11178, 21500 };

    private static string ProgFile(string name) => TestPaths.ProgFile(name);

    [RegFact]
    public void IoMisc()
    {
        var rp = new RegTest("iomisc");

        Utils.LeptRmdir("lept/io");
        Utils.LeptMkdir("lept/io");

            /* Test 16 to 8 stripping */
        Pix pixs = Pix.Read(ProgFile("test16.tif"));
        Pix.Write("/tmp/lept/io/test16.png", pixs, L.IFF_PNG);
        rp.CheckFile("/tmp/lept/io/test16.png");  /* 0 */
        Pix pix1 = Pix.Read("/tmp/lept/io/test16.png");
        rp.CompareValues(8, pix1.Depth, 0.0f);  /* 1 */
        lock (TestPaths.PngReadLock) {
            Pix.PngSetReadStrip16To8(0);
            try {
                pix1 = Pix.Read("/tmp/lept/io/test16.png");
            } finally {
                Pix.PngSetReadStrip16To8(1);
            }
        }
        rp.CompareValues(16, pix1.Depth, 0.0f);  /* 2 */

            /* Test chroma sampling options in jpeg */
        pixs = Pix.Read(ProgFile("marge.jpg"));
        Pix.Write("/tmp/lept/io/chromatest1.jpg", pixs, L.IFF_JFIF_JPEG);
        rp.CheckFile("/tmp/lept/io/chromatest1.jpg");  /* 3 */
        pixs.SetChromaSampling(0);
        Pix.Write("/tmp/lept/io/chromatest2.jpg", pixs, L.IFF_JFIF_JPEG);
        rp.CheckFile("/tmp/lept/io/chromatest2.jpg");  /* 4 */
        pixs.SetChromaSampling(1);
        Pix.Write("/tmp/lept/io/chromatest3.jpg", pixs, L.IFF_JFIF_JPEG);
        rp.CheckFile("/tmp/lept/io/chromatest3.jpg");  /* 5 */

            /* Test read/write of alpha with png */
        pixs = Pix.Read(ProgFile("books_logo.png"));
        Pix pixg = pixs.GetRGBComponent(L.L_ALPHA_CHANNEL);
        rp.WritePixAndCheck(pixg, L.IFF_PNG);  /* 6 */
        pix1 = pixs.AlphaBlendUniform(0xffffff00);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 7 */
        Pix pix2 = pix1.SetAlphaOverWhite();
        Pix.Write("/tmp/lept/io/logo2.png", pix2, L.IFF_PNG);
        rp.CheckFile("/tmp/lept/io/logo2.png");  /* 8 */
        pixg = pix2.GetRGBComponent(L.L_ALPHA_CHANNEL);
        rp.WritePixAndCheck(pixg, L.IFF_PNG);  /* 9 */
        Pix pix3 = Pix.Read("/tmp/lept/io/logo2.png");
        Pix pix4 = pix3.AlphaBlendUniform(0x00ffff00);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 10 */

            /* Colormap serialization */
        pixs = Pix.Read(ProgFile("weasel4.11c.png"));
        string cmap1Path = Utils.GenPathname("/tmp/lept/io/cmap1", null);
        using (FileStream fp = File.Create(cmap1Path))
            pixs.Colormap!.WriteStream(fp);
        rp.CheckFile("/tmp/lept/io/cmap1");  /* 11 */
        PixColormap cmap;
        using (FileStream fp = File.OpenRead(cmap1Path))
            cmap = PixColormap.ReadStream(fp);
        string cmap2Path = Utils.GenPathname("/tmp/lept/io/cmap2", null);
        using (FileStream fp = File.Create(cmap2Path))
            cmap.WriteStream(fp);
        rp.CheckFile("/tmp/lept/io/cmap2");  /* 12 */

            /* Remove and regenerate colormap */
        pix1 = pixs.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 13 */
        pix2 = pix1.ConvertRGBToColormap(1);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 14 */

            /* Remove and regenerate gray colormap */
        pixs = Pix.Read(ProgFile("weasel4.5g.png"));
        pix1 = pixs.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 15 */
        pix2 = pix1.ConvertGrayToColormap();
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 16 */

        rp.CompareValues(pixs.InputFormat, L.IFF_PNG, 0.0f);  /* 17 */

            /* TIFF compressions and headers */
        Pix.ReadHeaderTiff(ProgFile("feyn-fract.tif"), 0, out _, out _, out _, out _, out _, out _, out _);
        pixs = Pix.Read(ProgFile("feyn-fract.tif"));
        int[] formats = {
            L.IFF_TIFF, L.IFF_TIFF_PACKBITS, L.IFF_TIFF_RLE,
            L.IFF_TIFF_G3, L.IFF_TIFF_G4, L.IFF_TIFF_LZW
        };
        for (int i = 0; i < formats.Length; i++) {
            string tpath = $"/tmp/lept/io/fract{i + 1}.tif";
            Pix.Write(tpath, pixs, formats[i]);
            rp.CheckFile(tpath);  /* 18, 20, 22, 24, 26, 28 */
            long size = Utils.NbytesInFile(tpath);
            rp.CompareValues(TiffSize[i], size, 2048.0f);  /* 19, 21, 23, 25, 27, 29 */
        }

            /* Alpha with PNM */
        pixs = Pix.Read(ProgFile("books_logo.png"));
        Pix.Write("/tmp/lept/io/alpha1.pnm", pixs, L.IFF_PNM);
        rp.CheckFile("/tmp/lept/io/alpha1.pnm");  /* 30 */
        pix1 = Pix.Read("/tmp/lept/io/alpha1.pnm");
        rp.ComparePix(pixs, pix1);  /* 31 */

        rp.Cleanup();
    }
}
