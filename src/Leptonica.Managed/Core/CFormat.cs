using System;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Leptonica;

/// <summary>
/// printf / sscanf with the behavior of the MSVC UCRT (VS2019 and later), used by the
/// serialization code (fprintf / fscanf of numa, pta, dna, boxa, ...) to stay byte-exact.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sprintf.</b> Conversions: <c>d i u x X o c s f F e E g G</c> and <c>%%</c>. Flags:
/// <c>- + space # 0</c>; width and precision, either may be <c>*</c> (taken from the
/// arguments); length modifiers <c>hh h l ll z j t L I32 I64</c>.
/// Integer arguments (any CLR integer type, <see cref="char"/>, <see cref="bool"/>) are
/// truncated to the width the C type has on Windows x64: <c>hh</c> 8 bits, <c>h</c> 16,
/// none / <c>l</c> / <c>I32</c> 32 (LLP64: <c>long</c> is 32 bits), <c>ll z j t I64</c> 64.
/// <c>L</c> is accepted and ignored (<c>long double</c> is <c>double</c> in MSVC).
/// Floating arguments are <see cref="double"/> or <see cref="float"/>. <c>%s</c> takes a
/// <see cref="string"/> (null prints <c>(null)</c>). <c>%n</c> and <c>%p</c> are not supported.
/// Floating output is exact: the shortest-round-trip machinery is not used, the decimal
/// expansion of the binary value is computed exactly and rounded half-to-even on the exact
/// value, which is what the UCRT does since Windows 10 2004.
/// Special values: <c>inf</c>, <c>-inf</c>, <c>nan</c>, <c>-nan(ind)</c> (the default quiet NaN),
/// <c>nan(snan)</c>; upper-case conversions print them in upper case.
/// </para>
/// <para>
/// <b>Sscanf.</b> Conversions: <c>d i u x X o c s [set] f F e E g G n %%</c>, with width, the
/// <c>*</c> assignment suppression and the length modifiers above. Results are returned in
/// <c>values</c>, one slot per non-suppressed conversion in format order (so the slot index
/// is the index of the C pointer argument); a slot that was not assigned stays null.
/// Slot types: <c>d i</c> sbyte / short / int / long for hh / h / none or l / ll z j t I64;
/// <c>u x X o</c> the unsigned counterparts; <c>f e g</c> float, or double with <c>l</c> or
/// <c>L</c>; <c>c s [</c> string; <c>n</c> int. The return value is the number of assigned
/// items (<c>%n</c> is not counted), or -1 if the input ended before the first conversion
/// completed. Integer overflow wraps (the C behavior is undefined).
/// </para>
/// Culture is never consulted.
/// </remarks>
internal static class CFormat
{
    [Flags]
    private enum Flags
    {
        None = 0,
        Left = 1,
        Plus = 2,
        Space = 4,
        Alt = 8,
        Zero = 16,
    }

    [ThreadStatic] private static StringBuilder? t_out;
    [ThreadStatic] private static StringBuilder? t_body;

    /*-------------------------------------------------------------------------*
     *                                printf                                   *
     *-------------------------------------------------------------------------*/

    /// <summary>Formats like <c>sprintf</c>; see the class remarks for the supported subset.</summary>
    public static string Sprintf(string format, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(args);
        StringBuilder sb = t_out ??= new StringBuilder(256);
        sb.Clear();
        Format(sb, format, args);
        string result = sb.ToString();
        if (sb.Capacity > 1 << 16)
            t_out = null;
        return result;
    }

    private static void Format(StringBuilder sb, string format, object?[] args)
    {
        int argi = 0;
        int i = 0;
        int n = format.Length;
        while (i < n)
        {
            if (format[i] != '%')
            {
                int j = format.IndexOf('%', i);
                if (j < 0) j = n;
                sb.Append(format, i, j - i);
                i = j;
                continue;
            }
            i++;

            /* flags */
            Flags fl = Flags.None;
            bool more = true;
            while (more && i < n)
            {
                switch (format[i])
                {
                    case '-': fl |= Flags.Left; i++; break;
                    case '+': fl |= Flags.Plus; i++; break;
                    case ' ': fl |= Flags.Space; i++; break;
                    case '#': fl |= Flags.Alt; i++; break;
                    case '0': fl |= Flags.Zero; i++; break;
                    default: more = false; break;
                }
            }

            /* width */
            int width = 0;
            if (i < n && format[i] == '*')
            {
                i++;
                width = (int)ToInt64(NextArg(args, ref argi));
                if (width < 0)
                {
                    fl |= Flags.Left;
                    width = -width;
                }
            }
            else
            {
                while (i < n && format[i] >= '0' && format[i] <= '9')
                {
                    width = ClampedAdd(width, format[i] - '0');
                    i++;
                }
            }

            /* precision */
            int prec = -1;
            if (i < n && format[i] == '.')
            {
                i++;
                if (i < n && format[i] == '*')
                {
                    i++;
                    prec = (int)ToInt64(NextArg(args, ref argi));
                    if (prec < 0) prec = -1;
                }
                else
                {
                    prec = 0;
                    while (i < n && format[i] >= '0' && format[i] <= '9')
                    {
                        prec = ClampedAdd(prec, format[i] - '0');
                        i++;
                    }
                }
            }

            /* length modifier */
            int bits = 32;
            bool wide = false;   // 'l' (selects wide character for %c)
            if (i < n)
            {
                switch (format[i])
                {
                    case 'h':
                        i++;
                        if (i < n && format[i] == 'h') { i++; bits = 8; }
                        else bits = 16;
                        break;
                    case 'l':
                        i++;
                        wide = true;
                        if (i < n && format[i] == 'l') { i++; bits = 64; }
                        break;
                    case 'L': i++; break;
                    case 'z':
                    case 'j':
                    case 't':
                        i++;
                        bits = 64;
                        break;
                    case 'I':
                        i++;
                        if (i + 1 < n && format[i] == '6' && format[i + 1] == '4') { i += 2; bits = 64; }
                        else if (i + 1 < n && format[i] == '3' && format[i + 1] == '2') { i += 2; bits = 32; }
                        else bits = 64;
                        break;
                }
            }

            if (i >= n)
                throw new FormatException("CFormat: incomplete conversion specification in \"" + format + "\"");
            char conv = format[i++];

            switch (conv)
            {
                case '%':
                    sb.Append('%');
                    break;
                case 'd':
                case 'i':
                case 'u':
                case 'x':
                case 'X':
                case 'o':
                    FormatInteger(sb, conv, fl, width, prec, bits, NextArg(args, ref argi));
                    break;
                case 'c':
                    {
                        object? a = NextArg(args, ref argi);
                        char c = a is char ch ? ch : (char)(ToInt64(a) & (wide ? 0xFFFF : 0xFF));
                        PadString(sb, new ReadOnlySpan<char>(in c), width, fl);
                        break;
                    }
                case 's':
                    {
                        object? a = NextArg(args, ref argi);
                        string s = a switch
                        {
                            null => "(null)",
                            string str => str,
                            _ => throw new ArgumentException("CFormat: %s needs a string argument, got " + a.GetType().Name),
                        };
                        int len = prec >= 0 ? Math.Min(prec, s.Length) : s.Length;
                        PadString(sb, s.AsSpan(0, len), width, fl);
                        break;
                    }
                case 'f':
                case 'F':
                case 'e':
                case 'E':
                case 'g':
                case 'G':
                    FormatDouble(sb, conv, fl, width, prec, ToDouble(NextArg(args, ref argi)));
                    break;
                default:
                    throw new FormatException("CFormat: unsupported conversion '%" + conv + "' in \"" + format + "\"");
            }
        }
    }

    private static int ClampedAdd(int v, int digit)
    {
        long r = (long)v * 10 + digit;
        return r > int.MaxValue / 2 ? int.MaxValue / 2 : (int)r;
    }

    private static object? NextArg(object?[] args, ref int argi)
    {
        if (argi >= args.Length)
            throw new ArgumentException("CFormat: too few arguments for the format");
        return args[argi++];
    }

    private static long ToInt64(object? a) => a switch
    {
        int v => v,
        long v => v,
        uint v => v,
        ulong v => unchecked((long)v),
        short v => v,
        ushort v => v,
        byte v => v,
        sbyte v => v,
        char v => v,
        bool v => v ? 1 : 0,
        Enum v => Convert.ToInt64(v, CultureInfo.InvariantCulture),
        _ => throw new ArgumentException("CFormat: integer argument expected, got " + (a?.GetType().Name ?? "null")),
    };

    private static double ToDouble(object? a) => a switch
    {
        double v => v,
        float v => v,
        _ => throw new ArgumentException("CFormat: floating argument expected, got " + (a?.GetType().Name ?? "null")),
    };

    private static void Pad(StringBuilder sb, int count, char c)
    {
        if (count > 0)
            sb.Append(c, count);
    }

    private static void PadString(StringBuilder sb, ReadOnlySpan<char> s, int width, Flags fl)
    {
        int pad = width - s.Length;
        if ((fl & Flags.Left) == 0)
            Pad(sb, pad, ' ');
        sb.Append(s);
        if ((fl & Flags.Left) != 0)
            Pad(sb, pad, ' ');
    }

    /* Writes sign, then body, with padding to width. */
    private static void PadNumber(StringBuilder sb, string sign, ReadOnlySpan<char> body, Flags fl, int width, bool zeroPad)
    {
        int pad = width - sign.Length - body.Length;
        if ((fl & Flags.Left) != 0)
        {
            sb.Append(sign).Append(body);
            Pad(sb, pad, ' ');
        }
        else if (zeroPad)
        {
            sb.Append(sign);
            Pad(sb, pad, '0');
            sb.Append(body);
        }
        else
        {
            Pad(sb, pad, ' ');
            sb.Append(sign).Append(body);
        }
    }

    /*------------------------------ integers ---------------------------------*/

    private static void FormatInteger(StringBuilder sb, char conv, Flags fl, int width, int prec, int bits, object? arg)
    {
        long raw = ToInt64(arg);
        bool signed = conv == 'd' || conv == 'i';
        bool neg = false;
        ulong mag;
        if (signed)
        {
            long v = bits switch { 8 => (sbyte)raw, 16 => (short)raw, 32 => (int)raw, _ => raw };
            neg = v < 0;
            mag = neg ? 0UL - (ulong)v : (ulong)v;
        }
        else
        {
            ulong u = (ulong)raw;
            mag = bits switch { 8 => (byte)u, 16 => (ushort)u, 32 => (uint)u, _ => u };
        }

        uint radix = conv == 'o' ? 8u : (conv == 'x' || conv == 'X') ? 16u : 10u;
        string hexDigits = conv == 'X' ? "0123456789ABCDEF" : "0123456789abcdef";
        Span<char> buf = stackalloc char[24];
        int pos = buf.Length;
        ulong t = mag;
        do
        {
            buf[--pos] = hexDigits[(int)(t % radix)];
            t /= radix;
        } while (t != 0);
        int ndig = buf.Length - pos;
        if (prec == 0 && mag == 0)
            ndig = 0;   // "%.0d" of 0 prints no digits
        int zeros = Math.Max(0, prec - ndig);
        if (conv == 'o' && (fl & Flags.Alt) != 0 && zeros == 0 && (ndig == 0 || buf[pos] != '0'))
            zeros = 1;

        string prefix;
        if (signed)
            prefix = neg ? "-" : (fl & Flags.Plus) != 0 ? "+" : (fl & Flags.Space) != 0 ? " " : "";
        else if ((conv == 'x' || conv == 'X') && (fl & Flags.Alt) != 0 && mag != 0)
            prefix = conv == 'x' ? "0x" : "0X";
        else
            prefix = "";

        StringBuilder body = t_body ??= new StringBuilder();
        body.Clear();
        body.Append('0', zeros);
        body.Append(buf.Slice(buf.Length - ndig, ndig));
        bool zeroPad = (fl & Flags.Zero) != 0 && prec < 0;
        PadNumber(sb, prefix, body.ToString(), fl, width, zeroPad);
    }

    /*----------------------------- floating point ----------------------------*/

    private static void FormatDouble(StringBuilder sb, char conv, Flags fl, int width, int prec, double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        bool neg = bits < 0;
        bool upper = conv == 'F' || conv == 'E' || conv == 'G';
        char lc = char.ToLowerInvariant(conv);
        bool alt = (fl & Flags.Alt) != 0;

        string sign = neg ? "-" : (fl & Flags.Plus) != 0 ? "+" : (fl & Flags.Space) != 0 ? " " : "";
        StringBuilder body = t_body ??= new StringBuilder();
        body.Clear();

        if (((bits >> 52) & 0x7FF) == 0x7FF)
        {
            ulong mant = (ulong)bits & 0xFFFFFFFFFFFFFUL;
            string text;
            if (mant == 0)
                text = "inf";
            else if ((mant >> 51) == 0)
                text = "nan(snan)";
            else if (neg && mant == 0x8000000000000UL)
                text = "nan(ind)";
            else
                text = "nan";
            body.Append(upper ? text.ToUpperInvariant() : text);
            PadNumber(sb, sign, body.ToString(), fl, width, false);
            return;
        }

        Span<char> scratch = stackalloc char[40];
        ReadOnlySpan<char> exact = Decompose(Math.Abs(value), scratch, out int pointPos);
        Span<char> rounded = exact.Length + 2 <= 256 ? stackalloc char[256] : new char[exact.Length + 2];

        switch (lc)
        {
            case 'f':
                {
                    int p = prec < 0 ? 6 : prec;
                    int len = RoundDigits(exact, (int)Math.Min((long)pointPos + p, int.MaxValue), rounded, ref pointPos);
                    EmitFixed(body, rounded.Slice(0, len), pointPos, p, alt);
                    break;
                }
            case 'e':
                {
                    int p = prec < 0 ? 6 : prec;
                    int len = RoundDigits(exact, (int)Math.Min((long)p + 1, int.MaxValue), rounded, ref pointPos);
                    EmitExp(body, rounded.Slice(0, len), pointPos - 1, p, alt, upper);
                    break;
                }
            default:
                {
                    int p = prec < 0 ? 6 : prec == 0 ? 1 : prec;
                    int len = RoundDigits(exact, p, rounded, ref pointPos);
                    ReadOnlySpan<char> r = rounded.Slice(0, len);
                    int x = pointPos - 1;
                    int sig = len;
                    while (sig > 0 && r[sig - 1] == '0')
                        sig--;
                    if (x < -4 || x >= p)
                        EmitExp(body, r, x, alt ? p - 1 : Math.Max(0, sig - 1), alt, upper);
                    else
                        EmitFixed(body, r, pointPos, alt ? p - 1 - x : Math.Max(0, sig - pointPos), alt);
                    break;
                }
        }
        PadNumber(sb, sign, body.ToString(), fl, width, (fl & Flags.Zero) != 0);
    }

    private static char DigitAt(ReadOnlySpan<char> r, int i) => (uint)i < (uint)r.Length ? r[i] : '0';

    /* The value is 0.r * 10^pointPos; digits beyond r are zeros. */
    private static void EmitFixed(StringBuilder b, ReadOnlySpan<char> r, int pointPos, int frac, bool forcePoint)
    {
        if (pointPos <= 0)
            b.Append('0');
        else
            for (int i = 0; i < pointPos; i++)
                b.Append(DigitAt(r, i));
        if (frac > 0 || forcePoint)
            b.Append('.');
        for (int j = 0; j < frac; j++)
            b.Append(DigitAt(r, pointPos + j));
    }

    private static void EmitExp(StringBuilder b, ReadOnlySpan<char> r, int exp10, int frac, bool forcePoint, bool upper)
    {
        b.Append(DigitAt(r, 0));
        if (frac > 0 || forcePoint)
            b.Append('.');
        for (int j = 1; j <= frac; j++)
            b.Append(DigitAt(r, j));
        b.Append(upper ? 'E' : 'e');
        b.Append(exp10 < 0 ? '-' : '+');
        int ax = Math.Abs(exp10);
        if (ax < 10)
            b.Append('0');
        b.Append(ax.ToString(CultureInfo.InvariantCulture));
    }

    /*
     * Rounds the exact digit string d (value 0.d * 10^pointPos) to its first n digits,
     * half-to-even on the exact value. Returns the number of significant digits written to
     * dst (the rest are implicit zeros; 0 means the value is zero); pointPos is incremented
     * if the rounding carries out of the leading digit.
     */
    private static int RoundDigits(ReadOnlySpan<char> d, int n, Span<char> dst, ref int pointPos)
    {
        int len = d.Length;
        if (n >= len)
        {
            d.CopyTo(dst);
            return len;
        }
        if (n < 0)
            return 0;

        char first = d[n];
        bool up;
        if (first > '5')
            up = true;
        else if (first < '5')
            up = false;
        else
        {
            bool tail = false;
            for (int j = n + 1; j < len; j++)
            {
                if (d[j] != '0')
                {
                    tail = true;
                    break;
                }
            }
            up = tail || (n > 0 && ((d[n - 1] - '0') & 1) != 0);
        }

        if (n == 0)
        {
            if (!up)
                return 0;
            dst[0] = '1';
            pointPos++;
            return 1;
        }

        d.Slice(0, n).CopyTo(dst);
        if (up)
        {
            int k = n - 1;
            while (k >= 0 && dst[k] == '9')
            {
                dst[k] = '0';
                k--;
            }
            if (k < 0)
            {
                dst[0] = '1';
                pointPos++;
                return 1;
            }
            dst[k]++;
        }
        return n;
    }

    private static readonly UInt128[] s_pow5U128 = BuildPow5U128();
    private sealed class BigBox
    {
        public readonly BigInteger Value;
        public BigBox(BigInteger v) { Value = v; }
    }
    private static readonly BigBox?[] s_pow5Big = new BigBox?[1100];

    private static UInt128[] BuildPow5U128()
    {
        var t = new UInt128[32];
        t[0] = 1;
        for (int i = 1; i < t.Length; i++)
            t[i] = t[i - 1] * 5;
        return t;
    }

    private static BigInteger Pow5Big(int k)
    {
        BigBox? box = s_pow5Big[k];
        if (box == null)
        {
            box = new BigBox(BigInteger.Pow(5, k));
            s_pow5Big[k] = box;
        }
        return box.Value;
    }

    /*
     * Exact decimal expansion of a finite non-negative double: the result is the digit string
     * (no leading zeros; "0" for zero) and the value equals 0.digits * 10^pointPos.
     */
    private static ReadOnlySpan<char> Decompose(double abs, Span<char> scratch, out int pointPos)
    {
        ulong bits = (ulong)BitConverter.DoubleToInt64Bits(abs);
        int be = (int)((bits >> 52) & 0x7FF);
        ulong m = bits & 0xFFFFFFFFFFFFFUL;
        int e;
        if (be == 0)
            e = -1074;
        else
        {
            m |= 1UL << 52;
            e = be - 1075;
        }
        if (m == 0)
        {
            scratch[0] = '0';
            pointPos = 1;
            return scratch.Slice(0, 1);
        }
        int tz = BitOperations.TrailingZeroCount(m);
        m >>= tz;
        e += tz;

        if (e >= 0)
        {
            if (e <= 74)
            {
                int len = WriteU128((UInt128)m << e, scratch);
                pointPos = len;
                return scratch.Slice(0, len);
            }
            string s = (new BigInteger(m) << e).ToString(CultureInfo.InvariantCulture);
            pointPos = s.Length;
            return s;
        }

        int k = -e;
        if (k <= 31)
        {
            int len = WriteU128((UInt128)m * s_pow5U128[k], scratch);
            pointPos = len - k;
            return scratch.Slice(0, len);
        }
        string t = (new BigInteger(m) * Pow5Big(k)).ToString(CultureInfo.InvariantCulture);
        pointPos = t.Length - k;
        return t;
    }

    private static int WriteU128(UInt128 n, Span<char> dst)
    {
        const ulong E19 = 10_000_000_000_000_000_000UL;
        UInt128 q = n / E19;
        ulong lo = (ulong)(n - q * E19);
        ulong hi = (ulong)q;   // n < 2^127, so the quotient fits
        if (hi == 0)
            return WriteU64(lo, dst, 0, 1);
        int len = WriteU64(hi, dst, 0, 1);
        WriteU64(lo, dst, len, 19);
        return len + 19;
    }

    /* Writes v at dst[pos..], zero-padded to at least minDigits; returns the digit count. */
    private static int WriteU64(ulong v, Span<char> dst, int pos, int minDigits)
    {
        int cnt = 1;
        for (ulong t = v; t >= 10; t /= 10)
            cnt++;
        if (cnt < minDigits)
            cnt = minDigits;
        for (int i = cnt - 1; i >= 0; i--)
        {
            dst[pos + i] = (char)('0' + (int)(v % 10));
            v /= 10;
        }
        return cnt;
    }

    /*-------------------------------------------------------------------------*
     *                                sscanf                                   *
     *-------------------------------------------------------------------------*/

    private struct ScanSpec
    {
        public bool Suppress;
        public int Width;        // 0 = unlimited
        public int Bits;         // 8, 16, 32, 64
        public bool LongFloat;   // l or L: double target
        public char Conv;
        public int SetStart;     // %[ ]: format range of the set (after '[' and '^')
        public int SetEnd;
        public bool SetNegated;
    }

    /// <summary>
    /// Parses input like <c>sscanf</c>; see the class remarks. Returns the number of assigned
    /// items, or -1 if the input ended before the first conversion completed.
    /// </summary>
    public static int Sscanf(string input, string format, out object?[] values)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(format);

        int nvalues = 0;
        for (int k = 0; k < format.Length;)
        {
            if (format[k++] != '%')
                continue;
            ParseScanSpec(format, ref k, out ScanSpec sp);
            if (!sp.Suppress && sp.Conv != '%')
                nvalues++;
        }
        values = new object?[nvalues];

        int pos = 0;
        int fi = 0;
        int slot = 0;
        int assigned = 0;
        bool anyConv = false;
        int ilen = input.Length;

        while (fi < format.Length)
        {
            char fc = format[fi];
            if (IsSpace(fc))
            {
                while (fi < format.Length && IsSpace(format[fi]))
                    fi++;
                while (pos < ilen && IsSpace(input[pos]))
                    pos++;
                continue;
            }
            if (fc != '%')
            {
                if (pos >= ilen)
                    return ScanResult(true, assigned, anyConv);
                if (input[pos] != fc)
                    return ScanResult(false, assigned, anyConv);
                pos++;
                fi++;
                continue;
            }

            fi++;
            ParseScanSpec(format, ref fi, out ScanSpec sp);
            char conv = sp.Conv;

            if (conv == 'n')
            {
                if (!sp.Suppress)
                    values[slot++] = pos;
                continue;
            }

            if (conv != 'c' && conv != '[')
            {
                while (pos < ilen && IsSpace(input[pos]))
                    pos++;
            }
            if (conv == '%')
            {
                if (pos >= ilen)
                    return ScanResult(true, assigned, anyConv);
                if (input[pos] != '%')
                    return ScanResult(false, assigned, anyConv);
                pos++;
                continue;
            }
            if (pos >= ilen)
                return ScanResult(true, assigned, anyConv);

            object? result;
            switch (conv)
            {
                case 'd':
                case 'i':
                case 'u':
                case 'x':
                case 'X':
                case 'o':
                    {
                        int rc = ScanInteger(input, ref pos, sp, out ulong mag, out bool neg);
                        if (rc != 1)
                            return ScanResult(rc < 0, assigned, anyConv);
                        result = MakeInteger(conv, sp.Bits, mag, neg);
                        break;
                    }
                case 'f':
                case 'F':
                case 'e':
                case 'E':
                case 'g':
                case 'G':
                    {
                        int rc = ScanFloat(input, ref pos, sp, out result);
                        if (rc != 1)
                            return ScanResult(rc < 0, assigned, anyConv);
                        break;
                    }
                case 's':
                    {
                        int end = sp.Width > 0 ? (int)Math.Min((long)pos + sp.Width, ilen) : ilen;
                        int p = pos;
                        while (p < end && !IsSpace(input[p]))
                            p++;
                        result = input.Substring(pos, p - pos);
                        pos = p;
                        break;
                    }
                case 'c':
                    {
                        int cnt = sp.Width > 0 ? sp.Width : 1;
                        if (pos + cnt > ilen)
                            return ScanResult(true, assigned, anyConv);
                        result = input.Substring(pos, cnt);
                        pos += cnt;
                        break;
                    }
                case '[':
                    {
                        int end = sp.Width > 0 ? (int)Math.Min((long)pos + sp.Width, ilen) : ilen;
                        int p = pos;
                        while (p < end && InScanSet(format, sp, input[p]))
                            p++;
                        if (p == pos)
                            return ScanResult(false, assigned, anyConv);
                        result = input.Substring(pos, p - pos);
                        pos = p;
                        break;
                    }
                default:
                    throw new FormatException("CFormat: unsupported scanf conversion '%" + conv + "' in \"" + format + "\"");
            }

            if (!sp.Suppress)
            {
                values[slot++] = result;
                assigned++;
                anyConv = true;
            }
        }
        return assigned;
    }

    private static int ScanResult(bool inputFailure, int assigned, bool anyConv) =>
        inputFailure && !anyConv ? -1 : assigned;

    private static bool IsSpace(char c) => c == ' ' || (c >= '\t' && c <= '\r');

    /* fi points just after '%'; on return it points after the conversion character. */
    private static void ParseScanSpec(string f, ref int fi, out ScanSpec sp)
    {
        sp = default;
        sp.Bits = 32;
        int n = f.Length;
        if (fi < n && f[fi] == '*')
        {
            sp.Suppress = true;
            fi++;
        }
        while (fi < n && f[fi] >= '0' && f[fi] <= '9')
        {
            sp.Width = ClampedAdd(sp.Width, f[fi] - '0');
            fi++;
        }
        if (fi < n)
        {
            switch (f[fi])
            {
                case 'h':
                    fi++;
                    if (fi < n && f[fi] == 'h') { fi++; sp.Bits = 8; }
                    else sp.Bits = 16;
                    break;
                case 'l':
                    fi++;
                    sp.LongFloat = true;
                    if (fi < n && f[fi] == 'l') { fi++; sp.Bits = 64; }
                    break;
                case 'L':
                    fi++;
                    sp.LongFloat = true;
                    break;
                case 'z':
                case 'j':
                case 't':
                    fi++;
                    sp.Bits = 64;
                    break;
                case 'I':
                    fi++;
                    if (fi + 1 < n && f[fi] == '6' && f[fi + 1] == '4') { fi += 2; sp.Bits = 64; }
                    else if (fi + 1 < n && f[fi] == '3' && f[fi + 1] == '2') { fi += 2; sp.Bits = 32; }
                    else sp.Bits = 64;
                    break;
            }
        }
        if (fi >= n)
            throw new FormatException("CFormat: incomplete conversion specification in \"" + f + "\"");
        sp.Conv = f[fi++];
        if (sp.Conv == '[')
        {
            if (fi < n && f[fi] == '^')
            {
                sp.SetNegated = true;
                fi++;
            }
            sp.SetStart = fi;
            if (fi < n && f[fi] == ']')   // a leading ']' belongs to the set
                fi++;
            while (fi < n && f[fi] != ']')
                fi++;
            if (fi >= n)
                throw new FormatException("CFormat: unterminated %[ in \"" + f + "\"");
            sp.SetEnd = fi;
            fi++;
        }
    }

    private static bool InScanSet(string f, in ScanSpec sp, char c)
    {
        bool found = false;
        for (int k = sp.SetStart; k < sp.SetEnd; k++)
        {
            if (k + 2 < sp.SetEnd && f[k + 1] == '-')
            {
                if (c >= f[k] && c <= f[k + 2])
                {
                    found = true;
                    break;
                }
                k += 2;
            }
            else if (f[k] == c)
            {
                found = true;
                break;
            }
        }
        return found != sp.SetNegated;
    }

    private static int HexValue(char c) =>
        c >= '0' && c <= '9' ? c - '0' :
        c >= 'a' && c <= 'f' ? c - 'a' + 10 :
        c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;

    /* Returns 1 on success, 0 on matching failure, -1 on input failure. */
    private static int ScanInteger(string s, ref int pos, in ScanSpec sp, out ulong mag, out bool neg)
    {
        mag = 0;
        neg = false;
        int end = sp.Width > 0 ? (int)Math.Min((long)pos + sp.Width, s.Length) : s.Length;
        int p = pos;
        if (p < end && (s[p] == '+' || s[p] == '-'))
        {
            neg = s[p] == '-';
            p++;
        }
        if (p >= end)
            return 0;

        int radix = sp.Conv switch { 'x' or 'X' => 16, 'o' => 8, 'i' => 0, _ => 10 };
        if ((radix == 0 || radix == 16) && s[p] == '0' && p + 2 < end
            && (s[p + 1] == 'x' || s[p + 1] == 'X') && HexValue(s[p + 2]) >= 0)
        {
            p += 2;
            radix = 16;
        }
        else if (radix == 0)
            radix = s[p] == '0' ? 8 : 10;

        int start = p;
        while (p < end)
        {
            int dv = HexValue(s[p]);
            if (dv < 0 || dv >= radix)
                break;
            mag = mag * (uint)radix + (uint)dv;
            p++;
        }
        if (p == start)
            return 0;
        pos = p;
        return 1;
    }

    private static object MakeInteger(char conv, int bits, ulong mag, bool neg)
    {
        if (conv == 'd' || conv == 'i')
        {
            long v = neg ? (long)(0UL - mag) : (long)mag;
            return bits switch { 8 => (object)(sbyte)v, 16 => (short)v, 32 => (int)v, _ => v };
        }
        ulong u = neg ? 0UL - mag : mag;
        return bits switch { 8 => (object)(byte)u, 16 => (ushort)u, 32 => (uint)u, _ => u };
    }

    /* Returns 1 on success, 0 on matching failure, -1 on input failure. */
    private static int ScanFloat(string s, ref int pos, in ScanSpec sp, out object? result)
    {
        result = null;
        int end = sp.Width > 0 ? (int)Math.Min((long)pos + sp.Width, s.Length) : s.Length;
        int p = pos;
        bool neg = false;
        if (p < end && (s[p] == '+' || s[p] == '-'))
        {
            neg = s[p] == '-';
            p++;
        }
        if (p >= end)
            return 0;

        if (MatchWord(s, p, end, "inf"))
        {
            p += 3;
            if (MatchWord(s, p, end, "inity"))
                p += 5;
            pos = p;
            result = Box(neg ? double.NegativeInfinity : double.PositiveInfinity, sp);
            return 1;
        }
        if (MatchWord(s, p, end, "nan"))
        {
            p += 3;
            if (p < end && s[p] == '(')
            {
                int q = p + 1;
                while (q < end && (char.IsAsciiLetterOrDigit(s[q]) || s[q] == '_'))
                    q++;
                if (q < end && s[q] == ')')
                    p = q + 1;
            }
            pos = p;
            double nan = BitConverter.Int64BitsToDouble(neg ? unchecked((long)0xFFF8000000000000UL) : 0x7FF8000000000000L);
            result = Box(nan, sp);
            return 1;
        }

        int numStart = pos;
        int digits = 0;
        while (p < end && s[p] >= '0' && s[p] <= '9')
        {
            p++;
            digits++;
        }
        if (p < end && s[p] == '.')
        {
            p++;
            while (p < end && s[p] >= '0' && s[p] <= '9')
            {
                p++;
                digits++;
            }
        }
        if (digits == 0)
            return 0;
        if (p < end && (s[p] == 'e' || s[p] == 'E'))
        {
            int q = p + 1;
            if (q < end && (s[q] == '+' || s[q] == '-'))
                q++;
            int expStart = q;
            while (q < end && s[q] >= '0' && s[q] <= '9')
                q++;
            if (q > expStart)
                p = q;
        }

        ReadOnlySpan<char> text = s.AsSpan(numStart, p - numStart);
        if (sp.LongFloat)
            result = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        else
            result = float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        pos = p;
        return 1;
    }

    private static object Box(double v, in ScanSpec sp) => sp.LongFloat ? v : (object)(float)v;

    private static bool MatchWord(string s, int p, int end, string word)
    {
        if (p + word.Length > end)
            return false;
        for (int i = 0; i < word.Length; i++)
        {
            if (char.ToLowerInvariant(s[p + i]) != word[i])
                return false;
        }
        return true;
    }
}
