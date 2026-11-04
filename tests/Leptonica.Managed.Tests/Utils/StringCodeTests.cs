using System;
using System.IO;
using Leptonica;
using Leptonica.Managed.Tests.Reg;
using Xunit;

namespace Leptonica.Managed.Tests;

/// <summary>Tests for stringcode.c.  The templates and a serialized pixa come from testdata/prog.</summary>
public class StringCodeTests
{
    private static string ProgFile(string name) => Path.Combine(TestPaths.ProgDir, name);

    private static bool HaveInputs() =>
        File.Exists(ProgFile("stringtemplate1.txt")) && File.Exists(ProgFile("stringtemplate2.txt")) &&
        File.Exists(ProgFile("circles.pa")) && File.Exists(ProgFile("autogen.137.c"));

    /// <summary>The templates are read from the current directory.</summary>
    private static void CopyTemplates()
    {
        File.Copy(ProgFile("stringtemplate1.txt"), "stringtemplate1.txt", true);
        File.Copy(ProgFile("stringtemplate2.txt"), "stringtemplate2.txt", true);
    }

    [Fact]
    public void GeneratedCodeIsTheSameAsTheUpstreamAutogenFile()
    {
        if (!HaveInputs())
            return;
        CopyTemplates();
        string dir = Path.Combine(Path.GetTempPath(), "strcode_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            /* the description table has the names of the input files */
            string f1 = Path.Combine(dir, "chars-6.pa");
            string f2 = Path.Combine(dir, "chars-10.pa");
            File.Copy(ProgFile("circles.pa"), f1);
            File.Copy(ProgFile("circles.pa"), f2);

            L_StrCode strc = L_StrCode.Create(137);
            strc.Generate(f1, "PIXA");
            strc.Generate(f2, "PIXA");
            Assert.Equal(2, strc.N);
            strc.FinalizeCode(dir);

            /* The files of testdata/prog may have been checked out with CRLF line endings, and
             * the committed autogen.137.c has the config_auto.h lines that the template lacks. */
            string expectedc = File.ReadAllText(ProgFile("autogen.137.c")).Replace("\r\n", "\n")
                .Replace("#ifdef HAVE_CONFIG_H\n#include <config_auto.h>\n#endif  /* HAVE_CONFIG_H */\n\n", "");
            Assert.Equal(expectedc,
                         File.ReadAllText(Path.Combine(dir, "autogen.137.c")).Replace("\r\n", "\n"));

            /* the header has the same text; the gzipped data may differ from zlib's */
            string[] expected = File.ReadAllLines(ProgFile("autogen.137.h"));  /* ReadAllLines splits CRLF and LF */
            string[] actual = File.ReadAllLines(Path.Combine(dir, "autogen.137.h"));
            int ndata = Array.FindIndex(expected, s => s.StartsWith("static const char *l_strdata_0"));
            Assert.True(ndata > 0);
            Assert.Equal(expected[..ndata], actual[..ndata]);
            Assert.Contains("static const char *l_strdata_0 =", actual);
            Assert.Contains("static const char *l_strdata_1 =", actual);
            Assert.Equal(expected[^1], actual[^1]);   /* #endif */
        } finally {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CreateFromFileFindsTheTypesAndSkipsComments()
    {
        if (!HaveInputs())
            return;
        CopyTemplates();
        string dir = Path.Combine(Path.GetTempPath(), "strcode_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            string pa = Path.Combine(dir, "a.pa");
            File.Copy(ProgFile("circles.pa"), pa);
            string list = Path.Combine(dir, "names.txt");
            File.WriteAllText(list, "# names\n" + pa + "\n");
            L_StrCode.CreateFromFile(list, 138, dir);

            string c = File.ReadAllText(Path.Combine(dir, "autogen.138.c"));
            Assert.Contains("l_autodecode_138(l_int32 index)", c);
            Assert.Contains("l_int32   nfunc = 1;", c);
            Assert.Contains("result = (void *)pixaReadMem(data2, size2);", c);
            Assert.Contains("a.pa", c);
            Assert.True(File.Exists(Path.Combine(dir, "autogen.138.h")));
        } finally {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void StructStringsAreFoundFromTheFileContent()
    {
        if (!File.Exists(ProgFile("circles.pa")))
            return;
        string pa = ProgFile("circles.pa");
        Assert.Equal("Pixa", StringCode.GetStructStrFromFile(pa, L.L_STR_NAME));
        Assert.Equal("PIXA", StringCode.GetStructStrFromFile(pa, L.L_STR_TYPE));
        Assert.Equal("pixaRead", StringCode.GetStructStrFromFile(pa, L.L_STR_READER));
        Assert.Equal("pixaReadMem", StringCode.GetStructStrFromFile(pa, L.L_STR_MEMREADER));
        Assert.Throws<ArgumentException>(() => StringCode.GetStructStrFromFile(pa, 7));

        /* a compressed image is a Pix */
        string png = Path.Combine(Path.GetTempPath(), "strcode_" + Guid.NewGuid().ToString("N") + ".png");
        try {
            Pix.Write(png, Pix.Create(10, 10, 8), L.IFF_PNG);
            Assert.Equal("Pix", StringCode.GetStructStrFromFile(png, L.L_STR_NAME));
        } finally {
            File.Delete(png);
        }

        /* neither a leptonica object nor an image */
        string txt = Path.Combine(Path.GetTempPath(), "strcode_" + Guid.NewGuid().ToString("N") + ".txt");
        try {
            File.WriteAllText(txt, "\n\nhello world\n");
            Assert.Throws<LeptonicaException>(() => StringCode.GetStructStrFromFile(txt, L.L_STR_NAME));
        } finally {
            File.Delete(txt);
        }
    }

    [Fact]
    public void GenerateChecksTheType()
    {
        L_StrCode strc = L_StrCode.Create(5);
        Assert.Throws<ArgumentException>(() => strc.Generate("whatever", "NOTATYPE"));
        Assert.Equal(0, strc.N);
    }
}
