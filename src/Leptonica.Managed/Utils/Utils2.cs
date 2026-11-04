using System;
using System.IO;

namespace Leptonica;

/*
 *  utils2.c: Safe string operations, file and path utilities.
 *
 *  FILE* streams are System.IO.Stream; char* strings are string; byte buffers
 *  are byte[] / spans.  Not ported, because they only make sense with a C runtime
 *  or run a process: lept_calloc, lept_free (memory is managed), callSystemDebug
 *  (runs a shell command).  Notes on the managed equivalents:
 *    - String offsets of stringReplaceSubstr are in characters, not bytes.
 *    - stringCopy, stringLength and stringCat work on NUL-terminated byte buffers.
 *    - Functions that only report an error with a return code throw instead;
 *      lept_rmdir, lept_rm_match, lept_rm, lept_rmfile,
 *      lept_mv and lept_cp keep the 0 / non-zero return for operational failures.
 *    - modifyTrailingSlash and makeTempDirname return the string; there is no
 *      size limit of the buffer to check.
 */
/// <summary>Constants from pix.h used by the path utilities.</summary>
public static partial class L
{
    /// <summary>Add a trailing slash to the string.</summary>
    public const int L_ADD_TRAIL_SLASH = 1;
    /// <summary>Remove the trailing slash from the string.</summary>
    public const int L_REMOVE_TRAIL_SLASH = 2;
}

public static partial class Utils
{
    /// <summary>Path separator type selecting forward slashes (Unix style).</summary>
    public const int UNIX_PATH_SEPCHAR = 0;
    /// <summary>Path separator type selecting backslashes (Windows style).</summary>
    public const int WIN_PATH_SEPCHAR = 1;

    /* Rewrite paths starting with /tmp for macOS, iOS and Windows. */
    private static readonly bool rewriteTmp = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                                              || OperatingSystem.IsIOS();

    /*--------------------------------------------------------------------*
     *                       Safe string operations                       *
     *--------------------------------------------------------------------*/
    // leptonica: stringNew (utils2.c)
    /// <summary>Copy of a string.</summary>
    /// <param name="src"></param>
    /// <returns>dest copy of %src string, or null if %src is null</returns>
    /// <remarks>Strings are immutable, so the copy is the string itself.</remarks>
    public static string? StringNew(string? src)
    {
        if (src == null) {
            Log.Warning("stringNew", "src not defined");
            return null;
        }
        return src;
    }

    // leptonica: stringCopy (utils2.c)
    /// <summary>Copies at most n bytes of a string into a byte buffer, padding with zeros.</summary>
    /// <param name="dest">existing byte buffer; needs to be at least %n bytes in size</param>
    /// <param name="src">[optional] string; can be null</param>
    /// <param name="n">max number of characters to copy</param>
    /// <remarks>
    /// (1) Relatively safe wrapper for strncpy, that checks the input,
    ///     and does not complain if %src is null or %n &lt; 1.
    ///     If %n &lt; 1, this is a no-op.
    /// (2) The string is copied as UTF-8 bytes.
    /// </remarks>
    public static void StringCopy(Span<byte> dest, string? src, int n)
    {
        if (src == null || n < 1)
            return;

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(src);
        int i;
        for (i = 0; i < n && i < bytes.Length && bytes[i] != 0; i++)
            dest[i] = bytes[i];
        for (; i < n; i++)
            dest[i] = 0;
    }

    // leptonica: stringCopySegment (utils2.c)
    /// <summary>Copy of a segment of a string.</summary>
    /// <param name="src"></param>
    /// <param name="start">byte position at start of segment</param>
    /// <param name="nbytes">number of bytes in the segment; use 0 to go to end</param>
    /// <returns>copy of segment</returns>
    /// <remarks>
    /// (1) This is a variant of stringNew() that makes a new string
    ///     from a segment of the input string.  The segment is specified
    ///     by the starting position and the number of bytes.
    /// (2) The start location %start must be within the string %src.
    /// (3) The copy is truncated to the end of the source string.
    ///     Use %nbytes = 0 to copy to the end of %src.
    /// </remarks>
    public static string StringCopySegment(string src, int start, int nbytes)
    {
        int len = src.Length;
        if (start < 0 || start > len - 1)
            throw Lept.Arg("stringCopySegment", "invalid start");
        if (nbytes <= 0)  /* copy to the end */
            nbytes = len - start;
        if (start + nbytes > len)  /* truncate to the end */
            nbytes = len - start;
        return src.Substring(start, nbytes);
    }

    // leptonica: stringReplace (utils2.c)
    /// <summary>Replaces a string by a copy of another string.</summary>
    /// <param name="pdest">string copy</param>
    /// <param name="src">[optional] string; can be null</param>
    /// <remarks>
    /// (1) Frees any existing dest string
    /// (2) Puts a copy of src string in the dest
    /// (3) If either or both strings are null, does something reasonable.
    /// </remarks>
    public static void StringReplace(ref string? pdest, string? src)
    {
        if (src != null)
            pdest = StringNew(src);
        else
            pdest = null;
    }

    // leptonica: stringLength (utils2.c)
    /// <summary>Length of a NUL-terminated string in a buffer, checking at most size bytes.</summary>
    /// <param name="src">string can be null or NUL-terminated string</param>
    /// <param name="size">number of bytes to check; e.g., size of src buffer</param>
    /// <returns>length of src in bytes; 0 if no bytes are found; %size on error when NUL byte is not found.</returns>
    /// <remarks>
    /// (1) Safe implementation of strlen that only checks %size bytes
    ///     for trailing NUL.
    /// (2) Valid returned string lengths are between 0 and size - 1.
    ///     If %size bytes are checked without finding a NUL byte, then
    ///     an error is indicated by returning %size.
    /// </remarks>
    public static int StringLength(ReadOnlySpan<byte> src, int size)
    {
        if (src.IsEmpty)
            return 0;
        if (size < 1) {
            Log.Error("stringLength", "size < 1; too small");
            return 0;
        }

        for (int i = 0; i < size; i++) {
            if (i >= src.Length || src[i] == 0)  /* the end of the span counts as the terminator */
                return i;
        }

            /* Didn't find a NUL byte */
        Log.Error("stringLength", $"NUL byte not found in {size} bytes");
        return size;
    }

    // leptonica: stringCat (utils2.c)
    /// <summary>Appends a string to a NUL-terminated byte buffer.</summary>
    /// <param name="dest">null-terminated byte buffer; its length is the size of the buffer</param>
    /// <param name="src">string can be null</param>
    /// <returns>number of bytes added to dest; -1 on error</returns>
    /// <remarks>
    /// (1) Alternative implementation of strncat, that checks the input,
    ///     is easier to use (since the size of the dest buffer is specified
    ///     rather than the number of bytes to copy), and does not complain
    ///     if %src is null.
    /// (2) Never writes past end of dest.
    /// (3) If there is not enough room to append the src, which is an error,
    ///     it does nothing.
    /// </remarks>
    public static int StringCat(Span<byte> dest, string? src)
    {
        const string func = "stringCat";
        int size = dest.Length;

        if (size < 1) {
            Log.Error(func, "size < 1; too small");
            return -1;
        }
        if (src == null)
            return 0;

        int lendest = StringLength(dest, size);
        if (lendest == size) {
            Log.Error(func, "no terminating nul byte");
            return -1;
        }
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(src);
        int lensrc = StringLength(bytes, size);
        if (lensrc == 0)
            return 0;  /* nothing added to dest */
        int n = (lendest + lensrc > size - 1) ? 0 : lensrc;
        if (n == 0) {
            Log.Error(func, "dest too small for append");
            return -1;
        }

        for (int i = 0; i < n; i++)
            dest[lendest + i] = bytes[i];
        dest[lendest + n] = 0;
        return n;
    }

    // leptonica: stringConcatNew (utils2.c)
    /// <summary>Concatenates a list of strings.</summary>
    /// <param name="first">first string in list</param>
    /// <param name="rest">list of strings; the list ends at the first null</param>
    /// <returns>result new string concatenating the input strings, or null if first == null</returns>
    public static string? StringConcatNew(string? first, params string?[] rest)
    {
        if (first == null) return null;

        System.Text.StringBuilder result = new System.Text.StringBuilder(first);
        foreach (string? arg in rest) {
            if (arg == null)
                break;
            result.Append(arg);
        }
        return result.ToString();
    }

    // leptonica: stringJoin (utils2.c)
    /// <summary>Concatenates two strings; a null string counts as empty.</summary>
    /// <param name="src1">[optional] string; can be null</param>
    /// <param name="src2">[optional] string; can be null</param>
    /// <returns>concatenated string</returns>
    /// <remarks>
    /// (1) This is a safe version of strcat; it makes a new string.
    /// (2) It is not an error if either or both of the strings
    ///     are empty, or if either or both of the pointers are null.
    /// </remarks>
    public static string StringJoin(string? src1, string? src2)
    {
        return (src1 ?? "") + (src2 ?? "");
    }

    // leptonica: stringJoinIP (utils2.c)
    /// <summary>Replaces the first string by its concatenation with the second.</summary>
    /// <param name="psrc1">string src1</param>
    /// <param name="src2">[optional] string; can be null</param>
    /// <remarks>
    /// (1) This is a safe in-place version of strcat.  The contents of
    ///     src1 is replaced by the concatenation of src1 and src2.
    /// (2) It is not an error if either or both of the strings
    ///     are empty (""), or if the strings (*psrc1, src2) are null.
    /// (3) src1 should be initialized to null or an empty string
    ///     before the first call.
    /// (5) Another function to consider for joining many strings is
    ///     stringConcatNew().
    /// </remarks>
    public static void StringJoinIP(ref string? psrc1, string? src2)
    {
        psrc1 = StringJoin(psrc1, src2);
    }

    // leptonica: stringReverse (utils2.c)
    /// <summary>Reversed copy of a string.</summary>
    /// <param name="src">string</param>
    /// <returns>dest newly-allocated reversed string</returns>
    public static string StringReverse(string src)
    {
        char[] dest = src.ToCharArray();
        Array.Reverse(dest);
        return new string(dest);
    }

    // leptonica: strtokSafe (utils2.c)
    /// <summary>Thread-safe strtok(); the input string is not altered.</summary>
    /// <param name="cstr">input string to be sequentially parsed; use null after the first call</param>
    /// <param name="seps">a string of character separators</param>
    /// <param name="psaveptr">the rest of the string after the last encountered separators, or null</param>
    /// <returns>substr a new string that is copied from the previous saveptr up to but not
    /// including the next separator character, or null if end of cstr.</returns>
    /// <remarks>
    /// (1) This is a thread-safe implementation of strtok.
    /// (2) It has the same interface as strtok_r.
    /// (3) It differs from strtok_r in usage in two respects:
    ///     (a) the input string is not altered
    ///     (b) each returned substring is newly allocated.
    /// (6) Important usage points:
    ///     ~ Input the string to be parsed on the first invocation.
    ///     ~ Then input NULL after that; the value returned in saveptr
    ///       is used in all subsequent calls.
    /// (7) A string ends at its first '\0' character, as a C string does.
    /// </remarks>
    public static string? StrtokSafe(string? cstr, string seps, ref string? psaveptr)
    {
        char nextc;
        string? start;
        int istart, i, j, nchars;

        if (cstr == null) {
            start = psaveptr;
        } else {
            start = cstr;
            psaveptr = null;
        }
        if (start == null)  /* nothing to do */
            return null;

            /* First time, scan for the first non-sep character */
        istart = 0;
        if (cstr != null) {
            for (istart = 0;; istart++) {
                if ((nextc = CharAt(start, istart)) == '\0') {
                    psaveptr = null;  /* in case caller doesn't check ret value */
                    return null;
                }
                if (seps.IndexOf(nextc) < 0)
                    break;
            }
        }

            /* Scan through, looking for a sep character; if none is
             * found, 'i' will be at the end of the string. */
        for (i = istart;; i++) {
            if ((nextc = CharAt(start, i)) == '\0')
                break;
            if (seps.IndexOf(nextc) >= 0)
                break;
        }

            /* Save the substring */
        nchars = i - istart;
        string substr = start.Substring(istart, nchars);

            /* Look for the next non-sep character.
             * If this is the last substring, return a null saveptr. */
        for (j = i;; j++) {
            if ((nextc = CharAt(start, j)) == '\0') {
                psaveptr = null;  /* no more non-sep characters */
                break;
            }
            if (seps.IndexOf(nextc) < 0) {
                psaveptr = start.Substring(j);  /* start here on next call */
                break;
            }
        }

        return substr;
    }

    private static char CharAt(string s, int i)
    {
        return i < s.Length ? s[i] : '\0';
    }

    // leptonica: stringSplitOnToken (utils2.c)
    /// <summary>Splits a string around the first sequence of separator characters.</summary>
    /// <param name="cstr">input string to be split; not altered</param>
    /// <param name="seps">a string of character separators</param>
    /// <param name="phead">copy of the input string, up to the first separator token encountered</param>
    /// <param name="ptail">copy of the part of the input string starting with the first
    /// non-separator character that occurs after the first separator is found; null if none</param>
    /// <remarks>
    /// (1) The input string is not altered; all split parts are new strings.
    /// (2) The split occurs around the first consecutive sequence of
    ///     tokens encountered.
    /// (3) The head goes from the beginning of the string up to
    ///     but not including the first token found.
    /// (4) The tail contains the second part of the string, starting
    ///     with the first char in that part that is NOT a token.
    /// (5) If no separator token is found, 'head' contains a copy
    ///     of the input string and 'tail' is null.
    /// </remarks>
    public static void StringSplitOnToken(string cstr, string seps, out string? phead, out string? ptail)
    {
        string? saveptr = null;

        ptail = null;
        phead = StrtokSafe(cstr, seps, ref saveptr);
        if (saveptr != null)
            ptail = StringNew(saveptr);
    }

    /*--------------------------------------------------------------------*
     *                       Find and replace procs                       *
     *--------------------------------------------------------------------*/
    // leptonica: stringCheckForChars (utils2.c)
    /// <summary>Tests whether a string contains any of a set of characters.</summary>
    /// <param name="src">input string; can be of zero length</param>
    /// <param name="chars">string of chars to be searched for in %src</param>
    /// <returns>true if any characters are found</returns>
    /// <remarks>
    /// (1) This can be used to sanitize an operation by checking for
    ///     special characters that don't belong in a string.
    /// </remarks>
    public static bool StringCheckForChars(string src, string chars)
    {
        int n = src.Length;
        for (int i = 0; i < n; i++) {
            char ch = src[i];
            if (ch != '\0' && chars.IndexOf(ch) >= 0)
                return true;
        }
        return false;
    }

    // leptonica: stringRemoveChars (utils2.c)
    /// <summary>Copy of a string with a set of characters removed.</summary>
    /// <param name="src">input string; can be of zero length</param>
    /// <param name="remchars">[optional] string of chars to be removed from src</param>
    /// <returns>dest string with specified chars removed</returns>
    public static string StringRemoveChars(string src, string? remchars)
    {
        if (remchars == null)
            return src;

        System.Text.StringBuilder dest = new System.Text.StringBuilder(src.Length);
        int nsrc = src.Length;
        for (int i = 0; i < nsrc; i++) {
            char ch = src[i];
            if (ch == '\0' || remchars.IndexOf(ch) < 0)
                dest.Append(ch);
        }

        return dest.ToString();
    }

    // leptonica: stringReplaceEachSubstr (utils2.c)
    /// <summary>Replaces every non-overlapping occurrence of a substring.</summary>
    /// <param name="src">input string; can be of zero length</param>
    /// <param name="sub1">substring to be replaced</param>
    /// <param name="sub2">substring to put in; can be ""</param>
    /// <param name="pcount">the number of times that sub1 is found in src; 0 if not found</param>
    /// <returns>dest string with substring replaced; a copy of src if sub1 is not found</returns>
    /// <remarks>
    /// (1) This is a wrapper for simple string substitution that uses
    ///     the more general function arrayReplaceEachSequence().
    /// (2) This finds every non-overlapping occurrence of %sub1 in
    ///     %src, and replaces it with %sub2.  By "non-overlapping"
    ///     we mean that after it finds each match, it removes the
    ///     matching characters, replaces with the substitution string
    ///     (if not empty), and continues.  For example, if you replace
    ///     'aa' by 'X' in 'baaabbb', you find one match at position 1
    ///     and return 'bXabbb'.
    /// (3) To only remove each instance of sub1, use "" for sub2
    /// (4) Returns a copy of %src if sub1 and sub2 are the same.
    /// (5) If the input %src is binary data that can have null characters,
    ///     use arrayReplaceEachSequence() directly.
    /// </remarks>
    public static string StringReplaceEachSubstr(string src, string sub1, string sub2, out int pcount)
    {
        byte[] datad = ArrayReplaceEachSequence(System.Text.Encoding.UTF8.GetBytes(src),
                                                System.Text.Encoding.UTF8.GetBytes(sub1),
                                                System.Text.Encoding.UTF8.GetBytes(sub2),
                                                out int datadlen, out pcount);
        return System.Text.Encoding.UTF8.GetString(datad, 0, datadlen);
    }

    // leptonica: stringReplaceSubstr (utils2.c)
    /// <summary>Replaces the first occurrence of a substring, searching from a location.</summary>
    /// <param name="src">input string; can be of zero length</param>
    /// <param name="sub1">substring to be replaced</param>
    /// <param name="sub2">substring to put in; can be ""</param>
    /// <param name="ploc">input start location for search; returns the loc after replacement</param>
    /// <param name="pfound">true if sub1 is found</param>
    /// <returns>dest string with substring replaced</returns>
    /// <remarks>
    /// (1) Replaces the first instance.
    /// (2) To remove sub1 without replacement, use "" for sub2.
    /// (3) Returns a copy of %src if either no instance of %sub1 is found,
    ///     or if %sub1 and %sub2 are the same.
    /// (4) *ploc must be initialized to the offset within %src from which
    ///     the search starts.  To search the string from the beginning,
    ///     set %loc = 0 and input &amp;loc.
    ///     After finding %sub1 and replacing it with %sub2, %loc will be
    ///     returned as the next position after %sub2 in the output string.
    /// (5) Note that the output string also includes all the characters
    ///     from the input string that occur after the single substitution.
    /// (6) Offsets are in characters (the C function counts bytes; the two agree for ASCII).
    /// </remarks>
    public static string StringReplaceSubstr(string src, string sub1, string sub2,
                                             ref int ploc, out bool pfound)
    {
        pfound = false;
        int loc = ploc;
        if (sub1 == sub2)
            return src;
        if (loc < 0 || loc > src.Length)
            return src;
        int ptr = src.IndexOf(sub1, loc, StringComparison.Ordinal);
        if (ptr < 0)
            return src;
        pfound = true;

        string dest = src.Substring(0, ptr) + sub2 + src.Substring(ptr + sub1.Length);
        ploc = ptr + sub2.Length;
        return dest;
    }

    /// <summary>stringReplaceSubstr() with a NULL %ploc: the search starts at the beginning.</summary>
    public static string StringReplaceSubstr(string src, string sub1, string sub2, out bool pfound)
    {
        int loc = 0;
        return StringReplaceSubstr(src, sub1, sub2, ref loc, out pfound);
    }

    // leptonica: stringFindEachSubstr (utils2.c)
    /// <summary>Offsets of every non-overlapping occurrence of a substring.</summary>
    /// <param name="src">input string; can be of zero length</param>
    /// <param name="sub">substring to be searched for</param>
    /// <returns>dna of offsets where the sequence is found, or null if none are found</returns>
    /// <remarks>
    /// (1) This finds every non-overlapping occurrence in %src of %sub.
    ///     After it finds each match, it moves forward in %src by the length
    ///     of %sub before continuing the search.  So for example,
    ///     if you search for the sequence 'aa' in the data 'baaabbb',
    ///     you find one match at position 1.
    /// </remarks>
    public static L_Dna? StringFindEachSubstr(string src, string sub)
    {
        return ArrayFindEachSequence(System.Text.Encoding.UTF8.GetBytes(src),
                                     System.Text.Encoding.UTF8.GetBytes(sub));
    }

    // leptonica: stringFindSubstr (utils2.c)
    /// <summary>Finds the first occurrence of a substring.</summary>
    /// <param name="src">input string; can be of zero length</param>
    /// <param name="sub">substring to be searched for; must not be empty</param>
    /// <param name="ploc">location of substring in src; -1 if not found</param>
    /// <returns>true if found</returns>
    /// <remarks>
    /// (1) This is a wrapper around strstr().  It finds the first
    ///     instance of %sub in %src.  If the substring is not found
    ///     and the location is returned, it has the value -1.
    /// (2) Both %src and %sub must be defined, and %sub must have
    ///     length of at least 1.
    /// </remarks>
    public static bool StringFindSubstr(string src, string sub, out int ploc)
    {
        ploc = -1;
        if (sub.Length == 0)
            throw Lept.Arg("stringFindSubstr", "substring length 0");
        if (src.Length == 0)
            return false;

        int ptr = src.IndexOf(sub, StringComparison.Ordinal);
        if (ptr < 0)  /* not found */
            return false;

        ploc = ptr;
        return true;
    }

    // leptonica: arrayReplaceEachSequence (utils2.c)
    /// <summary>Replaces (or removes) every non-overlapping occurrence of a byte sequence.</summary>
    /// <param name="datas">source byte array</param>
    /// <param name="seq">subarray of bytes to find in source data</param>
    /// <param name="newseq">replacement subarray; empty to remove</param>
    /// <param name="pdatadlen">length of dest byte array, in bytes</param>
    /// <param name="pcount">the number of times that seq is found in datas; 0 if not found</param>
    /// <returns>datad with all all subarrays replaced (or removed); it is longer than
    /// %pdatadlen and the bytes after the data are zero</returns>
    /// <remarks>
    /// (1) The byte arrays %datas, %seq and %newseq are not C strings,
    ///     because they can contain null bytes.
    /// (2) If %newseq is empty, this just removes all instances of %seq.
    ///     Otherwise, it replaces every non-overlapping occurrence of
    ///     %seq in %datas with %newseq. A new array %datad and its
    ///     size are returned.  See arrayFindEachSequence() for more
    ///     details on finding non-overlapping occurrences.
    /// (3) If no instances of %seq are found, this returns a copy of %datas.
    /// (4) The returned %datad is null terminated.
    /// (5) Can use stringReplaceEachSubstr() if using C strings.
    /// </remarks>
    public static byte[] ArrayReplaceEachSequence(ReadOnlySpan<byte> datas, ReadOnlySpan<byte> seq,
                                                  ReadOnlySpan<byte> newseq,
                                                  out int pdatadlen, out int pcount)
    {
        byte[] datad;
        int newsize;
        int n, i, j, di, si, index, incr;
        int dataslen = datas.Length;
        int seqlen = seq.Length;
        int newseqlen = newseq.Length;
        L_Dna? da;

        pcount = 0;
        pdatadlen = 0;

            /* Identify the locations of the sequence.  If there are none,
             * return a copy of %datas. */
        if ((da = ArrayFindEachSequence(datas, seq)) == null) {
            pdatadlen = dataslen;
            return BinaryCopy(datas);
        }

            /* Allocate the output data; insure null termination */
        n = da.Count;
        pcount = n;
        newsize = dataslen + n * (newseqlen - seqlen) + 4;
        datad = new byte[newsize];

            /* Replace each sequence instance with a new sequence */
        si = da.GetIValue(0);
        for (i = 0, di = 0, index = 0; i < dataslen; i++) {
            if (i == si) {
                index++;
                if (index < n) {
                    si = da.GetIValue(index);
                    incr = Math.Min(seqlen, si - i);  /* amount to remove from datas */
                } else {
                    incr = seqlen;
                }
                i += incr - 1;  /* jump over the matched sequence in datas */
                for (j = 0; j < newseqlen; j++)  /* add new sequence to datad */
                    datad[di++] = newseq[j];
            } else {
                datad[di++] = datas[i];
            }
        }

        pdatadlen = di;
        return datad;
    }

    // leptonica: arrayFindEachSequence (utils2.c)
    /// <summary>Offsets of every non-overlapping occurrence of a byte sequence.</summary>
    /// <param name="data">byte array</param>
    /// <param name="sequence">subarray of bytes to find in data</param>
    /// <returns>dna of offsets where the sequence is found, or null if none are found</returns>
    /// <remarks>
    /// (1) The byte arrays %data and %sequence are not C strings,
    ///     because they can contain null bytes.
    /// (2) This finds every non-overlapping occurrence in %data of %sequence.
    ///     After it finds each match, it moves forward by the length
    ///     of the sequence before continuing the search.  So for example,
    ///     if you search for the sequence 'aa' in the data 'baaabbb',
    ///     you find one match at position 1.
    /// </remarks>
    public static L_Dna? ArrayFindEachSequence(ReadOnlySpan<byte> data, ReadOnlySpan<byte> sequence)
    {
        int start, realoffset;
        L_Dna da;

        int datalen = data.Length;
        int seqlen = sequence.Length;
        da = L_Dna.Create(0);
        start = 0;
        while (true) {
            bool found = ArrayFindSequence(data.Slice(start), sequence, out int offset);
            if (found == false)
                break;

            realoffset = start + offset;
            da.AddNumber(realoffset);
            start = realoffset + seqlen;
            if (start >= datalen)
                break;
        }

        if (da.Count == 0)
            return null;
        return da;
    }

    // leptonica: arrayFindSequence (utils2.c)
    /// <summary>Finds the first occurrence of a byte sequence.</summary>
    /// <param name="data">byte array</param>
    /// <param name="sequence">subarray of bytes to find in data</param>
    /// <param name="poffset">offset from beginning of data where the sequence begins; 0 if not found</param>
    /// <returns>true if sequence is found</returns>
    /// <remarks>
    /// (1) The byte arrays 'data' and 'sequence' are in general not C strings,
    ///     because they can contain null bytes.
    /// (2) This searches for the first occurrence in %data of %sequence.
    ///     An empty sequence is never found.
    /// (4) If the sequence is not found, the offset will be 0, so you
    ///     must check the return value.
    /// </remarks>
    public static bool ArrayFindSequence(ReadOnlySpan<byte> data, ReadOnlySpan<byte> sequence,
                                         out int poffset)
    {
        int i, j, lastpos;
        bool found;

        poffset = 0;
        int datalen = data.Length;
        int seqlen = sequence.Length;
        lastpos = datalen - seqlen + 1;
        found = false;
        for (i = 0; i < lastpos; i++) {
            for (j = 0; j < seqlen; j++) {
                if (data[i + j] != sequence[j])
                     break;
                if (j == seqlen - 1)
                     found = true;
            }
            if (found == true)
                break;
        }

        if (found == true) {
            poffset = i;
            return true;
        }
        return false;
    }

    /*--------------------------------------------------------------------*
     *                             Safe realloc                           *
     *--------------------------------------------------------------------*/
    // leptonica: reallocNew (utils2.c)
    /// <summary>Allocates a new zeroed buffer, copying as much of the old data as fits.</summary>
    /// <param name="pindata">nulls indata before reallocing</param>
    /// <param name="oldsize">size of input data to be copied, in bytes</param>
    /// <param name="newsize">size of buffer to be reallocated in bytes</param>
    /// <returns>the new data, or null if newsize == 0</returns>
    /// <remarks>
    /// (1) If newsize == 0, frees input data and nulls ptr
    /// (2) If input data is null, only callocs new memory
    /// (3) This differs from realloc in that it always allocates
    ///     new memory (if newsize > 0) and initializes it to 0,
    ///     it requires the amount of old data to be copied,
    ///     and it takes the address of the input ptr and
    ///     nulls the handle.
    /// </remarks>
    public static byte[]? ReallocNew(ref byte[]? pindata, int oldsize, int newsize)
    {
        byte[]? indata = pindata;

        if (newsize == 0) {   /* nonstandard usage */
            pindata = null;
            return null;
        }

        byte[] newdata = new byte[newsize];
        if (indata == null)  /* nonstandard usage */
            return newdata;

            /* Standard usage */
        int minsize = Math.Min(oldsize, newsize);
        Array.Copy(indata, newdata, minsize);
        pindata = null;
        return newdata;
    }

    /*--------------------------------------------------------------------*
     *                    File opening (fopen wrappers)                   *
     *--------------------------------------------------------------------*/
    // leptonica: fopenReadStream (utils2.c)
    /// <summary>Opens a file for reading.</summary>
    /// <remarks>
    /// (1) This should be used whenever you want to open a file to
    ///     read from a stream.
    /// (2) This handles the temp directory pathname conversion where needed:
    ///         /tmp  ==>  [OS specific temp directory]
    /// (3) If the file is not found, it is looked for in the current directory
    ///     by its tail.
    /// </remarks>
    public static FileStream FopenReadStream(string filename)
    {
            /* Try input filename */
        string fname = GenPathname(filename, null);
        if (File.Exists(fname))
            return new FileStream(fname, FileMode.Open, FileAccess.Read, FileShare.Read);

            /* Else, strip directory and try locally */
        SplitPathAtDirectory(filename, out _, out string tail);
        if (File.Exists(tail))
            return new FileStream(tail, FileMode.Open, FileAccess.Read, FileShare.Read);
        Log.Error("fopenReadStream", $"failed to open locally with tail {tail} for filename {filename}");
        throw Lept.Fail("fopenReadStream", $"file not found: {filename}");
    }

    // leptonica: fopenWriteStream (utils2.c)
    /// <summary>Opens a file for writing or appending.</summary>
    /// <param name="filename">Path of the file to open.</param>
    /// <param name="modestring">An fopen-style mode: "w", "wb", "wb+", "a", "ab", and so on.</param>
    /// <returns>The opened stream.</returns>
    /// <remarks>
    /// This handles the temp directory pathname conversion where needed:
    /// a path starting with <c>/tmp</c> is mapped to the OS-specific temp directory.
    /// </remarks>
    public static FileStream FopenWriteStream(string filename, string modestring)
    {
        string fname = GenPathname(filename, null);
        FileMode mode = modestring.StartsWith('a') ? FileMode.Append : FileMode.Create;
        FileAccess access = modestring.Contains('+') && mode != FileMode.Append
                            ? FileAccess.ReadWrite : FileAccess.Write;
        try {
            return new FileStream(fname, mode, access, FileShare.Read);
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) {
            throw Lept.Fail("fopenWriteStream", $"stream not opened: {fname}");
        }
    }

    // leptonica: fopenReadFromMemory (utils2.c)
    /// <summary>Opens a read stream over a copy of a memory buffer.</summary>
    /// <param name="data"></param>
    /// <returns>file stream</returns>
    public static Stream FopenReadFromMemory(ReadOnlySpan<byte> data)
    {
        return new MemoryStream(data.ToArray(), false);
    }

    // leptonica: fopenWriteWinTempfile (utils2.c)
    /// <summary>Opens a temporary file stream for reading and writing, on Windows.</summary>
    /// <returns>file stream; null on other systems</returns>
    /// <remarks>
    /// (1) The Windows version of tmpfile() writes into the root
    ///     C:\ directory, which requires admin privileges.  This
    ///     function provides an alternative implementation.  The file is
    ///     deleted when the stream is closed.
    /// </remarks>
    public static FileStream? FopenWriteWinTempfile()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        string filename = LMakeTempFilename();
        return new FileStream(filename, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                              4096, FileOptions.DeleteOnClose);
    }

    /*--------------------------------------------------------------------*
     *                       Binary file read/write                       *
     *--------------------------------------------------------------------*/
    // leptonica: l_binaryRead (utils2.c)
    /// <summary>Reads an entire file into a byte array.</summary>
    /// <param name="filename">Path of the file to read.</param>
    /// <returns>The contents of the file.</returns>
    public static byte[] BinaryRead(string filename)
    {
        using FileStream fp = FopenReadStream(filename);
        return BinaryReadStream(fp);
    }

    // leptonica: l_binaryReadStream (utils2.c)
    /// <summary>Reads the rest of the stream from the current position.</summary>
    /// <param name="fp">The stream to read.</param>
    /// <returns>The bytes from the current position to the end of the stream.</returns>
    public static byte[] BinaryReadStream(Stream fp)
    {
        using MemoryStream ms = new MemoryStream();
        fp.CopyTo(ms);
        return ms.ToArray();
    }

    // leptonica: l_binaryWrite (utils2.c)
    /// <summary>Writes a block of bytes to a file.</summary>
    /// <param name="filename">Path of the file to write.</param>
    /// <param name="operation">"w" to write (overwrite); "a" to append.</param>
    /// <param name="data">The bytes to write; must not be empty.</param>
    /// <exception cref="ArgumentException"><paramref name="data"/> is empty or <paramref name="operation"/> is invalid.</exception>
    public static void BinaryWrite(string filename, string operation, ReadOnlySpan<byte> data)
    {
        if (data.Length <= 0)
            throw Lept.Arg("l_binaryWrite", "nbytes must be > 0");
        if (operation != "w" && operation != "a")
            throw Lept.Arg("l_binaryWrite", "operation not one of {'w','a'}");

        using FileStream fp = FopenWriteStream(filename, operation + "b");
        fp.Write(data);
    }

    // leptonica: l_binaryCompare (utils2.c)
    /// <summary>Determines whether two byte sequences are identical.</summary>
    /// <param name="data1">The first sequence.</param>
    /// <param name="data2">The second sequence.</param>
    /// <returns>true if the sequences are identical; otherwise false.</returns>
    public static bool BinaryCompare(ReadOnlySpan<byte> data1, ReadOnlySpan<byte> data2)
    {
        return data1.SequenceEqual(data2);
    }

    // leptonica: nbytesInFile (utils2.c)
    /// <summary>Gets the size of a file in bytes.</summary>
    /// <param name="filename">Path of the file.</param>
    /// <returns>The number of bytes in the file.</returns>
    public static long NbytesInFile(string filename)
    {
        using FileStream fp = FopenReadStream(filename);
        return FnbytesInFile(fp);
    }

    // leptonica: fnbytesInFile (utils2.c)
    /// <summary>Gets the total number of bytes in a stream; the position is unchanged.</summary>
    /// <param name="fp">The stream to measure.</param>
    /// <returns>The length of the stream in bytes.</returns>
    public static long FnbytesInFile(Stream fp)
    {
        return fp.Length;
    }

    // leptonica: l_binaryReadSelect (utils2.c)
    /// <summary>Reads part of a file into memory.</summary>
    /// <param name="filename"></param>
    /// <param name="start">first byte to read</param>
    /// <param name="nbytes">number of bytes to read; use 0 to read to end of file</param>
    /// <returns>the data; its length is the number of bytes actually read</returns>
    public static byte[] BinaryReadSelect(string filename, long start, long nbytes)
    {
        using FileStream fp = FopenReadStream(filename);
        return BinaryReadSelectStream(fp, start, nbytes);
    }

    // leptonica: l_binaryReadSelectStream (utils2.c)
    /// <summary>Reads part of a stream into memory.</summary>
    /// <param name="fp">file stream</param>
    /// <param name="start">first byte to read</param>
    /// <param name="nbytes">number of bytes to read; use 0 to read to end of file</param>
    /// <returns>the data; its length is the number of bytes actually read; reading 0 bytes
    /// is not an error</returns>
    /// <remarks>
    /// (1) The C function returns the array terminated with a null byte; the
    ///     number of bytes read is the length of the array here.
    /// (2) Side effect: the stream pointer is re-positioned to the
    ///     beginning of the file.
    /// </remarks>
    public static byte[] BinaryReadSelectStream(Stream fp, long start, long nbytes)
    {
        const string func = "l_binaryReadSelectStream";
        long bytesleft, bytestoread, nread, filebytes;

            /* Verify and adjust the parameters if necessary */
        filebytes = fp.Length;
        fp.Seek(0, SeekOrigin.Begin);
        if (start > filebytes) {
            Log.Error(func, $"start = {start} but filebytes = {filebytes}");
            throw Lept.Arg(func, "start beyond the end of the stream");
        }
        if (filebytes == 0)  /* start == 0; nothing to read */
            return Array.Empty<byte>();
        bytesleft = filebytes - start;  /* greater than 0 */
        if (nbytes == 0) nbytes = bytesleft;
        bytestoread = (bytesleft >= nbytes) ? nbytes : bytesleft;

            /* Read the data */
        byte[] data = new byte[bytestoread];
        fp.Seek(start, SeekOrigin.Begin);
        nread = 0;
        while (nread < bytestoread) {
            int got = fp.Read(data, (int)nread, (int)(bytestoread - nread));
            if (got <= 0)
                break;
            nread += got;
        }
        if (nbytes != nread)
            Log.Info(func, $"{nbytes} bytes requested; {nread} bytes read");
        fp.Seek(0, SeekOrigin.Begin);
        if (nread != data.Length)
            Array.Resize(ref data, (int)nread);
        return data;
    }

    /*--------------------------------------------------------------------*
     *                     Copy and compare in memory                     *
     *--------------------------------------------------------------------*/
    // leptonica: l_binaryCopy (utils2.c)
    /// <summary>Copy of a byte array, followed by 4 zero bytes.</summary>
    /// <param name="datas"></param>
    /// <returns>datad of length size + 4</returns>
    /// <remarks>
    /// (1) We add 4 bytes to the zeroed output because in some cases
    ///     (e.g., string handling) it is important to have the data
    ///     be null terminated.  This guarantees that after the memcpy,
    ///     the result is automatically null terminated.
    /// </remarks>
    public static byte[] BinaryCopy(ReadOnlySpan<byte> datas)
    {
        byte[] datad = new byte[datas.Length + 4];
        datas.CopyTo(datad);
        return datad;
    }

    /*--------------------------------------------------------------------*
     *                         File copy operations                       *
     *--------------------------------------------------------------------*/
    // leptonica: fileCopy (utils2.c)
    /// <summary>Copies a file.</summary>
    /// <param name="srcfile">copy from this file</param>
    /// <param name="newfile">copy to this file</param>
    public static void FileCopy(string srcfile, string newfile)
    {
        byte[] data = BinaryRead(srcfile);
        BinaryWrite(newfile, "w", data);
    }

    // leptonica: fileConcatenate (utils2.c)
    /// <summary>Appends the contents of a file to another file.</summary>
    /// <param name="srcfile">append data from this file</param>
    /// <param name="destfile">add data to this file</param>
    public static void FileConcatenate(string srcfile, string destfile)
    {
        byte[] data = BinaryRead(srcfile);
        try {
            BinaryWrite(destfile, "a", data);
        } catch (ArgumentException) {
            /* the C function ignores the result of l_binaryWrite(), which fails for no data */
        }
    }

    // leptonica: fileAppendString (utils2.c)
    /// <summary>Appends a string to a file.</summary>
    /// <param name="filename"></param>
    /// <param name="str">string to append to file</param>
    public static void FileAppendString(string filename, string str)
    {
        using FileStream fp = FopenWriteStream(filename, "a");
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(str);
        fp.Write(bytes, 0, bytes.Length);
    }

    /*--------------------------------------------------------------------*
     *                         File split operations                      *
     *--------------------------------------------------------------------*/
    // leptonica: fileSplitLinesUniform (utils2.c)
    /// <summary>Splits a text file into files with roughly equal numbers of lines.</summary>
    /// <param name="filename">input file</param>
    /// <param name="n">number of output files (&gt;= 1)</param>
    /// <param name="save_empty">1 to save empty lines; 0 to remove them</param>
    /// <param name="rootpath">root pathname of output files</param>
    /// <param name="ext">output extension, including the '.'; can be null</param>
    /// <remarks>
    /// (1) This splits an input text file into %n files with roughly
    ///     equal numbers of text lines in each file.
    /// (2) if %save_empty == 1, empty lines are included, and concatention
    ///     of the text in the split files will be identical to the original.
    /// (3) The output filenames are in the form:
    ///          &lt;rootpath&gt;_N.&lt;ext&gt;, N = 1, ... n
    /// (4) This handles the temp directory pathname conversion where needed:
    ///         /tmp  ==&gt;  [OS specific temp directory]
    /// (5) Files can also be sharded into sets of lines by the program 'split':
    ///         split -n l/&lt;n&gt; &lt;filename&gt;
    ///     Using 'split', the resulting files have approximately equal
    ///     numbers of bytes, rather than equal numbers of lines.
    /// </remarks>
    public static void FileSplitLinesUniform(string filename, int n, int save_empty,
                                             string rootpath, string? ext)
    {
        const string func = "fileSplitLinesUniform";
        int i, totlines, nlines, index;

        if (n <= 0)
            throw Lept.Arg(func, "n must be > 0");
        if (save_empty != 0 && save_empty != 1)
            throw Lept.Arg(func, "save_empty not 0 or 1");

            /* Make sarray of lines; the newlines are stripped off */
        byte[] data = BinaryRead(filename);
        Sarray sa = Sarray.CreateLinesFromString(System.Text.Encoding.UTF8.GetString(data), save_empty);
        totlines = sa.Count;
        if (n > totlines) {
            Log.Error(func, $"num files = {n} > num lines = {totlines}");
            throw Lept.Fail(func, "more files than lines");
        }

            /* Write n sets of lines to n files, adding the newlines back */
        Numa na = Numa.GetUniformBinSizes(totlines, n);
        index = 0;
        for (i = 0; i < n; i++) {
            string outname;
            if (ext == null)
                outname = $"{rootpath}_{i}";
            else
                outname = $"{rootpath}_{i}{ext}";
            nlines = na.GetIValue(i);
            string str = sa.ToStringRange(index, nlines, 1);  /* add newlines */
            BinaryWrite(outname, "w", System.Text.Encoding.UTF8.GetBytes(str));
            index += nlines;
        }
    }

    /*--------------------------------------------------------------------*
     *                       Path and directory utilities                 *
     *--------------------------------------------------------------------*/
    // leptonica: lept_mkdir (utils2.c)
    /// <summary>Makes a subdirectory of /tmp (with path rewriting), recursively.</summary>
    /// <param name="subdir">of /tmp or its OS specific equivalent</param>
    /// <returns>the number of directories that could not be made</returns>
    /// <remarks>
    /// (1) This is used to create a subdirectory of /tmp for writing
    ///     debug and test output.  It is disabled unless LeptDebugOK is true.
    /// </remarks>
    public static int LeptMkdir(string subdir)
    {
        if (!Pix.LeptDebugOK) {
            Log.Info("lept_mkdir", $"making named temp subdirectory {subdir} is disabled");
            return 0;
        }
        if ((subdir.Length == 0) || (subdir[0] == '.') || (subdir[0] == '/'))
            throw Lept.Arg("lept_mkdir", "subdir not an actual subdirectory");

        string dir = GenPathname("/tmp", null);
        int ret = 0;
        try {
            Directory.CreateDirectory(Path.Combine(dir, subdir));
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) {
            ret = 1;
        }
        if (ret > 0)
            Log.Error("lept_mkdir", $"failure to create {ret} directories");
        return ret;
    }

    // leptonica: splitPathAtExtension (utils2.c)
    /// <summary>Splits a pathname at the last dot of its tail.</summary>
    /// <param name="pathname">full path; can be a directory</param>
    /// <param name="pbasename">pathname not including the last dot and characters after that</param>
    /// <param name="pextension">path extension, which is the last dot and the characters after it.
    /// If there is no extension, it returns the empty string</param>
    /// <remarks>
    /// (3) This function makes decisions based only on the lexical
    ///     structure of the input.  Examples:
    ///       /usr/tmp/abc.jpg  --&gt;  basename: /usr/tmp/abc    ext: .jpg
    ///       /usr/tmp/.jpg     --&gt;  basename: /usr/tmp/       ext: .jpg
    ///       /usr/tmp.jpg/     --&gt;  basename: /usr/tmp.jpg/   ext: [empty str]
    ///       ./.jpg            --&gt;  basename: ./              ext: .jpg
    /// (4) The input can have either forward (unix) or backward (win)
    ///     slash separators.  The output has unix separators.
    /// (5) Note that basename, as used here, is different from the result
    ///     of the unix program 'basename'.  Here, basename is the entire
    ///     pathname up to a final extension and its preceding dot.
    /// </remarks>
    public static void SplitPathAtExtension(string pathname, out string pbasename, out string pextension)
    {
            /* Split out the directory first */
        SplitPathAtDirectory(pathname, out string dir, out string tail);

            /* Then look for a "." in the tail part.
             * This way we ignore all "." in the directory. */
        int lastdot = tail.LastIndexOf('.');
        if (lastdot >= 0) {
            pextension = tail.Substring(lastdot);
            pbasename = dir + tail.Substring(0, lastdot);
        } else {
            pextension = "";
            pbasename = pathname;
        }
    }

    // leptonica: splitPathAtDirectory (utils2.c)
    /// <summary>Splits a pathname at the last slash.</summary>
    /// <param name="pathname">The path to split.</param>
    /// <param name="pdir">Receives the directory with a trailing slash, or an empty string if there is no slash.</param>
    /// <param name="ptail">Receives the part after the last slash.</param>
    /// <remarks>
    /// Examples:
    /// <list type="bullet">
    /// <item><description><c>"/usr/local/abc"</c> gives <c>"/usr/local/"</c> and <c>"abc"</c>.</description></item>
    /// <item><description><c>"abc"</c> gives <c>""</c> and <c>"abc"</c>.</description></item>
    /// <item><description><c>"/usr/local/"</c> gives <c>"/usr/local/"</c> and <c>""</c>.</description></item>
    /// </list>
    /// </remarks>
    public static void SplitPathAtDirectory(string pathname, out string pdir, out string ptail)
    {
        string cpathname = ConvertSepCharsInPath(pathname, UNIX_PATH_SEPCHAR);
        int lastslash = cpathname.LastIndexOf('/');
        if (lastslash >= 0) {
            ptail = cpathname.Substring(lastslash + 1);
            pdir = cpathname.Substring(0, lastslash + 1);
        } else {  /* no directory */
            pdir = "";
            ptail = cpathname;
        }
    }

    // leptonica: convertSepCharsInPath (utils2.c)
    /// <summary>Converts the path separators in a path.</summary>
    /// <param name="path">The path to convert.</param>
    /// <param name="type"><see cref="UNIX_PATH_SEPCHAR"/> or <see cref="WIN_PATH_SEPCHAR"/>.</param>
    /// <returns>A new string with the converted separators.</returns>
    /// <exception cref="ArgumentException"><paramref name="type"/> is invalid.</exception>
    /// <remarks>Conversion to Unix separators only happens on Windows.</remarks>
    public static string ConvertSepCharsInPath(string path, int type)
    {
        if (type != UNIX_PATH_SEPCHAR && type != WIN_PATH_SEPCHAR)
            throw Lept.Arg("convertSepCharsInPath", "invalid type");

        if (type == UNIX_PATH_SEPCHAR) {
            if (OperatingSystem.IsWindows())  /* only convert on Windows */
                return path.Replace('\\', '/');
            return path;
        } else {  /* WIN_PATH_SEPCHAR */
            return path.Replace('/', '\\');
        }
    }

    // leptonica: genPathname (utils2.c)
    /// <summary>Generates an actual path from a directory and/or file name.</summary>
    /// <param name="dir">optional; directory or full path name, with or without the trailing '/'</param>
    /// <param name="fname">optional; file name within a directory</param>
    /// <remarks>
    /// (1) This function generates actual paths in the following ways:
    ///       * from two sub-parts (e.g., a directory and a file name).
    ///       * from a single path full path, placed in dir, with
    ///         fname == null.
    ///       * from the name of a file in the local directory placed in
    ///         fname, with dir == null.
    ///       * if in a "/tmp" directory and on iOS, macOS or Windows,
    ///         the OS specific temp directory is used.
    /// (3) On unix, the TMPDIR variable is ignored.  No rewriting
    ///     of temp directories is permitted.
    /// (5) In all cases, the resulting pathname is not terminated with a slash.
    /// </remarks>
    public static string GenPathname(string? dir, string? fname)
    {
        if (dir == null && fname == null)
            throw Lept.Arg("genPathname", "no input");

            /* Handle the case where we start from the current directory */
        string cdir;
        if (string.IsNullOrEmpty(dir))
            cdir = Directory.GetCurrentDirectory();
        else
            cdir = dir;

            /* Convert to unix path separators, and remove the trailing
             * slash in the directory, except when dir == "/"  */
        cdir = ConvertSepCharsInPath(cdir, UNIX_PATH_SEPCHAR);
        int dirlen = cdir.Length;
        if (cdir[dirlen - 1] == '/' && dirlen != 1) {
            cdir = cdir.Substring(0, dirlen - 1);
            dirlen--;
        }

            /* First handle dir (which may be a full pathname).
             * There is no path rewriting on unix, and on win32, we do not
             * rewrite unless the specified directory is /tmp or
             * a subdirectory of /tmp */
        string pathout;
        if (!rewriteTmp || dirlen < 4 ||
            (dirlen == 4 && !cdir.StartsWith("/tmp", StringComparison.Ordinal)) ||  /* not in "/tmp" */
            (dirlen > 4 && !cdir.StartsWith("/tmp/", StringComparison.Ordinal))) {  /* not in "/tmp/" */
            pathout = cdir;
        } else {  /* Rewrite with "/tmp" specified for the directory. */
            string tmpdir = Path.GetTempPath();
            if (tmpdir.Length > 0 && (tmpdir[^1] == '\\' || tmpdir[^1] == '/'))
                tmpdir = tmpdir.Substring(0, tmpdir.Length - 1);  /* trim the trailing separator */
            pathout = tmpdir;
                /* Add the rest of cdir */
            if (dirlen > 4)
                pathout += cdir.Substring(4);
        }

            /* Now handle fname */
        if (!string.IsNullOrEmpty(fname))
            pathout = pathout + "/" + fname;
        return pathout;
    }

    // leptonica: pathJoin (utils2.c)
    /// <summary>Joins a directory and a file name, with single slashes and no trailing slash.</summary>
    /// <param name="dir">[optional] can be null</param>
    /// <param name="fname">[optional] can be null</param>
    /// <returns>specially concatenated path</returns>
    /// <remarks>
    /// (1) Use unix-style pathname separators ('/').
    /// (2) %fname can be the entire path, or part of the path containing
    ///     at least one directory, or a tail without a directory, or NULL.
    /// (3) It produces a path that strips multiple slashes to a single
    ///     slash, joins %dir and %fname by a slash, and has no trailing
    ///     slashes (except in the cases where %dir == "/" and
    ///     %fname == NULL, or v.v.).
    /// (4) If both %dir and %fname are null, produces an empty string.
    /// (5) Neither %dir nor %fname can begin with '..'.
    /// (6) The result is not canonicalized or tested for correctness:
    ///     garbage in (e.g., /&amp;%), garbage out.
    /// (7) Examples:
    ///        //tmp// + //abc/  --&gt;  /tmp/abc
    ///        tmp/ + /abc/      --&gt;  tmp/abc
    ///        tmp/ + abc/       --&gt;  tmp/abc
    ///        /tmp/ + ///       --&gt;  /tmp
    ///        /tmp/ + NULL      --&gt;  /tmp
    ///        // + /abc//       --&gt;  /abc
    ///        // + NULL         --&gt;  /
    ///        NULL + /abc/def/  --&gt;  /abc/def
    ///        NULL + abc//      --&gt;  abc
    ///        NULL + //         --&gt;  /
    ///        NULL + NULL       --&gt;  (empty string)
    ///        "" + ""           --&gt;  (empty string)
    ///        "" + /            --&gt;  /
    ///        ".." + /etc/foo   --&gt;  error (the C function returns NULL)
    ///        /tmp + ".."       --&gt;  error (the C function returns NULL)
    /// </remarks>
    public static string PathJoin(string? dir, string? fname)
    {
        const string func = "pathJoin";
        const string slash = "/";

        if (dir == null && fname == null)
            return "";
        if (dir != null && dir.Length >= 2 && dir[0] == '.' && dir[1] == '.')
            throw Lept.Arg(func, "dir starts with '..'");
        if (fname != null && fname.Length >= 2 && fname[0] == '.' && fname[1] == '.')
            throw Lept.Arg(func, "fname starts with '..'");

        System.Text.StringBuilder ba = new System.Text.StringBuilder();

            /* Process %dir */
        if (dir != null && dir.Length > 0) {
            if (dir[0] == '/')
                ba.Append(slash);
            Sarray sa1 = Sarray.Create(0);
            sa1.SplitString(dir, "/");  /* removes all slashes */
            int n1 = sa1.Count;
            for (int i = 0; i < n1; i++) {
                string str = sa1.GetString(i, L.L_NOCOPY);
                ba.Append(str);
                ba.Append(slash);
            }
        }

            /* Special case to add leading slash: dir NULL or empty string  */
        bool emptydir = dir != null && dir.Length == 0;
        if ((dir == null || emptydir) && fname != null && fname.Length > 0 && fname[0] == '/')
            ba.Append(slash);

            /* Process %fname */
        if (fname != null && fname.Length > 0) {
            Sarray sa2 = Sarray.Create(0);
            sa2.SplitString(fname, "/");
            int n2 = sa2.Count;
            for (int i = 0; i < n2; i++) {
                string str = sa2.GetString(i, L.L_NOCOPY);
                ba.Append(str);
                ba.Append(slash);
            }
        }

            /* Remove trailing slash */
        string dest = ba.ToString();
        if (dest.Length > 1 && dest[dest.Length - 1] == '/')
            dest = dest.Substring(0, dest.Length - 1);
        return dest;
    }

    // leptonica: appendSubdirs (utils2.c)
    /// <summary>Appends subdirectories to a base directory.</summary>
    /// <param name="basedir"></param>
    /// <param name="subdirs"></param>
    /// <returns>concatenated full directory path without trailing slash</returns>
    /// <remarks>
    /// (1) Use unix pathname separators
    /// (2) Allocates a new string:  [basedir]/[subdirs]
    /// </remarks>
    public static string AppendSubdirs(string basedir, string subdirs)
    {
        int len1 = basedir.Length;
        System.Text.StringBuilder newdir = new System.Text.StringBuilder(basedir);
        if (len1 == 0 || basedir[len1 - 1] != '/')  /* add '/' if necessary */
            newdir.Append('/');
        if (subdirs.Length > 0 && subdirs[0] == '/')  /* add subdirs, stripping leading '/' */
            newdir.Append(subdirs, 1, subdirs.Length - 1);
        else
            newdir.Append(subdirs);
        int len4 = newdir.Length;
        if (newdir[len4 - 1] == '/')  /* strip trailing '/' */
            newdir.Length = len4 - 1;

        return newdir.ToString();
    }

    // leptonica: makeTempDirname (utils2.c)
    /// <summary>Makes the pathname of a subdirectory of the temp directory.</summary>
    /// <param name="subdir">[optional] of the temp directory; can be null or an empty string</param>
    /// <returns>the path, with unix separators</returns>
    /// <remarks>
    /// (1) This generates the directory path for output temp files,
    ///     with unix separators.
    /// (2) The path is:
    ///       /tmp/%subdir       (unix)
    ///       [Temp]/%subdir     (Windows, macOS, iOS)
    ///     where [Temp] is the OS path
    ///     and %subdir is in general a set of nested subdirectories:
    ///       dir1/dir2/.../dirN
    ///     which in use would not typically exceed 2 levels.
    /// (3) Usage example:
    ///       string result = MakeTempDirname("lept/golden");
    /// </remarks>
    public static string MakeTempDirname(string? subdir)
    {
        string dir, path;

        if (subdir != null && subdir.Length > 0 && ((subdir[0] == '.') || (subdir[0] == '/')))
            throw Lept.Arg("makeTempDirname", "subdir not an actual subdirectory");

        dir = PathJoin("/tmp", subdir);

        if (rewriteTmp)
            path = GenPathname(dir, null);
        else
            path = dir;
        return path;
    }

    // leptonica: modifyTrailingSlash (utils2.c)
    /// <summary>Adds or removes the trailing slash of a path.</summary>
    /// <param name="path"></param>
    /// <param name="flag">L_ADD_TRAIL_SLASH, L_REMOVE_TRAIL_SLASH</param>
    /// <returns>the modified path</returns>
    /// <remarks>
    /// (1) This carries out the requested action if necessary.
    /// (2) In C the path is modified in place, in a buffer of fixed size.
    ///     The empty path is returned unchanged.
    /// </remarks>
    public static string ModifyTrailingSlash(string path, int flag)
    {
        if (flag != L.L_ADD_TRAIL_SLASH && flag != L.L_REMOVE_TRAIL_SLASH)
            throw Lept.Arg("modifyTrailingSlash", "invalid flag");

        int len = path.Length;
        if (len == 0)
            return path;
        char lastchar = path[len - 1];
        if (flag == L.L_ADD_TRAIL_SLASH && lastchar != '/')
            return path + "/";
        else if (flag == L.L_REMOVE_TRAIL_SLASH && lastchar == '/')
            return path.Substring(0, len - 1);
        return path;
    }

    // leptonica: l_makeTempFilename (utils2.c)
    /// <summary>Makes a unique file in the temp directory.</summary>
    /// <returns>fname : the name of the (empty) file that was created</returns>
    /// <remarks>
    /// (1) On unix, this makes a filename of the form
    ///          "/tmp/lept.XXXXXX",
    ///     where each X is a random character.
    /// (2) On Windows, this makes a filename of the form
    ///          "/[Temp]/lp.XXXXXX".
    /// (3) On all systems, this fails if the file is not writable.
    /// (4) Safest usage is to write to a subdirectory in debug code.
    /// (6) The tail of the filename has a '.', so that cygwin interprets
    ///     the file as having an extension.  Otherwise, cygwin assumes it
    ///     is an executable and appends ".exe" to the filename.
    /// </remarks>
    public static string LMakeTempFilename()
    {
        const string func = "l_makeTempFilename";
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        string dirname = MakeTempDirname(null);
        string prefix = OperatingSystem.IsWindows() ? "lp." : "lept.";
        for (int attempt = 0; attempt < 100; attempt++) {
            char[] suffix = new char[6];
            for (int i = 0; i < suffix.Length; i++)
                suffix[i] = chars[System.Security.Cryptography.RandomNumberGenerator.GetInt32(chars.Length)];
            string pattern = StringConcatNew(dirname, "/", prefix, new string(suffix))!;
            try {
                using FileStream fs = new FileStream(pattern, FileMode.CreateNew, FileAccess.Write);
                return pattern;
            } catch (IOException) {
                if (File.Exists(pattern))
                    continue;   /* name in use: try another one */
                throw Lept.Fail(func, "failed to make a temp file");
            } catch (UnauthorizedAccessException) {
                throw Lept.Fail(func, "file cannot be written to");
            }
        }
        throw Lept.Fail(func, "failed to make a temp file");
    }

    // leptonica: extractNumberFromFilename (utils2.c)
    /// <summary>Finds the number embedded in the base name of a file.</summary>
    /// <param name="fname"></param>
    /// <param name="numpre">number of characters before the digits to be found</param>
    /// <param name="numpost">number of characters after the digits to be found</param>
    /// <returns>num number embedded in the filename; -1 on error or if not found</returns>
    /// <remarks>
    /// (1) The number is to be found in the basename, which is the
    ///     filename without either the directory or the last extension.
    /// (2) When a number is found, it is non-negative.  If no number
    ///     is found, this returns -1, without an error message.  The
    ///     caller needs to check.
    /// (3) The C function returns -1 both for "no number found" and for an error;
    ///     callers (convertSortedToNumberedPathnames) rely on that and skip the file.
    /// </remarks>
    public static int ExtractNumberFromFilename(string fname, int numpre, int numpost)
    {
        SplitPathAtDirectory(fname, out _, out string tail);
        SplitPathAtExtension(tail, out string basename, out _);

        int len = basename.Length;
        if (numpre + numpost > len - 1) {
            Log.Error("extractNumberFromFilename", "numpre + numpost too big");
            return -1;
        }

        basename = basename.Substring(0, len - numpost);
        return ScanInt(basename.Substring(numpre), out int num) ? num : -1;
    }

    /// <summary>sscanf(str, "%d", &amp;num) == 1.</summary>
    private static bool ScanInt(string str, out int num)
    {
        num = 0;
        int i = 0, n = str.Length;
        while (i < n && (str[i] == ' ' || (str[i] >= '\t' && str[i] <= '\r')))
            i++;
        bool neg = false;
        if (i < n && (str[i] == '+' || str[i] == '-')) {
            neg = str[i] == '-';
            i++;
        }
        if (i >= n || str[i] < '0' || str[i] > '9')
            return false;
        long val = 0;
        while (i < n && str[i] >= '0' && str[i] <= '9') {
            val = val * 10 + (str[i] - '0');
            if (val > 0x7fffffffL + 1)
                val = 0x7fffffffL + 1;   /* saturate; overflow is undefined in C */
            i++;
        }
        if (neg)
            val = -val;
        num = (int)Math.Clamp(val, int.MinValue, int.MaxValue);
        return true;
    }

    /*--------------------------------------------------------------------*
     *                Multi-platform file system operations               *
     *         [ These only write to /tmp or its subdirectories ]         *
     *--------------------------------------------------------------------*/
    // leptonica: lept_rmdir (utils2.c)
    /// <summary>Removes a directory in the temp directory, with the files in it.</summary>
    /// <param name="subdir">of /tmp or its OS specific equivalent</param>
    /// <returns>0 on success, non-zero on failure</returns>
    /// <remarks>
    /// (1) %subdir is a partial path that can consist of one or more
    ///     directories.
    /// (2) This removes all files from the specified subdirectory of
    ///     the root temp directory:
    ///       /tmp    (unix)
    ///       [Temp]  (Windows)
    ///     and then removes the subdirectory.
    /// (3) The combination
    ///       lept_rmdir(subdir);
    ///       lept_mkdir(subdir);
    ///     is guaranteed to give you an empty subdirectory.
    /// </remarks>
    public static int LeptRmdir(string subdir)
    {
        const string func = "lept_rmdir";
        string dir;
        Sarray sa;

        if ((subdir.Length == 0) || (subdir[0] == '.') || (subdir[0] == '/'))
            throw Lept.Arg(func, "subdir not an actual subdirectory");

            /* Find the temp subdirectory */
        dir = PathJoin("/tmp", subdir);
        if (!LeptDirexists(dir))  /* fail silently */
            return 0;

            /* List all the files in that directory */
        try {
            sa = Sarray.GetFilenamesInDirectory(dir);
        } catch (Exception e) when (e is ArgumentException || e is LeptonicaException) {
            Log.Error(func, $"directory {dir} does not exist!");
            return 1;
        }
        int nfiles = sa.Count;

        for (int i = 0; i < nfiles; i++) {
            string fname = sa.GetString(i, L.L_NOCOPY);
            string fullname = GenPathname(dir, fname);
            try { File.Delete(fullname); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        string realdir = GenPathname("/tmp", subdir);
        try {
            Directory.Delete(realdir);
            return 0;
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) {
            return 1;
        }
    }

    // leptonica: lept_direxists (utils2.c)
    /// <summary>Tests whether a directory exists.</summary>
    /// <param name="dir"></param>
    /// <returns>true if it exists</returns>
    /// <remarks>
    /// (1) Always use unix pathname separators.
    /// (2) By calling genPathname(), if the pathname begins with "/tmp"
    ///     this does an automatic directory translation for operating
    ///     systems that use a different path for /tmp.
    /// </remarks>
    public static bool LeptDirexists(string? dir)
    {
        if (dir == null)
            return false;
        string realdir = GenPathname(dir, null);
        return Directory.Exists(realdir);
    }

    // leptonica: lept_rm_match (utils2.c)
    /// <summary>Removes the files in /tmp or a subdirectory whose names contain a substring.</summary>
    /// <param name="subdir">[optional] if null, the removed files are in /tmp</param>
    /// <param name="substr">[optional] pattern to match in filename</param>
    /// <returns>0 on success, non-zero on failure</returns>
    /// <remarks>
    /// (1) This removes the matched files in /tmp or a subdirectory of /tmp.
    ///     Use NULL for %subdir if the files are in /tmp.
    /// (2) If %substr == NULL, this removes all files in the directory.
    ///     If %substr == "" (empty), this removes no files.
    ///     If both %subdir == NULL and %substr == NULL, this removes
    ///     all files in /tmp.
    /// (3) Use unix pathname separators.
    /// (4) By calling genPathname(), if the pathname begins with "/tmp"
    ///     this does an automatic directory translation for operating
    ///     systems that use a different path for /tmp.
    /// (5) Error conditions:
    ///       * returns -1 if the directory is not found
    ///       * returns the number of files (> 0) that it was unable to remove.
    /// </remarks>
    public static int LeptRmMatch(string? subdir, string? substr)
    {
        const string func = "lept_rm_match";
        int ret;
        Sarray sa;

        string tempdir = MakeTempDirname(subdir);
        try {
            sa = Sarray.GetSortedPathnamesInDirectory(tempdir, substr, 0, 0);
        } catch (Exception e) when (e is ArgumentException || e is LeptonicaException) {
            Log.Error(func, "sa not made");
            return -1;
        }
        int n = sa.Count;
        if (n == 0) {
            Log.Warning(func, "no matching files found");
            return 0;
        }

        ret = 0;
        for (int i = 0; i < n; i++) {
            string fname = sa.GetString(i, L.L_NOCOPY);
            string path = GenPathname(fname, null);
            if (LeptRmfile(path) != 0) {
                Log.Error(func, $"failed to remove {path}");
                ret++;
            }
        }
        return ret;
    }

    // leptonica: lept_rm (utils2.c)
    /// <summary>Removes a file in /tmp or a subdirectory of it.</summary>
    /// <param name="subdir">[optional] subdir of '/tmp'; can be null</param>
    /// <param name="tail">filename without the directory</param>
    /// <returns>0 on success, non-zero on failure</returns>
    /// <remarks>
    /// (1) By calling genPathname(), this does an automatic directory
    ///     translation on operating systems which use a different path.
    /// </remarks>
    public static int LeptRm(string? subdir, string tail)
    {
        if (tail.Length == 0)
            throw Lept.Arg("lept_rm", "tail undefined or empty");

        string newtemp = MakeTempDirname(subdir);
        string path = GenPathname(newtemp, tail);
        return LeptRmfile(path);
    }

    // leptonica: lept_rmfile (utils2.c)
    /// <summary>Removes a file.</summary>
    /// <param name="filepath">full path to file including the directory</param>
    /// <returns>0 on success, non-zero on failure</returns>
    /// <remarks>
    /// (1) This removes the named file.
    /// (2) Use unix pathname separators.
    /// (3) There is no name translation.
    /// (4) Unlike the other lept_* functions in this section, this can remove
    ///     any file -- it is not restricted to files that are in /tmp or a
    ///     subdirectory of it.
    /// (5) For files in /tmp or a subdirectory of it, this does an automatic
    ///     directory translation for operating systems that use a different
    ///     path for /tmp.
    /// </remarks>
    public static int LeptRmfile(string filepath)
    {
        if (filepath.Length == 0)
            throw Lept.Arg("lept_rmfile", "filepath undefined or empty");

        try {
            if (!File.Exists(filepath))
                return 1;
                /* Set attributes to allow deletion of read-only files */
            File.SetAttributes(filepath, FileAttributes.Normal);
            File.Delete(filepath);
            return 0;
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) {
            return 1;
        }
    }

    // leptonica: lept_mv (utils2.c)
    /// <summary>Moves a file to /tmp or to a subdirectory of /tmp.</summary>
    /// <param name="srcfile"></param>
    /// <param name="newdir">[optional]; can be null</param>
    /// <param name="newtail">[optional]; can be null</param>
    /// <param name="pnewpath">of actual path</param>
    /// <returns>0 on success, non-zero on failure</returns>
    /// <remarks>
    /// (1) This moves %srcfile to /tmp or to a subdirectory of /tmp.
    /// (2) %srcfile can either be a full path or relative to the
    ///     current directory.
    /// (3) %newdir can either specify an existing subdirectory of /tmp
    ///     or can be NULL.  In the latter case, the file will be written
    ///     into /tmp.
    /// (4) %newtail can either specify a filename tail or, if NULL,
    ///     the filename is taken from src-tail, the tail of %srcfile.
    /// (5) For debugging, the computed newpath can be returned.
    /// (6) Reminders:
    ///     (a) specify files using unix pathnames
    ///     (b) this does an automatic directory translation on operating
    ///         systems that use a different path for /tmp.
    /// (7) Examples:
    ///     * newdir = NULL,    newtail = NULL    ==&gt; /tmp/src-tail
    ///     * newdir = NULL,    newtail = abc     ==&gt; /tmp/abc
    ///     * newdir = def/ghi, newtail = NULL    ==&gt; /tmp/def/ghi/src-tail
    ///     * newdir = def/ghi, newtail = abc     ==&gt; /tmp/def/ghi/abc
    /// </remarks>
    public static int LeptMv(string srcfile, string? newdir, string? newtail, out string pnewpath)
    {
        pnewpath = "";

            /* Require output pathname to be in /tmp/ or a subdirectory */
        string newtemp = MakeTempDirname(newdir);

            /* Get canonical src pathname */
        SplitPathAtDirectory(srcfile, out string dir, out string srctail);
        string srcpath = PathJoin(dir, srctail);

            /* Generate output pathname */
        string newpath;
        if (newtail == null || newtail.Length == 0)
            newpath = PathJoin(newtemp, srctail);
        else
            newpath = PathJoin(newtemp, newtail);
        pnewpath = newpath;

            /* Overwrite any existing file at 'newpath' */
        int ret;
        try {
            FileCopy(srcpath, newpath);
            ret = 0;
        } catch (Exception e) when (e is ArgumentException || e is LeptonicaException) {
            ret = 1;
        }
        if (ret == 0) {  /* and remove srcfile */
            string realpath = GenPathname(srcpath, null);
            try { File.Delete(realpath); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }
        return ret;
    }

    // leptonica: lept_cp (utils2.c)
    /// <summary>Copies a file to /tmp or to a subdirectory of /tmp.</summary>
    /// <param name="srcfile"></param>
    /// <param name="newdir">[optional]; can be null</param>
    /// <param name="newtail">[optional]; can be null</param>
    /// <param name="pnewpath">of actual path</param>
    /// <returns>0 on success, non-zero on failure</returns>
    /// <remarks>
    /// (1) This copies %srcfile to /tmp or to a subdirectory of /tmp.
    /// (2) %srcfile can either be a full path or relative to the
    ///     current directory.
    /// (3) %newdir can either specify an existing subdirectory of /tmp,
    ///     or can be NULL.  In the latter case, the file will be written
    ///     into /tmp.
    /// (4) %newtail can either specify a filename tail or, if NULL,
    ///     the filename is taken from src-tail, the tail of %srcfile.
    /// (5) For debugging, the computed newpath can be returned.
    /// (6) Reminders:
    ///     (a) specify files using unix pathnames
    ///     (b) this does an automatic directory translation for operating
    ///         systems that use a different path for /tmp
    /// (7) Examples:
    ///     * newdir = NULL,    newtail = NULL    ==&gt; /tmp/src-tail
    ///     * newdir = NULL,    newtail = abc     ==&gt; /tmp/abc
    ///     * newdir = def/ghi, newtail = NULL    ==&gt; /tmp/def/ghi/src-tail
    ///     * newdir = def/ghi, newtail = abc     ==&gt; /tmp/def/ghi/abc
    /// </remarks>
    public static int LeptCp(string srcfile, string? newdir, string? newtail, out string pnewpath)
    {
        pnewpath = "";

            /* Require output pathname to be in /tmp or a subdirectory */
        string newtemp = MakeTempDirname(newdir);

           /* Get canonical src pathname */
        SplitPathAtDirectory(srcfile, out string dir, out string srctail);
        string srcpath = PathJoin(dir, srctail);

            /* Generate output pathname */
        string newpath;
        if (newtail == null || newtail.Length == 0)
            newpath = PathJoin(newtemp, srctail);
        else
            newpath = PathJoin(newtemp, newtail);
        pnewpath = newpath;

            /* Overwrite any existing file at 'newpath' */
        try {
            FileCopy(srcpath, newpath);
            return 0;
        } catch (Exception e) when (e is ArgumentException || e is LeptonicaException) {
            return 1;
        }
    }

    /*--------------------------------------------------------------------*
     *          Special debug/test function for calling 'system'          *
     *--------------------------------------------------------------------*/
    /* callSystemDebug() is not ported: it runs a shell command, which has no
     * place in a managed library. */

    /*--------------------------------------------------------------------*
     *       Multi-platform functions that avoid C-runtime boundary       *
     *             crossing for applications with Windows DLLs            *
     *--------------------------------------------------------------------*/
    // leptonica: lept_fopen (utils2.c)
    /// <summary>Opens a file stream; a mode containing 'r' opens for reading.</summary>
    /// <param name="filename"></param>
    /// <param name="mode">same as for fopen(); e.g., "rb"</param>
    /// <returns>stream</returns>
    public static FileStream LeptFopen(string filename, string mode)
    {
        if (StringFindSubstr(mode, "r", out _))
            return FopenReadStream(filename);
        else
            return FopenWriteStream(filename, mode);
    }

    // leptonica: lept_fclose (utils2.c)
    /// <summary>Closes a file stream.</summary>
    /// <param name="fp">file stream</param>
    public static void LeptFclose(Stream fp)
    {
        fp.Dispose();
    }

    /* lept_calloc() and lept_free() are not ported: memory is managed by the GC
     *. */
}
