using System;
using System.IO;
using Leptonica;

namespace Leptonica.Managed.Tests.GplotTests;

[Collection("LeptDebugOK")]
public class GplotTests : IDisposable
{
    private readonly string dir;

    public GplotTests()
    {
            /* genPathname converts separators to '/' (also on Windows) */
        dir = Path.Combine(Path.GetTempPath(), "lepsharp_gplot_" + Guid.NewGuid().ToString("N")).Replace('\\', '/');
        Directory.CreateDirectory(dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private static Numa MakeNuma(params float[] vals)
    {
        Numa na = Numa.Create(vals.Length);
        foreach (float v in vals) na.AddNumber(v);
        return na;
    }

    [Fact]
    public void Create_SetsNames()
    {
        string root = (dir + "/plot");
        Gplot gplot = Gplot.Create(root, L.GPLOT_PNG, "Title", "x", "y");
        Assert.Equal(root + ".cmd", gplot.CmdName);
        Assert.Equal(root + ".png", gplot.OutName);
        Assert.Equal(L.GPLOT_PNG, gplot.OutFormat);
        Assert.Equal("Title", gplot.Title);
        Assert.Equal("x", gplot.XLabel);
        Assert.Equal("y", gplot.YLabel);
        Assert.Equal(0, gplot.NPlots);

        Assert.Equal(root + ".ps", Gplot.Create(root, L.GPLOT_PS, null, null, null).OutName);
        Assert.Equal(root + ".eps", Gplot.Create(root, L.GPLOT_EPS, null, null, null).OutName);
        Assert.Equal(root + ".tex", Gplot.Create(root, L.GPLOT_LATEX, null, null, null).OutName);
        Assert.Equal(root + ".pnm", Gplot.Create(root, L.GPLOT_PNM, null, null, null).OutName);
    }

    [Fact]
    public void Create_Invalid()
    {
        Assert.Throws<ArgumentException>(() => Gplot.Create("root", L.GPLOT_NONE, null, null, null));
        Assert.Throws<ArgumentException>(() => Gplot.Create("root", 17, null, null, null));
        foreach (string bad in new[] { "a;b", "a|b", "a&b", "`ls`", "$(ls)", "a>b", "a<b", "a\"b", "a?b", "a*b" })
            Assert.Throws<ArgumentException>(() => Gplot.Create(bad, L.GPLOT_PNG, null, null, null));
    }

    [Fact]
    public void AddPlot_GeneratesDataStrings()
    {
        Gplot gplot = Gplot.Create((dir + "/p"), L.GPLOT_PNG, null, null, null);
        gplot.AddPlot(null, MakeNuma(1f, 2.5f), L.GPLOT_LINES, "first");
        Assert.Equal(1, gplot.NPlots);
        Assert.Equal("0.000000 1.000000\n1.000000 2.500000\n", gplot.PlotData.GetString(0, L.L_NOCOPY));
        Assert.Equal((dir + "/p") + ".data.1", gplot.DataNames.GetString(0, L.L_NOCOPY));
        Assert.Equal("first", gplot.PlotLabels.GetString(0, L.L_NOCOPY));
        Assert.Equal(L.GPLOT_LINES, gplot.PlotStyles.GetIValue(0));

        Numa nax = MakeNuma(10f, 20f);
        gplot.AddPlot(nax, MakeNuma(-1f, 0.25f), L.GPLOT_DOTS, null);
        Assert.Equal(2, gplot.NPlots);
        Assert.Equal("10.000000 -1.000000\n20.000000 0.250000\n", gplot.PlotData.GetString(1, L.L_NOCOPY));
        Assert.Equal("", gplot.PlotLabels.GetString(1, L.L_NOCOPY));
    }

    [Fact]
    public void AddPlot_UsesStartAndDel()
    {
        Gplot gplot = Gplot.Create((dir + "/p"), L.GPLOT_PNG, null, null, null);
        Numa na = MakeNuma(5f, 6f, 7f);
        na.SetParameters(2f, 0.5f);
        gplot.AddPlot(null, na, L.GPLOT_POINTS, null);
        Assert.Equal("2.000000 5.000000\n2.500000 6.000000\n3.000000 7.000000\n",
                     gplot.PlotData.GetString(0, L.L_NOCOPY));
    }

    [Fact]
    public void AddPlot_SinglePointLinesBecomePoints()
    {
        Gplot gplot = Gplot.Create((dir + "/p"), L.GPLOT_PNG, null, null, null);
        gplot.AddPlot(null, MakeNuma(3f), L.GPLOT_LINES, null);
        Assert.Equal(L.GPLOT_POINTS, gplot.PlotStyles.GetIValue(0));
    }

    [Fact]
    public void AddPlot_Invalid()
    {
        Gplot gplot = Gplot.Create((dir + "/p"), L.GPLOT_PNG, null, null, null);
        Assert.Throws<ArgumentException>(() => gplot.AddPlot(null, MakeNuma(1f), -1, null));
        Assert.Throws<ArgumentException>(() => gplot.AddPlot(null, MakeNuma(1f), L.NUM_GPLOT_STYLES, null));
        Assert.Throws<ArgumentException>(() => gplot.AddPlot(null, Numa.Create(0), L.GPLOT_LINES, null));
        Assert.Throws<ArgumentException>(() =>
            gplot.AddPlot(MakeNuma(1f), MakeNuma(1f, 2f), L.GPLOT_LINES, null));
    }

    [Fact]
    public void SetScaling_Validates()
    {
        Gplot gplot = Gplot.Create((dir + "/p"), L.GPLOT_PNG, null, null, null);
        gplot.SetScaling(L.GPLOT_LOG_SCALE_X_Y);
        Assert.Equal(L.GPLOT_LOG_SCALE_X_Y, gplot.Scaling);
        Assert.Throws<ArgumentException>(() => gplot.SetScaling(4));
        Assert.Throws<ArgumentException>(() => gplot.SetScaling(-1));
    }

    [Fact]
    public void GenCommandFile_SinglePlot()
    {
        string root = (dir + "/single");
        Gplot gplot = Gplot.Create(root, L.GPLOT_PNG, "T", "xx", "yy");
        gplot.AddPlot(null, MakeNuma(1f, 2f), L.GPLOT_LINES, "lbl");
        gplot.GenCommandFile();
        string expected =
            "set title 'T'\n" +
            "set xlabel 'xx'\n" +
            "set ylabel 'yy'\n" +
            $"set terminal png; set output '{root}.png'\n" +
            $"plot '{root}.data.1' title 'lbl' with lines\n";
        Assert.Equal(expected, File.ReadAllText(root + ".cmd"));
    }

    [Fact]
    public void GenCommandFile_MultiplePlotsAndScaling()
    {
        string root = (dir + "/multi");
        Gplot gplot = Gplot.Create(root, L.GPLOT_EPS, null, null, null);
        gplot.AddPlot(null, MakeNuma(1f, 2f), L.GPLOT_LINES, "a");
        gplot.AddPlot(null, MakeNuma(1f, 2f), L.GPLOT_IMPULSES, "b");
        gplot.AddPlot(null, MakeNuma(1f, 2f), L.GPLOT_LINESPOINTS, "c");
        gplot.SetScaling(L.GPLOT_LOG_SCALE_X_Y);
        gplot.GenCommandFile();
        string expected =
            $"set terminal postscript eps; set output '{root}.eps'\n" +
            "set logscale x\n" +
            "set logscale y\n" +
            $"plot '{root}.data.1' title 'a' with lines, \\\n" +
            $" '{root}.data.2' title 'b' with impulses, \\\n" +
            $" '{root}.data.3' title 'c' with linespoints\n";
        Assert.Equal(expected, File.ReadAllText(root + ".cmd"));

        // generating again replaces the previous command data
        gplot.GenCommandFile();
        Assert.Equal(expected, File.ReadAllText(root + ".cmd"));
    }

    [Fact]
    public void GenCommandFile_TerminalPerFormat()
    {
        string root = (dir + "/fmt");
        (int fmt, string terminal)[] cases =
        {
            (L.GPLOT_PS, "set terminal postscript; set output '" + root + ".ps'"),
            (L.GPLOT_LATEX, "set terminal latex; set output '" + root + ".tex'"),
            (L.GPLOT_PNM, "set terminal pbm color; set output '" + root + ".pnm'"),
        };
        foreach (var (fmt, terminal) in cases) {
            Gplot gplot = Gplot.Create(root, fmt, null, null, null);
            gplot.GenCommandFile();
            Assert.StartsWith(terminal + "\n", File.ReadAllText(root + ".cmd"));
        }
    }

    [Fact]
    public void GenDataFiles_WritesPlotData()
    {
        string root = (dir + "/data");
        Gplot gplot = Gplot.Create(root, L.GPLOT_PNG, null, null, null);
        gplot.AddPlot(null, MakeNuma(4f, 5f), L.GPLOT_LINES, null);
        gplot.AddPlot(MakeNuma(1f, 2f), MakeNuma(8f, 9f), L.GPLOT_LINES, null);
        gplot.GenDataFiles();
        Assert.Equal("0.000000 4.000000\n1.000000 5.000000\n", File.ReadAllText(root + ".data.1"));
        Assert.Equal("1.000000 8.000000\n2.000000 9.000000\n", File.ReadAllText(root + ".data.2"));
    }

    [Fact]
    public void MakeOutput_DisabledWritesNothing()
    {
        using var debug = new LeptDebugScope(false);
        string root = (dir + "/off");
        Gplot gplot = Gplot.Create(root, L.GPLOT_PNG, null, null, null);
        gplot.AddPlot(null, MakeNuma(1f, 2f), L.GPLOT_LINES, null);
        gplot.MakeOutput();
        Assert.False(File.Exists(root + ".cmd"));
        Assert.False(File.Exists(root + ".data.1"));
    }

    [Fact]
    public void MakeOutputPix_RequiresImageFormat()
    {
        Gplot gplot = Gplot.Create((dir + "/ps"), L.GPLOT_PS, null, null, null);
        Assert.Throws<ArgumentException>(() => gplot.MakeOutputPix());
    }

    [Fact]
    public void SimpleXY_Validates()
    {
        Numa na = MakeNuma(1f, 2f);
        string root = (dir + "/s");
        Assert.Throws<ArgumentException>(() =>
            Gplot.SimpleXY1(null, na, 9, L.GPLOT_PNG, root, null));
        Assert.Throws<ArgumentException>(() =>
            Gplot.SimpleXY1(null, na, L.GPLOT_LINES, L.GPLOT_NONE, root, null));
        Assert.Throws<ArgumentException>(() =>
            Gplot.SimpleXY2(null, na, na, 9, L.GPLOT_PNG, root, null));
        Assert.Throws<ArgumentException>(() =>
            Gplot.SimpleXYN(null, Numaa.Create(0), L.GPLOT_LINES, L.GPLOT_PNG, root, null));
    }

    [Fact]
    public void SimpleXY_BuildsPlots()
    {
        Numa na = MakeNuma(1f, 2f);
        string root = (dir + "/s");
        Gplot g1 = Gplot.SimpleXY1(null, na, L.GPLOT_LINES, L.GPLOT_PNG, root, "t");
        Assert.Equal(1, g1.NPlots);
        Assert.Equal("t", g1.Title);
        Gplot g2 = Gplot.SimpleXY2(null, na, na, L.GPLOT_POINTS, L.GPLOT_PS, root, null);
        Assert.Equal(2, g2.NPlots);
        Numaa naa = Numaa.Create(2);
        naa.AddNuma(na, L.L_COPY);
        naa.AddNuma(na, L.L_COPY);
        naa.AddNuma(na, L.L_COPY);
        Gplot gn = Gplot.SimpleXYN(null, naa, L.GPLOT_LINES, L.GPLOT_PNG, root, null);
        Assert.Equal(3, gn.NPlots);
        Gplot.Simple1(na, L.GPLOT_PNG, root, null);
        Gplot.Simple2(na, na, L.GPLOT_PNG, root, null);
        Gplot.SimpleN(naa, L.GPLOT_PNG, root, null);
    }

    [Fact]
    public void WriteRead_RoundTripOfWrittenFields()
    {
        string root = (dir + "/rw");
        Gplot gplot = Gplot.Create(root, L.GPLOT_PNG, "My title", "xl", "yl");
        gplot.AddPlot(null, MakeNuma(1f, 2f), L.GPLOT_LINES, "one");
        gplot.AddPlot(null, MakeNuma(3f, 4f), L.GPLOT_POINTS, "two");
        gplot.GenCommandFile();
        string file = (dir + "/gplot.ser");
        gplot.Write(file);

        string text = File.ReadAllText(file);
        Assert.StartsWith("Gplot Version 1\nRootname: " + root + "\nOutput format: 1\nTitle: My title\n" +
                          "X axis label: xl\nY axis label: yl\nCommandfile name: " + root + ".cmd\n", text);
        Assert.EndsWith("Number of plots: 2\nOutput file name: " + root + ".png\nAxis scaling: 0\n", text);

        Gplot back = Gplot.Read(file);
        Assert.Equal(gplot.RootName, back.RootName);
        Assert.Equal(L.GPLOT_PNG, back.OutFormat);
        Assert.Equal("My title", back.Title);
        Assert.Equal("xl", back.XLabel);
        Assert.Equal("yl", back.YLabel);
        Assert.Equal(gplot.CmdName, back.CmdName);
        Assert.Equal(gplot.CmdData.Count, back.CmdData.Count);
        Assert.Equal(gplot.DataNames.GetString(1, L.L_NOCOPY), back.DataNames.GetString(1, L.L_NOCOPY));
        Assert.Equal(gplot.PlotData.GetString(1, L.L_NOCOPY), back.PlotData.GetString(1, L.L_NOCOPY));
        Assert.Equal("two", back.PlotLabels.GetString(1, L.L_NOCOPY));
    }

    [Fact]
    public void Read_RejectsOtherFiles()
    {
        string file = (dir + "/bad.ser");
        File.WriteAllText(file, "Not a gplot\n");
        Assert.Throws<ArgumentException>(() => Gplot.Read(file));
        File.WriteAllText(file, "Gplot Version 7\n");
        Assert.Throws<ArgumentException>(() => Gplot.Read(file));
    }
}
