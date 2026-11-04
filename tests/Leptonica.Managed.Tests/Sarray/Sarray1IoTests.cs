using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Leptonica;

namespace Leptonica.Managed.Tests.SarrayTests;

public class Sarray1IoTests
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

    private static string Text(byte[] b) => Encoding.UTF8.GetString(b);

    [Fact]
    public void ExactTextFormat()
    {
        Sarray sa = Make("ab", "", "hello world");
        Assert.Equal(
            "\nSarray Version 1\n" +
            "Number of strings = 3\n" +
            "  0[2]:  ab\n" +
            "  1[0]:  \n" +
            "  2[11]:  hello world\n" +
            "\n",
            Text(sa.WriteMem()));
    }

    [Fact]
    public void ExactTextFormatEmpty()
    {
        Assert.Equal("\nSarray Version 1\nNumber of strings = 0\n\n",
                     Text(Sarray.Create(0).WriteMem()));
    }

    [Fact]
    public void LengthIsInBytes()
    {
        Assert.Equal("\nSarray Version 1\nNumber of strings = 1\n  0[2]:  é\n\n",
                     Text(Make("é").WriteMem()));
        Sarray back = Sarray.ReadMem(Make("é", "x").WriteMem());
        Assert.Equal(new[] { "é", "x" }, Strings(back));
    }

    [Fact]
    public void WriteStreamEqualsWriteMem()
    {
        Sarray sa = Make("a", "b c");
        var ms = new MemoryStream();
        sa.WriteStream(ms);
        Assert.Equal(sa.WriteMem(), ms.ToArray());
    }

    [Fact]
    public void RoundTripWithEmbeddedNewlinesAndFormatLookalikes()
    {
        Sarray sa = Make("line1\nline2\n", "", "  leading", "trailing  ",
                         "0[3]:  abc", "\n\n", new string('x', 2000));
        Sarray back = Sarray.ReadMem(sa.WriteMem());
        Assert.Equal(Strings(sa), Strings(back));
    }

    [Fact]
    public void RoundTripEmpty()
    {
        Sarray back = Sarray.ReadMem(Sarray.Create(0).WriteMem());
        Assert.Equal(0, back.Count);
    }

    [Fact]
    public void ReadsHandWrittenSerialization()
    {
        byte[] data = Encoding.ASCII.GetBytes(
            "\nSarray Version 1\nNumber of strings = 2\n  0[3]:  foo\n  1[3]:  b\nr\n\n");
        Assert.Equal(new[] { "foo", "b\nr" }, Strings(Sarray.ReadMem(data)));
    }

    [Fact]
    public void ReadRejectsBadInput()
    {
        Assert.Throws<LeptonicaException>(() => Sarray.ReadMem(Encoding.ASCII.GetBytes("junk")));
        Assert.Throws<LeptonicaException>(() => Sarray.ReadMem(
            Encoding.ASCII.GetBytes("\nSarray Version 2\nNumber of strings = 0\n\n")));
        Assert.Throws<LeptonicaException>(() => Sarray.ReadMem(
            Encoding.ASCII.GetBytes("\nSarray Version 1\nNumber of strings = -1\n\n")));
        Assert.Throws<LeptonicaException>(() => Sarray.ReadMem(
            Encoding.ASCII.GetBytes("\nSarray Version 1\nNumber of strings = 50000001\n\n")));
        /* truncated string */
        Assert.Throws<LeptonicaException>(() => Sarray.ReadMem(
            Encoding.ASCII.GetBytes("\nSarray Version 1\nNumber of strings = 1\n  0[9]:  abc\n")));
        /* missing size */
        Assert.Throws<LeptonicaException>(() => Sarray.ReadMem(
            Encoding.ASCII.GetBytes("\nSarray Version 1\nNumber of strings = 1\n  x\n")));
    }

    [Fact]
    public void ReadsConsecutiveSerializationsFromOneStream()
    {
        var ms = new MemoryStream();
        Make("a", "b").WriteStream(ms);
        Make("c").WriteStream(ms);
        Encoding.ASCII.GetBytes("TAIL").ToList().ForEach(b => ms.WriteByte(b));
        ms.Position = 0;

        Assert.Equal(new[] { "a", "b" }, Strings(Sarray.ReadStream(ms)));
        Assert.Equal(new[] { "c" }, Strings(Sarray.ReadStream(ms)));
        Assert.Equal((byte)'T', ms.ReadByte());   /* look-ahead byte was given back */
    }

    [Fact]
    public void FileWriteReadAppend()
    {
        string path = Path.Combine(Path.GetTempPath(), "sarray1_" + Guid.NewGuid().ToString("N"));
        try {
            Make("one", "two").Write(path);
            Assert.Equal("\nSarray Version 1\nNumber of strings = 2\n  0[3]:  one\n  1[3]:  two\n\n",
                         File.ReadAllText(path));
            Assert.Equal(new[] { "one", "two" }, Strings(Sarray.Read(path)));

            Make("three").Append(path);
            using (var fp = File.OpenRead(path)) {
                Assert.Equal(new[] { "one", "two" }, Strings(Sarray.ReadStream(fp)));
                Assert.Equal(new[] { "three" }, Strings(Sarray.ReadStream(fp)));
            }

            Make("replaced").Write(path);
            Assert.Equal(new[] { "replaced" }, Strings(Sarray.Read(path)));
        } finally {
            File.Delete(path);
        }
        Assert.Throws<LeptonicaException>(() => Sarray.Read(path));
    }

    [Fact]
    public void GetFilenamesInDirectoryListsFilesOnly()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sarray1_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            File.WriteAllText(Path.Combine(dir, "b.txt"), "x");
            File.WriteAllText(Path.Combine(dir, "a.png"), "x");
            Directory.CreateDirectory(Path.Combine(dir, "subdir"));

            Sarray sa = Sarray.GetFilenamesInDirectory(dir + Path.DirectorySeparatorChar);
            Assert.Equal(new[] { "a.png", "b.txt" }, Strings(sa).OrderBy(s => s, StringComparer.Ordinal));

            Assert.Equal(new[] { "b.txt" },
                         Strings(sa.SelectBySubstring(".txt")));
            Assert.Throws<ArgumentException>(() => Sarray.GetFilenamesInDirectory(""));
            Assert.Throws<LeptonicaException>(
                () => Sarray.GetFilenamesInDirectory(Path.Combine(dir, "missing")));
            Assert.Throws<LeptonicaException>(
                () => Sarray.GetFilenamesInDirectory(Path.Combine(dir, "a.png")));
        } finally {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SortedAndNumberedPathnames()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sarray1_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            File.WriteAllText(Path.Combine(dir, "f1.txt"), "x");
            File.WriteAllText(Path.Combine(dir, "7.txt"), "x");
            Sarray sorted = Sarray.GetSortedPathnamesInDirectory(dir, null, 0, 0);
            Assert.Equal(2, sorted.Count);
            Assert.EndsWith("/7.txt", sorted.GetString(0, L.L_NOCOPY));
            Assert.EndsWith("/f1.txt", sorted.GetString(1, L.L_NOCOPY));
            Sarray numbered = Sarray.GetNumberedPathnamesInDirectory(dir, null, 0, 0, 10);
            Assert.Equal(8, numbered.Count);
            Assert.EndsWith("/7.txt", numbered.GetString(7, L.L_NOCOPY));
            Assert.Equal("", numbered.GetString(3, L.L_NOCOPY));
            /* no match for the filter: no sort or path join is needed */
            Assert.Equal(0, Sarray.GetSortedPathnamesInDirectory(dir, "zzz", 0, 0).Count);
            Sarray none = Sarray.GetNumberedPathnamesInDirectory(dir, "zzz", 0, 0, 10);
            none.GetArray(out int nalloc, out int n);
            Assert.Equal((1, 0), (nalloc, n));
        } finally {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ConvertSortedToNumberedPathnamesEmptyInput()
    {
        Sarray sa = Sarray.ConvertSortedToNumberedPathnames(Sarray.Create(0), 0, 0, 10);
        sa.GetArray(out int nalloc, out int n);
        Assert.Equal((1, 0), (nalloc, n));
    }
}
