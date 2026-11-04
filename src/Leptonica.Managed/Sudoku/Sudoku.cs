using System;
using System.Text;

namespace Leptonica;

/*
 *  sudoku.c: solve a sudoku by brute force search.
 *
 *      Read input data from file or string
 *          L_Sudoku.ReadFile (sudokuReadFile)
 *          L_Sudoku.ReadString (sudokuReadString)
 *
 *      Create/destroy
 *          L_Sudoku.Create (sudokuCreate)
 *
 *      Solve the puzzle
 *          L_Sudoku.Solve (sudokuSolve)
 *          static ValidState (sudokuValidState)
 *          static NewGuess (sudokuNewGuess)
 *          static TestState (sudokuTestState)
 *
 *      Test for uniqueness
 *          L_Sudoku.TestUniqueness (sudokuTestUniqueness)
 *          static CompareState (sudokuCompareState)
 *          static RotateArray (sudokuRotateArray)
 *
 *      Generation
 *          L_Sudoku.Generate (sudokuGenerate)
 *
 *      Output
 *          L_Sudoku.Output (sudokuOutput)
 *
 *  Not ported: sudokuDestroy (memory management only).
 *
 *  Solving sudokus is a somewhat addictive pastime.  The rules are
 *  simple but it takes just enough concentration to make it rewarding
 *  when you find a number.  And you get 50 to 60 such rewards each time
 *  you complete one.  The downside is that you could have been doing
 *  something more creative, like keying out a new plant, staining
 *  the deck, or even writing a computer program to discourage your
 *  wife from doing sudokus.
 *
 *  My original plan for the sudoku solver was somewhat grandiose.
 *  The program would model the way a person solves the problem.
 *  It would examine each empty position and determine how many possible
 *  numbers could fit.  The empty positions would be entered in a priority
 *  queue keyed on the number of possible numbers that could fit.
 *  If there existed a position where only a single number would work,
 *  it would greedily take it.  Otherwise it would consider a
 *  positions that could accept two and make a guess, with backtracking
 *  if an impossible state were reached.  And so on.
 *
 *  Then one of my colleagues announced she had solved the problem
 *  by brute force and it was fast.  At that point the original plan was
 *  dead in the water, because the two top requirements for a leptonica
 *  algorithm are (1) as simple as possible and (2) fast.  The brute
 *  force approach starts at the UL corner, and in succession at each
 *  blank position it finds the first valid number (testing in
 *  sequence from 1 to 9).  When no number will fit a blank position
 *  it backtracks, choosing the next valid number in the previous
 *  blank position.
 *
 *  This is an inefficient method for pruning the space of solutions
 *  (imagine backtracking from the LR corner back to the UL corner
 *  and starting over with a new guess), but it nevertheless gets
 *  the job done quickly.  I have made no effort to optimize
 *  it, because it is fast: a 5-star (highest difficulty) sudoku might
 *  require a million guesses and take 0.05 sec.  (This BF implementation
 *  does about 20M guesses/sec at 3 GHz.)
 *
 *  Proving uniqueness of a sudoku solution is tricker than finding
 *  a solution (or showing that no solution exists).  A good indication
 *  that a solution is unique is if we get the same result solving
 *  by brute force when the puzzle is also rotated by 90, 180 and 270
 *  degrees.  If there are multiple solutions, it seems unlikely
 *  that you would get the same solution four times in a row, using a
 *  brute force method that increments guesses and scans LR/TB.
 *  The function sudokuTestUniqueness() does this.
 *
 *  And given a function that can determine uniqueness, it is
 *  easy to generate valid sudokus.  We provide sudokuGenerate(),
 *  which starts with some valid initial solution, and randomly
 *  removes numbers, stopping either when a minimum number of non-zero
 *  elements are left, or when it becomes difficult to remove another
 *  element without destroying the uniqueness of the solution.
 *
 *  For further reading, see the Wikipedia articles:
 *     (1) http://en.wikipedia.org/wiki/Algorithmics_of_sudoku
 *     (2) http://en.wikipedia.org/wiki/Sudoku
 *
 *  How many 9x9 sudokus are there?  Here are the numbers.
 *   ~ From ref(1), there are about 6 x 10^27 "latin squares", where
 *     each row and column has all 9 digits.
 *   ~ There are 7.2 x 10^21 actual solutions, having the added
 *     constraint in each of the 9 3x3 squares.  (The constraint
 *     reduced the number by the fraction 1.2 x 10^(-6).)
 *   ~ There are a mere 5.5 billion essentially different solutions (EDS),
 *     when symmetries (rotation, reflection, permutation and relabelling)
 *     are removed.
 *   ~ Thus there are 1.3 x 10^12 solutions that can be derived by
 *     symmetry from each EDS.  Can we account for these?
 *   ~ Sort-of.  From an EDS, you can derive (3!)^8 = 1.7 million solutions
 *     by simply permuting rows and columns.  (Do you see why it is
 *     not (3!)^6 ?)
 *   ~ Also from an EDS, you can derive 9! solutions by relabelling,
 *     and 4 solutions by rotation, for a total of 1.45 million solutions
 *     by relabelling and rotation.  Then taking the product, by symmetry
 *     we can derive 1.7M x 1.45M = 2.45 trillion solutions from each EDS.
 *     (Something is off by about a factor of 2 -- close enough.)
 *
 *  Another interesting fact is that there are apparently 48K EDS sudokus
 *  (with unique solutions) that have only 17 givens.  No sudokus are known
 *  with less than 17, but there exists no proof that this is the minimum.
 *
 *  Differences from C:
 *    - The messages that C prints to stderr in sudokuSolve() and sudokuGenerate()
 *      are logged (Log.Info); sudokuOutput() still writes to stderr.
 */

/// <summary>Constants from sudoku.h.</summary>
public static partial class L
{
    /* For printing out array data */
    /// <summary>Print the initial (given) puzzle values.</summary>
    public const int L_SUDOKU_INIT = 0;
    /// <summary>Print the current state of the puzzle.</summary>
    public const int L_SUDOKU_STATE = 1;
}

/// <summary>A sudoku puzzle and its solution state (struct L_Sudoku).</summary>
public sealed class L_Sudoku
{
    /// <summary>number of unknowns</summary>
    public int Num { get; private set; }
    /// <summary>location of unknowns</summary>
    public int[] Locs { get; private set; }
    /// <summary>index into %locs of current location</summary>
    public int Current { get; private set; }
    /// <summary>initial state, with 0 representing the unknowns</summary>
    public int[] Init { get; private set; }
    /// <summary>present state, including inits and guesses of unknowns up to %current</summary>
    public int[] State { get; private set; }
    /// <summary>shows current number of guesses</summary>
    public int NGuess { get; private set; }
    /// <summary>set to true when solved</summary>
    public bool Finished { get; private set; }
    /// <summary>set to true if no solution is possible</summary>
    public bool Failure { get; private set; }

    private L_Sudoku()
    {
        Locs = new int[81];
        Init = new int[81];
        State = new int[81];
    }

    /* --------------------------------------------------------------- */
    /*               An example of a valid solution                    */
    /* --------------------------------------------------------------- *
    static const char valid_solution[] = "3 8 7 2 6 4 1 9 5 "
                                         "2 6 5 8 9 1 4 3 7 "
                                         "1 4 9 5 3 7 6 8 2 "
                                         "5 2 3 7 1 6 8 4 9 "
                                         "7 1 6 9 4 8 2 5 3 "
                                         "8 9 4 3 5 2 7 1 6 "
                                         "9 7 2 1 8 5 3 6 4 "
                                         "4 3 1 6 7 9 5 2 8 "
                                         "6 5 8 4 2 3 9 7 1 ";
    */

    /*---------------------------------------------------------------------*
     *               Read input data from file or string                   *
     *---------------------------------------------------------------------*/
    // leptonica: sudokuReadFile (sudoku.c)
    /// <param name="filename">formatted sudoku file</param>
    /// <returns>array of 81 numbers</returns>
    /// <remarks>
    /// (1) The file format has:
    ///     * any number of comment lines beginning with '#'
    ///     * a set of 9 lines, each having 9 digits (0-9) separated
    ///       by a space
    /// </remarks>
    public static int[] ReadFile(string filename)
    {
        byte[] data = Utils.BinaryRead(filename);
        Sarray sa1 = Sarray.CreateLinesFromString(Encoding.Latin1.GetString(data), 0);
        Sarray sa2 = Sarray.Create(9);

            /* Filter out the comment lines; verify that there are 9 data lines */
        int nlines = sa1.Count;
        for (int i = 0; i < nlines; i++) {
            string str = sa1.GetString(i, L.L_NOCOPY);
            if (str[0] != '#')
                sa2.AddString(str, L.L_COPY);
        }
        nlines = sa2.Count;
        if (nlines != 9) {
            Log.Error("sudokuReadFile", $"file has {nlines} lines");
            throw Lept.Arg("sudokuReadFile", "invalid file");
        }

            /* Read the data into the array, verifying that each data
             * line has 9 numbers. */
        bool error = false;
        int[] array = new int[81];
        int index = 0;
        for (int i = 0; i < 9; i++) {
            string str = sa2.GetString(i, L.L_NOCOPY);
            Sarray saline = Sarray.CreateWordsFromString(str);
            if (saline.Count != 9) {
                error = true;
                break;
            }
            for (int j = 0; j < 9; j++) {
                string strj = saline.GetString(j, L.L_NOCOPY);
                if (CFormat.Sscanf(strj, "%d", out object?[] v) != 1)
                    error = true;
                else
                    array[index++] = (int)v[0]!;
            }
            if (error) break;
        }

        if (error)
            throw Lept.Arg("sudokuReadFile", "invalid data");

        return array;
    }

    // leptonica: sudokuReadString (sudoku.c)
    /// <param name="str">formatted input data</param>
    /// <returns>array of 81 numbers</returns>
    /// <remarks>
    /// (1) The string is formatted as 81 single digits, each separated
    ///     by 81 spaces.
    /// </remarks>
    public static int[] ReadString(string str)
    {
            /* Read in the initial solution */
        int[] array = new int[81];
        for (int i = 0; i < 81; i++) {
            if (2 * i > str.Length ||
                CFormat.Sscanf(str.Substring(2 * i), "%d ", out object?[] v) != 1)
                throw Lept.Arg("sudokuReadString", "invalid format");
            array[i] = (int)v[0]!;
        }

        return array;
    }

    /*---------------------------------------------------------------------*
     *                       Create/destroy sudoku                         *
     *---------------------------------------------------------------------*/
    // leptonica: sudokuCreate (sudoku.c)
    /// <param name="array">81 numbers, 9 rows of 9 numbers each</param>
    /// <remarks>
    /// (1) The input array has 0 for the unknown values, and 1-9
    ///     for the known initial values.  It is generated from
    ///     a file using sudokuReadInput(), which checks that the file
    ///     data has 81 numbers in 9 rows.
    /// </remarks>
    public static L_Sudoku Create(int[] array)
    {
        int locs_index = 0;  /* into locs array */
        L_Sudoku sud = new L_Sudoku();
        for (int i = 0; i < 81; i++) {
            int val = array[i];
            sud.Init[i] = val;
            sud.State[i] = val;
            if (val == 0)
                sud.Locs[locs_index++] = i;
        }
        sud.Num = locs_index;
        sud.Failure = false;
        sud.Finished = false;
        return sud;
    }

    /*---------------------------------------------------------------------*
     *                           Solve the puzzle                          *
     *---------------------------------------------------------------------*/
    // leptonica: sudokuSolve (sudoku.c)
    /// <summary>Solves the sudoku by brute force; starting in initial state.</summary>
    /// <returns>true on success, false on failure to solve</returns>
    public bool Solve()
    {
        L_Sudoku sud = this;

        if (!ValidState(sud.Init))
            throw Lept.Fail("sudokuSolve", "initial state not valid");

        while (true) {
            if (NewGuess(sud))
                break;
            if (sud.Finished)
                break;
        }

        if (sud.Failure) {
            Log.Info("sudokuSolve", $"Failure after {sud.NGuess} guesses");
            return false;
        }

        Log.Info("sudokuSolve", $"Solved after {sud.NGuess} guesses");
        return true;
    }

    // leptonica: sudokuValidState (sudoku.c)
    /// <param name="state">array of size 81</param>
    /// <returns>true if valid, false if invalid</returns>
    /// <remarks>
    /// (1) This can be used on either the initial state (init)
    ///     or on the current state (state) of the l_soduku.
    ///     All values of 0 are ignored.
    /// </remarks>
    private static bool ValidState(int[] state)
    {
        for (int i = 0; i < 81; i++) {
            if (!TestState(state, i))
                return false;
        }

        return true;
    }

    // leptonica: sudokuNewGuess (sudoku.c)
    /// <returns>false if OK; true if no solution is possible (C: 0 if OK; 1 if no solution)</returns>
    /// <remarks>
    /// (1) This attempts to increment the number in the current
    ///     location.  If it can't, it backtracks (sets the number
    ///     in the current location to zero and decrements the
    ///     current location).  If it can, it tests that number,
    ///     and if the number is valid, moves forward to the next
    ///     empty location (increments the current location).
    /// (2) If there is no solution, backtracking will eventually
    ///     exhaust possibilities for the first location.
    /// </remarks>
    private static bool NewGuess(L_Sudoku sud)
    {
        int[] locs = sud.Locs;
        int[] state = sud.State;
        int index = locs[sud.Current];  /* 0 to 80 */
        int val = state[index];
        if (val == 9) {  /* backtrack or give up */
            if (sud.Current == 0) {
                sud.Failure = true;
                return true;
            }
            state[index] = 0;
            sud.Current--;
        } else {  /* increment current value and test */
            sud.NGuess++;
            state[index]++;
            bool valid = TestState(state, index);
            if (valid) {
                if (sud.Current == sud.Num - 1) {  /* we're done */
                    sud.Finished = true;
                    return false;
                } else {  /* advance to next position */
                    sud.Current++;
                }
            }
        }

        return false;
    }

    // leptonica: sudokuTestState (sudoku.c)
    /// <param name="state">current state: array of 81 values</param>
    /// <param name="index">into state element that we are testing</param>
    /// <returns>true if valid; false if invalid; no error checking</returns>
    private static bool TestState(int[] state, int index)
    {
        int val;
        if ((val = state[index]) == 0)  /* automatically valid */
            return true;

            /* Test row.  Test val is at (x, y) = (index % 9, index / 9)  */
        int row = index / 9;
        int rowstart = 9 * row;
        for (int i = rowstart; i < index; i++) {
            if (state[i] == val)
                return false;
        }
        int rowend = rowstart + 9;
        for (int i = index + 1; i < rowend; i++) {
            if (state[i] == val)
                return false;
        }

            /* Test column */
        int col = index % 9;
        for (int j = col; j < index; j += 9) {
            if (state[j] == val)
                return false;
        }
        for (int j = index + 9; j < 81; j += 9) {
            if (state[j] == val)
                return false;
        }

            /* Test local 3x3 block */
        int blockrow = 3 * (row / 3);
        int blockcol = 3 * (col / 3);
        int blockstart = 9 * blockrow + blockcol;
        for (int i = 0; i < 3; i++) {
            int rowindex = blockstart + 9 * i;
            for (int j = 0; j < 3; j++) {
                int locindex = rowindex + j;
                if (index == locindex) continue;
                if (state[locindex] == val)
                    return false;
            }
        }

        return true;
    }

    /*---------------------------------------------------------------------*
     *                         Test for uniqueness                         *
     *---------------------------------------------------------------------*/
    // leptonica: sudokuTestUniqueness (sudoku.c)
    /// <param name="array">of 81 numbers, 9 lines of 9 numbers each</param>
    /// <returns>true if unique, false if not</returns>
    /// <remarks>
    /// (1) This applies the brute force method to all four 90 degree
    ///     rotations.  If there is more than one solution, it is highly
    ///     unlikely that all four results will be the same;
    ///     consequently, if they are the same, the solution is
    ///     most likely to be unique.
    /// </remarks>
    public static bool TestUniqueness(int[] array)
    {
        L_Sudoku sud = Create(array);
        sud.Solve();
        int[] array1 = RotateArray(array, 1);
        L_Sudoku sud1 = Create(array1);
        sud1.Solve();
        int[] array2 = RotateArray(array, 2);
        L_Sudoku sud2 = Create(array2);
        sud2.Solve();
        int[] array3 = RotateArray(array, 3);
        L_Sudoku sud3 = Create(array3);
        sud3.Solve();

        bool same1 = CompareState(sud, sud1, 1);
        bool same2 = CompareState(sud, sud2, 2);
        bool same3 = CompareState(sud, sud3, 3);
        return same1 && same2 && same3;
    }

    // leptonica: sudokuCompareState (sudoku.c)
    /// <param name="sud1">two l_Sudoku states (solutions)</param>
    /// <param name="sud2">two l_Sudoku states (solutions)</param>
    /// <param name="quads">rotation of sud2 input with respect to sud1, in units of 90 degrees cw</param>
    /// <returns>true if all 4 results are identical; false otherwise</returns>
    /// <remarks>
    /// (1) The input to sud2 has been rotated by %quads relative to the
    ///     input to sud1.  Therefore, we must rotate the solution to
    ///     sud1 by the same amount before comparing it to the
    ///     solution to sud2.
    /// </remarks>
    private static bool CompareState(L_Sudoku sud1, L_Sudoku sud2, int quads)
    {
        if (quads < 1 || quads > 3)
            throw Lept.Arg("sudokuCompareState", "valid quads in {1,2,3}");

        bool same = true;
        int[] array = RotateArray(sud1.State, quads);
        for (int i = 0; i < 81; i++) {
            if (array[i] != sud2.State[i]) {
                same = false;
                break;
            }
        }
        return same;
    }

    // leptonica: sudokuRotateArray (sudoku.c)
    /// <param name="array">81 numbers; 9 lines of 9 numbers each</param>
    /// <param name="quads">1-3; number of 90 degree cw rotations</param>
    /// <returns>rarray rotated array</returns>
    private static int[] RotateArray(int[] array, int quads)
    {
        int i, j, sindex, dindex;

        if (quads < 1 || quads > 3)
            throw Lept.Arg("sudokuRotateArray", "valid quads in {1,2,3}");

        int[] rarray = new int[81];
        if (quads == 1) {
            for (j = 0, dindex = 0; j < 9; j++) {
                for (i = 8; i >= 0; i--) {
                    sindex = 9 * i + j;
                    rarray[dindex++] = array[sindex];
                }
            }
        } else if (quads == 2) {
            for (i = 8, dindex = 0; i >= 0; i--) {
                for (j = 8; j >= 0; j--) {
                    sindex = 9 * i + j;
                    rarray[dindex++] = array[sindex];
                }
            }
        } else {  /* quads == 3 */
            for (j = 8, dindex = 0; j >= 0; j--) {
                for (i = 0; i < 9; i++) {
                    sindex = 9 * i + j;
                    rarray[dindex++] = array[sindex];
                }
            }
        }

        return rarray;
    }

    /*---------------------------------------------------------------------*
     *                              Generation                             *
     *---------------------------------------------------------------------*/
    // leptonica: sudokuGenerate (sudoku.c)
    /// <param name="array">81 numbers, 9 rows of 9 numbers each; it is modified</param>
    /// <param name="seed">random number</param>
    /// <param name="minelems">min non-zero elements allowed; &lt;= 80</param>
    /// <param name="maxtries">max tries to remove a number and get a valid sudoku</param>
    /// <returns>l_sudoku</returns>
    /// <remarks>
    /// (1) This is a brute force generator.  It starts with a completed
    ///     sudoku solution and, by removing elements (setting them to 0),
    ///     generates a valid (unique) sudoku initial condition.
    /// (2) The process stops when either %minelems, the minimum
    ///     number of non-zero elements, is reached, or when the
    ///     number of attempts to remove the next element exceeds %maxtries.
    /// (3) No sudoku is known with less than 17 nonzero elements.
    /// </remarks>
    public static L_Sudoku Generate(int[] array, int seed, int minelems, int maxtries)
    {
        const string func = "sudokuGenerate";
        int index, oldval;
        L_Sudoku testsud;

        if (minelems > 80)
            throw Lept.Arg(func, "minelems must be < 81");

            /* Remove up to 30 numbers at random from the solution.
             * Test if the solution is valid -- the initial 'solution' may
             * have been invalid.  Then test if the sudoku with 30 zeroes
             * is unique -- it almost always will be. */
        CRand.Seed((uint)seed);
        int nzeros = 0;
        int sector = 0;
        int removefirst = Math.Min(30, 81 - minelems);
        while (nzeros < removefirst) {
            int val = Utils.GenRandomIntOnInterval(0, 8, 0);
            index = 27 * (sector / 3) + 3 * (sector % 3) +
                    9 * (val / 3) + (val % 3);
            if (array[index] == 0) continue;
            array[index] = 0;
            nzeros++;
            sector++;
            sector %= 9;
        }
        testsud = Create(array);
        testsud.Solve();
        if (testsud.Failure) {
            Log.Error(func, "invalid initial solution");
            throw Lept.Fail(func, "invalid initial solution");
        }
        bool unique = TestUniqueness(testsud.Init);
        if (!unique) {
            Log.Error(func, "non-unique result with 30 zeroes");
            throw Lept.Fail(func, "non-unique result with 30 zeroes");
        }

            /* Remove more numbers, testing at each removal for uniqueness. */
        int tries = 0;
        sector = 0;
        while (true) {
            if (tries > maxtries) break;
            if (81 - nzeros <= minelems) break;

            if (tries == 0) {
                Log.Info(func, $"Trying {nzeros} zeros");
                tries = 1;
            }

                /* Choose an element to be zeroed.  We choose one
                 * at random in succession from each of the nine sectors. */
            int val = Utils.GenRandomIntOnInterval(0, 8, 0);
            index = 27 * (sector / 3) + 3 * (sector % 3) +
                    9 * (val / 3) + (val % 3);
            sector++;
            sector %= 9;
            if (array[index] == 0) continue;

                /* Save the old value in case we need to revert */
            oldval = array[index];

                /* Is there a solution?  If not, try again. */
            array[index] = 0;
            testsud = Create(array);
            testsud.Solve();
            if (testsud.Failure) {
                array[index] = oldval;  /* revert */
                tries++;
                continue;
            }

                /* Is the solution unique?  If not, try again. */
            unique = TestUniqueness(testsud.Init);
            if (!unique) {  /* revert and try again */
                array[index] = oldval;
                tries++;
            } else {  /* accept this */
                tries = 0;
                Log.Info(func, $"Have {nzeros} zeros");
                nzeros++;
            }
        }
        Log.Info(func, $"Final: nelems = {81 - nzeros}");

            /* Show that we can recover the solution */
        L_Sudoku sud = Create(array);
        sud.Output(L.L_SUDOKU_INIT);
        sud.Solve();
        sud.Output(L.L_SUDOKU_STATE);

        return sud;
    }

    /*---------------------------------------------------------------------*
     *                               Output                                *
     *---------------------------------------------------------------------*/
    // leptonica: sudokuOutput (sudoku.c)
    /// <summary>Prints either the initial array or the current state of the solution to stderr.</summary>
    /// <param name="arraytype">L_SUDOKU_INIT, L_SUDOKU_STATE</param>
    public void Output(int arraytype)
    {
        int[] array;
        if (arraytype == L.L_SUDOKU_INIT)
            array = Init;
        else if (arraytype == L.L_SUDOKU_STATE)
            array = State;
        else
            throw Lept.Arg("sudokuOutput", "invalid arraytype");

        for (int i = 0; i < 9; i++) {
            for (int j = 0; j < 9; j++)
                CStdio.Stderr($"{array[9 * i + j]} ");
            CStdio.Stderr($"\n");
        }
    }
}
