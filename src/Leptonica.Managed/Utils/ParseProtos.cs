using System.Text;

namespace Leptonica;

/*
 *  parseprotos.c
 *
 *      Utils.ParseForProtos (parseForProtos)
 */
public static partial class Utils
{
    private const int ProtoMaxTokenSize = 2048;

    // leptonica: parseForProtos (parseprotos.c)
    /// <summary>Parses cpp output for non-static C function prototypes.</summary>
    /// <param name="filein">output of cpp</param>
    /// <param name="prestring">[optional] string that prefaces each decl; use null to omit</param>
    /// <returns>string of function prototypes</returns>
    public static string ParseForProtos(string filein, string? prestring)
    {
        const string func = "parseForProtos";
        if (filein == null)
            throw Lept.Arg(func, "filein not defined");

        byte[] raw = BinaryRead(filein);
        string strdata = Encoding.Latin1.GetString(raw);
        Sarray sa = Sarray.CreateLinesFromString(strdata, 0);

        Sarray saout = Sarray.Create(0);
        int next = 0;
        while (true)
        {
            SearchForProtoSignature(sa, next, out int start, out int stop, out int charindex, out bool found);
            if (!found)
                break;

            string str = CaptureProtoSignature(sa, start, stop, charindex);
            Sarray satest = Sarray.CreateWordsFromString(str);
            string secondword = satest.Count > 1 ? satest.GetString(1, L.L_NOCOPY) : string.Empty;
            if (secondword != "static" && secondword != "extern" && secondword != "typedef")
            {
                if (prestring != null)
                    saout.AddString(prestring + str, L.L_INSERT);
                else
                    saout.AddString(str, L.L_INSERT);
            }

            SkipToEndOfFunction(sa, stop, charindex, out next);
            if (next == -1)
                break;
        }

        return saout.ToString(1);
    }

    // leptonica: getNextNonCommentLine (parseprotos.c)
    private static void GetNextNonCommentLine(Sarray sa, int start, out int pnext)
    {
        pnext = -1;
        int n = sa.Count;
        for (int i = start; i < n; i++)
        {
            string str = sa.GetString(i, L.L_NOCOPY);
            if (str.Length == 0 || str[0] != '#')
            {
                pnext = i;
                return;
            }
        }
    }

    // leptonica: getNextNonBlankLine (parseprotos.c)
    private static void GetNextNonBlankLine(Sarray sa, int start, out int pnext)
    {
        pnext = -1;
        int n = sa.Count;
        for (int i = start; i < n; i++)
        {
            string str = sa.GetString(i, L.L_NOCOPY);
            for (int j = 0; j < str.Length; j++)
            {
                char c = str[j];
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r')
                {
                    pnext = i;
                    return;
                }
            }
        }
    }

    // leptonica: getNextNonDoubleSlashLine (parseprotos.c)
    private static void GetNextNonDoubleSlashLine(Sarray sa, int start, out int pnext)
    {
        pnext = -1;
        int n = sa.Count;
        for (int i = start; i < n; i++)
        {
            string str = sa.GetString(i, L.L_NOCOPY);
            if (str.Length < 2 || str[0] != '/' || str[1] != '/')
            {
                pnext = i;
                return;
            }
        }
    }

    // leptonica: searchForProtoSignature (parseprotos.c)
    private static void SearchForProtoSignature(Sarray sa, int begin,
                                                out int pstart, out int pstop,
                                                out int pcharindex, out bool pfound)
    {
        pstart = pstop = pcharindex = 0;
        pfound = false;

        while (true)
        {
            GetNextNonCommentLine(sa, begin, out int next);
            if (next == -1)
                return;
            if (next != begin)
            {
                begin = next;
                continue;
            }

            GetNextNonBlankLine(sa, begin, out next);
            if (next == -1)
                return;
            if (next != begin)
            {
                begin = next;
                continue;
            }

            GetNextNonDoubleSlashLine(sa, begin, out next);
            if (next == -1)
                return;
            if (next != begin)
            {
                begin = next;
                continue;
            }

            GetOffsetForCharacter(sa, next, '(', out int soffsetlp, out int boffsetlp, out int toffsetlp);
            if (soffsetlp == -1)
                break;
            GetOffsetForMatchingRP(sa, next, soffsetlp, boffsetlp, toffsetlp,
                                   out int soffsetrp, out int boffsetrp, out _);
            GetOffsetForCharacter(sa, next, '{', out int soffsetlb, out int boffsetlb, out int toffsetlb);
            GetOffsetForCharacter(sa, next, ';', out int soffsetsc, out _, out int toffsetsc);

            if (soffsetrp == -1 || soffsetlb == -1)
                break;

            if (toffsetlb < toffsetlp)
            {
                SkipToMatchingBrace(sa, next + soffsetlb, boffsetlb, out int rbline, out int rbindex);
                SkipToSemicolon(sa, rbline, rbindex, out int scline);
                begin = scline + 1;
                continue;
            }

            if (soffsetsc != -1 && (toffsetsc < toffsetlb || toffsetsc < toffsetlp))
            {
                SkipToSemicolon(sa, next, -1, out int scline);
                begin = scline + 1;
                continue;
            }

            pstart = next;
            pstop = next + soffsetrp;
            pcharindex = boffsetrp;
            pfound = true;
            break;
        }
    }

    // leptonica: captureProtoSignature (parseprotos.c)
    private static string CaptureProtoSignature(Sarray sa, int start, int stop, int charindex)
    {
        Sarray sap = Sarray.Create(0);
        for (int i = start; i < stop; i++)
            sap.AddString(sa.GetString(i, L.L_COPY), L.L_INSERT);

        string last = sa.GetString(stop, L.L_NOCOPY);
        string prefix = charindex + 1 <= last.Length ? last.Substring(0, charindex + 1) : last;
        sap.AddString(prefix + ";", L.L_INSERT);
        string protostr = sap.ToString(2);
        return CleanProtoSignature(protostr);
    }

    // leptonica: cleanProtoSignature (parseprotos.c)
    private static string CleanProtoSignature(string instr)
    {
        const string func = "cleanProtoSignature";
        Sarray sa = Sarray.CreateWordsFromString(instr);
        int nwords = sa.Count;
        Sarray saout = Sarray.Create(0);
        saout.AddString("extern", L.L_COPY);

        StringBuilder buf = new StringBuilder(128);
        for (int i = 0; i < nwords; i++)
        {
            string str = sa.GetString(i, L.L_NOCOPY);
            buf.Clear();
            for (int j = 0; j < str.Length; j++)
            {
                if (buf.Length > ProtoMaxTokenSize - 6)
                    throw Lept.Fail(func, "token too large");
                char c = str[j];
                if (c == '(')
                    buf.Append(" ( ");
                else if (c == ')')
                    buf.Append(" )");
                else
                    buf.Append(c);
            }
            saout.AddString(buf.ToString(), L.L_COPY);
        }

        string cleanstr = saout.ToString(2);
        return cleanstr.Length > 0 ? cleanstr.Substring(0, cleanstr.Length - 1) : cleanstr;
    }

    // leptonica: skipToEndOfFunction (parseprotos.c)
    private static void SkipToEndOfFunction(Sarray sa, int start, int lbindex, out int pnext)
    {
        _ = lbindex;
        GetOffsetForCharacter(sa, start, '{', out int soffsetlb, out int boffsetlb, out _);
        if (soffsetlb == -1)
        {
            pnext = -1;
            return;
        }
        SkipToMatchingBrace(sa, start + soffsetlb, boffsetlb, out int end, out _);
        pnext = end == -1 ? -1 : end + 1;
    }

    // leptonica: skipToMatchingBrace (parseprotos.c)
    private static void SkipToMatchingBrace(Sarray sa, int start, int lbindex, out int pstop, out int prbindex)
    {
        const string func = "skipToMatchingBrace";
        pstop = -1;
        prbindex = 0;
        bool instring = false;
        int n = sa.Count;
        int sumbrace = 1;

        for (int i = start; i < n; i++)
        {
            string str = sa.GetString(i, L.L_NOCOPY);
            int jstart = (i == start) ? lbindex + 1 : 0;
            for (int j = jstart; j < str.Length; j++)
            {
                if (j == jstart && str[j] == '"')
                    instring = !instring;
                if (j > jstart && str[j] == '"' && str[j - 1] != '\\')
                    instring = !instring;

                char nextChar = (j + 1 < str.Length) ? str[j + 1] : '\0';
                if (str[j] == '{' && nextChar != '\'' && !instring)
                {
                    sumbrace++;
                }
                else if (str[j] == '}' && nextChar != '\'' && !instring)
                {
                    sumbrace--;
                    if (sumbrace == 0)
                    {
                        pstop = i;
                        prbindex = j;
                        return;
                    }
                }
            }
        }

        Log.Error(func, "matching right brace not found");
    }

    // leptonica: skipToSemicolon (parseprotos.c)
    private static void SkipToSemicolon(Sarray sa, int start, int charindex, out int pnext)
    {
        const string func = "skipToSemicolon";
        pnext = -1;
        int n = sa.Count;
        for (int i = start; i < n; i++)
        {
            string str = sa.GetString(i, L.L_NOCOPY);
            int jstart = (i == start) ? charindex + 1 : 0;
            for (int j = jstart; j < str.Length; j++)
            {
                if (str[j] == ';')
                {
                    pnext = i;
                    return;
                }
            }
        }
        Log.Error(func, "semicolon not found");
    }

    // leptonica: getOffsetForCharacter (parseprotos.c)
    private static void GetOffsetForCharacter(Sarray sa, int start, char tchar,
                                              out int psoffset, out int pboffset, out int ptoffset)
    {
        psoffset = -1;
        pboffset = 100000000;
        ptoffset = 100000000;

        int n = sa.Count;
        int totchars = 0;
        for (int i = start; i < n; i++)
        {
            string str = sa.GetString(i, L.L_NOCOPY);
            for (int j = 0; j < str.Length; j++)
            {
                if (str[j] == tchar)
                {
                    psoffset = i - start;
                    pboffset = j;
                    ptoffset = totchars + j;
                    return;
                }
            }
            totchars += str.Length;
        }
    }

    // leptonica: getOffsetForMatchingRP (parseprotos.c)
    private static void GetOffsetForMatchingRP(Sarray sa, int start,
                                              int soffsetlp, int boffsetlp, int toffsetlp,
                                              out int psoffset, out int pboffset, out int ptoffset)
    {
        psoffset = -1;
        pboffset = 100000000;
        ptoffset = 100000000;

        int n = sa.Count;
        int totchars = toffsetlp;
        int leftmatch = 1;
        int firstline = start + soffsetlp;
        for (int i = firstline; i < n; i++)
        {
            string str = sa.GetString(i, L.L_NOCOPY);
            int jstart = (i == firstline) ? boffsetlp + 1 : 0;
            for (int j = jstart; j < str.Length; j++)
            {
                if (str[j] == '(')
                    leftmatch++;
                else if (str[j] == ')')
                    leftmatch--;
                if (leftmatch == 0)
                {
                    psoffset = i - start;
                    pboffset = j;
                    ptoffset = totchars + j;
                    return;
                }
            }
            if (i == firstline)
                totchars += str.Length - boffsetlp;
            else
                totchars += str.Length;
        }
    }
}
