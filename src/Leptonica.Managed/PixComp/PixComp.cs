using System;
using System.IO;
using System.Text;

namespace Leptonica;

/// <summary>Constants used by the serialization of compressed image arrays.</summary>
public static partial class L
{
    /// <summary>Version number of the serialized <see cref="Pixacomp"/> format.</summary>
    public const int PIXACOMP_VERSION_NUMBER = 2;
}

/*
 *  pixcomp.c: compressed pix and pixa.
 *
 *  PNG compression (plus uncompressed BMP/PNM/SPIX buffers on read) is fully
 *  supported; TIFF-G4, JPEG, and PDF codec calls remain stubs.
 */

/// <summary>Compressed Pix (struct PixComp).</summary>
public sealed class Pixcomp
{
    private const long MaxDataSize = 1000000000L;   /* 1 GB */

    private int w;
    private int h;
    private int d;
    private int xres;
    private int yres;
    private int comptype;
    private string? text;
    private int cmapflag;
    private byte[] data = Array.Empty<byte>();

    internal Pixcomp() { }

    internal Pixcomp(int w, int h, int d, int xres, int yres, int comptype,
                     string? text, int cmapflag, byte[] data)
    {
        this.w = w;
        this.h = h;
        this.d = d;
        this.xres = xres;
        this.yres = yres;
        this.comptype = comptype;
        this.text = text;
        this.cmapflag = cmapflag;
        this.data = data;
    }

    /// <summary>Gets the width of the original image in pixels.</summary>
    public int Width => w;
    /// <summary>Gets the height of the original image in pixels.</summary>
    public int Height => h;
    /// <summary>Gets the depth of the original image in bits per pixel.</summary>
    public int Depth => d;
    /// <summary>Gets the horizontal resolution in pixels per inch.</summary>
    public int XRes => xres;
    /// <summary>Gets the vertical resolution in pixels per inch.</summary>
    public int YRes => yres;
    /// <summary>Gets the compression format of the data, one of the <c>L.IFF_*</c> values.</summary>
    public int CompType => comptype;
    /// <summary>Gets the text string attached to the image, or <c>null</c> if there is none.</summary>
    public string? Text => text;
    /// <summary>Gets a flag that is 1 if the image has a colormap and 0 otherwise.</summary>
    public int CmapFlag => cmapflag;
    /// <summary>Gets the compressed image data.</summary>
    public byte[] Data => data;
    /// <summary>Gets the size of the compressed data in bytes.</summary>
    public int Size => data.Length;

    /*---------------------------------------------------------------------*
     *                  Pixcomp creation and destruction                   *
     *---------------------------------------------------------------------*/
    // leptonica: pixcompCreateFromPix (pixcomp.c)
    /// <summary>Creates a compressed image from an image.</summary>
    /// <param name="pix">The image to compress.</param>
    /// <param name="comptype">Requested format: <see cref="L.IFF_DEFAULT"/>, <see cref="L.IFF_TIFF_G4"/>, <see cref="L.IFF_PNG"/> or <see cref="L.IFF_JFIF_JPEG"/>.</param>
    /// <returns>The compressed image.</returns>
    /// <exception cref="ArgumentException"><paramref name="pix"/> is <c>null</c> or <paramref name="comptype"/> is invalid.</exception>
    /// <remarks>If the requested format cannot represent the image, the format is chosen by <see cref="DetermineFormat"/>.</remarks>
    public static Pixcomp CreateFromPix(Pix pix, int comptype)
    {
        const string func = "pixcompCreateFromPix";
        if (pix == null)
            throw Lept.Arg(func, "pix not defined");
        if (comptype != L.IFF_DEFAULT && comptype != L.IFF_TIFF_G4 &&
            comptype != L.IFF_PNG && comptype != L.IFF_JFIF_JPEG)
            throw Lept.Arg(func, "invalid comptype");

        Pixcomp pixc = new Pixcomp();
        pix.GetDimensions(out pixc.w, out pixc.h, out pixc.d);
        pix.GetResolution(out pixc.xres, out pixc.yres);
        if (pix.Colormap != null)
            pixc.cmapflag = 1;
        if (pix.Text != null)
            pixc.text = pix.Text;

        DetermineFormat(comptype, pixc.d, pixc.cmapflag, out int format);
        pixc.comptype = format;
        pixc.data = Pix.WriteMem(pix, format);
        return pixc;
    }

    // leptonica: pixcompCreateFromString (pixcomp.c)
    /// <summary>Creates a compressed image from an already compressed byte array.</summary>
    /// <param name="data">The compressed data; PNG, JPEG or TIFF-G4.</param>
    /// <param name="copyflag"><see cref="L.L_INSERT"/> to use the array directly, or <see cref="L.L_COPY"/> to store a copy of it.</param>
    /// <returns>The compressed image.</returns>
    /// <exception cref="ArgumentException"><paramref name="data"/> is <c>null</c> or <paramref name="copyflag"/> is invalid.</exception>
    public static Pixcomp CreateFromString(byte[] data, int copyflag)
    {
        const string func = "pixcompCreateFromString";
        if (data == null)
            throw Lept.Arg(func, "data not defined");
        if (copyflag != L.L_INSERT && copyflag != L.L_COPY)
            throw Lept.Arg(func, "invalid copyflag");

        Pix.ReadHeaderMem(data, out int format, out int w, out int h,
                          out int bps, out int spp, out bool iscmap);
        Pixcomp pixc = new Pixcomp();
        int d = (spp == 3) ? 32 : bps * spp;
        pixc.w = w;
        pixc.h = h;
        pixc.d = d;
        pixc.comptype = format;
        pixc.cmapflag = iscmap ? 1 : 0;
        if (copyflag == L.L_INSERT)
            pixc.data = data;
        else {
            byte[] copy = new byte[data.Length];
            Buffer.BlockCopy(data, 0, copy, 0, data.Length);
            pixc.data = copy;
        }
        return pixc;
    }

    // leptonica: pixcompCreateFromFile (pixcomp.c)
    /// <summary>Creates a compressed image from an image file.</summary>
    /// <param name="filename">Path of the image file.</param>
    /// <param name="comptype">Requested format: <see cref="L.IFF_DEFAULT"/>, <see cref="L.IFF_TIFF_G4"/>, <see cref="L.IFF_PNG"/> or <see cref="L.IFF_JFIF_JPEG"/>.</param>
    /// <returns>The compressed image.</returns>
    /// <exception cref="ArgumentException"><paramref name="filename"/> is <c>null</c> or <paramref name="comptype"/> is invalid.</exception>
    /// <remarks>If the file is already in an acceptable compressed format, its bytes are used directly; otherwise the image is read and recompressed.</remarks>
    public static Pixcomp CreateFromFile(string filename, int comptype)
    {
        const string func = "pixcompCreateFromFile";
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");
        if (comptype != L.IFF_DEFAULT && comptype != L.IFF_TIFF_G4 &&
            comptype != L.IFF_PNG && comptype != L.IFF_JFIF_JPEG)
            throw Lept.Arg(func, "invalid comptype");

        Pix.FindFileFormat(filename, out int format);
        if (format == L.IFF_UNKNOWN)
            throw Lept.Fail(func, $"unreadable file: {filename}");

            /* Can we accept the encoded file directly? */
        if ((format == L.IFF_TIFF_G4 && comptype != L.IFF_PNG) ||
            (format == L.IFF_JFIF_JPEG && comptype != L.IFF_PNG))
            comptype = format;
        if (comptype != L.IFF_DEFAULT && comptype == format) {
            byte[] data = Utils.BinaryRead(filename);
            return CreateFromString(data, L.L_INSERT);
        }

            /* Need to recompress in the default format */
        Pix pix = Pix.Read(filename);
        return CreateFromPix(pix, comptype);
    }

    // leptonica: pixcompCopy (pixcomp.c)
    /// <summary>Makes a deep copy of the compressed image.</summary>
    /// <returns>A new <see cref="Pixcomp"/> with its own copy of the data.</returns>
    public Pixcomp Copy()
    {
        const string func = "pixcompCopy";
        long size = data.LongLength;
        if (size > MaxDataSize)
            throw Lept.Fail(func, "size > 1 GB; too big");

        byte[] datad = new byte[data.Length];
        Buffer.BlockCopy(data, 0, datad, 0, data.Length);
        return new Pixcomp(w, h, d, xres, yres, comptype, text, cmapflag, datad);
    }

    /*---------------------------------------------------------------------*
     *                           Pixcomp accessors                         *
     *---------------------------------------------------------------------*/
    // leptonica: pixcompGetDimensions (pixcomp.c)
    /// <summary>Gets the width, height and depth of the original image.</summary>
    /// <param name="pw">Receives the width in pixels.</param>
    /// <param name="ph">Receives the height in pixels.</param>
    /// <param name="pd">Receives the depth in bits per pixel.</param>
    public void GetDimensions(out int pw, out int ph, out int pd)
    {
        pw = w;
        ph = h;
        pd = d;
    }

    // leptonica: pixcompGetParameters (pixcomp.c)
    /// <summary>Gets the resolution, compression type and colormap flag.</summary>
    /// <param name="pxres">Receives the horizontal resolution in pixels per inch.</param>
    /// <param name="pyres">Receives the vertical resolution in pixels per inch.</param>
    /// <param name="pcomptype">Receives the compression format, one of the <c>L.IFF_*</c> values.</param>
    /// <param name="pcmapflag">Receives 1 if the image has a colormap and 0 otherwise.</param>
    public void GetParameters(out int pxres, out int pyres, out int pcomptype, out int pcmapflag)
    {
        pxres = xres;
        pyres = yres;
        pcomptype = comptype;
        pcmapflag = cmapflag;
    }

    /*---------------------------------------------------------------------*
     *                    Pixcomp compression selection                    *
     *---------------------------------------------------------------------*/
    // leptonica: pixcompDetermineFormat (pixcomp.c)
    /// <summary>Chooses the compression format to use for an image of a given depth.</summary>
    /// <param name="comptype">Requested format: <see cref="L.IFF_DEFAULT"/>, <see cref="L.IFF_TIFF_G4"/>, <see cref="L.IFF_PNG"/> or <see cref="L.IFF_JFIF_JPEG"/>.</param>
    /// <param name="d">The image depth in bits per pixel.</param>
    /// <param name="cmapflag">1 if the image is to be compressed as a colormap image; 0 otherwise.</param>
    /// <param name="pformat">Receives the chosen format: <see cref="L.IFF_TIFF_G4"/>, <see cref="L.IFF_PNG"/> or <see cref="L.IFF_JFIF_JPEG"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="comptype"/> is invalid.</exception>
    /// <remarks>PNG is the fallback whenever the requested format cannot represent the image.</remarks>
    public static void DetermineFormat(int comptype, int d, int cmapflag, out int pformat)
    {
        const string func = "pixcompDetermineFormat";
        pformat = L.IFF_PNG;  /* init value and default */
        if (comptype != L.IFF_DEFAULT && comptype != L.IFF_TIFF_G4 &&
            comptype != L.IFF_PNG && comptype != L.IFF_JFIF_JPEG)
            throw Lept.Arg(func, "invalid comptype");

        if (comptype == L.IFF_DEFAULT) {
            if (d == 1)
                pformat = L.IFF_TIFF_G4;
            else if (d == 16)
                pformat = L.IFF_PNG;
            else if (d >= 8 && cmapflag == 0)
                pformat = L.IFF_JFIF_JPEG;
        } else if (comptype == L.IFF_TIFF_G4 && d == 1) {
            pformat = L.IFF_TIFF_G4;
        } else if (comptype == L.IFF_JFIF_JPEG && d >= 8 && cmapflag == 0) {
            pformat = L.IFF_JFIF_JPEG;
        }
    }

    /*--------------------------------------------------------------------*
     *                        Output for debugging                        *
     *--------------------------------------------------------------------*/
    // leptonica: pixcompWriteStreamInfo (pixcomp.c)
    /// <summary>Writes a description of the compressed image to a stream, for debugging.</summary>
    /// <param name="fp">The stream to write to.</param>
    /// <param name="text">Optional label for the output; may be <c>null</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="fp"/> is <c>null</c>.</exception>
    public void WriteStreamInfo(Stream fp, string? text)
    {
        const string func = "pixcompWriteStreamInfo";
        if (fp == null)
            throw Lept.Arg(func, "fp not defined");

        if (text != null)
            CStdio.Fprintf(fp, $"  Pixcomp Info for {text}:");
        else
            CStdio.Fprintf(fp, $"  Pixcomp Info:");
        CStdio.Fprintf(fp, $" width = {w}, height = {h}, depth = {d}\n");
        CStdio.Fprintf(fp, $"    xres = {xres}, yres = {yres}, size in bytes = {data.Length}\n");
        if (cmapflag != 0)
            CStdio.Fprintf(fp, $"    has colormap\n");
        else
            CStdio.Fprintf(fp, $"    no colormap\n");
        if (comptype >= 0 && comptype < Leptonica.WriteFile.NumImageFileFormatExtensions) {
            CStdio.Fprintf(fp, $"    comptype = {L.ImageFileFormatExtensions[comptype]} ({comptype})\n");
        } else {
            CStdio.Fprintf(fp, $"    Error!! Invalid comptype index: {comptype}\n");
        }
    }

    // leptonica: pixcompWriteFile (pixcomp.c)
    /// <summary>Writes the compressed data to a file, with an extension that matches the compression format.</summary>
    /// <param name="rootname">Root of the output filename; the extension is appended.</param>
    /// <exception cref="ArgumentException"><paramref name="rootname"/> is <c>null</c>.</exception>
    public void WriteFile(string rootname)
    {
        const string func = "pixcompWriteFile";
        if (rootname == null)
            throw Lept.Arg(func, "rootname not defined");

        string buf = $"{rootname}.{L.ImageFileFormatExtensions[comptype]}";
        Utils.BinaryWrite(buf, "w", data);
    }
}

public sealed partial class Pix
{
    // leptonica: pixCreateFromPixcomp (pixcomp.c)
    /// <summary>Decompresses a compressed image into a regular image.</summary>
    /// <param name="pixc">The compressed image.</param>
    /// <returns>The decompressed image, with resolution and text restored.</returns>
    /// <exception cref="ArgumentException"><paramref name="pixc"/> is <c>null</c>.</exception>
    public static Pix CreateFromPixcomp(Pixcomp pixc)
    {
        const string func = "pixCreateFromPixcomp";
        if (pixc == null)
            throw Lept.Arg(func, "pixc not defined");

        Pix pix = ReadMem(pixc.Data);
        pix.SetResolution(pixc.XRes, pixc.YRes);
        if (pixc.Text != null)
            pix.Text = pixc.Text;

            /* Check fields for consistency */
        pix.GetDimensions(out int w, out int h, out int d);
        if (pixc.Width != w) {
            Log.Info(func, $"pix width {w} != pixc width {pixc.Width}");
            Log.Error(func, $"pix width {w} != pixc width");
        }
        if (pixc.Height != h)
            Log.Error(func, $"pix height {h} != pixc height");
        if (pixc.Depth != d) {
            if (pixc.Depth == 16)  /* we strip 16 --> 8 bpp by default */
                Log.Warning(func, $"pix depth {d} != pixc depth 16");
            else
                Log.Error(func, $"pix depth {d} != pixc depth");
        }
        bool cmapinpix = pix.Colormap != null;
        if ((cmapinpix && pixc.CmapFlag == 0) || (!cmapinpix && pixc.CmapFlag != 0))
            Log.Error(func, "pix cmap flag inconsistent");
        int format = pix.InputFormat;
        if (format != pixc.CompType)
            Log.Error(func, $"pix comptype {format} not equal to pixc comptype");

        return pix;
    }
}

/// <summary>Array of compressed pix (struct PixaComp).</summary>
public sealed partial class Pixacomp
{
    private const int MaxPtrArraySize = 1000000;
    private const int InitialPtrArraySize = 20;      /* n'importe quoi */
    private const long MaxDataSize = 1000000000L;    /* 1 GB */

    private int n;
    private int nalloc;
    private int offset;
    private Pixcomp?[] pixc;
    private Boxa boxa;

    private Pixacomp(int nalloc)
    {
        this.n = 0;
        this.nalloc = nalloc;
        this.offset = 0;
        this.pixc = new Pixcomp?[nalloc];
        this.boxa = Boxa.Create(nalloc);
    }

    /*---------------------------------------------------------------------*
     *                Pixacomp creation and destruction                    *
     *---------------------------------------------------------------------*/
    // leptonica: pixacompCreate (pixcomp.c)
    /// <summary>Creates an empty array of compressed images.</summary>
    /// <param name="n">Initial capacity of the array; a default is used if the value is not positive or too large.</param>
    /// <returns>The new, empty <see cref="Pixacomp"/>.</returns>
    public static Pixacomp Create(int n)
    {
        if (n <= 0 || n > MaxPtrArraySize)
            n = InitialPtrArraySize;
        return new Pixacomp(n);
    }

    // leptonica: pixacompCreateWithInit (pixcomp.c)
    /// <summary>Creates an array of compressed images in which every element is initialized to the same image.</summary>
    /// <param name="n">Number of compressed images to create.</param>
    /// <param name="offset">Offset applied to the indices used to access the array; a negative value is set to 0.</param>
    /// <param name="pix">Image used to initialize every element; if <c>null</c>, a 1 x 1 image with 1 bpp is used.</param>
    /// <param name="comptype">Format used for the initial images: <see cref="L.IFF_DEFAULT"/>, <see cref="L.IFF_TIFF_G4"/>, <see cref="L.IFF_PNG"/> or <see cref="L.IFF_JFIF_JPEG"/>.</param>
    /// <returns>The new <see cref="Pixacomp"/> with <paramref name="n"/> elements.</returns>
    /// <exception cref="ArgumentException"><paramref name="n"/> is out of range or <paramref name="comptype"/> is invalid.</exception>
    /// <remarks>
    /// This is useful for managing an array of images by index when only some elements are ever replaced
    /// with real content.
    /// </remarks>
    public static Pixacomp CreateWithInit(int n, int offset, Pix? pix, int comptype)
    {
        const string func = "pixacompCreateWithInit";
        if (n <= 0 || n > MaxPtrArraySize)
            throw Lept.Arg(func, "n out of valid bounds");
        if (pix != null) {
            if (comptype != L.IFF_DEFAULT && comptype != L.IFF_TIFF_G4 &&
                comptype != L.IFF_PNG && comptype != L.IFF_JFIF_JPEG)
                throw Lept.Arg(func, "invalid comptype");
        } else {
            comptype = L.IFF_TIFF_G4;
        }
        if (offset < 0) {
            Log.Warning(func, "offset < 0; setting to 0");
            offset = 0;
        }

        Pixacomp pixac = Create(n);
        pixac.Offset = offset;
        Pix pixt = pix != null ? Pix.Clone(pix) : Pix.Create(1, 1, 1);
        for (int i = 0; i < n; i++) {
            Pixcomp pc = Pixcomp.CreateFromPix(pixt, comptype);
            pixac.AddPixcomp(pc, L.L_INSERT);
        }

        return pixac;
    }

    // leptonica: pixacompCreateFromPixa (pixcomp.c)
    /// <summary>Creates an array of compressed images from an array of images.</summary>
    /// <param name="pixa">The source images.</param>
    /// <param name="comptype">Format used for compression: <see cref="L.IFF_DEFAULT"/>, <see cref="L.IFF_TIFF_G4"/>, <see cref="L.IFF_PNG"/> or <see cref="L.IFF_JFIF_JPEG"/>.</param>
    /// <param name="accesstype">How the boxes are taken from <paramref name="pixa"/>: <see cref="L.L_COPY"/>, <see cref="L.L_CLONE"/> or <see cref="L.L_COPY_CLONE"/>.</param>
    /// <returns>The new <see cref="Pixacomp"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="pixa"/> is <c>null</c>, or <paramref name="comptype"/> or <paramref name="accesstype"/> is invalid.</exception>
    public static Pixacomp CreateFromPixa(Pixa pixa, int comptype, int accesstype)
    {
        const string func = "pixacompCreateFromPixa";
        if (pixa == null)
            throw Lept.Arg(func, "pixa not defined");
        if (comptype != L.IFF_DEFAULT && comptype != L.IFF_TIFF_G4 &&
            comptype != L.IFF_PNG && comptype != L.IFF_JFIF_JPEG)
            throw Lept.Arg(func, "invalid comptype");
        if (accesstype != L.L_COPY && accesstype != L.L_CLONE &&
            accesstype != L.L_COPY_CLONE)
            throw Lept.Arg(func, "invalid accesstype");

        int n = pixa.Count;
        Pixacomp pixac = Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix = pixa.GetPix(i, L.L_CLONE);
            pixac.AddPix(pix, comptype);
        }
        Boxa? ba = pixa.GetBoxa(accesstype);
        if (ba != null)
            pixac.boxa = ba;

        return pixac;
    }

    // leptonica: pixacompCreateFromFiles (pixcomp.c)
    /// <summary>Creates an array of compressed images from the image files in a directory.</summary>
    /// <param name="dirname">The directory to read.</param>
    /// <param name="substr">Required substring of the file names; <c>null</c> to use all files.</param>
    /// <param name="comptype">Format used for compression: <see cref="L.IFF_DEFAULT"/>, <see cref="L.IFF_TIFF_G4"/>, <see cref="L.IFF_PNG"/> or <see cref="L.IFF_JFIF_JPEG"/>.</param>
    /// <returns>The new <see cref="Pixacomp"/>, with files in sorted order.</returns>
    /// <exception cref="ArgumentException"><paramref name="dirname"/> is <c>null</c> or <paramref name="comptype"/> is invalid.</exception>
    public static Pixacomp CreateFromFiles(string dirname, string? substr, int comptype)
    {
        const string func = "pixacompCreateFromFiles";
        if (dirname == null)
            throw Lept.Arg(func, "dirname not defined");
        if (comptype != L.IFF_DEFAULT && comptype != L.IFF_TIFF_G4 &&
            comptype != L.IFF_PNG && comptype != L.IFF_JFIF_JPEG)
            throw Lept.Arg(func, "invalid comptype");

        Sarray sa = Sarray.GetSortedPathnamesInDirectory(dirname, substr, 0, 0);
        return CreateFromSA(sa, comptype);
    }

    // leptonica: pixacompCreateFromSA (pixcomp.c)
    /// <summary>Creates an array of compressed images from a list of image file names.</summary>
    /// <param name="sa">The file names; files that cannot be read are skipped with an error message.</param>
    /// <param name="comptype">Format used for compression: <see cref="L.IFF_DEFAULT"/>, <see cref="L.IFF_TIFF_G4"/>, <see cref="L.IFF_PNG"/> or <see cref="L.IFF_JFIF_JPEG"/>.</param>
    /// <returns>The new <see cref="Pixacomp"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="sa"/> is <c>null</c> or <paramref name="comptype"/> is invalid.</exception>
    public static Pixacomp CreateFromSA(Sarray sa, int comptype)
    {
        const string func = "pixacompCreateFromSA";
        if (sa == null)
            throw Lept.Arg(func, "sarray not defined");
        if (comptype != L.IFF_DEFAULT && comptype != L.IFF_TIFF_G4 &&
            comptype != L.IFF_PNG && comptype != L.IFF_JFIF_JPEG)
            throw Lept.Arg(func, "invalid comptype");

        int count = sa.Count;
        Pixacomp pixac = Create(count);
        for (int i = 0; i < count; i++) {
            string str = sa.GetString(i, L.L_NOCOPY);
            Pixcomp pc;
            try {
                pc = Pixcomp.CreateFromFile(str, comptype);
            } catch (Exception e) when (e is ArgumentException || e is LeptonicaException || e is IOException) {
                Log.Error(func, $"pixc not read from file: {str}");
                continue;
            }
            pixac.AddPixcomp(pc, L.L_INSERT);
        }
        return pixac;
    }

    /*---------------------------------------------------------------------*
     *                          Pixacomp addition                          *
     *---------------------------------------------------------------------*/
    // leptonica: pixacompAddPix (pixcomp.c)
    /// <summary>Compresses an image and appends it to the array.</summary>
    /// <param name="pix">The image to add.</param>
    /// <param name="comptype">Requested format: <see cref="L.IFF_DEFAULT"/>, <see cref="L.IFF_TIFF_G4"/>, <see cref="L.IFF_PNG"/> or <see cref="L.IFF_JFIF_JPEG"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="pix"/> is <c>null</c> or <paramref name="comptype"/> is invalid.</exception>
    public void AddPix(Pix pix, int comptype)
    {
        const string func = "pixacompAddPix";
        if (pix == null)
            throw Lept.Arg(func, "pix not defined");
        if (comptype != L.IFF_DEFAULT && comptype != L.IFF_TIFF_G4 &&
            comptype != L.IFF_PNG && comptype != L.IFF_JFIF_JPEG)
            throw Lept.Arg(func, "invalid format");

        int cmapflag = pix.Colormap != null ? 1 : 0;
        Pixcomp.DetermineFormat(comptype, pix.Depth, cmapflag, out int format);
        Pixcomp pc = Pixcomp.CreateFromPix(pix, format);
        AddPixcomp(pc, L.L_INSERT);
    }

    // leptonica: pixacompAddPixcomp (pixcomp.c)
    /// <summary>Appends a compressed image to the array.</summary>
    /// <param name="pixc">The compressed image to add.</param>
    /// <param name="copyflag"><see cref="L.L_INSERT"/> to store the object itself, or <see cref="L.L_COPY"/> to store a copy.</param>
    /// <exception cref="ArgumentException"><paramref name="pixc"/> is <c>null</c> or <paramref name="copyflag"/> is invalid.</exception>
    public void AddPixcomp(Pixcomp pixc, int copyflag)
    {
        const string func = "pixacompAddPixcomp";
        if (pixc == null)
            throw Lept.Arg(func, "pixc not defined");
        if (copyflag != L.L_INSERT && copyflag != L.L_COPY)
            throw Lept.Arg(func, "invalid copyflag");

        if (n >= nalloc)
            ExtendArray();

        if (copyflag == L.L_INSERT)
            this.pixc[n] = pixc;
        else  /* L_COPY */
            this.pixc[n] = pixc.Copy();
        n++;
    }

    // leptonica: pixacompExtendArray (pixcomp.c)
    private void ExtendArray()
    {
        const string func = "pixacompExtendArray";
        if (nalloc > MaxPtrArraySize)
            throw Lept.Fail(func, "pixac has too many ptrs");
        long newNalloc = 2L * nalloc;
        if (newNalloc > MaxPtrArraySize)
            throw Lept.Fail(func, "newsize > 8 MB; too large");

        Array.Resize(ref pixc, (int)newNalloc);
        nalloc = (int)newNalloc;
        boxa.ExtendArray();
    }

    // leptonica: pixacompReplacePix (pixcomp.c)
    /// <summary>Compresses an image and uses it to replace an existing element of the array.</summary>
    /// <param name="index">Index of the element to replace, taking <see cref="Offset"/> into account.</param>
    /// <param name="pix">The replacement image.</param>
    /// <param name="comptype">Requested format: <see cref="L.IFF_DEFAULT"/>, <see cref="L.IFF_TIFF_G4"/>, <see cref="L.IFF_PNG"/> or <see cref="L.IFF_JFIF_JPEG"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="index"/> is out of range, <paramref name="pix"/> is <c>null</c>, or <paramref name="comptype"/> is invalid.</exception>
    public void ReplacePix(int index, Pix pix, int comptype)
    {
        const string func = "pixacompReplacePix";
        int aindex = index - offset;
        if (aindex < 0 || aindex >= n)
            throw Lept.Arg(func, "array index out of bounds");
        if (pix == null)
            throw Lept.Arg(func, "pix not defined");
        if (comptype != L.IFF_DEFAULT && comptype != L.IFF_TIFF_G4 &&
            comptype != L.IFF_PNG && comptype != L.IFF_JFIF_JPEG)
            throw Lept.Arg(func, "invalid format");

        Pixcomp pc = Pixcomp.CreateFromPix(pix, comptype);
        ReplacePixcomp(index, pc);
    }

    // leptonica: pixacompReplacePixcomp (pixcomp.c)
    /// <summary>Replaces an existing element of the array with a compressed image.</summary>
    /// <param name="index">Index of the element to replace, taking <see cref="Offset"/> into account.</param>
    /// <param name="pixc">The replacement compressed image; the array takes ownership of it.</param>
    /// <exception cref="ArgumentException"><paramref name="index"/> is out of range or <paramref name="pixc"/> is <c>null</c>.</exception>
    public void ReplacePixcomp(int index, Pixcomp pixc)
    {
        const string func = "pixacompReplacePixcomp";
        int aindex = index - offset;
        if (aindex < 0 || aindex >= n)
            throw Lept.Arg(func, "array index out of bounds");
        if (pixc == null)
            throw Lept.Arg(func, "pixc not defined");

        this.pixc[aindex] = pixc;
    }

    // leptonica: pixacompAddBox (pixcomp.c)
    /// <summary>Appends a box to the array of boxes held with the compressed images.</summary>
    /// <param name="box">The box to add.</param>
    /// <param name="copyflag"><see cref="L.L_INSERT"/> to store the object itself, or <see cref="L.L_COPY"/> to store a copy.</param>
    /// <exception cref="ArgumentException"><paramref name="box"/> is <c>null</c> or <paramref name="copyflag"/> is invalid.</exception>
    public void AddBox(Box box, int copyflag)
    {
        const string func = "pixacompAddBox";
        if (box == null)
            throw Lept.Arg(func, "box not defined");
        if (copyflag != L.L_INSERT && copyflag != L.L_COPY)
            throw Lept.Arg(func, "invalid copyflag");

        boxa.AddBox(box, copyflag);
    }

    /*---------------------------------------------------------------------*
     *                         Pixacomp accessors                          *
     *---------------------------------------------------------------------*/
    // leptonica: pixacompGetCount (pixcomp.c)
    /// <summary>Gets the number of compressed images in the array.</summary>
    public int Count => n;

    // leptonica: pixacompGetPixcomp (pixcomp.c)
    /// <summary>Gets one compressed image from the array.</summary>
    /// <param name="index">Index of the element, taking <see cref="Offset"/> into account.</param>
    /// <param name="copyflag"><see cref="L.L_NOCOPY"/> to return the stored object, or <see cref="L.L_COPY"/> to return a copy.</param>
    /// <returns>The compressed image.</returns>
    /// <exception cref="ArgumentException"><paramref name="index"/> is out of range or <paramref name="copyflag"/> is invalid.</exception>
    public Pixcomp GetPixcomp(int index, int copyflag)
    {
        const string func = "pixacompGetPixcomp";
        if (copyflag != L.L_NOCOPY && copyflag != L.L_COPY)
            throw Lept.Arg(func, "invalid copyflag");
        int aindex = index - offset;
        if (aindex < 0 || aindex >= n)
            throw Lept.Arg(func, "array index not valid");

        Pixcomp pc = pixc[aindex]!;
        return copyflag == L.L_NOCOPY ? pc : pc.Copy();
    }

    // leptonica: pixacompGetPix (pixcomp.c)
    /// <summary>Decompresses one element of the array into an image.</summary>
    /// <param name="index">Index of the element, taking <see cref="Offset"/> into account.</param>
    /// <returns>The decompressed image.</returns>
    /// <exception cref="ArgumentException"><paramref name="index"/> is out of range.</exception>
    public Pix GetPix(int index)
    {
        const string func = "pixacompGetPix";
        int aindex = index - offset;
        if (aindex < 0 || aindex >= n)
            throw Lept.Arg(func, "array index not valid");

        Pixcomp pc = GetPixcomp(index, L.L_NOCOPY);
        return Pix.CreateFromPixcomp(pc);
    }

    // leptonica: pixacompGetPixDimensions (pixcomp.c)
    /// <summary>Gets the width, height and depth of one element without decompressing it.</summary>
    /// <param name="index">Index of the element, taking <see cref="Offset"/> into account.</param>
    /// <param name="pw">Receives the width in pixels.</param>
    /// <param name="ph">Receives the height in pixels.</param>
    /// <param name="pd">Receives the depth in bits per pixel.</param>
    /// <exception cref="ArgumentException"><paramref name="index"/> is out of range.</exception>
    public void GetPixDimensions(int index, out int pw, out int ph, out int pd)
    {
        const string func = "pixacompGetPixDimensions";
        int aindex = index - offset;
        if (aindex < 0 || aindex >= n)
            throw Lept.Arg(func, "array index not valid");

        Pixcomp? pc = pixc[aindex];
        if (pc == null)
            throw Lept.Fail(func, "pixc not found!");
        pc.GetDimensions(out pw, out ph, out pd);
    }

    // leptonica: pixacompGetBoxa (pixcomp.c)
    /// <summary>Gets the array of boxes held with the compressed images.</summary>
    /// <param name="accesstype"><see cref="L.L_COPY"/>, <see cref="L.L_CLONE"/> or <see cref="L.L_COPY_CLONE"/>.</param>
    /// <returns>The box array, as selected by <paramref name="accesstype"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="accesstype"/> is invalid.</exception>
    public Boxa GetBoxa(int accesstype)
    {
        const string func = "pixacompGetBoxa";
        if (accesstype != L.L_COPY && accesstype != L.L_CLONE &&
            accesstype != L.L_COPY_CLONE)
            throw Lept.Arg(func, "invalid accesstype");

        return boxa.Copy(accesstype);
    }

    // leptonica: pixacompGetBoxaCount (pixcomp.c)
    /// <summary>Gets the number of boxes held with the compressed images.</summary>
    public int BoxaCount => boxa.Count;

    // leptonica: pixacompGetBox (pixcomp.c)
    /// <summary>Gets one box from the array of boxes.</summary>
    /// <param name="index">Index of the box, taking <see cref="Offset"/> into account.</param>
    /// <param name="accesstype"><see cref="L.L_COPY"/> or <see cref="L.L_CLONE"/>.</param>
    /// <returns>A copy or clone of the box, or <c>null</c> if there is no box at that position.</returns>
    /// <exception cref="ArgumentException"><paramref name="index"/> is out of range or <paramref name="accesstype"/> is invalid.</exception>
    public Box? GetBox(int index, int accesstype)
    {
        const string func = "pixacompGetBox";
        int aindex = index - offset;
        if (aindex < 0 || aindex >= boxa.Count)
            throw Lept.Arg(func, "array index not valid");
        if (accesstype != L.L_COPY && accesstype != L.L_CLONE)
            throw Lept.Arg(func, "invalid accesstype");

        Box? box = boxa.PeekBox(aindex);
        if (box != null) {
            return accesstype == L.L_COPY ? box.Copy() : box.Clone();
        }
        return null;
    }

    // leptonica: pixacompGetBoxGeometry (pixcomp.c)
    /// <summary>Gets the location and size of one box.</summary>
    /// <param name="index">Index of the box, taking <see cref="Offset"/> into account.</param>
    /// <param name="px">Receives the left coordinate.</param>
    /// <param name="py">Receives the top coordinate.</param>
    /// <param name="pw">Receives the width.</param>
    /// <param name="ph">Receives the height.</param>
    /// <exception cref="ArgumentException"><paramref name="index"/> is out of range.</exception>
    public void GetBoxGeometry(int index, out int px, out int py, out int pw, out int ph)
    {
        const string func = "pixacompGetBoxGeometry";
        int aindex = index - offset;
        if (aindex < 0 || aindex >= n)
            throw Lept.Arg(func, "array index not valid");

        // NOTE(upstream): passes aindex instead of index to pixacompGetBox,
        // which subtracts offset a second time when offset != 0.
        Box? box = GetBox(aindex, L.L_CLONE);
        if (box == null)
            throw Lept.Fail(func, "box not found!");
        box.GetGeometry(out px, out py, out pw, out ph);
    }

    // leptonica: pixacompGetOffset, pixacompSetOffset (pixcomp.c)
    /// <summary>Gets or sets the offset subtracted from the indices used to access the array.</summary>
    /// <remarks>
    /// The offset makes it possible to address the elements by an external index, for example a page number
    /// that starts above zero. A negative value is stored as 0.
    /// </remarks>
    public int Offset
    {
        get => offset;
        set => offset = Math.Max(0, value);
    }

    /*---------------------------------------------------------------------*
     *                         Combining pixacomp                          *
     *---------------------------------------------------------------------*/
    // leptonica: pixacompJoin (pixcomp.c)
    /// <summary>Appends a range of elements from another array to this array.</summary>
    /// <param name="pixacs">The source array; if <c>null</c> or empty, nothing is done.</param>
    /// <param name="istart">First source index to copy; a negative value is treated as 0.</param>
    /// <param name="iend">Last source index to copy; a negative value or one past the end means the last element.</param>
    /// <exception cref="ArgumentException"><paramref name="istart"/> is greater than <paramref name="iend"/>.</exception>
    /// <remarks>The compressed images and their boxes are copied.</remarks>
    public void Join(Pixacomp? pixacs, int istart, int iend)
    {
        const string func = "pixacompJoin";
        int ns;
        if (pixacs == null || (ns = pixacs.Count) == 0)
            return;

        if (istart < 0)
            istart = 0;
        if (iend < 0 || iend >= ns)
            iend = ns - 1;
        if (istart > iend)
            throw Lept.Arg(func, "istart > iend; nothing to add");

        for (int i = istart; i <= iend; i++) {
            Pixcomp pc = pixacs.GetPixcomp(i, L.L_NOCOPY);
            AddPixcomp(pc, L.L_COPY);
        }

        Boxa boxas = pixacs.GetBoxa(L.L_CLONE);
        Boxa boxad = GetBoxa(L.L_CLONE);
        int nb = pixacs.BoxaCount;
        iend = Math.Min(iend, nb - 1);
        boxad.Join(boxas, istart, iend);
    }

    // leptonica: pixacompInterleave (pixcomp.c)
    /// <summary>Interleaves the elements of two arrays, alternating between them.</summary>
    /// <param name="pixac1">The first array; supplies the even-numbered elements of the result.</param>
    /// <param name="pixac2">The second array; supplies the odd-numbered elements of the result.</param>
    /// <returns>A new array with copies of the interleaved elements and boxes.</returns>
    /// <exception cref="ArgumentException">An argument is <c>null</c>.</exception>
    /// <remarks>If the arrays differ in size, the interleaving stops at the end of the shorter one.</remarks>
    public static Pixacomp Interleave(Pixacomp pixac1, Pixacomp pixac2)
    {
        const string func = "pixacompInterleave";
        if (pixac1 == null)
            throw Lept.Arg(func, "pixac1 not defined");
        if (pixac2 == null)
            throw Lept.Arg(func, "pixac2 not defined");
        int n1 = pixac1.Count;
        int n2 = pixac2.Count;
        int n = Math.Min(n1, n2);
        if (n == 0)
            throw Lept.Fail(func, "at least one input pixac is empty");
        if (n1 != n2)
            Log.Warning(func, $"counts differ: {n1} != {n2}");

        Pixacomp pixacd = Create(2 * n);
        int nb1 = pixac1.BoxaCount;
        int nb2 = pixac2.BoxaCount;
        for (int i = 0; i < n; i++) {
            Pixcomp pixc1 = pixac1.GetPixcomp(i, L.L_COPY);
            pixacd.AddPixcomp(pixc1, L.L_INSERT);
            if (i < nb1) {
                Box? box = pixac1.GetBox(i, L.L_COPY);
                if (box != null)
                    pixacd.AddBox(box, L.L_INSERT);
            }
            Pixcomp pixc2 = pixac2.GetPixcomp(i, L.L_COPY);
            pixacd.AddPixcomp(pixc2, L.L_INSERT);
            if (i < nb2) {
                Box? box = pixac2.GetBox(i, L.L_COPY);
                if (box != null)
                    pixacd.AddBox(box, L.L_INSERT);
            }
        }

        return pixacd;
    }

    /*---------------------------------------------------------------------*
     *                       Pixacomp serialized I/O                       *
     *---------------------------------------------------------------------*/
    // leptonica: pixacompRead (pixcomp.c)
    /// <summary>Reads a serialized array of compressed images from a file.</summary>
    /// <param name="filename">Path of the file to read.</param>
    /// <returns>The deserialized <see cref="Pixacomp"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="filename"/> is <c>null</c>.</exception>
    public static Pixacomp Read(string filename)
    {
        const string func = "pixacompRead";
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");

        using FileStream fp = Utils.FopenReadStream(filename);
        return ReadStream(fp);
    }

    // leptonica: pixacompReadStream (pixcomp.c)
    /// <summary>Reads a serialized array of compressed images from a stream.</summary>
    /// <param name="fp">The stream to read.</param>
    /// <returns>The deserialized <see cref="Pixacomp"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="fp"/> is <c>null</c> or the data are not a valid serialization.</exception>
    public static Pixacomp ReadStream(Stream fp)
    {
        const string func = "pixacompReadStream";
        if (fp == null)
            throw Lept.Arg(func, "stream not defined");

        fp = Pixa.Seekable(fp);
        CScanner sc = new CScanner(fp);
        Span<int> v = stackalloc int[4];
        int n, offset;
        Boxa boxa;
        try {
            if (sc.Scan("\nPixacomp Version %d\n", v) != 1)
                throw Lept.Fail(func, "not a pixacomp file");
            int version = v[0];
            if (version != L.PIXACOMP_VERSION_NUMBER)
                throw Lept.Fail(func, "invalid pixacomp version");
            if (sc.Scan("Number of pixcomp = %d\n", v) != 1)
                throw Lept.Fail(func, "not a pixacomp file");
            n = v[0];
            if (sc.Scan("Offset of index into array = %d", v) != 1)
                throw Lept.Fail(func, "offset not read");
            offset = v[0];
            if (n < 0)
                throw Lept.Fail(func, "num pixcomp ptrs < 0");
            if (n > MaxPtrArraySize)
                throw Lept.Fail(func, "too many pixcomp ptrs");
            if (n == 0)
                Log.Info(func, "the pixacomp is empty");

            boxa = Boxa.ReadStream(sc);
        } finally {
            sc.Release();
        }

        Pixacomp pixac = Create(n);
        pixac.boxa = boxa;
        pixac.Offset = offset;

        for (int i = 0; i < n; i++) {
            if (!ScanPixcompDims(fp, out _, out int w, out int h, out int d))
                throw Lept.Fail(func, "dimension reading");
            if (!ScanPixcompCompSize(fp, out int comptype, out ulong size64, out int cmapflag, out int pushedAfter))
                throw Lept.Fail(func, "comptype/size reading");
            if (size64 > (ulong)MaxDataSize)
                throw Lept.Fail(func, $"data size = {size64} is too big");

                /* fgets(buf, sizeof(buf), fp): stop right after '\n' so binary whitespace is preserved */
            string? line = FgetsWithPushback(fp, pushedAfter, 256);
            if (line == null)
                throw Lept.Fail(func, "fgets read fail");
            if (!ParseResLine(line, out int xres, out int yres))
                throw Lept.Fail(func, "read fail for res");

            int size = (int)size64;
            byte[] data = new byte[size];
            if (fp.ReadAtLeast(data, size, false) != size)
                throw Lept.Fail(func, "error reading data");
            fp.ReadByte();  /* swallow the ending nl */

            Pixcomp pc = new Pixcomp(w, h, d, xres, yres, comptype, null, cmapflag, data);
            pixac.AddPixcomp(pc, L.L_INSERT);
        }
        return pixac;
    }

    private static bool IsAsciiSpace(int c) => c == ' ' || (c >= '\t' && c <= '\r');

    private static bool ScanPixcompDims(Stream fp, out int idx, out int w, out int h, out int d)
    {
        idx = w = h = d = 0;
        int push = -2;
        if (!SkipSpaceAndMatch(fp, ref push, "Pixcomp[")) return false;
        if (!ScanInt(fp, ref push, out idx)) return false;
        if (!MatchLiteral(fp, ref push, "]:")) return false;
        if (!SkipSpaceAndMatch(fp, ref push, "w")) return false;
        if (!SkipSpaceAndMatch(fp, ref push, "=")) return false;
        if (!ScanInt(fp, ref push, out w)) return false;
        if (!MatchLiteral(fp, ref push, ",")) return false;
        if (!SkipSpaceAndMatch(fp, ref push, "h")) return false;
        if (!SkipSpaceAndMatch(fp, ref push, "=")) return false;
        if (!ScanInt(fp, ref push, out h)) return false;
        if (!MatchLiteral(fp, ref push, ",")) return false;
        if (!SkipSpaceAndMatch(fp, ref push, "d")) return false;
        if (!SkipSpaceAndMatch(fp, ref push, "=")) return false;
        if (!ScanInt(fp, ref push, out d)) return false;
        int c = ReadCh(fp, ref push);
        while (IsAsciiSpace(c))
            c = ReadCh(fp, ref push);
        if (c >= 0 && fp.CanSeek)
            fp.Seek(-1, SeekOrigin.Current);
        return true;
    }

    private static bool ScanPixcompCompSize(Stream fp, out int comptype, out ulong size,
                                            out int cmapflag, out int pushedOut)
    {
        comptype = cmapflag = 0;
        size = 0;
        pushedOut = -2;
        int push = -2;
        if (!SkipSpaceAndMatch(fp, ref push, "comptype")) return false;
        if (!SkipSpaceAndMatch(fp, ref push, "=")) return false;
        if (!ScanInt(fp, ref push, out comptype)) return false;
        if (!MatchLiteral(fp, ref push, ",")) return false;
        if (!SkipSpaceAndMatch(fp, ref push, "size")) return false;
        if (!SkipSpaceAndMatch(fp, ref push, "=")) return false;
        if (!ScanUInt64(fp, ref push, out size)) return false;
        if (!MatchLiteral(fp, ref push, ",")) return false;
        if (!SkipSpaceAndMatch(fp, ref push, "cmapflag")) return false;
        if (!SkipSpaceAndMatch(fp, ref push, "=")) return false;
        if (!ScanInt(fp, ref push, out cmapflag)) return false;
        int c = ReadCh(fp, ref push);
        while (IsAsciiSpace(c))
            c = ReadCh(fp, ref push);
        pushedOut = c;  /* first non-space char of the fgets line (or -1 EOF) */
        return true;
    }

    private static int ReadCh(Stream fp, ref int push)
    {
        if (push != -2) {
            int c = push;
            push = -2;
            return c;
        }
        return fp.ReadByte();
    }

    private static bool MatchLiteral(Stream fp, ref int push, string lit)
    {
        for (int i = 0; i < lit.Length; i++) {
            int c = ReadCh(fp, ref push);
            if (c != lit[i])
                return false;
        }
        return true;
    }

    private static bool SkipSpaceAndMatch(Stream fp, ref int push, string lit)
    {
        int c = ReadCh(fp, ref push);
        while (IsAsciiSpace(c))
            c = ReadCh(fp, ref push);
        if (c < 0 || c != lit[0])
            return false;
        for (int i = 1; i < lit.Length; i++) {
            c = ReadCh(fp, ref push);
            if (c != lit[i])
                return false;
        }
        return true;
    }

    private static bool ScanInt(Stream fp, ref int push, out int val)
    {
        val = 0;
        int c = ReadCh(fp, ref push);
        while (IsAsciiSpace(c))
            c = ReadCh(fp, ref push);
        if (c < 0) return false;
        bool neg = false;
        if (c == '+' || c == '-') {
            neg = c == '-';
            c = ReadCh(fp, ref push);
        }
        if (c < '0' || c > '9') return false;
        long acc = 0;
        while (c >= '0' && c <= '9') {
            acc = acc * 10 + (c - '0');
            c = ReadCh(fp, ref push);
        }
        push = c;
        val = (int)(neg ? -acc : acc);
        return true;
    }

    private static bool ScanUInt64(Stream fp, ref int push, out ulong val)
    {
        val = 0;
        int c = ReadCh(fp, ref push);
        while (IsAsciiSpace(c))
            c = ReadCh(fp, ref push);
        if (c < '0' || c > '9') return false;
        ulong acc = 0;
        while (c >= '0' && c <= '9') {
            acc = acc * 10UL + (ulong)(c - '0');
            c = ReadCh(fp, ref push);
        }
        push = c;
        val = acc;
        return true;
    }

    private static string? FgetsWithPushback(Stream fp, int pushed, int maxBuf)
    {
        StringBuilder sb = new StringBuilder(64);
        int limit = maxBuf - 1;
        int c = pushed != -2 ? pushed : fp.ReadByte();
        if (c < 0)
            return null;
        while (c >= 0 && sb.Length < limit) {
            sb.Append((char)c);
            if (c == '\n')
                break;
            if (sb.Length >= limit)
                break;
            c = fp.ReadByte();
        }
        return sb.ToString();
    }

    private static bool ParseResLine(string buf, out int xres, out int yres)
    {
        xres = yres = 0;
        using MemoryStream ms = new MemoryStream(Encoding.ASCII.GetBytes(buf), false);
        CScanner sc = new CScanner(ms);
        Span<int> v = stackalloc int[2];
        if (sc.Scan("  xres = %d, yres = %d\n", v) != 2)
            return false;
        xres = v[0];
        yres = v[1];
        return true;
    }

    // leptonica: pixacompReadMem (pixcomp.c)
    /// <summary>Reads a serialized array of compressed images from memory.</summary>
    /// <param name="data">The serialized bytes.</param>
    /// <returns>The deserialized <see cref="Pixacomp"/>.</returns>
    public static Pixacomp ReadMem(ReadOnlySpan<byte> data)
    {
        using MemoryStream fp = new MemoryStream(data.ToArray(), false);
        return ReadStream(fp);
    }

    // leptonica: pixacompWrite (pixcomp.c)
    /// <summary>Serializes the array of compressed images to a file.</summary>
    /// <param name="filename">Path of the file to write.</param>
    /// <exception cref="ArgumentException"><paramref name="filename"/> is <c>null</c>.</exception>
    public void Write(string filename)
    {
        const string func = "pixacompWrite";
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");

        using FileStream fp = Utils.FopenWriteStream(filename, "wb");
        WriteStream(fp);
    }

    // leptonica: pixacompWriteStream (pixcomp.c)
    /// <summary>Serializes the array of compressed images to a stream.</summary>
    /// <param name="fp">The stream to write to.</param>
    /// <exception cref="ArgumentException"><paramref name="fp"/> is <c>null</c>.</exception>
    public void WriteStream(Stream fp)
    {
        const string func = "pixacompWriteStream";
        if (fp == null)
            throw Lept.Arg(func, "stream not defined");

        CStdio.Fprintf(fp, $"\nPixacomp Version {L.PIXACOMP_VERSION_NUMBER}\n");
        CStdio.Fprintf(fp, $"Number of pixcomp = {n}\n");
        CStdio.Fprintf(fp, $"Offset of index into array = {offset}");
        boxa.WriteStream(fp);
        for (int i = 0; i < n; i++) {
            Pixcomp pc = GetPixcomp(offset + i, L.L_NOCOPY);
            CStdio.Fprintf(fp, $"\nPixcomp[{i}]: w = {pc.Width}, h = {pc.Height}, d = {pc.Depth}\n");
            CStdio.Fprintf(fp, $"  comptype = {pc.CompType}, size = {pc.Size}, cmapflag = {pc.CmapFlag}\n");
            CStdio.Fprintf(fp, $"  xres = {pc.XRes}, yres = {pc.YRes}\n");
            fp.Write(pc.Data, 0, pc.Data.Length);
            CStdio.Fprintf(fp, $"\n");
        }
    }

    // leptonica: pixacompWriteMem (pixcomp.c)
    /// <summary>Serializes the array of compressed images to a byte array.</summary>
    /// <returns>The serialized bytes.</returns>
    public byte[] WriteMem()
    {
        using MemoryStream fp = new MemoryStream();
        WriteStream(fp);
        return fp.ToArray();
    }

    /*--------------------------------------------------------------------*
     *                         Conversion to pdf                          *
     *--------------------------------------------------------------------*/
    // leptonica: pixacompConvertToPdf (pixcomp.c)
    /// <summary>Converts the images in the array to a multi-page pdf file.</summary>
    /// <param name="res">Input resolution of all images, in pixels per inch.</param>
    /// <param name="scalefactor">Scaling factor applied to each image; typically 1.0.</param>
    /// <param name="type">Encoding type: <see cref="L.L_JPEG_ENCODE"/>, <see cref="L.L_G4_ENCODE"/>, <see cref="L.L_FLATE_ENCODE"/> or <see cref="L.L_DEFAULT_ENCODE"/>.</param>
    /// <param name="quality">Quality for JPEG encoding only; 0 for the default (75).</param>
    /// <param name="title">Optional pdf title; may be <c>null</c>.</param>
    /// <param name="fileout">Path of the output pdf file.</param>
    public void ConvertToPdf(int res, float scalefactor, int type, int quality,
                             string? title, string fileout)
    {
        byte[] data = ConvertToPdfData(res, scalefactor, type, quality, title);
        Utils.BinaryWrite(fileout, "w", data);
    }

    // leptonica: pixacompConvertToPdfData (pixcomp.c)
    /// <summary>Converts the images in this array to the data of a multi-page pdf.</summary>
    /// <param name="res">Input resolution of all images, in pixels per inch.</param>
    /// <param name="scalefactor">Scaling factor applied to each image; typically 1.0.</param>
    /// <param name="type">Encoding type: <see cref="L.L_JPEG_ENCODE"/>, <see cref="L.L_G4_ENCODE"/>, <see cref="L.L_FLATE_ENCODE"/> or <see cref="L.L_DEFAULT_ENCODE"/>.</param>
    /// <param name="quality">Quality for JPEG encoding only; 0 for the default (75).</param>
    /// <param name="title">Optional pdf title; may be <c>null</c>.</param>
    /// <returns>The bytes of the pdf file.</returns>
    public byte[] ConvertToPdfData(int res, float scalefactor, int type, int quality, string? title)
    {
        const string func = "pixacompConvertToPdfData";
        if (scalefactor <= 0.0f)
            scalefactor = 1.0f;
        if (type != L.L_DEFAULT_ENCODE && type != L.L_JPEG_ENCODE &&
            type != L.L_G4_ENCODE && type != L.L_FLATE_ENCODE &&
            type != L.L_JP2K_ENCODE) {
            Log.Warning(func, "invalid compression type; using per-page default");
            type = L.L_DEFAULT_ENCODE;
        }

        int n = Count;
        var pa_data = L_Ptra<L_Bytea>.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pixs;
            try {
                pixs = GetPix(Offset + i);
            } catch (Exception) {
                Log.Error(func, $"pix[{i}] not retrieved");
                continue;
            }
            if (pixs.Width == 1) {  /* used sometimes as placeholders */
                Log.Info(func, $"placeholder image[{i}] has w = 1");
                continue;
            }
            Pix pix = (scalefactor != 1.0f)
                ? pixs.Scale(scalefactor, scalefactor)
                : Pix.Clone(pixs);
            int scaledres = (int)(res * scalefactor);

            int pagetype = (type != L.L_DEFAULT_ENCODE)
                ? type
                : PdfIo.SelectDefaultPdfEncoding(pix);

            try {
                byte[] imdata = pix.ConvertToPdfDataSingle(pagetype, quality, 0, 0, scaledres, title);
                pa_data.Add(L_Bytea.InitFromMem(imdata));
            } catch (Exception) {
                Log.Error(func, $"pdf encoding failed for pix[{i}]");
            }
        }

        if (pa_data.ActualCount == 0)
            throw Lept.Fail(func, "no pdf files made");

        return PdfIo.PtraConcatenateToData(pa_data, null);
    }

    // leptonica: pixacompFastConvertToPdfData (pixcomp.c)
    /// <summary>Converts the images in this array to the data of a multi-page pdf without transcoding.</summary>
    /// <param name="title">Optional pdf title; may be <c>null</c>.</param>
    /// <returns>The bytes of the pdf file.</returns>
    /// <remarks>
    /// <para>The pdf is generated without transcoding if all the images are JPEG compressed.
    /// Images that are not JPEG compressed are skipped.</para>
    /// <para>All images are assumed to have the same resolution, and the resolution embedded in each JPEG file is assumed to be correct.</para>
    /// </remarks>
    public byte[] FastConvertToPdfData(string? title)
    {
        const string func = "pixacompFastConvertToPdfData";
        int n = Count;
        var pa_data = L_Ptra<L_Bytea>.Create(n);
        for (int i = 0; i < n; i++) {
            Pixcomp pixc;
            try {
                pixc = GetPixcomp(i, L.L_NOCOPY);
            } catch (Exception) {
                Log.Error(func, $"pixc[{i}] not retrieved");
                continue;
            }
            pixc.GetParameters(out _, out _, out int comptype, out _);
            if (comptype != L.IFF_JFIF_JPEG) {
                Log.Error(func, $"pixc[{i}] not jpeg compressed");
                continue;
            }
            try {
                byte[] imdata = PixcompFastConvertToPdfData(pixc, title);
                pa_data.Add(L_Bytea.InitFromMem(imdata));
            } catch (Exception) {
                Log.Error(func, $"pdf encoding failed for pixc[{i}]");
            }
        }

        if (pa_data.ActualCount == 0)
            throw Lept.Fail(func, "no pdf files made");

        return PdfIo.PtraConcatenateToData(pa_data, null);
    }

    // leptonica: pixcompFastConvertToPdfData (pixcomp.c)
    private static byte[] PixcompFastConvertToPdfData(Pixcomp pixc, string? title)
    {
        const string func = "pixcompFastConvertToPdfData";
        if (pixc == null)
            throw Lept.Arg(func, "pixc not defined");

        byte[] copy = (byte[])pixc.Data.Clone();
        L_Compressed_Data cid = PdfIo.GenerateJpegDataMem(copy, 0);
        return PdfIo.CidConvertToPdfData(cid, title);
    }

    /*--------------------------------------------------------------------*
     *                        Output for debugging                        *
     *--------------------------------------------------------------------*/
    // leptonica: pixacompWriteStreamInfo (pixcomp.c)
    /// <summary>Writes a description of the array and each of its compressed images to a stream, for debugging.</summary>
    /// <param name="fp">The stream to write to.</param>
    /// <param name="text">Optional label for the output; may be <c>null</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="fp"/> is <c>null</c>.</exception>
    public void WriteStreamInfo(Stream fp, string? text)
    {
        const string func = "pixacompWriteStreamInfo";
        if (fp == null)
            throw Lept.Arg(func, "fp not defined");

        if (text != null)
            CStdio.Fprintf(fp, $"Pixacomp Info for {text}:\n");
        else
            CStdio.Fprintf(fp, $"Pixacomp Info:\n");
        int nboxes = BoxaCount;
        CStdio.Fprintf(fp, $"Number of pixcomp: {n}\n");
        CStdio.Fprintf(fp, $"Size of pixcomp array alloc: {nalloc}\n");
        CStdio.Fprintf(fp, $"Offset of index into array: {offset}\n");
        if (nboxes > 0)
            CStdio.Fprintf(fp, $"Boxa has {nboxes} boxes\n");
        else
            CStdio.Fprintf(fp, $"Boxa is empty\n");
        for (int i = 0; i < n; i++) {
            Pixcomp pc = GetPixcomp(offset + i, L.L_NOCOPY);
            pc.WriteStreamInfo(fp, null);
        }
    }

    // leptonica: pixacompDisplayTiledAndScaled (pixcomp.c)
    /// <summary>Renders the images in the array as a single tiled image, with each image scaled to a common width.</summary>
    /// <param name="outdepth">Output depth: 1, 8 or 32 bpp.</param>
    /// <param name="tilewidth">Each image is scaled to this width.</param>
    /// <param name="ncols">Number of tiles in each row.</param>
    /// <param name="background">0 for a white background, 1 for black; the color of the spacing between images.</param>
    /// <param name="spacing">Spacing between images, and on the outside.</param>
    /// <param name="border">Width of an additional black border on each image; 0 for no border.</param>
    /// <returns>The tiled image.</returns>
    /// <remarks>This is the same as <see cref="Pixa.DisplayTiledAndScaled"/>, but works on compressed images. It is particularly useful for showing the images at reduced resolution.</remarks>
    public Pix DisplayTiledAndScaled(int outdepth, int tilewidth, int ncols,
                                     int background, int spacing, int border)
    {
        Pixa pixa = Pixa.CreateFromPixacomp(this, L.L_COPY);
        return pixa.DisplayTiledAndScaled(outdepth, tilewidth, ncols, background, spacing, border);
    }

    // leptonica: pixacompWriteFiles (pixcomp.c)
    /// <summary>Writes each compressed image to its own file in a subdirectory of the temp directory.</summary>
    /// <param name="subdir">Name of the subdirectory under <c>/tmp</c>.</param>
    /// <exception cref="LeptonicaException">The subdirectory cannot be created.</exception>
    public void WriteFiles(string subdir)
    {
        const string func = "pixacompWriteFiles";
        if (Utils.LeptMkdir(subdir) > 0)
            throw Lept.Fail(func, "invalid subdir");

        for (int i = 0; i < n; i++) {
            Pixcomp pc = GetPixcomp(i, L.L_NOCOPY);
            string buf = $"/tmp/{subdir}/{i:D3}";
            pc.WriteFile(buf);
        }
    }
}

public sealed partial class Pixa
{
    // leptonica: pixaCreateFromPixacomp (pixcomp.c)
    /// <param name="pixac"></param>
    /// <param name="accesstype">L_COPY, L_CLONE, L_COPY_CLONE</param>
    public static Pixa CreateFromPixacomp(Pixacomp pixac, int accesstype)
    {
        const string func = "pixaCreateFromPixacomp";
        if (pixac == null)
            throw Lept.Arg(func, "pixac not defined");
        if (accesstype != L.L_COPY && accesstype != L.L_CLONE &&
            accesstype != L.L_COPY_CLONE)
            throw Lept.Arg(func, "invalid accesstype");

        int count = pixac.Count;
        int savedOffset = pixac.Offset;
        pixac.Offset = 0;
        Pixa pixa;
        try {
            pixa = Create(count);
            for (int i = 0; i < count; i++) {
                Pix pix;
                try {
                    pix = pixac.GetPix(i);
                } catch (Exception e) when (e is ArgumentException || e is LeptonicaException) {
                    Log.Warning(func, $"pix {i} not made");
                    continue;
                }
                pixa.AddPix(pix, L.L_INSERT);
            }
            pixa.SetBoxa(pixac.GetBoxa(accesstype), L.L_INSERT);
        } finally {
            pixac.Offset = savedOffset;
        }

        return pixa;
    }
}
