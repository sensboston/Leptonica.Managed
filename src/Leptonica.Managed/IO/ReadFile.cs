using System;
using System.IO;

namespace Leptonica;

/*
 *  readfile.c: Top-level image reading and file format detection.
 *
 *      Top-level functions for reading images from file
 *           PIXA      *pixaReadFiles()
 *           PIXA      *pixaReadFilesSA()
 *           PIX       *pixRead()
 *           PIX       *pixReadWithHint()
 *           PIX       *pixReadIndexed()
 *           PIX       *pixReadStream()
 *
 *      Read header information from file
 *           l_int32    pixReadHeader()
 *
 *      Format finders
 *           l_int32    findFileFormat()
 *           l_int32    findFileFormatStream()
 *           l_int32    findFileFormatBuffer()
 *           l_int32    fileFormatIsTiff()
 *
 *      Read from memory
 *           PIX       *pixReadMem()
 *           l_int32    pixReadHeaderMem()
 *
 *      Output image file information
 *           l_ok       writeImageFileInfo()
 *
 *      Test function for I/O with different formats
 *           l_int32    ioFormatTest()
 */
public sealed partial class Pix
{
    private const ushort BMP_ID = 0x4d42;              /* 'BM' */
    private const ushort TIFF_BIGEND_ID = 0x4d4d;      /* MM - for 'motorola' */
    private const ushort TIFF_LITTLEEND_ID = 0x4949;   /* II - for 'intel'    */
    private static readonly byte[] JP2K_CODESTREAM = { 0xff, 0x4f, 0xff, 0x51 };
    private static readonly byte[] JP2K_IMAGE_DATA = { 0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50,
                                                       0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0A };

    private const string FILE_BMP = "/tmp/lept/format/file.bmp";
    private const string FILE_PNG = "/tmp/lept/format/file.png";
    private const string FILE_PNM = "/tmp/lept/format/file.pnm";
    private const string FILE_G3 = "/tmp/lept/format/file_g3.tif";
    private const string FILE_G4 = "/tmp/lept/format/file_g4.tif";
    private const string FILE_RLE = "/tmp/lept/format/file_rle.tif";
    private const string FILE_PB = "/tmp/lept/format/file_packbits.tif";
    private const string FILE_LZW = "/tmp/lept/format/file_lzw.tif";
    private const string FILE_ZIP = "/tmp/lept/format/file_zip.tif";
    private const string FILE_TIFF_JPEG = "/tmp/lept/format/file_jpeg.tif";
    private const string FILE_TIFF = "/tmp/lept/format/file.tif";
    private const string FILE_JPG = "/tmp/lept/format/file.jpg";
    private const string FILE_GIF = "/tmp/lept/format/file.gif";
    private const string FILE_WEBP = "/tmp/lept/format/file.webp";
    private const string FILE_JP2K = "/tmp/lept/format/file.jp2";

    /*---------------------------------------------------------------------*
     *          Top-level functions for reading images from file           *
     *---------------------------------------------------------------------*/
    // leptonica: pixRead (readfile.c)
    /// <summary>Reads an image file of any supported format.</summary>
    public static Pix Read(string filename)
    {
        const string func = "pixRead";
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");

        using FileStream fp = Utils.FopenReadStream(filename);
        return ReadStream(fp, 0);
    }

    // leptonica: pixReadWithHint (readfile.c)
    /// <param name="filename">with full pathname or in local directory</param>
    /// <param name="hint">bitwise OR of L_HINT_* values for jpeg; use 0 for no hint</param>
    public static Pix ReadWithHint(string filename, int hint)
    {
        const string func = "pixReadWithHint";
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");

        using FileStream fp = Utils.FopenReadStream(filename);
        return ReadStream(fp, hint);
    }

    // leptonica: pixReadIndexed (readfile.c)
    /// <param name="sa">string array of full pathnames</param>
    /// <param name="index">into pathname array</param>
    /// <returns>pix if OK; null if empty pathname or not readable</returns>
    public static Pix? ReadIndexed(Sarray sa, int index)
    {
        const string func = "pixReadIndexed";
        if (sa == null)
            throw Lept.Arg(func, "sa not defined");
        int n = sa.Count;
        if (index < 0 || index >= n)
            throw Lept.Arg(func, "index out of bounds");

        string fname = sa.GetString(index, L.L_NOCOPY);
        if (fname.Length == 0)
            return null;

        try {
            return Read(fname);
        } catch (LeptonicaException) {
            Log.Error(func, $"pix not read from file {fname}");
            return null;
        }
    }

    // leptonica: pixReadStream (readfile.c)
    /// <param name="fp">file stream</param>
    /// <param name="hint">bitwise OR of L_HINT_* values for jpeg; use 0 for no hint</param>
    public static Pix ReadStream(Stream fp, int hint)
    {
        const string func = "pixReadStream";
        if (fp == null)
            throw Lept.Arg(func, "stream not defined");

        Pix pix;
        FindFileFormatStream(fp, out int format);
        switch (format)
        {
        case L.IFF_BMP:
            pix = ReadStreamBmp(fp);
            break;
        case L.IFF_JFIF_JPEG:
            pix = ReadStreamJpeg(fp, 0, 1, out _, hint);
            FgetJpegComment(fp, out byte[]? comment);
            if (comment != null)
                pix.Text = System.Text.Encoding.Latin1.GetString(comment);
            break;
        case L.IFF_PNG:
            pix = ReadStreamPng(fp);
            break;
        case L.IFF_TIFF:
        case L.IFF_TIFF_PACKBITS:
        case L.IFF_TIFF_RLE:
        case L.IFF_TIFF_G3:
        case L.IFF_TIFF_G4:
        case L.IFF_TIFF_LZW:
        case L.IFF_TIFF_ZIP:
        case L.IFF_TIFF_JPEG:
            pix = ReadStreamTiff(fp, 0) ?? throw Lept.Fail(func, "tiff: no pix returned");
            break;
        case L.IFF_PNM:
            pix = ReadStreamPnm(fp);
            break;
        case L.IFF_GIF:
            pix = ReadStreamGif(fp);
            break;
        case L.IFF_JP2:
            pix = ReadStreamJp2k(fp, 1, null, 0, 0);
            break;
        case L.IFF_WEBP:
            pix = ReadStreamWebP(fp);
            break;
        case L.IFF_PS:
            Log.Error(func, "PostScript reading is not supported");
            throw Lept.Fail(func, "PostScript reading is not supported");
        case L.IFF_LPDF:
            Log.Error(func, "Pdf reading is not supported");
            throw Lept.Fail(func, "Pdf reading is not supported");
        case L.IFF_SPIX:
            pix = ReadStreamSpix(fp);
            break;
        default:  /* IFF_UNKNOWN */
            throw Lept.Fail(func, "Unknown format: no pix returned");
        }

        pix.InputFormat = format;
        PixColormap? cmap;
        if ((cmap = pix.Colormap) != null) {
            if (!cmap.IsValid(pix))
                throw Lept.Fail(func, "invalid colormap");
        }
        return pix;
    }

    /*---------------------------------------------------------------------*
     *                     Read header information from file               *
     *---------------------------------------------------------------------*/
    // leptonica: pixReadHeader (readfile.c)
    /// <summary>Reads the header information of an image file without decoding the pixels.</summary>
    /// <param name="filename">Path of the image file.</param>
    /// <param name="pformat">Receives the image format, as an <c>IFF_*</c> constant.</param>
    /// <param name="pw">Receives the image width.</param>
    /// <param name="ph">Receives the image height.</param>
    /// <param name="pbps">Receives the number of bits per sample.</param>
    /// <param name="pspp">Receives the number of samples per pixel.</param>
    /// <param name="piscmap">Receives <see langword="true"/> if the image has a colormap.</param>
    /// <exception cref="System.ArgumentException"><paramref name="filename"/> is <see langword="null"/>.</exception>
    /// <exception cref="LeptonicaException">The format is unknown or the header cannot be read.</exception>
    public static void ReadHeader(string filename, out int pformat, out int pw, out int ph,
                                  out int pbps, out int pspp, out bool piscmap)
    {
        const string func = "pixReadHeader";
        pw = 0;
        ph = 0;
        pbps = 0;
        pspp = 0;
        piscmap = false;
        pformat = 0;
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");

        int format;
        using (FileStream fp = Utils.FopenReadStream(filename))
            FindFileFormatStream(fp, out format);

        int w, h, d, bps, spp;
        bool iscmap = false;
        switch (format)
        {
        case L.IFF_BMP: {  /* cheating: reading the entire file */
            Pix pix = Read(filename);
            pix.GetDimensions(out w, out h, out d);
            bps = (d == 32) ? 8 : d;
            spp = pix.Spp;
            iscmap = pix.Colormap != null;
            break;
        }
        case L.IFF_JFIF_JPEG:
            ReadHeaderJpeg(filename, out w, out h, out spp, out _, out _);
            bps = 8;
            break;
        case L.IFF_PNG:
            ReadHeaderPng(filename, out w, out h, out bps, out spp, out iscmap);
            break;
        case L.IFF_TIFF:
        case L.IFF_TIFF_PACKBITS:
        case L.IFF_TIFF_RLE:
        case L.IFF_TIFF_G3:
        case L.IFF_TIFF_G4:
        case L.IFF_TIFF_LZW:
        case L.IFF_TIFF_ZIP:
        case L.IFF_TIFF_JPEG:
            ReadHeaderTiff(filename, 0, out w, out h, out bps, out spp, out _, out iscmap, out format);
            break;
        case L.IFF_PNM:
            ReadHeaderPnm(filename, out w, out h, out _, out _, out bps, out spp);
            break;
        case L.IFF_GIF: {
            Pix pix = Read(filename);
            pix.GetDimensions(out w, out h, out d);
            iscmap = true;
            spp = 1;
            bps = d;
            break;
        }
        case L.IFF_JP2:
            ReadHeaderJp2k(filename, out w, out h, out bps, out spp, out _);
            break;
        case L.IFF_WEBP:
            ReadHeaderWebP(filename, out w, out h, out spp);
            bps = 8;
            break;
        case L.IFF_PS:
            pformat = format;
            throw Lept.Fail(func, "PostScript reading is not supported");
        case L.IFF_LPDF:
            pformat = format;
            throw Lept.Fail(func, "Pdf reading is not supported");
        case L.IFF_SPIX:
            ReadHeaderSpix(filename, out w, out h, out bps, out spp, out iscmap);
            break;
        default:
            throw Lept.Fail(func, $"unknown format in file: {filename}");
        }

        pw = w;
        ph = h;
        pbps = bps;
        pspp = spp;
        piscmap = iscmap;
        pformat = format;
    }

    /*---------------------------------------------------------------------*
     *                            Format finders                           *
     *---------------------------------------------------------------------*/
    // leptonica: findFileFormat (readfile.c)
    /// <returns>true if the format is known; the format is IFF_UNKNOWN otherwise</returns>
    public static bool FindFileFormat(string filename, out int pformat)
    {
        if (filename == null)
            throw Lept.Arg("findFileFormat", "filename not defined");
        using FileStream fp = Utils.FopenReadStream(filename);
        return FindFileFormatStream(fp, out pformat);
    }

    // leptonica: findFileFormatStream (readfile.c)
    /// <returns>true if the format is known; the format is IFF_UNKNOWN otherwise</returns>
    /// <remarks>
    /// (1) Important: Side effect -- this resets fp to BOF.
    /// </remarks>
    public static bool FindFileFormatStream(Stream fp, out int pformat)
    {
        pformat = L.IFF_UNKNOWN;
        if (fp == null)
            throw Lept.Arg("findFileFormatStream", "stream not defined");
        fp.Position = 0;
        if (Utils.FnbytesInFile(fp) < 12)
            throw Lept.Fail("findFileFormatStream", "truncated file");
        byte[] firstbytes = new byte[13];
        if (fp.ReadAtLeast(firstbytes.AsSpan(0, 12), 12, false) != 12)
            throw Lept.Fail("findFileFormatStream", "failed to read first 12 bytes of file");
        firstbytes[12] = 0;
        fp.Position = 0;

        FindFileFormatBuffer(firstbytes, out int format);
        if (format == L.IFF_TIFF) {
            FindTiffCompression(fp, out format);
            fp.Position = 0;
        }
        pformat = format;
        return format != L.IFF_UNKNOWN;
    }

    // leptonica: findFileFormatBuffer (readfile.c)
    /// <param name="buf">The first bytes of the encoded data; at least 12 bytes are needed to identify a format.</param>
    /// <param name="pformat">Receives the image format, as an <c>IFF_*</c> constant.</param>
    /// <returns><see langword="true"/> if the format is known; otherwise <see langword="false"/>.</returns>
    public static bool FindFileFormatBuffer(ReadOnlySpan<byte> buf, out int pformat)
    {
        pformat = L.IFF_UNKNOWN;
        if (buf.Length < 12)
            return false;

            /* Check the bmp and tiff 2-byte header ids */
        ushort twobytepw = (ushort)(buf[0] | (buf[1] << 8));
        if (twobytepw == BMP_ID) {
            pformat = L.IFF_BMP;
            return true;
        }
        if (twobytepw == TIFF_BIGEND_ID || twobytepw == TIFF_LITTLEEND_ID) {
            pformat = L.IFF_TIFF;
            return true;
        }

            /* Check for the p*m 2-byte header ids */
        if ((buf[0] == 'P' && buf[1] == '4') || /* newer packed */
            (buf[0] == 'P' && buf[1] == '1')) {  /* old ASCII format */
            pformat = L.IFF_PNM;
            return true;
        }
        if ((buf[0] == 'P' && buf[1] == '5') || /* newer */
            (buf[0] == 'P' && buf[1] == '2')) {  /* old */
            pformat = L.IFF_PNM;
            return true;
        }
        if ((buf[0] == 'P' && buf[1] == '6') || /* newer */
            (buf[0] == 'P' && buf[1] == '3')) {  /* old */
            pformat = L.IFF_PNM;
            return true;
        }
        if (buf[0] == 'P' && buf[1] == '7') {  /* new arbitrary (PAM) */
            pformat = L.IFF_PNM;
            return true;
        }

            /*  Consider the first 11 bytes of the standard JFIF JPEG header:
             *    - The first two bytes are the most important:  0xffd8.
             *    - Because of variability in the rest of the header, we only
             *      check the first two byte marker.  All jpeg files are
             *      identified as IFF_JFIF_JPEG.  */
        if (buf[0] == 0xff && buf[1] == 0xd8) {
            pformat = L.IFF_JFIF_JPEG;
            return true;
        }

            /* Check for the 8 byte PNG signature (png_signature in png.c):
             *       {137, 80, 78, 71, 13, 10, 26, 10}      */
        if (buf[0] == 137 && buf[1] == 80  && buf[2] == 78  && buf[3] == 71  &&
            buf[4] == 13  && buf[5] == 10  && buf[6] == 26  && buf[7] == 10) {
            pformat = L.IFF_PNG;
            return true;
        }

            /* Look for "GIF87a" or "GIF89a" */
        if (buf[0] == 'G' && buf[1] == 'I' && buf[2] == 'F' && buf[3] == '8' &&
            (buf[4] == '7' || buf[4] == '9') && buf[5] == 'a') {
            pformat = L.IFF_GIF;
            return true;
        }

            /* Check for both types of jp2k file */
        if (buf.Slice(0, 4).SequenceEqual(JP2K_CODESTREAM) ||
            buf.Slice(0, 12).SequenceEqual(JP2K_IMAGE_DATA)) {
            pformat = L.IFF_JP2;
            return true;
        }

            /* Check for webp */
        if (buf[0] == 'R' && buf[1] == 'I' && buf[2] == 'F' && buf[3] == 'F' &&
            buf[8] == 'W' && buf[9] == 'E' && buf[10] == 'B' && buf[11] == 'P') {
            pformat = L.IFF_WEBP;
            return true;
        }

            /* Check for ps */
        if (buf[0] == '%' && buf[1] == '!' && buf[2] == 'P' && buf[3] == 'S' &&
            buf[4] == '-' && buf[5] == 'A' && buf[6] == 'd' && buf[7] == 'o' &&
            buf[8] == 'b' && buf[9] == 'e') {
            pformat = L.IFF_PS;
            return true;
        }

            /* Check for pdf */
        if (buf[0] == '%' && buf[1] == 'P' && buf[2] == 'D' && buf[3] == 'F' &&
            buf[4] == '-' && buf[5] == '1') {
            pformat = L.IFF_LPDF;
            return true;
        }

            /* Check for "spix" serialized pix */
        if (buf[0] == 's' && buf[1] == 'p' && buf[2] == 'i' && buf[3] == 'x') {
            pformat = L.IFF_SPIX;
            return true;
        }

            /* File format identifier not found; unknown */
        return false;
    }

    // leptonica: fileFormatIsTiff (readfile.c)
    /// <summary>Determines whether a stream contains a TIFF image, with any compression.</summary>
    /// <param name="fp">The stream to examine; it is rewound afterwards.</param>
    /// <returns><see langword="true"/> if the data is in TIFF format; otherwise <see langword="false"/>.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> is <see langword="null"/>.</exception>
    public static bool FileFormatIsTiff(Stream fp)
    {
        if (fp == null)
            throw Lept.Arg("fileFormatIsTiff", "stream not defined");

        FindFileFormatStream(fp, out int format);
        return format == L.IFF_TIFF || format == L.IFF_TIFF_PACKBITS ||
               format == L.IFF_TIFF_RLE || format == L.IFF_TIFF_G3 ||
               format == L.IFF_TIFF_G4 || format == L.IFF_TIFF_LZW ||
               format == L.IFF_TIFF_ZIP || format == L.IFF_TIFF_JPEG;
    }

    /*---------------------------------------------------------------------*
     *                            Read from memory                         *
     *---------------------------------------------------------------------*/
    // leptonica: pixReadMem (readfile.c)
    /// <summary>Reads an image of any supported format from memory.</summary>
    /// <param name="data">The encoded image data; at least 12 bytes are required.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="data"/> is shorter than 12 bytes.</exception>
    /// <exception cref="LeptonicaException">The format is unknown, cannot be read from memory, or the colormap is invalid.</exception>
    public static Pix ReadMem(ReadOnlySpan<byte> data)
    {
        const string func = "pixReadMem";
        if (data.Length < 12)
            throw Lept.Arg(func, "size < 12");

        Pix pix;
        FindFileFormatBuffer(data, out int format);
        switch (format)
        {
        case L.IFF_BMP:
            pix = ReadMemBmp(data);
            break;
        case L.IFF_JFIF_JPEG:
            pix = ReadMemJpeg(data, 0, 1, out _, 0);
            break;
        case L.IFF_PNG:
            pix = ReadMemPng(data.ToArray());
            break;
        case L.IFF_TIFF:
        case L.IFF_TIFF_PACKBITS:
        case L.IFF_TIFF_RLE:
        case L.IFF_TIFF_G3:
        case L.IFF_TIFF_G4:
        case L.IFF_TIFF_LZW:
        case L.IFF_TIFF_ZIP:
        case L.IFF_TIFF_JPEG:
            pix = ReadMemTiff(data, 0) ?? throw Lept.Fail(func, "tiff: no pix returned");
            break;
        case L.IFF_PNM:
            pix = ReadMemPnm(data);
            break;
        case L.IFF_GIF:
            pix = ReadMemGif(data);
            break;
        case L.IFF_JP2:
            pix = ReadMemJp2k(data, 1, null, 0, 0);
            break;
        case L.IFF_WEBP:
            pix = ReadMemWebP(data);
            break;
        case L.IFF_PS:
            Log.Error(func, "PostScript reading is not supported");
            throw Lept.Fail(func, "PostScript reading is not supported");
        case L.IFF_LPDF:
            Log.Error(func, "Pdf reading is not supported");
            throw Lept.Fail(func, "Pdf reading is not supported");
        case L.IFF_SPIX:
            pix = ReadMemSpix(data);
            break;
        default:
            throw Lept.Fail(func, "Unknown format: no pix returned");
        }

            /* Set the input format.  For tiff reading from memory we lose
             * the actual input format; for 1 bpp, default to G4.  Also
             * verify that the colormap is valid.  */
        if (format == L.IFF_TIFF && pix.Depth == 1)
            format = L.IFF_TIFF_G4;
        pix.InputFormat = format;
        PixColormap? cmap;
        if ((cmap = pix.Colormap) != null) {
            if (!cmap.IsValid(pix))
                throw Lept.Fail(func, "invalid colormap");
        }
        pix.SetPadBits(0);
        return pix;
    }

    // leptonica: pixReadHeaderMem (readfile.c)
    /// <summary>Reads the header information of an encoded image in memory.</summary>
    /// <param name="data">The encoded image data; at least 12 bytes are required.</param>
    /// <param name="pformat">Receives the image format, as an <c>IFF_*</c> constant.</param>
    /// <param name="pw">Receives the image width.</param>
    /// <param name="ph">Receives the image height.</param>
    /// <param name="pbps">Receives the number of bits per sample.</param>
    /// <param name="pspp">Receives the number of samples per pixel.</param>
    /// <param name="piscmap">Receives <see langword="true"/> if the image has a colormap.</param>
    /// <remarks>
    /// <para>The actual header is read for JPEG, PNG, TIFF, JPEG 2000 and PNM. For BMP and GIF the
    /// whole image is decoded and the header information is taken from it.</para>
    /// <para>The amount of data needed depends on the format. PNG requires fewer than 30 bytes,
    /// but JPEG can require most of the compressed file; in practice, pass the entire encoded image.</para>
    /// </remarks>
    /// <exception cref="System.ArgumentException"><paramref name="data"/> is shorter than 12 bytes.</exception>
    /// <exception cref="LeptonicaException">The format is unknown or the header cannot be read.</exception>
    public static void ReadHeaderMem(ReadOnlySpan<byte> data, out int pformat, out int pw,
                                     out int ph, out int pbps, out int pspp, out bool piscmap)
    {
        const string func = "pixReadHeaderMem";
        pw = 0;
        ph = 0;
        pbps = 0;
        pspp = 0;
        piscmap = false;
        pformat = 0;
        if (data.Length < 12)
            throw Lept.Arg(func, "size < 12");

        FindFileFormatBuffer(data, out int format);

        int w, h, d, bps, spp;
        bool iscmap = false;
        switch (format)
        {
        case L.IFF_BMP: {  /* cheating: read the pix */
            Pix pix = ReadMemBmp(data);
            pix.GetDimensions(out w, out h, out d);
            bps = (d == 32) ? 8 : d;
            spp = (d == 32) ? 3 : 1;
            break;
        }
        case L.IFF_JFIF_JPEG:
            ReadHeaderMemJpeg(data, out w, out h, out spp, out _, out _);
            bps = 8;
            break;
        case L.IFF_PNG:
            ReadHeaderMemPng(data, out w, out h, out bps, out spp, out iscmap);
            break;
        case L.IFF_TIFF:
        case L.IFF_TIFF_PACKBITS:
        case L.IFF_TIFF_RLE:
        case L.IFF_TIFF_G3:
        case L.IFF_TIFF_G4:
        case L.IFF_TIFF_LZW:
        case L.IFF_TIFF_ZIP:
        case L.IFF_TIFF_JPEG:
            ReadHeaderMemTiff(data, 0, out w, out h, out bps, out spp, out _, out iscmap, out format);
            break;
        case L.IFF_PNM:
            ReadHeaderMemPnm(data, out w, out h, out _, out _, out bps, out spp);
            break;
        case L.IFF_GIF: {
            Pix pix = ReadMemGif(data);
            pix.GetDimensions(out w, out h, out d);
            iscmap = true;
            spp = 1;
            bps = d;
            break;
        }
        case L.IFF_JP2:
            ReadHeaderMemJp2k(data, out w, out h, out bps, out spp, out _);
            break;
        case L.IFF_WEBP:
            bps = 8;
            ReadHeaderMemWebP(data, out w, out h, out spp);
            break;
        case L.IFF_PS:
            pformat = format;
            throw Lept.Fail(func, "PostScript reading is not supported");
        case L.IFF_LPDF:
            pformat = format;
            throw Lept.Fail(func, "Pdf reading is not supported");
        case L.IFF_SPIX:
            SreadHeaderSpix(data, out w, out h, out bps, out spp, out iscmap);
            break;
        default:
            throw Lept.Fail(func, "unknown format; no data returned");
        }

        pw = w;
        ph = h;
        pbps = bps;
        pspp = spp;
        piscmap = iscmap;
        pformat = format;
    }

    /*---------------------------------------------------------------------*
     *                    Output image file information                    *
     *---------------------------------------------------------------------*/
    // leptonica: writeImageFileInfo (readfile.c)
    /// <summary>Writes a description of an image file, from its header and optionally from the decoded image, to a stream.</summary>
    /// <param name="filename">Path of the image file.</param>
    /// <param name="fpout">The stream that receives the text.</param>
    /// <param name="headeronly"><see langword="true"/> to report only the header information; <see langword="false"/> to also read the full image and report on it.</param>
    /// <exception cref="System.ArgumentException"><paramref name="filename"/> or <paramref name="fpout"/> is <see langword="null"/>.</exception>
    public static void WriteImageFileInfo(string filename, Stream fpout, bool headeronly)
    {
        const string func = "writeImageFileInfo";
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");
        if (fpout == null)
            throw Lept.Arg(func, "stream not defined");

            /* Read the header */
        ReadHeader(filename, out int format, out int w, out int h,
                   out int bps, out int spp, out bool iscmap);
        CStdio.Fprintf(fpout, $"===============================================\nReading the header:\n");
        CStdio.Fprintf(fpout, $"  input image format type: {L.ImageFileFormatExtensions[format]}\n");
        CStdio.Fprintf(fpout, $"  w = {w}, h = {h}, bps = {bps}, spp = {spp}, iscmap = {(iscmap ? 1 : 0)}\n");

        FindFileFormat(filename, out format);
        if (format == L.IFF_JP2) {
            using FileStream fpin = Utils.FopenReadStream(filename);
            FgetJp2kResolution(fpin, out int xres, out int yres);
            CStdio.Fprintf(fpout, $"  xres = {xres}, yres = {yres}\n");
        } else if (format == L.IFF_PNG) {
            using (FileStream fpin = Utils.FopenReadStream(filename)) {
                FgetPngResolution(fpin, out int xres, out int yres);
                CStdio.Fprintf(fpout, $"  xres = {xres}, yres = {yres}\n");
            }
            if (iscmap) {
                PixColormap? cmapHdr;
                bool transparency;
                using (FileStream fpin = Utils.FopenReadStream(filename))
                    FgetPngColormapInfo(fpin, out cmapHdr, out transparency);
                if (transparency)
                    CStdio.Fprintf(fpout, $"  colormap has transparency\n");
                else
                    CStdio.Fprintf(fpout, $"  colormap does not have transparency\n");
                cmapHdr?.WriteStream(fpout);
            }
        } else if (format == L.IFF_JFIF_JPEG) {
            using FileStream fpin = Utils.FopenReadStream(filename);
            FgetJpegResolution(fpin, out int xres, out int yres);
            CStdio.Fprintf(fpout, $"  xres = {xres}, yres = {yres}\n");
        }

        if (headeronly)
            return;

            /* Read the full image.  Note that when we read an image that
             * has transparency in a colormap, we convert it to RGBA. */
        CStdio.Fprintf(fpout, $"===============================================\nReading the full image:\n");

            /* Preserve 16 bpp if the format is png */
        if (format == L.IFF_PNG && bps == 16)
            PngSetReadStrip16To8(0);

        Pix pix;
        try {
            pix = Read(filename);
        } finally {
            if (format == L.IFF_PNG && bps == 16)
                PngSetReadStrip16To8(1);
        }

        format = pix.InputFormat;
        pix.GetDimensions(out w, out h, out int d);
        int wpl = pix.Wpl;
        spp = pix.Spp;
        CStdio.Fprintf(fpout, $"  input image format type: {L.ImageFileFormatExtensions[format]}\n");
        CStdio.Fprintf(fpout, $"  w = {w}, h = {h}, d = {d}, spp = {spp}, wpl = {wpl}\n");
        CStdio.Fprintf(fpout, $"  xres = {pix.XRes}, yres = {pix.YRes}\n");

        string? text = pix.Text;
        if (text != null)
            CStdio.Fprintf(fpout, $"  text: {text}\n");

        PixColormap? cmap = pix.Colormap;
        if (cmap != null) {
            if (cmap.HasColor())
                CStdio.Fprintf(fpout, $"  colormap exists and has color values:");
            else
                CStdio.Fprintf(fpout, $"  colormap exists and has only gray values:");
            cmap.WriteStream(fpout);
        } else {
            CStdio.Fprintf(fpout, $"  colormap does not exist\n");
        }

        if (format == L.IFF_TIFF || format == L.IFF_TIFF_G4 ||
            format == L.IFF_TIFF_G3 || format == L.IFF_TIFF_PACKBITS) {
            CStdio.Fprintf(fpout, $"  Tiff header information:\n");
            int npages;
            using (FileStream fpin = Utils.FopenReadStream(filename))
                TiffGetCount(fpin, out npages);
            if (npages == 1)
                CStdio.Fprintf(fpout, $"    One page in file\n");
            else
                CStdio.Fprintf(fpout, $"    {npages} pages in file\n");
            FprintTiffInfo(fpout, filename);
        }

        if (d == 1) {
            int count = pix.CountPixels(null);
            pix.GetDimensions(out w, out h, out _);
            string fracLine = CFormat.Sprintf("  1 bpp: foreground pixel fraction ON/Total = %g\n",
                                              (float)count / (float)(w * h));
            CStdio.Fprintf(fpout, $"{fracLine}");
        }
        CStdio.Fprintf(fpout, $"===============================================\n");

        if (pix.Spp == 4) {
            Pix pixt = pix.DisplayLayersRGBA(0xffffff00u, 600);
            pixt.Display(100, 100);
        }
    }

    /*---------------------------------------------------------------------*
     *             Test function for I/O with different formats            *
     *---------------------------------------------------------------------*/
    // leptonica: ioFormatTest (readfile.c)
    /// <returns>true if any format round-trip had problems; false if all OK</returns>
    public static bool IoFormatTest(string filename)
    {
        const string func = "ioFormatTest";
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");

            /* Read the input file and limit the size */
        Pix pix1 = Read(filename);
        pix1.GetDimensions(out int w, out int h, out _);
        Pix pixs;
        if (w > 250 && h > 250) {  /* take the central 250 x 250 region */
            Box box = Box.Create(w / 2 - 125, h / 2 - 125, 250, 250);
            pixs = pix1.ClipRectangle(box, out _)!;
        } else {
            pixs = Clone(pix1);
        }

        Utils.LeptMkdir("lept/format");

        Pix pixc = Clone(pixs);
        if (pixc.Spp == 4)
            pixc.Spp = 3;
        PixColormap? cmap = pixc.Colormap;
        int d = pixc.Depth;

        bool problems = false;

            /* ----------------------- BMP -------------------------- */
        if (d == 1 || d == 8) {
            Log.Info(func, "write/read bmp");
            Write(FILE_BMP, pixc, L.IFF_BMP);
            pix1 = Read(FILE_BMP);
            Pix pix2 = cmap == null
                ? pix1.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC)
                : Clone(pix1);
            if (!Equal(pixc, pix2)) {
                Log.Info(func, $"   **** bad bmp image: d = {d} ****");
                problems = true;
            }
        }

        if (d == 2 || d == 4 || d == 32) {
            Log.Info(func, "write/read bmp");
            Write(FILE_BMP, pixc, L.IFF_BMP);
            pix1 = Read(FILE_BMP);
            if (!Equal(pixc, pix1)) {
                Log.Info(func, $"   **** bad bmp image: d = {d} ****");
                problems = true;
            }
        }

            /* ----------------------- PNG -------------------------- */
        if (d != 16) {
            Log.Info(func, "write/read png");
            Write(FILE_PNG, pixc, L.IFF_PNG);
            pix1 = Read(FILE_PNG);
            if (!Equal(pixc, pix1)) {
                Log.Info(func, $"   **** bad png image: d = {d} ****");
                problems = true;
            }
        }

            /* ----------------------- TIFF -------------------------- */
        Log.Info(func, "write/read uncompressed tiff");
        Write(FILE_TIFF, pixc, L.IFF_TIFF);
        pix1 = Read(FILE_TIFF);
        if (!Equal(pixc, pix1)) {
            Log.Info(func, $"   **** bad tiff uncompressed image: d = {d} ****");
            problems = true;
        }

        Log.Info(func, "write/read lzw compressed tiff");
        Write(FILE_LZW, pixc, L.IFF_TIFF_LZW);
        pix1 = Read(FILE_LZW);
        if (!Equal(pixc, pix1)) {
            Log.Info(func, $"   **** bad tiff lzw compressed image: d = {d} ****");
            problems = true;
        }

        Log.Info(func, "write/read zip compressed tiff");
        Write(FILE_ZIP, pixc, L.IFF_TIFF_ZIP);
        pix1 = Read(FILE_ZIP);
        if (!Equal(pixc, pix1)) {
            Log.Info(func, $"   **** bad tiff zip compressed image: d = {d} ****");
            problems = true;
        }

        if (d == 8 || d == 32) {
            Log.Info(func, "write/read jpeg compressed tiff");
            if (d == 8 && pixc.Colormap != null) {
                Pix pixc1 = pixc.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
                Write(FILE_TIFF_JPEG, pixc1, L.IFF_TIFF_JPEG);
                pix1 = Read(FILE_TIFF_JPEG);
            } else {
                Write(FILE_TIFF_JPEG, pixc, L.IFF_TIFF_JPEG);
                pix1 = Read(FILE_TIFF_JPEG);
                float diff;
                if (d == 8)
                    pix1.CompareGray(pixc, L.L_COMPARE_ABS_DIFF, 0, out _, out diff, out _, out _);
                else
                    pix1.CompareRGB(pixc, L.L_COMPARE_ABS_DIFF, 0, out _, out diff, out _, out _);
                if (diff > 8.0f) {
                    Log.Info(func, $"   **** bad tiff jpeg compressed image: d = {d}, diff = {diff:F2} ****");
                    problems = true;
                }
            }
        }

        if (d == 1) {
            Log.Info(func, "write/read g4 compressed tiff");
            Write(FILE_G4, pixc, L.IFF_TIFF_G4);
            pix1 = Read(FILE_G4);
            if (!Equal(pixc, pix1)) {
                Log.Info(func, "   **** bad tiff g4 image ****");
                problems = true;
            }

            Log.Info(func, "write/read g3 compressed tiff");
            Write(FILE_G3, pixc, L.IFF_TIFF_G3);
            pix1 = Read(FILE_G3);
            if (!Equal(pixc, pix1)) {
                Log.Info(func, "   **** bad tiff g3 image ****");
                problems = true;
            }

            Log.Info(func, "write/read rle compressed tiff");
            Write(FILE_RLE, pixc, L.IFF_TIFF_RLE);
            pix1 = Read(FILE_RLE);
            if (!Equal(pixc, pix1)) {
                Log.Info(func, $"   **** bad tiff rle image: d = {d} ****");
                problems = true;
            }

            Log.Info(func, "write/read packbits compressed tiff");
            Write(FILE_PB, pixc, L.IFF_TIFF_PACKBITS);
            pix1 = Read(FILE_PB);
            if (!Equal(pixc, pix1)) {
                Log.Info(func, $"   **** bad tiff packbits image: d = {d} ****");
                problems = true;
            }
        }

            /* ----------------------- PNM -------------------------- */
        Log.Info(func, "write/read pnm");
        Write(FILE_PNM, pixc, L.IFF_PNM);
        pix1 = Read(FILE_PNM);
        Pix pix2Pnm = cmap != null
            ? pixc.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC)
            : Clone(pixc);
        if (!Equal(pix1, pix2Pnm)) {
            Log.Info(func, $"   **** bad pnm image: d = {d} ****");
            problems = true;
        }

            /* ----------------------- GIF -------------------------- */
        {
            Pix pixGif = (d != 8 || cmap == null) ? pixc.ConvertTo8(true) : Clone(pixc);
            Log.Info(func, "write/read gif");
            Write(FILE_GIF, pixGif, L.IFF_GIF);
            Pix pixGif2 = Read(FILE_GIF);
            if (!Equal(pixGif, pixGif2)) {
                Log.Info(func, $"   **** bad gif image: d = {d} ****");
                problems = true;
            }
        }

            /* ----------------------- JPEG ------------------------- */
        {
            Pix pixJpg = (cmap != null || d > 8) ? pixc.ConvertTo32() : pixc.ConvertTo8(false);
            int depth = pixJpg.Depth;
            Log.Info(func, "write/read jpeg");
            Write(FILE_JPG, pixJpg, L.IFF_JFIF_JPEG);
            Pix pixJpg2 = Read(FILE_JPG);
            float diff;
            if (depth == 8)
                pixJpg.CompareGray(pixJpg2, L.L_COMPARE_ABS_DIFF, 0, out _, out diff, out _, out _);
            else
                pixJpg.CompareRGB(pixJpg2, L.L_COMPARE_ABS_DIFF, 0, out _, out diff, out _, out _);
            if (diff > 8.0f) {
                Log.Info(func, $"   **** bad jpeg image: d = {depth}, diff = {diff:F2} ****");
                problems = true;
            }
        }

            /* ----------------------- WEBP ------------------------- */
        {
            Pix pixWebp = (cmap != null || d <= 16) ? pixc.ConvertTo32() : Clone(pixc);
            int depth = pixWebp.Depth;
            Log.Info(func, "write/read webp");
            Write(FILE_WEBP, pixWebp, L.IFF_WEBP);
            Pix pixWebp2 = Read(FILE_WEBP);
            pixWebp.CompareRGB(pixWebp2, L.L_COMPARE_ABS_DIFF, 0, out _, out float diff, out _, out _);
            if (diff > 5.0f) {
                Log.Info(func, $"   **** bad webp image: d = {depth}, diff = {diff:F2} ****");
                problems = true;
            }
        }

            /* ----------------------- JP2K ------------------------- */
        {
            Pix pixJp2 = (cmap != null || d > 8) ? pixc.ConvertTo32() : pixc.ConvertTo8(false);
            int depth = pixJp2.Depth;
            Log.Info(func, "write/read jp2k");
            Write(FILE_JP2K, pixJp2, L.IFF_JP2);
            Pix pixJp22 = Read(FILE_JP2K);
            float diff;
            if (depth == 8)
                pixJp2.CompareGray(pixJp22, L.L_COMPARE_ABS_DIFF, 0, out _, out diff, out _, out _);
            else
                pixJp2.CompareRGB(pixJp22, L.L_COMPARE_ABS_DIFF, 0, out _, out diff, out _, out _);
            if (diff > 7.0f) {
                Log.Info(func, $"   **** bad jp2k image: d = {depth}, diff = {diff:F2} ****");
                problems = true;
            }
        }

        if (!problems)
            Log.Info(func, "All formats read and written OK!");

        return problems;
    }
}

public sealed partial class Pixa
{
    // leptonica: pixaReadFiles (readfile.c)
    /// <param name="dirname">directory</param>
    /// <param name="substr">[optional] substring filter on filenames; can be null</param>
    public static Pixa ReadFiles(string dirname, string? substr)
    {
        const string func = "pixaReadFiles";
        if (dirname == null)
            throw Lept.Arg(func, "dirname not defined");

        Sarray sa = Sarray.GetSortedPathnamesInDirectory(dirname, substr, 0, 0);
        return ReadFilesSA(sa);
    }

    // leptonica: pixaReadFilesSA (readfile.c)
    /// <param name="sa">full pathnames for all files</param>
    public static Pixa ReadFilesSA(Sarray sa)
    {
        const string func = "pixaReadFilesSA";
        if (sa == null)
            throw Lept.Arg(func, "sa not defined");

        int n = sa.Count;
        Pixa pixa = Create(n);
        for (int i = 0; i < n; i++) {
            string str = sa.GetString(i, L.L_NOCOPY);
            Pix pix;
            try {
                pix = Pix.Read(str);
            } catch (LeptonicaException) {
                Log.Warning(func, $"pix not read from file {str}");
                continue;
            }
            pixa.AddPix(pix, L.L_INSERT);
        }

        return pixa;
    }
}
