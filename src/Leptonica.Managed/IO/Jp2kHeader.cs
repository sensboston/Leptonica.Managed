using System;
using System.Buffers.Binary;
using System.IO;

namespace Leptonica;

/*
 *  jp2kheader.c
 *
 *      Read header
 *          l_int32          readHeaderJp2k()
 *          l_int32          freadHeaderJp2k()
 *          l_int32          readHeaderMemJp2k()
 *          l_int32          fgetJp2kResolution()
 *          l_int32          readResolutionMemJp2k()
 *
 *  Note: these functions read image metadata from a jp2k file, without
 *  using any jp2k libraries.
 */
public sealed partial class Pix
{
    /* a sanity check on the size read from file */
    private const int MAX_JP2K_WIDTH = 100000;
    private const int MAX_JP2K_HEIGHT = 100000;

    private static readonly byte[] Jp2kIhdrTag = { 0x69, 0x68, 0x64, 0x72 };  /* 'ihdr' */
    private static readonly byte[] Jp2kRescTag = { 0x72, 0x65, 0x73, 0x63 };  /* 'resc' */
    private static readonly byte[] Jp2kCodestreamMagic = { 0xff, 0x4f, 0xff, 0x51 };

    /*--------------------------------------------------------------------*
     *                          Stream interface                          *
     *--------------------------------------------------------------------*/
    // leptonica: readHeaderJp2k (jp2kheader.c)
    /// <summary>Reads the header information of a JPEG 2000 file.</summary>
    /// <param name="filename">Path of the file to read.</param>
    /// <param name="pw">Receives the image width.</param>
    /// <param name="ph">Receives the image height.</param>
    /// <param name="pbps">Receives the number of bits per sample.</param>
    /// <param name="pspp">Receives the number of samples per pixel.</param>
    /// <param name="pcodec">Receives the codec type, <see cref="L.L_JP2_CODEC"/> or <see cref="L.L_J2K_CODEC"/>.</param>
    /// <exception cref="System.ArgumentException"><paramref name="filename"/> is <see langword="null"/>.</exception>
    public static void ReadHeaderJp2k(string filename, out int pw, out int ph,
                                      out int pbps, out int pspp, out int pcodec)
    {
        const string func = "readHeaderJp2k";
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");

        using FileStream fp = Utils.FopenReadStream(filename);
        FreadHeaderJp2k(fp, out pw, out ph, out pbps, out pspp, out pcodec);
    }

    // leptonica: freadHeaderJp2k (jp2kheader.c)
    /// <summary>Reads the header information of a JPEG 2000 image from a stream.</summary>
    /// <param name="fp">The stream to read; it is rewound to the beginning before reading.</param>
    /// <param name="pw">Receives the image width.</param>
    /// <param name="ph">Receives the image height.</param>
    /// <param name="pbps">Receives the number of bits per sample.</param>
    /// <param name="pspp">Receives the number of samples per pixel.</param>
    /// <param name="pcodec">Receives the codec type, <see cref="L.L_JP2_CODEC"/> or <see cref="L.L_J2K_CODEC"/>.</param>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> is <see langword="null"/>.</exception>
    public static void FreadHeaderJp2k(Stream fp, out int pw, out int ph,
                                       out int pbps, out int pspp, out int pcodec)
    {
        const string func = "freadHeaderJp2k";
        if (fp == null)
            throw Lept.Arg(func, "fp not defined");

        Span<byte> buf = stackalloc byte[120];  /* usually just need the first 80 bytes */
        fp.Position = 0;
        int nread = fp.ReadAtLeast(buf, buf.Length, false);
        if (nread != buf.Length)
            throw Lept.Fail(func, "read failure");

        try {
            ReadHeaderMemJp2k(buf, out pw, out ph, out pbps, out pspp, out pcodec);
        } finally {
            fp.Position = 0;
        }
    }

    // leptonica: readHeaderMemJp2k (jp2kheader.c)
    /// <remarks>
    /// (1) With an image file codec (L_JP2_CODEC), the Image Header Box
    ///     begins with 'ihdr' = 0x69686472 in big-endian order.
    /// (2) With a codestream codec (L_J2K_CODEC), the first 4 bytes are
    ///     0xff4fff51.
    /// </remarks>
    public static void ReadHeaderMemJp2k(ReadOnlySpan<byte> data, out int pw, out int ph,
                                         out int pbps, out int pspp, out int pcodec)
    {
        const string func = "readHeaderMemJp2k";
        pw = 0;
        ph = 0;
        pbps = 0;
        pspp = 0;
        pcodec = 0;

        if (data.Length < 120)
            throw Lept.Arg(func, "size < 80");
        FindFileFormatBuffer(data, out int format);
        if (format != L.IFF_JP2)
            throw Lept.Fail(func, "not jp2 file");

            /* Find beginning of the image metadata */
        int index;
        int codec;
        if (data.Slice(0, 4).SequenceEqual(Jp2kCodestreamMagic)) {  /* codestream */
            index = 8;
            codec = L.L_J2K_CODEC;
        } else {  /* file data with image header box 'ihdr' */
            bool found = Utils.ArrayFindSequence(data, Jp2kIhdrTag, out int loc);
            if (!found)
                throw Lept.Fail(func, "image parameters not found");
            index = loc + 4;
            codec = L.L_JP2_CODEC;
        }
        pcodec = codec;

        int w, h, spp, bps;
        if (codec == L.L_JP2_CODEC) {
            if (data.Length < index + 4 * 3)
                throw Lept.Fail(func, "header size is too small");
            h = BinaryPrimitives.ReadInt32BigEndian(data.Slice(index, 4));
            w = BinaryPrimitives.ReadInt32BigEndian(data.Slice(index + 4, 4));
            spp = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(index + 8, 2));
            bps = data[index + 10] + 1;
        } else {  /* codec == L_J2K_CODEC */
            if (data.Length < index + 4 * 9)
                throw Lept.Fail(func, "header size is too small");
            w = BinaryPrimitives.ReadInt32BigEndian(data.Slice(index, 4));
            h = BinaryPrimitives.ReadInt32BigEndian(data.Slice(index + 4, 4));
            spp = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(index + 32, 2));
            bps = data[index + 34] + 1;
        }

        if (w < 1 || h < 1)
            throw Lept.Fail(func, "w and h must both be > 0");
        if (w > MAX_JP2K_WIDTH || h > MAX_JP2K_HEIGHT)
            throw Lept.Fail(func, "unrealistically large sizes");
        if (spp != 1 && spp != 3 && spp != 4)
            throw Lept.Fail(func, "spp must be in 1, 3 or 4");
        if (bps != 8 && bps != 16)
            throw Lept.Fail(func, "bps must be 8 or 16");

        pw = w;
        ph = h;
        pspp = spp;
        pbps = bps;
    }

    // leptonica: fgetJp2kResolution (jp2kheader.c)
    /// <returns>true if found, false if not found</returns>
    public static bool FgetJp2kResolution(Stream fp, out int pxres, out int pyres)
    {
        const string func = "fgetJp2kResolution";
        if (fp == null)
            throw Lept.Arg(func, "stream not opened");

        fp.Position = 0;
        byte[] data = Utils.BinaryReadStream(fp);
        fp.Position = 0;

        return ReadResolutionMemJp2k(data, out pxres, out pyres);
    }

    // leptonica: readResolutionMemJp2k (jp2kheader.c)
    /// <returns>true if found, false if not found or invalid</returns>
    public static bool ReadResolutionMemJp2k(ReadOnlySpan<byte> data, out int pxres, out int pyres)
    {
        const string func = "readResolutionMemJp2k";
        pxres = 0;
        pyres = 0;

            /* Search for the start of the first capture resolution box: 'resc' */
        bool found = Utils.ArrayFindSequence(data, Jp2kRescTag, out int loc);
        if (!found) {
            Log.Warning(func, "image resolution not found");
            return false;
        }
        if (data.Length < 80 || loc >= data.Length - 13) {
            Log.Warning(func, "image resolution found without enough space");
            return false;
        }

            /* Extract the fields and calculate the resolution in pixels/meter.
             * See section 1.5.3.7.1 of JPEG 2000 ISO/IEC 15444-1 spec.  */
        ushort ynum = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(loc + 4, 2));
        ushort ydenom = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(loc + 6, 2));
        ushort xnum = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(loc + 8, 2));
        ushort xdenom = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(loc + 10, 2));
        if (ydenom == 0 || xdenom == 0) {
            Log.Warning(func, "bad data: ydenom or xdenom is 0");
            return false;
        }
        byte yexp = data[loc + 12];
        byte xexp = data[loc + 13];
        double yres = ((double)ynum / (double)ydenom) * Math.Pow(10.0, (double)yexp);
        double xres = ((double)xnum / (double)xdenom) * Math.Pow(10.0, (double)xexp);

            /* Convert from pixels/meter to ppi */
        yres *= (300.0 / 11811.0);
        xres *= (300.0 / 11811.0);

            /* Sanity check for bad data */
        double maxres = 100000.0;  /* ppi */
        if (xres > maxres || yres > maxres) {
            Log.Warning(func, "ridiculously large resolution");
        } else {
            pyres = (int)(yres + 0.5);
            pxres = (int)(xres + 0.5);
        }

        return true;
    }
}
