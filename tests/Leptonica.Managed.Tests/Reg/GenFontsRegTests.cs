using System.IO;
using System.Text;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// genfonts_reg.c: generation of bitmap font characters from file and compiled strings.
/// </summary>
public class GenFontsRegTests
{
    private static readonly int[] Sizes = { 4, 6, 8, 10, 12, 14, 16, 18, 20 };

    private static readonly string[] InputFonts = {
        "chars-4.tif", "chars-6.tif", "chars-8.tif", "chars-10.tif", "chars-12.tif",
        "chars-14.tif", "chars-16.tif", "chars-18.tif", "chars-20.tif"
    };

    private static readonly string[] OutputFonts = {
        "chars-4.pa", "chars-6.pa", "chars-8.pa", "chars-10.pa", "chars-12.pa",
        "chars-14.pa", "chars-16.pa", "chars-18.pa", "chars-20.pa"
    };

    [RegFact]
    public void GenFonts()
    {
        var rp = new RegTest("genfonts");

        /* ------------ Generate pixa char bitmap files from file ----------- */
        Utils.LeptRmdir("lept/filefonts");
        Utils.LeptMkdir("lept/filefonts");
        for (int i = 0; i < 9; i++) {
            Pixa.SaveFont(Path.Combine(TestPaths.ProgDir, "fonts"), "/tmp/lept/filefonts", Sizes[i]);
            string pathname = Utils.PathJoin("/tmp/lept/filefonts", OutputFonts[i]);
            Pixa pixa = Pixa.Read(pathname);
            Pix pixd = pixa.DisplayTiled(1500, 0, 15);
            rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 0 - 8 */
        }
        Utils.LeptRmdir("lept/filefonts");

        /* ---------- Generate pixa char bitmap files from string --------- */
        Utils.LeptRmdir("lept/strfonts");
        Utils.LeptMkdir("lept/strfonts");
        for (int i = 0; i < 9; i++) {
            Pixa.SaveFont(null, "/tmp/lept/strfonts", Sizes[i]);
            string pathname = Utils.PathJoin("/tmp/lept/strfonts", OutputFonts[i]);
            Pixa pixa = Pixa.Read(pathname);
            Pix pixd = pixa.DisplayTiled(1500, 0, 15);
            rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 9 - 17 */
        }

        /* ----- Use pixaGetFont() and write the result out ----- */
        Utils.LeptRmdir("lept/pafonts");
        Utils.LeptMkdir("lept/pafonts");
        for (int i = 0; i < 9; i++) {
            Pixa pixa = Pixa.GetFont("/tmp/lept/strfonts", Sizes[i], out _, out _, out _)!;
            string buf = $"/tmp/lept/pafonts/chars-{Sizes[i]}.pa";
            pixa.Write(buf);
        }
        Utils.LeptRmdir("lept/pafonts");

        /* ------- Generate 4/3 encoded ascii strings from tiff files ------ */
        Utils.LeptRmdir("lept/encfonts");
        Utils.LeptMkdir("lept/encfonts");
        Pix? pix1 = null;
        for (int i = 0; i < 9; i++) {
            int fontsize = 2 * i + 4;
            string pathname = Path.Combine(TestPaths.ProgDir, "fonts", InputFonts[i]);
            byte[] data1 = Utils.BinaryRead(pathname);
            string datastr = LeptEncoding.EncodeBase64(data1);
            string formstr = LeptEncoding.ReformatPacked64(datastr, 4, 72, true);
            string buf = $"/tmp/lept/encfonts/formstr_{fontsize}.txt";
            Utils.BinaryWrite(buf, "w", Encoding.ASCII.GetBytes(formstr));
            rp.CheckFile(buf);  /* 18 - 26 */
            if (i == 8) {
                using var ms1 = new MemoryStream(data1);
                pix1 = Pix.ReadStream(ms1, 0);
            }

            byte[] data2 = LeptEncoding.DecodeBase64(datastr);
            Utils.BinaryWrite($"/tmp/lept/encfonts/image_{fontsize}.tif", "w", data2);
            if (i == 8) {
                using var ms2 = new MemoryStream(data2);
                Pix pix2 = Pix.ReadStream(ms2, 0);
                rp.ComparePix(pix1!, pix2);  /* 27 */
            }
        }

        Assert.Equal(27, rp.Index);
        rp.Cleanup();
    }

    [RegFact]
    public void GenFontsFromCompiledStringsAndBase64()
    {
        var rp = new RegTest("genfonts");
        Utils.LeptRmdir("lept/strfonts_live");
        Utils.LeptMkdir("lept/strfonts_live");
        Utils.LeptRmdir("lept/encfonts_live");
        Utils.LeptMkdir("lept/encfonts_live");

        for (int i = 0; i < 9; i++) {
            Pixa.SaveFont(null, "/tmp/lept/strfonts_live", Sizes[i]);
            Pixa pixa = Pixa.GetFont("/tmp/lept/strfonts_live", Sizes[i], out _, out _, out _)!;
            Pix pixd = pixa.DisplayTiled(1500, 0, 15);
            string goldenPng = Path.Combine(TestPaths.GoldenDir!, $"genfonts_golden.{9 + i:D2}.png");
            Pix pixGolden;
            lock (TestPaths.PngReadLock)
                pixGolden = Pix.Read(goldenPng);
            Assert.True(Pix.Equal(pixGolden, pixd));

            int fontsize = 2 * i + 4;
            string pathname = Path.Combine(TestPaths.ProgDir, "fonts", InputFonts[i]);
            byte[] data1 = Utils.BinaryRead(pathname);
            string datastr = LeptEncoding.EncodeBase64(data1);
            string formstr = LeptEncoding.ReformatPacked64(datastr, 4, 72, true);
            string goldenTxt = File.ReadAllText(Path.Combine(TestPaths.GoldenDir!, $"genfonts_golden.{18 + i:D2}.txt"))
                                   .Replace("\r\n", "\n");
            Assert.Equal(goldenTxt, formstr);
            Assert.Equal(data1, LeptEncoding.DecodeBase64(datastr));
        }

        Utils.LeptRmdir("lept/strfonts_live");
        Utils.LeptRmdir("lept/encfonts_live");
        rp.Cleanup();
    }
}
