using System;
using System.Text;

namespace Leptonica;

/*
 *  encoding.c: Base64 and Ascii85 encoding and decoding.
 *
 *    Base64
 *        LeptEncoding.EncodeBase64 (encodeBase64)
 *        LeptEncoding.DecodeBase64 (decodeBase64)
 *
 *    Ascii85
 *        LeptEncoding.EncodeAscii85 (encodeAscii85)
 *        LeptEncoding.DecodeAscii85 (decodeAscii85)
 *        static ConvertChunkToAscii85 (convertChunkToAscii85)
 *
 *        LeptEncoding.EncodeAscii85WithComp (encodeAscii85WithComp)
 *        LeptEncoding.DecodeAscii85WithComp (decodeAscii85WithComp)
 *
 *    String reformatting for base 64 encoded data
 *        LeptEncoding.ReformatPacked64 (reformatPacked64)
 *
 *  Base64 encoding is useful for encding binary data in a restricted set of
 *  64 printable ascii symbols, that includes the 62 alphanumerics and '+'
 *  and '/'.  Notably it does not include quotes, so that base64 encoded
 *  strings can be used in situations where quotes are used for formatting.
 *  64 symbols was chosen because it is the smallest number that can be used
 *  in 4-for-3 byte encoding of binary data:
 *         log2(64) / log2(256) = 0.75 = 3/4
 *
 *  Ascii85 encoding is used in PostScript and some pdf files for
 *  representing binary data (for example, a compressed image) in printable
 *  ascii symbols.  It has a dictionary of 85 symbols; 85 was chosen because
 *  it is the smallest number that can be used in 5-for-4 byte encoding
 *  of binary data (256 possible input values).  This can be seen from
 *  the max information content in such a sequence:
 *         log2(84) / log2(256) = 0.799 < 4/5
 *         log2(85) / log2(256) = 0.801 > 4/5
 *
 *  The encoded data are strings: the char arrays of the C functions are
 *  ASCII, so a string holds them with outsize == Length.
 */
/// <summary>Encoders and decoders for the ascii85 and base64 text representations of binary data.</summary>
public static class LeptEncoding
{
    /* Base64 encoding table in string representation */
    private const int MAX_BASE64_LINE = 72;  /* max line length base64 */
    private const string tablechar64 =
                 "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
                 "abcdefghijklmnopqrstuvwxyz" +
                 "0123456789+/";

    /* Ascii85 encoding */
    private const int MAX_ASCII85_LINE = 64;  /* max line length ascii85 */
    private static readonly uint[] power85 = { 1,
                                               85,
                                               85 * 85,
                                               85 * 85 * 85,
                                               85 * 85 * 85 * 85 };

    /*-------------------------------------------------------------*
     *      Utility for encoding and decoding data with base64     *
     *-------------------------------------------------------------*/
    // leptonica: encodeBase64 (encoding.c)
    /// <summary>Encodes binary data in base64.</summary>
    /// <param name="inarray">input binary data</param>
    /// <returns>chara with MAX_BASE64_LINE characters + \n in each line</returns>
    /// <remarks>
    /// (1) The input character data is unrestricted binary.
    ///     The output encoded data consists of the 64 characters
    ///     in the base64 set, plus newlines and the pad character '='.
    /// </remarks>
    public static string EncodeBase64(ReadOnlySpan<byte> inarray)
    {
        int insize = inarray.Length;
        if (insize <= 0)
            throw Lept.Arg("encodeBase64", "insize not > 0");

        var chara = new StringBuilder();
        var array3 = new byte[3];
        var array4 = new byte[4];

            /* Read all the input data, and convert in sets of 3 input
             * bytes --> 4 output bytes. */
        int i = 0, linecount = 0, pos = 0;
        while (insize-- > 0) {
            if (linecount == MAX_BASE64_LINE) {
                chara.Append('\n');
                linecount = 0;
            }
            array3[i++] = inarray[pos++];
            if (i == 3) {  /* convert 3 to 4 and save */
                ByteConvert3to4(array3, array4);
                for (int j = 0; j < 4; j++)
                    chara.Append(tablechar64[array4[j]]);
                i = 0;
                linecount += 4;
            }
        }

            /* Suppose 1 or 2 bytes has been read but not yet processed.
             * If 1 byte has been read, this will generate 2 bytes of
             * output, with 6 bits to the first byte and 2 bits to the second.
             * We will add two bytes of '=' for padding.
             * If 2 bytes has been read, this will generate 3 bytes of output,
             * with 6 bits to the first 2 bytes and 4 bits to the third, and
             * we add a fourth padding byte ('='). */
        if (i > 0) {  /* left-over 1 or 2 input bytes */
            for (int j = i; j < 3; j++)
                array3[j] = 0;  /* zero the remaining input bytes */
            ByteConvert3to4(array3, array4);
            for (int j = 0; j <= i; j++)
                chara.Append(tablechar64[array4[j]]);
            for (int j = i + 1; j < 4; j++)
                chara.Append('=');
        }

        return chara.ToString();
    }

    // leptonica: decodeBase64 (encoding.c)
    /// <summary>Decodes base64 data.</summary>
    /// <param name="inarray">input encoded char data, with 72 chars/line)</param>
    /// <returns>decoded byte data</returns>
    /// <remarks>
    /// (1) The input character data should have only 66 different characters:
    ///     The 64 character set for base64 encoding, plus the pad
    ///     character '=' and newlines for formatting with fixed line
    ///     lengths.  If there are any other characters, the decoder
    ///     will declare the input data to be invalid and fail.
    /// (2) The decoder ignores newlines and, for a valid input string,
    ///     stops reading input when a pad byte is found.
    /// </remarks>
    public static byte[] DecodeBase64(string inarray)
    {
        const string func = "decodeBase64";
        int insize = inarray.Length;
        if (insize <= 0)
            throw Lept.Arg(func, "insize not > 0");

            /* Validate the input data */
        for (int i = 0; i < insize; i++) {
            char inchar = inarray[i];
            if (inchar == '\n') continue;
            if (!IsBase64(inchar) && inchar != '=')
                throw Lept.Arg(func, "invalid char in inarray");
        }

            /* The input array typically is made with a newline every
             * MAX_BASE64_LINE input bytes.  However, as a printed string, the
             * newlines would be stripped.  So when we allocate the output
             * array, assume the input array is all data, but strip
             * out the newlines during decoding.  This guarantees that
             * the allocated array is large enough. */
        int outsize = 3 * ((insize + 3) / 4) + 4;
        byte[] bytea = new byte[outsize];

            /* The number of encoded input data bytes is always a multiple of 4.
             * Read all the data, until you reach either the end or
             * the first pad character '='.  The data is processed in
             * units of 4 input bytes, generating 3 output decoded bytes
             * of binary data.  Newlines are ignored.  If there are no
             * pad bytes, i == 0 at the end of this section. */
        int[] rtable64 = GenReverseTab64();
        var array3 = new byte[3];
        var array4 = new byte[4];
        int ii = 0, out_index = 0;
        for (int in_index = 0; in_index < insize; in_index++) {
            char inchar = inarray[in_index];
            if (inchar == '\n') continue;
            if (inchar == '=') break;
            array4[ii++] = (byte)rtable64[inchar];
            if (ii < 4) {
                continue;
            } else {  /* i == 4; convert 4 to 3 and save */
                ByteConvert4to3(array4, array3);
                for (int j = 0; j < 3; j++)
                    bytea[out_index++] = array3[j];
                ii = 0;
            }
        }

            /* If i > 0, we ran into pad bytes ('=').  If i == 2, there are
             * two input pad bytes and one output data byte.  If i == 3,
             * there is one input pad byte and two output data bytes. */
        if (ii > 0) {
            for (int j = ii; j < 4; j++)
                array4[j] = 0;  /* zero the remaining input bytes */
            ByteConvert4to3(array4, array3);
            for (int j = 0; j < ii - 1; j++)
                bytea[out_index++] = array3[j];
        }

        Array.Resize(ref bytea, out_index);
        return bytea;
    }

    // leptonica: isBase64 (encoding.c)
    private static bool IsBase64(char c)
    {
        return (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')
               || c == '+' || c == '/';
    }

    // leptonica: genReverseTab64 (encoding.c)
    private static int[] GenReverseTab64()
    {
        int[] rtable64 = new int[128];
        for (int i = 0; i < 64; i++)
            rtable64[tablechar64[i]] = i;
        return rtable64;
    }

    // leptonica: byteConvert3to4 (encoding.c)
    private static void ByteConvert3to4(byte[] in3, byte[] out4)
    {
        out4[0] = (byte)(in3[0] >> 2);
        out4[1] = (byte)(((in3[0] & 0x03) << 4) | (in3[1] >> 4));
        out4[2] = (byte)(((in3[1] & 0x0f) << 2) | (in3[2] >> 6));
        out4[3] = (byte)(in3[2] & 0x3f);
    }

    // leptonica: byteConvert4to3 (encoding.c)
    private static void ByteConvert4to3(byte[] in4, byte[] out3)
    {
        out3[0] = (byte)((in4[0] << 2) | (in4[1] >> 4));
        out3[1] = (byte)(((in4[1] & 0x0f) << 4) | (in4[2] >> 2));
        out3[2] = (byte)(((in4[2] & 0x03) << 6) | in4[3]);
    }

    /*-------------------------------------------------------------*
     *      Utility for encoding and decoding data with ascii85    *
     *-------------------------------------------------------------*/
    // leptonica: encodeAscii85 (encoding.c)
    /// <summary>Encodes binary data in ascii85.</summary>
    /// <param name="inarray">input data</param>
    /// <returns>chara with 64 characters + \n in each line</returns>
    /// <remarks>
    /// (1) Ghostscript has a stack break if the last line of
    ///     data only has a '>', so we avoid the problem by
    ///     always putting '~>' on the last line.
    /// </remarks>
    public static string EncodeAscii85(ReadOnlySpan<byte> inarray)
    {
        if (inarray.Length <= 0)
            throw Lept.Arg("encodeAscii85", "insize not > 0");

            /* Accumulate results in char array */
        var chara = new StringBuilder();
        var outbuf = new char[8];
        int linecount = 0;
        int index = 0;
        while (true) {
            bool eof = ConvertChunkToAscii85(inarray, ref index, outbuf, out int nbout);
            for (int i = 0; i < nbout; i++) {
                chara.Append(outbuf[i]);
                linecount++;
                if (linecount >= MAX_ASCII85_LINE) {
                    chara.Append('\n');
                    linecount = 0;
                }
            }
            if (eof) {
                if (linecount != 0)
                    chara.Append('\n');
                chara.Append('~');
                chara.Append('>');
                chara.Append('\n');
                break;
            }
        }

        return chara.ToString();
    }

    // leptonica: convertChunkToAscii85 (encoding.c)
    /// <summary>Converts up to 4 input bytes to ascii85.</summary>
    /// <param name="inarray">The input bytes.</param>
    /// <param name="pindex">The read position in <paramref name="inarray"/>; advanced past the bytes consumed.</param>
    /// <param name="outbuf">Output buffer that holds 8 ascii chars; no more than 7 are used.</param>
    /// <param name="nbout">Receives the number of chars written to <paramref name="outbuf"/>.</param>
    /// <returns>true at the end of the data; otherwise false.</returns>
    /// <remarks>
    /// Attempts to read 4 bytes and write 5. Writes 1 char if the value is 0.
    /// </remarks>
    private static bool ConvertChunkToAscii85(ReadOnlySpan<byte> inarray, ref int pindex,
                                              char[] outbuf, out int nbout)
    {
        bool eof = false;
        int insize = inarray.Length;
        int index = pindex;
        int nread = Math.Min(4, insize - index);
        if (insize == index + nread)
            eof = true;
        pindex += nread;  /* save new index */

            /* Read input data and save in l_uint32 */
        uint inword = 0;
        for (int i = 0; i < nread; i++) {
            byte inbyte = inarray[index + i];
            inword += (uint)inbyte << (8 * (3 - i));
        }

            /* Special case: output 1 byte only */
        if (inword == 0) {
            outbuf[0] = 'z';
            nbout = 1;
        } else { /* output nread + 1 bytes */
            for (int i = 4; i >= 4 - nread; i--) {
                uint val = inword / power85[i];
                outbuf[4 - i] = (char)(byte)(val + '!');
                inword -= val * power85[i];
            }
            nbout = nread + 1;
        }

        return eof;
    }

    // leptonica: decodeAscii85 (encoding.c)
    /// <summary>Decodes ascii85 data.</summary>
    /// <param name="inarray">ascii85 input data</param>
    /// <returns>outarray binary</returns>
    /// <remarks>
    /// (1) We assume the data is properly encoded, so we do not check
    ///     for invalid characters or the final '>' character.
    /// (2) We permit whitespace to be added to the encoding in an
    ///     arbitrary way.
    /// </remarks>
    public static byte[] DecodeAscii85(string inarray)
    {
        const string func = "decodeAscii85";
        int insize = inarray.Length;
        if (insize <= 0)
            throw Lept.Arg(func, "insize not > 0");

            /* Accumulate results in outa */
        int maxsize = (int)(80.0 + (insize * 4.0 / 5.0));  /* plenty big */
        byte[] outa = new byte[maxsize];

        int ocount = 0;  /* byte index into outa */
        uint oword = 0;
        int bytecount = 0;
        for (int index = 0; index < insize; index++) {
            char inc = inarray[index];

            if (inc == ' ' || inc == '\t' || inc == '\n' ||
                inc == '\f' || inc == '\r' || inc == '\v')  /* ignore white space */
                continue;

            byte val = (byte)(inc - '!');
            if (val < 85) {
                oword = oword * 85 + val;
                if (bytecount < 4) {
                    bytecount++;
                } else {  /* we have all 5 input chars for the oword */
                    outa[ocount] = (byte)((oword >> 24) & 0xff);
                    outa[ocount + 1] = (byte)((oword >> 16) & 0xff);
                    outa[ocount + 2] = (byte)((oword >> 8) & 0xff);
                    outa[ocount + 3] = (byte)(oword & 0xff);
                    ocount += 4;
                    bytecount = 0;
                    oword = 0;
                }
            } else if (inc == 'z' && bytecount == 0) {
                outa[ocount] = 0;
                outa[ocount + 1] = 0;
                outa[ocount + 2] = 0;
                outa[ocount + 3] = 0;
                ocount += 4;
            } else if (inc == '~') {  /* end of data */
                Log.Info(func, CFormat.Sprintf(" %d extra bytes output", bytecount - 1));
                switch (bytecount) {
                case 0:   /* normal eof */
                case 1:   /* error */
                    break;
                case 2:   /* 1 extra byte */
                    oword = oword * power85[3] + 0xffffff;
                    outa[ocount] = (byte)((oword >> 24) & 0xff);
                    break;
                case 3:   /* 2 extra bytes */
                    oword = oword * power85[2] + 0xffff;
                    outa[ocount] = (byte)((oword >> 24) & 0xff);
                    outa[ocount + 1] = (byte)((oword >> 16) & 0xff);
                    break;
                case 4:   /* 3 extra bytes */
                    oword = oword * 85 + 0xff;
                    outa[ocount] = (byte)((oword >> 24) & 0xff);
                    outa[ocount + 1] = (byte)((oword >> 16) & 0xff);
                    outa[ocount + 2] = (byte)((oword >> 8) & 0xff);
                    break;
                }
                if (bytecount > 1)
                    ocount += (bytecount - 1);
                break;
            }
        }

        Array.Resize(ref outa, ocount);
        return outa;
    }

    // leptonica: encodeAscii85WithComp (encoding.c)
    /// <summary>Compresses the data with zlib, then encodes it in ascii85.</summary>
    /// <param name="indata">input binary data</param>
    /// <returns>outstr with 64 characters + \n in each line</returns>
    /// <remarks>
    /// (1) Compress the input data; then encode ascii85.  For ascii
    ///     input, a first compression step will significantly reduce
    ///     the final encoded output size.
    /// </remarks>
    public static string EncodeAscii85WithComp(ReadOnlySpan<byte> indata)
    {
        byte[] data1 = ZlibMem.ZlibCompress(indata);
        return EncodeAscii85(data1);
    }

    // leptonica: decodeAscii85WithComp (encoding.c)
    /// <summary>Decodes ascii85 data, then decompresses it with zlib.</summary>
    /// <param name="instr">ascii85 input data string</param>
    /// <param name="insize">number of characters of the string to use; 0 for all</param>
    /// <returns>binary data before compression and ascii85 encoding</returns>
    /// <remarks>
    /// (1) We assume the input data has been zlib compressed and then
    ///     properly encoded, so we reverse the procedure.  This is the
    ///     inverse of encodeAscii85WithComp().
    /// (2) Set %insize == 0 to use strlen(%instr).
    /// </remarks>
    public static byte[] DecodeAscii85WithComp(string instr, int insize = 0)
    {
        if (insize == 0) insize = instr.Length;
        byte[] data1 = DecodeAscii85(instr.Substring(0, insize));
        return ZlibMem.ZlibUncompress(data1);
    }

    /*-------------------------------------------------------------*
     *       String reformatting for base 64 encoded data          *
     *-------------------------------------------------------------*/
    // leptonica: reformatPacked64 (encoding.c)
    /// <summary>Reformats a base64 string into lines with a lead space and optional quotes.</summary>
    /// <param name="inarray">base64 encoded string with newlines</param>
    /// <param name="leadspace">number of spaces in each line before the data</param>
    /// <param name="linechars">number of bytes of data in each line; multiple of 4</param>
    /// <param name="addquotes">true to add quotes to each line of data</param>
    /// <returns>outarray ascii</returns>
    /// <remarks>
    /// (1) Each line in the output array has %leadspace space characters,
    ///     followed optionally by a double-quote, followed by %linechars
    ///     bytes of base64 data, followed optionally by a double-quote,
    ///     followed by a newline.
    /// (2) This can be used to convert a base64 encoded string to a
    ///     string formatted for inclusion in a C source file.
    /// </remarks>
    public static string ReformatPacked64(string inarray, int leadspace, int linechars,
                                          bool addquotes)
    {
        const string func = "reformatPacked64";
        int insize = inarray.Length;
        if (insize <= 0)
            throw Lept.Arg(func, "insize not > 0");
        if (leadspace < 0)
            throw Lept.Arg(func, "leadspace must be >= 0");
        if (linechars % 4 != 0)
            throw Lept.Arg(func, "linechars % 4 must be 0");
        if (linechars <= 0)
            throw Lept.Arg(func, "linechars must be > 0");

            /* Remove all white space */
        var flata = new StringBuilder();
        for (int i = 0; i < insize; i++) {
            if (IsBase64(inarray[i]) || inarray[i] == '=')
                flata.Append(inarray[i]);
        }

            /* Generate output string */
        int flatsize = flata.Length;
        var outa = new StringBuilder();
        outa.Append(' ', leadspace);
        if (addquotes) outa.Append('"');
        int linecount = 0;
        for (int i = 0; i < flatsize; i++) {
            if (linecount == linechars) {
                if (addquotes) outa.Append('"');
                outa.Append('\n');
                outa.Append(' ', leadspace);
                if (addquotes) outa.Append('"');
                linecount = 0;
            }
            outa.Append(flata[i]);
            linecount++;
        }
        if (addquotes) outa.Append('"');

        return outa.ToString();
    }
}
