using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Leptonica;

/*
 *  sarray1.c: Array of strings.
 *
 *  Differences from C:
 *    - No reference counting and no sarrayDestroy(): the GC owns Sarray.
 *      sarrayClone() returns the same reference.
 *    - C strings (char *) are .NET strings. They are immutable, so the
 *      copyflag arguments (L_INSERT, L_NOCOPY, L_COPY) are validated as in C
 *      but make no observable difference.
 *    - C code measures strings in bytes (strlen). Where that matters (the
 *      stored length in the serialization, offsets returned by the substring
 *      search, line lengths in sarrayConvertWordsToLines) the length of the
 *      UTF-8 encoding is used, which is identical for ASCII text. A string
 *      is taken to end at its first '\0' character, as a C string would.
 *    - Serialization writes and reads UTF-8 bytes, with '\n' line ends on
 *      every platform (the C library writes in text mode, which gives
 *      "\r\n" on Windows for files opened with fopen(..., "w")).
 *    - sarrayReadStream() on a stream that cannot seek loses the one byte it
 *      has to look at after the trailing blank line of the serialization.
 *    - sarrayGetCount() is the property Count.
 *    - getFilenamesInDirectory() uses System.IO for both the unix and the
 *      Windows version of the C function.
 */

public static partial class L
{
    /* array_internal.h */
    /// <summary>Version number of the serialized string array format.</summary>
    public const int SARRAY_VERSION_NUMBER = 1;

    /* Sort order (pix.h) */
    /// <summary>Sort in increasing order.</summary>
    public const int L_SORT_INCREASING = 1;
    /// <summary>Sort in decreasing order.</summary>
    public const int L_SORT_DECREASING = 2;
}

/// <summary>
/// Array of strings (struct Sarray).
/// </summary>
/// <remarks>
/// These functions are important for efficient manipulation
/// of string data, and they have found widespread use in
/// leptonica.  For example:
/// (1) to generate text files: e.g., PostScript and PDF
///     wrappers around sets of images
/// (2) to parse text files: e.g., extracting prototypes
///     from the source to generate allheaders.h
/// (3) to generate code for compilation: e.g., the fast
///     dwa code for arbitrary structuring elements.
/// </remarks>
public sealed partial class Sarray
{
    private const int MaxPtrArraySize = 50000000;     /* 50 million */
    private const int InitialPtrArraySize = 50;       /* n'importe quoi */

    private string?[] array;   /* string array                            */
    private int nalloc;        /* size of allocated ptr array             */
    private int n;             /* number of strings allocated             */

    private Sarray(string?[] array, int nalloc)
    {
        this.array = array;
        this.nalloc = nalloc;
        this.n = 0;
    }

    /*--------------------------------------------------------------------------*
     *                   String array create/destroy/copy/extend                *
     *--------------------------------------------------------------------------*/
    // leptonica: sarrayCreate (sarray1.c)
    /// <summary>Creates an empty string array.</summary>
    /// <param name="n">size of string ptr array to be alloc'd; use 0 for default</param>
    public static Sarray Create(int n)
    {
        if (n <= 0 || n > MaxPtrArraySize)
            n = InitialPtrArraySize;

        return new Sarray(new string?[n], n);
    }

    // leptonica: sarrayCreateInitialized (sarray1.c)
    /// <summary>Creates a string array with n copies of initstr.</summary>
    /// <param name="n">size of string ptr array to be alloc'd</param>
    /// <param name="initstr">string to be initialized on the full array</param>
    public static Sarray CreateInitialized(int n, string initstr)
    {
        if (n <= 0)
            throw Lept.Arg("sarrayCreateInitialized", "n must be > 0");

        Sarray sa = Create(n);
        for (int i = 0; i < n; i++)
            sa.AddString(initstr, L.L_COPY);
        return sa;
    }

    // leptonica: sarrayCreateWordsFromString (sarray1.c)
    /// <summary>Creates a string array of the words in a string.</summary>
    /// <remarks>
    /// (1) This finds the number of word substrings, creates an sarray
    ///     of this size, and puts copies of each substring into the sarray.
    /// </remarks>
    public static Sarray CreateWordsFromString(string str)
    {
        const string separators = " \n\t";

        str = CStr(str);

            /* Find the number of words */
        int size = str.Length;
        int nsub = 0;
        bool inword = false;
        for (int i = 0; i < size; i++) {
            if (!inword &&
               (str[i] != ' ' && str[i] != '\t' && str[i] != '\n')) {
               inword = true;
               nsub++;
            } else if (inword &&
               (str[i] == ' ' || str[i] == '\t' || str[i] == '\n')) {
               inword = false;
            }
        }

        Sarray sa = Create(nsub);
        sa.SplitString(str, separators);

        return sa;
    }

    // leptonica: sarrayCreateLinesFromString (sarray1.c)
    /// <summary>Creates a string array of the lines in a string.</summary>
    /// <param name="str">the text</param>
    /// <param name="blankflag">0 to exclude blank lines; 1 to include</param>
    /// <remarks>
    /// (1) This finds the number of line substrings, each of which
    ///     ends with a newline, and puts a copy of each substring
    ///     in a new sarray.
    /// (2) The newline characters are removed from each substring.
    /// </remarks>
    public static Sarray CreateLinesFromString(string str, int blankflag)
    {
        str = CStr(str);

            /* Find the number of lines */
        int size = str.Length;
        int nsub = 0;
        for (int i = 0; i < size; i++) {
            if (str[i] == '\n')
                nsub++;
        }

        Sarray sa = Create(nsub);

        if (blankflag != 0) {  /* keep blank lines as null strings */
                /* Make a copy for munging */
            char[] cstring = str.ToCharArray();
                /* We'll insert nulls like strtok */
            int startptr = 0;
            for (int i = 0; i < size; i++) {
                if (cstring[i] == '\n') {
                    cstring[i] = '\0';
                    if (i > 0 && cstring[i - 1] == '\r')
                        cstring[i - 1] = '\0';  /* also remove Windows CR */
                    sa.AddString(CStr(cstring, startptr), L.L_INSERT);
                    startptr = i + 1;
                }
            }
            if (startptr < size) {  /* no newline at end of last line */
                sa.AddString(CStr(cstring, startptr), L.L_INSERT);
            }
        } else {  /* remove blank lines; use strtok */
            sa.SplitString(str, "\r\n");
        }

        return sa;
    }

    // leptonica: sarrayCopy (sarray1.c)
    /// <summary>Deep copy: the new sarray has the same allocated size and strings.</summary>
    public Sarray Copy()
    {
        Sarray csa = Create(nalloc);

        for (int i = 0; i < n; i++)
            csa.AddString(array[i]!, L.L_COPY);

        return csa;
    }

    // leptonica: sarrayClone (sarray1.c)
    /// <summary>Returns the same sarray (changing one changes the other).</summary>
    public Sarray Clone()
    {
        return this;
    }

    /*--------------------------------------------------------------------------*
     *                         Add/Remove string                                *
     *--------------------------------------------------------------------------*/
    // leptonica: sarrayAddString (sarray1.c)
    /// <summary>Adds a string at the end of the array.</summary>
    /// <param name="str">string to be added</param>
    /// <param name="copyflag">L_INSERT, L_NOCOPY or L_COPY</param>
    /// <remarks>
    /// (1) See usage comments at the top of this file.  L_INSERT is
    ///     equivalent to L_NOCOPY.
    /// </remarks>
    public void AddString(string str, int copyflag)
    {
        if (copyflag != L.L_INSERT && copyflag != L.L_NOCOPY && copyflag != L.L_COPY)
            throw Lept.Arg("sarrayAddString", "invalid copyflag");

        if (n >= nalloc)
            ExtendArray();

        array[n] = str;
        n++;
    }

    // leptonica: sarrayExtendArray (sarray1.c)
    /// <summary>Doubles the size of the string ptr array.</summary>
    /// <remarks>
    /// (1) Doubles the size of the string ptr array.
    /// (2) The max number of strings is 50M.
    /// </remarks>
    private void ExtendArray()
    {
        if (nalloc >= MaxPtrArraySize)  /* belt & suspenders */
            throw Lept.Fail("sarrayExtendArray", "sa at maximum ptr size; can't extend");
        int newsize;
        if (nalloc > MaxPtrArraySize / 2) {
            newsize = MaxPtrArraySize;
            nalloc = MaxPtrArraySize;
        } else {
            newsize = 2 * nalloc;
            nalloc *= 2;
        }
        Array.Resize(ref array, newsize);
    }

    // leptonica: sarrayRemoveString (sarray1.c)
    /// <summary>Removes the string at the given index and returns it.</summary>
    /// <param name="index">of string within sarray</param>
    public string RemoveString(int index)
    {
        string?[] array = GetArray(out _, out int n);

        if (index < 0 || index >= n)
            throw Lept.Arg("sarrayRemoveString", "array index out of bounds");

        string str = array[index]!;

            /* If removed string is not at end of array, shift
             * to fill in, maintaining original ordering.
             * Note: if we didn't care about the order, we could
             * put the last string array[n - 1] directly into the hole.  */
        for (int i = index; i < n - 1; i++)
            array[i] = array[i + 1];

        this.n--;
        return str;
    }

    // leptonica: sarrayReplaceString (sarray1.c)
    /// <summary>Replaces the string at the given index.</summary>
    /// <param name="index">of string within sarray to be replaced</param>
    /// <param name="newstr">string to replace existing one</param>
    /// <param name="copyflag">L_INSERT, L_COPY</param>
    /// <remarks>
    /// (1) This destroys an existing string and replaces it with
    ///     the new string or a copy of it.
    /// (2) By design, an sarray is always compacted, so there are
    ///     never any holes (null ptrs) in the ptr array up to the
    ///     current count.
    /// </remarks>
    public void ReplaceString(int index, string newstr, int copyflag)
    {
        int n = Count;
        if (index < 0 || index >= n)
            throw Lept.Arg("sarrayReplaceString", "array index out of bounds");
        if (copyflag != L.L_INSERT && copyflag != L.L_COPY)
            throw Lept.Arg("sarrayReplaceString", "invalid copyflag");

        array[index] = newstr;
    }

    // leptonica: sarrayClear (sarray1.c)
    /// <summary>Removes all strings; the allocated size is kept.</summary>
    public void Clear()
    {
        for (int i = 0; i < n; i++)   /* free strings and null ptrs */
            array[i] = null;
        n = 0;
    }

    /*----------------------------------------------------------------------*
     *                               Accessors                              *
     *----------------------------------------------------------------------*/
    // leptonica: sarrayGetCount (sarray1.c)
    /// <summary>Number of strings in the array.</summary>
    public int Count => n;

    // leptonica: sarrayGetArray (sarray1.c)
    /// <summary>Returns the string array itself (not a copy).</summary>
    /// <param name="nalloc">number allocated string ptrs</param>
    /// <param name="n">number allocated strings</param>
    /// <remarks>
    /// (1) Caution: the returned array is not a copy, so changes
    ///     made to it change the sarray.  Only the first n entries are valid.
    /// </remarks>
    public string?[] GetArray(out int nalloc, out int n)
    {
        nalloc = this.nalloc;
        n = this.n;
        return array;
    }

    // leptonica: sarrayGetString (sarray1.c)
    /// <summary>Returns the index-th string.</summary>
    /// <param name="index">to the index-th string</param>
    /// <param name="copyflag">L_NOCOPY or L_COPY</param>
    /// <remarks>
    /// (1) See usage comments at the top of this file.
    /// (2) To get a pointer to the string itself, use L_NOCOPY.
    ///     To get a copy of the string, use L_COPY.
    /// </remarks>
    public string GetString(int index, int copyflag)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("sarrayGetString", "index not valid");
        if (copyflag != L.L_NOCOPY && copyflag != L.L_COPY)
            throw Lept.Arg("sarrayGetString", "invalid copyflag");

        return array[index]!;
    }

    /*----------------------------------------------------------------------*
     *                      Conversion to string                           *
     *----------------------------------------------------------------------*/
    // leptonica: sarrayToString (sarray1.c)
    /// <summary>Concatenates all the strings in the array.</summary>
    /// <param name="addnlflag">flag: 0 adds nothing to each substring;
    /// 1 adds '\n' to each substring; 2 adds ' ' to each substring;
    /// 3 adds ',' to each substring</param>
    /// <remarks>
    /// (1) Concatenates all the strings in the sarray, preserving
    ///     all white space.
    /// (2) If addnlflag != 0, adds '\n', ' ' or ',' after each substring.
    /// (3) This function was NOT implemented as:
    ///       for (i = 0; i &lt; n; i++)
    ///           strcat(dest, sarrayGetString(sa, i, L_NOCOPY));
    ///     Do you see why?
    /// </remarks>
    public string ToString(int addnlflag)
    {
        return ToStringRange(0, 0, addnlflag);
    }

    // leptonica: sarrayToStringRange (sarray1.c)
    /// <summary>Concatenates the specified range of strings.</summary>
    /// <param name="first">index of first string to use; starts with 0</param>
    /// <param name="nstrings">number of strings to append into the result; use
    /// 0 to append to the end of the sarray</param>
    /// <param name="addnlflag">flag: 0 adds nothing to each substring;
    /// 1 adds '\n' to each substring; 2 adds ' ' to each substring;
    /// 3 adds ',' to each substring</param>
    /// <remarks>
    /// (1) Concatenates the specified strings in the sarray, preserving
    ///     all white space.
    /// (2) If addnlflag != 0, adds '\n', ' ' or ',' after each substring.
    /// (3) If the sarray is empty, this returns a string with just
    ///     the character corresponding to %addnlflag.
    /// </remarks>
    public string ToStringRange(int first, int nstrings, int addnlflag)
    {
        const string func = "sarrayToStringRange";
        if (addnlflag != 0 && addnlflag != 1 && addnlflag != 2 && addnlflag != 3)
            throw Lept.Arg(func, "invalid addnlflag");

        int n = Count;

            /* Empty sa; return char corresponding to addnlflag only */
        if (n == 0) {
            if (first == 0) {
                if (addnlflag == 0)
                    return "";
                if (addnlflag == 1)
                    return "\n";
                if (addnlflag == 2)
                    return " ";
                else  /* addnlflag == 3) */
                    return ",";
            } else {
                throw Lept.Arg(func, "first not valid");
            }
        }

            /* Determine the range of string indices to be used */
        if (first < 0 || first >= n)
            throw Lept.Arg(func, "first not valid");
        if (nstrings == 0 || (nstrings > n - first))
            nstrings = n - first;  /* no overflow */
        int last = first + nstrings - 1;

            /* Construct the output */
        var dest = new StringBuilder();
        for (int i = first; i <= last; i++) {
            string src = GetString(i, L.L_NOCOPY);
            dest.Append(CStr(src));
            if (addnlflag == 1)
                dest.Append('\n');
            else if (addnlflag == 2)
                dest.Append(' ');
            else if (addnlflag == 3)
                dest.Append(',');
        }

        return dest.ToString();
    }

    /*----------------------------------------------------------------------*
     *           Concatenate strings uniformly within the sarray            *
     *----------------------------------------------------------------------*/
    // leptonica: sarrayConcatUniformly (sarray1.c)
    /// <summary>Divides the array into n sets and concatenates each set.</summary>
    /// <param name="n">number of strings in output sarray</param>
    /// <param name="addnlflag">flag: 0 adds nothing to each substring;
    /// 1 adds '\n' to each substring; 2 adds ' ' to each substring;
    /// 3 adds ',' to each substring</param>
    /// <remarks>
    /// (1) Divides %sa into %n essentially equal sets of strings,
    ///     concatenates each set individually, and makes an output
    ///     sarray with the %n concatenations.  %n must not exceed the
    ///     number of strings in %sa.
    /// (2) If addnlflag != 0, adds '\n', ' ' or ',' after each substring.
    /// </remarks>
    public Sarray ConcatUniformly(int n, int addnlflag)
    {
        int ntot = Count;
        if (n < 1)
            throw Lept.Arg("sarrayConcatUniformly", "n must be >= 1");
        if (n > ntot)
            throw Lept.Arg("sarrayConcatUniformly", $"n = {n} > ntot = {ntot}");
        if (addnlflag != 0 && addnlflag != 1 && addnlflag != 2 && addnlflag != 3)
            throw Lept.Arg("sarrayConcatUniformly", "invalid addnlflag");

        Sarray saout = Create(0);
        Numa na = Numa.GetUniformBinSizes(ntot, n);
        int first = 0;
        for (int i = 0; i < n; i++) {
            int nstr = na.GetIValue(i);
            string str = ToStringRange(first, nstr, addnlflag);
            saout.AddString(str, L.L_INSERT);
            first += nstr;
        }
        return saout;
    }

    /*----------------------------------------------------------------------*
     *                           Join 2 sarrays                             *
     *----------------------------------------------------------------------*/
    // leptonica: sarrayJoin (sarray1.c)
    /// <summary>Appends copies of the strings of sa2 to this array.</summary>
    /// <param name="sa2">append to this array</param>
    /// <remarks>
    /// (1) Copies of the strings in sarray2 are added to sarray1.
    /// </remarks>
    public void Join(Sarray sa2)
    {
        int n = sa2.Count;
        for (int i = 0; i < n; i++) {
            string str = sa2.GetString(i, L.L_NOCOPY);
            AddString(str, L.L_COPY);
        }
    }

    // leptonica: sarrayAppendRange (sarray1.c)
    /// <summary>Appends copies of a range of the strings of sa2 to this array.</summary>
    /// <param name="sa2">append specified range of strings in sa2 to this array</param>
    /// <param name="start">index of first string of sa2 to append</param>
    /// <param name="end">index of last string of sa2 to append;
    /// -1 to append to end of array</param>
    /// <remarks>
    /// (1) Copies of the strings in sarray2 are added to sarray1.
    /// (2) The [start ... end] range is truncated if necessary.
    /// (3) Use end == -1 to append to the end of sa2.
    /// </remarks>
    public void AppendRange(Sarray sa2, int start, int end)
    {
        if (start < 0)
            start = 0;
        int n = sa2.Count;
        if (end < 0 || end >= n)
            end = n - 1;
        if (start > end)
            throw Lept.Arg("sarrayAppendRange", "start > end");

        for (int i = start; i <= end; i++) {
            string str = sa2.GetString(i, L.L_NOCOPY);
            AddString(str, L.L_COPY);
        }
    }

    /*----------------------------------------------------------------------*
     *          Pad an sarray to be the same size as another sarray         *
     *----------------------------------------------------------------------*/
    // leptonica: sarrayPadToSameSize (sarray1.c)
    /// <summary>Pads the smaller of the two arrays with padstring.</summary>
    /// <param name="sa2">the other array</param>
    /// <param name="padstring">the string to add</param>
    /// <remarks>
    /// (1) If two sarrays have different size, this adds enough
    ///     instances of %padstring to the smaller so that they are
    ///     the same size.  It is useful when two or more sarrays
    ///     are being sequenced in parallel, and it is necessary to
    ///     find a valid string at each index.
    /// </remarks>
    public void PadToSameSize(Sarray sa2, string padstring)
    {
        Sarray sa1 = this;
        int n1 = sa1.Count;
        int n2 = sa2.Count;
        if (n1 < n2) {
            for (int i = n1; i < n2; i++)
                sa1.AddString(padstring, L.L_COPY);
        } else if (n1 > n2) {
            for (int i = n2; i < n1; i++)
                sa2.AddString(padstring, L.L_COPY);
        }
    }

    /*----------------------------------------------------------------------*
     *                   Convert word sarray to line sarray                 *
     *----------------------------------------------------------------------*/
    // leptonica: sarrayConvertWordsToLines (sarray1.c)
    /// <summary>Converts an array of words to an array of formatted lines.</summary>
    /// <param name="linesize">max num of chars in each line</param>
    /// <returns>sa of formatted lines</returns>
    /// <remarks>
    /// (1) This is useful for re-typesetting text to a specific maximum
    ///     line length.  The individual words in the input sarray
    ///     are concatenated into textlines.  An input word string of zero
    ///     length is taken to be a paragraph separator.  Each time
    ///     such a string is found, the current line is ended and
    ///     a new line is also produced that contains just the
    ///     string of zero length "".  When the output sarray
    ///     of lines is eventually converted to a string with newlines
    ///     typically appended to each line string, the empty
    ///     strings are just converted to newlines, producing the visible
    ///     paragraph separation.
    /// (2) What happens when a word is larger than linesize?
    ///     We write it out as a single line anyway!  Words preceding
    ///     or following this long word are placed on lines preceding
    ///     or following the line with the long word.  Why this choice?
    ///     Long "words" found in text documents are typically URLs, and
    ///     it's often desirable not to put newlines in the middle of a URL.
    ///     The text display program e.g., text editor will typically
    ///     wrap the long "word" to fit in the window.
    /// </remarks>
    public Sarray ConvertWordsToLines(int linesize)
    {
        const string emptystring = "";

        Sarray saout = Create(0);
        int n = Count;
        int totlen = 0;
        Sarray? sal = null;
        for (int i = 0; i < n; i++) {
            if (sal == null)
                sal = Create(0);
            string wd = GetString(i, L.L_NOCOPY);
            int len = Utf8Len(wd);
            if (len == 0) {  /* end of paragraph: end line & insert blank line */
                if (totlen > 0) {
                    string strl = sal.ToString(2);
                    saout.AddString(strl, L.L_INSERT);
                }
                saout.AddString(emptystring, L.L_COPY);
                sal = null;
                totlen = 0;
            } else if (totlen == 0 && len + 1 > linesize) {  /* long word! */
                saout.AddString(wd, L.L_COPY);  /* copy to one line */
            } else if (totlen + len + 1 > linesize) {  /* end line & start new */
                string strl = sal.ToString(2);
                saout.AddString(strl, L.L_INSERT);
                sal = Create(0);
                sal.AddString(wd, L.L_COPY);
                totlen = len + 1;
            } else {  /* add to current line */
                sal.AddString(wd, L.L_COPY);
                totlen += len + 1;
            }
        }
        if (totlen > 0) {   /* didn't end with blank line; output last line */
            string strl = sal!.ToString(2);
            saout.AddString(strl, L.L_INSERT);
        }

        return saout;
    }

    /*----------------------------------------------------------------------*
     *                    Split string on separator list                    *
     *----------------------------------------------------------------------*/
    // leptonica: sarraySplitString (sarray1.c)
    /// <summary>Appends the substrings of a string split on any separator character.</summary>
    /// <param name="str">string to split</param>
    /// <param name="separators">characters that split input string</param>
    /// <remarks>
    /// (1) This uses strtokSafe().  See the notes there in utils.c.
    /// </remarks>
    public void SplitString(string str, string separators)
    {
        string? saveptr = null;
        string? substr = Utils.StrtokSafe(str, separators, ref saveptr);
        if (substr != null)
            AddString(substr, L.L_INSERT);
        while ((substr = Utils.StrtokSafe(null, separators, ref saveptr)) != null)
            AddString(substr, L.L_INSERT);
    }

    /*----------------------------------------------------------------------*
     *                              Filter sarray                           *
     *----------------------------------------------------------------------*/
    // leptonica: sarraySelectBySubstring (sarray1.c)
    /// <summary>Selects the strings that have substr as a substring.</summary>
    /// <param name="substr">[optional] substring for matching; can be null</param>
    /// <returns>output sarray, filtered with substring</returns>
    /// <remarks>
    /// (1) This selects all strings in sain that have substr as a substring.
    ///     Note that we can't use strncmp() because we're looking for
    ///     a match to the substring anywhere within each filename.
    /// (2) If substr == NULL, returns a copy of the sarray.
    /// </remarks>
    public Sarray SelectBySubstring(string? substr)
    {
        Sarray sain = this;
        int n = sain.Count;
        if (substr == null || n == 0)
            return sain.Copy();

        Sarray saout = Create(n);
        for (int i = 0; i < n; i++) {
            string str = sain.GetString(i, L.L_NOCOPY);
            FindSequence(str, substr, out _, out bool found);
            if (found)
                saout.AddString(str, L.L_COPY);
        }

        return saout;
    }

    // leptonica: sarraySelectRange (sarray1.c)
    /// <summary>Selects copies of the strings in the index range [first ... last].</summary>
    /// <param name="first">index of first string to be selected</param>
    /// <param name="last">index of last string to be selected;
    /// use 0 to go to the end of the sarray</param>
    /// <remarks>
    /// (1) This makes %saout consisting of copies of all strings in %sain
    ///     in the index set [first ... last].  Use %last == 0 to get all
    ///     strings from %first to the last string in the sarray.
    /// </remarks>
    public Sarray SelectRange(int first, int last)
    {
        Sarray sain = this;
        if (first < 0) first = 0;
        int n = sain.Count;
        if (last <= 0) last = n - 1;
        if (last >= n) {
            Log.Warning("sarraySelectRange", "last > n - 1; setting to n - 1");
            last = n - 1;
        }
        if (first > last)
            throw Lept.Arg("sarraySelectRange", "first must be >= last");

        Sarray saout = Create(0);
        for (int i = first; i <= last; i++) {
            string str = sain.GetString(i, L.L_COPY);
            saout.AddString(str, L.L_INSERT);
        }

        return saout;
    }

    // leptonica: sarrayParseRange (sarray1.c)
    /// <summary>Finds the range of the next set of strings that do not have the marker substring.</summary>
    /// <param name="start">index to start range search</param>
    /// <param name="actualstart">index of actual start; may be &gt; 'start'</param>
    /// <param name="end">index of end</param>
    /// <param name="newstart">index of start of next range</param>
    /// <param name="substr">substring for matching at beginning of string</param>
    /// <param name="loc">byte offset within the string for the pattern;
    /// use -1 if the location does not matter.</param>
    /// <returns>true if a valid range was found (0 in C); false otherwise (1 in C)</returns>
    /// <remarks>
    /// (1) This finds the range of the next set of strings in SA,
    ///     beginning the search at 'start', that does NOT have
    ///     the substring 'substr' either at the indicated location
    ///     in the string or anywhere in the string.  The input
    ///     variable 'loc' is the specified offset within the string;
    ///     use -1 to indicate 'anywhere in the string'.
    /// (2) Always check the return value to verify that a valid range
    ///     was found.
    /// (3) If a valid range is not found, the values of actstart,
    ///     end and newstart are all set to the size of sa.
    /// (4) If this is the last valid range, newstart returns the value n.
    ///     In use, this should be tested before calling the function.
    /// (5) Usage example.  To find all the valid ranges in a file
    ///     where the invalid lines begin with two dashes, copy each
    ///     line in the file to a string in an sarray, and do:
    ///        start = 0;
    ///        while (sa.ParseRange(start, out actstart, out end, out start, "--", 0))
    ///            Console.Error.WriteLine($"start = {actstart}, end = {end}");
    /// </remarks>
    public bool ParseRange(int start, out int actualstart, out int end, out int newstart,
                           string substr, int loc)
    {
        Sarray sa = this;
        int n = sa.Count;
        actualstart = end = newstart = n;

            /* Look for the first string without the marker */
        if (start < 0 || start >= n)
            return false;
        int i;
        for (i = start; i < n; i++) {
            string str = sa.GetString(i, L.L_NOCOPY);
            FindSequence(str, substr, out int offset, out bool found);
            if (loc < 0) {
                if (!found) break;
            } else {
                if (!found || offset != loc) break;
            }
        }
        start = i;
        if (i == n)  /* couldn't get started */
            return false;

            /* Look for the last string without the marker */
        actualstart = start;
        for (i = start + 1; i < n; i++) {
            string str = sa.GetString(i, L.L_NOCOPY);
            FindSequence(str, substr, out int offset, out bool found);
            if (loc < 0) {
                if (found) break;
            } else {
                if (found && offset == loc) break;
            }
        }
        end = i - 1;
        start = i;
        if (i == n)  /* no further range */
            return true;

            /* Look for the first string after *pend without the marker.
             * This will start the next run of strings, if it exists. */
        for (i = start; i < n; i++) {
            string str = sa.GetString(i, L.L_NOCOPY);
            FindSequence(str, substr, out int offset, out bool found);
            if (loc < 0) {
                if (!found) break;
            } else {
                if (!found || offset != loc) break;
            }
        }
        if (i < n)
            newstart = i;

        return true;
    }

    /*----------------------------------------------------------------------*
     *                           Serialize for I/O                          *
     *----------------------------------------------------------------------*/
    // leptonica: sarrayRead (sarray1.c)
    /// <summary>Reads a serialized sarray from a file.</summary>
    public static Sarray Read(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenReadStream(filename);
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException
                                    || e is ArgumentException || e is NotSupportedException) {
            throw Lept.Fail("sarrayRead", $"stream not opened: {filename}");
        }
        using (fp) {
            try {
                return ReadStream(fp);
            } catch (LeptonicaException) {
                throw Lept.Fail("sarrayRead", $"sa not read: {filename}");
            }
        }
    }

    // leptonica: sarrayReadStream (sarray1.c)
    /// <summary>Reads a serialized sarray from a stream.</summary>
    /// <param name="fp">stream, positioned at the start of the serialization</param>
    /// <remarks>
    /// (1) We store the size of each string along with the string.
    ///     The limit on the number of strings is 50M.
    ///     The limit on the size of any string is 2^30 bytes.
    /// (2) This allows a string to have embedded newlines.  By reading
    ///     the entire string, as determined by its size, we are
    ///     not affected by any number of embedded newlines.
    /// (3) It is OK for the sarray to be empty.
    /// </remarks>
    public static Sarray ReadStream(Stream fp)
    {
        const string func = "sarrayReadStream";
        var rd = new ScanReader(fp);
        int[] vals = new int[2];

        if (rd.Scanf("\nSarray Version %d\n", vals) != 1)
            throw Lept.Fail(func, "not an sarray file");
        if (vals[0] != L.SARRAY_VERSION_NUMBER)
            throw Lept.Fail(func, "invalid sarray version");

        if (rd.Scanf("Number of strings = %d\n", vals) != 1)
            throw Lept.Fail(func, "error on # strings");
        int n = vals[0];
        if (n < 0)
            throw Lept.Fail(func, "num string ptrs <= 0");
        if (n > MaxPtrArraySize)
            throw Lept.Fail(func, "too many string ptrs");
        if (n == 0) Log.Info(func, "the sarray is empty");

        Sarray sa = Create(n);

        for (int i = 0; i < n; i++) {
                /* Get the size of the stored string */
            int size;
            if (rd.Scanf("%d[%d]:", vals) != 2 || (size = vals[1]) > (1 << 30)) {
                Log.Error(func, "error on string size");
                throw Lept.Fail(func, "error on string size");
            }
            /* NOTE(upstream): a negative size reads out of bounds in C; it is an error here. */
            if (size < 0) {
                Log.Error(func, "error on string size");
                throw Lept.Fail(func, "error on string size");
            }
                /* Read the stored string, plus leading spaces and trailing \n */
            byte[]? buf = rd.ReadBytes(size + 3);
            if (buf == null) {
                Log.Error(func, "error reading string");
                throw Lept.Fail(func, "error reading string");
            }
                /* Remove the \n that was added by sarrayWriteStream(), and
                 * copy the string in, skipping the 2 leading spaces */
            sa.AddString(CStr(Encoding.UTF8.GetString(buf, 2, size)), L.L_COPY);
        }
        rd.Scanf("\n", vals);
        rd.Finish();

        return sa;
    }

    // leptonica: sarrayReadMem (sarray1.c)
    /// <summary>Reads a serialized sarray from memory.</summary>
    /// <param name="data">serialization in ascii</param>
    public static Sarray ReadMem(ReadOnlySpan<byte> data)
    {
        using var fp = new MemoryStream(data.ToArray(), false);
        try {
            return ReadStream(fp);
        } catch (LeptonicaException) {
            Log.Error("sarrayReadMem", "sarray not read");
            throw;
        }
    }

    // leptonica: sarrayWrite (sarray1.c)
    /// <summary>Writes the serialized sarray to a file (replacing it).</summary>
    public void Write(string filename)
    {
        WriteFile(filename, FileMode.Create, "sarrayWrite", "sa not written to stream");
    }

    // leptonica: sarrayWriteStream (sarray1.c)
    /// <summary>Writes the serialized sarray to a stream.</summary>
    /// <param name="fp">output stream</param>
    /// <remarks>
    /// (1) This appends a '\n' to each string, which is stripped
    ///     off by sarrayReadStream().
    /// </remarks>
    public void WriteStream(Stream fp)
    {
        byte[] bytes = Serialize();
        fp.Write(bytes, 0, bytes.Length);
    }

    // leptonica: sarrayWriteStderr (sarray1.c)
    /// <summary>Writes the serialized sarray to stderr.</summary>
    public void WriteStderr()
    {
        using Stream err = Console.OpenStandardError();
        WriteStream(err);
    }

    // leptonica: sarrayWriteMem (sarray1.c)
    /// <summary>Serializes the sarray to a byte array (ascii).</summary>
    /// <remarks>
    /// (1) Serializes a sarray in memory and puts the result in a buffer.
    /// </remarks>
    public byte[] WriteMem()
    {
        return Serialize();
    }

    // leptonica: sarrayAppend (sarray1.c)
    /// <summary>Appends the serialized sarray to a file.</summary>
    public void Append(string filename)
    {
        WriteFile(filename, FileMode.Append, "sarrayAppend", "sa not appended to stream");
    }

    private void WriteFile(string filename, FileMode mode, string func, string failmsg)
    {
        FileStream fp;
        try {
            fp = Utils.FopenWriteStream(filename, mode == FileMode.Append ? "a" : "w");
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException
                                    || e is ArgumentException || e is NotSupportedException) {
            throw Lept.Fail(func, $"stream not opened: {filename}");
        }
        using (fp) {
            try {
                WriteStream(fp);
            } catch (IOException) {
                throw Lept.Fail(func, $"{failmsg}: {filename}");
            }
        }
    }

    /// <summary>
    /// The text written by sarrayWriteStream() / sarrayWriteStderr():
    /// "\nSarray Version %d\n", "Number of strings = %d\n",
    /// "  %d[%d]:  %s\n" for each string, and a final "\n".
    /// </summary>
    private byte[] Serialize()
    {
        CultureInfo inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("\nSarray Version ").Append(L.SARRAY_VERSION_NUMBER.ToString(inv)).Append('\n');
        sb.Append("Number of strings = ").Append(n.ToString(inv)).Append('\n');
        for (int i = 0; i < n; i++) {
            string str = CStr(array[i]!);
            int len = Encoding.UTF8.GetByteCount(str);
            sb.Append("  ").Append(i.ToString(inv)).Append('[').Append(len.ToString(inv))
              .Append("]:  ").Append(str).Append('\n');
        }
        sb.Append('\n');
        return new UTF8Encoding(false).GetBytes(sb.ToString());
    }

    /// <summary>
    /// The part of fscanf() used by sarrayReadStream(): white space in the format
    /// matches any amount of white space, %d reads a decimal int, any other
    /// character must match. One byte of look-ahead is kept, as with ungetc().
    /// </summary>
    private sealed class ScanReader
    {
        private readonly Stream stream;
        private int pending = -2;   /* -2: none; -1: end of file */

        public ScanReader(Stream stream)
        {
            this.stream = stream;
        }

        private int Read()
        {
            if (pending != -2) {
                int b = pending;
                pending = -2;
                return b;
            }
            return stream.ReadByte();
        }

        private void Unread(int b)
        {
            pending = b;
        }

        private static bool IsSpace(int c)
        {
            return c == ' ' || (c >= '\t' && c <= '\r');
        }

        /// <summary>Returns the number of conversions made, stored in vals.</summary>
        public int Scanf(string fmt, int[] vals)
        {
            int count = 0;
            for (int k = 0; k < fmt.Length; k++) {
                char c = fmt[k];
                if (IsSpace(c)) {
                    SkipSpace();
                } else if (c == '%') {
                    k++;  /* only %d is used */
                    SkipSpace();
                    int ch = Read();
                    int sign = 1;
                    if (ch == '+' || ch == '-') {
                        if (ch == '-') sign = -1;
                        ch = Read();
                    }
                    if (ch < '0' || ch > '9') {
                        Unread(ch);
                        return count;
                    }
                    long v = 0;
                    while (ch >= '0' && ch <= '9') {
                        if (v < (1L << 40))
                            v = v * 10 + (ch - '0');
                        ch = Read();
                    }
                    Unread(ch);
                    vals[count++] = unchecked((int)(sign * v));
                } else {
                    int ch = Read();
                    if (ch != c) {
                        Unread(ch);
                        return count;
                    }
                }
            }
            return count;
        }

        private void SkipSpace()
        {
            int ch;
            while (IsSpace(ch = Read())) { }
            Unread(ch);
        }

        /// <summary>Reads exactly count bytes (like fread); null if the stream ends first.</summary>
        public byte[]? ReadBytes(int count)
        {
            var result = new MemoryStream();
            byte[] chunk = new byte[1 << 16];
            while (count > 0) {
                int got;
                if (pending >= 0) {
                    chunk[0] = (byte)pending;
                    pending = -2;
                    got = 1;
                } else {
                    got = stream.Read(chunk, 0, Math.Min(chunk.Length, count));
                }
                if (got <= 0)
                    return null;
                result.Write(chunk, 0, got);
                count -= got;
            }
            return result.ToArray();
        }

        /// <summary>Gives the look-ahead byte back to the stream, if it can seek.</summary>
        public void Finish()
        {
            if (pending >= 0 && stream.CanSeek)
                stream.Seek(-1, SeekOrigin.Current);
            pending = -2;
        }
    }

    /*---------------------------------------------------------------------*
     *                           Directory filenames                       *
     *---------------------------------------------------------------------*/
    // leptonica: getNumberedPathnamesInDirectory (sarray1.c)
    /// <summary>Returns the full pathnames of the numbered files in a directory, indexed by number.</summary>
    /// <param name="dirname">directory name</param>
    /// <param name="substr">[optional] substring filter on filenames; can be null</param>
    /// <param name="numpre">number of characters in name before number</param>
    /// <param name="numpost">number of characters in name after the number,
    /// up to a dot before an extension</param>
    /// <param name="maxnum">only consider page numbers up to this value</param>
    /// <remarks>
    /// (1) Returns the full pathnames of the numbered filenames in
    ///     the directory.  The number in the filename is the index
    ///     into the sarray.  For indices for which there are no filenames,
    ///     an empty string ("") is placed into the sarray.
    ///     This makes reading numbered files very simple.  For example,
    ///     the image whose filename includes number N can be retrieved using
    ///          pixReadIndexed(sa, N);
    /// (2) If %substr is not NULL, only filenames that contain
    ///     the substring can be included.  If %substr is NULL,
    ///     all matching filenames are used.
    /// (3) If no numbered files are found, it returns an empty sarray,
    ///     with no initialized strings.
    /// (4) It is assumed that the page number is contained within
    ///     the basename (the filename without directory or extension).
    ///     %numpre is the number of characters in the basename
    ///     preceding the actual page number; %numpost is the number
    ///     following the page number, up to either the end of the
    ///     basename or a ".", whichever comes first.
    /// (5) This is useful when all filenames contain numbers that are
    ///     not necessarily consecutive.  0-padding is not required.
    /// (6) To use a O(n) matching algorithm, the largest page number
    ///     is found and two internal arrays of this size are created.
    ///     This maximum is constrained not to exceed %maxsum,
    ///     to make sure that an unrealistically large number is not
    ///     accidentally used to determine the array sizes.
    /// </remarks>
    public static Sarray GetNumberedPathnamesInDirectory(string dirname, string? substr,
                                                         int numpre, int numpost, int maxnum)
    {
        Sarray sa = GetSortedPathnamesInDirectory(dirname, substr, 0, 0);
        int nfiles = sa.Count;
        if (nfiles == 0)
            return Create(1);

        return ConvertSortedToNumberedPathnames(sa, numpre, numpost, maxnum);
    }

    // leptonica: getSortedPathnamesInDirectory (sarray1.c)
    /// <summary>Returns the sorted full pathnames of the files in a directory.</summary>
    /// <param name="dirname">directory name</param>
    /// <param name="substr">[optional] substring filter on filenames; can be null</param>
    /// <param name="first">0-based</param>
    /// <param name="nfiles">use 0 for all to the end</param>
    /// <remarks>
    /// (1) Use %substr to filter filenames in the directory.  If
    ///     %substr == NULL, this takes all files.
    /// (2) The files in the directory, after optional filtering by
    ///     the substring, are lexically sorted in increasing order.
    ///     Use %first and %nfiles to select a contiguous set of files.
    /// (3) The full pathnames are returned for the requested sequence.
    ///     If no files are found after filtering, returns an empty sarray.
    /// </remarks>
    public static Sarray GetSortedPathnamesInDirectory(string dirname, string? substr,
                                                       int first, int nfiles)
    {
        Sarray sa = GetFilenamesInDirectory(dirname);
        Sarray safiles = sa.SelectBySubstring(substr);
        int n = safiles.Count;
        if (n == 0) {
            Log.Warning("getSortedPathnamesInDirectory", "no files found");
            return safiles;
        }

        Sort(safiles, safiles, L.L_SORT_INCREASING);

        first = Math.Min(Math.Max(first, 0), n - 1);
        if (nfiles == 0)
            nfiles = n - first;
        int last = Math.Min(first + nfiles - 1, n - 1);

        Sarray saout = Create(last - first + 1);
        for (int i = first; i <= last; i++) {
            string fname = safiles.GetString(i, L.L_NOCOPY);
            string fullname = Utils.PathJoin(dirname, fname);
            saout.AddString(fullname, L.L_INSERT);
        }

        return saout;
    }

    // leptonica: convertSortedToNumberedPathnames (sarray1.c)
    /// <summary>Converts sorted pathnames with numbers to an array indexed by the number.</summary>
    /// <param name="sa">sorted pathnames including zero-padded integers</param>
    /// <param name="numpre">number of characters in name before number</param>
    /// <param name="numpost">number of characters in name after the number,
    /// up to a dot before an extension</param>
    /// <param name="maxnum">only consider page numbers up to this value</param>
    /// <remarks>
    /// (1) Typically, numpre = numpost = 0; e.g., when the filename
    ///     just has a number followed by an optional extension.
    /// </remarks>
    public static Sarray ConvertSortedToNumberedPathnames(Sarray sa, int numpre, int numpost,
                                                          int maxnum)
    {
        int nfiles = sa.Count;
        if (nfiles == 0)
            return Create(1);

            /* Find the last file in the sorted array that has a number
             * that (a) matches the count pattern and (b) does not
             * exceed %maxnum.  %maxnum sets an upper limit on the size
             * of the sarray.  */
        int num = 0;
        for (int i = nfiles - 1; i >= 0; i--) {
            string fname = sa.GetString(i, L.L_NOCOPY);
            num = Utils.ExtractNumberFromFilename(fname, numpre, numpost);
            if (num < 0) continue;
            num = Math.Min(num + 1, maxnum);
            break;
        }

        if (num <= 0)  /* none found */
            return Create(1);

            /* Insert pathnames into the output sarray.
             * Ignore numbers that are out of the range of sarray. */
        Sarray saout = CreateInitialized(num, "");
        for (int i = 0; i < nfiles; i++) {
            string fname = sa.GetString(i, L.L_NOCOPY);
            int index = Utils.ExtractNumberFromFilename(fname, numpre, numpost);
            if (index < 0 || index >= num) continue;
            string str = saout.GetString(index, L.L_NOCOPY);
            if (str.Length != 0 && str[0] != '\0') {
                Log.Warning("convertSortedToNumberedPathnames",
                            $"\n  Multiple files with same number: {index}");
            }
            saout.ReplaceString(index, fname, L.L_COPY);
        }

        return saout;
    }

    // leptonica: getFilenamesInDirectory (sarray1.c)
    /// <summary>Returns the names of the files (not directories) in a directory.</summary>
    /// <param name="dirname">directory name</param>
    /// <returns>sarray of file names</returns>
    /// <remarks>
    /// (1) The versions compiled under unix and cygwin use the POSIX C
    ///     library commands for handling directories.  For Windows,
    ///     there is a separate implementation.  Here System.IO is used.
    /// (2) It returns an array of filename tails; i.e., only the part of
    ///     the path after the last slash.
    /// (3) Entries that are directories (including symbolic links to
    ///     directories) are skipped; the order is that of the file system.
    /// </remarks>
    public static Sarray GetFilenamesInDirectory(string dirname)
    {
        const string func = "getFilenamesInDirectory";
        if (dirname.Length == 0)
            throw Lept.Arg(func, "dirname is empty");

        string realdir = Path.GetFullPath(Utils.GenPathname(dirname, null));
        if (!Directory.Exists(realdir)) {
            if (File.Exists(realdir)) {
                Log.Error(func, $"directory {realdir} not opened");
                throw Lept.Fail(func, $"directory {realdir} not opened");
            }
            throw Lept.Fail(func, "realdir not made");
        }

        Sarray safiles = Create(0);
        try {
            foreach (string path in Directory.EnumerateFileSystemEntries(realdir)) {
                if (Directory.Exists(path))
                    continue;
                safiles.AddString(Path.GetFileName(path), L.L_COPY);
            }
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) {
            Log.Error(func, $"directory {realdir} not opened");
            throw Lept.Fail(func, $"directory {realdir} not opened");
        }
        return safiles;
    }

    /*----------------------------------------------------------------------*
     *                         C string helpers                             *
     *----------------------------------------------------------------------*/
    /// <summary>The string as C sees it: cut at the first '\0'.</summary>
    private static string CStr(string s)
    {
        int z = s.IndexOf('\0');
        return z < 0 ? s : s.Substring(0, z);
    }

    /// <summary>The C string that starts at buf[start]: up to the first '\0' or the end.</summary>
    private static string CStr(char[] buf, int start)
    {
        int end = start;
        while (end < buf.Length && buf[end] != '\0')
            end++;
        return new string(buf, start, end - start);
    }

    /// <summary>strlen() of the string, as the UTF-8 bytes of the C string.</summary>
    private static int Utf8Len(string s)
    {
        return Encoding.UTF8.GetByteCount(CStr(s));
    }

    /// <summary>
    /// Finds the first occurrence of the byte sequence of substr in the bytes of str.
    /// An empty sequence is never found.
    /// </summary>
    private static void FindSequence(string str, string substr, out int offset, out bool found)
    {
        byte[] data = Encoding.UTF8.GetBytes(CStr(str));
        byte[] sequence = Encoding.UTF8.GetBytes(CStr(substr));
        found = Utils.ArrayFindSequence(data, sequence, out offset);
    }
}
