using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Leptonica;

/*
 *  utils1.c: error, warning and info messages, redirection of stderr,
 *  endian conversions, file corruption operations, random and prime number
 *  operations, 64-bit hash functions, version number, timing and date.
 *
 *  Not ported as such:
 *    - LeptMsgSeverity is Log.MinSeverity; setMsgSeverity() sets it.
 *    - returnError*() print through Log.Error(); the macros ERROR_INT, ERROR_FLOAT
 *      and ERROR_PTR are replaced by exceptions, so these
 *      functions are only provided for callers that want the C behavior.
 *    - startTimer/stopTimer measure wall-clock time with Stopwatch, not the
 *      user CPU time of the process.
 *    - l_getFormattedDate and getLeptonicaVersion return a managed string.
 */

/// <summary>Constants from environ.h and allheaders.h that utils1.c needs.</summary>
public static partial class L
{
    /// <summary>Severity level that defers to an externally set level.</summary>
    public const int L_SEVERITY_EXTERNAL = 0;
    /// <summary>Severity level that reports all messages.</summary>
    public const int L_SEVERITY_ALL = 1;
    /// <summary>Severity level that reports debug messages and above.</summary>
    public const int L_SEVERITY_DEBUG = 2;
    /// <summary>Severity level that reports informational messages and above.</summary>
    public const int L_SEVERITY_INFO = 3;
    /// <summary>Severity level that reports warnings and errors.</summary>
    public const int L_SEVERITY_WARNING = 4;
    /// <summary>Severity level that reports errors only.</summary>
    public const int L_SEVERITY_ERROR = 5;
    /// <summary>Severity level that suppresses all messages.</summary>
    public const int L_SEVERITY_NONE = 6;

    /// <summary>Major version number of the ported Leptonica library.</summary>
    public const int LIBLEPT_MAJOR_VERSION = 1;
    /// <summary>Minor version number of the ported Leptonica library.</summary>
    public const int LIBLEPT_MINOR_VERSION = 87;
    /// <summary>Patch version number of the ported Leptonica library.</summary>
    public const int LIBLEPT_PATCH_VERSION = 0;
}

/// <summary>Timer for nested timing (L_TIMER).</summary>
public sealed class LTimer
{
    internal long Start;
}

/// <summary>Wall clock timer (L_WALLTIMER).</summary>
public sealed class WallTimer
{
    /// <summary>Seconds part of the start time.</summary>
    public int StartSec;
    /// <summary>Microseconds part of the start time.</summary>
    public int StartUsec;
    /// <summary>Seconds part of the stop time.</summary>
    public int StopSec;
    /// <summary>Microseconds part of the stop time.</summary>
    public int StopUsec;
}

public static partial class Utils
{
    private const int MAX_DEBUG_MESSAGE = 2000;

    /*----------------------------------------------------------------------*
     *                Control of error, warning and info messages           *
     *----------------------------------------------------------------------*/
    // leptonica: setMsgSeverity (utils1.c)
    /// <summary>Sets the message severity threshold; returns the previous one.</summary>
    /// <param name="newsev">one of L.L_SEVERITY_*</param>
    /// <remarks>
    /// (1) setMsgSeverity() allows the user to specify the desired
    ///     message severity threshold.  Messages of equal or greater
    ///     severity will be output.  The previous message severity is
    ///     returned when the new severity is set.
    /// (2) If L_SEVERITY_EXTERNAL is passed, then the severity will be
    ///     obtained from the LEPT_MSG_SEVERITY environment variable.
    /// </remarks>
    public static int SetMsgSeverity(int newsev)
    {
        int oldsev = (int)Log.MinSeverity;
        if (newsev == L.L_SEVERITY_EXTERNAL) {
            string? envsev = Environment.GetEnvironmentVariable("LEPT_MSG_SEVERITY");
            if (envsev != null)
                Log.MinSeverity = (LogSeverity)Atoi(envsev);
        } else {
            Log.MinSeverity = (LogSeverity)newsev;
        }
        return oldsev;
    }

    // C atoi(): optional white space, optional sign, then decimal digits
    private static int Atoi(string s)
    {
        int i = 0;
        while (i < s.Length && (s[i] == ' ' || (s[i] >= '\t' && s[i] <= '\r')))
            i++;
        bool negative = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) {
            negative = s[i] == '-';
            i++;
        }
        int val = 0;
        for (; i < s.Length && s[i] >= '0' && s[i] <= '9'; i++)
            val = val * 10 + (s[i] - '0');
        return negative ? -val : val;
    }

    /*----------------------------------------------------------------------*
     *                Error return functions, invoked by macros             *
     *----------------------------------------------------------------------*
     *  These print "Error in {procname}: {msg}" through Log.Error() and      *
     *  return the given value.                                              *
     *----------------------------------------------------------------------*/
    // leptonica: returnErrorInt (utils1.c)
    /// <summary>Logs an error message and returns the given integer value.</summary>
    /// <param name="msg">The error message.</param>
    /// <param name="procname">Name of the calling function.</param>
    /// <param name="ival">The value to return; typically 1 for an error.</param>
    /// <returns>The value <paramref name="ival"/>.</returns>
    public static int ReturnErrorInt(string msg, string procname, int ival)
    {
        Log.Error(procname, msg);
        return ival;
    }

    // leptonica: returnErrorFloat (utils1.c)
    /// <summary>Logs an error message and returns the given floating-point value.</summary>
    /// <param name="msg">The error message.</param>
    /// <param name="procname">Name of the calling function.</param>
    /// <param name="fval">The value to return.</param>
    /// <returns>The value <paramref name="fval"/>.</returns>
    public static float ReturnErrorFloat(string msg, string procname, float fval)
    {
        Log.Error(procname, msg);
        return fval;
    }

    // leptonica: returnErrorPtr (utils1.c)
    /// <summary>Logs an error message and returns the given object reference.</summary>
    /// <typeparam name="T">The type of the returned reference.</typeparam>
    /// <param name="msg">The error message.</param>
    /// <param name="procname">Name of the calling function.</param>
    /// <param name="pval">The reference to return; typically <c>null</c>.</param>
    /// <returns>The value <paramref name="pval"/>.</returns>
    public static T? ReturnErrorPtr<T>(string msg, string procname, T? pval) where T : class
    {
        Log.Error(procname, msg);
        return pval;
    }

    // leptonica: returnErrorInt1 (utils1.c)
    /// <summary>Logs an error message with an additional argument and returns the given integer value.</summary>
    /// <param name="msg">The error message.</param>
    /// <param name="arg">Additional text appended to the error message.</param>
    /// <param name="procname">Name of the calling function.</param>
    /// <param name="ival">The value to return; typically 1 for an error.</param>
    /// <returns>The value <paramref name="ival"/>.</returns>
    public static int ReturnErrorInt1(string msg, string arg, string procname, int ival)
    {
        Log.Error(procname, $"{msg}: {arg}");
        return ival;
    }

    // leptonica: returnErrorFloat1 (utils1.c)
    /// <summary>Logs an error message with an additional argument and returns the given floating-point value.</summary>
    /// <param name="msg">The error message.</param>
    /// <param name="arg">Additional text appended to the error message.</param>
    /// <param name="procname">Name of the calling function.</param>
    /// <param name="fval">The value to return.</param>
    /// <returns>The value <paramref name="fval"/>.</returns>
    public static float ReturnErrorFloat1(string msg, string arg, string procname, float fval)
    {
        Log.Error(procname, $"{msg}: {arg}");
        return fval;
    }

    // leptonica: returnErrorPtr1 (utils1.c)
    /// <summary>Logs an error message with an additional argument and returns the given object reference.</summary>
    /// <typeparam name="T">The type of the returned reference.</typeparam>
    /// <param name="msg">The error message.</param>
    /// <param name="arg">Additional text appended to the error message.</param>
    /// <param name="procname">Name of the calling function.</param>
    /// <param name="pval">The reference to return; typically <c>null</c>.</param>
    /// <returns>The value <paramref name="pval"/>.</returns>
    public static T? ReturnErrorPtr1<T>(string msg, string arg, string procname, T? pval) where T : class
    {
        Log.Error(procname, $"{msg}: {arg}");
        return pval;
    }

    /*------------------------------------------------------------------------*
     *                   Runtime redirection of stderr                        *
     *------------------------------------------------------------------------*/
        /* By default, all messages go to stderr */
    private static void LeptDefaultStderrHandler(string formattedMsg)
    {
        Console.Error.Write(formattedMsg);
    }

        /* The stderr callback handler is private to leptonica.
         * By default it writes to stderr.  */
    private static Action<string> stderrHandler = LeptDefaultStderrHandler;

    /* leptSetStderrHandler (utils1.c) */
    /// <summary>Registers a handler for redirection of output to stderr at runtime.</summary>
    /// <remarks>
    /// (1) This registers a handler for redirection of output to stderr
    ///     at runtime.
    /// (2) If called with null, the output goes to stderr.
    /// </remarks>
    public static void LeptSetStderrHandler(Action<string>? handler)
    {
        if (handler != null)
            stderrHandler = handler;
        else
            stderrHandler = LeptDefaultStderrHandler;
    }

    /* lept_stderr (utils1.c) */
    /// <summary>Replacement for fprintf(stderr, ...), allowing redirection of output.</summary>
    /// <param name="fmt">printf-style format string (see <see cref="CFormat"/>).</param>
    /// <param name="args">Arguments referenced by the format string.</param>
    /// <remarks>
    /// <para>This is a replacement for writing directly to the standard error stream, to allow redirection of output.</para>
    /// <para>The message size is limited to 2K bytes.</para>
    /// </remarks>
    public static void LeptStderr(string fmt, params object?[] args)
    {
        string msg = CFormat.Sprintf(fmt, args);
        if (msg.Length > MAX_DEBUG_MESSAGE - 1)
            msg = msg.Substring(0, MAX_DEBUG_MESSAGE - 1);
        stderrHandler(msg);
    }

    /*--------------------------------------------------------------------*
     *                    Test files for equivalence                      *
     *--------------------------------------------------------------------*/
    // leptonica: filesAreIdentical (utils1.c)
    /// <summary>True if the two files have identical contents.</summary>
    public static bool FilesAreIdentical(string fname1, string fname2)
    {
        long nbytes1 = NbytesInFile(fname1);
        long nbytes2 = NbytesInFile(fname2);
        if (nbytes1 != nbytes2)
            return false;

        byte[] array1 = BinaryRead(fname1);
        byte[] array2 = BinaryRead(fname2);
        bool same = true;
        for (int i = 0; i < nbytes1; i++) {
            if (array1[i] != array2[i]) {
                same = false;
                break;
            }
        }
        return same;
    }

    /*--------------------------------------------------------------------------*
     *   16 and 32 bit byte-swapping on big endian and little  endian machines  *
     *--------------------------------------------------------------------------*
     *   The port assumes a little-endian machine.             *
     *--------------------------------------------------------------------------*/

    /*--------------------------------------------------------------------*
     *                        16-bit byte swapping                        *
     *--------------------------------------------------------------------*/
    // leptonica: convertOnLittleEnd16 (utils1.c)
    /// <summary>Swaps the two bytes of a 16-bit value, as needed to read big-endian data on a little-endian machine.</summary>
    /// <param name="shortin">The 16-bit value to convert.</param>
    /// <returns>The value with its bytes swapped.</returns>
    public static ushort ConvertOnLittleEnd16(ushort shortin)
    {
        return (ushort)((shortin << 8) | (shortin >> 8));
    }

    // leptonica: convertOnBigEnd16 (utils1.c)
    /// <summary>Returns a 16-bit value unchanged; no byte swap is needed to read big-endian data on a big-endian machine.</summary>
    /// <param name="shortin">The 16-bit value to convert.</param>
    /// <returns>The unchanged value.</returns>
    public static ushort ConvertOnBigEnd16(ushort shortin)
    {
        return shortin;
    }

    /*--------------------------------------------------------------------*
     *                        32-bit byte swapping                        *
     *--------------------------------------------------------------------*/
    // leptonica: convertOnLittleEnd32 (utils1.c)
    /// <summary>Reverses the four bytes of a 32-bit value, as needed to read big-endian data on a little-endian machine.</summary>
    /// <param name="wordin">The 32-bit value to convert.</param>
    /// <returns>The value with its bytes reversed.</returns>
    public static uint ConvertOnLittleEnd32(uint wordin)
    {
        return ((wordin << 24) | ((wordin << 8) & 0x00ff0000u) |
                ((wordin >> 8) & 0x0000ff00u) | (wordin >> 24));
    }

    // leptonica: convertOnBigEnd32 (utils1.c)
    /// <summary>Returns a 32-bit value unchanged; no byte swap is needed to read big-endian data on a big-endian machine.</summary>
    /// <param name="wordin">The 32-bit value to convert.</param>
    /// <returns>The unchanged value.</returns>
    public static uint ConvertOnBigEnd32(uint wordin)
    {
        return wordin;
    }

    /*---------------------------------------------------------------------*
     *           File corruption and byte replacement operations           *
     *---------------------------------------------------------------------*/
    // leptonica: fileCorruptByDeletion (utils1.c)
    /// <param name="filein">input file</param>
    /// <param name="loc">fractional location of start of deletion</param>
    /// <param name="size">fractional size of deletion</param>
    /// <param name="fileout">corrupted file</param>
    /// <remarks>
    /// (1) %loc and %size are expressed as a fraction of the file size.
    /// (2) This makes a copy of the data in %filein, where bytes in the
    ///     specified region have deleted.
    /// (3) If (%loc + %size) >= 1.0, this deletes from the position
    ///     represented by %loc to the end of the file.
    /// (4) It is useful for testing robustness of I/O wrappers when the
    ///     data is corrupted, by simulating data corruption by deletion.
    /// </remarks>
    public static void FileCorruptByDeletion(string filein, float loc, float size, string fileout)
    {
        if (loc < 0.0 || loc >= 1.0)
            throw Lept.Arg("fileCorruptByDeletion", "loc must be in [0.0 ... 1.0)");
        if (size <= 0.0)
            throw Lept.Arg("fileCorruptByDeletion", "size must be > 0.0");
        if (loc + size > 1.0)
            size = 1.0f - loc;

        byte[] datain = BinaryRead(filein);
        ulong inbytes = (ulong)datain.Length;
        int locb = (int)(loc * (float)inbytes + 0.5);
        locb = (int)Math.Min((ulong)locb, inbytes - 1);
        int sizeb = (int)(size * (float)inbytes + 0.5);
        sizeb = Math.Max(1, sizeb);
        sizeb = (int)Math.Min((ulong)sizeb, inbytes - (ulong)locb);  /* >= 1 */
        Log.Info("fileCorruptByDeletion", $"Removed {sizeb} bytes at location {locb}");
        int rembytes = (int)(inbytes - (ulong)locb - (ulong)sizeb);  /* >= 0; to be copied, after excision */

        ulong outbytes = inbytes - (ulong)sizeb;
        byte[] dataout = new byte[outbytes];
        int i;
        for (i = 0; i < locb; i++)
            dataout[i] = datain[i];
        for (i = 0; i < rembytes; i++)
            dataout[locb + i] = datain[locb + sizeb + i];
        WriteIgnoringError(fileout, dataout);
    }

    // l_binaryWrite() errors are not checked by the callers in this file
    private static void WriteIgnoringError(string fileout, byte[] data)
    {
        if (data.Length > 0)
            BinaryWrite(fileout, "w", data);
        else
            Log.Error("l_binaryWrite", "nbytes must be > 0");
    }

    // leptonica: fileCorruptByMutation (utils1.c)
    /// <param name="filein">input file</param>
    /// <param name="loc">fractional location of start of randomization</param>
    /// <param name="size">fractional size of randomization</param>
    /// <param name="fileout">corrupted file</param>
    /// <remarks>
    /// (1) %loc and %size are expressed as a fraction of the file size.
    /// (2) This makes a copy of the data in %filein, where bytes in the
    ///     specified region have been replaced by random data.
    /// (3) If (%loc + %size) >= 1.0, this modifies data from the position
    ///     represented by %loc to the end of the file.
    /// (4) It is useful for testing robustness of I/O wrappers when the
    ///     data is corrupted, by simulating data corruption.
    /// </remarks>
    public static void FileCorruptByMutation(string filein, float loc, float size, string fileout)
    {
        if (loc < 0.0 || loc >= 1.0)
            throw Lept.Arg("fileCorruptByMutation", "loc must be in [0.0 ... 1.0)");
        if (size <= 0.0)
            throw Lept.Arg("fileCorruptByMutation", "size must be > 0.0");
        if (loc + size > 1.0)
            size = 1.0f - loc;

        byte[] data = BinaryRead(filein);
        ulong bytes = (ulong)data.Length;
        int locb = (int)(loc * (float)bytes + 0.5);
        locb = (int)Math.Min((ulong)locb, bytes - 1);
        int sizeb = (int)(size * (float)bytes + 0.5);
        sizeb = Math.Max(1, sizeb);
        sizeb = (int)Math.Min((ulong)sizeb, bytes - (ulong)locb);  /* >= 1 */
        Log.Info("fileCorruptByMutation", $"Randomizing {sizeb} bytes at location {locb}");

            /* Make an array of random bytes and do the substitution */
        for (int i = 0; i < sizeb; i++) {
            data[locb + i] =
                (byte)(255.9 * ((double)CRand.Next() / (double)CRand.RandMax));
        }

        WriteIgnoringError(fileout, data);
    }

    // leptonica: fileReplaceBytes (utils1.c)
    /// <param name="filein">input file</param>
    /// <param name="start">start location for replacement</param>
    /// <param name="nbytes">number of bytes to be removed</param>
    /// <param name="newdata">replacement bytes; empty for none</param>
    /// <param name="fileout">output file</param>
    /// <remarks>
    /// (1) To remove %nbytes without replacement, set %newdata empty
    ///     (NULL in C; the C %newsize is newdata.Length).
    /// (2) One use is for replacing the date/time in a pdf file by a
    ///     string of 12 '0's, effectively removing the date without
    ///     invalidating the byte counters in the pdf file:
    ///         FileReplaceBytes(filein, 86, 12, "000000000000"u8, fileout)
    /// </remarks>
    public static void FileReplaceBytes(string filein, int start, int nbytes,
                                        ReadOnlySpan<byte> newdata, string fileout)
    {
        byte[] datain = BinaryRead(filein);
        ulong inbytes = (ulong)datain.Length;
        if ((ulong)(long)(start + nbytes) > inbytes)
            Log.Warning("fileReplaceBytes", $"start + nbytes > length(filein) = {inbytes}");

        ulong newsize = (ulong)newdata.Length;
        ulong outbytes = inbytes - (ulong)(long)nbytes + newsize;
        byte[] dataout = new byte[outbytes];

        long i;
        for (i = 0; i < start; i++)
            dataout[i] = datain[i];
        for (i = start; i < (long)((ulong)(long)start + newsize); i++)
            dataout[i] = newdata[(int)(i - start)];
        long index = start + nbytes;  /* for datain */
        long dstart = (long)((ulong)(long)start + newsize);  /* for dataout */
        for (i = dstart; i < (long)outbytes; i++, index++)
            dataout[i] = datain[index];
        WriteIgnoringError(fileout, dataout);
    }

    /*---------------------------------------------------------------------*
     *              Generate random integer in given interval              *
     *---------------------------------------------------------------------*/
    // leptonica: genRandomIntOnInterval (utils1.c)
    /// <summary>Returns a random integer in the interval [start ... end].</summary>
    /// <param name="start">beginning of interval; can be &lt; 0</param>
    /// <param name="end">end of interval; must be >= start</param>
    /// <param name="seed">use 0 to skip; otherwise call srand</param>
    public static int GenRandomIntOnInterval(int start, int end, int seed)
    {
        if (end < start)
            throw Lept.Arg("genRandomIntOnInterval", "invalid range");

        if (seed > 0) CRand.Seed((uint)seed);
        double range = (double)(end - start + 1);
        return start + (int)((double)range *
                             ((double)CRand.Next() / (double)CRand.RandMax));
    }

    /*---------------------------------------------------------------------*
     *                        Simple math functions                        *
     *---------------------------------------------------------------------*/
    // leptonica: lept_roundftoi (utils1.c)
    /// <summary>Value rounded to the nearest integer.</summary>
    /// <remarks>
    /// (1) For fval >= 0, fval --> round(fval) == floor(fval + 0.5)
    ///     For fval &lt; 0, fval --> -round(-fval))
    ///     This is symmetric around 0.
    ///     e.g., for fval in (-0.5 ... 0.5), fval --> 0
    /// </remarks>
    public static int RoundFToI(float fval)
    {
        return (fval >= 0.0) ? (int)(fval + 0.5) : (int)(fval - 0.5);
    }

    // leptonica: lept_floor (utils1.c)
    /// <summary>Largest integer that is not greater than fval.</summary>
    public static int LeptFloor(float fval)
    {
        return (fval >= 0.0) ? (int)(fval) : -(int)(-fval);
    }

    // leptonica: lept_ceiling (utils1.c)
    /// <summary>Smallest integer that is not less than fval.</summary>
    /// <remarks>
    /// (1) If fval is equal to its interger value, return that.
    ///     Otherwise:
    ///       For fval > 0, fval --> 1 + floor(fval)
    ///       For fval &lt; 0, fval --> -(1 + floor(-fval))
    /// </remarks>
    public static int LeptCeiling(float fval)
    {
        return (fval == (int)fval ? (int)fval :
                fval > 0.0 ? 1 + (int)(fval) : -(1 + (int)(-fval)));
    }

    /*---------------------------------------------------------------------*
     *                        64-bit hash functions                        *
     *---------------------------------------------------------------------*/
    // C converts a double to l_uint64 with cvttsd2si (values below 2^63 go through a signed
    // conversion, so negative values wrap); .NET saturates, so that is reproduced here
    private static ulong DoubleToUint64(double val)
    {
        const double two63 = 9223372036854775808.0;
        if (val < two63)
            return (ulong)(long)val;
        return (ulong)(long)(val - two63) + 0x8000000000000000UL;
    }

    // leptonica: l_hashStringToUint64 (utils1.c)
    /// <summary>Hashes a string into 64 bits; false (hash 0) for a null or empty string.</summary>
    /// <remarks>
    /// (1) The intent of the hash is to avoid collisions by mapping
    ///     the string as randomly as possible into 64 bits.
    /// (2) To the extent that the hashes are random, the probability of
    ///     a collision can be approximated by the square of the number
    ///     of strings divided by 2^64.  For 1 million strings, the
    ///     collision probability is about 1 in 16 million.
    /// (3) I expect non-randomness of the distribution to be most evident
    ///     for small text strings.  This hash function has been tested
    ///     for all 5-character text strings composed of 26 letters,
    ///     of which there are 26^5 = 12356630.  There are no hash
    ///     collisions for this set.
    /// (4) The string is hashed as its UTF-8 bytes, taken as C <c>char</c>
    ///     (signed, as with the x86 compilers).
    /// </remarks>
    public static bool HashStringToUint64(string str, out ulong hash)
    {
        hash = 0;
        if (string.IsNullOrEmpty(str)) {
            Log.Error("l_hashStringToUint64", "str not defined or empty");
            return false;
        }

        ulong mulp = 26544357894361247;  /* prime, about 1/700 of the max uint64 */
        hash = 104395301;
        foreach (byte b in Encoding.UTF8.GetBytes(str)) {
            hash += ((ulong)(long)(sbyte)b * mulp) ^ (hash >> 7);   /* shift [1...23] are ok */
        }
        hash = hash ^ (hash << 37);
        return true;
    }

    // leptonica: l_hashStringToUint64Fast (utils1.c)
    /// <remarks>
    /// (1) This very simple hash algorithm is described in "The Practice
    ///     of Programming" by Kernighan and Pike, p. 57 (1999).
    /// (2) The returned hash value would then be hashed into an index into
    ///     the hashtable, using the mod operator with the hashtable size.
    /// (3) The C function returns 1 (and sets the hash to 0) for a null or empty string, and its
    ///     callers in sarray2.c ignore that; so this returns false instead of throwing.
    /// (4) The string is hashed as its UTF-8 bytes.
    /// </remarks>
    public static bool HashStringToUint64Fast(string str, out ulong hash)
    {
        hash = 0;
        if (string.IsNullOrEmpty(str)) {
            Log.Error("l_hashStringToUint64Fast", "str not defined or empty");
            return false;
        }

        ulong h = 0;
        foreach (byte p in Encoding.UTF8.GetBytes(str))
            h = 37 * h + p;  /* 37 is good prime number for this */
        hash = h;
        return true;
    }

    // leptonica: l_hashPtToUint64 (utils1.c)
    /// <remarks>
    /// (1) This simple hash function has no collisions for
    ///     any of 400 million points with x and y up to 20000.
    /// </remarks>
    public static ulong HashPtToUint64(int x, int y)
    {
        return DoubleToUint64(2173249142.3849 * x + 3763193258.6227 * y);
    }

    // leptonica: l_hashFloat64ToUint64 (utils1.c)
    /// <returns>the hash key</returns>
    /// <remarks>
    /// (1) This is a simple hash for using hashmaps with 64-bit float data.
    /// (2) The resulting hash is called a "key" in a lookup operation.
    ///     The bucket for %val in a hashmap is then found by taking the mod
    ///     of the hash key with the number of buckets (which is prime).
    /// </remarks>
    public static ulong HashFloat64ToUint64(double val)
    {
        val = (val >= 0.0) ? 847019.66701 * val : -217324.91613 * val;
        return DoubleToUint64(val);
    }

    /*---------------------------------------------------------------------*
     *                           Prime finders                             *
     *---------------------------------------------------------------------*/
    // leptonica: findNextLargerPrime (utils1.c)
    /// <returns>first prime larger than start</returns>
    public static uint FindNextLargerPrime(int start)
    {
        if (start <= 0)
            throw Lept.Arg("findNextLargerPrime", "start must be > 0");

        for (int i = start + 1; ; i++) {
            LeptIsPrime((ulong)(long)i, out bool isPrime, out _);
            if (isPrime)
                return (uint)i;
        }
    }

    // leptonica: lept_isPrime (utils1.c)
    /// <param name="n">64-bit unsigned</param>
    /// <param name="isPrime">true if prime</param>
    /// <param name="factor">smallest divisor, or 0 if prime</param>
    /// <remarks>
    /// NOTE(upstream): the loop stops before reaching the square root, so the
    /// squares of odd primes (9, 25, 49, ...) are reported as prime.
    /// </remarks>
    public static void LeptIsPrime(ulong n, out bool isPrime, out uint factor)
    {
        isPrime = false;
        factor = 0;
        if (n <= 0)
            throw Lept.Arg("lept_isPrime", "n must be > 0");

        if (n % 2 == 0) {
            factor = 2;
            return;
        }

        ulong limit = (ulong)Math.Sqrt((double)n);
        // NOTE(upstream): "div < limit" never tests div == limit, so 9, 25, 49, ... come out prime
        for (uint div = 3; div < limit; div += 2) {
            ulong ratio = n / div;
            if (ratio * div == n) {
                factor = div;
                return;
            }
        }

        isPrime = true;
    }

    /*---------------------------------------------------------------------*
     *                         Gray code conversion                        *
     *---------------------------------------------------------------------*/
    // leptonica: convertIntToGrayCode (utils1.c)
    /// <summary>Converts an integer to its Gray code.</summary>
    /// <param name="val">The integer to convert.</param>
    /// <returns>The Gray code value.</returns>
    /// <remarks>
    /// Gray code values corresponding to integers differ by only one bit transition between successive integers.
    /// </remarks>
    public static uint ConvertIntToGrayCode(uint val)
    {
        return (val >> 1) ^ val;
    }

    // leptonica: convertGrayCodeToInt (utils1.c)
    /// <summary>Converts a Gray code value back to the integer it represents.</summary>
    /// <param name="val">The Gray code value.</param>
    /// <returns>The corresponding integer.</returns>
    public static uint ConvertGrayCodeToInt(uint val)
    {
        for (uint shift = 1; shift < 32; shift <<= 1)
            val ^= val >> (int)shift;
        return val;
    }

    /*---------------------------------------------------------------------*
     *                       Leptonica version number                      *
     *---------------------------------------------------------------------*/
    // leptonica: getLeptonicaVersion (utils1.c)
    /// <summary>Version string, e.g. 'leptonica-1.87.0'.</summary>
    public static string GetLeptonicaVersion()
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"leptonica-{L.LIBLEPT_MAJOR_VERSION}.{L.LIBLEPT_MINOR_VERSION}.{L.LIBLEPT_PATCH_VERSION}");
    }

    /*---------------------------------------------------------------------*
     *                           Timing procs                              *
     *---------------------------------------------------------------------*/
    private static long timerBefore;

    // Elapsed time as the C code computes it: whole seconds plus microseconds
    private static float ElapsedSeconds(long start)
    {
        long ticks = Stopwatch.GetElapsedTime(start).Ticks;
        int tsec = (int)(ticks / TimeSpan.TicksPerSecond);
        int tusec = (int)((ticks % TimeSpan.TicksPerSecond) / 10);
        return (float)(tsec + ((float)tusec) / 1000000.0);
    }

    // leptonica: startTimer (utils1.c)
    /// <summary>Starts the global timer; use <see cref="StopTimer"/> to read the elapsed time.</summary>
    /// <remarks>
    /// These two calls measure the time elapsed between them:
    /// <code>
    /// StartTimer();
    /// // ...
    /// LeptStderr("Elapsed time = %7.3f sec\n", StopTimer());
    /// </code>
    /// </remarks>
    public static void StartTimer()
    {
        timerBefore = Stopwatch.GetTimestamp();
    }

    // leptonica: stopTimer (utils1.c)
    /// <summary>Stops the timer started by <see cref="StartTimer"/>.</summary>
    /// <returns>The elapsed time in seconds since <see cref="StartTimer"/> was called.</returns>
    public static float StopTimer()
    {
        return ElapsedSeconds(timerBefore);
    }

    // leptonica: startTimerNested (utils1.c)
    /// <summary>Starts a new timer that can be nested inside other timers.</summary>
    /// <returns>A timer to pass to <see cref="StopTimerNested"/>.</returns>
    /// <remarks>
    /// Example of usage:
    /// <code>
    /// LTimer t1 = StartTimerNested();
    /// // ...
    /// LTimer t2 = StartTimerNested();
    /// // ...
    /// LeptStderr("Elapsed time 2 = %7.3f sec\n", StopTimerNested(t2));
    /// // ...
    /// LeptStderr("Elapsed time 1 = %7.3f sec\n", StopTimerNested(t1));
    /// </code>
    /// </remarks>
    public static LTimer StartTimerNested()
    {
        return new LTimer { Start = Stopwatch.GetTimestamp() };
    }

    // leptonica: stopTimerNested (utils1.c)
    /// <summary>Stops a timer started by <see cref="StartTimerNested"/>.</summary>
    /// <param name="timer">The timer returned by <see cref="StartTimerNested"/>.</param>
    /// <returns>The elapsed time in seconds since the timer was started.</returns>
    public static float StopTimerNested(LTimer timer)
    {
        return ElapsedSeconds(timer.Start);
    }

    // leptonica: l_getCurrentTime (utils1.c)
    /// <param name="sec">seconds since birth of Unix</param>
    /// <param name="usec">microseconds since birth of Unix</param>
    public static void GetCurrentTime(out int sec, out int usec)
    {
        long usecs = (DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks) / 10;
        sec = (int)(usecs / 1000000);
        usec = (int)(usecs % 1000000);
    }

    // leptonica: startWallTimer (utils1.c)
    /// <remarks>
    /// (1) These measure the wall clock time  elapsed between the two calls:
    ///       WallTimer? timer = StartWallTimer();
    ///       ....
    ///       LeptStderr("Elapsed time = %f sec\n", StopWallTimer(ref timer));
    /// (2) Note that the timer is set to null by StopWallTimer().
    /// </remarks>
    public static WallTimer StartWallTimer()
    {
        WallTimer timer = new WallTimer();
        GetCurrentTime(out timer.StartSec, out timer.StartUsec);
        return timer;
    }

    // leptonica: stopWallTimer (utils1.c)
    /// <summary>Wall time elapsed in seconds; the timer is set to null.</summary>
    public static float StopWallTimer(ref WallTimer? timer)
    {
        if (timer == null)
            throw Lept.Arg("stopWallTimer", "timer not defined");

        GetCurrentTime(out timer.StopSec, out timer.StopUsec);
        int tsec = timer.StopSec - timer.StartSec;
        int tusec = timer.StopUsec - timer.StartUsec;
        timer = null;
        return (tsec + ((float)tusec) / 1000000.0f);
    }

    // leptonica: l_getFormattedDate (utils1.c)
    /// <remarks>
    /// (1) This is used in pdf, in the form specified in section 3.8.2 of
    ///     http://partners.adobe.com/public/developer/en/pdf/PDFReference.pdf
    /// (2) Contributed by Dave Bryan.  Works on all platforms.
    /// </remarks>
    public static string GetFormattedDate()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        char sep = 'Z';

        int gmtOffset = (int)now.Offset.TotalSeconds;
        if (gmtOffset > 0)
            sep = '+';
        else if (gmtOffset < 0)
            sep = '-';
        int relh = Math.Abs(gmtOffset) / 3600;
        int relm = (Math.Abs(gmtOffset) % 3600) / 60;

        string buf = now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        return buf + sep + relh.ToString("D2", CultureInfo.InvariantCulture) + "'"
               + relm.ToString("D2", CultureInfo.InvariantCulture) + "'";
    }
}
