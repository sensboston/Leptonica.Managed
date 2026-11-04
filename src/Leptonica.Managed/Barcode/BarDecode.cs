using System;

namespace Leptonica;

/*
 *  bardecode.c: 1D barcode format identification and symbology decoders.
 */

public static partial class L
{
        /* Flags for method of extracting barcode widths */

    /// <summary>Extract barcode bar widths directly from the measured bar widths.</summary>
    public const int L_USE_WIDTHS = 1;

    /// <summary>Extract barcode bar widths by analyzing the crossings in a sliding window.</summary>
    public const int L_USE_WINDOWS = 2;

        /* Flags for barcode formats */

    /// <summary>The barcode format is unknown.</summary>
    public const int L_BF_UNKNOWN = 0;

    /// <summary>Try all supported barcode formats.</summary>
    public const int L_BF_ANY = 1;

    /// <summary>Code 128 format.</summary>
    public const int L_BF_CODE128 = 2;

    /// <summary>EAN-8 format.</summary>
    public const int L_BF_EAN8 = 3;

    /// <summary>EAN-13 format.</summary>
    public const int L_BF_EAN13 = 4;

    /// <summary>Code 2 of 5 format.</summary>
    public const int L_BF_CODE2OF5 = 5;

    /// <summary>Interleaved Code 2 of 5 format.</summary>
    public const int L_BF_CODEI2OF5 = 6;

    /// <summary>Code 39 format.</summary>
    public const int L_BF_CODE39 = 7;

    /// <summary>Code 93 format.</summary>
    public const int L_BF_CODE93 = 8;

    /// <summary>Codabar format.</summary>
    public const int L_BF_CODABAR = 9;

    /// <summary>UPC-A format.</summary>
    public const int L_BF_UPCA = 10;
}

/// <summary>
/// Identifies the format of a 1D barcode from a string of bar widths and decodes it
/// into text.
/// </summary>
/// <remarks>
/// The decoders currently handle Code 2 of 5, Interleaved 2 of 5, Code 39, Code 93,
/// Codabar, UPC-A and EAN-13. The input is a string of bar widths, each in the set
/// {1,2,3,4}, alternating between black and white bars and starting with a black bar.
/// </remarks>
public static partial class Barcode
{
    private static readonly int[] SupportedBarcodeFormat = {
        L.L_BF_CODE2OF5,
        L.L_BF_CODEI2OF5,
        L.L_BF_CODE93,
        L.L_BF_CODE39,
        L.L_BF_CODABAR,
        L.L_BF_UPCA,
        L.L_BF_EAN13
    };

    private static readonly string[] SupportedBarcodeFormatName = {
        "Code2of5",
        "CodeI2of5",
        "Code93",
        "Code39",
        "Codabar",
        "Upca",
        "Ean13"
    };

    private const int NumSupportedBarcodeFormats = 7;

        /* Code 2 of 5 symbology */
    private static readonly string[] Code2of5 = {
        "111121211", "211111112", "112111112", "212111111",
        "111121112", "211121111", "112121111", "111111212",
        "211111211", "112111211",
        "21211", "21112"
    };
    private const int C25_START = 10;
    private const int C25_STOP = 11;

        /* Code Interleaved 2 of 5 symbology */
    private static readonly string[] CodeI2of5 = {
        "11221", "21112", "12112", "22111", "11212",
        "21211", "12211", "11122", "21121", "12121",
        "1111", "211"
    };
    private const int CI25_START = 10;
    private const int CI25_STOP = 11;

        /* Code 93 symbology */
    private static readonly string[] Code93 = {
        "131112", "111213", "111312", "111411", "121113",
        "121212", "121311", "111114", "131211", "141111",
        "211113", "211212", "211311", "221112", "221211",
        "231111", "112113", "112212", "112311", "122112",
        "132111", "111123", "111222", "111321", "121122",
        "131121", "212112", "212211", "211122", "211221",
        "221121", "222111", "112122", "112221", "122121",
        "123111", "121131", "311112", "311211", "321111",
        "112131", "113121", "211131", "131221", "312111",
        "311121", "122211", "111141"
    };
    private const string Code93Val = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%[]{}#";
    private const int C93_START = 47;
    private const int C93_STOP = 47;

        /* Code 39 symbology */
    private static readonly string[] Code39 = {
        "111221211", "211211112", "112211112", "212211111",
        "111221112", "211221111", "112221111", "111211212",
        "211211211", "112211211", "211112112", "112112112",
        "212112111", "111122112", "211122111", "112122111",
        "111112212", "211112211", "112112211", "111122211",
        "211111122", "112111122", "212111121", "111121122",
        "211121121", "112121121", "111111222", "211111221",
        "112111221", "111121221", "221111112", "122111112",
        "222111111", "121121112", "221121111", "122121111",
        "121111212", "221111211", "122111211", "121212111",
        "121211121", "121112121", "111212121", "121121211"
    };
    private const string Code39Val = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%*";
    private const int C39_START = 43;
    private const int C39_STOP = 43;

        /* Codabar symbology */
    private static readonly string[] Codabar = {
        "1111122", "1111221", "1112112", "2211111", "1121121",
        "2111121", "1211112", "1211211", "1221111", "2112111",
        "1112211", "1122111", "2111212", "2121112", "2121211",
        "1121212", "1122121", "1212112", "1112122", "1112221"
    };
    private const string CodabarVal = "0123456789-$:/.+ABCD";

        /* UPC-A symbology */
    private static readonly string[] Upca = {
        "3211", "2221", "2122", "1411", "1132",
        "1231", "1114", "1312", "1213", "3112",
        "111", "111", "11111"
    };
    private const int UPCA_START = 10;
    private const int UPCA_STOP = 11;
    private const int UPCA_MID = 12;

    /*------------------------------------------------------------------------*
     *                           Decoding dispatcher                          *
     *------------------------------------------------------------------------*/
    /// <summary>Decodes a string of bar widths into the barcode text, dispatching on the barcode format.</summary>
    /// <param name="barstr">String of bar widths, each in the set {1,2,3,4}.</param>
    /// <param name="format">
    /// The barcode format, for example <see cref="L.L_BF_CODEI2OF5"/> or <see cref="L.L_BF_CODE93"/>,
    /// or <see cref="L.L_BF_ANY"/> to identify the format automatically.
    /// </param>
    /// <param name="debugflag">Reserved for debug output; currently ignored.</param>
    /// <returns>The decoded text, or <c>null</c> if the format is not implemented or the data cannot be decoded.</returns>
    /// <exception cref="ArgumentException"><paramref name="barstr"/> is <c>null</c>.</exception>
    // leptonica: barcodeDispatchDecoder (bardecode.c)
    public static string? DispatchDecoder(string barstr, int format, int debugflag)
    {
        const string func = "barcodeDispatchDecoder";
        if (barstr == null)
            throw Lept.Arg(func, "barstr not defined");

        debugflag = 0;

        if (format == L.L_BF_ANY)
            format = FindFormat(barstr);

        if (format == L.L_BF_CODE2OF5)
            return Decode2of5(barstr, debugflag);
        else if (format == L.L_BF_CODEI2OF5)
            return DecodeI2of5(barstr, debugflag);
        else if (format == L.L_BF_CODE93)
            return Decode93(barstr, debugflag);
        else if (format == L.L_BF_CODE39)
            return Decode39(barstr, debugflag);
        else if (format == L.L_BF_CODABAR)
            return DecodeCodabar(barstr, debugflag);
        else if (format == L.L_BF_UPCA)
            return DecodeUpca(barstr, debugflag);
        else if (format == L.L_BF_EAN13)
            return DecodeEan13(barstr, 0, debugflag);
        else {
            Log.Error(func, "format not implemented");
            return null;
        }
    }

    /*------------------------------------------------------------------------*
     *                      Barcode format determination                      *
     *------------------------------------------------------------------------*/
    // leptonica: barcodeFindFormat (bardecode.c)
    private static int FindFormat(string barstr)
    {
        const string func = "barcodeFindFormat";
        if (barstr == null)
            return L.L_BF_UNKNOWN;

        for (int i = 0; i < NumSupportedBarcodeFormats; i++) {
            int format = SupportedBarcodeFormat[i];
            VerifyFormat(barstr, format, out bool valid, out _);
            if (valid) {
                Log.Info(func, $"Barcode format: {SupportedBarcodeFormatName[i]}");
                return format;
            }
        }
        return L.L_BF_UNKNOWN;
    }

    /// <summary>Determines whether a barcode format is supported by the decoder.</summary>
    /// <param name="format">The barcode format, one of the <c>L.L_BF_*</c> constants.</param>
    /// <returns><c>true</c> if a decoder exists for <paramref name="format"/>; otherwise <c>false</c>.</returns>
    // leptonica: barcodeFormatIsSupported (bardecode.c)
    public static bool FormatIsSupported(int format)
    {
        for (int i = 0; i < NumSupportedBarcodeFormats; i++) {
            if (format == SupportedBarcodeFormat[i])
                return true;
        }
        return false;
    }

    // leptonica: barcodeVerifyFormat (bardecode.c)
    private static int VerifyFormat(string barstr, int format, out bool pvalid, out bool preverse)
    {
        const string func = "barcodeVerifyFormat";
        pvalid = false;
        preverse = false;
        if (barstr == null) {
            Log.Error(func, "barstr not defined");
            return 1;
        }

        int len = barstr.Length;
        switch (format) {
        case L.L_BF_CODE2OF5: {
            if (len < 20) {
                Log.Error(func, "barstr too short for CODE2OF5");
                return 1;
            }
            bool start = StrncmpEq(barstr, 0, Code2of5[C25_START], 3);
            bool stop = StrncmpEq(barstr, len - 5, Code2of5[C25_STOP], 5);
            if (start && stop) {
                pvalid = true;
            } else {
                string rev = ReverseString(barstr);
                start = StrncmpEq(rev, 0, Code2of5[C25_START], 3);
                stop = StrncmpEq(rev, len - 5, Code2of5[C25_STOP], 5);
                if (start && stop) {
                    pvalid = true;
                    preverse = true;
                }
            }
            break;
        }
        case L.L_BF_CODEI2OF5: {
            if (len < 20) {
                Log.Error(func, "barstr too short for CODEI2OF5");
                return 1;
            }
            bool start = StrncmpEq(barstr, 0, CodeI2of5[CI25_START], 4);
            bool stop = StrncmpEq(barstr, len - 3, CodeI2of5[CI25_STOP], 3);
            if (start && stop) {
                pvalid = true;
            } else {
                string rev = ReverseString(barstr);
                start = StrncmpEq(rev, 0, CodeI2of5[CI25_START], 4);
                stop = StrncmpEq(rev, len - 3, CodeI2of5[CI25_STOP], 3);
                if (start && stop) {
                    pvalid = true;
                    preverse = true;
                }
            }
            break;
        }
        case L.L_BF_CODE93: {
            if (len < 28) {
                Log.Error(func, "barstr too short for CODE93");
                return 1;
            }
            bool start = StrncmpEq(barstr, 0, Code93[C93_START], 6);
            bool stop = StrncmpEq(barstr, len - 7, Code93[C93_STOP], 6);
            if (start && stop) {
                pvalid = true;
            } else {
                string rev = ReverseString(barstr);
                start = StrncmpEq(rev, 0, Code93[C93_START], 6);
                stop = StrncmpEq(rev, len - 7, Code93[C93_STOP], 6);
                if (start && stop) {
                    pvalid = true;
                    preverse = true;
                }
            }
            break;
        }
        case L.L_BF_CODE39: {
            if (len < 30) {
                Log.Error(func, "barstr too short for CODE39");
                return 1;
            }
            bool start = StrncmpEq(barstr, 0, Code39[C39_START], 9);
            bool stop = StrncmpEq(barstr, len - 9, Code39[C39_STOP], 9);
            if (start && stop) {
                pvalid = true;
            } else {
                string rev = ReverseString(barstr);
                start = StrncmpEq(rev, 0, Code39[C39_START], 9);
                stop = StrncmpEq(rev, len - 9, Code39[C39_STOP], 9);
                if (start && stop) {
                    pvalid = true;
                    preverse = true;
                }
            }
            break;
        }
        case L.L_BF_CODABAR: {
            if (len < 26) {
                Log.Error(func, "barstr too short for CODABAR");
                return 1;
            }
            int start = 0, stop = 0;
            for (int i = 16; i <= 19; i++) {
                if (StrncmpEq(barstr, 0, Codabar[i], 7)) start++;
                if (StrncmpEq(barstr, len - 7, Codabar[i], 7)) stop++;
            }
            if (start != 0 && stop != 0) {
                pvalid = true;
            } else {
                start = stop = 0;
                string rev = ReverseString(barstr);
                for (int i = 16; i <= 19; i++) {
                    if (StrncmpEq(rev, 0, Codabar[i], 7)) start++;
                    if (StrncmpEq(rev, len - 7, Codabar[i], 7)) stop++;
                }
                if (start != 0 && stop != 0) {
                    pvalid = true;
                    preverse = true;
                }
            }
            break;
        }
        case L.L_BF_UPCA:
        case L.L_BF_EAN13: {
            if (len != 59) {
                Log.Error(func, "invalid length for UPCA or EAN13");
                return 1;
            }
            bool start = StrncmpEq(barstr, 0, Upca[UPCA_START], 3);
            bool mid = StrncmpEq(barstr, 27, Upca[UPCA_MID], 5);
            bool stop = StrncmpEq(barstr, len - 3, Upca[UPCA_STOP], 3);
            if (start && mid && stop)
                pvalid = true;
            break;
        }
        default:
            Log.Error(func, "format not supported");
            return 1;
        }

        return 0;
    }

    /*------------------------------------------------------------------------*
     *                             Code 2 of 5                                *
     *------------------------------------------------------------------------*/
    // leptonica: barcodeDecode2of5 (bardecode.c)
    private static string? Decode2of5(string barstr, int debugflag)
    {
        const string func = "barcodeDecode2of5";
        VerifyFormat(barstr, L.L_BF_CODE2OF5, out bool valid, out bool reverse);
        if (!valid) {
            Log.Error(func, "barstr not in 2of5 format");
            return null;
        }
        string vbarstr = reverse ? ReverseString(barstr) : barstr;

        int len = vbarstr.Length;
        if ((len - 11) % 10 != 0) {
            Log.Error(func, "size not divisible by 10: invalid 2of5 code");
            return null;
        }

        bool error = false;
        int ndigits = (len - 11) / 10;
        char[] data = new char[ndigits];
        for (int i = 0; i < ndigits; i++) {
            string code = vbarstr.Substring(6 + 10 * i, 9);
            if (debugflag != 0)
                Console.Error.WriteLine($"code: {code}");

            bool found = false;
            for (int j = 0; j < 10; j++) {
                if (code == Code2of5[j]) {
                    data[i] = (char)('0' + j);
                    found = true;
                    break;
                }
            }
            if (!found) error = true;
        }

        if (error) {
            Log.Error(func, "error in decoding");
            return null;
        }
        return new string(data);
    }

    /*------------------------------------------------------------------------*
     *                       Interleaved Code 2 of 5                          *
     *------------------------------------------------------------------------*/
    // leptonica: barcodeDecodeI2of5 (bardecode.c)
    private static string? DecodeI2of5(string barstr, int debugflag)
    {
        const string func = "barcodeDecodeI2of5";
        VerifyFormat(barstr, L.L_BF_CODEI2OF5, out bool valid, out bool reverse);
        if (!valid) {
            Log.Error(func, "barstr not in i2of5 format");
            return null;
        }
        string vbarstr = reverse ? ReverseString(barstr) : barstr;

        int len = vbarstr.Length;
        if ((len - 7) % 10 != 0) {
            Log.Error(func, "size not divisible by 10: invalid I2of5 code");
            return null;
        }

        bool error = false;
        int npairs = (len - 7) / 10;
        char[] data = new char[2 * npairs];
        char[] c1 = new char[5];
        char[] c2 = new char[5];
        for (int i = 0; i < npairs; i++) {
            int start = 4 + 10 * i;
            for (int j = 0; j < 5; j++) {
                c1[j] = vbarstr[start + 2 * j];
                c2[j] = vbarstr[start + 2 * j + 1];
            }
            string code1 = new string(c1);
            string code2 = new string(c2);
            if (debugflag != 0)
                Console.Error.WriteLine($"code1: {code1}, code2: {code2}");

            bool found = false;
            for (int j = 0; j < 10; j++) {
                if (code1 == CodeI2of5[j]) {
                    data[2 * i] = (char)('0' + j);
                    found = true;
                    break;
                }
            }
            if (!found) error = true;

            found = false;
            for (int j = 0; j < 10; j++) {
                if (code2 == CodeI2of5[j]) {
                    data[2 * i + 1] = (char)('0' + j);
                    found = true;
                    break;
                }
            }
            if (!found) error = true;
        }

        if (error) {
            Log.Error(func, "error in decoding");
            return null;
        }
        return new string(data);
    }

    /*------------------------------------------------------------------------*
     *                                 Code 93                                *
     *------------------------------------------------------------------------*/
    // leptonica: barcodeDecode93 (bardecode.c)
    private static string? Decode93(string barstr, int debugflag)
    {
        const string func = "barcodeDecode93";
        VerifyFormat(barstr, L.L_BF_CODE93, out bool valid, out bool reverse);
        if (!valid) {
            Log.Error(func, "barstr not in code93 format");
            return null;
        }
        string vbarstr = reverse ? ReverseString(barstr) : barstr;

        int len = vbarstr.Length;
        if ((len - 13) % 6 != 0) {
            Log.Error(func, "size not divisible by 6: invalid code 93");
            return null;
        }

        int nsymb = (len - 13) / 6;
        char[] data = new char[nsymb];
        int[] index = new int[nsymb];
        bool error = false;
        for (int i = 0; i < nsymb; i++) {
            string code = vbarstr.Substring(6 + 6 * i, 6);
            if (debugflag != 0)
                Console.Error.WriteLine($"code: {code}");

            bool found = false;
            for (int j = 0; j < C93_START; j++) {
                if (code == Code93[j]) {
                    data[i] = Code93Val[j];
                    index[i] = j;
                    found = true;
                    break;
                }
            }
            if (!found) error = true;
        }

        if (error) {
            Log.Error(func, "error in decoding");
            return null;
        }

        int sum = 0;
        for (int i = 0; i < nsymb - 2; i++)
            sum += ((i % 20) + 1) * index[nsymb - 3 - i];
        if (data[nsymb - 2] != Code93Val[sum % 47])
            Log.Warning(func, "Error for check C");
        if (debugflag != 0)
            Console.Error.WriteLine($"checkc = {Code93[sum % 47]}");

        sum = 0;
        for (int i = 0; i < nsymb - 1; i++)
            sum += ((i % 15) + 1) * index[nsymb - 2 - i];
        if (data[nsymb - 1] != Code93Val[sum % 47])
            Log.Warning(func, "Error for check K");
        if (debugflag != 0)
            Console.Error.WriteLine($"checkk = {Code93[sum % 47]}");

        return new string(data, 0, nsymb - 2);
    }

    /*------------------------------------------------------------------------*
     *                                 Code 39                                *
     *------------------------------------------------------------------------*/
    // leptonica: barcodeDecode39 (bardecode.c)
    private static string? Decode39(string barstr, int debugflag)
    {
        const string func = "barcodeDecode39";
        VerifyFormat(barstr, L.L_BF_CODE39, out bool valid, out bool reverse);
        if (!valid) {
            Log.Error(func, "barstr not in code39 format");
            return null;
        }
        string vbarstr = reverse ? ReverseString(barstr) : barstr;

        int len = vbarstr.Length;
        if ((len + 1) % 10 != 0) {
            Log.Error(func, "size+1 not divisible by 10: invalid code 39");
            return null;
        }

        int nsymb = (len - 19) / 10;
        char[] data = new char[nsymb];
        bool error = false;
        for (int i = 0; i < nsymb; i++) {
            string code = vbarstr.Substring(10 + 10 * i, 9);
            if (debugflag != 0)
                Console.Error.WriteLine($"code: {code}");

            bool found = false;
            for (int j = 0; j < C39_START; j++) {
                if (code == Code39[j]) {
                    data[i] = Code39Val[j];
                    found = true;
                    break;
                }
            }
            if (!found) error = true;
        }

        if (error) {
            Log.Error(func, "error in decoding");
            return null;
        }
        return new string(data);
    }

    /*------------------------------------------------------------------------*
     *                                 Codabar                                *
     *------------------------------------------------------------------------*/
    // leptonica: barcodeDecodeCodabar (bardecode.c)
    private static string? DecodeCodabar(string barstr, int debugflag)
    {
        const string func = "barcodeDecodeCodabar";
        VerifyFormat(barstr, L.L_BF_CODABAR, out bool valid, out bool reverse);
        if (!valid) {
            Log.Error(func, "barstr not in codabar format");
            return null;
        }
        string vbarstr = reverse ? ReverseString(barstr) : barstr;

        int len = vbarstr.Length;
        if ((len + 1) % 8 != 0) {
            Log.Error(func, "size+1 not divisible by 8: invalid codabar");
            return null;
        }

        int nsymb = (len - 15) / 8;
        char[] data = new char[nsymb];
        bool error = false;
        for (int i = 0; i < nsymb; i++) {
            string code = vbarstr.Substring(8 + 8 * i, 7);
            if (debugflag != 0)
                Console.Error.WriteLine($"code: {code}");

            bool found = false;
            for (int j = 0; j < 16; j++) {
                if (code == Codabar[j]) {
                    data[i] = CodabarVal[j];
                    found = true;
                    break;
                }
            }
            if (!found) error = true;
        }

        if (error) {
            Log.Error(func, "error in decoding");
            return null;
        }
        return new string(data);
    }

    /*------------------------------------------------------------------------*
     *                               Code UPC-A                               *
     *------------------------------------------------------------------------*/
    // leptonica: barcodeDecodeUpca (bardecode.c)
    private static string? DecodeUpca(string barstr, int debugflag)
    {
        const string func = "barcodeDecodeUpca";
        VerifyFormat(barstr, L.L_BF_UPCA, out bool valid, out _);
        if (!valid) {
            Log.Error(func, "barstr not in UPC-A format");
            return null;
        }

        if (barstr.Length != 59) {
            Log.Error(func, "size not 59; invalid UPC-A barcode");
            return null;
        }

        string firstCode = barstr.Substring(3, 4);
        bool foundFirst = false;
        for (int i = 0; i < 10; i++) {
            if (firstCode == Upca[i]) {
                foundFirst = true;
                break;
            }
        }
        string vbarstr = foundFirst ? barstr : ReverseString(barstr);

        char[] data = new char[12];
        bool error = false;
        for (int i = 0; i < 12; i++) {
            int start = i < 6 ? 3 + 4 * i : 32 + 4 * (i - 6);
            string code = vbarstr.Substring(start, 4);
            if (debugflag != 0)
                Console.Error.WriteLine($"code: {code}");

            bool found = false;
            for (int j = 0; j < 10; j++) {
                if (code == Upca[j]) {
                    data[i] = (char)('0' + j);
                    found = true;
                    break;
                }
            }
            if (!found) error = true;
        }

        if (error) {
            Log.Error(func, "error in decoding");
            return null;
        }

        int sum = 0;
        for (int i = 0; i < 12; i += 2)
            sum += 3 * (data[i] - '0');
        for (int i = 1; i < 11; i += 2)
            sum += data[i] - '0';
        int checkdigit = sum % 10;
        if (checkdigit != 0)
            checkdigit = 10 - checkdigit;
        if (checkdigit + '0' != data[11])
            Log.Warning(func, "Error for UPC-A check character");

        return new string(data);
    }

    /*------------------------------------------------------------------------*
     *                               Code EAN-13                              *
     *------------------------------------------------------------------------*/
    // leptonica: barcodeDecodeEan13 (bardecode.c)
    private static string? DecodeEan13(string barstr, int first, int debugflag)
    {
        const string func = "barcodeDecodeEan13";
        VerifyFormat(barstr, L.L_BF_UPCA, out bool valid, out _);
        if (!valid) {
            Log.Error(func, "barstr not in EAN 13 format");
            return null;
        }

        if (barstr.Length != 59) {
            Log.Error(func, "size not 59; invalid EAN 13 barcode");
            return null;
        }

        string firstCode = barstr.Substring(3, 4);
        bool foundFirst = false;
        for (int i = 0; i < 10; i++) {
            if (firstCode == Upca[i]) {
                foundFirst = true;
                break;
            }
        }
        string vbarstr = foundFirst ? barstr : ReverseString(barstr);

        char[] data = new char[12];
        bool error = false;
        for (int i = 0; i < 12; i++) {
            int start = i < 6 ? 3 + 4 * i : 32 + 4 * (i - 6);
            string code = vbarstr.Substring(start, 4);
            if (debugflag != 0)
                Console.Error.WriteLine($"code: {code}");

            bool found = false;
            for (int j = 0; j < 10; j++) {
                if (code == Upca[j]) {
                    data[i] = (char)('0' + j);
                    found = true;
                    break;
                }
            }
            if (!found) error = true;
        }

        if (error) {
            Log.Error(func, "error in decoding");
            return null;
        }

        int sum = 0;
        for (int i = 0; i < 12; i += 2)
            sum += 3 * (data[i] - '0');
        for (int i = 1; i < 12; i += 2)
            sum += data[i] - '0';
        int checkdigit = sum % 10;
        if (checkdigit != 0)
            checkdigit = 10 - checkdigit;
        if (checkdigit + '0' != data[11])
            Log.Warning(func, "Error for EAN-13 check character");

        return new string(data);
    }

    private static bool StrncmpEq(string s, int offset, string pattern, int count)
    {
        if (offset < 0 || offset + count > s.Length || count > pattern.Length)
            return false;
        return string.CompareOrdinal(s, offset, pattern, 0, count) == 0;
    }

    private static string ReverseString(string s)
    {
        char[] arr = s.ToCharArray();
        Array.Reverse(arr);
        return new string(arr);
    }
}
