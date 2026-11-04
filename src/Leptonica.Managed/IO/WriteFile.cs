using System;
using System.IO;
using System.Threading;

namespace Leptonica;

/*
 *  writefile.c: Top-level image writing.
 *
 *     Set jpeg quality for pixWrite() and pixWriteMem()
 *        l_jpegSetQuality()
 *
 *     Set global variable LeptDebugOK for writing to named temp files
 *        setLeptDebugOK()
 *
 *     High-level procedures for writing images to file:
 *        pixaWriteFiles(), pixWriteDebug(), pixWrite(), pixWriteAutoFormat(),
 *        pixWriteStream(), pixWriteImpliedFormat()
 *
 *     Selection of output format if default is requested
 *        pixChooseOutputFormat(), getImpliedFileFormat(), getFormatFromExtension(),
 *        pixGetAutoFormat(), getFormatExtension()
 *
 *     Write to memory
 *        pixWriteMem()
 *
 *     Image display for debugging
 *        l_fileDisplay(), pixDisplay(), pixDisplayWithTitle(), pixMakeColorSquare(),
 *        l_chooseDisplayProg()
 *
 *     Change format for missing library
 *        changeFormatForMissingLib()
 *
 *     Nonfunctional stub of pix output for debugging
 *        pixDisplayWrite()
 *
 *  Supported file formats:
 *  (1) Writing is supported without any external libraries:
 *          bmp
 *          pnm   (including pbm, pgm, etc)
 *          spix  (raw serialized)
 *  (2) Writing is supported with installation of external libraries:
 *          png
 *          jpg   (standard jfif version)
 *          tiff  (including most varieties of compression)
 *          gif
 *          webp
 *          jp2 (jpeg2000)
 *  (3) Writing is supported through special interfaces:
 *          ps (PostScript, in psio1.c, psio2.c):
 *          pdf (PDF, in pdfio.c):
 *
 *  Differences from C:
 *    - The writers of the individual formats are separate files; those that
 *      are not ported yet are stubs, so that pixWrite() and friends throw
 *      "not ported" for those formats.
 *    - changeFormatForMissingLib() depends on the libraries that the C build
 *      is configured with.  The managed port has no external libraries, and
 *      all the formats are considered available, so that the requested format
 *      is never changed.
 *    - Displaying an image: pixDisplay() and pixDisplayWithTitle() write the
 *      temporary file, but do not start a display program, because
 *      callSystemDebug() is not ported.
 *    - Output pointers become return values.  The functions that return an error
 *      code in C throw.
 */

/// <summary>Constants from imageio.h and pix.h used by writefile.c.</summary>
public static partial class L
{
    /* Codecs for jp2k (imageio.h) */
    /// <summary>JPEG 2000 codec type: a raw codestream.</summary>
    public const int L_J2K_CODEC = 1;
    /// <summary>JPEG 2000 codec type: the file format, which begins with an <c>ihdr</c> header box.</summary>
    public const int L_JP2_CODEC = 2;

    /* Programs for pixDisplay() (pix.h) */
    /// <summary>Disable image display.</summary>
    public const int L_DISPLAY_WITH_NONE = 0;
    /// <summary>Display images with <c>xzgv</c>.</summary>
    public const int L_DISPLAY_WITH_XZGV = 1;
    /// <summary>Display images with <c>xli</c>.</summary>
    public const int L_DISPLAY_WITH_XLI = 2;
    /// <summary>Display images with <c>xv</c>.</summary>
    public const int L_DISPLAY_WITH_XV = 3;
    /// <summary>Display images with IrfanView (Windows).</summary>
    public const int L_DISPLAY_WITH_IV = 4;
    /// <summary>Display images with <c>open</c> (macOS and Windows).</summary>
    public const int L_DISPLAY_WITH_OPEN = 5;
}

/// <summary>Settings and helpers for writing and displaying images, such as the debug-output switch and the display program.</summary>
public static class WriteFile
{

    /// <summary>Allows writing to named temp files for debugging (LeptDebugOK); off by default.</summary>
    public static bool LeptDebugOK { get; private set; }

    /* Set defaults for the display program (xv, xli, xzgv, open, irfanview)
     * that is invoked by pixDisplay()  */
    private static int var_DISPLAY_PROG =
        OperatingSystem.IsWindows() ? L.L_DISPLAY_WITH_IV :
        OperatingSystem.IsMacOS() ? L.L_DISPLAY_WITH_OPEN : L.L_DISPLAY_WITH_XZGV;

    /* Parameter that controls jpeg quality for high-level calls. */
    private static int var_JPEG_QUALITY = 75;   /* default */

    /* Local map of image file name extension to output format.
     * Note that the extension string always includes a '.'  */
    private static readonly (string extension, int format)[] extension_map =
    {
        (".bmp", L.IFF_BMP),
        (".jpg", L.IFF_JFIF_JPEG),
        (".jpeg", L.IFF_JFIF_JPEG),
        (".JPG", L.IFF_JFIF_JPEG),
        (".png", L.IFF_PNG),
        (".tif", L.IFF_TIFF),
        (".tiff", L.IFF_TIFF),
        (".tiffg4", L.IFF_TIFF_G4),
        (".pbm", L.IFF_PNM),
        (".pgm", L.IFF_PNM),
        (".pnm", L.IFF_PNM),
        (".gif", L.IFF_GIF),
        (".jp2", L.IFF_JP2),
        (".j2k", L.IFF_JP2),
        (".ps", L.IFF_PS),
        (".pdf", L.IFF_LPDF),
        (".webp", L.IFF_WEBP),
    };

    /// <summary>The jpeg quality used by pixWriteStream() and pixWriteMem().</summary>
    internal static int JpegQuality => var_JPEG_QUALITY;

    /// <summary>The program chosen for pixDisplay() (L_DISPLAY_WITH_*).</summary>
    internal static int DisplayProg => var_DISPLAY_PROG;

    /*---------------------------------------------------------------------*
     *           Set jpeg quality for pixWrite() and pixWriteMem()         *
     *---------------------------------------------------------------------*/

    // leptonica: l_jpegSetQuality (writefile.c)
    /// <summary>Sets the jpeg quality for the high-level write calls.</summary>
    /// <param name="new_quality">1 - 100; 75 is default; 0 defaults to 75</param>
    /// <returns>prev: previous quality</returns>
    /// <remarks>
    /// (1) This variable is used in pixWriteStream() and pixWriteMem(),
    ///     to control the jpeg quality.  The default is 75.
    /// (2) It returns the previous quality, so for example:
    ///      int prev = WriteFile.JpegSetQuality(85);  // sets to 85
    ///      Pix.WriteStream(...);
    ///      WriteFile.JpegSetQuality(prev);   // resets to previous value
    /// (3) On error, logs a message and does not change the variable.
    /// </remarks>
    public static int JpegSetQuality(int new_quality)
    {
        int prevq = var_JPEG_QUALITY;
        int newq = (new_quality == 0) ? 75 : new_quality;
        if (newq < 1 || newq > 100)
            Log.Error("l_jpegSetQuality", "invalid jpeg quality; unchanged\n");
        else
            var_JPEG_QUALITY = newq;
        return prevq;
    }

    /*----------------------------------------------------------------------*
     *    Set global variable LeptDebugOK for writing to named temp files   *
     *----------------------------------------------------------------------*/

    // leptonica: setLeptDebugOK (writefile.c)
    /// <summary>Sets the global switch that allows debug output to named temp files.</summary>
    /// <param name="allow">true or false</param>
    /// <remarks>
    /// (1) This sets or clears the global variable LeptDebugOK, to
    ///     control writing files in a temp directory with names that
    ///     are compiled in.
    /// (2) The default in the library distribution is false.  Call with
    ///     allow = true for development and debugging.
    /// </remarks>
    public static void SetLeptDebugOK(bool allow)
    {
        LeptDebugOK = allow;
    }

    /*---------------------------------------------------------------------*
     *          Selection of output format if default is requested         *
     *---------------------------------------------------------------------*/

    // leptonica: getImpliedFileFormat (writefile.c)
    /// <summary>Determines the output file format from the extension of a filename.</summary>
    /// <param name="filename">the file name</param>
    /// <returns>output format, or IFF_UNKNOWN on invalid extension</returns>
    public static int GetImpliedFileFormat(string filename)
    {
        Utils.SplitPathAtExtension(filename, out _, out string extension);
        return GetFormatFromExtension(extension);
    }

    // leptonica: getFormatFromExtension (writefile.c)
    /// <summary>Determines the format for writing that corresponds to an extension.</summary>
    /// <param name="extension">including the dot, e.g. ".jpg"</param>
    /// <returns>output format, or IFF_UNKNOWN on invalid extension</returns>
    /// <remarks>
    /// (1) This determines the integer for writing in a format that
    ///     corresponds to the image file type extension.  For example,
    ///     the integer code corresponding to the extension ".jpg" is 2;
    ///     it is used to write with jpeg encoding.
    /// </remarks>
    public static int GetFormatFromExtension(string extension)
    {
        int format = L.IFF_UNKNOWN;
        for (int i = 0; i < extension_map.Length; i++) {
            if (string.Equals(extension, extension_map[i].extension, StringComparison.Ordinal)) {
                format = extension_map[i].format;
                break;
            }
        }
        return format;
    }

    // leptonica: getFormatExtension (writefile.c)
    /// <summary>Returns the extension string of an output format.</summary>
    /// <param name="format">integer, IFF_*</param>
    /// <returns>extension string</returns>
    public static string GetFormatExtension(int format)
    {
        if (format < 0 || format >= L.ImageFileFormatExtensions.Length)
            throw Lept.Arg("getFormatExtension", "invalid format");

        return L.ImageFileFormatExtensions[format];
    }

    /*---------------------------------------------------------------------*
     *                      Image display for debugging                    *
     *---------------------------------------------------------------------*/

    // leptonica: l_fileDisplay (writefile.c)
    /// <summary>Displays an image file for debugging.</summary>
    /// <param name="fname">the file</param>
    /// <param name="x">location of display frame on the screen</param>
    /// <param name="y">location of display frame on the screen</param>
    /// <param name="scale">scale factor (use 0 to skip display)</param>
    /// <remarks>
    /// (1) This is a convenient wrapper for displaying image files.
    /// (2) It does nothing unless LeptDebugOK is true.
    /// (3) Set scale = 0 to disable display.
    /// (4) This downscales 1 bpp to gray.
    /// </remarks>
    public static void FileDisplay(string fname, int x, int y, float scale)
    {
        const string func = "l_fileDisplay";

        if (!LeptDebugOK) {
            Log.Info(func, "displaying files is disabled; " +
                     "use setLeptDebugOK(1) to enable\n");
            return;
        }
        if (scale == 0.0)
            return;
        if (scale < 0.0)
            throw Lept.Arg(func, "invalid scale factor");
        Pix pixs = Pix.Read(fname);

        Pix pixd;
        if (scale == 1.0) {
            pixd = Pix.Clone(pixs);
        } else {
            if (scale < 1.0 && pixs.Depth == 1)
                pixd = pixs.ScaleToGray(scale);
            else
                pixd = pixs.Scale(scale, scale);
        }
        pixd.Display(x, y);
    }

    // leptonica: l_chooseDisplayProg (writefile.c)
    /// <summary>Chooses the program used by pixDisplay().</summary>
    /// <param name="selection">L_DISPLAY_WITH_XLI, L_DISPLAY_WITH_XZGV, L_DISPLAY_WITH_XV,
    /// L_DISPLAY_WITH_IV, L_DISPLAY_WITH_OPEN</param>
    public static void ChooseDisplayProg(int selection)
    {
        if (selection == L.L_DISPLAY_WITH_XLI ||
            selection == L.L_DISPLAY_WITH_XZGV ||
            selection == L.L_DISPLAY_WITH_XV ||
            selection == L.L_DISPLAY_WITH_IV ||
            selection == L.L_DISPLAY_WITH_OPEN) {
            var_DISPLAY_PROG = selection;
        } else {
            Log.Error("l_chooseDisplayProg", "invalid display program\n");
        }
    }

    /*---------------------------------------------------------------------*
     *                   Change format for missing lib                     *
     *---------------------------------------------------------------------*/

    // leptonica: changeFormatForMissingLib (writefile.c)
    /// <summary>Changes the output format when the library of the format is missing.</summary>
    /// <param name="pformat">the requested output image format</param>
    /// <remarks>
    /// (1) This is useful for testing functionality when the library for
    ///     the requested output format (jpeg, png or tiff) is not linked.
    ///     In that case, the output format is changed to bmp.
    /// (2) The managed port has no external libraries; all the formats are
    ///     available, and the format is never changed.
    /// </remarks>
    public static void ChangeFormatForMissingLib(ref int pformat)
    {
    }

    /// <summary>Number of image file format extensions (NumImageFileFormatExtensions).</summary>
    public static int NumImageFileFormatExtensions => L.ImageFileFormatExtensions.Length;

    internal static int NextDisplayIndex(out bool first)
    {
        // caution: not .so safe
        first = Volatile.Read(ref displayIndex) == 0;
        return Interlocked.Increment(ref displayIndex);
    }

    private static int displayIndex;
}

public sealed partial class Pix
{
    private const int MaxDisplayWidth = 1000;
    private const int MaxDisplayHeight = 800;
    private const int MaxSizeForPng = 200;

    /* PostScript output for printing */
    private const float DefaultScaling = 1.0f;

    /// <summary>Same as <see cref="WriteFile.LeptDebugOK"/>.</summary>
    public static bool LeptDebugOK
    {
        get => WriteFile.LeptDebugOK;
        set => WriteFile.SetLeptDebugOK(value);
    }

    /*---------------------------------------------------------------------*
     *           Top-level procedures for writing images to file           *
     *---------------------------------------------------------------------*/

    // leptonica: pixWriteDebug (writefile.c)
    /// <summary>Writes this pix to a debug file, if LeptDebugOK allows it.</summary>
    /// <param name="fname">the file</param>
    /// <param name="format">IFF_*</param>
    /// <remarks>
    /// (1) Debug version, intended for use in the library when writing
    ///     to files in a temp directory with names that are compiled in.
    ///     This is used instead of pixWrite() for all such library calls.
    /// (2) The global variable LeptDebugOK defaults to false, and can be set
    ///     or cleared by the function setLeptDebugOK().
    /// </remarks>
    public void WriteDebug(string fname, int format)
    {
        if (WriteFile.LeptDebugOK)
            Write(fname, this, format);
        else
            Log.Info("pixWriteDebug", $"write to named temp file {fname} is disabled\n");
    }

    // leptonica: pixWrite (writefile.c)
    /// <summary>Writes the pix to a file in the given format.</summary>
    /// <param name="fname">the file</param>
    /// <param name="pix">the image</param>
    /// <param name="format">IFF_*</param>
    /// <remarks>
    /// (1) Open for write using binary mode (with the "b" flag)
    ///     to avoid having Windows automatically translate the NL
    ///     into CRLF, which corrupts image files.
    /// (2) If the default image format IFF_DEFAULT is requested:
    ///     use the input format if known; otherwise, use a lossless format.
    /// (3) The default jpeg quality is 75.  For some other value,
    ///     Use l_jpegSetQuality().
    /// </remarks>
    public static void Write(string fname, Pix pix, int format)
    {
        const string func = "pixWrite";

        using FileStream fp = Utils.FopenWriteStream(fname, "wb+");
        try {
            WriteStream(fp, pix, format);
        } catch (Exception e) when (e is ArgumentException || e is LeptonicaException) {
            Log.Error(func, $"pix not written to stream: {fname}\n");
            throw;
        }
    }

    // leptonica: pixWriteAutoFormat (writefile.c)
    /// <summary>Writes the pix to a file, in a format chosen from its properties.</summary>
    /// <param name="filename">the file</param>
    /// <param name="pix">the image</param>
    public static void WriteAutoFormat(string filename, Pix pix)
    {
        int format = pix.GetAutoFormat();
        Write(filename, pix, format);
    }

    // leptonica: pixWriteStream (writefile.c)
    /// <summary>Writes the pix to a stream in the given format.</summary>
    /// <param name="fp">file stream</param>
    /// <param name="pix">the image</param>
    /// <param name="format">IFF_*</param>
    public static void WriteStream(Stream fp, Pix pix, int format)
    {
        if (format == L.IFF_DEFAULT)
            format = pix.ChooseOutputFormat();

            /* Use bmp format for testing if library for requested
             * format for jpeg, png or tiff is not available */
        WriteFile.ChangeFormatForMissingLib(ref format);

        switch(format)
        {
        case L.IFF_BMP:
            WriteStreamBmp(fp, pix);
            break;

        case L.IFF_JFIF_JPEG:   /* default quality; baseline sequential */
            WriteStreamJpeg(fp, pix, WriteFile.JpegQuality, 0);
            break;

        case L.IFF_PNG:   /* no gamma value stored */
            WriteStreamPng(fp, pix, 0.0f);
            break;

        case L.IFF_TIFF:           /* uncompressed */
        case L.IFF_TIFF_PACKBITS:  /* compressed, binary only */
        case L.IFF_TIFF_RLE:       /* compressed, binary only */
        case L.IFF_TIFF_G3:        /* compressed, binary only */
        case L.IFF_TIFF_G4:        /* compressed, binary only */
        case L.IFF_TIFF_LZW:       /* compressed, all depths */
        case L.IFF_TIFF_ZIP:       /* compressed, all depths */
        case L.IFF_TIFF_JPEG:      /* compressed, 8 bpp gray and 32 bpp rgb */
            WriteStreamTiff(fp, pix, format);
            break;

        case L.IFF_PNM:
            WriteStreamPnm(fp, pix);
            break;

        case L.IFF_PS:
            WriteStreamPS(fp, pix, null, 0, DefaultScaling);
            break;

        case L.IFF_GIF:
            WriteStreamGif(fp, pix);
            break;

        case L.IFF_JP2:
            WriteStreamJp2k(fp, pix, 34, 0, L.L_JP2_CODEC, 0, 0);
            break;

        case L.IFF_WEBP:
            WriteStreamWebP(fp, pix, 80, 0);
            break;

        case L.IFF_LPDF:
            WriteStreamPdf(fp, pix, 0, null);
            break;

        case L.IFF_SPIX:
            WriteStreamSpix(fp, pix);
            break;

        default:
            throw Lept.Arg("pixWriteStream", "unknown format");
        }
    }

    // leptonica: pixWriteImpliedFormat (writefile.c)
    /// <summary>Writes the pix to a file, with the format determined by the file extension.</summary>
    /// <param name="filename">the file</param>
    /// <param name="pix">the image</param>
    /// <param name="quality">iff JPEG; 1 - 100, 0 for default</param>
    /// <param name="progressive">iff JPEG; 0 for baseline seq., 1 for progressive</param>
    /// <remarks>
    /// (1) This determines the output format from the filename extension.
    /// (2) The last two args are ignored except for requests for jpeg files.
    /// (3) The jpeg default quality is 75.
    /// </remarks>
    public static void WriteImpliedFormat(string filename, Pix pix, int quality, int progressive)
    {
            /* Determine output format */
        int format = WriteFile.GetImpliedFileFormat(filename);
        if (format == L.IFF_UNKNOWN) {
            format = L.IFF_PNG;
        } else if (format == L.IFF_TIFF) {
            if (pix.Depth == 1)
                format = L.IFF_TIFF_G4;
            else if (OperatingSystem.IsWindows())
                format = L.IFF_TIFF_LZW;  /* poor compression */
            else
                format = L.IFF_TIFF_ZIP;  /* native Windows tools can't handle this */
        }

        if (format == L.IFF_JFIF_JPEG) {
            quality = Math.Min(quality, 100);
            quality = Math.Max(quality, 0);
            if (progressive != 0 && progressive != 1) {
                progressive = 0;
                Log.Warning("pixWriteImpliedFormat", "invalid progressive; setting to baseline\n");
            }
            if (quality == 0)
                quality = 75;
            WriteJpeg(filename, pix, quality, progressive);
        } else {
            Write(filename, pix, format);
        }
    }

    /*---------------------------------------------------------------------*
     *          Selection of output format if default is requested         *
     *---------------------------------------------------------------------*/

    // leptonica: pixChooseOutputFormat (writefile.c)
    /// <summary>Chooses the output format when IFF_DEFAULT is requested.</summary>
    /// <returns>output format</returns>
    /// <remarks>
    /// (1) This should only be called if the requested format is IFF_DEFAULT.
    /// (2) If the pix wasn't read from a file, its input format value
    ///     will be IFF_UNKNOWN, and in that case it is written out
    ///     in a compressed but lossless format.
    /// </remarks>
    public int ChooseOutputFormat()
    {
        int d = Depth;
        int format = InputFormat;
        if (format == L.IFF_UNKNOWN) {  /* output lossless */
            if (d == 1)
                format = L.IFF_TIFF_G4;
            else
                format = L.IFF_PNG;
        }

        return format;
    }

    // leptonica: pixGetAutoFormat (writefile.c)
    /// <summary>Decides what format to use based on the pix.</summary>
    /// <returns>format: IFF_TIFF_G4, IFF_JFIF_JPEG or IFF_PNG</returns>
    /// <remarks>
    /// (1) The output formats are restricted to tiff, jpeg and png
    ///     because these are the most commonly used image formats and
    ///     the ones that are typically installed with leptonica.
    /// (2) This decides what compression to use based on the pix.
    ///     It chooses tiff-g4 if 1 bpp without a colormap, jpeg with
    ///     quality 75 if grayscale, rgb or rgba (where it loses
    ///     the alpha layer), and lossless png for all other situations.
    /// </remarks>
    public int GetAutoFormat()
    {
        int d = Depth;
        PixColormap? cmap = Colormap;
        if (d == 1 && cmap == null)
            return L.IFF_TIFF_G4;
        if ((d == 8 && cmap == null) || d == 24 || d == 32)
            return L.IFF_JFIF_JPEG;
        return L.IFF_PNG;
    }

    /*---------------------------------------------------------------------*
     *                            Write to memory                          *
     *---------------------------------------------------------------------*/

    // leptonica: pixWriteMem (writefile.c)
    /// <summary>Writes the pix to memory in the given format.</summary>
    /// <param name="pix">the image</param>
    /// <param name="format">IFF_*</param>
    /// <returns>data of the compressed image</returns>
    /// <remarks>
    /// (1) PostScript output is uncompressed, in hex ascii.
    ///     Most printers support level 2 compression (tiff_g4 for 1 bpp,
    ///     jpeg for 8 and 32 bpp).
    /// (2) The default jpeg quality is 75.  For some other value,
    ///     Use l_jpegSetQuality().
    /// </remarks>
    public static byte[] WriteMem(Pix pix, int format)
    {
        if (format == L.IFF_DEFAULT)
            format = pix.ChooseOutputFormat();

            /* Use bmp format for testing if library for requested
             * format for jpeg, png or tiff is not available */
        WriteFile.ChangeFormatForMissingLib(ref format);

        switch(format)
        {
        case L.IFF_BMP:
            return WriteMemBmp(pix);

        case L.IFF_JFIF_JPEG:   /* default quality; baseline sequential */
            return WriteMemJpeg(pix, WriteFile.JpegQuality, 0);

        case L.IFF_PNG:   /* no gamma value stored */
            return WriteMemPng(pix, 0.0f);

        case L.IFF_TIFF:           /* uncompressed */
        case L.IFF_TIFF_PACKBITS:  /* compressed, binary only */
        case L.IFF_TIFF_RLE:       /* compressed, binary only */
        case L.IFF_TIFF_G3:        /* compressed, binary only */
        case L.IFF_TIFF_G4:        /* compressed, binary only */
        case L.IFF_TIFF_LZW:       /* compressed, all depths */
        case L.IFF_TIFF_ZIP:       /* compressed, all depths */
        case L.IFF_TIFF_JPEG:      /* compressed, 8 bpp gray or 32 bpp rgb */
            return WriteMemTiff(pix, format);

        case L.IFF_PNM:
            return WriteMemPnm(pix);

        case L.IFF_PS:
            return WriteMemPS(pix, null, 0, DefaultScaling);

        case L.IFF_GIF:
            return WriteMemGif(pix);

        case L.IFF_JP2:
            return WriteMemJp2k(pix, 34, 0, 0, 0);

        case L.IFF_WEBP:
            return WriteMemWebP(pix, 80, 0);

        case L.IFF_LPDF:
            return WriteMemPdf(pix, 0, null);

        case L.IFF_SPIX:
            return WriteMemSpix(pix);

        default:
            throw Lept.Arg("pixWriteMem", "unknown format");
        }
    }

    /*---------------------------------------------------------------------*
     *                      Image display for debugging                    *
     *---------------------------------------------------------------------*/

    // leptonica: pixDisplay (writefile.c)
    /// <summary>Displays the image on the screen, for debugging.</summary>
    /// <param name="x">location of display frame on the screen</param>
    /// <param name="y">location of display frame on the screen</param>
    /// <remarks>
    /// The pix is 1, 2, 4, 8, 16, 32 bpp.
    /// (1) This is debugging code that displays an image on the screen.
    ///     It uses a static internal variable to number the output files
    ///     written by a single process.
    /// (2) It does nothing unless LeptDebugOK is true.
    /// (3) Here, the temporary file is written, but no display program is
    ///     started.
    /// (4) Images with dimensions larger than MaxDisplayWidth or
    ///     MaxDisplayHeight are downscaled to fit those constraints.
    ///     For 1 bpp, we use scale-to-gray to get decent-looking
    ///     anti-aliased images.  In all cases, we write a temporary file
    ///     to /tmp/lept/disp.
    /// (5) The temporary file is written as png if, after initial
    ///     processing for special cases, any of these obtain:
    ///       * pix dimensions are smaller than some thresholds
    ///       * pix depth is less than 8 bpp
    ///       * pix is colormapped
    /// (6) For spp == 4, we call pixDisplayLayersRGBA() to show 3
    ///     versions of the image: the image with a fully opaque
    ///     alpha, the alpha, and the image as it would appear with
    ///     a white background.
    /// (7) pixDisplay() can be inactivated at runtime by calling:
    ///          l_chooseDisplayProg(L_DISPLAY_WITH_NONE);
    /// </remarks>
    public void Display(int x, int y)
    {
        DisplayWithTitle(x, y, null, 1);
    }

    // leptonica: pixDisplayWithTitle (writefile.c)
    /// <summary>Displays the image on the screen, with an optional title.</summary>
    /// <param name="x">location of display frame</param>
    /// <param name="y">location of display frame</param>
    /// <param name="title">[optional] on frame; can be null</param>
    /// <param name="dispflag">1 to write, else disabled</param>
    /// <remarks>
    /// (1) See notes for pixDisplay().
    /// (2) This displays the image if dispflag == 1 and the global
    ///     var_DISPLAY_PROG != L_DISPLAY_WITH_NONE; otherwise it punts.
    /// </remarks>
    public void DisplayWithTitle(int x, int y, string? title, int dispflag)
    {
        const string func = "pixDisplayWithTitle";

        if (!WriteFile.LeptDebugOK) {
            Log.Info(func, "displaying images is disabled;\n      " +
                     "use setLeptDebugOK(1) to enable\n");
            return;
        }

        int prog = WriteFile.DisplayProg;
        if (dispflag != 1 || prog == L.L_DISPLAY_WITH_NONE)
            return;

        if (!OperatingSystem.IsWindows()) {  /* unix */
            if (prog != L.L_DISPLAY_WITH_XZGV &&
                prog != L.L_DISPLAY_WITH_XLI &&
                prog != L.L_DISPLAY_WITH_XV &&
                prog != L.L_DISPLAY_WITH_OPEN)
                throw Lept.Arg(func, "invalid unix program chosen for display");
        } else {  /* _WIN32 */
            if (prog != L.L_DISPLAY_WITH_IV &&
                prog != L.L_DISPLAY_WITH_OPEN)
                throw Lept.Arg(func, "invalid windows program chosen for display");
        }

            /* Display with three views if either spp = 4 or if colormapped
             * and the alpha component is not fully opaque */
        bool opaque = true;
        PixColormap? cmap = Colormap;
        if (cmap != null)
            opaque = cmap.IsOpaque();
        int spp = Spp;
        bool threeviews = (spp == 4 || !opaque) ? true : false;

            /* If colormapped and not opaque, remove the colormap to RGBA */
        Pix pix0;
        if (!opaque)
            pix0 = RemoveColormap(L.REMOVE_CMAP_WITH_ALPHA);
        else
            pix0 = this;

            /* Scale if necessary; this will also remove a colormap */
        pix0.GetDimensions(out int w, out int h, out int d);
        int maxheight = (threeviews) ? MaxDisplayHeight / 3 : MaxDisplayHeight;
        Pix pix1;
        if (w <= MaxDisplayWidth && h <= maxheight) {
            if (d == 16)  /* take MSB */
                pix1 = pix0.Convert16To8(L.L_MS_BYTE);
            else
                pix1 = pix0;
        } else {
            float ratw = (float)MaxDisplayWidth / (float)w;
            float rath = (float)maxheight / (float)h;
            float ratmin = Math.Min(ratw, rath);
            if (ratmin < 0.125 && d == 1)
                pix1 = pix0.ScaleToGray8();
            else if (ratmin < 0.25 && d == 1)
                pix1 = pix0.ScaleToGray4();
            else if (ratmin < 0.33 && d == 1)
                pix1 = pix0.ScaleToGray3();
            else if (ratmin < 0.5 && d == 1)
                pix1 = pix0.ScaleToGray2();
            else
                pix1 = pix0.Scale(ratmin, ratmin);
        }

            /* Generate the three views if required */
        Pix pix2;
        if (threeviews)
            pix2 = pix1.DisplayLayersRGBA(0xffffff00, 0);
        else
            pix2 = pix1;

        int index = WriteFile.NextDisplayIndex(out bool first);
        if (first) {  /* erase any existing images */
            Utils.LeptRmdir("lept/disp");
            Utils.LeptMkdir("lept/disp");
        }

        string buffer;
        if (pix2.Depth < 8 || pix2.Colormap != null ||
            (w < MaxSizeForPng && h < MaxSizeForPng)) {
            buffer = $"/tmp/lept/disp/write.{index:D3}.png";
            Write(buffer, pix2, L.IFF_PNG);
        } else {
            buffer = $"/tmp/lept/disp/write.{index:D3}.jpg";
            Write(buffer, pix2, L.IFF_JFIF_JPEG);
        }

            /* NOTE: the display program (xzgv, xli, xv, open, i_view32) is not
             * started: callSystemDebug() is not ported. */
    }

    // leptonica: pixMakeColorSquare (writefile.c)
    /// <summary>Makes a square of one color, optionally with a label of its components.</summary>
    /// <param name="color">in 0xrrggbb00 format</param>
    /// <param name="size">in pixels; &gt;= 100; use 0 for default (min size)</param>
    /// <param name="addlabel">use 1 to display the color component values</param>
    /// <param name="location">of text: L_ADD_ABOVE, etc; ignored if addlabel == 0</param>
    /// <param name="textcolor">of text label; in 0xrrggbb00 format</param>
    /// <returns>32 bpp rgb pixd</returns>
    /// <remarks>
    /// (1) If addlabel == 0, location and textcolor are ignored.
    /// (2) To make an array of color squares, use pixDisplayColorArray().
    /// </remarks>
    public static Pix MakeColorSquare(uint color, int size, int addlabel, int location,
                                      uint textcolor)
    {
        const string func = "pixMakeColorSquare";

        int w = (size <= 0) ? 100 : size;
        if (addlabel != 0 && w < 100) {
            Log.Warning(func, "size too small for label; omitting label\n");
            addlabel = 0;
        }

        Pix pix1 = Create(w, w, 32);
        pix1.SetAllArbitrary(color);
        if (addlabel == 0)
            return pix1;

            /* Adding text of color component values */
        if (location != L.L_ADD_ABOVE && location != L.L_ADD_AT_TOP &&
            location != L.L_ADD_AT_BOT && location != L.L_ADD_BELOW) {
            Log.Error(func, "invalid location: adding below\n");
            location = L.L_ADD_BELOW;
        }
        L_Bmf bmf = L_Bmf.Create(null, 4);
        ExtractRGBValues(color, out int rval, out int gval, out int bval);
        string buf = $"{rval},{gval},{bval}";
        return pix1.AddSingleTextblock(bmf, buf, textcolor, location, out _);
    }

    /*---------------------------------------------------------------------*
     *                Deprecated pix output for debugging                  *
     *---------------------------------------------------------------------*/

    // leptonica: pixDisplayWrite (writefile.c)
    /// <summary>Non-functional stub of pix output for debugging.</summary>
    /// <param name="reduction">ignored</param>
    /// <returns>1 (error)</returns>
    /// <remarks>As of 1.80, this is a non-functional stub.</remarks>
    public int DisplayWrite(int reduction)
    {
        CStreamText.Stderr("\n########################################################\n" +
                           "  pixDisplayWrite() was last used in tesseract 3.04," +
                           "  in Feb 2016.  As of 1.80, it is a non-functional stub\n" +
                           "########################################################" +
                           "\n\n\n");
        return 1;
    }
}

public sealed partial class Pixa
{
    // leptonica: pixaWriteFiles (writefile.c)
    /// <summary>Writes the pix of a pixa as numbered image files.</summary>
    /// <param name="rootname">root of the file names</param>
    /// <param name="pixa">the images</param>
    /// <param name="format">IFF_*; see notes for default</param>
    /// <remarks>
    /// (1) Use format = IFF_DEFAULT to decide the output format
    ///     individually for each pix.
    /// </remarks>
    public static void WriteFiles(string rootname, Pixa pixa, int format)
    {
        const string func = "pixaWriteFiles";

        if (format < 0 || format == L.IFF_UNKNOWN ||
            format >= L.ImageFileFormatExtensions.Length)
            throw Lept.Arg(func, "invalid format");

        int n = pixa.Count;
        for (int i = 0; i < n; i++) {
            Pix pix = pixa.GetPix(i, L.L_CLONE);
            int pixformat;
            if (format == L.IFF_DEFAULT)
                pixformat = pix.ChooseOutputFormat();
            else
                pixformat = format;
            string bigbuf = $"{rootname}{i:D3}.{L.ImageFileFormatExtensions[pixformat]}";
            if (bigbuf.Length > 511)
                bigbuf = bigbuf.Substring(0, 511);
            Pix.Write(bigbuf, pix, pixformat);
        }
    }
}
