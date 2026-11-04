using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Leptonica;

/*
 *  gplot.c: gnuplot data and script generation.
 *
 *  Utility for programmatic plotting using gnuplot 4.6 or later.
 *  Enabled:
 *      ~ output to png (color), ps and eps (mono), latex (mono)
 *      ~ optional title for plot
 *      ~ optional x and y axis labels
 *      ~ multiple plots on one frame
 *      ~ optional label for each plot on the frame
 *      ~ optional log scaling on either or both axes
 *      ~ choice of 5 plot styles for each array of input data
 *      ~ choice of 2 plot modes, either using one input array
 *        (Y vs index) or two input arrays (Y vs X).  For functions
 *        that take two arrays, the first mode (Y vs index) is
 *        employed if the first array is null.
 *
 *  General usage:
 *      Gplot.Create() initializes for plotting
 *      AddPlot() for each plot on the frame
 *      MakeOutput() to generate all output files and run gnuplot
 *
 *  Differences from C:
 *    - Running gnuplot is out of scope.  MakeOutput() writes
 *      the command file and the data files (when LeptDebugOK allows it), and
 *      does not start the gnuplot process: callSystemDebug() is not ported.
 *      Consequently, MakeOutputPix() and the functions built on it find no
 *      output image unless the caller has produced it by other means.
 *    - No gplotDestroy(): the GC owns the objects (not ported).
 *    - The C strings are formatted into a buffer of 512 bytes; the same
 *      truncation (511 characters) is applied to the strings built with
 *      snprintf() in the C code.
 *    - Functions that return an error code or NULL in C throw instead.
 */

/// <summary>Constants from gplot.h.</summary>
public static partial class L
{
    /// <summary>Version number of the serialized plot format.</summary>
    public const int GPLOT_VERSION_NUMBER = 1;

    /* Plotting styles (GPLOT_STYLE) */
    /// <summary>Number of plotting styles.</summary>
    public const int NUM_GPLOT_STYLES = 5;
    /// <summary>Plot style: connect the data points with lines.</summary>
    public const int GPLOT_LINES = 0;
    /// <summary>Plot style: draw each data point as a marker.</summary>
    public const int GPLOT_POINTS = 1;
    /// <summary>Plot style: draw a vertical line from the axis to each data point.</summary>
    public const int GPLOT_IMPULSES = 2;
    /// <summary>Plot style: connect the data points with lines and mark each point.</summary>
    public const int GPLOT_LINESPOINTS = 3;
    /// <summary>Plot style: draw each data point as a small dot.</summary>
    public const int GPLOT_DOTS = 4;

    /* Output formats (GPLOT_OUTPUT) */
    /// <summary>Number of output formats.</summary>
    public const int NUM_GPLOT_OUTPUTS = 6;
    /// <summary>No output format specified.</summary>
    public const int GPLOT_NONE = 0;
    /// <summary>Output format: PNG image.</summary>
    public const int GPLOT_PNG = 1;
    /// <summary>Output format: PostScript.</summary>
    public const int GPLOT_PS = 2;
    /// <summary>Output format: Encapsulated PostScript.</summary>
    public const int GPLOT_EPS = 3;
    /// <summary>Output format: LaTeX.</summary>
    public const int GPLOT_LATEX = 4;
    /// <summary>Output format: PNM image.</summary>
    public const int GPLOT_PNM = 5;

    /* Axis scaling (GPLOT_SCALING) */
    /// <summary>Linear scale on both axes (the default).</summary>
    public const int GPLOT_LINEAR_SCALE = 0;
    /// <summary>Logarithmic scale on the x axis.</summary>
    public const int GPLOT_LOG_SCALE_X = 1;
    /// <summary>Logarithmic scale on the y axis.</summary>
    public const int GPLOT_LOG_SCALE_Y = 2;
    /// <summary>Logarithmic scale on both axes.</summary>
    public const int GPLOT_LOG_SCALE_X_Y = 3;
}

/// <summary>Plot description (struct GPLOT).</summary>
public sealed partial class Gplot
{
    private const int Bufsize = 512;  /* hardcoded below in fscanf */

    /// <summary>Gnuplot style keywords, indexed by the <c>GPLOT_*</c> style constants.</summary>
    public static readonly string[] gplotstylenames =
    {
        "with lines",
        "with points",
        "with impulses",
        "with linespoints",
        "with dots",
    };

    /// <summary>Names of the output formats, indexed by the <c>GPLOT_*</c> output constants.</summary>
    public static readonly string[] gplotfileoutputs =
    {
        "",
        "PNG",
        "PS",
        "EPS",
        "LATEX",
        "PNM",
    };

    private static int pix1Index;
    private static int pix2Index;
    private static int pixNIndex;

    /// <summary>Root for all output files.</summary>
    public string RootName { get; private set; } = "";

    /// <summary>Name of the command file.</summary>
    public string CmdName { get; private set; } = "";

    /// <summary>Command file data.</summary>
    public Sarray CmdData { get; private set; } = null!;

    /// <summary>Names of the data files.</summary>
    public Sarray DataNames { get; private set; } = null!;

    /// <summary>Plot data (1 string/file).</summary>
    public Sarray PlotData { get; private set; } = null!;

    /// <summary>Label for each individual plot.</summary>
    public Sarray PlotLabels { get; private set; } = null!;

    /// <summary>Plot style for each plot.</summary>
    public Numa PlotStyles { get; private set; } = null!;

    /// <summary>Current number of plots.</summary>
    public int NPlots { get; private set; }

    /// <summary>Name of the output file.</summary>
    public string OutName { get; private set; } = "";

    /// <summary>Overall title of the plot.</summary>
    public string? Title { get; private set; }

    /// <summary>x axis label.</summary>
    public string? XLabel { get; private set; }

    /// <summary>y axis label.</summary>
    public string? YLabel { get; private set; }

    /// <summary>One of GPLOT_PNG, GPLOT_PS, GPLOT_EPS, GPLOT_LATEX, GPLOT_PNM.</summary>
    public int OutFormat { get; private set; }

    /// <summary>One of GPLOT_LINEAR_SCALE, ...</summary>
    public int Scaling { get; private set; }

    private static string Trunc(string s)
    {
        return s.Length > Bufsize - 1 ? s.Substring(0, Bufsize - 1) : s;
    }

    /*-----------------------------------------------------------------*
     *                       Basic Plotting Functions                  *
     *-----------------------------------------------------------------*/

    // leptonica: gplotCreate (gplot.c)
    /// <summary>Initializes the plot.</summary>
    /// <param name="rootname">root for all output files</param>
    /// <param name="outformat">GPLOT_PNG, GPLOT_PS, GPLOT_EPS, GPLOT_LATEX, GPLOT_PNM</param>
    /// <param name="title">[optional] overall title</param>
    /// <param name="xlabel">[optional] x axis label</param>
    /// <param name="ylabel">[optional] y axis label</param>
    /// <returns>gplot</returns>
    /// <remarks>
    /// (1) This initializes the plot.
    /// (2) The 'title', 'xlabel' and 'ylabel' strings can have spaces,
    ///     double quotes and backquotes, but not single quotes.
    /// </remarks>
    public static Gplot Create(string rootname, int outformat, string? title, string? xlabel,
                               string? ylabel)
    {
        const string func = "gplotCreate";

        if (outformat != L.GPLOT_PNG && outformat != L.GPLOT_PS &&
            outformat != L.GPLOT_EPS && outformat != L.GPLOT_LATEX &&
            outformat != L.GPLOT_PNM)
            throw Lept.Arg(func, "outformat invalid");
        if (Utils.StringCheckForChars(rootname, "`;&|><\"?*$()"))  /* danger of command injection */
            throw Lept.Arg(func, "invalid rootname");

        Gplot gplot = new Gplot();
        gplot.CmdData = Sarray.Create(0);
        gplot.DataNames = Sarray.Create(0);
        gplot.PlotData = Sarray.Create(0);
        gplot.PlotLabels = Sarray.Create(0);
        gplot.PlotStyles = Numa.Create(0);

            /* Save title, labels, rootname, outformat, cmdname, outname */
        string newroot = Utils.GenPathname(rootname, null);
        gplot.RootName = newroot;
        gplot.OutFormat = outformat;
        gplot.CmdName = Trunc($"{rootname}.cmd");
        string buf = "";
        if (outformat == L.GPLOT_PNG)
            buf = Trunc($"{newroot}.png");
        else if (outformat == L.GPLOT_PS)
            buf = Trunc($"{newroot}.ps");
        else if (outformat == L.GPLOT_EPS)
            buf = Trunc($"{newroot}.eps");
        else if (outformat == L.GPLOT_LATEX)
            buf = Trunc($"{newroot}.tex");
        else if (outformat == L.GPLOT_PNM)
            buf = Trunc($"{newroot}.pnm");
        gplot.OutName = buf;
        if (title != null) gplot.Title = title;
        if (xlabel != null) gplot.XLabel = xlabel;
        if (ylabel != null) gplot.YLabel = ylabel;

        return gplot;
    }

    // leptonica: gplotAddPlot (gplot.c)
    /// <summary>Adds a plot (a data file and a plot command) to the gplot.</summary>
    /// <param name="nax">[optional] numa: set to null for Y_VS_I; required for Y_VS_X</param>
    /// <param name="nay">numa; required for both Y_VS_I and Y_VS_X</param>
    /// <param name="plotstyle">GPLOT_LINES, GPLOT_POINTS, GPLOT_IMPULSES,
    /// GPLOT_LINESPOINTS, GPLOT_DOTS</param>
    /// <param name="plotlabel">[optional] label for individual plot</param>
    /// <remarks>
    /// (1) There are 2 options for (x,y) values:
    ///       o  To plot an array vs a linear function of the
    ///          index, set nax = null.
    ///       o  To plot one array vs another, use both nax and nay.
    /// (2) If nax is null, the x value corresponding to the i-th
    ///     value of nay is found from the startx and delx fields
    ///     in nay:
    ///          x = startx + i * delx
    ///     These are set with numaSetParameters().  Their default
    ///     values are startx = 0.0, delx = 1.0.
    /// (3) If nax is defined, it must be the same size as nay, and
    ///     must have at least one number.
    /// (4) The 'plotlabel' string can have spaces, double
    ///     quotes and backquotes, but not single quotes.
    /// </remarks>
    public void AddPlot(Numa? nax, Numa nay, int plotstyle, string? plotlabel)
    {
        const string func = "gplotAddPlot";

        if (plotstyle < 0 || plotstyle >= L.NUM_GPLOT_STYLES)
            throw Lept.Arg(func, "invalid plotstyle");

        int n;
        if ((n = nay.Count) == 0)
            throw Lept.Arg(func, "no points to plot");
        if (nax != null && (n != nax.Count))
            throw Lept.Arg(func, "nax and nay sizes differ");
        if (n == 1 && plotstyle == L.GPLOT_LINES) {
            Log.Info(func, "only 1 pt; changing style to points\n");
            plotstyle = L.GPLOT_POINTS;
        }

            /* Save plotstyle and plotlabel */
        nay.GetParameters(out float startx, out float delx);
        PlotStyles.AddNumber(plotstyle);
        if (plotlabel != null)
            PlotLabels.AddString(plotlabel, L.L_INSERT);
        else
            PlotLabels.AddString("", L.L_COPY);

            /* Generate and save data filename */
        NPlots++;
        string buf = Trunc($"{RootName}.data.{NPlots}");
        DataNames.AddString(buf, L.L_COPY);

            /* Generate data and save as a string */
        Sarray sa = Sarray.Create(n);
        for (int i = 0; i < n; i++) {
            float valx;
            if (nax != null)
                valx = nax.GetFValue(i);
            else
                valx = startx + i * delx;
            float valy = nay.GetFValue(i);
            buf = Trunc(CFormat.Sprintf("%f %f\n", (double)valx, (double)valy));
            sa.AddString(buf, L.L_COPY);
        }
        string datastr = sa.ToString(0);
        PlotData.AddString(datastr, L.L_INSERT);
    }

    // leptonica: gplotSetScaling (gplot.c)
    /// <summary>Sets the scaling of the axes.</summary>
    /// <param name="scaling">GPLOT_LINEAR_SCALE, GPLOT_LOG_SCALE_X, GPLOT_LOG_SCALE_Y,
    /// GPLOT_LOG_SCALE_X_Y</param>
    /// <remarks>
    /// (1) By default, the x and y axis scaling is linear.
    /// (2) Call this function to set semi-log or log-log scaling.
    /// </remarks>
    public void SetScaling(int scaling)
    {
        if (scaling != L.GPLOT_LINEAR_SCALE &&
            scaling != L.GPLOT_LOG_SCALE_X &&
            scaling != L.GPLOT_LOG_SCALE_Y &&
            scaling != L.GPLOT_LOG_SCALE_X_Y)
            throw Lept.Arg("gplotSetScaling", "invalid gplot scaling");
        Scaling = scaling;
    }

    // leptonica: gplotMakeOutputPix (gplot.c)
    /// <summary>Makes the output and reads it as a pix.</summary>
    /// <returns>pix of the plot</returns>
    /// <remarks>
    /// (1) This wraps gplotMakeOutput(), and returns a pix.
    ///     See gplotMakeOutput() for details.
    /// (2) The gplot output format must be an image (png or pnm).
    /// </remarks>
    public Pix MakeOutputPix()
    {
        const string func = "gplotMakeOutputPix";

        if (OutFormat != L.GPLOT_PNG && OutFormat != L.GPLOT_PNM)
            throw Lept.Arg(func, "output format not an image");

        MakeOutput();
        return Pix.Read(OutName);
    }

    // leptonica: gplotMakeOutput (gplot.c)
    /// <summary>Writes the command and data files that gnuplot needs to make the output.</summary>
    /// <remarks>
    /// (1) This uses gplot and the new arrays to add a plot
    ///     to the output, by writing a new data file and appending
    ///     the appropriate plot commands to the command file.
    /// (2) Nothing is written unless LeptDebugOK is set.
    /// (3) Running the gnuplot program is not part of the port: the command
    ///     file has to be given to gnuplot by the caller.
    /// </remarks>
    public void MakeOutput()
    {
        if (!Pix.LeptDebugOK) {
            Log.Info("gplotMakeOutput", "running gnuplot is disabled; " +
                     "use setLeptDebugOK(1) to enable\n");
            return;
        }

        GenCommandFile();
        GenDataFiles();
        // NOTE: callSystemDebug("gnuplot <cmdname>") is not ported.
    }

    // leptonica: gplotGenCommandFile (gplot.c)
    /// <summary>Generates the gnuplot command file.</summary>
    public void GenCommandFile()
    {
            /* Remove any previous command data */
        CmdData.Clear();

            /* Generate command data instructions */
        string buf;
        if (Title != null) {   /* set title */
            buf = Trunc($"set title '{Title}'");
            CmdData.AddString(buf, L.L_COPY);
        }
        if (XLabel != null) {   /* set xlabel */
            buf = Trunc($"set xlabel '{XLabel}'");
            CmdData.AddString(buf, L.L_COPY);
        }
        if (YLabel != null) {   /* set ylabel */
            buf = Trunc($"set ylabel '{YLabel}'");
            CmdData.AddString(buf, L.L_COPY);
        }

            /* Set terminal type and output */
        buf = "";
        if (OutFormat == L.GPLOT_PNG) {
            buf = Trunc($"set terminal png; set output '{OutName}'");
        } else if (OutFormat == L.GPLOT_PS) {
            buf = Trunc($"set terminal postscript; set output '{OutName}'");
        } else if (OutFormat == L.GPLOT_EPS) {
            buf = Trunc($"set terminal postscript eps; set output '{OutName}'");
        } else if (OutFormat == L.GPLOT_LATEX) {
            buf = Trunc($"set terminal latex; set output '{OutName}'");
        } else if (OutFormat == L.GPLOT_PNM) {
            buf = Trunc($"set terminal pbm color; set output '{OutName}'");
        }
        CmdData.AddString(buf, L.L_COPY);

        if (Scaling == L.GPLOT_LOG_SCALE_X ||
            Scaling == L.GPLOT_LOG_SCALE_X_Y) {
            CmdData.AddString("set logscale x", L.L_COPY);
        }
        if (Scaling == L.GPLOT_LOG_SCALE_Y ||
            Scaling == L.GPLOT_LOG_SCALE_X_Y) {
            CmdData.AddString("set logscale y", L.L_COPY);
        }

        int nplots = DataNames.Count;
        for (int i = 0; i < nplots; i++) {
            string plotlabel = PlotLabels.GetString(i, L.L_NOCOPY);
            string dataname = DataNames.GetString(i, L.L_NOCOPY);
            int plotstyle = PlotStyles.GetIValue(i);
            if (nplots == 1) {
                buf = Trunc($"plot '{dataname}' title '{plotlabel}' {gplotstylenames[plotstyle]}");
            } else {
                if (i == 0)
                    buf = Trunc($"plot '{dataname}' title '{plotlabel}' {gplotstylenames[plotstyle]}, \\");
                else if (i < nplots - 1)
                    buf = Trunc($" '{dataname}' title '{plotlabel}' {gplotstylenames[plotstyle]}, \\");
                else
                    buf = Trunc($" '{dataname}' title '{plotlabel}' {gplotstylenames[plotstyle]}");
            }
            CmdData.AddString(buf, L.L_COPY);
        }

            /* Write command data to file */
        string cmdstr = CmdData.ToString(1);
        FileStream fp;
        try {
            fp = Utils.FopenWriteStream(CmdName, "w");
        } catch (LeptonicaException) {
            Log.Error("gplotGenCommandFile", $"stream not opened for command: {cmdstr}\n");
            throw;
        }
        using (fp) {
            byte[] bytes = Encoding.UTF8.GetBytes(cmdstr);
            fp.Write(bytes, 0, bytes.Length);
        }
    }

    // leptonica: gplotGenDataFiles (gplot.c)
    /// <summary>Writes the data files of the plots.</summary>
    /// <remarks>
    /// (1) The pathnames in the gplot command file are actual pathnames,
    ///     which can be in temp directories.  Consequently, they must not be
    ///     rewritten by calling fopenWriteStream(), and we use File.Create().
    /// </remarks>
    public void GenDataFiles()
    {
        int nplots = DataNames.Count;
        for (int i = 0; i < nplots; i++) {
            string plotdata = PlotData.GetString(i, L.L_NOCOPY);
            string dataname = DataNames.GetString(i, L.L_NOCOPY);
            try {
                using FileStream fp = File.Create(dataname);
                byte[] bytes = Encoding.UTF8.GetBytes(plotdata);
                fp.Write(bytes, 0, bytes.Length);
            } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) {
                throw Lept.Fail("gplotGenDataFiles", $"datafile stream not opened: {dataname}");
            }
        }
    }

    /*-----------------------------------------------------------------*
     *                        Quick one-line plots                     *
     *-----------------------------------------------------------------*/

    // leptonica: gplotSimple1 (gplot.c)
    /// <summary>Generates a line plot of a numa, plotted vs the array index.</summary>
    /// <param name="na">numa; plot Y_VS_I</param>
    /// <param name="outformat">GPLOT_PNG, GPLOT_PS, GPLOT_EPS, GPLOT_LATEX, GPLOT_PNM</param>
    /// <param name="outroot">root of output files</param>
    /// <param name="title">[optional], can be null</param>
    /// <remarks>
    /// (1) This generates a line plot of a numa, where the array value
    ///     is plotted vs the array index.  The plot is generated
    ///     in the specified output format; the title  is optional.
    /// (2) When calling these simple plot functions more than once, use
    ///     different outroot to avoid overwriting the output files.
    /// </remarks>
    public static void Simple1(Numa na, int outformat, string outroot, string? title)
    {
        SimpleXY1(null, na, L.GPLOT_LINES, outformat, outroot, title);
    }

    // leptonica: gplotSimple2 (gplot.c)
    /// <summary>Generates line plots of two numa, each plotted vs the array index.</summary>
    /// <param name="na1">numa; plot with Y_VS_I</param>
    /// <param name="na2">ditto</param>
    /// <param name="outformat">GPLOT_PNG, GPLOT_PS, GPLOT_EPS, GPLOT_LATEX, GPLOT_PNM</param>
    /// <param name="outroot">root of output files</param>
    /// <param name="title">[optional]</param>
    /// <remarks>
    /// (1) This generates a line plot of two numa, where the array values
    ///     are each plotted vs the array index.  The plot is generated
    ///     in the specified output format; the title  is optional.
    /// (2) When calling these simple plot functions more than once, use
    ///     different outroot to avoid overwriting the output files.
    /// </remarks>
    public static void Simple2(Numa na1, Numa na2, int outformat, string outroot, string? title)
    {
        SimpleXY2(null, na1, na2, L.GPLOT_LINES, outformat, outroot, title);
    }

    // leptonica: gplotSimpleN (gplot.c)
    /// <summary>Generates line plots of all numas in a numaa, each plotted vs the array index.</summary>
    /// <param name="naa">numaa; plot Y_VS_I for each numa</param>
    /// <param name="outformat">GPLOT_PNG, GPLOT_PS, GPLOT_EPS, GPLOT_LATEX, GPLOT_PNM</param>
    /// <param name="outroot">root of output files</param>
    /// <param name="title">[optional]</param>
    /// <remarks>
    /// (1) This generates a line plot of all numas in a numaa (array of numa),
    ///     where the array values are each plotted vs the array index.
    ///     The plot is generated in the specified output format;
    ///     the title  is optional.
    /// (2) When calling these simple plot functions more than once, use
    ///     different outroot to avoid overwriting the output files.
    /// </remarks>
    public static void SimpleN(Numaa naa, int outformat, string outroot, string? title)
    {
        SimpleXYN(null, naa, L.GPLOT_LINES, outformat, outroot, title);
    }

    // leptonica: gplotSimplePix1 (gplot.c)
    /// <summary>Generates a line plot of a numa as a pix.</summary>
    /// <param name="na">numa; plot Y_VS_I</param>
    /// <param name="title">[optional], can be null</param>
    /// <returns>pix of plot</returns>
    /// <remarks>
    /// (1) This generates a line plot of a numa as a pix, where the array
    ///     value is plotted vs the array index.  The title is optional.
    /// (2) The temporary plot file is a png; its name is generated internally
    ///     and stored in gplot.
    /// </remarks>
    public static Pix SimplePix1(Numa na, string? title)
    {
        Utils.LeptMkdir("lept/gplot/pix");
        string buf = $"/tmp/lept/gplot/pix1.{Interlocked.Increment(ref pix1Index) - 1}";
        Gplot gplot = SimpleXY1(null, na, L.GPLOT_LINES, L.GPLOT_PNG, buf, title);
        return Pix.Read(gplot.OutName);
    }

    // leptonica: gplotSimplePix2 (gplot.c)
    /// <summary>Generates line plots of two numa as a pix.</summary>
    /// <param name="na1">numa; plot with Y_VS_I</param>
    /// <param name="na2">ditto</param>
    /// <param name="title">[optional], can be null</param>
    /// <returns>pix of plot</returns>
    /// <remarks>
    /// (1) This generates a pix with line plots of two numa, where each of
    ///     two arrays is plotted vs the array index.  the title is optional.
    /// (2) The temporary plot file is a png; its name is generated internally
    ///     and stored in gplot.
    /// </remarks>
    public static Pix SimplePix2(Numa na1, Numa na2, string? title)
    {
        Utils.LeptMkdir("lept/gplot/pix");
        string buf = $"/tmp/lept/gplot/pix2.{Interlocked.Increment(ref pix2Index) - 1}";
        Gplot gplot = SimpleXY2(null, na1, na2, L.GPLOT_LINES, L.GPLOT_PNG, buf, title);
        return Pix.Read(gplot.OutName);
    }

    // leptonica: gplotSimplePixN (gplot.c)
    /// <summary>Generates line plots of all numas in a numaa as a pix.</summary>
    /// <param name="naa">numaa; plot Y_VS_I for each numa</param>
    /// <param name="title">[optional], can be null</param>
    /// <returns>pix of plot</returns>
    /// <remarks>
    /// (1) This generates a pix with an arbitrary number of line plots,
    ///     each coming from a numa in naa.  Each array value is plotted
    ///     vs the array index.  The title is optional.
    /// (2) The temporary plot file is a png; its name is generated internally
    ///     and stored in gplot.
    /// </remarks>
    public static Pix SimplePixN(Numaa naa, string? title)
    {
        Utils.LeptMkdir("lept/gplot/pix");
        string buf = $"/tmp/lept/gplot/pixN.{Interlocked.Increment(ref pixNIndex) - 1}";
        Gplot gplot = SimpleXYN(null, naa, L.GPLOT_LINES, L.GPLOT_PNG, buf, title);
        return Pix.Read(gplot.OutName);
    }

    // leptonica: gplotSimpleXY1 (gplot.c)
    /// <summary>Generates a plot of nay vs nax.</summary>
    /// <param name="nax">[optional]</param>
    /// <param name="nay">[required]</param>
    /// <param name="plotstyle">GPLOT_LINES, GPLOT_POINTS, GPLOT_IMPULSES,
    /// GPLOT_LINESPOINTS, GPLOT_DOTS</param>
    /// <param name="outformat">GPLOT_PNG, GPLOT_PS, GPLOT_EPS, GPLOT_LATEX, GPLOT_PNM</param>
    /// <param name="outroot">root of output files</param>
    /// <param name="title">[optional], can be null</param>
    /// <returns>gplot</returns>
    /// <remarks>
    /// (1) This generates a plot of a nay vs nax, generated in
    ///     the specified output format.  The title is optional.
    /// (2) Use 0 for default plotstyle (lines).
    /// (3) nax is optional.  If null, nay is plotted against
    ///     the array index.
    /// (4) When calling these simple plot functions more than once, use
    ///     different outroot to avoid overwriting the output files.
    /// </remarks>
    public static Gplot SimpleXY1(Numa? nax, Numa nay, int plotstyle, int outformat,
                                  string outroot, string? title)
    {
        const string func = "gplotSimpleXY1";

        if (plotstyle < 0 || plotstyle >= L.NUM_GPLOT_STYLES)
            throw Lept.Arg(func, "invalid plotstyle");
        if (outformat != L.GPLOT_PNG && outformat != L.GPLOT_PS &&
            outformat != L.GPLOT_EPS && outformat != L.GPLOT_LATEX &&
            outformat != L.GPLOT_PNM)
            throw Lept.Arg(func, "invalid outformat");

        Gplot gplot = Create(outroot, outformat, title, null, null);
        gplot.AddPlot(nax, nay, plotstyle, null);
        gplot.MakeOutput();
        return gplot;
    }

    // leptonica: gplotSimpleXY2 (gplot.c)
    /// <summary>Generates plots of nay1 and nay2 against nax.</summary>
    /// <param name="nax">[optional], can be null</param>
    /// <param name="nay1">first array</param>
    /// <param name="nay2">second array</param>
    /// <param name="plotstyle">GPLOT_LINES, GPLOT_POINTS, GPLOT_IMPULSES,
    /// GPLOT_LINESPOINTS, GPLOT_DOTS</param>
    /// <param name="outformat">GPLOT_PNG, GPLOT_PS, GPLOT_EPS, GPLOT_LATEX, GPLOT_PNM</param>
    /// <param name="outroot">root of output files</param>
    /// <param name="title">[optional]</param>
    /// <returns>gplot</returns>
    /// <remarks>
    /// (1) This generates plots of nay1 and nay2 against nax, generated
    ///     in the specified output format.  The title is optional.
    /// (2) Use 0 for default plotstyle (lines).
    /// (3) nax is optional.  If null, nay1 and nay2 are plotted
    ///     against the array index.
    /// (4) When calling these simple plot functions more than once, use
    ///     different outroot to avoid overwriting the output files.
    /// </remarks>
    public static Gplot SimpleXY2(Numa? nax, Numa nay1, Numa nay2, int plotstyle,
                                  int outformat, string outroot, string? title)
    {
        const string func = "gplotSimpleXY2";

        if (plotstyle < 0 || plotstyle >= L.NUM_GPLOT_STYLES)
            throw Lept.Arg(func, "invalid plotstyle");
        if (outformat != L.GPLOT_PNG && outformat != L.GPLOT_PS &&
            outformat != L.GPLOT_EPS && outformat != L.GPLOT_LATEX &&
            outformat != L.GPLOT_PNM)
            throw Lept.Arg(func, "invalid outformat");

        Gplot gplot = Create(outroot, outformat, title, null, null);
        gplot.AddPlot(nax, nay1, plotstyle, null);
        gplot.AddPlot(nax, nay2, plotstyle, null);
        gplot.MakeOutput();
        return gplot;
    }

    // leptonica: gplotSimpleXYN (gplot.c)
    /// <summary>Generates plots of each Numa in a Numaa against nax.</summary>
    /// <param name="nax">[optional]; can be null</param>
    /// <param name="naay">numaa of arrays to plot against nax</param>
    /// <param name="plotstyle">GPLOT_LINES, GPLOT_POINTS, GPLOT_IMPULSES,
    /// GPLOT_LINESPOINTS, GPLOT_DOTS</param>
    /// <param name="outformat">GPLOT_PNG, GPLOT_PS, GPLOT_EPS, GPLOT_LATEX, GPLOT_PNM</param>
    /// <param name="outroot">root of output files</param>
    /// <param name="title">[optional]</param>
    /// <returns>gplot</returns>
    /// <remarks>
    /// (1) This generates plots of each Numa in naa against nax,
    ///     generated in the specified output format.  The title is optional.
    /// (2) Use 0 for default plotstyle (lines).
    /// (3) nax is optional.  If null, each Numa array is plotted against
    ///     the array index.
    /// (4) When calling these simple plot functions more than once, use
    ///     different outroot to avoid overwriting the output files.
    /// </remarks>
    public static Gplot SimpleXYN(Numa? nax, Numaa naay, int plotstyle, int outformat,
                                  string outroot, string? title)
    {
        const string func = "gplotSimpleXYN";

        int n;
        if ((n = naay.Count) == 0)
            throw Lept.Arg(func, "no numa in array");
        if (plotstyle < 0 || plotstyle >= L.NUM_GPLOT_STYLES)
            throw Lept.Arg(func, "invalid plotstyle");
        if (outformat != L.GPLOT_PNG && outformat != L.GPLOT_PS &&
            outformat != L.GPLOT_EPS && outformat != L.GPLOT_LATEX &&
            outformat != L.GPLOT_PNM)
            throw Lept.Arg(func, "invalid outformat");

        Gplot gplot = Create(outroot, outformat, title, null, null);
        for (int i = 0; i < n; i++) {
            Numa nay = naay.GetNuma(i, L.L_CLONE);
            gplot.AddPlot(nax, nay, plotstyle, null);
        }
        gplot.MakeOutput();
        return gplot;
    }

    // leptonica: gplotGeneralPix1 (gplot.c)
    /// <summary>Generates a plot of a numa as a pix, with title and axis labels.</summary>
    /// <param name="na">data array</param>
    /// <param name="plotstyle">GPLOT_LINES, GPLOT_POINTS, GPLOT_IMPULSES,
    /// GPLOT_LINESPOINTS, GPLOT_DOTS</param>
    /// <param name="rootname">root for all output files</param>
    /// <param name="title">[optional] overall title</param>
    /// <param name="xlabel">[optional] x axis label</param>
    /// <param name="ylabel">[optional] y axis label</param>
    /// <returns>pix of plot</returns>
    /// <remarks>
    /// (1) The 'title', 'xlabel' and 'ylabel' strings can have spaces,
    ///     double quotes and backquotes, but not single quotes.
    /// </remarks>
    public static Pix GeneralPix1(Numa na, int plotstyle, string rootname, string? title,
                                  string? xlabel, string? ylabel)
    {
        if (plotstyle < 0 || plotstyle >= L.NUM_GPLOT_STYLES)
            throw Lept.Arg("gplotGeneralPix1", "invalid plotstyle");

        Gplot gplot = Create(rootname, L.GPLOT_PNG, title, xlabel, ylabel);
        gplot.AddPlot(null, na, plotstyle, null);
        return gplot.MakeOutputPix();
    }

    // leptonica: gplotGeneralPix2 (gplot.c)
    /// <summary>Generates a plot of na2 vs na1 as a pix, with title and axis labels.</summary>
    /// <param name="na1">x-axis data array</param>
    /// <param name="na2">y-axis data array</param>
    /// <param name="plotstyle">GPLOT_LINES, GPLOT_POINTS, GPLOT_IMPULSES,
    /// GPLOT_LINESPOINTS, GPLOT_DOTS</param>
    /// <param name="rootname">root for all output files</param>
    /// <param name="title">[optional] overall title</param>
    /// <param name="xlabel">[optional] x axis label</param>
    /// <param name="ylabel">[optional] y axis label</param>
    /// <returns>pix of plot</returns>
    /// <remarks>
    /// (1) The 'title', 'xlabel' and 'ylabel' strings can have spaces,
    ///     double quotes and backquotes, but not single quotes.
    /// </remarks>
    public static Pix GeneralPix2(Numa na1, Numa na2, int plotstyle, string rootname,
                                  string? title, string? xlabel, string? ylabel)
    {
        if (plotstyle < 0 || plotstyle >= L.NUM_GPLOT_STYLES)
            throw Lept.Arg("gplotGeneralPix2", "invalid plotstyle");

        Gplot gplot = Create(rootname, L.GPLOT_PNG, title, xlabel, ylabel);
        gplot.AddPlot(na1, na2, plotstyle, null);
        return gplot.MakeOutputPix();
    }

    // leptonica: gplotGeneralPixN (gplot.c)
    /// <summary>Generates plots of the numas in a numaa vs nax as a pix, with title and axis labels.</summary>
    /// <param name="nax">x-axis data array</param>
    /// <param name="naay">array of y-axis data arrays</param>
    /// <param name="plotstyle">GPLOT_LINES, GPLOT_POINTS, GPLOT_IMPULSES,
    /// GPLOT_LINESPOINTS, GPLOT_DOTS</param>
    /// <param name="rootname">root for all output files</param>
    /// <param name="title">[optional] overall title</param>
    /// <param name="xlabel">[optional] x axis label</param>
    /// <param name="ylabel">[optional] y axis label</param>
    /// <returns>pix of plot</returns>
    /// <remarks>
    /// (1) The 'title', 'xlabel' and 'ylabel' strings can have spaces,
    ///     double quotes and backquotes, but not single quotes.
    /// </remarks>
    public static Pix GeneralPixN(Numa nax, Numaa naay, int plotstyle, string rootname,
                                  string? title, string? xlabel, string? ylabel)
    {
        const string func = "gplotGeneralPixN";

        int n;
        if ((n = naay.Count) == 0)
            throw Lept.Arg(func, "no numa in array");
        if (plotstyle < 0 || plotstyle >= L.NUM_GPLOT_STYLES)
            throw Lept.Arg(func, "invalid plotstyle");

        Gplot gplot = Create(rootname, L.GPLOT_PNG, title, xlabel, ylabel);
        for (int i = 0; i < n; i++) {
            Numa nay = naay.GetNuma(i, L.L_CLONE);
            gplot.AddPlot(nax, nay, plotstyle, null);
        }
        return gplot.MakeOutputPix();
    }

    /*-----------------------------------------------------------------*
     *                           Serialize for I/O                     *
     *-----------------------------------------------------------------*/

    /// <summary>
    /// The part of fscanf() and fgets() used by gplotRead(): white space in the format
    /// matches any amount of white space, %d reads a decimal int, %s (with an optional
    /// width) reads a word, and any other character must match.  A byte that does not
    /// match is given back, as with ungetc().
    /// </summary>
    private sealed class FileScanner
    {
        private readonly MemoryStream stream;

        public FileScanner(MemoryStream stream)
        {
            this.stream = stream;
        }

        private static bool IsSpace(int c)
        {
            return c == ' ' || (c >= '\t' && c <= '\r');
        }

        private int Read()
        {
            return stream.ReadByte();
        }

        private void Unread(int b)
        {
            if (b >= 0)
                stream.Position--;
        }

        private void SkipSpace()
        {
            int ch;
            while (IsSpace(ch = Read())) { }
            Unread(ch);
        }

        /// <summary>
        /// Returns the number of conversions made.  The converted values are stored in
        /// vals: an int for %d, a string for %s.
        /// </summary>
        public int Scanf(string fmt, object?[] vals)
        {
            int count = 0;
            for (int k = 0; k < fmt.Length; k++) {
                char c = fmt[k];
                if (IsSpace(c)) {
                    SkipSpace();
                } else if (c == '%') {
                    k++;
                    int width = int.MaxValue;
                    if (char.IsDigit(fmt[k])) {
                        width = 0;
                        while (char.IsDigit(fmt[k]))
                            width = width * 10 + (fmt[k++] - '0');
                    }
                    SkipSpace();
                    if (fmt[k] == 'd') {
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
                    } else {  /* %s */
                        var sb = new StringBuilder();
                        int ch;
                        while (sb.Length < width && (ch = Read()) >= 0) {
                            if (IsSpace(ch)) {
                                Unread(ch);
                                break;
                            }
                            sb.Append((char)ch);
                        }
                        if (sb.Length == 0)
                            return count;
                        vals[count++] = sb.ToString();
                    }
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

        /// <summary>fgets(): reads at most size - 1 bytes, up to and including a newline.</summary>
        public string? Fgets(int size)
        {
            var sb = new StringBuilder();
            while (sb.Length < size - 1) {
                int ch = Read();
                if (ch < 0)
                    break;
                sb.Append((char)ch);
                if (ch == '\n')
                    break;
            }
            return sb.Length == 0 ? null : sb.ToString();
        }
    }

    // leptonica: gplotRead (gplot.c)
    /// <summary>Reads a serialized gplot from a file.</summary>
    /// <param name="filename">the file</param>
    /// <returns>gplot</returns>
    public static Gplot Read(string filename)
    {
        const string func = "gplotRead";

        byte[] bytes;
        using (FileStream fs = Utils.FopenReadStream(filename)) {
            using MemoryStream copy = new MemoryStream();
            fs.CopyTo(copy);
            bytes = copy.ToArray();
        }
        MemoryStream fp = new MemoryStream(bytes, false);
        FileScanner sc = new FileScanner(fp);
        object?[] v = new object?[2];

        int ret = sc.Scanf("Gplot Version %d\n", v);
        if (ret != 1)
            throw Lept.Arg(func, $"not a gplot file: {filename}");
        int version = (int)v[0]!;
        if (version != L.GPLOT_VERSION_NUMBER)
            throw Lept.Arg(func, $"invalid gplot version: {filename}");

        string buf = "";
        sc.Scanf("Rootname: %511s\n", v);  /* Bufsize - 1 */
        string rootname = (v[0] as string) ?? "";
        int outformat = 0;
        if (sc.Scanf("Output format: %d\n", v) == 1)
            outformat = (int)v[0]!;
        buf = sc.Fgets(Bufsize) ?? buf;   /* Title: ... */
        string title = StripLabel(buf, 7);
        buf = sc.Fgets(Bufsize) ?? buf;   /* X axis label: ... */
        string xlabel = StripLabel(buf, 14);
        buf = sc.Fgets(Bufsize) ?? buf;   /* Y axis label: ... */
        string ylabel = StripLabel(buf, 14);

        Gplot gplot = Create(rootname, outformat, title, xlabel, ylabel);

        sc.Scanf("Commandfile name: %s\n", v);  /* Bufsize - 1 */
        gplot.CmdName = (v[0] as string) ?? gplot.CmdName;
        sc.Scanf("\nCommandfile data:", v);
        gplot.CmdData = Sarray.ReadStream(fp);
        sc.Scanf("\nDatafile names:", v);
        gplot.DataNames = Sarray.ReadStream(fp);
        sc.Scanf("\nPlot data:", v);
        gplot.PlotData = Sarray.ReadStream(fp);
        sc.Scanf("\nPlot titles:", v);
        gplot.PlotLabels = Sarray.ReadStream(fp);
        sc.Scanf("\nPlot styles:", v);
        try {
            gplot.PlotStyles = Numa.ReadStream(fp);
        } catch (ArgumentException) {
                /* C: plotstyles is NULL and the scan goes on; this is what
                 * happens with a file written by gplotWrite() (see there) */
            Log.Error(func, "plot styles not read");
            gplot.PlotStyles = Numa.Create(0);
        }

        if (sc.Scanf("Number of plots: %d\n", v) == 1)
            gplot.NPlots = (int)v[0]!;
        if (sc.Scanf("Output file name: %s\n", v) == 1)
            gplot.OutName = (string)v[0]!;
        if (sc.Scanf("Axis scaling: %d\n", v) == 1)
            gplot.Scaling = (int)v[0]!;

        return gplot;
    }

    /* stringNew(buf + skip), with the last character (the newline) removed */
    private static string StripLabel(string buf, int skip)
    {
        string s = skip < buf.Length ? buf.Substring(skip) : "";
        return s.Length > 0 ? s.Substring(0, s.Length - 1) : s;
    }

    // leptonica: gplotWrite (gplot.c)
    /// <summary>Writes the serialized gplot to a file.</summary>
    /// <param name="filename">the file</param>
    /// <remarks>
    /// NOTE(upstream): the plot styles are written by numaWriteStderr(), that is,
    /// to stderr and not to the file, so that the file cannot be read back
    /// completely by gplotRead().
    /// </remarks>
    public void Write(string filename)
    {
        using FileStream fp = Utils.FopenWriteStream(filename, "wb");

        CStreamText.Fprintf(fp, "Gplot Version %d\n", L.GPLOT_VERSION_NUMBER);
        CStreamText.Fprintf(fp, "Rootname: %s\n", RootName);
        CStreamText.Fprintf(fp, "Output format: %d\n", OutFormat);
        CStreamText.Fprintf(fp, "Title: %s\n", Title ?? "(null)");
        CStreamText.Fprintf(fp, "X axis label: %s\n", XLabel ?? "(null)");
        CStreamText.Fprintf(fp, "Y axis label: %s\n", YLabel ?? "(null)");

        CStreamText.Fprintf(fp, "Commandfile name: %s\n", CmdName);
        CStreamText.Fprintf(fp, "\nCommandfile data:");
        CmdData.WriteStream(fp);
        CStreamText.Fprintf(fp, "\nDatafile names:");
        DataNames.WriteStream(fp);
        CStreamText.Fprintf(fp, "\nPlot data:");
        PlotData.WriteStream(fp);
        CStreamText.Fprintf(fp, "\nPlot titles:");
        PlotLabels.WriteStream(fp);
        CStreamText.Fprintf(fp, "\nPlot styles:");
        PlotStyles.WriteStderr();

        CStreamText.Fprintf(fp, "Number of plots: %d\n", NPlots);
        CStreamText.Fprintf(fp, "Output file name: %s\n", OutName);
        CStreamText.Fprintf(fp, "Axis scaling: %d\n", Scaling);
    }
}
