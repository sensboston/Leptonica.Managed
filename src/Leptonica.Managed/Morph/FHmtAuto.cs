using System;
using System.Text;

namespace Leptonica;

/*
 *  fhmtauto.c: Main function calls:
 *       Sela.FHmtAutoGen, FHmtAutoGen1, FHmtAutoGen2
 *
 *  Static helpers:
 *       SarrayMakeHmtWplsCode, SarrayMakeHmtInnerLoopDWACode, MakeHmtBarrelshiftString
 *
 *  This automatically generates dwa code for the hit-miss transform.
 */
public sealed partial class Sela
{
    private const string HMT_OUTROOT = "fhmtgen";
    private const string HMT_TEMPLATE1 = "hmttemplate1.txt";
    private const string HMT_TEMPLATE2 = "hmttemplate2.txt";

    // leptonica: fhmtautogen (fhmtauto.c)
    /// <summary>Generates the two files of dwa hit-miss code for the sels in this Sela.</summary>
    /// <param name="fileindex">0 for no index; otherwise, use 1 ...</param>
    /// <param name="filename">root of output filename; use null for default</param>
    public void FHmtAutoGen(int fileindex, string? filename)
    {
        FHmtAutoGen1(fileindex, filename);
        FHmtAutoGen2(fileindex, filename);
    }

    // leptonica: fhmtautogen1 (fhmtauto.c)
    /// <summary>Generates the top-level file of dwa hit-miss code.</summary>
    /// <param name="fileindex">0 for no index; otherwise, use 1 ...</param>
    /// <param name="filename">root of output filename; use null for default</param>
    public void FHmtAutoGen1(int fileindex, string? filename)
    {
        if (fileindex < 0)
            fileindex = 0;
        string filestr = FHmtAutoGen1Text(fileindex, filename);
        string outname = $"{filename ?? HMT_OUTROOT}.{fileindex}.cs";
        Utils.BinaryWrite(outname, "w", Encoding.ASCII.GetBytes(filestr));
    }

    /// <summary>The text written by <see cref="FHmtAutoGen1"/>.</summary>
    internal string FHmtAutoGen1Text(int fileindex, string? filename)
    {
        const string func = "fhmtautogen1";
        Sela sela = this;
        int i, nsels, actstart, end, newstart;
        string str_doc1, str_doc2, str_doc3, str_doc4;
        string str_def1, str_def2, str_proc1, str_proc2;
        string str_dwa1, str_low_dt, str_low_ds;
        Sarray sa1, sa2, sa3;

        if (fileindex < 0)
            fileindex = 0;
        if ((nsels = sela.Count) == 0)
            throw Lept.Arg(func, "no sels in sela");
        string cname = $"{filename ?? HMT_OUTROOT}.{fileindex}.c";

            /* Make array of textlines from hmttemplate1.txt */
        sa2 = Sarray.CreateLinesFromString(ReadTemplate(HMT_TEMPLATE1), 1);

            /* Make array of sel names */
        sa1 = sela.GetSelnames();

            /* Make strings containing function call names */
        str_doc1 = $" *            Pix.HMTDwa_{fileindex}()";
        str_doc2 = $" *            Pix.FHMTGen_{fileindex}()";
        str_doc3 = $"    // leptonica: pixHMTDwa_{fileindex} ({cname})";
        str_doc4 = $"    // leptonica: pixFHMTGen_{fileindex} ({cname})";
        str_def1 = $"    public static Pix HMTDwa_{fileindex}(Pix? pixd, Pix pixs, string selname)";
        str_def2 = $"    public static unsafe Pix FHMTGen_{fileindex}(Pix? pixd, Pix pixs, string selname)";
        str_proc1 = $"        const string func = \"pixHMTDwa_{fileindex}\";";
        str_proc2 = $"        const string func = \"pixFHMTGen_{fileindex}\";";
        str_dwa1 = $"        pixt2 = FHMTGen_{fileindex}(null, pixt1, selname);";
        str_low_dt =
            $"                    DwaLow.fhmtgen_low_{fileindex}(datad, w, h, wpld, datat, wpls, index);";
        str_low_ds =
            $"                DwaLow.fhmtgen_low_{fileindex}(datad, w, h, wpld, datas, wpls, index);";

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

            /* Add static globals (file-local class keeps each generated file apart) */
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

            /* Start pixHMTDwa_*() function description */
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);
        sa3.AddString(str_doc3, L.L_INSERT);
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);

            /* Finish pixHMTDwa_*() function definition */
        sa3.AddString(str_def1, L.L_INSERT);
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);
        sa3.AddString(str_proc1, L.L_INSERT);
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);
        sa3.AddString(str_dwa1, L.L_INSERT);
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);

            /* Start pixFHMTGen_*() function description */
        sa3.AddString(str_doc4, L.L_INSERT);
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);

            /* Finish pixFHMTGen_*() function definition */
        sa3.AddString(str_def2, L.L_INSERT);
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);
        sa3.AddString(str_proc2, L.L_INSERT);
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);
        sa3.AddString(str_low_dt, L.L_INSERT);
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);
        sa3.AddString(str_low_ds, L.L_INSERT);
        sa2.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa3.AppendRange(sa2, actstart, end);

        return sa3.ToString(1);
    }

    // leptonica: fhmtautogen2 (fhmtauto.c)
    /// <summary>Generates the low-level file of dwa hit-miss code.</summary>
    /// <param name="fileindex">0 for no index; otherwise, use 1 ...</param>
    /// <param name="filename">root of output filename; use null for default</param>
    public void FHmtAutoGen2(int fileindex, string? filename)
    {
        if (fileindex < 0)
            fileindex = 0;
        string filestr = FHmtAutoGen2Text(fileindex, filename);
        string outname = $"{filename ?? HMT_OUTROOT}low.{fileindex}.cs";
        Utils.BinaryWrite(outname, "w", Encoding.ASCII.GetBytes(filestr));
    }

    /// <summary>The text written by <see cref="FHmtAutoGen2"/>.</summary>
    internal string FHmtAutoGen2Text(int fileindex, string? filename)
    {
        const string func = "fhmtautogen2";
        Sela sela = this;
        string fname;
        string str_doc1, str_doc2, str_doc3, str_def1;
        const string breakstring = "            break;";
        int i, k, l, nsels, nhits, nmisses;
        int actstart, end, newstart;
        int argstart, argend, loopstart, loopend, finalstart, finalend;
        Sarray sa1, sa2, sa4, sa5, sa6;
        Sel sel;

        if (fileindex < 0)
            fileindex = 0;
        if ((nsels = sela.Count) == 0)
            throw Lept.Arg(func, "no sels in sela");
        string cname = $"{filename ?? HMT_OUTROOT}low.{fileindex}.c";

            /* Make the array of textlines from hmttemplate2.txt */
        sa1 = Sarray.CreateLinesFromString(ReadTemplate(HMT_TEMPLATE2), 1);

            /* Make the array of static function names */
        sa2 = Sarray.Create(nsels);
        for (i = 0; i < nsels; i++)
            sa2.AddString($"fhmt_{fileindex}_{i}", L.L_COPY);

            /* Make strings containing function names */
        str_doc1 = $" *            fhmtgen_low_{fileindex}()";
        str_doc2 = $" *            fhmt_{fileindex}_*()";

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
        sa1.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa4.AppendRange(sa1, actstart, end);

            /* More strings containing function names */
        str_doc3 = $"     *  fhmtgen_low_{fileindex}()";
        str_def1 = $"    internal static int fhmtgen_low_{fileindex}(uint* datad, int w, int h, int wpld,";

            /* Insert function header */
        sa1.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa4.AppendRange(sa1, actstart, end);
        sa4.AddString(str_doc3, L.L_INSERT);
        sa1.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa4.AppendRange(sa1, actstart, end);
        sa4.AddString($"    // leptonica: fhmtgen_low_{fileindex} ({cname})", L.L_COPY);
        sa4.AddString(str_def1, L.L_INSERT);
        sa1.ParseRange(newstart, out actstart, out end, out newstart, "--", 0);
        sa4.AppendRange(sa1, actstart, end);

            /* Generate and insert the dispatcher code */
        for (i = 0; i < nsels; i++) {
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
        for (i = 0; i < nsels; i++) {
                /* Generate the function header and add the common args */
            fname = sa2.GetString(i, L.L_NOCOPY);
            sa4.AddString($"    // leptonica: {fname} ({cname})", L.L_COPY);
            sa4.AddString($"    private static void {fname}(uint* datad,", L.L_COPY);
            sa4.AppendRange(sa1, argstart, argend);

                /* Declare and define wplsN args, as necessary */
            sel = sela.GetSel(i);
            sa5 = SarrayMakeHmtWplsCode(sel);
            sa4.Join(sa5);

                /* Make sure sel has at least one hit */
            nhits = 0;
            nmisses = 0;
            for (k = 0; k < sel.Sy; k++) {
                for (l = 0; l < sel.Sx; l++) {
                    if (sel.Data[k][l] == 1)
                        nhits++;
                    else if (sel.Data[k][l] == 2)
                        nmisses++;
                }
            }
            if (nhits == 0) {
                sa4.AddString("        Log.Error(\"fhmt\", \"Error in HMT: no hits in sel!\");\n    }\n", L.L_COPY);
                continue;
            }

                /* Add the function loop code */
            sa4.AppendRange(sa1, loopstart, loopend);

                /* Insert barrel-op code for *dptr */
            sa6 = SarrayMakeHmtInnerLoopDWACode(sel, nhits, nmisses);
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
    // leptonica: sarrayMakeWplsCode (fhmtauto.c)
    private static Sarray SarrayMakeHmtWplsCode(Sel sel)
    {
        const string func = "sarrayMakeWplsCode";
        const string emptystring = "";
        int i, j, ymax, dely;
        Sarray sa;

        ymax = 0;
        for (i = 0; i < sel.Sy; i++) {
            for (j = 0; j < sel.Sx; j++) {
                if (sel.Data[i][j] == 1 || sel.Data[i][j] == 2) {
                    dely = Math.Abs(i - sel.Cy);
                    ymax = Math.Max(ymax, dely);
                }
            }
        }
        if (ymax > 31) {
            Log.Warning(func, "ymax > 31; truncating to 31\n");
            ymax = 31;
        }

        sa = Sarray.Create(0);

            /* Declarations */
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

        sa.AddString(emptystring, L.L_COPY);

            /* Definitions */
        for (i = 2; i <= ymax; i++)
            sa.AddString(wpldefs[i - 2], L.L_COPY);

        return sa;
    }

    // leptonica: sarrayMakeInnerLoopDWACode (fhmtauto.c)
    private static Sarray SarrayMakeHmtInnerLoopDWACode(Sel sel, int nhits, int nmisses)
    {
        const string func = "sarrayMakeInnerLoopDWACode";
        const string land = "&";
        int i, j, ntot, nfound, type, delx, dely;
        string? str;
        string line;
        Sarray sa;

        sa = Sarray.Create(0);
        ntot = nhits + nmisses;
        sa.AddString("            if (Vector256.IsHardwareAccelerated) {", L.L_COPY);
        sa.AddString("                for (; j + 8 <= pwpls; j += 8, sptr += 8, dptr += 8) {", L.L_COPY);
        nfound = 0;
        for (i = 0; i < sel.Sy; i++) {
            for (j = 0; j < sel.Sx; j++) {
                type = sel.Data[i][j];
                if (type == L.SEL_HIT || type == L.SEL_MISS) {
                    nfound++;
                    dely = i - sel.Cy;
                    delx = j - sel.Cx;
                    string? vstr = MakeHmtBarrelshiftVecString(delx, dely, type);
                    if (vstr == null)
                        continue;
                    string vline;
                    if (ntot == 1)
                        vline = $"                    Vector256.Store({vstr}, dptr);";
                    else if (nfound == 1)
                        vline = $"                    Vector256.Store({vstr} {land}";
                    else if (nfound < ntot)
                        vline = $"                        {vstr} {land}";
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
                type = sel.Data[i][j];
                if (type == L.SEL_HIT || type == L.SEL_MISS) {
                    nfound++;
                    dely = i - sel.Cy;
                    delx = j - sel.Cx;
                    if ((str = MakeHmtBarrelshiftString(delx, dely, type)) == null) {
                        Log.Warning(func, "barrel shift string not made");
                        continue;
                    }
                    if (ntot == 1)  /* just one item */
                        line = $"                *dptr = {str};";
                    else if (nfound == 1)
                        line = $"                *dptr = {str} {land}";
                    else if (nfound < ntot)
                        line = $"                        {str} {land}";
                    else  /* nfound == ntot */
                        line = $"                        {str};";
                    sa.AddString(line, L.L_COPY);
                }
            }
        }
        sa.AddString("            }", L.L_COPY);

        return sa;
    }

    private static string? MakeHmtBarrelshiftVecString(int delx, int dely, int type)
    {
        if (delx < -31 || delx > 31 || dely < -31 || dely > 31)
            return null;
        int absx = Math.Abs(delx);
        int absy = Math.Abs(dely);
        string inv = type == L.SEL_HIT ? "" : "~";

        if ((delx == 0) && (dely == 0))
            return $"({inv}Vector256.Load(sptr))";
        else if ((delx == 0) && (dely < 0))
            return $"({inv}Vector256.Load(sptr {wplstrm[absy - 1]}))";
        else if ((delx == 0) && (dely > 0))
            return $"({inv}Vector256.Load(sptr {wplstrp[absy - 1]}))";
        else if ((delx < 0) && (dely == 0))
            return $"(({inv}Vector256.Load(sptr) >> {absx}) | ({inv}Vector256.Load(sptr - 1) << {32 - absx}))";
        else if ((delx > 0) && (dely == 0))
            return $"(({inv}Vector256.Load(sptr) << {absx}) | ({inv}Vector256.Load(sptr + 1) >> {32 - absx}))";
        else if ((delx < 0) && (dely < 0))
            return $"(({inv}Vector256.Load(sptr {wplstrm[absy - 1]}) >> {absx}) | ({inv}Vector256.Load(sptr {wplstrm[absy - 1]} - 1) << {32 - absx}))";
        else if ((delx > 0) && (dely < 0))
            return $"(({inv}Vector256.Load(sptr {wplstrm[absy - 1]}) << {absx}) | ({inv}Vector256.Load(sptr {wplstrm[absy - 1]} + 1) >> {32 - absx}))";
        else if ((delx < 0) && (dely > 0))
            return $"(({inv}Vector256.Load(sptr {wplstrp[absy - 1]}) >> {absx}) | ({inv}Vector256.Load(sptr {wplstrp[absy - 1]} - 1) << {32 - absx}))";
        else
            return $"(({inv}Vector256.Load(sptr {wplstrp[absy - 1]}) << {absx}) | ({inv}Vector256.Load(sptr {wplstrp[absy - 1]} + 1) >> {32 - absx}))";
    }

    // leptonica: makeBarrelshiftString (fhmtauto.c)
    private static string? MakeHmtBarrelshiftString(int delx, int dely, int type)
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

        if (type == L.SEL_HIT) {
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
            else  /* ((delx > 0) && (dely > 0)) */
                return $"((*(sptr {wplstrp[absy - 1]}) << {absx}) | (*(sptr {wplstrp[absy - 1]} + 1) >> {32 - absx}))";
        } else {  /* type == SEL_MISS */
            if ((delx == 0) && (dely == 0))
                return "(~*sptr)";
            else if ((delx == 0) && (dely < 0))
                return $"(~*(sptr {wplstrm[absy - 1]}))";
            else if ((delx == 0) && (dely > 0))
                return $"(~*(sptr {wplstrp[absy - 1]}))";
            else if ((delx < 0) && (dely == 0))
                return $"((~*(sptr) >> {absx}) | (~*(sptr - 1) << {32 - absx}))";
            else if ((delx > 0) && (dely == 0))
                return $"((~*(sptr) << {absx}) | (~*(sptr + 1) >> {32 - absx}))";
            else if ((delx < 0) && (dely < 0))
                return $"((~*(sptr {wplstrm[absy - 1]}) >> {absx}) | (~*(sptr {wplstrm[absy - 1]} - 1) << {32 - absx}))";
            else if ((delx > 0) && (dely < 0))
                return $"((~*(sptr {wplstrm[absy - 1]}) << {absx}) | (~*(sptr {wplstrm[absy - 1]} + 1) >> {32 - absx}))";
            else if ((delx < 0) && (dely > 0))
                return $"((~*(sptr {wplstrp[absy - 1]}) >> {absx}) | (~*(sptr {wplstrp[absy - 1]} - 1) << {32 - absx}))";
            else  /* ((delx > 0) && (dely > 0)) */
                return $"((~*(sptr {wplstrp[absy - 1]}) << {absx}) | (~*(sptr {wplstrp[absy - 1]} + 1) >> {32 - absx}))";
        }
    }
}
