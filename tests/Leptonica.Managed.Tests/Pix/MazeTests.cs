using System;
using Leptonica;

namespace Leptonica.Managed.Tests.PixTests;

public class MazeTests
{
    private static uint Get(Pix pix, int x, int y)
    {
        pix.GetPixel(x, y, out uint v);
        return v;
    }

    private static void Pt(Pta pta, int i, out int x, out int y)
    {
        pta.GetPt(i, out float fx, out float fy);
        x = (int)fx;
        y = (int)fy;
    }

    private static void AssertValidPath(Pix maze, Pta pta, int xi, int yi, int xf, int yf)
    {
        Pt(pta, 0, out int x0, out int y0);
        Assert.Equal((xf, yf), (x0, y0));
        Pt(pta, pta.Count - 1, out int xl, out int yl);
        Assert.Equal((xi, yi), (xl, yl));
        for (int i = 0; i < pta.Count; i++) {
            Pt(pta, i, out int x, out int y);
            Assert.Equal(0u, Get(maze, x, y));    /* never on a wall */
            if (i > 0) {
                Pt(pta, i - 1, out int px, out int py);
                Assert.Equal(1, Math.Abs(px - x) + Math.Abs(py - y));
            }
        }
    }

    private static Pix WallMaze()
    {
        Pix maze = Pix.Create(60, 60, 1);
        for (int y = 0; y <= 40; y++)
            maze.SetPixel(30, y, 1);
        return maze;
    }

    [Fact]
    public void Generate_IsDeterministicAndRespectsTheMinimumSize()
    {
        Pix a = Pix.GenerateBinaryMaze(20, 30, 0, 0, 0.0f, 0.0f);
        Pix b = Pix.GenerateBinaryMaze(20, 30, 0, 0, 0.0f, 0.0f);
        a.GetDimensions(out int w, out int h, out int d);
        Assert.Equal((50, 50, 1), (w, h, d));
        Assert.True(Pix.Equal(a, b));
        Assert.Equal(0u, Get(a, 50 / 6, 50 / 5));   /* the initial location is OFF */
        double density = a.CountPixels(null) / (double)(w * h);
        Assert.InRange(density, 0.05, 0.7);
        Pix c = Pix.GenerateBinaryMaze(120, 90, 10, 10, 0.5f, 0.5f);
        Assert.Equal(0u, Get(c, 10, 10));
        c.GetDimensions(out w, out h, out _);
        Assert.Equal((120, 90), (w, h));
    }

    [Fact]
    public void Generate_DifferentParametersGiveDifferentMazes()
    {
        Pix a = Pix.GenerateBinaryMaze(100, 100, 0, 0, 0.45f, 0.7f);
        Pix b = Pix.GenerateBinaryMaze(100, 100, 0, 0, 0.65f, 0.25f);
        Assert.False(Pix.Equal(a, b));
    }

    [Fact]
    public void SearchBinary_EmptyMazeGivesAShortestPath()
    {
        Pix maze = Pix.Create(60, 60, 1);
        Pta pta = maze.SearchBinaryMaze(1, 1, 58, 58, out Pix pixd)!;
        Assert.NotNull(pta);
        Assert.Equal(57 + 57 + 1, pta.Count);
        AssertValidPath(maze, pta, 1, 1, 58, 58);
        Assert.Equal(32, pixd.Depth);
        Pix.ComposeRGBPixel(255, 0, 0, out uint red);
        Pix.ComposeRGBPixel(0, 0, 255, out uint blue);
        Pix.ComposeRGBPixel(0, 255, 0, out uint green);
        Assert.Equal(red, Get(pixd, 1, 1));
        Assert.Equal(blue, Get(pixd, 58, 58));
        Pt(pta, 5, out int x, out int y);
        Assert.Equal(green, Get(pixd, x, y));
    }

    [Fact]
    public void SearchBinary_DetoursAroundTheWall()
    {
        Pix maze = WallMaze();
        Pta pta = maze.SearchBinaryMaze(1, 10, 58, 10, out _)!;
        Assert.NotNull(pta);
        Assert.Equal(120, pta.Count);               /* 29 + 31 + 28 + 31 steps */
        AssertValidPath(maze, pta, 1, 10, 58, 10);
    }

    [Fact]
    public void SearchBinary_NoPathPaintsTheSearchedPart()
    {
        Pix maze = Pix.Create(60, 60, 1);
        for (int i = 44; i <= 56; i++) {            /* a closed box around (50, 50) */
            maze.SetPixel(i, 44, 1);
            maze.SetPixel(i, 56, 1);
            maze.SetPixel(44, i, 1);
            maze.SetPixel(56, i, 1);
        }
        Pta? pta = maze.SearchBinaryMaze(1, 1, 50, 50, out Pix pixd);
        Assert.Null(pta);
        Pix.ComposeRGBPixel(0, 255, 0, out uint green);
        Assert.Equal(green, Get(pixd, 20, 20));      /* visited */
        Assert.NotEqual(green, Get(pixd, 50, 48));   /* inside the box: never visited */
    }

    [Fact]
    public void SearchBinary_EndPointOnAWallMovesToANearbyBackground()
    {
        Pix maze = Pix.Create(60, 60, 1);
        maze.SetPixel(40, 40, 1);
        Pta pta = maze.SearchBinaryMaze(1, 1, 40, 40, out _)!;
        Assert.NotNull(pta);
        Pt(pta, 0, out int x, out int y);
        Assert.NotEqual((40, 40), (x, y));
        Assert.True(Math.Abs(x - 40) <= 1 && Math.Abs(y - 40) <= 1);
    }

    [Fact]
    public void SearchBinary_InvalidArguments()
    {
        Pix maze = Pix.Create(60, 60, 1);
        maze.SetPixel(5, 5, 1);
        Assert.Throws<ArgumentException>(() => Pix.Create(60, 60, 8).SearchBinaryMaze(1, 1, 5, 5, out _));
        Assert.Throws<ArgumentException>(() => maze.SearchBinaryMaze(0, 1, 5, 5, out _));
        Assert.Throws<ArgumentException>(() => maze.SearchBinaryMaze(1, 60, 5, 5, out _));
        Assert.Throws<ArgumentException>(() => maze.SearchBinaryMaze(5, 5, 20, 20, out _));
    }

    [Fact]
    public void SearchBinary_OnAGeneratedMaze()
    {
        Pix maze = Pix.GenerateBinaryMaze(100, 100, 0, 0, 0.0f, 0.0f);
        int xi = 100 / 6, yi = 100 / 5;
        Pta? pta = maze.SearchBinaryMaze(xi, yi, 90, 90, out Pix pixd);
        Assert.NotNull(pixd);
        if (pta != null) {
            Pt(pta, 0, out int xf, out int yf);
            AssertValidPath(maze, pta, xi, yi, xf, yf);
        }
    }

    [Fact]
    public void SearchGray_FlatImageGivesAShortestPath()
    {
        Pix pix = Pix.Create(60, 60, 8);
        for (int i = 0; i < 60; i++)
            for (int j = 0; j < 60; j++)
                pix.SetPixel(j, i, 100);
        Pta pta = pix.SearchGrayMaze(1, 1, 58, 58, out Pix pixd);
        Assert.Equal(115, pta.Count);
        Pt(pta, 0, out int x0, out int y0);
        Pt(pta, pta.Count - 1, out int xl, out int yl);
        Assert.Equal((58, 58, 1, 1), (x0, y0, xl, yl));
        Assert.Equal(32, pixd.Depth);
        Pix.ComposeRGBPixel(255, 0, 0, out uint red);
        Assert.Equal(red, Get(pixd, 1, 1));
    }

    [Fact]
    public void SearchGray_AvoidsAHighRidge()
    {
        Pix pix = Pix.Create(60, 60, 8);
        for (int i = 0; i < 60; i++)
            for (int j = 0; j < 60; j++)
                pix.SetPixel(j, i, (j == 30 && i <= 40) ? 255u : 100u);
        Pta pta = pix.SearchGrayMaze(1, 10, 58, 10, out _);
        Assert.Equal(120, pta.Count);               /* through the gap, as in the binary maze */
        for (int i = 0; i < pta.Count; i++) {
            Pt(pta, i, out int x, out int y);
            Assert.NotEqual(255u, Get(pix, x, y));
        }
    }

    [Fact]
    public void SearchGray_InvalidArguments()
    {
        Pix pix = Pix.Create(60, 60, 8);
        Assert.Throws<ArgumentException>(() => Pix.Create(40, 60, 8).SearchGrayMaze(1, 1, 5, 5, out _));
        Assert.Throws<ArgumentException>(() => Pix.Create(60, 60, 1).SearchGrayMaze(1, 1, 5, 5, out _));
        Assert.Throws<ArgumentException>(() => pix.SearchGrayMaze(0, 1, 5, 5, out _));
        Assert.Throws<ArgumentException>(() => pix.SearchGrayMaze(1, 0, 5, 5, out _));
        Assert.Throws<ArgumentException>(() => pix.SearchGrayMaze(1, 1, 60, 5, out _));
    }
}
