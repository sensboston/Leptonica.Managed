using System;
using System.IO;
using System.Linq;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests;

/// <summary>Tests for sudoku.c.</summary>
public class SudokuTests
{
    private const string Solution = "3 8 7 2 6 4 1 9 5 " +
                                    "2 6 5 8 9 1 4 3 7 " +
                                    "1 4 9 5 3 7 6 8 2 " +
                                    "5 2 3 7 1 6 8 4 9 " +
                                    "7 1 6 9 4 8 2 5 3 " +
                                    "8 9 4 3 5 2 7 1 6 " +
                                    "9 7 2 1 8 5 3 6 4 " +
                                    "4 3 1 6 7 9 5 2 8 " +
                                    "6 5 8 4 2 3 9 7 1";

    /// <summary>The "5 star" puzzle of prog/sudoku1.dat, whose solution is the one above.</summary>
    private const string Puzzle = "3 8 0 2 0 0 0 9 0 " +
                                  "2 0 0 0 9 1 0 0 7 " +
                                  "0 4 0 0 3 0 0 0 0 " +
                                  "0 2 0 0 0 0 0 0 0 " +
                                  "0 1 6 0 0 0 2 5 0 " +
                                  "0 0 0 0 0 0 0 1 0 " +
                                  "0 0 0 0 8 0 0 6 0 " +
                                  "4 0 0 6 7 0 0 0 8 " +
                                  "0 5 0 0 0 3 0 7 1";

    [Fact]
    public void ReadStringParses81Numbers()
    {
        int[] array = L_Sudoku.ReadString(Solution);
        Assert.Equal(81, array.Length);
        Assert.Equal(Solution.Split(' ').Select(int.Parse).ToArray(), array);
        Assert.Throws<ArgumentException>(() => L_Sudoku.ReadString("1 2 3"));
        Assert.Throws<ArgumentException>(() => L_Sudoku.ReadString(Solution.Replace("5", "x")));
    }

    [Fact]
    public void ReadFileSkipsCommentsAndChecksTheFormat()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sudoku_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            string[] rows = Enumerable.Range(0, 9)
                .Select(r => string.Join(" ", Puzzle.Split(' ').Skip(9 * r).Take(9))).ToArray();
            string good = Path.Combine(dir, "good.dat");
            File.WriteAllText(good, "# a comment\n# another\n" + string.Join("\n", rows) + "\n");
            Assert.Equal(L_Sudoku.ReadString(Puzzle), L_Sudoku.ReadFile(good));

            string crlf = Path.Combine(dir, "crlf.dat");
            File.WriteAllText(crlf, "# c\r\n" + string.Join("\r\n", rows) + "\r\n");
            Assert.Equal(L_Sudoku.ReadString(Puzzle), L_Sudoku.ReadFile(crlf));

            string fewlines = Path.Combine(dir, "few.dat");
            File.WriteAllText(fewlines, string.Join("\n", rows.Take(8)));
            Assert.Throws<ArgumentException>(() => L_Sudoku.ReadFile(fewlines));

            string badrow = Path.Combine(dir, "bad.dat");
            File.WriteAllText(badrow, string.Join("\n", rows.Take(8)) + "\n1 2 3\n");
            Assert.Throws<ArgumentException>(() => L_Sudoku.ReadFile(badrow));

            string notnum = Path.Combine(dir, "notnum.dat");
            File.WriteAllText(notnum, string.Join("\n", rows.Take(8)) + "\n1 2 3 4 5 6 7 8 x\n");
            Assert.Throws<ArgumentException>(() => L_Sudoku.ReadFile(notnum));
        } finally {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SolvesTheHardPuzzle()
    {
        L_Sudoku sud = L_Sudoku.Create(L_Sudoku.ReadString(Puzzle));
        Assert.Equal(55, sud.Num);   /* 26 givens */
        Assert.True(sud.Solve());
        Assert.True(sud.Finished);
        Assert.False(sud.Failure);
        Assert.Equal(L_Sudoku.ReadString(Solution), sud.State);
        Assert.Equal(L_Sudoku.ReadString(Puzzle), sud.Init);   /* the initial state is kept */
        Assert.True(sud.NGuess > 1000);
    }

    [Fact]
    public void SolutionIsUniqueForTheHardPuzzle()
    {
        Assert.True(L_Sudoku.TestUniqueness(L_Sudoku.ReadString(Puzzle)));

        /* a nearly empty grid has many solutions, which the four rotated brute force searches find differently */
        int[] sparse = new int[81];
        sparse[0] = 1;
        Assert.False(L_Sudoku.TestUniqueness(sparse));
    }

    [Fact]
    public void ImpossiblePuzzlesFail()
    {
        /* valid initial state, but the last cell of the first row can not take any value:
         * 1..8 are in the row, 9 is in its column */
        int[] array = new int[81];
        for (int i = 0; i < 8; i++)
            array[i] = i + 1;
        array[17] = 9;
        L_Sudoku sud = L_Sudoku.Create(array);
        Assert.False(sud.Solve());
        Assert.True(sud.Failure);

        /* invalid initial state: two 5s in a row */
        int[] bad = new int[81];
        bad[0] = 5;
        bad[4] = 5;
        Assert.Throws<LeptonicaException>(() => L_Sudoku.Create(bad).Solve());
    }

    [Fact]
    public void GeneratedSudokuHasAUniqueSolutionAndEnoughRemovals()
    {
        int[] array = L_Sudoku.ReadString(Solution);
        L_Sudoku sud = L_Sudoku.Generate(array, 3693, 28, 7);
        int nelems = sud.Init.Count(v => v != 0);
        Assert.InRange(nelems, 28, 51);   /* at least 30 numbers were removed, so at most 51 are left */
        Assert.True(sud.Finished);
        Assert.Equal(L_Sudoku.ReadString(Solution), sud.State);
        Assert.True(L_Sudoku.TestUniqueness(sud.Init));
        /* the input array is modified in place to the puzzle */
        Assert.Equal(sud.Init, array);

        /* the same seed gives the same puzzle */
        int[] array2 = L_Sudoku.ReadString(Solution);
        Assert.Equal(sud.Init, L_Sudoku.Generate(array2, 3693, 28, 7).Init);

        Assert.Throws<ArgumentException>(() => L_Sudoku.Generate(L_Sudoku.ReadString(Solution), 1, 81, 5));
        /* an invalid 'solution' (two 3s in the first row, not removed by the single random removal) is detected */
        int[] invalid = L_Sudoku.ReadString(Solution);
        invalid[1] = invalid[0];
        Assert.Throws<LeptonicaException>(() => L_Sudoku.Generate(invalid, 3, 80, 7));
    }

    [Fact]
    public void OutputChecksTheArrayType()
    {
        L_Sudoku sud = L_Sudoku.Create(L_Sudoku.ReadString(Puzzle));
        Assert.Throws<ArgumentException>(() => sud.Output(5));
        var stderr = new StringWriter();
        TextWriter old = Console.Error;
        Console.SetError(stderr);
        try {
            sud.Output(L.L_SUDOKU_INIT);
        } finally {
            Console.SetError(old);
        }
        string[] lines = stderr.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(9, lines.Length);
        Assert.Equal("3 8 0 2 0 0 0 9 0 ", lines[0]);
    }
}
