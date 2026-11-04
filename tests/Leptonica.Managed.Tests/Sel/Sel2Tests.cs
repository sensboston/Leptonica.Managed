using System;
using System.Collections.Generic;
using Leptonica;

namespace Leptonica.Managed.Tests;

public class Sel2Tests
{
    private static List<string> Names(Sela sela)
    {
        List<string> names = new List<string>();
        for (int i = 0; i < sela.Count; i++)
            names.Add(sela.GetSel(i).Name!);
        return names;
    }

    private static Sel Find(Sela sela, string name)
    {
        Assert.True(sela.FindSelByName(name, out _, out Sel? sel));
        return sel!;
    }

    private static string Pattern(Sel sel)
    {
        sel.GetParameters(out int sy, out int sx, out _, out _);
        char[] c = new char[sy * sx];
        for (int i = 0; i < sy; i++)
            for (int j = 0; j < sx; j++)
                c[i * sx + j] = sel.GetElement(i, j) switch {
                    L.SEL_HIT => 'x',
                    L.SEL_MISS => 'o',
                    _ => ' ',
                };
        return new string(c);
    }

    /*------------------------------ AddBasic ------------------------------*/

    [Fact]
    public void AddBasic_CountAndNames()
    {
        Sela sela = Sela.AddBasic(null);
        Assert.Equal(25 + 25 + 4 + 4, sela.Count);
        List<string> names = Names(sela);
        Assert.Equal("sel_2h", names[0]);
        Assert.Equal("sel_51h", names[24]);
        Assert.Equal("sel_2v", names[25]);
        Assert.Equal("sel_51v", names[49]);
        Assert.Equal(new[] { "sel_2", "sel_3", "sel_4", "sel_5",
                             "sel_2dp", "sel_2dm", "sel_5dp", "sel_5dm" },
                     names.GetRange(50, 8));
    }

    [Fact]
    public void AddBasic_AppendsToExistingSela()
    {
        Sela sela = Sela.Create(0);
        Sela same = Sela.AddBasic(sela);
        Assert.Same(sela, same);
        Sela.AddBasic(sela);
        Assert.Equal(2 * 58, sela.Count);
    }

    [Fact]
    public void AddBasic_BrickGeometry()
    {
        Sela sela = Sela.AddBasic(null);

        Sel h = Find(sela, "sel_11h");
        h.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((1, 11, 0, 5), (sy, sx, cy, cx));
        Assert.Equal(new string('x', 11), Pattern(h));

        Sel v = Find(sela, "sel_6v");
        v.GetParameters(out sy, out sx, out cy, out cx);
        Assert.Equal((6, 1, 3, 0), (sy, sx, cy, cx));
        Assert.Equal(new string('x', 6), Pattern(v));

        Sel sq = Find(sela, "sel_5");
        sq.GetParameters(out sy, out sx, out cy, out cx);
        Assert.Equal((5, 5, 2, 2), (sy, sx, cy, cx));
        Assert.Equal(new string('x', 25), Pattern(sq));
    }

    [Fact]
    public void AddBasic_Diagonals()
    {
        Sela sela = Sela.AddBasic(null);

        Assert.Equal(" xx ", Pattern(Find(sela, "sel_2dp")));
        Assert.Equal("x  x", Pattern(Find(sela, "sel_2dm")));

        Sel dp = Find(sela, "sel_5dp");
        dp.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((5, 5, 2, 2), (sy, sx, cy, cx));
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 5; j++)
                Assert.Equal(i + j == 4 ? L.SEL_HIT : L.SEL_DONT_CARE, dp.GetElement(i, j));

        Sel dm = Find(sela, "sel_5dm");
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 5; j++)
                Assert.Equal(i == j ? L.SEL_HIT : L.SEL_DONT_CARE, dm.GetElement(i, j));
    }

    /*----------------------------- AddHitMiss -----------------------------*/

    [Fact]
    public void AddHitMiss_CountAndNames()
    {
        Sela sela = Sela.AddHitMiss(null);
        Assert.Equal(new[] { "sel_3hm", "sel_3de", "sel_3ue", "sel_3re", "sel_3le",
                             "sel_sl1", "sel_ulc", "sel_urc", "sel_llc", "sel_lrc" },
                     Names(sela));
    }

    [Fact]
    public void AddHitMiss_IsolatedPixelAndEdges()
    {
        Sela sela = Sela.AddHitMiss(null);

        Sel hm = Find(sela, "sel_3hm");
        Assert.Equal("ooooxoooo", Pattern(hm));
        Assert.Equal(L.SEL_HIT, hm.GetElement(1, 1));
        Assert.Equal(L.SEL_MISS, hm.GetElement(0, 0));
        hm.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((3, 3, 1, 1), (sy, sx, cy, cx));

        Sel de = Find(sela, "sel_3de");
        de.GetParameters(out sy, out sx, out cy, out cx);
        Assert.Equal((2, 3, 0, 1), (sy, sx, cy, cx));
        Assert.Equal("xxxooo", Pattern(de));

        Assert.Equal("oooxxx", Pattern(Find(sela, "sel_3ue")));
        Assert.Equal("xoxoxo", Pattern(Find(sela, "sel_3re")));
        Assert.Equal("oxoxox", Pattern(Find(sela, "sel_3le")));
    }

    [Fact]
    public void AddHitMiss_SlantedEdge()
    {
        Sel sel = Find(Sela.AddHitMiss(null), "sel_sl1");
        sel.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((13, 6, 6, 2), (sy, sx, cy, cx));
        Assert.Equal(L.SEL_MISS, sel.GetElement(0, 3));
        Assert.Equal(L.SEL_HIT, sel.GetElement(0, 5));
        Assert.Equal(L.SEL_MISS, sel.GetElement(12, 0));
        Assert.Equal(L.SEL_HIT, sel.GetElement(12, 2));
        Assert.Equal(L.SEL_DONT_CARE, sel.GetElement(6, 3));
    }

    [Fact]
    public void AddHitMiss_Corners()
    {
        Sela sela = Sela.AddHitMiss(null);

        Sel ulc = Find(sela, "sel_ulc");
        ulc.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((4, 4, 1, 1), (sy, sx, cy, cx));
        Assert.Equal("oooo" + "o  x" + "o xx" + "oxxx", Pattern(ulc));

        Sel urc = Find(sela, "sel_urc");
        urc.GetParameters(out sy, out sx, out cy, out cx);
        Assert.Equal((1, 2), (cy, cx));
        Assert.Equal("oooo" + "x  o" + "xx o" + "xxxo", Pattern(urc));

        Sel llc = Find(sela, "sel_llc");
        llc.GetParameters(out sy, out sx, out cy, out cx);
        Assert.Equal((2, 1), (cy, cx));
        Assert.Equal("oxxx" + "o xx" + "o  x" + "oooo", Pattern(llc));

        Sel lrc = Find(sela, "sel_lrc");
        lrc.GetParameters(out sy, out sx, out cy, out cx);
        Assert.Equal((2, 2), (cy, cx));
        Assert.Equal("xxxo" + "xx o" + "x  o" + "oooo", Pattern(lrc));
    }

    /*---------------------------- AddDwaLinear ----------------------------*/

    [Fact]
    public void AddDwaLinear_CountAndNames()
    {
        Sela sela = Sela.AddDwaLinear(null);
        Assert.Equal(2 * 62, sela.Count);
        List<string> names = Names(sela);
        Assert.Equal("sel_2h", names[0]);
        Assert.Equal("sel_63h", names[61]);
        Assert.Equal("sel_2v", names[62]);
        Assert.Equal("sel_63v", names[123]);

        Sel h = sela.GetSel(61);
        h.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((1, 63, 0, 31), (sy, sx, cy, cx));
        Sel v = sela.GetSel(62);
        v.GetParameters(out sy, out sx, out cy, out cx);
        Assert.Equal((2, 1, 1, 0), (sy, sx, cy, cx));
    }

    /*------------------------------ Thinning ------------------------------*/

    [Fact]
    public void Thin4cc_NinesSels()
    {
        Sela sela = Sela.Thin4cc(null);
        Assert.Equal(9, sela.Count);
        for (int i = 0; i < 9; i++)
            Assert.Equal($"sel_4_{i + 1}", sela.GetSel(i).Name);

        Sel s1 = sela.GetSel(0);
        s1.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((3, 3, 1, 1), (sy, sx, cy, cx));
        Assert.Equal("  x" + "o x" + "  x", Pattern(s1));
        Assert.Equal(L.SEL_DONT_CARE, s1.GetElement(1, 1));
        Assert.Equal(L.SEL_MISS, s1.GetElement(1, 0));
        Assert.Equal(L.SEL_DONT_CARE, s1.GetElement(0, 0));

        Assert.Equal("o x" + "o x" + "  x", Pattern(sela.GetSel(8)));
    }

    [Fact]
    public void Thin8cc_NinesSels()
    {
        Sela sela = Sela.Thin8cc(null);
        Assert.Equal(9, sela.Count);
        for (int i = 0; i < 9; i++)
            Assert.Equal($"sel_8_{i + 1}", sela.GetSel(i).Name);
        Assert.Equal(" x " + "o x" + " x ", Pattern(sela.GetSel(0)));
        Assert.Equal("ox " + "o x" + " x ", Pattern(sela.GetSel(8)));
    }

    [Fact]
    public void Thin4and8cc_TwoSels()
    {
        Sela sela = Sela.Thin4and8cc(null);
        Assert.Equal(new[] { "sel_48_1", "sel_48_2" }, Names(sela));
        Assert.Equal(" xx" + "o x" + "oo ", Pattern(sela.GetSel(0)));
        Assert.Equal("o x" + "o x" + "o x", Pattern(sela.GetSel(1)));
    }

    [Fact]
    public void Thin_AppendsToExistingSela()
    {
        Sela sela = Sela.Thin4and8cc(null);
        Sela same = Sela.Thin8cc(sela);
        Assert.Same(sela, same);
        Assert.Equal(11, sela.Count);
    }

    /*----------------------------- Error cases ----------------------------*/

    [Fact]
    public void CrossJunctions_InvalidArgs()
    {
        Assert.Throws<ArgumentException>(() => Sela.AddCrossJunctions(null, 0f, 5f, 2, 0));
        Assert.Throws<ArgumentException>(() => Sela.AddCrossJunctions(null, 7f, 5f, 0, 0));
        Assert.Throws<ArgumentException>(() => Sela.AddCrossJunctions(null, 7f, 5f, 9, 0));
    }

    [Fact]
    public void TJunctions_InvalidArgs()
    {
        Assert.Throws<ArgumentException>(() => Sela.AddTJunctions(null, 2f, 5f, 2, 0));
        Assert.Throws<ArgumentException>(() => Sela.AddTJunctions(null, 7f, 5f, 0, 0));
        Assert.Throws<ArgumentException>(() => Sela.AddTJunctions(null, 7f, 5f, 9, 0));
    }

    [Fact]
    public void MakePlusSign_InvalidArgs()
    {
        Assert.Throws<ArgumentException>(() => Sel.MakePlusSign(2, 1));
        Assert.Throws<ArgumentException>(() => Sel.MakePlusSign(5, 6));
    }
}
