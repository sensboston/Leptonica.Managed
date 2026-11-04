using System;
using System.IO;
using System.Text;

namespace Leptonica;

/*
 *  stringcode.c: generation of code for storing and extracting serializable
 *  leptonica objects (such as pixa, recog, ...).
 *
 *  The input is a set of files with serialized data.
 *  The output is two files, that must be compiled and linked:
 *    ~ autogen.*.c: code for base64 unencoding the strings and
 *                   deserializing the result.
 *    ~ autogen.*.h: function prototypes and base64 encoded strings
 *                   of the input data
 *
 *  This should work for any data structures in leptonica that have
 *  *Write() and *Read() serialization functions.  An array of 20
 *  of these, including the Pix, is given below.  (The Pix is a special
 *  case, because it is serialized by standardized compression
 *  techniques, instead of a file format determined by leptonica.)
 *
 *  Each time the generator function is invoked, three sets of strings are
 *  produced, which are written into their respective string arrays:
 *    ~ string of serialized, gzipped and base 64 encoded data
 *    ~ case string for base64 decoding, gunzipping and deserialization,
 *      to return the data struct in memory
 *    ~ description string for selecting which struct to return
 *  To create the two output files, a finalize function is invoked.
 *
 *  There are two ways to do this, both shown in prog/autogentest1.c.
 *    ~ Explicitly call strcodeGenerate() for each file with the
 *      serialized data structure, followed by strcodeFinalize().
 *    ~ Put the filenames of the serialized data structures in a file,
 *      and call strcodeCreateFromFile().
 *
 *  The generated code in autogen.X.c and autogen.X.h (where X is an
 *  integer supplied to strcodeCreate()) is then compiled, and the
 *  original data can be regenerated using the function l_autodecode_X().
 *  A test example is found in the two prog files:
 *      prog/autogentest1.c  -- generates autogen.137.c, autogen.137.h
 *      prog/autogentest2.c  -- uses autogen.137.c, autogen.137.h
 *  In general, the generator (e.g., autogentest1) would be compiled and
 *  run before compiling and running the application (e.g., autogentest2).
 *
 *      L_StrCode.Create (strcodeCreate)
 *      L_StrCode.CreateFromFile (strcodeCreateFromFile)
 *      L_StrCode.Generate (strcodeGenerate)
 *      L_StrCode.FinalizeCode (strcodeFinalize)
 *      StringCode.GetStructStrFromFile (l_getStructStrFromFile, useful externally)
 *
 *  Static helpers
 *      StringCode.GetIndexFromType (l_getIndexFromType)
 *      StringCode.GetIndexFromStructname (l_getIndexFromStructname)
 *      StringCode.GetIndexFromFile (l_getIndexFromFile)
 *      StringCode.GenDataString (l_genDataString)
 *      StringCode.GenCaseString (l_genCaseString)
 *      StringCode.GenDescrString (l_genDescrString)
 *
 *  Not ported: strcodeDestroy (memory management only).
 *
 *  Differences from C:
 *    - strcodeFinalize() is L_StrCode.FinalizeCode(), because Finalize is the
 *      name of the .NET destructor.
 *    - The templates stringtemplate1.txt and stringtemplate2.txt are read from
 *      the current directory, as in C.
 */

/// <summary>Constants from stringcode.h.</summary>
public static partial class L
{
    /* Select string in stringcode for a specific serializable data type */
    /// <summary>Selects the type definition of the data type.</summary>
    public const int L_STR_TYPE = 0;
    /// <summary>Selects the name of the data type.</summary>
    public const int L_STR_NAME = 1;
    /// <summary>Selects the reader that gets the data type from a file.</summary>
    public const int L_STR_READER = 2;
    /// <summary>Selects the reader that gets the data type from a compressed string in memory.</summary>
    public const int L_STR_MEMREADER = 3;
}

/// <summary>Accumulates the code for the two output files (struct L_StrCode).</summary>
public sealed class L_StrCode
{
    private const string TEMPLATE1 = "stringtemplate1.txt";  /* for assembling autogen.*.c */
    private const string TEMPLATE2 = "stringtemplate2.txt";  /* for assembling autogen.*.h */

    /// <summary>index for function and output file names</summary>
    public int Fileno { get; private set; }
    /// <summary>index into struct currently being stored</summary>
    public int Ifunc { get; private set; }
    /// <summary>store case code for extraction</summary>
    public Sarray Function { get; private set; }
    /// <summary>store base64 encoded data as strings</summary>
    public Sarray Data { get; private set; }
    /// <summary>store line in description table</summary>
    public Sarray Descr { get; private set; }
    /// <summary>number of data strings</summary>
    public int N { get; private set; }

    private L_StrCode(int fileno)
    {
        Fileno = fileno;
        Function = Sarray.Create(0);
        Data = Sarray.Create(0);
        Descr = Sarray.Create(0);
    }

    /*---------------------------------------------------------------------*/
    /*                         Stringcode functions                        */
    /*---------------------------------------------------------------------*/
    // leptonica: strcodeCreate (stringcode.c)
    /// <param name="fileno">integer that labels the two output files</param>
    /// <returns>initialized L_StrCode</returns>
    /// <remarks>
    /// (1) This struct exists to build two files containing code for
    ///     any number of data objects.  The two files are named
    ///        autogen.[fileno].c
    ///        autogen.[fileno].h
    /// </remarks>
    public static L_StrCode Create(int fileno)
    {
        Utils.LeptMkdir("lept/auto");

        return new L_StrCode(fileno);
    }

    // leptonica: strcodeCreateFromFile (stringcode.c)
    /// <param name="filein">containing filenames of serialized data</param>
    /// <param name="fileno">integer that labels the two output files</param>
    /// <param name="outdir">[optional] if null, files are made in /tmp/lept/auto</param>
    /// <remarks>
    /// (1) The %filein has one filename on each line.
    ///     Comment lines begin with "#".
    /// (2) The output is 2 files:
    ///        autogen.[fileno].c
    ///        autogen.[fileno].h
    /// </remarks>
    public static void CreateFromFile(string filein, int fileno, string? outdir)
    {
        byte[] data = Utils.BinaryRead(filein);
        Sarray sa = Sarray.CreateLinesFromString(Encoding.Latin1.GetString(data), 0);
        int n = sa.Count;
        if (n == 0)
            throw Lept.Fail("strcodeCreateFromFile", "no filenames in the file");

        L_StrCode strcode = Create(fileno);

        for (int i = 0; i < n; i++) {
            string fname = sa.GetString(i, L.L_NOCOPY);
            if (fname[0] == '#') continue;
            if (!StringCode.GetIndexFromFile(fname, out int index)) {
                Log.Error("strcodeCreateFromFile", $"File {fname} has no recognizable type");
            } else {
                string type = StringCode.Assoc[index].type;
                Log.Info("strcodeCreateFromFile", $"File {fname} is type {type}");
                strcode.Generate(fname, type);
            }
        }
        strcode.FinalizeCode(outdir);
    }

    // leptonica: strcodeGenerate (stringcode.c)
    /// <param name="filein">input file with serialized data</param>
    /// <param name="type">of data; use the typedef string</param>
    /// <remarks>
    /// (1) The generated function name is
    ///       l_autodecode_[fileno]()
    ///     where [fileno] is the index label for the pair of output files.
    /// (2) To deserialize this data, the function is called with the
    ///     argument 'ifunc', which increments each time strcodeGenerate()
    ///     is called.
    /// </remarks>
    public void Generate(string filein, string type)
    {
        L_StrCode strcode = this;

            /* Get the index corresponding to type and validate */
        if (!StringCode.GetIndexFromType(type, out int itype))
            throw Lept.Arg("strcodeGenerate", "data type unknown");

            /* Generate the encoded data string */
        string strdata = StringCode.GenDataString(filein, strcode.Ifunc);
        strcode.Data.AddString(strdata, L.L_INSERT);

            /* Generate the case data for the decoding function */
        string strfunc = StringCode.GenCaseString(strcode.Ifunc, itype);
        strcode.Function.AddString(strfunc, L.L_INSERT);

            /* Generate row of table for function type selection */
        string strdescr = StringCode.GenDescrString(filein, strcode.Ifunc, itype);
        strcode.Descr.AddString(strdescr, L.L_INSERT);

        strcode.N++;
        strcode.Ifunc++;
    }

    // leptonica: strcodeFinalize (stringcode.c)
    /// <summary>Writes the .c and .h files.</summary>
    /// <param name="outdir">[optional] if null, make files in /tmp/lept/auto</param>
    public void FinalizeCode(string? outdir)
    {
        L_StrCode strcode = this;
        string buf;

        Utils.LeptMkdir("lept/auto");

        string realoutdir;
        if (outdir == null) {
            Log.Info("strcodeFinalize", "no outdir specified; writing to /tmp/lept/auto");
            realoutdir = "/tmp/lept/auto";
        } else {
            realoutdir = outdir;
        }

        /* ------------------------------------------------------- */
        /*              Make the output autogen.*.c file           */
        /* ------------------------------------------------------- */

           /* Make array of textlines from TEMPLATE1 */
        string filestr = Encoding.Latin1.GetString(Utils.BinaryRead(TEMPLATE1));
        Sarray sa1 = Sarray.CreateLinesFromString(filestr, 1);
        Sarray sa3 = Sarray.Create(0);

            /* Copyright notice */
        sa1.ParseRange(0, out int actstart, out int end, out int newstart, "--", 0);
        sa3.AppendRange(sa1, actstart, end);

            /* File name comment */
        int fileno = strcode.Fileno;
        buf = $" *   autogen.{fileno}.c";
        sa3.AddString(buf, L.L_COPY);

            /* More text */
        sa1.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa1, actstart, end);

            /* Description of function types by index */
        string descr = strcode.Descr.ToString(1);
        descr = descr.Substring(0, descr.Length - 1);
        sa3.AddString(descr, L.L_INSERT);

            /* Includes */
        sa1.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa1, actstart, end);
        buf = $"#include \"autogen.{fileno}.h\"";
        sa3.AddString(buf, L.L_COPY);

            /* Header for auto-generated deserializers */
        sa1.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa1, actstart, end);

            /* Function name (as comment) */
        buf = $" * \\brief  l_autodecode_{fileno}()";
        sa3.AddString(buf, L.L_COPY);

            /* Input and return values */
        sa1.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa1, actstart, end);

            /* Function name */
        buf = $"l_autodecode_{fileno}(l_int32 index)";
        sa3.AddString(buf, L.L_COPY);

            /* Stack vars */
        sa1.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa1, actstart, end);

            /* Declaration of nfunc on stack */
        buf = $"l_int32   nfunc = {strcode.N};\n";
        sa3.AddString(buf, L.L_COPY);

            /* Declaration of PROCNAME */
        buf = $"    PROCNAME(\"l_autodecode_{fileno}\");";
        sa3.AddString(buf, L.L_COPY);

            /* Test input variables */
        sa1.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa1, actstart, end);

            /* Insert case string */
        string casestr = strcode.Function.ToString(0);
        casestr = casestr.Substring(0, casestr.Length - 1);
        sa3.AddString(casestr, L.L_INSERT);

            /* End of function */
        sa1.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa1, actstart, end);

            /* Flatten to string and output to autogen*.c file */
        filestr = sa3.ToString(1);
        buf = $"{realoutdir}/autogen.{fileno}.c";
        Utils.BinaryWrite(buf, "w", Encoding.Latin1.GetBytes(filestr));

        /* ------------------------------------------------------- */
        /*              Make the output autogen.*.h file           */
        /* ------------------------------------------------------- */

           /* Make array of textlines from TEMPLATE2 */
        filestr = Encoding.Latin1.GetString(Utils.BinaryRead(TEMPLATE2));
        Sarray sa2 = Sarray.CreateLinesFromString(filestr, 1);
        sa3 = Sarray.Create(0);

            /* Copyright notice */
        sa2.ParseRange(0, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);

            /* File name comment */
        buf = $" *   autogen.{fileno}.h";
        sa3.AddString(buf, L.L_COPY);

            /* More text */
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);

            /* Beginning header protection */
        buf = $"#ifndef  LEPTONICA_AUTOGEN_{fileno}_H\n" +
              $"#define  LEPTONICA_AUTOGEN_{fileno}_H";
        sa3.AddString(buf, L.L_COPY);

            /* Prototype header text */
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);

            /* Prototype declaration */
        buf = $"void *l_autodecode_{fileno}(l_int32 index);";
        sa3.AddString(buf, L.L_COPY);

            /* Prototype trailer text */
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);

            /* Insert serialized data strings */
        string datastr = strcode.Data.ToString(1);
        datastr = datastr.Substring(0, datastr.Length - 1);
        sa3.AddString(datastr, L.L_INSERT);

            /* End header protection */
        buf = $"#endif  /* LEPTONICA_AUTOGEN_{fileno}_H */";
        sa3.AddString(buf, L.L_COPY);

            /* Flatten to string and output to autogen*.h file */
        filestr = sa3.ToString(1);
        buf = $"{realoutdir}/autogen.{fileno}.h";
        Utils.BinaryWrite(buf, "w", Encoding.Latin1.GetBytes(filestr));
    }
}

/// <summary>Lookup helpers that map serialized Leptonica data types to their names and reader functions.</summary>
public static class StringCode
{
    /// <summary>Associations between names and functions (struct L_GenAssoc).</summary>
    internal readonly record struct GenAssoc(int index, string type, string structname,
                                             string reader, string memreader);

    /// <summary>Number of serializable data types</summary>
    private const int l_ntypes = 19;

    /// <summary>Serializable data types</summary>
    internal static readonly GenAssoc[] Assoc = {
        new(0,  "INVALID",     "invalid",     "invalid",        "invalid"         ),
        new(1,  "BOXA",        "Boxa",        "boxaRead",       "boxaReadMem"     ),
        new(2,  "BOXAA",       "Boxaa",       "boxaaRead",      "boxaaReadMem"    ),
        new(3,  "L_DEWARP",    "Dewarp",      "dewarpRead",     "dewarpReadMem"   ),
        new(4,  "L_DEWARPA",   "Dewarpa",     "dewarpaRead",    "dewarpaReadMem"  ),
        new(5,  "L_DNA",       "L_Dna",       "l_dnaRead",      "l_dnaReadMem"    ),
        new(6,  "L_DNAA",      "L_Dnaa",      "l_dnaaRead",     "l_dnaaReadMem"   ),
        new(7,  "DPIX",        "DPix",        "dpixRead",       "dpixReadMem"     ),
        new(8,  "FPIX",        "FPix",        "fpixRead",       "fpixReadMem"     ),
        new(9,  "NUMA",        "Numa",        "numaRead",       "numaReadMem"     ),
        new(10, "NUMAA",       "Numaa",       "numaaRead",      "numaaReadMem"    ),
        new(11, "PIX",         "Pix",         "pixRead",        "pixReadMem"      ),
        new(12, "PIXA",        "Pixa",        "pixaRead",       "pixaReadMem"     ),
        new(13, "PIXAA",       "Pixaa",       "pixaaRead",      "pixaaReadMem"    ),
        new(14, "PIXACOMP",    "Pixacomp",    "pixacompRead",   "pixacompReadMem" ),
        new(15, "PIXCMAP",     "Pixcmap",     "pixcmapRead",    "pixcmapReadMem"  ),
        new(16, "PTA",         "Pta",         "ptaRead",        "ptaReadMem"      ),
        new(17, "PTAA",        "Ptaa",        "ptaaRead",       "ptaaReadMem"     ),
        new(18, "RECOG",       "Recog",       "recogRead",      "recogReadMem"    ),
        new(19, "SARRAY",      "Sarray",      "sarrayRead",     "sarrayReadMem"   )
    };

    // leptonica: l_getStructStrFromFile (stringcode.c)
    /// <summary>Gets the requested descriptive string for the serialized data type stored in a file.</summary>
    /// <param name="filename">Path of the serialized file.</param>
    /// <param name="field">One of <see cref="L.L_STR_TYPE"/>, <see cref="L.L_STR_NAME"/>,
    /// <see cref="L.L_STR_READER"/> or <see cref="L.L_STR_MEMREADER"/>.</param>
    /// <returns>The requested string for the data type in the file.</returns>
    /// <exception cref="ArgumentException"><paramref name="field"/> is not a valid selector.</exception>
    /// <remarks>
    /// For example, if <paramref name="field"/> is <see cref="L.L_STR_NAME"/> and the file is a
    /// serialized <c>Pixa</c>, this returns "Pixa", the name of the struct.
    /// </remarks>
    public static string GetStructStrFromFile(string filename, int field)
    {
        if (field != L.L_STR_TYPE && field != L.L_STR_NAME &&
            field != L.L_STR_READER && field != L.L_STR_MEMREADER)
            throw Lept.Arg("l_getStructStrFromFile", "invalid field");

        if (!GetIndexFromFile(filename, out int index))
            throw Lept.Fail("l_getStructStrFromFile", "index not retrieved");
        if (field == L.L_STR_TYPE)
            return Assoc[index].type;
        else if (field == L.L_STR_NAME)
            return Assoc[index].structname;
        else if (field == L.L_STR_READER)
            return Assoc[index].reader;
        else   /* field == L_STR_MEMREADER */
            return Assoc[index].memreader;
    }

    /*---------------------------------------------------------------------*/
    /*                           Static helpers                            */
    /*---------------------------------------------------------------------*/
    // leptonica: l_getIndexFromType (stringcode.c)
    /// <param name="type">e.g., "PIXA"</param>
    /// <param name="index">found index</param>
    /// <returns>true if found, false if not (C: 0 if found, 1 if not)</returns>
    /// <remarks>
    /// (1) For valid type, %found == true and %index > 0.
    /// </remarks>
    internal static bool GetIndexFromType(string type, out int index)
    {
        index = 0;
        bool found = false;
        for (int i = 1; i <= l_ntypes; i++) {
            if (type == Assoc[i].type) {
                found = true;
                index = i;
                break;
            }
        }
        return found;
    }

    // leptonica: l_getIndexFromStructname (stringcode.c)
    /// <param name="sn">structname e.g., "Pixa"</param>
    /// <param name="index">found index</param>
    /// <returns>true if found, false if not (C: 0 if found, 1 if not)</returns>
    /// <remarks>
    /// (1) This is used to identify the type of serialized file;
    ///     the first word in the file is the structname.
    /// (2) For valid structname, %found == true and %index > 0.
    /// </remarks>
    internal static bool GetIndexFromStructname(string sn, out int index)
    {
        index = 0;
        bool found = false;
        for (int i = 1; i <= l_ntypes; i++) {
            if (sn == Assoc[i].structname) {
                found = true;
                index = i;
                break;
            }
        }
        return found;
    }

    // leptonica: l_getIndexFromFile (stringcode.c)
    /// <param name="filename"></param>
    /// <param name="index">found index</param>
    /// <returns>true if found, false on error (C: 0 if found, 1 on error)</returns>
    internal static bool GetIndexFromFile(string filename, out int index)
    {
        const string func = "l_getIndexFromFile";

        index = 0;

            /* Open the stream, read lines until you find one with more
             * than a newline, and grab the first word. */
        string buf;
        try {
            using FileStream fp = Utils.FopenReadStream(filename);
            do {
                buf = ReadLine(fp, 256) ?? throw Lept.Fail(func, $"fgets read fail: {filename}");
            } while (buf.Length > 0 && buf[0] == '\n');
        } catch (LeptonicaException e) {
            Log.Error(func, e.Message);
            return false;
        }
        Sarray sa = Sarray.CreateWordsFromString(buf);
        string word = sa.GetString(0, L.L_NOCOPY);

            /* Find the index associated with the word.  If it is not
             * found, test to see if the file is a compressed pix. */
        bool found = GetIndexFromStructname(word, out index);
        if (!found) {  /* maybe a Pix */
            if (Pix.FindFileFormat(filename, out _))
                GetIndexFromStructname("Pix", out index);
            else {
                Log.Error(func, $"no file type identified: {filename}");
                return false;
            }
        }

        return true;
    }

    /// <summary>fgets(buf, size, fp): reads up to size - 1 characters, including the newline; null at EOF.</summary>
    private static string? ReadLine(Stream fp, int size)
    {
        var sb = new StringBuilder();
        while (sb.Length < size - 1) {
            int b = fp.ReadByte();
            if (b < 0)
                break;
            sb.Append((char)b);
            if (b == '\n')
                break;
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    // leptonica: l_genDataString (stringcode.c)
    /// <param name="filein">input file of serialized data</param>
    /// <param name="ifunc">index into set of functions in output file</param>
    /// <returns>encoded ascii data string</returns>
    internal static string GenDataString(string filein, int ifunc)
    {
            /* Read it in, gzip it, encode, and reformat.  We gzip because some
             * serialized data has a significant amount of ascii content. */
        byte[] data1 = Utils.BinaryRead(filein);
        byte[] data2 = ZlibMem.ZlibCompress(data1);
        string cdata1 = LeptEncoding.EncodeBase64(data2);
        string cdata2 = LeptEncoding.ReformatPacked64(cdata1, 4, 72, true);

            /* Prepend the string declaration signature and put it together */
        Sarray sa = Sarray.Create(3);
        string buf = $"static const char *l_strdata_{ifunc} =\n";
        sa.AddString(buf, L.L_COPY);
        sa.AddString(cdata2, L.L_INSERT);
        sa.AddString(";\n", L.L_COPY);
        return sa.ToString(0);
    }

    // leptonica: l_genCaseString (stringcode.c)
    /// <param name="ifunc">index into set of functions in generated file</param>
    /// <param name="itype">index into type of function to be used</param>
    /// <returns>case string for this decoding function</returns>
    /// <remarks>
    /// (1) %ifunc and %itype have been validated, so no error can occur
    /// </remarks>
    internal static string GenCaseString(int ifunc, int itype)
    {
        string? code = null;

        string buf = $"    case {ifunc}:\n";
        Utils.StringJoinIP(ref code, buf);
        buf = $"        data1 = decodeBase64(l_strdata_{ifunc}, strlen(l_strdata_{ifunc}), " +
              "&size1);\n";
        Utils.StringJoinIP(ref code, buf);
        Utils.StringJoinIP(ref code,
                           "        data2 = zlibUncompress(data1, size1, &size2);\n");
        buf = $"        result = (void *){Assoc[itype].memreader}(data2, size2);\n";
        Utils.StringJoinIP(ref code, buf);
        Utils.StringJoinIP(ref code, "        lept_free(data1);\n");
        Utils.StringJoinIP(ref code, "        lept_free(data2);\n");
        Utils.StringJoinIP(ref code, "        break;\n");
        return code!;
    }

    // leptonica: l_genDescrString (stringcode.c)
    /// <param name="filein">input file of serialized data</param>
    /// <param name="ifunc">index into set of functions in generated file</param>
    /// <param name="itype">index into type of function to be used</param>
    /// <returns>description string for this decoding function</returns>
    internal static string GenDescrString(string filein, int ifunc, int itype)
    {
        Utils.SplitPathAtDirectory(filein, out _, out string tail);
        return $" *     {ifunc,-2}       {Assoc[itype].type,-10}    {Assoc[itype].reader,-14}   {tail}";
    }
}
