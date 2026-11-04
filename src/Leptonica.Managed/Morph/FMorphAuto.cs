using System;
using System.IO;
using System.Text;

namespace Leptonica;

/*
 *  fmorphauto.c: Main function calls:
 *       Sela.FMorphAutoGen, FMorphAutoGen1, FMorphAutoGen2
 *
 *  Static helpers:
 *       SarrayMakeWplsCode, SarrayMakeInnerLoopDWACode, MakeBarrelshiftString
 *
 *  This automatically generates dwa code for erosion and dilation.
 *  Here's a typical use:
 *
 *       Sela sela = Sela.AddBasic(null);
 *       sela.FMorphAutoGen(1, "fmorphgen");
 *
 *  It generates two files of C# code, fmorphgen.1.cs and fmorphgenlow.1.cs,
 *  with the same content as the C files of the C library (the low-level
 *  code works on uint* like the C code; the barrel shift expressions
 *  are textually the same).  tools/DwaGen makes the files that are part of
 *  this library.
 *
 *  Differences from C: the output is C#; the templates morphtemplate1.txt and
 *  morphtemplate2.txt are C# versions, embedded in the assembly instead of
 *  being read from the current directory; no prototypes are written.
 */
public sealed partial class Sela
{
    private const string OUTROOT = "fmorphgen";
    private const string TEMPLATE1 = "morphtemplate1.txt";
    private const string TEMPLATE2 = "morphtemplate2.txt";

    private static readonly string[] wpldecls = {
                "        int wpls2;",
                "        int wpls2, wpls3;",
                "        int wpls2, wpls3, wpls4;",
                "        int wpls5;",
                "        int wpls5, wpls6;",
                "        int wpls5, wpls6, wpls7;",
                "        int wpls5, wpls6, wpls7, wpls8;",
                "        int wpls9;",
                "        int wpls9, wpls10;",
                "        int wpls9, wpls10, wpls11;",
                "        int wpls9, wpls10, wpls11, wpls12;",
                "        int wpls13;",
                "        int wpls13, wpls14;",
                "        int wpls13, wpls14, wpls15;",
                "        int wpls13, wpls14, wpls15, wpls16;",
                "        int wpls17;",
                "        int wpls17, wpls18;",
                "        int wpls17, wpls18, wpls19;",
                "        int wpls17, wpls18, wpls19, wpls20;",
                "        int wpls21;",
                "        int wpls21, wpls22;",
                "        int wpls21, wpls22, wpls23;",
                "        int wpls21, wpls22, wpls23, wpls24;",
                "        int wpls25;",
                "        int wpls25, wpls26;",
                "        int wpls25, wpls26, wpls27;",
                "        int wpls25, wpls26, wpls27, wpls28;",
                "        int wpls29;",
                "        int wpls29, wpls30;",
                "        int wpls29, wpls30, wpls31;"};

    private static readonly string[] wplgendecls = {
                "        int wpls2;",
                "        int wpls3;",
                "        int wpls4;",
                "        int wpls5;",
                "        int wpls6;",
                "        int wpls7;",
                "        int wpls8;",
                "        int wpls9;",
                "        int wpls10;",
                "        int wpls11;",
                "        int wpls12;",
                "        int wpls13;",
                "        int wpls14;",
                "        int wpls15;",
                "        int wpls16;",
                "        int wpls17;",
                "        int wpls18;",
                "        int wpls19;",
                "        int wpls20;",
                "        int wpls21;",
                "        int wpls22;",
                "        int wpls23;",
                "        int wpls24;",
                "        int wpls25;",
                "        int wpls26;",
                "        int wpls27;",
                "        int wpls28;",
                "        int wpls29;",
                "        int wpls30;",
                "        int wpls31;"};

    private static readonly string[] wpldefs = {
                "        wpls2 = 2 * wpls;",
                "        wpls3 = 3 * wpls;",
                "        wpls4 = 4 * wpls;",
                "        wpls5 = 5 * wpls;",
                "        wpls6 = 6 * wpls;",
                "        wpls7 = 7 * wpls;",
                "        wpls8 = 8 * wpls;",
                "        wpls9 = 9 * wpls;",
                "        wpls10 = 10 * wpls;",
                "        wpls11 = 11 * wpls;",
                "        wpls12 = 12 * wpls;",
                "        wpls13 = 13 * wpls;",
                "        wpls14 = 14 * wpls;",
                "        wpls15 = 15 * wpls;",
                "        wpls16 = 16 * wpls;",
                "        wpls17 = 17 * wpls;",
                "        wpls18 = 18 * wpls;",
                "        wpls19 = 19 * wpls;",
                "        wpls20 = 20 * wpls;",
                "        wpls21 = 21 * wpls;",
                "        wpls22 = 22 * wpls;",
                "        wpls23 = 23 * wpls;",
                "        wpls24 = 24 * wpls;",
                "        wpls25 = 25 * wpls;",
                "        wpls26 = 26 * wpls;",
                "        wpls27 = 27 * wpls;",
                "        wpls28 = 28 * wpls;",
                "        wpls29 = 29 * wpls;",
                "        wpls30 = 30 * wpls;",
                "        wpls31 = 31 * wpls;"};

    private static readonly string[] wplstrp = {"+ wpls", "+ wpls2", "+ wpls3", "+ wpls4",
                                 "+ wpls5", "+ wpls6", "+ wpls7", "+ wpls8",
                                 "+ wpls9", "+ wpls10", "+ wpls11", "+ wpls12",
                                 "+ wpls13", "+ wpls14", "+ wpls15", "+ wpls16",
                                 "+ wpls17", "+ wpls18", "+ wpls19", "+ wpls20",
                                 "+ wpls21", "+ wpls22", "+ wpls23", "+ wpls24",
                                 "+ wpls25", "+ wpls26", "+ wpls27", "+ wpls28",
                                 "+ wpls29", "+ wpls30", "+ wpls31"};

    private static readonly string[] wplstrm = {"- wpls", "- wpls2", "- wpls3", "- wpls4",
                                 "- wpls5", "- wpls6", "- wpls7", "- wpls8",
                                 "- wpls9", "- wpls10", "- wpls11", "- wpls12",
                                 "- wpls13", "- wpls14", "- wpls15", "- wpls16",
                                 "- wpls17", "- wpls18", "- wpls19", "- wpls20",
                                 "- wpls21", "- wpls22", "- wpls23", "- wpls24",
                                 "- wpls25", "- wpls26", "- wpls27", "- wpls28",
                                 "- wpls29", "- wpls30", "- wpls31"};

    // leptonica: fmorphautogen (fmorphauto.c)
    /// <summary>Generates the two files of dwa code for the sels in this Sela.</summary>
    /// <param name="fileindex">0 for no index; otherwise, use 1 ...</param>
    /// <param name="filename">root of output filename; use null for default</param>
    /// <remarks>
    /// (1) This function generates all the code for implementing
    ///     dwa morphological operations using all the sels in the sela.
    /// (2) See FMorphAutoGen1() and FMorphAutoGen2() for details.
    /// </remarks>
    public void FMorphAutoGen(int fileindex, string? filename)
    {
        FMorphAutoGen1(fileindex, filename);
        FMorphAutoGen2(fileindex, filename);
    }

    // leptonica: fmorphautogen1 (fmorphauto.c)
    /// <summary>Generates the top-level file of dwa code.</summary>
    /// <param name="fileindex">0 for no index; otherwise, use 1 ...</param>
    /// <param name="filename">root of output filename; use null for default</param>
    /// <remarks>
    /// (1) If filename == null, the output file is fmorphgen.[n].cs,
    ///     where [n] is equal to the %fileindex parameter.
    /// (2) If filename != null, the output file is [filename].[n].cs.
    /// (3) This generates the top-level functions Pix.MorphDwa_[n] and
    ///     Pix.FMorphopGen_[n].
    /// </remarks>
    public void FMorphAutoGen1(int fileindex, string? filename)
    {
        if (fileindex < 0)
            fileindex = 0;
        string filestr = FMorphAutoGen1Text(fileindex, filename);
        string outname = $"{filename ?? OUTROOT}.{fileindex}.cs";
        Utils.BinaryWrite(outname, "w", Encoding.ASCII.GetBytes(filestr));
    }

    /// <summary>The text written by <see cref="FMorphAutoGen1"/>.</summary>
    internal string FMorphAutoGen1Text(int fileindex, string? filename)
    {
        const string func = "fmorphautogen1";
        Sela sela = this;
        int i, nsels, actstart, end, newstart;
        string str_doc1, str_doc2, str_doc3, str_doc4;
        string str_def1, str_def2, str_proc1, str_proc2;
        string str_dwa1, str_low_dt, str_low_ds, str_low_ts;
        string str_low_tsp1, str_low_dtp1;
        Sarray sa1, sa2, sa3;

        if (fileindex < 0)
            fileindex = 0;
        if ((nsels = sela.Count) == 0)
            throw Lept.Arg(func, "no sels in sela");
        string cname = $"{filename ?? OUTROOT}.{fileindex}.c";

            /* Make array of textlines from morphtemplate1.txt */
        sa2 = Sarray.CreateLinesFromString(ReadTemplate(TEMPLATE1), 1);

            /* Make array of sel names */
        sa1 = sela.GetSelnames();

            /* Make strings containing function call names */
        str_doc1 = $" *            Pix.MorphDwa_{fileindex}()";
        str_doc2 = $" *            Pix.FMorphopGen_{fileindex}()";
        str_doc3 = $"    // leptonica: pixMorphDwa_{fileindex} ({cname})";
        str_doc4 = $"    // leptonica: pixFMorphopGen_{fileindex} ({cname})";
        str_def1 = $"    public static Pix MorphDwa_{fileindex}(Pix? pixd, Pix pixs, int operation, string selname)";
        str_def2 = $"    public static unsafe Pix FMorphopGen_{fileindex}(Pix? pixd, Pix pixs, int operation, string selname)";
        str_proc1 = $"        const string func = \"pixMorphDwa_{fileindex}\";";
        str_proc2 = $"        const string func = \"pixFMorphopGen_{fileindex}\";";
        str_dwa1 = $"        pixt2 = FMorphopGen_{fileindex}(null, pixt1, operation, selname);";
        str_low_dt =
            $"                        DwaLow.fmorphopgen_low_{fileindex}(datad, w, h, wpld, datat, wpls, index);";
        str_low_ds =
            $"                        DwaLow.fmorphopgen_low_{fileindex}(datad, w, h, wpld, datas, wpls, index);";
        str_low_tsp1 =
            $"                        DwaLow.fmorphopgen_low_{fileindex}(datat, w, h, wpls, datas, wpls, index + 1);";
        str_low_ts =
            $"                        DwaLow.fmorphopgen_low_{fileindex}(datat, w, h, wpls, datas, wpls, index);";
        str_low_dtp1 =
            $"                        DwaLow.fmorphopgen_low_{fileindex}(datad, w, h, wpld, datat, wpls, index + 1);";

            /* Make the output sa */
        sa3 = Sarray.Create(0);

            /* Copyright notice and info header */
        sa2.ParseRange(0, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);

            /* Insert function names as documentation */
        sa3.AddString(str_doc1, L.L_INSERT);
        sa3.AddString(str_doc2, L.L_INSERT);

            /* Add 'using's */
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);

            /* Add static globals (no function prototypes in C#); a file-local
             * class keeps the names of each generated file apart */
        sa3.AddString("file static class SelNames", L.L_COPY);
        sa3.AddString("{", L.L_COPY);
        sa3.AddString($"    public const int NUM_SELS_GENERATED = {nsels};", L.L_COPY);
        sa3.AddString("    public static readonly string[] SEL_NAMES = {", L.L_COPY);
        for (i = 0; i < nsels - 1; i++)
            sa3.AddString($"                             \"{sa1.GetString(i, L.L_NOCOPY)}\",", L.L_COPY);
        sa3.AddString($"                             \"{sa1.GetString(i, L.L_NOCOPY)}\"}};", L.L_COPY);
        sa3.AddString("    public static readonly FrozenDictionary<string, int> SEL_INDEX = BuildIndex();", L.L_COPY);
        sa3.AddString("    private static FrozenDictionary<string, int> BuildIndex()", L.L_COPY);
        sa3.AddString("    {", L.L_COPY);
        sa3.AddString("        var d = new Dictionary<string, int>(NUM_SELS_GENERATED, StringComparer.Ordinal);", L.L_COPY);
        sa3.AddString("        for (int k = 0; k < NUM_SELS_GENERATED; k++) d[SEL_NAMES[k]] = k;", L.L_COPY);
        sa3.AddString("        return d.ToFrozenDictionary(StringComparer.Ordinal);", L.L_COPY);
        sa3.AddString("    }", L.L_COPY);
        sa3.AddString("}", L.L_COPY);

            /* Start pixMorphDwa_*() function description */
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);
        sa3.AddString(str_doc3, L.L_INSERT);
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);

            /* Finish pixMorphDwa_*() function definition */
        sa3.AddString(str_def1, L.L_INSERT);
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);
        sa3.AddString(str_proc1, L.L_INSERT);
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);
        sa3.AddString(str_dwa1, L.L_INSERT);
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);

            /* Start pixFMorphopGen_*() function description */
        sa3.AddString(str_doc4, L.L_INSERT);
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);

            /* Finish pixFMorphopGen_*() function definition */
        sa3.AddString(str_def2, L.L_INSERT);
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);
        sa3.AddString(str_proc2, L.L_INSERT);
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);
        sa3.AddString(str_low_dt, L.L_COPY);
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);
        sa3.AddString(str_low_ds, L.L_INSERT);
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);
        sa3.AddString(str_low_tsp1, L.L_INSERT);
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);
        sa3.AddString(str_low_dt, L.L_INSERT);
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);
        sa3.AddString(str_low_ts, L.L_INSERT);
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);
        sa3.AddString(str_low_dtp1, L.L_INSERT);
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);

        return sa3.ToString(1);
    }

    // leptonica: fmorphautogen2 (fmorphauto.c)
    /// <summary>Generates the low-level file of dwa code.</summary>
    /// <param name="fileindex">0 for no index; otherwise, use 1 ...</param>
    /// <param name="filename">root of output filename; use null for default</param>
    /// <remarks>
    /// (1) If filename == null, the output file is fmorphgenlow.[n].cs,
    ///     where [n] is equal to the %fileindex parameter.
    /// (2) If filename != null, the output file is [filename]low.[n].cs.
    /// </remarks>
    public void FMorphAutoGen2(int fileindex, string? filename)
    {
        if (fileindex < 0)
            fileindex = 0;
        string filestr = FMorphAutoGen2Text(fileindex, filename);
        string outname = $"{filename ?? OUTROOT}low.{fileindex}.cs";
        Utils.BinaryWrite(outname, "w", Encoding.ASCII.GetBytes(filestr));
    }

    /// <summary>The text written by <see cref="FMorphAutoGen2"/>.</summary>
    internal string FMorphAutoGen2Text(int fileindex, string? filename)
    {
        const string func = "fmorphautogen2";
        Sela sela = this;
        string fname;
        string str_doc1, str_doc2, str_doc3, str_doc4, str_def1;
        const string breakstring = "            break;";
        int i, nsels, actstart, end, newstart;
        int argstart, argend, loopstart, loopend, finalstart, finalend;
        Sarray sa1, sa2, sa4, sa5, sa6;
        Sel sel;

        if (fileindex < 0)
            fileindex = 0;
        if ((nsels = sela.Count) == 0)
            throw Lept.Arg(func, "no sels in sela");
        string cname = $"{filename ?? OUTROOT}low.{fileindex}.c";

            /* Make the array of textlines from morphtemplate2.txt */
        sa1 = Sarray.CreateLinesFromString(ReadTemplate(TEMPLATE2), 1);

            /* Make the array of static function names */
        sa2 = Sarray.Create(2 * nsels);
        for (i = 0; i < nsels; i++) {
            sa2.AddString($"fdilate_{fileindex}_{i}", L.L_COPY);
            sa2.AddString($"ferode_{fileindex}_{i}", L.L_COPY);
        }

            /* Make strings containing function names */
        str_doc1 = $" *            fmorphopgen_low_{fileindex}()";
        str_doc2 = $" *            fdilate_{fileindex}_*()";
        str_doc3 = $" *            ferode_{fileindex}_*()";

            /* Output to this sa */
        sa4 = Sarray.Create(0);

            /* Copyright notice and info header */
        sa1.ParseRange(0, out actstart, out end, out newstart, "--", 0);
        sa4.AppendRange(sa1, actstart, end);

            /* Insert function names as documentation */
        sa4.AddString(str_doc1, L.L_INSERT);
        sa1.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa4.AppendRange(sa1, actstart, end);
        sa4.AddString(str_doc2, L.L_INSERT);
        sa4.AddString(str_doc3, L.L_INSERT);
        sa1.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa4.AppendRange(sa1, actstart, end);

            /* (no static protos in C#) */

            /* More strings with function names */
        str_doc4 = $"     *  fmorphopgen_low_{fileindex}()";
        str_def1 = $"    internal static int fmorphopgen_low_{fileindex}(uint* datad, int w, int h, int wpld,";

            /* Insert function header */
        sa1.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa4.AppendRange(sa1, actstart, end);
        sa4.AddString(str_doc4, L.L_INSERT);
        sa1.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa4.AppendRange(sa1, actstart, end);
        sa4.AddString($"    // leptonica: fmorphopgen_low_{fileindex} ({cname})", L.L_COPY);
        sa4.AddString(str_def1, L.L_INSERT);
        sa1.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa4.AppendRange(sa1, actstart, end);

            /* Generate and insert the dispatcher code */
        for (i = 0; i < 2 * nsels; i++) {
            sa4.AddString($"        case {i}:", L.L_COPY);
            sa4.AddString($"            {sa2.GetString(i, L.L_NOCOPY)}(datad, w, h, wpld, datas, wpls);",
                          L.L_COPY);
            sa4.AddString(breakstring, L.L_COPY);
        }

            /* Finish the dispatcher and introduce the low-level code */
        sa1.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa4.AppendRange(sa1, actstart, end);

            /* Get the range for the args common to all functions */
        sa1.ParseRange(newstart, out argstart, out argend, out newstart, "--", 0);

            /* Get the range for the loop code common to all functions */
        sa1.ParseRange(newstart, out loopstart, out loopend, out newstart, "--", 0);

            /* Get the range for the ending code common to all functions */
        sa1.ParseRange(newstart, out finalstart, out finalend, out newstart, "--", 0);

            /* Do all the static functions */
        for (i = 0; i < 2 * nsels; i++) {
                /* Generate the function header and add the common args */
            fname = sa2.GetString(i, L.L_NOCOPY);
            sa4.AddString($"    // leptonica: {fname} ({cname})", L.L_COPY);
            sa4.AddString($"    private static void {fname}(uint* datad,", L.L_COPY);
            sa4.AppendRange(sa1, argstart, argend);

                /* Declare and define wplsN args, as necessary */
            sel = sela.GetSel(i / 2);
            sa5 = SarrayMakeWplsCode(sel);
            sa4.Join(sa5);

                /* Add the function loop code */
            sa4.AppendRange(sa1, loopstart, loopend);

                /* Insert barrel-op code for *dptr */
            sa6 = SarrayMakeInnerLoopDWACode(sel, i);
            sa4.Join(sa6);

                /* Finish the function code */
            sa4.AppendRange(sa1, finalstart, finalend);
        }
        sa4.AddString("}", L.L_COPY);  /* end of the class */

        return sa4.ToString(1);
    }

    /*--------------------------------------------------------------------------*
     *                            Helper code for sel                           *
     *--------------------------------------------------------------------------*/
    // leptonica: sarrayMakeWplsCode (fmorphauto.c)
    private static Sarray SarrayMakeWplsCode(Sel sel)
    {
        const string func = "sarrayMakeWplsCode";
        const string emptystring = "";
        int i, j, ymax, dely;
        bool allvshifts;
        int[] vshift = new int[32];
        Sarray sa;

        for (i = 0; i < 32; i++)
            vshift[i] = 0;
        ymax = 0;
        for (i = 0; i < sel.Sy; i++) {
            for (j = 0; j < sel.Sx; j++) {
                if (sel.Data[i][j] == 1) {
                    dely = Math.Abs(i - sel.Cy);
                    if (dely < 32)
                        vshift[dely] = 1;
                    ymax = Math.Max(ymax, dely);
                }
            }
        }
        if (ymax > 31) {
            Log.Warning(func, "ymax > 31; truncating to 31\n");
            ymax = 31;
        }

            /* Test if this is a vertical brick */
        allvshifts = true;
        for (i = 0; i < ymax; i++) {
            if (vshift[i] == 0) {
                allvshifts = false;
                break;
            }
        }

        sa = Sarray.Create(0);

            /* Add declarations */
        if (allvshifts == true) {   /* packs them as well as possible */
            if (ymax > 4)
                sa.AddString(wpldecls[2], L.L_COPY);
            if (ymax > 8)
                sa.AddString(wpldecls[6], L.L_COPY);
            if (ymax > 12)
                sa.AddString(wpldecls[10], L.L_COPY);
            if (ymax > 16)
                sa.AddString(wpldecls[14], L.L_COPY);
            if (ymax > 20)
                sa.AddString(wpldecls[18], L.L_COPY);
            if (ymax > 24)
                sa.AddString(wpldecls[22], L.L_COPY);
            if (ymax > 28)
                sa.AddString(wpldecls[26], L.L_COPY);
            if (ymax > 1)
                sa.AddString(wpldecls[ymax - 2], L.L_COPY);
        } else {  /* puts them one/line */
            for (i = 2; i <= ymax; i++) {
                if (vshift[i] != 0)
                    sa.AddString(wplgendecls[i - 2], L.L_COPY);
            }
        }

        sa.AddString(emptystring, L.L_COPY);

            /* Add definitions */
        for (i = 2; i <= ymax; i++) {
            if (vshift[i] != 0)
                sa.AddString(wpldefs[i - 2], L.L_COPY);
        }

        return sa;
    }

    // leptonica: sarrayMakeInnerLoopDWACode (fmorphauto.c)
    private static Sarray SarrayMakeInnerLoopDWACode(Sel sel, int index)
    {
        const string func = "sarrayMakeInnerLoopDWACode";
        string tstr;
        string? str;
        const string logicalor = "|";
        const string logicaland = "&";
        int i, j, optype, count, nfound, delx, dely;
        Sarray sa;

        if (index % 2 == 0) {
            optype = L.L_MORPH_DILATE;
            tstr = logicalor;
        } else {
            optype = L.L_MORPH_ERODE;
            tstr = logicaland;
        }

        count = 0;
        for (i = 0; i < sel.Sy; i++) {
            for (j = 0; j < sel.Sx; j++) {
                if (sel.Data[i][j] == 1)
                    count++;
            }
        }

        sa = Sarray.Create(0);
        if (count == 0) {
            Log.Warning(func, $"no hits in Sel {index}\n");
            sa.AddString("            for (; j < pwpls; j++, sptr++, dptr++) { }", L.L_COPY);
            return sa;
        }

        sa.AddString("            if (Vector256.IsHardwareAccelerated) {", L.L_COPY);
        sa.AddString("                for (; j + 8 <= pwpls; j += 8, sptr += 8, dptr += 8) {", L.L_COPY);
        nfound = 0;
        for (i = 0; i < sel.Sy; i++) {
            for (j = 0; j < sel.Sx; j++) {
                if (sel.Data[i][j] == 1) {
                    nfound++;
                    if (optype == L.L_MORPH_DILATE) {
                        dely = sel.Cy - i;
                        delx = sel.Cx - j;
                    } else {
                        dely = i - sel.Cy;
                        delx = j - sel.Cx;
                    }
                    string? vstr = MakeBarrelshiftVecString(delx, dely);
                    if (vstr == null)
                        continue;
                    string vline;
                    if (count == 1)
                        vline = $"                    Vector256.Store({vstr}, dptr);";
                    else if (nfound == 1)
                        vline = $"                    Vector256.Store({vstr} {tstr}";
                    else if (nfound < count)
                        vline = $"                        {vstr} {tstr}";
                    else
                        vline = $"                        {vstr}, dptr);";
                    sa.AddString(vline, L.L_COPY);
                }
            }
        }
        sa.AddString("                }", L.L_COPY);
        sa.AddString("            }", L.L_COPY);
        sa.AddString("            for (; j < pwpls; j++, sptr++, dptr++) {", L.L_COPY);

        nfound = 0;
        for (i = 0; i < sel.Sy; i++) {
            for (j = 0; j < sel.Sx; j++) {
                if (sel.Data[i][j] == 1) {
                    nfound++;
                    if (optype == L.L_MORPH_DILATE) {
                        dely = sel.Cy - i;
                        delx = sel.Cx - j;
                    } else {  /* optype == L_MORPH_ERODE */
                        dely = i - sel.Cy;
                        delx = j - sel.Cx;
                    }
                    if ((str = MakeBarrelshiftString(delx, dely)) == null) {
                        Log.Warning(func, "barrel shift string not made\n");
                        continue;
                    }
                    string line;
                    if (count == 1)  /* just one item */
                        line = $"                *dptr = {str};";
                    else if (nfound == 1)
                        line = $"                *dptr = {str} {tstr}";
                    else if (nfound < count)
                        line = $"                        {str} {tstr}";
                    else  /* nfound == count */
                        line = $"                        {str};";
                    sa.AddString(line, L.L_COPY);
                }
            }
        }
        sa.AddString("            }", L.L_COPY);

        return sa;
    }

    private static string? MakeBarrelshiftVecString(int delx, int dely)
    {
        if (delx < -31 || delx > 31 || dely < -31 || dely > 31)
            return null;
        int absx = Math.Abs(delx);
        int absy = Math.Abs(dely);

        if ((delx == 0) && (dely == 0))
            return "Vector256.Load(sptr)";
        else if ((delx == 0) && (dely < 0))
            return $"Vector256.Load(sptr {wplstrm[absy - 1]})";
        else if ((delx == 0) && (dely > 0))
            return $"Vector256.Load(sptr {wplstrp[absy - 1]})";
        else if ((delx < 0) && (dely == 0))
            return $"((Vector256.Load(sptr) >> {absx}) | (Vector256.Load(sptr - 1) << {32 - absx}))";
        else if ((delx > 0) && (dely == 0))
            return $"((Vector256.Load(sptr) << {absx}) | (Vector256.Load(sptr + 1) >> {32 - absx}))";
        else if ((delx < 0) && (dely < 0))
            return $"((Vector256.Load(sptr {wplstrm[absy - 1]}) >> {absx}) | (Vector256.Load(sptr {wplstrm[absy - 1]} - 1) << {32 - absx}))";
        else if ((delx > 0) && (dely < 0))
            return $"((Vector256.Load(sptr {wplstrm[absy - 1]}) << {absx}) | (Vector256.Load(sptr {wplstrm[absy - 1]} + 1) >> {32 - absx}))";
        else if ((delx < 0) && (dely > 0))
            return $"((Vector256.Load(sptr {wplstrp[absy - 1]}) >> {absx}) | (Vector256.Load(sptr {wplstrp[absy - 1]} - 1) << {32 - absx}))";
        else
            return $"((Vector256.Load(sptr {wplstrp[absy - 1]}) << {absx}) | (Vector256.Load(sptr {wplstrp[absy - 1]} + 1) >> {32 - absx}))";
    }

    // leptonica: makeBarrelshiftString (fmorphauto.c)
    /// <param name="delx">j - cx</param>
    /// <param name="dely">i - cy</param>
    private static string? MakeBarrelshiftString(int delx, int dely)
    {
        const string func = "makeBarrelshiftString";
        int absx, absy;

        if (delx < -31 || delx > 31) {
            Log.Error(func, "delx out of bounds");
            return null;
        }
        if (dely < -31 || dely > 31) {
            Log.Error(func, "dely out of bounds");
            return null;
        }
        absx = Math.Abs(delx);
        absy = Math.Abs(dely);

        if ((delx == 0) && (dely == 0))
            return "(*sptr)";
        else if ((delx == 0) && (dely < 0))
            return $"(*(sptr {wplstrm[absy - 1]}))";
        else if ((delx == 0) && (dely > 0))
            return $"(*(sptr {wplstrp[absy - 1]}))";
        else if ((delx < 0) && (dely == 0))
            return $"((*(sptr) >> {absx}) | (*(sptr - 1) << {32 - absx}))";
        else if ((delx > 0) && (dely == 0))
            return $"((*(sptr) << {absx}) | (*(sptr + 1) >> {32 - absx}))";
        else if ((delx < 0) && (dely < 0))
            return $"((*(sptr {wplstrm[absy - 1]}) >> {absx}) | (*(sptr {wplstrm[absy - 1]} - 1) << {32 - absx}))";
        else if ((delx > 0) && (dely < 0))
            return $"((*(sptr {wplstrm[absy - 1]}) << {absx}) | (*(sptr {wplstrm[absy - 1]} + 1) >> {32 - absx}))";
        else if ((delx < 0) && (dely > 0))
            return $"((*(sptr {wplstrp[absy - 1]}) >> {absx}) | (*(sptr {wplstrp[absy - 1]} - 1) << {32 - absx}))";
        else  /*  ((delx > 0) && (dely > 0))  */
            return $"((*(sptr {wplstrp[absy - 1]}) << {absx}) | (*(sptr {wplstrp[absy - 1]} + 1) >> {32 - absx}))";
    }

    /// <summary>Text of an embedded C# code template (l_binaryRead() of the template in C).</summary>
    internal static string ReadTemplate(string name)
    {
        using Stream? s = typeof(Sela).Assembly.GetManifestResourceStream("Leptonica.Templates." + name);
        if (s == null)
            throw Lept.Fail("ReadTemplate", $"template {name} not found");
        using var r = new StreamReader(s, Encoding.ASCII);
        return r.ReadToEnd();
    }
}
