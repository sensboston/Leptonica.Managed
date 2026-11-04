using System;
using System.IO;
using System.Text;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// files_reg.c: regression test for lept_*() and other path utilities in utils2.c.
/// </summary>
public class FilesRegTests
{
    [RegFact]
    public void Files()
    {
        var rp = new RegTest("files");

            /* =================== Test pathJoin() =============== */
        TestPathJoin(rp, "/a/b//c///d//", "//e//f//g//", "/a/b/c/d/e/f/g");  /* 0 */
        TestPathJoin(rp, "/tmp/", "junk//", "/tmp/junk");  /* 1 */
        TestPathJoin(rp, "//tmp/", "junk//", "/tmp/junk");  /* 2 */
        TestPathJoin(rp, "tmp/", "//junk//", "tmp/junk");  /* 3 */
        TestPathJoin(rp, "tmp/", "junk/////", "tmp/junk");  /* 4 */
        TestPathJoin(rp, "/tmp/", "///", "/tmp");  /* 5 */
        TestPathJoin(rp, "////", null, "/");  /* 6 */
        TestPathJoin(rp, "//", "/junk//", "/junk");  /* 7 */
        TestPathJoin(rp, null, "/junk//", "/junk");  /* 8 */
        TestPathJoin(rp, null, "//junk//", "/junk");  /* 9 */
        TestPathJoin(rp, null, "junk//", "junk");  /* 10 */
        TestPathJoin(rp, null, "//", "/");  /* 11 */
        TestPathJoin(rp, null, null, "");  /* 12 */
        TestPathJoin(rp, "", "", "");  /* 13 */
        TestPathJoin(rp, "/", "", "/");  /* 14 */
        TestPathJoin(rp, "", "//", "/");  /* 15 */
        TestPathJoin(rp, "", "a", "a");  /* 16 */

            /* The next 3 joins properly throw ArgumentException in C# (return NULL in C) */
        Assert.Throws<ArgumentException>(() => Utils.PathJoin("..", "a"));
        Assert.Throws<ArgumentException>(() => Utils.PathJoin("/tmp", ".."));
        Assert.Throws<ArgumentException>(() => Utils.PathJoin("./", ".."));

            /* ======= Test lept_rmdir() and lept_mkdir() ======= */
        Utils.LeptRmdir("junkfiles");
        string junkfilesDir = Utils.MakeTempDirname("junkfiles");
        bool exists = Utils.LeptDirexists(junkfilesDir);
        rp.CompareValues(0, exists ? 1 : 0, 0.0f);  /* 17 */

        Utils.LeptMkdir("junkfiles");
        exists = Utils.LeptDirexists(junkfilesDir);
        rp.CompareValues(1, exists ? 1 : 0, 0.0f);  /* 18 */

            /* ======= Test lept_mv(), lept_cp(), lept_rm() ====== */
        string srcWeasel = Path.Combine(TestPaths.ProgDir, "weasel2.png");
        try {
            TestLeptCpRm(rp, srcWeasel, null, null);  /* 19 - 22 */
            TestLeptCpRm(rp, srcWeasel, "junkfiles", null);  /* 23 - 26 */
            TestLeptCpRm(rp, srcWeasel, null, "new_weasel2.png");  /* 27 - 30 */
            TestLeptCpRm(rp, srcWeasel, "junkfiles", "new_weasel2.png");  /* 31 - 34 */
        } finally {
            Utils.LeptRmdir("junkfiles");
            Utils.LeptRmdir("junko");
        }

            /* =============== Test genPathname() ================ */
        TestGenPathname(rp, "what/", null, "what");  /* 35 */
        TestGenPathname(rp, "what", "abc", "what/abc");  /* 36 */
        TestGenPathname(rp, null, "abc/def", "abc/def");  /* 37 */
        TestGenPathname(rp, "", "abc/def", "abc/def");  /* 38 */
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsIOS()) {
            TestGenPathname(rp, "/tmp", null, "/tmp");  /* 39 */
            TestGenPathname(rp, "/tmp/", null, "/tmp");  /* 40 */
            TestGenPathname(rp, "/tmp/junk", null, "/tmp/junk");  /* 41 */
            TestGenPathname(rp, "/tmp/junk/abc", null, "/tmp/junk/abc");  /* 42 */
            TestGenPathname(rp, "/tmp/junk/", null, "/tmp/junk");  /* 43 */
            TestGenPathname(rp, "/tmp/junk", "abc", "/tmp/junk/abc");  /* 44 */
            Assert.Equal(44, rp.Index);
        } else {
            Assert.Equal(38, rp.Index);
        }

        rp.Cleanup();
    }

    private static void TestPathJoin(RegTest rp, string? first, string? second, string result)
    {
        string path = Utils.PathJoin(first, second);
        rp.CompareStrings(Encoding.UTF8.GetBytes(result), Encoding.UTF8.GetBytes(path));
    }

    private static void TestLeptCpRm(RegTest rp, string srcpath, string? newdir, string? newtail)
    {
        Utils.SplitPathAtDirectory(srcpath, out _, out string srctail);
        string realtail = newtail ?? srctail;
        Utils.LeptRm(newdir, realtail);
        string realnewdir = Utils.MakeTempDirname(newdir);
        Sarray sa = Sarray.GetFilenamesInDirectory(realnewdir);
        int nfiles1 = CountFile(sa, realtail);

            /* Copy */
        Utils.LeptCp(srcpath, newdir, newtail, out _);
        sa = Sarray.GetFilenamesInDirectory(realnewdir);
        int nfiles2 = CountFile(sa, realtail);
        rp.CompareValues(1, nfiles2 - nfiles1, 0.0f);  /* '1' */

            /* Remove it */
        Utils.LeptRm(newdir, realtail);
        sa = Sarray.GetFilenamesInDirectory(realnewdir);
        nfiles2 = CountFile(sa, realtail);
        rp.CompareValues(0, nfiles2 - nfiles1, 0.0f);  /* '2' */

            /* Copy it again ... */
        Utils.LeptCp(srcpath, newdir, newtail, out _);

            /* move it elsewhere ... */
        Utils.LeptRmdir("junko");
        Utils.LeptMkdir("junko");
        string newsrc = Utils.PathJoin(realnewdir, realtail);
        Utils.LeptMv(newsrc, "junko", null, out _);
        string newnewdir = Utils.MakeTempDirname("junko");
        sa = Sarray.GetFilenamesInDirectory(newnewdir);
        int nfiles3 = sa.Count;
        rp.CompareValues(1, nfiles3, 0.0f);  /* '3' */

            /* and verify it was removed from the original location */
        sa = Sarray.GetFilenamesInDirectory(realnewdir);
        nfiles2 = CountFile(sa, realtail);
        rp.CompareValues(0, nfiles2 - nfiles1, 0.0f);  /* '4' */
    }

    private static int CountFile(Sarray sa, string tail)
    {
        int count = 0;
        for (int i = 0; i < sa.Count; i++) {
            Utils.SplitPathAtDirectory(sa.GetString(i, L.L_NOCOPY), out _, out string itemTail);
            if (string.Equals(itemTail, tail, StringComparison.Ordinal))
                count++;
        }
        return count;
    }

    private static void TestGenPathname(RegTest rp, string? dir, string? fname, string result)
    {
        string path = Utils.GenPathname(dir, fname);
        if (string.IsNullOrEmpty(dir)) {
            string localdir = Directory.GetCurrentDirectory();
            string expect = $"{localdir}/{result}";
            if (OperatingSystem.IsWindows())
                expect = Utils.ConvertSepCharsInPath(expect, Utils.UNIX_PATH_SEPCHAR);
            rp.CompareStrings(Encoding.UTF8.GetBytes(expect), Encoding.UTF8.GetBytes(path));
        } else {
            rp.CompareStrings(Encoding.UTF8.GetBytes(result), Encoding.UTF8.GetBytes(path));
        }
    }
}
