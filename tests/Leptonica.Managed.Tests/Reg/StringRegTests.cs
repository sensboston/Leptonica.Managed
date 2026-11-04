using System;
using System.IO;
using System.Text;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// string_reg.c: search/replace for strings and arrays, sarray generation and
/// flattening, sarray serialization, file splitting, sarray splitting, string
/// length and string concatenation.
/// </summary>
/// <remarks>
/// The C strings that live in char buffers (smallbuf, medbuf) are byte arrays here;
/// a string argument made from such a buffer is the text up to its first NUL byte.
/// </remarks>
public class StringRegTests
{
    private const string strs = "This is a gooood test!";
    private const string substr1 = "o";
    private const string substr2 = "00";

    /// <summary>The text of a NUL-terminated buffer.</summary>
    private static string CStr(byte[] buf)
    {
        int n = Array.IndexOf(buf, (byte)0);
        return Encoding.Latin1.GetString(buf, 0, n < 0 ? buf.Length : n);
    }

    [RegFact]
    public void String()
    {
        int i, loc, count, n, size1, size2;
        string str0, str1, str2, str3, str4, str5, str6;
        byte[] data1, data2;
        L_Dna? da;
        Sarray sa1, sa2, sa3, sa4, sa5;
        var rp = new RegTest("string");
        string kernelreg = Path.Combine(TestPaths.ProgDir, "kernel_reg.c");

        Utils.LeptMkdir("lept/string");

            /* Finding all substrings */
        da = Utils.StringFindEachSubstr(strs, substr1);
        rp.CompareValues(4, da!.Count, 0.0f);  /* 0 */

            /* Replacing a substring */
        loc = 0;
        str1 = Utils.StringReplaceSubstr(strs, "his", "hers", ref loc, out _);
        rp.CompareValues(5, loc, 0.0f);  /* 1 */
        rp.CompareStrings(Encoding.Latin1.GetBytes("Thers is a gooood test!"),
                          Encoding.Latin1.GetBytes(str1));   /* 2 */

            /* Replacing all substrings */
        str1 = Utils.StringReplaceEachSubstr(strs, substr1, substr2, out count);
        rp.CompareValues(4, count, 0.0f);  /* 3 */
        rp.CompareStrings(Encoding.Latin1.GetBytes("This is a g00000000d test!"),
                          Encoding.Latin1.GetBytes(str1));   /* 4 */

        str1 = Utils.StringReplaceEachSubstr(strs, substr1, "", out count);
        rp.CompareValues(4, count, 0.0f);  /* 5 */
        rp.CompareStrings(Encoding.Latin1.GetBytes("This is a gd test!"),
                          Encoding.Latin1.GetBytes(str1));   /* 6 */

            /* Finding all sequences */
        byte[] bytes1 = Utils.BinaryRead(kernelreg);
        da = Utils.ArrayFindEachSequence(bytes1, "Destroy"u8);
        rp.CompareValues(35, da!.Count, 0.0f);  /* 7 */

            /* Replacing all sequences */
        bytes1 = Utils.BinaryRead(kernelreg);
        data1 = Utils.ArrayReplaceEachSequence(bytes1, "Destroy"u8, "####"u8, out size2, out count);
        Utils.BinaryWrite("/tmp/lept/string/string1.txt", "w", data1.AsSpan(0, size2));
        rp.CheckFile("/tmp/lept/string/string1.txt");  /* 8 */
        rp.CompareValues(35, count, 0.0f);  /* 9 */
        data2 = Utils.ArrayReplaceEachSequence(bytes1, "Destroy"u8, ReadOnlySpan<byte>.Empty,
                                               out size2, out count);
        Utils.BinaryWrite("/tmp/lept/string/string2.txt", "w", data2.AsSpan(0, size2));
        rp.CheckFile("/tmp/lept/string/string2.txt");  /* 10 */
        rp.CompareValues(35, count, 0.0f);  /* 11 */

            /* Generating sarray from strings, and v.v  */
        str0 = Encoding.Latin1.GetString(Utils.BinaryRead(kernelreg));
        str0 = str0.Substring(0, 2500);   /* str0[2500] = '\0' */
        sa1 = Sarray.CreateWordsFromString(str0.Substring(2000));
        sa2 = Sarray.CreateLinesFromString(str0.Substring(2000), 0);
        sa3 = Sarray.CreateLinesFromString(str0.Substring(2000), 1);
        str1 = sa1.ToString(0);
        str2 = sa1.ToString(1);
        str3 = sa2.ToString(0);
        str4 = sa2.ToString(1);
        str5 = sa3.ToString(0);
        str6 = sa3.ToString(1);
        Utils.BinaryWrite("/tmp/lept/string/test1.txt", "w", Encoding.Latin1.GetBytes(str1));
        Utils.BinaryWrite("/tmp/lept/string/test2.txt", "w", Encoding.Latin1.GetBytes(str2));
        Utils.BinaryWrite("/tmp/lept/string/test3.txt", "w", Encoding.Latin1.GetBytes(str3));
        Utils.BinaryWrite("/tmp/lept/string/test4.txt", "w", Encoding.Latin1.GetBytes(str4));
        Utils.BinaryWrite("/tmp/lept/string/test5.txt", "w", Encoding.Latin1.GetBytes(str5));
        Utils.BinaryWrite("/tmp/lept/string/test6.txt", "w", Encoding.Latin1.GetBytes(str6));
        rp.CheckFile("/tmp/lept/string/test1.txt");  /* 12 */
        rp.CheckFile("/tmp/lept/string/test2.txt");  /* 13 */
        rp.CheckFile("/tmp/lept/string/test3.txt");  /* 14 */
        rp.CheckFile("/tmp/lept/string/test4.txt");  /* 15 */
        rp.CheckFile("/tmp/lept/string/test5.txt");  /* 16 */
        rp.CheckFile("/tmp/lept/string/test6.txt");  /* 17 */
        rp.CompareFiles(14, 16);  /* 18 */

            /* Test sarray serialization */
        str1 = Encoding.Latin1.GetString(Utils.BinaryRead(kernelreg));
        sa1 = Sarray.CreateLinesFromString(str1, 0);
        sa1.Write("/tmp/lept/string/test7.txt");
        sa2 = Sarray.Read("/tmp/lept/string/test7.txt");
        sa2.Write("/tmp/lept/string/test8.txt");
        rp.CheckFile("/tmp/lept/string/test7.txt");  /* 19 */
        rp.CheckFile("/tmp/lept/string/test8.txt");  /* 20 */
        rp.CompareFiles(19, 20);  /* 21 */

            /* Test byte replacement in a file:
             *   - replace 200 bytes by 10 bytes
             *   - remove the 10 bytes
             *   - recover the 200 bytes and insert back  */
        byte[] abcdefghij = Encoding.Latin1.GetBytes("abcdefghij\0");   /* sizeof("abcdefghij") is 11 */
        Utils.FileReplaceBytes(kernelreg, 100, 200, abcdefghij, "/tmp/lept/string/junk1.txt");
        str1 = Encoding.Latin1.GetString(Utils.BinaryRead(kernelreg));
        size1 = str1.Length;
        Utils.FileReplaceBytes("/tmp/lept/string/junk1.txt", 100, abcdefghij.Length,
                               ReadOnlySpan<byte>.Empty, "/tmp/lept/string/junk2.txt");
        str2 = Utils.StringCopySegment(str1, 100, 200);
        Utils.FileReplaceBytes("/tmp/lept/string/junk2.txt", 100, 0, Encoding.Latin1.GetBytes(str2),
                               "/tmp/lept/string/junk3.txt");
        str3 = Encoding.Latin1.GetString(Utils.BinaryRead("/tmp/lept/string/junk3.txt"));
        rp.CompareStrings(Encoding.Latin1.GetBytes(str1), Encoding.Latin1.GetBytes(str3));
                                                                    /* 22 */

            /* File splitting by lines.  sarrayCreateLinesFromString() removes the CR of a CRLF
             * line end, so the pieces can only be put back together to the original if the file
             * has LF line ends.  prog/kernel_reg.c has CRLF when it is checked out on Windows (the
             * goldens of the other checks were made from that file), so this check uses an LF copy. */
        string kernellf = "/tmp/lept/string/kernel_reg_lf.c";
        Utils.BinaryWrite(kernellf, "w",
                          Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(Utils.BinaryRead(kernelreg))
                                                   .Replace("\r\n", "\n")));
        str1 = Encoding.Latin1.GetString(Utils.BinaryRead(kernellf));
        Utils.FileSplitLinesUniform(kernellf, 3, 1, "/tmp/lept/string/split", ".txt");
        string? joined = null;
        for (i = 0; i < 3; i++) {  /* put the pieces back together */
            string fname = $"/tmp/lept/string/split_{i}.txt";
            str3 = Encoding.Latin1.GetString(Utils.BinaryRead(fname));
            Utils.StringJoinIP(ref joined, str3);
        }
        rp.CompareStrings(Encoding.Latin1.GetBytes(str1), Encoding.Latin1.GetBytes(joined!));  /* 23 */

            /* Sarray splitting by lines */
        str1 = Encoding.Latin1.GetString(Utils.BinaryRead(kernelreg));
        sa1 = Sarray.CreateLinesFromString(str1, 0);
        sa2 = sa1.ConcatUniformly(6, 0);  /* into 6 strings */
        sa3 = Sarray.Create(0);
        for (i = 0; i < 6; i++) {
            str2 = sa2.GetString(i, L.L_NOCOPY);
            sa4 = Sarray.CreateLinesFromString(str2, 0);
            sa3.Join(sa4);
        }
        sa5 = sa3.ConcatUniformly(6, 0);  /* same as sa2 ? */
        byte[] sab3 = sa2.WriteMem();
        byte[] sab4 = sa5.WriteMem();
        rp.WriteDataAndCheck(sab3, ".sa");  /* 24 */
        rp.WriteDataAndCheck(sab4, ".sa");  /* 25 */
        rp.CompareFiles(24, 25);  /* 26 */

            /* String length */
        n = Utils.StringLength(""u8, 0);
        rp.CompareValues(0.0f, (float)n, 0.0f);   /* 27 */
        n = Utils.StringLength(""u8, 4);
        rp.CompareValues(0, (float)n, 0.0f);   /* 28 */
        n = Utils.StringLength("morethan4"u8, 4);
        rp.CompareValues(4, (float)n, 0.0f);   /* 29 */

            /* String concatenation */
        byte[] smallbuf = new byte[8];
        byte[] medbuf = new byte[32];
        smallbuf[0] = 0;
        n = Utils.StringCat(smallbuf.AsSpan(0, 8), "abc");
        rp.CompareValues(3.0f, (float)n, 0.0f);   /* 30 */
        n = Utils.StringCat(smallbuf.AsSpan(0, 8), "def");
        rp.CompareValues(3.0f, (float)n, 0.0f);   /* 31 */
        n = Utils.StringLength(smallbuf, 8);
        rp.CompareValues(6.0f, (float)n, 0.0f);   /* 32 */
        n = Utils.StringCat(smallbuf.AsSpan(0, 8), "gh");
        rp.CompareValues(-1.0f, (float)n, 0.0f);   /* 33 */
        Utils.StringCopy(medbuf.AsSpan(0, 32), CStr(smallbuf), 32);
        n = Utils.StringCat(medbuf.AsSpan(0, 32), CStr(smallbuf));
        rp.CompareValues(6.0f, (float)n, 0.0f);   /* 34 */
        n = Utils.StringLength(medbuf, 32);
        rp.CompareValues(12.0f, (float)n, 0.0f);   /* 35 */
        n = Utils.StringCat(medbuf.AsSpan(0, 32), CStr(medbuf));
        rp.CompareValues(12.0f, (float)n, 0.0f);   /* 36 */
        medbuf[23] = 0;   /* shorten by 1 byte */
        n = Utils.StringLength(medbuf, 32);
        rp.CompareValues(23.0f, (float)n, 0.0f);   /* 37 */
        str1 = Utils.StringConcatNew(CStr(medbuf), "jkl")!;
        n = Utils.StringLength(Encoding.Latin1.GetBytes(str1), 32);
        rp.CompareValues(26.0f, (float)n, 0.0f);   /* 38 */
        Utils.StringCopy(smallbuf.AsSpan(0, 8), CStr(medbuf), 6);
        n = Utils.StringLength(smallbuf, 8);
        rp.CompareValues(6.0f, (float)n, 0.0f);   /* 39 */
        Utils.StringCopy(smallbuf.AsSpan(0, 8), CStr(medbuf), 8);
        n = Utils.StringLength(smallbuf, 8);
        rp.CompareValues(8.0f, (float)n, 0.0f);   /* 40 */

        Assert.Equal(40, rp.Index);
        rp.Cleanup();
    }
}
