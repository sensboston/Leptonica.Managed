using System;
using System.IO;
using System.Text;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// psio_reg.c: tests writing images in PostScript with arbitrary scaling and translation
/// (uncompressed, DCT/JPEG, CCITT-G4, and Flate).
/// </summary>
public class PsIoRegTests
{
    private static readonly string[] WeaselNames = {
        "weasel2.4c.png", "weasel2.4g.png", "weasel2.png",
        "weasel4.11c.png", "weasel4.8g.png", "weasel4.16g.png",
        "weasel8.16g.png", "weasel8.149g.png", "weasel8.240c.png",
        "weasel8.png", "weasel32.png"
    };

    [RegFact]
    public void PsIo_Reg()
    {
        var rp = new RegTest("psio");
        string progDir = TestPaths.ProgDir;
        const float factor = 0.95f;

            /* Uncompressed PS with scaling but centered on the page */
        Pix pixs = TestPaths.ReadInput("feyn-fract.tif");
        pixs.GetDimensions(out int w, out int h, out _);
        float scale = Math.Min(factor * 2550 / w, factor * 3300 / h);
        using (FileStream fp1 = Utils.FopenWriteStream("/tmp/lept/regout/psio0.ps", "wb+"))
            Pix.WriteStreamPS(fp1, pixs, null, 300, scale);
        rp.CheckFile("/tmp/lept/regout/psio0.ps");  /* 0 */

            /* Uncompressed PS with scaling, with LL corner at (1500, 1500) mils */
        pixs = TestPaths.ReadInput("weasel4.11c.png");
        pixs.GetDimensions(out w, out h, out _);
        scale = Math.Min(factor * 2550 / w, factor * 3300 / h);
        Box box = Box.Create(1500, 1500, (int)(1000 * scale * w / 300), (int)(1000 * scale * h / 300));
        using (FileStream fp1 = Utils.FopenWriteStream("/tmp/lept/regout/psio1.ps", "wb+"))
            Pix.WriteStreamPS(fp1, pixs, box, 300, 1.0f);
        rp.CheckFile("/tmp/lept/regout/psio1.ps");  /* 1 */

            /* DCT compressed PS with LL corner at (300, 1000) pixels */
        pixs = TestPaths.ReadInput("marge.jpg");
        Pix pixt = pixs.ConvertTo32();
        Pix.Write("/tmp/lept/regout/psio2.jpg", pixt, L.IFF_JFIF_JPEG);
        PsIo.ConvertJpegToPS("/tmp/lept/regout/psio2.jpg", "/tmp/lept/regout/psio3.ps",
                             "w", 300, 1000, 0, 4.0f, 1, true);
        rp.CheckFile("/tmp/lept/regout/psio2.jpg");  /* 2 */
        rp.CheckFile("/tmp/lept/regout/psio3.ps");   /* 3 */

            /* Multi-layer G4 + JPEG + Flate pages */
        string feynPath = Path.Combine(progDir, "feyn.tif");
        string margePath = Path.Combine(progDir, "marge.jpg");
        string w4cPath = Path.Combine(progDir, "weasel4.11c.png");
        string w8240cPath = Path.Combine(progDir, "weasel8.240c.png");
        string w8149gPath = Path.Combine(progDir, "weasel8.149g.png");

        PsIo.ConvertG4ToPS(feynPath, "/tmp/lept/regout/psio4.ps", "w", 0, 0, 0, 1.0f, 1, true, false);
        PsIo.ConvertJpegToPS(margePath, "/tmp/lept/regout/psio4.ps", "a", 500, 100, 300, 2.0f, 1, false);
        PsIo.ConvertFlateToPS(w4cPath, "/tmp/lept/regout/psio4.ps", "a", 300, 400, 300, 6.0f, 1, false);
        PsIo.ConvertJpegToPS(margePath, "/tmp/lept/regout/psio4.ps", "a", 100, 800, 300, 1.5f, 1, true);

        PsIo.ConvertG4ToPS(feynPath, "/tmp/lept/regout/psio4.ps", "a", 0, 0, 0, 1.0f, 2, true, false);
        PsIo.ConvertJpegToPS(margePath, "/tmp/lept/regout/psio4.ps", "a", 1000, 700, 300, 2.0f, 2, false);
        PsIo.ConvertJpegToPS(margePath, "/tmp/lept/regout/psio4.ps", "a", 100, 200, 300, 2.0f, 2, true);

        PsIo.ConvertG4ToPS(feynPath, "/tmp/lept/regout/psio4.ps", "a", 0, 0, 0, 1.0f, 3, true, false);
        PsIo.ConvertJpegToPS(margePath, "/tmp/lept/regout/psio4.ps", "a", 200, 200, 300, 2.0f, 3, false);
        PsIo.ConvertJpegToPS(margePath, "/tmp/lept/regout/psio4.ps", "a", 200, 900, 300, 2.0f, 3, true);
        rp.CheckFile("/tmp/lept/regout/psio4.ps");  /* 4 */

        pixs = TestPaths.ReadInput("wyom.jpg");
        pixt = pixs.ScaleToSize(2528, 3300);
        Pix.Write("/tmp/lept/regout/psio5.jpg", pixt, L.IFF_JFIF_JPEG);
        PsIo.ConvertJpegToPS("/tmp/lept/regout/psio5.jpg", "/tmp/lept/regout/psio5.ps", "w", 0, 0, 300, 1.0f, 1, false);
        PsIo.ConvertFlateToPS(w8240cPath, "/tmp/lept/regout/psio5.ps", "a", 100, 100, 300, 5.0f, 1, false);
        PsIo.ConvertFlateToPS(w8149gPath, "/tmp/lept/regout/psio5.ps", "a", 200, 300, 300, 5.0f, 1, false);
        PsIo.ConvertFlateToPS(w4cPath, "/tmp/lept/regout/psio5.ps", "a", 300, 500, 300, 5.0f, 1, false);
        PsIo.ConvertG4ToPS(feynPath, "/tmp/lept/regout/psio5.ps", "a", 0, 0, 0, 1.0f, 1, true, true);

        PsIo.ConvertJpegToPS(margePath, "/tmp/lept/regout/psio5.ps", "a", 500, 100, 300, 2.0f, 2, false);
        PsIo.ConvertFlateToPS(w4cPath, "/tmp/lept/regout/psio5.ps", "a", 300, 400, 300, 6.0f, 2, false);
        PsIo.ConvertJpegToPS(margePath, "/tmp/lept/regout/psio5.ps", "a", 100, 800, 300, 1.5f, 2, false);
        PsIo.ConvertG4ToPS(feynPath, "/tmp/lept/regout/psio5.ps", "a", 0, 0, 0, 1.0f, 2, true, true);

        PsIo.ConvertJpegToPS(margePath, "/tmp/lept/regout/psio5.ps", "a", 500, 100, 300, 2.0f, 3, false);
        PsIo.ConvertJpegToPS(margePath, "/tmp/lept/regout/psio5.ps", "a", 100, 800, 300, 2.0f, 3, false);
        PsIo.ConvertG4ToPS(feynPath, "/tmp/lept/regout/psio5.ps", "a", 0, 0, 0, 1.0f, 3, true, true);

        PsIo.ConvertJpegToPS(margePath, "/tmp/lept/regout/psio5.ps", "a", 700, 700, 300, 2.0f, 4, false);
        PsIo.ConvertFlateToPS(w8149gPath, "/tmp/lept/regout/psio5.ps", "a", 400, 400, 300, 5.0f, 4, false);
        PsIo.ConvertG4ToPS(feynPath, "/tmp/lept/regout/psio5.ps", "a", 0, 0, 0, 1.0f, 4, true, false);
        PsIo.ConvertFlateToPS(w8240cPath, "/tmp/lept/regout/psio5.ps", "a", 100, 220, 300, 5.0f, 4, false);
        PsIo.ConvertJpegToPS(margePath, "/tmp/lept/regout/psio5.ps", "a", 100, 200, 300, 2.0f, 4, true);

        PsIo.ConvertJpegToPS(margePath, "/tmp/lept/regout/psio5.ps", "a", 200, 200, 300, 1.5f, 5, false);
        PsIo.ConvertFlateToPS(w8240cPath, "/tmp/lept/regout/psio5.ps", "a", 140, 80, 300, 7.0f, 5, false);
        PsIo.ConvertG4ToPS(feynPath, "/tmp/lept/regout/psio5.ps", "a", 0, 0, 0, 1.0f, 5, true, false);
        PsIo.ConvertFlateToPS(w8149gPath, "/tmp/lept/regout/psio5.ps", "a", 280, 310, 300, 5.0f, 4, false);
        PsIo.ConvertJpegToPS(margePath, "/tmp/lept/regout/psio5.ps", "a", 200, 900, 300, 2.0f, 5, true);
        rp.CheckFile("/tmp/lept/regout/psio5.ps");  /* 5 */

            /* Generation using segmentation masks */
        PsIo.ConvertSegmentedPagesToPS(progDir, "lion-page", 10, progDir, "lion-mask", 10,
                                       0, 100, 2.0f, 0.8f, 190, "/tmp/lept/regout/psio6.ps");
        rp.CheckFile("/tmp/lept/regout/psio6.ps");  /* 6 */

            /* PS generation for embedding */
        PsIo.ConvertJpegToPSEmbed(Path.Combine(progDir, "tetons.jpg"), "/tmp/lept/regout/psio7.ps");
        rp.CheckFile("/tmp/lept/regout/psio7.ps");  /* 7 */

        PsIo.ConvertG4ToPSEmbed(Path.Combine(progDir, "feyn-fract.tif"), "/tmp/lept/regout/psio8.ps");
        rp.CheckFile("/tmp/lept/regout/psio8.ps");  /* 8 */

        PsIo.ConvertFlateToPSEmbed(w8240cPath, "/tmp/lept/regout/psio9.ps");
        rp.CheckFile("/tmp/lept/regout/psio9.ps");  /* 9 */

            /* Writing compressed from a pixa */
        Sarray sa = Sarray.Create(0);
        for (int i = 0; i < WeaselNames.Length; i++)
            sa.AddString(Path.Combine(progDir, WeaselNames[i]), L.L_COPY);
        Pixa pixa = Pixa.ReadFilesSA(sa);
        pixa.WriteCompressedToPS("/tmp/lept/regout/psio10.ps", 0, 3);
        rp.CheckFile("/tmp/lept/regout/psio10.ps");  /* 10 */

        Assert.Equal(10, rp.Index);
        rp.Cleanup();
    }

    [RegFact]
    public void PsIoUncompressedAndFlate()
    {
        var rp = new RegTest("psio");
        string progDir = TestPaths.ProgDir;
        const float factor = 0.95f;

            /* 0: Uncompressed PS with scaling centered on page (exact byte match vs golden 00.ps) */
        Pix pixs = TestPaths.ReadInput("feyn-fract.tif");
        pixs.GetDimensions(out int w, out int h, out _);
        float scale = Math.Min(factor * 2550 / w, factor * 3300 / h);
        using (FileStream fp1 = Utils.FopenWriteStream("/tmp/lept/regout/psio0.ps", "wb+"))
            Pix.WriteStreamPS(fp1, pixs, null, 300, scale);
        rp.CheckFile("/tmp/lept/regout/psio0.ps");  /* 0 */

            /* 1: Uncompressed PS with box at (1500, 1500) mils (exact byte match vs golden 01.ps) */
        pixs = TestPaths.ReadInput("weasel4.11c.png");
        pixs.GetDimensions(out w, out h, out _);
        scale = Math.Min(factor * 2550 / w, factor * 3300 / h);
        Box box = Box.Create(1500, 1500, (int)(1000 * scale * w / 300), (int)(1000 * scale * h / 300));
        using (FileStream fp1 = Utils.FopenWriteStream("/tmp/lept/regout/psio1.ps", "wb+"))
            Pix.WriteStreamPS(fp1, pixs, box, 300, 1.0f);
        rp.CheckFile("/tmp/lept/regout/psio1.ps");  /* 1 */

        Assert.Equal(1, rp.Index);
        rp.Cleanup();

            /* 9 & 10: Flate PS embed and 11-page Pixa level-3 Flate PS */
        string ps9 = Utils.GenPathname("/tmp/lept/regout/psio9.ps", null);
        PsIo.ConvertFlateToPSEmbed(Path.Combine(progDir, "weasel8.240c.png"), ps9);
        string ps9Text = File.ReadAllText(ps9, Encoding.Latin1);
        string g09Text = File.ReadAllText(Path.Combine(TestPaths.GoldenDir!, "psio_golden.09.ps"), Encoding.Latin1);
        Assert.Equal(g09Text.Substring(0, g09Text.IndexOf("~>\n") + 3),
                     ps9Text.Substring(0, ps9Text.IndexOf("~>\n") + 3));

        Sarray sa = Sarray.Create(0);
        for (int i = 0; i < WeaselNames.Length; i++)
            sa.AddString(Path.Combine(progDir, WeaselNames[i]), L.L_COPY);
        Pixa pixa = Pixa.ReadFilesSA(sa);
        Assert.Equal(11, pixa.Count);
        string ps10 = Utils.GenPathname("/tmp/lept/regout/psio10.ps", null);
        pixa.WriteCompressedToPS(ps10, 0, 3);
        string ps10Text = File.ReadAllText(ps10, Encoding.Latin1);
        Assert.Contains("%%Page: 1 1", ps10Text);
        Assert.Contains("%%Page: 11 11", ps10Text);
    }
}
