using System;
using System.IO;
using System.Text;
using Leptonica;

namespace Leptonica.Managed.Tests;

/// <summary>Tests of utils2.c: string, array, file and path utilities.</summary>
public class Utils2Tests
{
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    /*--------------------------- strings ---------------------------*/

    [Fact]
    public void StringCopyLengthCat_WorkOnNulTerminatedBuffers()
    {
        byte[] buf = new byte[8];
        Utils.StringCopy(buf, "abcdefghij", 5);
        Assert.Equal(new byte[] { 97, 98, 99, 100, 101, 0, 0, 0 }, buf);
        Utils.StringCopy(buf, "xy", 5);
        Assert.Equal(new byte[] { 120, 121, 0, 0, 0, 0, 0, 0 }, buf);
        Assert.Equal(2, Utils.StringLength(buf, 8));
        Assert.Equal(3, Utils.StringCat(buf, "123"));
        Assert.Equal(5, Utils.StringLength(buf, 8));
        Assert.Equal(-1, Utils.StringCat(buf, "12345"));    /* does not fit */
        Assert.Equal(0, Utils.StringCat(buf, null));
        Assert.Equal(4, Utils.StringLength(new byte[] { 1, 2, 3, 4 }, 4));    /* no NUL: size */
    }

    [Fact]
    public void StringNewReplaceJoinConcatReverse()
    {
        Assert.Equal("abc", Utils.StringNew("abc"));
        Assert.Null(Utils.StringNew(null));
        string? dest = "old";
        Utils.StringReplace(ref dest, "new");
        Assert.Equal("new", dest);
        Utils.StringReplace(ref dest, null);
        Assert.Null(dest);
        Assert.Equal("ab", Utils.StringJoin("a", "b"));
        Assert.Equal("b", Utils.StringJoin(null, "b"));
        Assert.Equal("", Utils.StringJoin(null, null));
        string? ip = null;
        Utils.StringJoinIP(ref ip, "x");
        Utils.StringJoinIP(ref ip, "y");
        Assert.Equal("xy", ip);
        Assert.Equal("abcd", Utils.StringConcatNew("a", "b", "c", "d"));
        Assert.Equal("ab", Utils.StringConcatNew("a", "b", null, "c"));
        Assert.Null(Utils.StringConcatNew(null, "b"));
        Assert.Equal("cba", Utils.StringReverse("abc"));
        Assert.Equal("", Utils.StringReverse(""));
    }

    [Fact]
    public void StringCopySegment_CopiesAndTruncates()
    {
        Assert.Equal("cde", Utils.StringCopySegment("abcdefg", 2, 3));
        Assert.Equal("cdefg", Utils.StringCopySegment("abcdefg", 2, 0));
        Assert.Equal("fg", Utils.StringCopySegment("abcdefg", 5, 10));
        Assert.Throws<ArgumentException>(() => Utils.StringCopySegment("abc", 3, 1));
        Assert.Throws<ArgumentException>(() => Utils.StringCopySegment("abc", -1, 1));
    }

    [Fact]
    public void StrtokSafe_MatchesStrtokR()
    {
        string? save = null;
        string? tok = Utils.StrtokSafe("  ab,, cd;e ", " ,;", ref save);
        Assert.Equal("ab", tok);
        Assert.Equal("cd", Utils.StrtokSafe(null, " ,;", ref save));
        Assert.Equal("e", Utils.StrtokSafe(null, " ,;", ref save));
        Assert.Null(Utils.StrtokSafe(null, " ,;", ref save));
        Assert.Null(Utils.StrtokSafe(" ,; ", " ,;", ref save));    /* only separators */
        Assert.Null(save);
        Assert.Equal("abc", Utils.StrtokSafe("abc", ",", ref save));
        Assert.Null(save);
    }

    [Fact]
    public void StringSplitOnToken_SplitsAtTheFirstSeparatorRun()
    {
        Utils.StringSplitOnToken("abc  def ghi", " ", out string? head, out string? tail);
        Assert.Equal("abc", head);
        Assert.Equal("def ghi", tail);
        Utils.StringSplitOnToken("abc", " ", out head, out tail);
        Assert.Equal("abc", head);
        Assert.Null(tail);
    }

    [Fact]
    public void FindAndReplace_OnStrings()
    {
        Assert.True(Utils.StringCheckForChars("hello world", "xyzw"));
        Assert.False(Utils.StringCheckForChars("hello", "xyz"));
        Assert.False(Utils.StringCheckForChars("", "xyz"));
        Assert.Equal("heo", Utils.StringRemoveChars("hello", "l"));
        Assert.Equal("hello", Utils.StringRemoveChars("hello", null));

        Assert.Equal("bXabbb", Utils.StringReplaceEachSubstr("baaabbb", "aa", "X", out int count));
        Assert.Equal(1, count);
        Assert.Equal("bab", Utils.StringReplaceEachSubstr("baaabbb", "aa", "", out count).Substring(0, 3));
        Assert.Equal("one-two", Utils.StringReplaceEachSubstr("one two", " ", "-", out count));
        Assert.Equal("none", Utils.StringReplaceEachSubstr("none", "zz", "-", out count));
        Assert.Equal(0, count);
        Assert.Equal("aXbXc", Utils.StringReplaceEachSubstr("a--b--c", "--", "X", out count));
        Assert.Equal(2, count);

        int loc = 0;
        Assert.Equal("1X3 2", Utils.StringReplaceSubstr("1 2 3 2", " 2 ", "X", ref loc, out bool found).Substring(0, 5));
        Assert.True(found);
        Assert.Equal(2, loc);
        Assert.Equal("abcabc", Utils.StringReplaceSubstr("abcabc", "x", "y", out found));
        Assert.False(found);
        Assert.Equal("aZc", Utils.StringReplaceSubstr("abc", "b", "Z", out found));
        Assert.Equal("abc", Utils.StringReplaceSubstr("abc", "b", "b", out found));
        Assert.False(found);

        Assert.True(Utils.StringFindSubstr("hello world", "o w", out loc));
        Assert.Equal(4, loc);
        Assert.False(Utils.StringFindSubstr("hello", "z", out loc));
        Assert.Equal(-1, loc);
        Assert.Throws<ArgumentException>(() => Utils.StringFindSubstr("hello", "", out _));

        L_Dna da = Utils.StringFindEachSubstr("baaabbbaa", "aa")!;
        Assert.Equal(2, da.Count);
        Assert.Equal(1, da.GetIValue(0));
        Assert.Equal(7, da.GetIValue(1));
        Assert.Null(Utils.StringFindEachSubstr("baaabbbaa", "zz"));
    }

    /*--------------------------- arrays ----------------------------*/

    [Fact]
    public void ArrayFindSequence_And_FindEach()
    {
        byte[] data = { 0, 1, 2, 0, 1, 2, 3 };
        Assert.True(Utils.ArrayFindSequence(data, new byte[] { 1, 2 }, out int offset));
        Assert.Equal(1, offset);
        Assert.False(Utils.ArrayFindSequence(data, new byte[] { 2, 2 }, out offset));
        Assert.Equal(0, offset);
        Assert.False(Utils.ArrayFindSequence(data, ReadOnlySpan<byte>.Empty, out _));
        Assert.False(Utils.ArrayFindSequence(new byte[] { 1 }, new byte[] { 1, 2 }, out _));

        L_Dna da = Utils.ArrayFindEachSequence(data, new byte[] { 1, 2 })!;
        Assert.Equal(2, da.Count);
        Assert.Equal(4, da.GetIValue(1));
        Assert.Null(Utils.ArrayFindEachSequence(data, new byte[] { 9 }));
    }

    [Fact]
    public void ArrayReplaceEachSequence_ReplacesRemovesAndCopies()
    {
        byte[] datas = { 5, 1, 1, 1, 6, 1, 1 };
        byte[] d1 = Utils.ArrayReplaceEachSequence(datas, new byte[] { 1, 1 }, new byte[] { 9 },
                                                   out int len, out int count);
        Assert.Equal(2, count);
        Assert.Equal(new byte[] { 5, 9, 1, 6, 9 }, d1.AsSpan(0, len).ToArray());
        Assert.True(d1.Length >= len + 4);        /* null terminated */
        byte[] d2 = Utils.ArrayReplaceEachSequence(datas, new byte[] { 1, 1 }, ReadOnlySpan<byte>.Empty,
                                                   out len, out count);
        Assert.Equal(new byte[] { 5, 1, 6 }, d2.AsSpan(0, len).ToArray());
        byte[] d3 = Utils.ArrayReplaceEachSequence(datas, new byte[] { 7 }, new byte[] { 8, 8 },
                                                   out len, out count);
        Assert.Equal(0, count);
        Assert.Equal(datas, d3.AsSpan(0, len).ToArray());
        byte[] d4 = Utils.ArrayReplaceEachSequence(datas, new byte[] { 6 }, new byte[] { 3, 3, 3 },
                                                   out len, out count);
        Assert.Equal(new byte[] { 5, 1, 1, 1, 3, 3, 3, 1, 1 }, d4.AsSpan(0, len).ToArray());
    }

    [Fact]
    public void ReallocNew_And_BinaryCopy()
    {
        byte[]? old = { 1, 2, 3, 4 };
        byte[]? bigger = Utils.ReallocNew(ref old, 4, 6);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 0, 0 }, bigger);
        Assert.Null(old);
        old = new byte[] { 1, 2, 3, 4 };
        Assert.Equal(new byte[] { 1, 2 }, Utils.ReallocNew(ref old, 4, 2));
        Assert.Null(Utils.ReallocNew(ref old, 0, 0));
        old = null;
        Assert.Equal(new byte[3], Utils.ReallocNew(ref old, 0, 3));

        byte[] copy = Utils.BinaryCopy(new byte[] { 7, 8 });
        Assert.Equal(new byte[] { 7, 8, 0, 0, 0, 0 }, copy);
    }

    /*---------------------------- paths ----------------------------*/

    [Theory]
    [InlineData("//tmp//", "//abc/", "/tmp/abc")]
    [InlineData("tmp/", "/abc/", "tmp/abc")]
    [InlineData("tmp/", "abc/", "tmp/abc")]
    [InlineData("/tmp/", "///", "/tmp")]
    [InlineData("/tmp/", null, "/tmp")]
    [InlineData("//", "/abc//", "/abc")]
    [InlineData("//", null, "/")]
    [InlineData(null, "/abc/def/", "/abc/def")]
    [InlineData(null, "abc//", "abc")]
    [InlineData(null, "//", "/")]
    [InlineData(null, null, "")]
    [InlineData("", "", "")]
    [InlineData("", "/", "/")]
    public void PathJoin_FollowsTheDocumentedExamples(string? dir, string? fname, string expected)
    {
        Assert.Equal(expected, Utils.PathJoin(dir, fname));
    }

    [Fact]
    public void PathJoin_RejectsLeadingDotDot()
    {
        Assert.Throws<ArgumentException>(() => Utils.PathJoin("..", "/etc/foo"));
        Assert.Throws<ArgumentException>(() => Utils.PathJoin("/tmp", ".."));
    }

    [Fact]
    public void AppendSubdirs_JoinsWithSingleSlash()
    {
        Assert.Equal("/a/b/c", Utils.AppendSubdirs("/a/b", "c"));
        Assert.Equal("/a/b/c/d", Utils.AppendSubdirs("/a/b/", "/c/d/"));
        Assert.Equal("/a/b", Utils.AppendSubdirs("/a/b/", ""));
    }

    [Fact]
    public void ModifyTrailingSlash_AddsAndRemoves()
    {
        Assert.Equal("/tmp/", Utils.ModifyTrailingSlash("/tmp", L.L_ADD_TRAIL_SLASH));
        Assert.Equal("/tmp/", Utils.ModifyTrailingSlash("/tmp/", L.L_ADD_TRAIL_SLASH));
        Assert.Equal("/tmp", Utils.ModifyTrailingSlash("/tmp/", L.L_REMOVE_TRAIL_SLASH));
        Assert.Equal("/tmp", Utils.ModifyTrailingSlash("/tmp", L.L_REMOVE_TRAIL_SLASH));
        Assert.Throws<ArgumentException>(() => Utils.ModifyTrailingSlash("/tmp", 7));
    }

    [Fact]
    public void MakeTempDirname_JoinsTmpAndSubdir()
    {
        string tmp = Utils.MakeTempDirname(null);
        Assert.Equal(Utils.GenPathname("/tmp", null), tmp);
        Assert.Equal(Utils.GenPathname("/tmp/lept/golden", null), Utils.MakeTempDirname("lept/golden"));
        Assert.Throws<ArgumentException>(() => Utils.MakeTempDirname("/abs"));
        Assert.Throws<ArgumentException>(() => Utils.MakeTempDirname(".hidden"));
    }

    [Theory]
    [InlineData("/dir/file_12.png", 0, 0, -1)]
    [InlineData("/dir/file_12.png", 5, 0, 12)]
    [InlineData("/dir/12.png", 0, 0, 12)]
    [InlineData("abc007x", 3, 1, 7)]
    [InlineData("abc.png", 0, 0, -1)]
    [InlineData("abc12", 10, 0, -1)]
    [InlineData("-5", 0, 0, -5)]
    public void ExtractNumberFromFilename_FindsTheNumber(string fname, int numpre, int numpost, int expected)
    {
        Assert.Equal(expected, Utils.ExtractNumberFromFilename(fname, numpre, numpost));
    }

    /*---------------------------- files ----------------------------*/

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "utils2test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void BinaryReadSelect_ReadsAByteRange()
    {
        string dir = TempDir();
        try {
            string path = Path.Combine(dir, "f.bin");
            File.WriteAllBytes(path, new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });
            Assert.Equal(new byte[] { 2, 3, 4 }, Utils.BinaryReadSelect(path, 2, 3));
            Assert.Equal(new byte[] { 5, 6, 7 }, Utils.BinaryReadSelect(path, 5, 0));
            Assert.Equal(new byte[] { 6, 7 }, Utils.BinaryReadSelect(path, 6, 10));
            Assert.Equal(8, Utils.BinaryReadSelect(path, 0, 0).Length);
            Assert.Throws<ArgumentException>(() => Utils.BinaryReadSelect(path, 9, 1));
            string empty = Path.Combine(dir, "e.bin");
            File.WriteAllBytes(empty, Array.Empty<byte>());
            Assert.Empty(Utils.BinaryReadSelect(empty, 0, 0));
        } finally {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FileCopyConcatenateAppendString()
    {
        string dir = TempDir();
        try {
            string a = Path.Combine(dir, "a.txt");
            string b = Path.Combine(dir, "b.txt");
            File.WriteAllText(a, "hello");
            Utils.FileCopy(a, b);
            Assert.Equal("hello", File.ReadAllText(b));
            Utils.FileConcatenate(a, b);
            Assert.Equal("hellohello", File.ReadAllText(b));
            Utils.FileAppendString(b, "!");
            Assert.Equal("hellohello!", File.ReadAllText(b));
        } finally {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FileSplitLinesUniform_SplitsIntoRoughlyEqualParts()
    {
        string dir = TempDir();
        try {
            string src = Path.Combine(dir, "lines.txt");
            File.WriteAllText(src, "a\nb\nc\nd\ne\n");
            string root = Path.Combine(dir, "out");
            Utils.FileSplitLinesUniform(src, 2, 1, root, ".txt");
            Assert.Equal("a\nb\n", File.ReadAllText(root + "_0.txt"));
            Assert.Equal("c\nd\ne\n", File.ReadAllText(root + "_1.txt"));
            Assert.Throws<ArgumentException>(() => Utils.FileSplitLinesUniform(src, 0, 1, root, null));
            Assert.Throws<LeptonicaException>(() => Utils.FileSplitLinesUniform(src, 6, 1, root, null));
        } finally {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FopenReadFromMemory_ReadsTheData()
    {
        using Stream fp = Utils.FopenReadFromMemory(new byte[] { 1, 2, 3 });
        Assert.Equal(3, Utils.FnbytesInFile(fp));
        Assert.Equal(new byte[] { 1, 2, 3 }, Utils.BinaryReadStream(fp));
    }

    [Fact]
    public void TempDirectoryFunctions_MakeMoveCopyRemove()
    {
        string sub = "utils2test_" + Guid.NewGuid().ToString("N");
        string tmpdir = Utils.MakeTempDirname(sub);
        string src = Path.Combine(TempDir(), "src.txt");
        try {
            Directory.CreateDirectory(tmpdir);
            Assert.True(Utils.LeptDirexists("/tmp/" + sub));
            Assert.False(Utils.LeptDirexists("/tmp/" + sub + "/nonexistent"));
            File.WriteAllText(src, "data");

            Assert.Equal(0, Utils.LeptCp(src, sub, null, out string newpath));
            Assert.Equal(Utils.PathJoin(tmpdir, "src.txt"), newpath);
            Assert.Equal("data", File.ReadAllText(newpath));
            Assert.Equal(0, Utils.LeptCp(src, sub, "copy.txt", out newpath));
            Assert.True(File.Exists(Path.Combine(tmpdir, "copy.txt")));
            Assert.True(File.Exists(src));

            Assert.Equal(0, Utils.LeptMv(src, sub, "moved.txt", out newpath));
            Assert.True(File.Exists(newpath));
            Assert.False(File.Exists(src));
            Assert.Equal(1, Utils.LeptCp(src, sub, null, out _));    /* source is gone */

            Assert.Equal(0, Utils.LeptRm(sub, "copy.txt"));
            Assert.Equal(1, Utils.LeptRm(sub, "copy.txt"));          /* already removed */
            Assert.Equal(0, Utils.LeptRmMatch(sub, "mov"));
            Assert.False(File.Exists(newpath));
            Assert.Equal(-1, Utils.LeptRmMatch(sub + "/nonexistent", null));
            Assert.Equal(0, Utils.LeptRmMatch(sub, "src"));          /* matches src.txt */
            Assert.Empty(Directory.GetFiles(tmpdir));

            Assert.Equal(0, Utils.LeptRmdir(sub));
            Assert.False(Directory.Exists(tmpdir));
            Assert.Equal(0, Utils.LeptRmdir(sub));                   /* fails silently */
            Assert.Throws<ArgumentException>(() => Utils.LeptRmdir("/abs"));
        } finally {
            if (Directory.Exists(tmpdir))
                Directory.Delete(tmpdir, true);
            Directory.Delete(Path.GetDirectoryName(src)!, true);
        }
    }

    [Fact]
    public void LeptRmfile_RemovesAnyFile()
    {
        string dir = TempDir();
        try {
            string f = Path.Combine(dir, "x");
            File.WriteAllText(f, "x");
            Assert.Equal(0, Utils.LeptRmfile(f));
            Assert.Equal(1, Utils.LeptRmfile(f));
            Assert.Throws<ArgumentException>(() => Utils.LeptRmfile(""));
        } finally {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void LMakeTempFilename_CreatesAUniqueFile()
    {
        string f1 = Utils.LMakeTempFilename();
        string f2 = Utils.LMakeTempFilename();
        try {
            Assert.NotEqual(f1, f2);
            Assert.True(File.Exists(f1));
            Assert.Contains(OperatingSystem.IsWindows() ? "lp." : "lept.", f1);
        } finally {
            File.Delete(f1);
            File.Delete(f2);
        }
    }

    [Fact]
    public void LeptFopen_PicksTheStreamByMode()
    {
        string dir = TempDir();
        try {
            string f = Path.Combine(dir, "x");
            using (FileStream w = Utils.LeptFopen(f, "wb"))
                w.WriteByte(42);
            using FileStream r = Utils.LeptFopen(f, "rb");
            Assert.Equal(42, r.ReadByte());
            Utils.LeptFclose(r);
        } finally {
            Directory.Delete(dir, true);
        }
    }
}
