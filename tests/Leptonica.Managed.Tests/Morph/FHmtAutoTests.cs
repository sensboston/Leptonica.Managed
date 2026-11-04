using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Leptonica;
using Leptonica.Managed.Tests.Reg;
using Xunit;

namespace Leptonica.Managed.Tests.Morph;

/// <summary>
/// The hit-miss dwa code generator (fhmtauto.c): the generated files in the library are up to
/// date, and their low-level code is line-for-line identical to the generated C files.
/// </summary>
public class FHmtAutoTests
{
    private static string GenDir => Path.Combine(TestPaths.RepoRoot, "src", "Leptonica.Managed", "Morph", "Generated");
    private static string CSrc => Path.Combine(TestPaths.RepoRoot, "testdata", "csrc");

    private static string Normalize(string s) => s.Replace("\r\n", "\n");

    [Fact]
    public void GeneratedFilesAreUpToDate()
    {
        Sela sela = Sela.AddHitMiss(null);
        Assert.Equal(Normalize(File.ReadAllText(Path.Combine(GenDir, "fhmtgen.1.cs"))),
                     sela.FHmtAutoGen1Text(1, "fhmtgen"));
        Assert.Equal(Normalize(File.ReadAllText(Path.Combine(GenDir, "fhmtgenlow.1.cs"))),
                     sela.FHmtAutoGen2Text(1, "fhmtgen"));
    }

    [Fact]
    public void LowLevelCodeMatchesC()
    {
        Sela sela = Sela.AddHitMiss(null);
        List<string> c = CodeLines(File.ReadAllText(Path.Combine(CSrc, "fhmtgenlow.1.c")));
        List<string> cs = CodeLines(sela.FHmtAutoGen2Text(1, "fhmtgen"));
        Assert.True(c.Count > 50);
        Assert.Equal(c.Count, cs.Count);
        for (int i = 0; i < c.Count; i++)
            Assert.True(c[i] == cs[i], $"line {i}:\n  C:  {c[i]}\n  C#: {cs[i]}");
    }

    [Fact]
    public void SelNamesMatchC()
    {
        var re = new Regex("^ +\"([^\"]+)\"", RegexOptions.Multiline);
        string c = File.ReadAllText(Path.Combine(CSrc, "fhmtgen.1.c"));
        string cs = Sela.AddHitMiss(null).FHmtAutoGen1Text(1, "fhmtgen");
        var a = new List<string>();
        var b = new List<string>();
        foreach (Match m in re.Matches(c)) a.Add(m.Groups[1].Value);
        foreach (Match m in re.Matches(cs)) b.Add(m.Groups[1].Value);
        Assert.Equal(10, a.Count);
        Assert.Equal(a, b);
    }

    private static List<string> CodeLines(string text)
    {
        var func = new Regex(@"^(?:private static void |static void\s*)?(fhmt_\d+_\d+)\(");
        var call = new Regex(@"^fhmt_\d+_\d+\(datad, w, h, wpld, datas, wpls\);");
        var wpls = new Regex(@"^(int wpls\d|wpls\d+ = )");
        var output = new List<string>();
        foreach (string line in Normalize(text).Split('\n')) {
            string s = string.Join(" ", line.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries));
            Match m = func.Match(s);
            if (m.Success && !s.EndsWith(";")) {
                output.Add("FUNC " + m.Groups[1].Value);
                continue;
            }
            if (s.StartsWith("case ") || call.IsMatch(s)) {
                output.Add(s);
                continue;
            }
            s = s.Replace("l_int32 ", "int ");
            if (s.StartsWith("*dptr =") || s.StartsWith("(*") || s.StartsWith("(~*") ||
                s.StartsWith("((*") || s.StartsWith("((~*") || wpls.IsMatch(s))
                output.Add(s);
        }
        return output;
    }
}
