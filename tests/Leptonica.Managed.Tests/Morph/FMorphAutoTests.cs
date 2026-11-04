using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Leptonica;
using Leptonica.Managed.Tests.Reg;

namespace Leptonica.Managed.Tests.Morph;

/// <summary>
/// The dwa code generator (fmorphauto.c): the generated files in the library are up to
/// date, and their low-level code is the same as the generated C files of the C library.
/// </summary>
public class FMorphAutoTests
{
    private static string GenDir => Path.Combine(TestPaths.RepoRoot, "src", "Leptonica.Managed", "Morph", "Generated");
    private static string CSrc => Path.Combine(TestPaths.RepoRoot, "testdata", "csrc");

    public static IEnumerable<object[]> Files() => new[] {
        new object[] { "basic", 1, "fmorphgen" },
        new object[] { "combs", 2, "dwacomb" },
    };

    private static Sela MakeSela(string kind) =>
        kind == "basic" ? Sela.AddBasic(null) : Sela.AddDwaCombs(null);

    private static string Normalize(string s) => s.Replace("\r\n", "\n");

    [Theory]
    [MemberData(nameof(Files))]
    public void GeneratedFilesAreUpToDate(string kind, int index, string root)
    {
        Sela sela = MakeSela(kind);
        Assert.Equal(Normalize(File.ReadAllText(Path.Combine(GenDir, $"{root}.{index}.cs"))),
                     sela.FMorphAutoGen1Text(index, root));
        Assert.Equal(Normalize(File.ReadAllText(Path.Combine(GenDir, $"{root}low.{index}.cs"))),
                     sela.FMorphAutoGen2Text(index, root));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void LowLevelCodeMatchesC(string kind, int index, string root)
    {
        Sela sela = MakeSela(kind);
        List<string> c = CodeLines(File.ReadAllText(Path.Combine(CSrc, $"{root}low.{index}.c")));
        List<string> cs = CodeLines(sela.FMorphAutoGen2Text(index, root));
        Assert.True(c.Count > 100);
        Assert.Equal(c.Count, cs.Count);
        for (int i = 0; i < c.Count; i++)
            Assert.True(c[i] == cs[i], $"line {i}:\n  C:  {c[i]}\n  C#: {cs[i]}");
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void SelNamesMatchC(string kind, int index, string root)
    {
        var re = new Regex("^ +\"([^\"]+)\"", RegexOptions.Multiline);
        string c = File.ReadAllText(Path.Combine(CSrc, $"{root}.{index}.c"));
        string cs = MakeSela(kind).FMorphAutoGen1Text(index, root);
        var a = new List<string>();
        var b = new List<string>();
        foreach (Match m in re.Matches(c)) a.Add(m.Groups[1].Value);
        foreach (Match m in re.Matches(cs)) b.Add(m.Groups[1].Value);
        Assert.True(a.Count > 50);
        Assert.Equal(a, b);
    }

    /// <summary>
    /// The lines that depend on the sels: function names, dispatcher cases, wplsN
    /// declarations and definitions, and the barrel shift expressions, with white
    /// space collapsed and l_int32 written as int.
    /// </summary>
    private static List<string> CodeLines(string text)
    {
        var func = new Regex(@"^(?:private static void |static void\s*)?(f(?:dilate|erode)_\d+_\d+)\(");
        var call = new Regex(@"^f(dilate|erode)_\d+_\d+\(datad, w, h, wpld, datas, wpls\);");
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
            if (s.StartsWith("*dptr =") || s.StartsWith("(*") || s.StartsWith("((*") || wpls.IsMatch(s))
                output.Add(s);
        }
        return output;
    }
}
