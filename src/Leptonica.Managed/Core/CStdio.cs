using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Leptonica;

/*
 *  Minimal stand-ins for the C stdio calls used by the serialization code
 *  (fprintf with %d, fscanf with %d, lept_stderr). They reproduce the
 *  observable behavior of the C calls for the formats that the library uses.
 */
internal static class CStdio
{
    /// <summary>fprintf(fp, ...): writes the text with invariant culture, as ASCII/UTF-8 bytes.</summary>
    public static void Fprintf(Stream fp, FormattableString text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text.ToString(CultureInfo.InvariantCulture));
        fp.Write(bytes, 0, bytes.Length);
    }

    /// <summary>lept_stderr(...): writes the text to the standard error stream.</summary>
    public static void Stderr(FormattableString text)
    {
        Console.Error.Write(text.ToString(CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Reads from a <see cref="Stream"/> with the semantics of C fscanf, for
/// formats made of whitespace, literal characters and %d.
/// </summary>
/// <remarks>
/// fscanf reads one character beyond what it consumes and pushes it back
/// (ungetc). That character is kept here between calls, so that several
/// scans on the same stream see the same input as with a FILE*. Call
/// <see cref="Release"/> when done: if the stream is seekable, the pushed-back
/// character is given back to it; otherwise it is lost.
/// </remarks>
internal sealed class CScanner
{
    private const int None = -2;
    private const int Eof = -1;

    private readonly Stream stream;
    private int pushed = None;

    public CScanner(Stream stream)
    {
        this.stream = stream;
    }

    private int Read()
    {
        if (pushed != None) {
            int c = pushed;
            pushed = None;
            return c;
        }
        return stream.ReadByte();
    }

    private void Unget(int c)
    {
        if (c != Eof)
            pushed = c;
    }

    // Same set as C isspace() in the "C" locale.
    private static bool IsSpace(int c) => c == ' ' || (c >= '\t' && c <= '\r');

    /// <summary>Gives the pushed-back character back to the stream, if that is possible.</summary>
    public void Release()
    {
        if (pushed != None && stream.CanSeek)
            stream.Seek(-1, SeekOrigin.Current);
        pushed = None;
    }

    /// <summary>
    /// Like fscanf(fp, format, ...) for %d conversions only.
    /// </summary>
    /// <param name="format">whitespace matches any amount of whitespace, %d reads an int,
    /// any other character must match exactly</param>
    /// <param name="values">receives the converted values, in order</param>
    /// <returns>number of values assigned; -1 (EOF) if the input ended before the first
    /// conversion</returns>
    public int Scan(string format, Span<int> values)
    {
        int assigned = 0;
        for (int i = 0; i < format.Length; i++) {
            char f = format[i];
            if (f == '%') {
                i++;
                if (i >= format.Length || format[i] != 'd')
                    throw new NotSupportedException("CScanner: only %d is supported");
                int c;
                do {
                    c = Read();
                } while (IsSpace(c));
                if (c == Eof)
                    return assigned == 0 ? Eof : assigned;
                bool negative = false;
                if (c == '+' || c == '-') {
                    negative = c == '-';
                    c = Read();
                }
                if (c < '0' || c > '9') {
                    Unget(c);
                    return assigned;
                }
                long val = 0;
                while (c >= '0' && c <= '9') {
                    val = val * 10 + (c - '0');
                    c = Read();
                }
                Unget(c);
                values[assigned++] = (int)(negative ? -val : val);
            } else if (IsSpace(f)) {
                int c;
                do {
                    c = Read();
                } while (IsSpace(c));
                Unget(c);
            } else {
                int c = Read();
                if (c != f) {
                    Unget(c);
                    return assigned == 0 && c == Eof ? Eof : assigned;
                }
            }
        }
        return assigned;
    }
}
