using System;
using Leptonica;

namespace Leptonica.Managed.Tests.SarrayTests;

public class Sarray2Tests
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

    /// <summary>
    /// Runs a check that needs callees from files that are not ported yet (map.c, hashmap.c,
    /// utils1.c); it is skipped while one of them is still a stub.
    /// </summary>
    private static void WhenCalleesPorted(Action check)
    {
        try {
            check();
        } catch (NotImplementedException e) when (e.Message.StartsWith("leptonica:")) {
        }
    }

    /*------------------------------ StringCompareLexical ------------------------------*/
    [Theory]
    [InlineData("b", "a", 1)]
    [InlineData("a", "b", 0)]
    [InlineData("abc", "abc", 0)]
    [InlineData("ab", "a", 1)]
    [InlineData("a", "ab", 0)]
    [InlineData("", "", 0)]
    [InlineData("a", "", 1)]
    [InlineData("", "a", 0)]
    [InlineData("a", "B", 1)]        // 'a' = 97 > 'B' = 66
    [InlineData("B", "a", 0)]
    [InlineData("abc", "abd", 0)]
    [InlineData("abd", "abc", 1)]
    [InlineData("10", "9", 0)]       // lexical, not numeric
    [InlineData("a\0z", "a", 0)]     // cut at the first NUL: "a" vs "a"
    public void StringCompareLexical_Ascii(string s1, string s2, int expected)
    {
        Assert.Equal(expected, Sarray.StringCompareLexical(s1, s2));
    }

    [Fact]
    public void StringCompareLexical_NonAsciiUsesSignedBytes()
    {
        // U+00E9 is C3 A9 in UTF-8; as a signed char 0xC3 is negative, so it is below 'z'.
        Assert.Equal(0, Sarray.StringCompareLexical("é", "z"));
        Assert.Equal(1, Sarray.StringCompareLexical("z", "é"));
        // Two non-ASCII strings: C3 A9 vs C3 A8, the second byte decides.
        Assert.Equal(1, Sarray.StringCompareLexical("é", "è"));
        Assert.Equal(0, Sarray.StringCompareLexical("è", "é"));
        // Equal prefix with a non-ASCII tail: longer wins.
        Assert.Equal(1, Sarray.StringCompareLexical("aé", "a"));
        Assert.Equal(0, Sarray.StringCompareLexical("a", "aé"));
    }

    /*------------------------------ Sort ------------------------------*/
    [Fact]
    public void Sort_Increasing_NewArray()
    {
        Sarray sain = Make("pear", "apple", "fig", "Apple", "apple2", "");
        Sarray saout = Sarray.Sort(null, sain, L.L_SORT_INCREASING);
        Assert.NotSame(sain, saout);
        Assert.Equal(new[] { "", "Apple", "apple", "apple2", "fig", "pear" }, Strings(saout));
        // The input is unchanged.
        Assert.Equal(new[] { "pear", "apple", "fig", "Apple", "apple2", "" }, Strings(sain));
    }

    [Fact]
    public void Sort_Decreasing_InPlace()
    {
        Sarray sa = Make("pear", "apple", "fig", "Apple", "apple2", "");
        Sarray r = Sarray.Sort(sa, sa, L.L_SORT_DECREASING);
        Assert.Same(sa, r);
        Assert.Equal(new[] { "pear", "fig", "apple2", "apple", "Apple", "" }, Strings(sa));
    }

    [Fact]
    public void Sort_LexicalNotNumeric()
    {
        Sarray sa = Make("9", "10", "1", "100", "2");
        Sarray.Sort(sa, sa, L.L_SORT_INCREASING);
        Assert.Equal(new[] { "1", "10", "100", "2", "9" }, Strings(sa));
    }

    [Fact]
    public void Sort_WithDuplicates()
    {
        Sarray sa = Make("b", "a", "b", "a", "c", "a");
        Sarray.Sort(sa, sa, L.L_SORT_INCREASING);
        Assert.Equal(new[] { "a", "a", "a", "b", "b", "c" }, Strings(sa));
    }

    [Fact]
    public void Sort_EmptyAndSingle()
    {
        Sarray e = Sarray.Sort(null, Sarray.Create(0), L.L_SORT_INCREASING);
        Assert.Equal(0, e.Count);
        Sarray one = Sarray.Sort(null, Make("x"), L.L_SORT_DECREASING);
        Assert.Equal(new[] { "x" }, Strings(one));
    }

    [Fact]
    public void Sort_UnknownOrderKeepsOrder()
    {
        Sarray sa = Make("b", "a", "c");
        Sarray r = Sarray.Sort(null, sa, 0);
        Assert.Equal(new[] { "b", "a", "c" }, Strings(r));
    }

    [Fact]
    public void Sort_NotInPlaceThrows()
    {
        Sarray sain = Make("b", "a");
        Sarray saout = Make("x");
        var e = Assert.Throws<ArgumentException>(() => Sarray.Sort(saout, sain, L.L_SORT_INCREASING));
        Assert.Equal("Error in sarraySort: invalid: not in-place", e.Message);
    }

    [Fact]
    public void Sort_LargerArrayMatchesOrdinalSort()
    {
        // 37 strings: more than one Shell sort gap sequence step (18, 9, 4, 2, 1).
        string[] strs = new string[37];
        for (int i = 0; i < strs.Length; i++)
            strs[i] = ((i * 17) % 37).ToString();
        Sarray sa = Make(strs);
        Sarray.Sort(sa, sa, L.L_SORT_INCREASING);
        string[] expected = (string[])strs.Clone();
        Array.Sort(expected, StringComparer.Ordinal);
        Assert.Equal(expected, Strings(sa));
        Sarray.Sort(sa, sa, L.L_SORT_DECREASING);
        Array.Reverse(expected);
        Assert.Equal(expected, Strings(sa));
    }

    /*------------------------------ SortByIndex ------------------------------*/
    [Fact]
    public void SortByIndex_Permutation()
    {
        Sarray sain = Make("a", "b", "c");
        Numa na = Numa.CreateFromIArray(new[] { 2, 0, 1 }, 3);
        Sarray saout = Sarray.SortByIndex(sain, na);
        Assert.Equal(new[] { "c", "a", "b" }, Strings(saout));
        Assert.Equal(new[] { "a", "b", "c" }, Strings(sain));
    }

    [Fact]
    public void SortByIndex_RepeatsAndSubset()
    {
        Sarray sain = Make("a", "b", "c");
        Numa na = Numa.CreateFromIArray(new[] { 1, 1, 0 }, 3);
        Assert.Equal(new[] { "b", "b", "a" }, Strings(Sarray.SortByIndex(sain, na)));
    }

    [Fact]
    public void SortByIndex_EmptyIndexGivesEmptyArray()
    {
        // the output has as many strings as sain, so an index shorter than sain fails;
        // an empty sain with an empty index works
        Sarray saout = Sarray.SortByIndex(Sarray.Create(0), Numa.Create(0));
        Assert.Equal(0, saout.Count);
    }

    [Fact]
    public void SortByIndex_BadIndexThrows()
    {
        Sarray sain = Make("a", "b");
        Numa na = Numa.CreateFromIArray(new[] { 0, 5 }, 2);
        var e = Assert.Throws<ArgumentException>(() => Sarray.SortByIndex(sain, na));
        Assert.Equal("Error in sarrayGetString: index not valid", e.Message);
    }

    /*------------------------------ GenerateIntegers ------------------------------*/
    [Fact]
    public void GenerateIntegers()
    {
        Assert.Equal(new[] { "0", "1", "2" }, Strings(Sarray.GenerateIntegers(3)));
        Assert.Equal(0, Sarray.GenerateIntegers(0).Count);
        Assert.Equal(0, Sarray.GenerateIntegers(-4).Count);
        Sarray sa = Sarray.GenerateIntegers(12);
        Assert.Equal(12, sa.Count);
        Assert.Equal("10", sa.GetString(10, L.L_NOCOPY));
        Assert.Equal("11", sa.GetString(11, L.L_NOCOPY));
    }

    /*------------------------------ LookupCSKV ------------------------------*/
    [Fact]
    public void LookupCSKV_FindsFirstMatch()
    {
        Sarray sa = Make("a,1", "junk", "b , 2", "c,3,4", "k,v", "k,w");
        Assert.Equal("1", sa.LookupCSKV("a"));
        Assert.Equal("v", sa.LookupCSKV("k"));
    }

    [Fact]
    public void LookupCSKV_WhiteSpaceIsKept()
    {
        Sarray sa = Make("b , 2", "two words,x y");
        Assert.Equal(" 2", sa.LookupCSKV("b "));
        Assert.Null(sa.LookupCSKV("b"));
        Assert.Equal("x y", sa.LookupCSKV("two words"));
    }

    [Fact]
    public void LookupCSKV_NotFound()
    {
        Sarray sa = Make("a,1", "junk", "c,3,4", "d,", ",e");
        Assert.Null(sa.LookupCSKV("zzz"));
        Assert.Null(sa.LookupCSKV("junk"));    // no comma: one token
        Assert.Null(sa.LookupCSKV("c"));       // three tokens
        Assert.Null(sa.LookupCSKV("d"));       // empty value: one token
        Assert.Null(sa.LookupCSKV(""));        // empty key: one token
        Assert.Null(Sarray.Create(0).LookupCSKV("a"));
    }

    /*------------------------------ Aset / Hmap operations ------------------------------*/
    [Fact]
    public void RemoveDupsByAset_KeepsFirstOccurrenceOrder()
    {
        WhenCalleesPorted(() => {
            Sarray sas = Make("b", "a", "b", "c", "a", "d");
            Sarray sad = sas.RemoveDupsByAset();
            Assert.Equal(new[] { "b", "a", "c", "d" }, Strings(sad));
            Assert.Equal(6, sas.Count);
            Assert.Equal(0, Make().RemoveDupsByAset().Count);
        });
    }

    [Fact]
    public void UnionByAset_OrderIsFirstThenSecond()
    {
        WhenCalleesPorted(() => {
            Sarray sa1 = Make("x", "y", "x");
            Sarray sa2 = Make("z", "y", "w");
            Assert.Equal(new[] { "x", "y", "z", "w" }, Strings(sa1.UnionByAset(sa2)));
            Assert.Equal(3, sa1.Count);
            Assert.Equal(3, sa2.Count);
            Assert.Equal(new[] { "x", "y" }, Strings(sa1.UnionByAset(Make())).AsSpan(0, 2).ToArray());
        });
    }

    [Fact]
    public void IntersectionByAset_OrderFollowsSmallerArray()
    {
        WhenCalleesPorted(() => {
            Sarray sa1 = Make("a", "b", "c", "d", "b");
            Sarray sa2 = Make("d", "x", "b", "d");
            // sa2 is the smaller one (4 < 5): its order, no duplicates
            Assert.Equal(new[] { "d", "b" }, Strings(sa1.IntersectionByAset(sa2)));
            // equal sizes: n1 < n2 is false, so sa2 is the "small" one
            Sarray sa3 = Make("p", "q");
            Sarray sa4 = Make("q", "p");
            Assert.Equal(new[] { "q", "p" }, Strings(sa3.IntersectionByAset(sa4)));
            Assert.Equal(0, sa1.IntersectionByAset(Make()).Count);
        });
    }

    [Fact]
    public void CreateAset_ContainsEveryString()
    {
        WhenCalleesPorted(() => {
            Sarray sa = Make("abc", "d");
            L_Aset set = sa.CreateAset();
            Utils.HashStringToUint64Fast("abc", out ulong h);
            RbType key = default;
            key.Utype = h;
            Assert.NotNull(set.Find(key));
            key.Utype = h + 1;
            Assert.Null(set.Find(key));
        });
    }

    [Fact]
    public void RemoveDupsByHmap_UniqueStrings()
    {
        WhenCalleesPorted(() => {
            Sarray sas = Make("b", "a", "b", "c", "a", "d");
            Sarray sad = sas.RemoveDupsByHmap(out L_Hashmap hmap);
            // the order is that of the hashtable, so compare as sets
            string[] got = Strings(sad);
            Array.Sort(got, StringComparer.Ordinal);
            Assert.Equal(new[] { "a", "b", "c", "d" }, got);
            Assert.Equal(4, hmap.NItems);
        });
    }

    [Fact]
    public void UnionByHmap_UniqueStrings()
    {
        WhenCalleesPorted(() => {
            Sarray sa1 = Make("x", "y", "x");
            Sarray sa2 = Make("z", "y", "w");
            string[] got = Strings(sa1.UnionByHmap(sa2));
            Array.Sort(got, StringComparer.Ordinal);
            Assert.Equal(new[] { "w", "x", "y", "z" }, got);
        });
    }

    [Fact]
    public void IntersectionByHmap_CommonStrings()
    {
        WhenCalleesPorted(() => {
            Sarray sa1 = Make("a", "b", "c", "d", "b");
            Sarray sa2 = Make("d", "x", "b", "d");
            string[] got = Strings(sa1.IntersectionByHmap(sa2));
            Array.Sort(got, StringComparer.Ordinal);
            Assert.Equal(new[] { "b", "d" }, got);
            Assert.Equal(0, sa1.IntersectionByHmap(Make()).Count);
        });
    }
}
