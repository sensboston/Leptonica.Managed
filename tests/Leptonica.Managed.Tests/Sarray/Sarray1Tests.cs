using System;
using Leptonica;

namespace Leptonica.Managed.Tests.SarrayTests;

public class Sarray1Tests
{
    private static Sarray Make(params string[] strs)
    {
        Sarray sa = Sarray.Create(0);
        foreach (string s in strs)
            sa.AddString(s, L.L_COPY);
        return sa;
    }

    private static string[] Strings(Sarray sa)
    {
        string[] r = new string[sa.Count];
        for (int i = 0; i < r.Length; i++)
            r[i] = sa.GetString(i, L.L_NOCOPY);
        return r;
    }

    [Fact]
    public void CreateUsesDefaultSizeForBadN()
    {
        foreach (int n in new[] { 0, -5, 50000001 }) {
            Sarray.Create(n).GetArray(out int nalloc, out int count);
            Assert.Equal(50, nalloc);
            Assert.Equal(0, count);
        }
        Sarray.Create(7).GetArray(out int na, out _);
        Assert.Equal(7, na);
    }

    [Fact]
    public void CreateInitialized()
    {
        Sarray sa = Sarray.CreateInitialized(3, "x");
        Assert.Equal(new[] { "x", "x", "x" }, Strings(sa));
        Assert.Throws<ArgumentException>(() => Sarray.CreateInitialized(0, "x"));
    }

    [Fact]
    public void AddStringExtendsByDoubling()
    {
        Sarray sa = Sarray.Create(2);
        for (int i = 0; i < 5; i++)
            sa.AddString("s" + i, L.L_INSERT);
        sa.GetArray(out int nalloc, out int n);
        Assert.Equal(8, nalloc);
        Assert.Equal(5, n);
        Assert.Equal("s4", sa.GetString(4, L.L_NOCOPY));
        Assert.Throws<ArgumentException>(() => sa.AddString("a", 7));
    }

    [Fact]
    public void RemoveKeepsOrder()
    {
        Sarray sa = Make("a", "b", "c", "d");
        Assert.Equal("b", sa.RemoveString(1));
        Assert.Equal(new[] { "a", "c", "d" }, Strings(sa));
        Assert.Equal("d", sa.RemoveString(2));
        Assert.Equal(2, sa.Count);
        Assert.Throws<ArgumentException>(() => sa.RemoveString(2));
        Assert.Throws<ArgumentException>(() => sa.RemoveString(-1));
    }

    [Fact]
    public void ReplaceAndClear()
    {
        Sarray sa = Make("a", "b");
        sa.ReplaceString(1, "z", L.L_COPY);
        sa.ReplaceString(0, "y", L.L_INSERT);
        Assert.Equal(new[] { "y", "z" }, Strings(sa));
        Assert.Throws<ArgumentException>(() => sa.ReplaceString(2, "q", L.L_COPY));
        Assert.Throws<ArgumentException>(() => sa.ReplaceString(0, "q", L.L_NOCOPY + 5));
        sa.Clear();
        Assert.Equal(0, sa.Count);
        sa.GetArray(out int nalloc, out _);
        Assert.Equal(50, nalloc);
    }

    [Fact]
    public void GetStringChecksArguments()
    {
        Sarray sa = Make("a");
        Assert.Throws<ArgumentException>(() => sa.GetString(1, L.L_COPY));
        Assert.Throws<ArgumentException>(() => sa.GetString(0, L.L_CLONE));
    }

    [Fact]
    public void CopyIsDeepCloneIsShared()
    {
        Sarray sa = Make("a", "b");
        Sarray copy = sa.Copy();
        Sarray clone = sa.Clone();
        sa.ReplaceString(0, "X", L.L_COPY);
        sa.AddString("c", L.L_COPY);
        Assert.Equal(new[] { "a", "b" }, Strings(copy));
        Assert.Same(sa, clone);
        Assert.Equal(3, clone.Count);
        sa.GetArray(out int n1, out _);
        copy.GetArray(out int n2, out _);
        Assert.Equal(n1, n2);
    }

    [Theory]
    [InlineData(0, "abc")]
    [InlineData(1, "a\nb\nc\n")]
    [InlineData(2, "a b c ")]
    [InlineData(3, "a,b,c,")]
    public void ToStringFlags(int flag, string expected)
    {
        Assert.Equal(expected, Make("a", "b", "c").ToString(flag));
    }

    [Fact]
    public void ToStringRangeAndEmpty()
    {
        Sarray sa = Make("a", "b", "c", "d");
        Assert.Equal("b c ", sa.ToStringRange(1, 2, 2));
        Assert.Equal("c,d,", sa.ToStringRange(2, 10, 3));
        Assert.Equal("", sa.ToStringRange(2, -1, 3));
        Assert.Throws<ArgumentException>(() => sa.ToStringRange(4, 0, 0));
        Assert.Throws<ArgumentException>(() => sa.ToStringRange(0, 0, 4));

        Sarray empty = Sarray.Create(0);
        Assert.Equal("", empty.ToString(0));
        Assert.Equal("\n", empty.ToString(1));
        Assert.Equal(" ", empty.ToString(2));
        Assert.Equal(",", empty.ToString(3));
        Assert.Throws<ArgumentException>(() => empty.ToStringRange(1, 0, 0));
    }

    [Fact]
    public void JoinAndAppendRange()
    {
        Sarray a = Make("a");
        Sarray b = Make("x", "y", "z");
        a.Join(b);
        Assert.Equal(new[] { "a", "x", "y", "z" }, Strings(a));
        Assert.Equal(3, b.Count);

        Sarray c = Make("c");
        c.AppendRange(b, 1, -1);
        Assert.Equal(new[] { "c", "y", "z" }, Strings(c));
        c.AppendRange(b, -5, 0);
        Assert.Equal(new[] { "c", "y", "z", "x" }, Strings(c));
        Assert.Throws<ArgumentException>(() => c.AppendRange(b, 2, 1));
    }

    [Fact]
    public void PadToSameSize()
    {
        Sarray a = Make("a");
        Sarray b = Make("x", "y", "z");
        a.PadToSameSize(b, "-");
        Assert.Equal(new[] { "a", "-", "-" }, Strings(a));
        Assert.Equal(3, b.Count);
        Sarray c = Make("1", "2", "3", "4");
        a.PadToSameSize(c, "+");
        Assert.Equal(new[] { "a", "-", "-", "+" }, Strings(a));
        c.PadToSameSize(Make("q"), "#");
        Assert.Equal(4, c.Count);
    }

    [Fact]
    public void SplitString()
    {
        Sarray sa = Sarray.Create(0);
        sa.SplitString("  ab, cd,,ef ", " ,");
        Assert.Equal(new[] { "ab", "cd", "ef" }, Strings(sa));

        Sarray none = Sarray.Create(0);
        none.SplitString(",,,", ",");
        Assert.Equal(0, none.Count);

        Sarray whole = Sarray.Create(0);
        whole.SplitString("abc", "");
        Assert.Equal(new[] { "abc" }, Strings(whole));
    }

    [Fact]
    public void CreateWordsFromString()
    {
        Sarray sa = Sarray.CreateWordsFromString("  the quick\tbrown\nfox  ");
        Assert.Equal(new[] { "the", "quick", "brown", "fox" }, Strings(sa));
        sa.GetArray(out int nalloc, out _);
        Assert.Equal(4, nalloc);
        Assert.Equal(0, Sarray.CreateWordsFromString("   ").Count);
    }

    [Fact]
    public void CreateLinesFromString()
    {
        const string text = "one\r\n\ntwo\nthree";
        Assert.Equal(new[] { "one", "", "two", "three" },
                     Strings(Sarray.CreateLinesFromString(text, 1)));
        Assert.Equal(new[] { "one", "two", "three" },
                     Strings(Sarray.CreateLinesFromString(text, 0)));
        Assert.Equal(new[] { "a", "" },
                     Strings(Sarray.CreateLinesFromString("a\n\n", 1)));
        Assert.Equal(0, Sarray.CreateLinesFromString("", 1).Count);
    }

    [Fact]
    public void ConvertWordsToLines()
    {
        Sarray words = Sarray.CreateWordsFromString("aa bb cc dd ee");
        Assert.Equal(new[] { "aa bb ", "cc dd ", "ee " },
                     Strings(words.ConvertWordsToLines(8)));

        Sarray para = Make("aa", "bb", "", "cc");
        Assert.Equal(new[] { "aa bb ", "", "cc " },
                     Strings(para.ConvertWordsToLines(80)));

        Sarray longword = Make("a", "verylongword", "b");
        Assert.Equal(new[] { "a ", "verylongword ", "b " },
                     Strings(longword.ConvertWordsToLines(5)));
    }

    [Fact]
    public void SelectBySubstring()
    {
        Sarray sa = Make("alpha", "beta", "gamma", "");
        Assert.Equal(new[] { "alpha", "beta", "gamma" },
                     Strings(sa.SelectBySubstring("a")));
        Assert.Equal(new[] { "gamma" }, Strings(sa.SelectBySubstring("mm")));
        Assert.Equal(4, sa.SelectBySubstring(null).Count);
        Assert.Equal(0, sa.SelectBySubstring("").Count);  /* empty is never found */
        Assert.Equal(0, sa.SelectBySubstring("zzzzzzzzzzzz").Count);
    }

    [Fact]
    public void SelectRange()
    {
        Sarray sa = Make("a", "b", "c", "d");
        Assert.Equal(new[] { "b", "c" }, Strings(sa.SelectRange(1, 2)));
        Assert.Equal(new[] { "c", "d" }, Strings(sa.SelectRange(2, 0)));
        Assert.Equal(new[] { "a", "b", "c", "d" }, Strings(sa.SelectRange(-3, 99)));
        Assert.Throws<ArgumentException>(() => sa.SelectRange(3, 1));
    }

    [Fact]
    public void ParseRange()
    {
        Sarray sa = Make("--a", "b", "c", "--d", "--e", "f", "--g");

        Assert.True(sa.ParseRange(0, out int act, out int end, out int next, "--", 0));
        Assert.Equal((1, 2, 5), (act, end, next));
        Assert.True(sa.ParseRange(next, out act, out end, out next, "--", 0));
        Assert.Equal((5, 5, 7), (act, end, next));
        Assert.False(sa.ParseRange(next, out act, out end, out next, "--", 0));
        Assert.Equal((7, 7, 7), (act, end, next));

        /* last range reaches the end: newstart is n */
        Sarray tail = Make("--a", "b", "c");
        Assert.True(tail.ParseRange(0, out act, out end, out next, "--", 0));
        Assert.Equal((1, 2, 3), (act, end, next));

        /* offset matters: "-" is found anywhere but not at offset 0 for "x-y" */
        Sarray loc = Make("x-y", "-z", "w");
        Assert.True(loc.ParseRange(0, out act, out end, out next, "-", 0));
        Assert.Equal((0, 0, 2), (act, end, next));
        Assert.True(loc.ParseRange(0, out act, out end, out next, "-", -1));
        Assert.Equal((2, 2, 3), (act, end, next));
    }

    [Fact]
    public void ConcatUniformlyNeedsNuma()
    {
        Sarray sa = Make("a", "b", "c");
        Assert.Throws<ArgumentException>(() => sa.ConcatUniformly(0, 0));
        Assert.Throws<ArgumentException>(() => sa.ConcatUniformly(4, 0));
        Assert.Throws<ArgumentException>(() => sa.ConcatUniformly(2, 9));
    }
}
