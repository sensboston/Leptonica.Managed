using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BitMiracle.LibTiff.Classic;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  tiffio.c
 *
 *     Reading tiff:
 *             PIX       *pixReadTiff()             [ special top level ]
 *             PIX       *pixReadStreamTiff()
 *
 *     Writing tiff:
 *             l_int32    pixWriteTiff()            [ special top level ]
 *             l_int32    pixWriteTiffCustom()      [ special top level ]
 *             l_int32    pixWriteStreamTiff()
 *             l_int32    pixWriteStreamTiffWA()
 *
 *     Reading and writing multipage tiff
 *             PIX       *pixReadFromMultipageTiff()
 *             PIXA      *pixaReadMultipageTiff()   [ special top level ]
 *             l_int32    pixaWriteMultipageTiff()  [ special top level ]
 *             l_int32    writeMultipageTiff()      [ special top level ]
 *             l_int32    writeMultipageTiffSA()
 *
 *     Information about tiff file
 *             l_int32    fprintTiffInfo()
 *             l_int32    tiffGetCount()
 *             l_int32    getTiffResolution()
 *             l_int32    readHeaderTiff()
 *             l_int32    freadHeaderTiff()
 *             l_int32    readHeaderMemTiff()
 *             l_int32    findTiffCompression()
 *
 *     Extraction of tiff g4 data:
 *             l_int32    extractG4DataFromFile()
 *
 *     Read and write to memory
 *             PIX       *pixReadMemTiff()
 *             PIX       *pixReadMemFromMultipageTiff()
 *             PIXA      *pixaReadMemMultipageTiff()
 *             l_int32    pixaWriteMemMultipageTiff()
 *             l_int32    pixWriteMemTiff()
 *             l_int32    pixWriteMemTiffCustom()
 */
public sealed partial class Pix
{
    private const int DefaultTiffResolution = 300;
    private const int ManyPagesInTiffFile = 3000;
    private const int MaxTiffWidth = 1 << 20;
    private const int MaxTiffHeight = 1 << 20;
    private const long MaxNumTiffBytes = (1L << 28) - 1;

    private readonly struct TiffTransform
    {
        public readonly bool VFlip;
        public readonly bool HFlip;
        public readonly int Rotate;

        public TiffTransform(int vflip, int hflip, int rotate)
        {
            VFlip = vflip != 0;
            HFlip = hflip != 0;
            Rotate = rotate;
        }
    }

    private static readonly TiffTransform[] TiffOrientationTransforms =
    {
        new(0, 0, 0),
        new(0, 1, 0),
        new(1, 1, 0),
        new(1, 0, 0),
        new(0, 1, -1),
        new(0, 0, 1),
        new(0, 1, 1),
        new(0, 0, -1),
    };

    private static readonly TiffTransform[] TiffPartialOrientationTransforms =
    {
        new(0, 0, 0),
        new(0, 0, 0),
        new(0, 0, 0),
        new(0, 0, 0),
        new(0, 1, -1),
        new(0, 1, 1),
        new(1, 0, 1),
        new(0, 1, -1),
    };

    private sealed class QuietTiffErrorHandler : TiffErrorHandler
    {
        public override void ErrorHandler(Tiff tif, string method, string format, params object[] args) { }
        public override void ErrorHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args) { }
        public override void WarningHandler(Tiff tif, string method, string format, params object[] args) { }
        public override void WarningHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args) { }
    }

    private sealed class LeaveOpenTiffStream : TiffStream
    {
        public static readonly LeaveOpenTiffStream Instance = new LeaveOpenTiffStream();

        public override void Close(object clientData)
        {
            if (clientData is Stream stream)
            {
                if (stream.CanWrite)
                    stream.Flush();
                if (stream.CanSeek)
                    stream.Position = 0;
            }
        }
    }

    private static bool s_tiffErrorHandlerInstalled;

    private static void EnsureTiffErrorHandler()
    {
        if (!s_tiffErrorHandlerInstalled)
        {
            Tiff.SetErrorHandler(new QuietTiffErrorHandler());
            s_tiffErrorHandlerInstalled = true;
        }
    }

    internal static Tiff? FopenTiff(Stream fp, string mode)
    {
        EnsureTiffErrorHandler();
        if (fp.CanSeek && mode.StartsWith("r", StringComparison.Ordinal))
            fp.Position = 0;
        else if (fp.CanSeek && mode == "w")
            fp.Position = 0;
        return Tiff.ClientOpen("tiffstream", mode, fp, LeaveOpenTiffStream.Instance);
    }

    internal static Tiff? OpenTiffFile(string filename, string modestr)
    {
        EnsureTiffErrorHandler();

        /* Keep TIFF file I/O consistent with the rest of the library: on
         * Windows/macOS, /tmp is rewritten to the OS temp directory.  Resolve
         * the path once and use it for both append detection and Tiff.Open;
         * otherwise TIFF writes use the raw /tmp path while Pix.Read and the
         * regression harness look in the rewritten temp directory. */
        string resolvedFilename = Utils.GenPathname(filename, null);
        string cleanMode = modestr;
        if (cleanMode.StartsWith("w", StringComparison.Ordinal))
            cleanMode = "w";
        else if (cleanMode.StartsWith("a", StringComparison.Ordinal))
            cleanMode = File.Exists(resolvedFilename) ? "a" : "w";
        else
            cleanMode = "r";
        return Tiff.Open(resolvedFilename, cleanMode);
    }

    internal static bool IsTiffFormatCode(int f)
    {
        return f == L.IFF_TIFF || f == L.IFF_TIFF_PACKBITS ||
               f == L.IFF_TIFF_RLE || f == L.IFF_TIFF_G3 ||
               f == L.IFF_TIFF_G4 || f == L.IFF_TIFF_LZW ||
               f == L.IFF_TIFF_ZIP || f == L.IFF_TIFF_JPEG;
    }

    private static int GetTiffCompressedFormat(Compression tiffcomp)
    {
        return tiffcomp switch
        {
            Compression.CCITTFAX4 => L.IFF_TIFF_G4,
            Compression.CCITTFAX3 => L.IFF_TIFF_G3,
            Compression.CCITTRLE => L.IFF_TIFF_RLE,
            Compression.PACKBITS => L.IFF_TIFF_PACKBITS,
            Compression.LZW => L.IFF_TIFF_LZW,
            Compression.ADOBE_DEFLATE or Compression.DEFLATE => L.IFF_TIFF_ZIP,
            Compression.JPEG or Compression.OJPEG => L.IFF_TIFF_JPEG,
            _ => L.IFF_TIFF,
        };
    }

    /*--------------------------------------------------------------*
     *                      Reading from file                       *
     *--------------------------------------------------------------*/
    // leptonica: pixReadTiff (tiffio.c)
    /// <summary>Reads one page of a TIFF file.</summary>
    /// <param name="filename">Path of the TIFF file.</param>
    /// <param name="n">Zero-based page number; use 0 for a single-page file.</param>
    /// <returns>The decoded image, or <see langword="null"/> if the page cannot be read.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="filename"/> is <see langword="null"/>.</exception>
    public static Pix? ReadTiff(string filename, int n = 0)
    {
        const string func = "pixReadTiff";
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");

        using FileStream fp = Utils.FopenReadStream(filename);
        return ReadStreamTiff(fp, n);
    }

    // leptonica: pixReadStreamTiff (tiffio.c)
    /// <summary>Reads one page of a TIFF image from a stream.</summary>
    /// <param name="fp">The stream to read.</param>
    /// <param name="n">Zero-based page number; use 0 for a single-page image.</param>
    /// <returns>The decoded image, or <see langword="null"/> if the page cannot be read.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> is <see langword="null"/>, or <paramref name="n"/> is out of range.</exception>
    public static Pix? ReadStreamTiff(Stream fp, int n = 0)
    {
        const string func = "pixReadStreamTiff";
        if (fp == null)
            throw Lept.Arg(func, "stream not defined");
        if (n < 0 || n > short.MaxValue)
            return null;

        Stream srcStream = EnsureSeekableReadStream(fp, out MemoryStream? tempCopy);
        try
        {
            using Tiff? tif = FopenTiff(srcStream, "r");
            if (tif == null)
                throw Lept.Fail(func, "tif not opened");

            if (!tif.SetDirectory((short)n))
                return null;

            return ReadFromTiffHandle(tif);
        }
        finally
        {
            tempCopy?.Dispose();
        }
    }

    internal static Pix? ReadFromTiffHandle(Tiff tif)
    {
        const string func = "pixReadFromTiffStream";
        bool readOriented = false;

        FieldValue[]? sfField = tif.GetFieldDefaulted(TiffTag.SAMPLEFORMAT);
        SampleFormat sampleFmt = (sfField != null && sfField.Length > 0)
            ? (SampleFormat)sfField[0].ToInt()
            : SampleFormat.UINT;
        if (sampleFmt != SampleFormat.UINT)
        {
            Log.Error(func, $"sample format = {sampleFmt} is not uint");
            return null;
        }

        if (tif.IsTiled())
        {
            Log.Error(func, "tiled format is not supported");
            return null;
        }

        FieldValue[]? compField = tif.GetFieldDefaulted(TiffTag.COMPRESSION);
        Compression tiffcomp = (compField != null && compField.Length > 0)
            ? (Compression)compField[0].ToInt()
            : Compression.NONE;

        FieldValue[]? bpsField = tif.GetFieldDefaulted(TiffTag.BITSPERSAMPLE);
        int bps = (bpsField != null && bpsField.Length > 0) ? bpsField[0].ToInt() : 1;
        FieldValue[]? sppField = tif.GetFieldDefaulted(TiffTag.SAMPLESPERPIXEL);
        int spp = (sppField != null && sppField.Length > 0) ? sppField[0].ToInt() : 1;

        if (bps != 1 && bps != 2 && bps != 4 && bps != 8 && bps != 16)
        {
            Log.Error(func, $"invalid bps = {bps}");
            return null;
        }
        if (spp == 2 && bps != 8)
        {
            Log.Error(func, $"for 2 spp, only handle 8 bps; this is {bps} bps");
            return null;
        }
        if ((spp == 3 || spp == 4) && bps < 8)
        {
            Log.Error(func, $"for 3 and 4 spp, only handle 8 and 16 bps; this is {bps} bps");
            return null;
        }

        int d;
        if (spp == 1)
            d = bps;
        else if (spp == 2 || spp == 3 || spp == 4)
            d = 32;
        else
        {
            Log.Error(func, $"spp = {spp}; not in {{1,2,3,4}}");
            return null;
        }

        FieldValue[]? wField = tif.GetField(TiffTag.IMAGEWIDTH);
        FieldValue[]? hField = tif.GetField(TiffTag.IMAGELENGTH);
        if (wField == null || hField == null)
            return null;
        int w = wField[0].ToInt();
        int h = hField[0].ToInt();
        if (w <= 0 || w > MaxTiffWidth || h <= 0 || h > MaxTiffHeight)
        {
            Log.Error(func, $"invalid dimensions {w}x{h}");
            return null;
        }

        int tiffbpl = tif.ScanlineSize();
        int packedbpl = (bps * spp * w + 7) / 8;
        bool halfSize = Math.Abs(2 * tiffbpl - packedbpl) <= 8;
        bool twoThirdsSize = Math.Abs(3 * tiffbpl - 2 * packedbpl) <= 8;
        if (tiffbpl != packedbpl && !halfSize && !twoThirdsSize)
        {
            Log.Error(func, $"invalid tiffbpl: tiffbpl = {tiffbpl}, packedbpl = {packedbpl}");
            return null;
        }

        Pix pix = Create(w, h, d);
        pix.InputFormat = L.IFF_TIFF;
        uint[] pixdata = pix.Data;
        int wpl = pix.Wpl;
        int bpl = 4 * wpl;

        if (spp == 1)
        {
            byte[] linebuf = new byte[Math.Max(bpl, tiffbpl)];
            Span<byte> pixBytes = MemoryMarshal.AsBytes(pixdata.AsSpan());
            int copyBytes = Math.Min(tiffbpl, bpl);
            for (int i = 0; i < h; i++)
            {
                if (!tif.ReadScanline(linebuf, i, 0))
                {
                    Log.Error(func, $"spp = 1, read fail at line {i}");
                    return null;
                }
                linebuf.AsSpan(0, copyBytes).CopyTo(pixBytes.Slice(i * bpl, copyBytes));
            }
            if (bps <= 8)
                pix.EndianByteSwap();
            else
                pix.EndianTwoByteSwap();
        }
        else if (spp == 2 && bps == 8)
        {
            Log.Info(func, "gray+alpha is not supported; converting to RGBA");
            pix.Spp = 4;
            byte[] linebuf = new byte[Math.Max(bpl, tiffbpl)];
            for (int i = 0; i < h; i++)
            {
                if (!tif.ReadScanline(linebuf, i, 0))
                {
                    Log.Error(func, $"spp = 2, read fail at line {i}");
                    return null;
                }
                int lineIdx = i * wpl;
                for (int j = 0, k = 0; j < w; j++)
                {
                    int gray = linebuf[k++];
                    int aval = linebuf[k++];
                    ComposeRGBAPixel(gray, gray, gray, aval, out pixdata[lineIdx + j]);
                }
            }
        }
        else
        {
            int[] tiffdata = new int[w * h];
            if (!tif.ReadRGBAImageOriented(w, h, tiffdata, Orientation.TOPLEFT, false))
                return null;
            readOriented = true;

            if (spp == 4)
                pix.Spp = 4;
            for (int i = 0; i < h; i++)
            {
                int lineIdx = i * wpl;
                int rowBase = i * w;
                for (int j = 0; j < w; j++)
                {
                    int abgr = tiffdata[rowBase + j];
                    int rval = Tiff.GetR(abgr);
                    int gval = Tiff.GetG(abgr);
                    int bval = Tiff.GetB(abgr);
                    if (spp == 3)
                        ComposeRGBPixel(rval, gval, bval, out pixdata[lineIdx + j]);
                    else
                        ComposeRGBAPixel(rval, gval, bval, Tiff.GetA(abgr), out pixdata[lineIdx + j]);
                }
            }
        }

        if (TryGetTiffStreamResolution(tif, out int xres, out int yres))
        {
            pix.XRes = xres;
            pix.YRes = yres;
        }

        pix.InputFormat = GetTiffCompressedFormat(tiffcomp);

        FieldValue[]? cmapField = tif.GetField(TiffTag.COLORMAP);
        if (cmapField != null && cmapField.Length >= 3)
        {
            if (bps > 8)
                return null;
            ushort[] redmap = cmapField[0].ToUShortArray();
            ushort[] greenmap = cmapField[1].ToUShortArray();
            ushort[] bluemap = cmapField[2].ToUShortArray();
            PixColormap cmap = PixColormap.Create(bps);
            int ncolors = Math.Min(1 << bps, Math.Min(redmap.Length, Math.Min(greenmap.Length, bluemap.Length)));
            for (int i = 0; i < ncolors; i++)
                cmap.AddColor(redmap[i] >> 8, greenmap[i] >> 8, bluemap[i] >> 8);
            pix.SetColormap(cmap);

            if (bps == 1)
                pix = pix.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        }
        else
        {
            FieldValue[]? photoField = tif.GetField(TiffTag.PHOTOMETRIC);
            Photometric photometry;
            if (photoField != null && photoField.Length > 0)
            {
                photometry = (Photometric)photoField[0].ToInt();
            }
            else if (tiffcomp == Compression.CCITTFAX3 ||
                     tiffcomp == Compression.CCITTFAX4 ||
                     tiffcomp == Compression.CCITTRLE ||
                     tiffcomp == Compression.CCITTRLEW)
            {
                photometry = Photometric.MINISWHITE;
            }
            else
            {
                photometry = Photometric.MINISBLACK;
            }

            if ((d == 1 && photometry == Photometric.MINISBLACK) ||
                (d == 8 && photometry == Photometric.MINISWHITE))
            {
                Invert(pix, pix);
            }
        }

        FieldValue[]? orientField = tif.GetField(TiffTag.ORIENTATION);
        if (orientField != null && orientField.Length > 0)
        {
            int orientation = orientField[0].ToInt();
            if (orientation >= 1 && orientation <= 8)
            {
                TiffTransform tr = readOriented
                    ? TiffPartialOrientationTransforms[orientation - 1]
                    : TiffOrientationTransforms[orientation - 1];
                if (tr.VFlip)
                    FlipTB(pix, pix);
                if (tr.HFlip)
                    FlipLR(pix, pix);
                if (tr.Rotate != 0)
                    pix = pix.Rotate90(tr.Rotate);
            }
        }

        FieldValue[]? descField = tif.GetField(TiffTag.IMAGEDESCRIPTION);
        if (descField != null && descField.Length > 0)
        {
            string? desc = descField[0].ToString();
            if (!string.IsNullOrEmpty(desc))
                pix.Text = desc;
        }

        pix.SetPadBits(0);
        return pix;
    }

    /*--------------------------------------------------------------*
     *                       Writing to file                        *
     *--------------------------------------------------------------*/
    // leptonica: pixWriteTiff (tiffio.c)
    /// <summary>Writes an image to a TIFF file.</summary>
    /// <param name="filename">Path of the file to write.</param>
    /// <param name="pix">The image to write; any depth is accepted, and a colormap is removed.</param>
    /// <param name="comptype">The compression: <see cref="L.IFF_TIFF"/>, <see cref="L.IFF_TIFF_RLE"/>, <see cref="L.IFF_TIFF_PACKBITS"/>,
    /// <see cref="L.IFF_TIFF_G3"/>, <see cref="L.IFF_TIFF_G4"/>, <see cref="L.IFF_TIFF_LZW"/>, <see cref="L.IFF_TIFF_ZIP"/> or <see cref="L.IFF_TIFF_JPEG"/>.</param>
    /// <param name="modestr"><c>"w"</c> to create a new file, or <c>"a"</c> to append a page to an existing file.</param>
    /// <remarks>For a multipage TIFF file, write the first image with mode <c>"w"</c> and all subsequent images with mode <c>"a"</c>.</remarks>
    public static void WriteTiff(string filename, Pix pix, int comptype, string modestr = "w")
    {
        WriteTiffCustom(filename, pix, comptype, modestr, null, null, null, null);
    }

    // leptonica: pixWriteTiffCustom (tiffio.c)
    /// <summary>Writes an image to a TIFF file, with optional custom TIFF tags.</summary>
    /// <param name="filename">Path of the file to write.</param>
    /// <param name="pix">The image to write; any depth is accepted, and a colormap is removed.</param>
    /// <param name="comptype">The compression: <see cref="L.IFF_TIFF"/>, <see cref="L.IFF_TIFF_RLE"/>, <see cref="L.IFF_TIFF_PACKBITS"/>,
    /// <see cref="L.IFF_TIFF_G3"/>, <see cref="L.IFF_TIFF_G4"/>, <see cref="L.IFF_TIFF_LZW"/>, <see cref="L.IFF_TIFF_ZIP"/> or <see cref="L.IFF_TIFF_JPEG"/>.</param>
    /// <param name="modestr"><c>"w"</c> to create a new file, or <c>"a"</c> to append a page to an existing file.</param>
    /// <param name="natags">The numeric identifiers of the custom tags, or <see langword="null"/> for none.</param>
    /// <param name="savals">The tag values in string format, or <see langword="null"/> for none.</param>
    /// <param name="satypes">The tag types in string format, or <see langword="null"/> for none.</param>
    /// <param name="nasizes">The number of elements for tags whose value is an array of bytes, or <see langword="null"/> for none.</param>
    /// <remarks>
    /// <para>The three arrays <paramref name="natags"/>, <paramref name="savals"/> and <paramref name="satypes"/> must all be
    /// <see langword="null"/>, or all be defined and of equal size. The tag identifiers are those defined in the TIFF specification.</para>
    /// <para>The types allowed are the strings <c>"char*"</c>, <c>"l_uint8*"</c>, <c>"l_uint16"</c>, <c>"l_uint32"</c>,
    /// <c>"l_int32"</c>, <c>"l_float64"</c> and <c>"l_uint16-l_uint16"</c>; for the last one, separate the two values in the
    /// value string with a dash. Of these, <c>"char*"</c> and <c>"l_uint16"</c> are the most commonly used.</para>
    /// <para><paramref name="nasizes"/> is needed only for tags that take an array of bytes; any tags that need
    /// a size value must be written first.</para>
    /// </remarks>
    public static void WriteTiffCustom(string filename, Pix pix, int comptype, string modestr,
                                       Numa? natags, Sarray? savals, Sarray? satypes, Numa? nasizes)
    {
        const string func = "pixWriteTiffCustom";
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");
        if (pix == null)
            throw Lept.Arg(func, "pix not defined");
        if (modestr == null)
            throw Lept.Arg(func, "modestr not defined");

        using Tiff? tif = OpenTiffFile(filename, modestr);
        if (tif == null)
            throw Lept.Fail(func, "tif not opened");

        WriteToTiffHandle(tif, pix, comptype, natags, savals, satypes, nasizes, func);
        tif.Close();
    }

    /*--------------------------------------------------------------*
     *                       Writing to stream                      *
     *--------------------------------------------------------------*/
    // leptonica: pixWriteStreamTiff (tiffio.c)
    /// <summary>Writes an image to a stream in TIFF format.</summary>
    /// <param name="fp">The stream to write.</param>
    /// <param name="pix">The image to write; any depth is accepted, and a colormap is removed.</param>
    /// <param name="comptype">The compression: <see cref="L.IFF_TIFF"/>, <see cref="L.IFF_TIFF_PACKBITS"/>, <see cref="L.IFF_TIFF_RLE"/>,
    /// <see cref="L.IFF_TIFF_G3"/>, <see cref="L.IFF_TIFF_G4"/>, <see cref="L.IFF_TIFF_LZW"/>, <see cref="L.IFF_TIFF_ZIP"/> or <see cref="L.IFF_TIFF_JPEG"/>.</param>
    public static void WriteStreamTiff(Stream fp, Pix pix, int comptype)
    {
        WriteStreamTiffWA(fp, pix, comptype, "w");
    }

    // leptonica: pixWriteStreamTiffWA (tiffio.c)
    /// <summary>Writes an image to a stream in TIFF format, either as a new file or appended as a page.</summary>
    /// <param name="fp">The stream to write.</param>
    /// <param name="pix">The image to write; any depth is accepted, and a colormap is removed.</param>
    /// <param name="comptype">The compression: <see cref="L.IFF_TIFF"/>, <see cref="L.IFF_TIFF_PACKBITS"/>, <see cref="L.IFF_TIFF_RLE"/>,
    /// <see cref="L.IFF_TIFF_G3"/>, <see cref="L.IFF_TIFF_G4"/>, <see cref="L.IFF_TIFF_LZW"/>, <see cref="L.IFF_TIFF_ZIP"/> or <see cref="L.IFF_TIFF_JPEG"/>.</param>
    /// <param name="modestr"><c>"w"</c> to write a new image, or <c>"a"</c> to append a page.</param>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> or <paramref name="pix"/> is <see langword="null"/>, or <paramref name="modestr"/> is neither <c>"w"</c> nor <c>"a"</c>.</exception>
    public static void WriteStreamTiffWA(Stream fp, Pix pix, int comptype, string modestr)
    {
        const string func = "pixWriteStreamTiffWA";
        if (fp == null)
            throw Lept.Arg(func, "stream not defined");
        if (pix == null)
            throw Lept.Arg(func, "pix not defined");
        if (modestr != "w" && modestr != "a")
            throw Lept.Arg(func, $"modestr = {modestr}; not 'w' or 'a'");

        if (pix.Depth != 1 && comptype != L.IFF_TIFF &&
            comptype != L.IFF_TIFF_LZW && comptype != L.IFF_TIFF_ZIP &&
            comptype != L.IFF_TIFF_JPEG)
        {
            Log.Warning(func, $"invalid compression type {comptype} for bpp > 1; using TIFF_ZIP");
            comptype = L.IFF_TIFF_ZIP;
        }

        if (modestr == "w" && fp.CanSeek)
        {
            fp.Position = 0;
            if (fp.CanWrite)
                fp.SetLength(0);
        }

        using Tiff? tif = FopenTiff(fp, modestr);
        if (tif == null)
            throw Lept.Fail(func, "tif not opened");

        WriteToTiffHandle(tif, pix, comptype, null, null, null, null, func);
        tif.Close();
    }

    internal static void WriteToTiffHandle(Tiff tif, Pix pix, int comptype,
                                           Numa? natags, Sarray? savals, Sarray? satypes, Numa? nasizes,
                                           string func)
    {
        if (pix.Depth != 1 && comptype != L.IFF_TIFF &&
            comptype != L.IFF_TIFF_LZW && comptype != L.IFF_TIFF_ZIP &&
            comptype != L.IFF_TIFF_JPEG)
        {
            Log.Warning(func, $"invalid compression type {comptype} for bpp > 1; using TIFF_ZIP");
            comptype = L.IFF_TIFF_ZIP;
        }

        // TIFF-JPEG only handles 8 bpp grayscale or 24/32 bpp RGB without palette
        Pix workPix = pix;
        if (comptype == L.IFF_TIFF_JPEG)
        {
            if (workPix.Colormap != null)
                workPix = workPix.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
            else if (workPix.Depth < 8 || workPix.Depth == 16)
                workPix = workPix.ConvertTo8(false);
            if (workPix.Depth == 32 && workPix.Spp == 4)
            {
                Pix rgb = Copy(null, workPix);
                rgb.Spp = 3;
                workPix = rgb;
            }
        }

        workPix.SetPadBits(0);
        workPix.GetDimensions(out int w, out int h, out int d);
        int spp = workPix.Spp;
        int xres = workPix.XRes == 0 ? DefaultTiffResolution : workPix.XRes;
        int yres = workPix.YRes == 0 ? DefaultTiffResolution : workPix.YRes;

        tif.SetField(TiffTag.RESOLUTIONUNIT, ResUnit.INCH);
        tif.SetField(TiffTag.XRESOLUTION, (double)xres);
        tif.SetField(TiffTag.YRESOLUTION, (double)yres);
        tif.SetField(TiffTag.IMAGEWIDTH, w);
        tif.SetField(TiffTag.IMAGELENGTH, h);
        tif.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);

        string? text = workPix.Text;
        if (!string.IsNullOrEmpty(text))
            tif.SetField(TiffTag.IMAGEDESCRIPTION, text);

        PixColormap? cmap = workPix.Colormap;
        if (d == 1 && cmap == null)
        {
            tif.SetField(TiffTag.PHOTOMETRIC, Photometric.MINISWHITE);
        }
        else if ((d == 32 && spp == 3) || d == 24)
        {
            tif.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
            tif.SetField(TiffTag.SAMPLESPERPIXEL, 3);
            tif.SetField(TiffTag.BITSPERSAMPLE, 8);
        }
        else if (d == 32 && spp == 4)
        {
            short[] extra = { (short)ExtraSample.ASSOCALPHA };
            tif.SetField(TiffTag.EXTRASAMPLES, 1, extra);
            tif.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
            tif.SetField(TiffTag.SAMPLESPERPIXEL, 4);
            tif.SetField(TiffTag.BITSPERSAMPLE, 8);
        }
        else if (d == 16)
        {
            tif.SetField(TiffTag.PHOTOMETRIC, Photometric.MINISBLACK);
        }
        else if (cmap == null)
        {
            tif.SetField(TiffTag.PHOTOMETRIC, Photometric.MINISBLACK);
        }
        else
        {
            if (d > 8)
                d = 8;
            cmap.ToArrays(out int[] rmap, out int[] gmap, out int[] bmap, out _);
            int cmapsize = Math.Min(256, 1 << d);
            int ncolors = Math.Min(cmapsize, cmap.Count);
            ushort[] redmap = new ushort[cmapsize];
            ushort[] greenmap = new ushort[cmapsize];
            ushort[] bluemap = new ushort[cmapsize];
            for (int i = 0; i < ncolors; i++)
            {
                redmap[i] = (ushort)((rmap[i] << 8) | rmap[i]);
                greenmap[i] = (ushort)((gmap[i] << 8) | gmap[i]);
                bluemap[i] = (ushort)((bmap[i] << 8) | bmap[i]);
            }

            tif.SetField(TiffTag.PHOTOMETRIC, Photometric.PALETTE);
            tif.SetField(TiffTag.SAMPLESPERPIXEL, 1);
            tif.SetField(TiffTag.BITSPERSAMPLE, d);
            tif.SetField(TiffTag.COLORMAP, redmap, greenmap, bluemap);
        }

        if (d <= 16)
        {
            tif.SetField(TiffTag.BITSPERSAMPLE, d);
            tif.SetField(TiffTag.SAMPLESPERPIXEL, 1);
        }

        tif.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);

        Compression comp = comptype switch
        {
            L.IFF_TIFF => Compression.NONE,
            L.IFF_TIFF_G4 => Compression.CCITTFAX4,
            L.IFF_TIFF_G3 => Compression.CCITTFAX3,
            L.IFF_TIFF_RLE => Compression.CCITTRLE,
            L.IFF_TIFF_PACKBITS => Compression.PACKBITS,
            L.IFF_TIFF_LZW => Compression.LZW,
            L.IFF_TIFF_ZIP => Compression.ADOBE_DEFLATE,
            L.IFF_TIFF_JPEG => Compression.JPEG,
            _ => Compression.NONE,
        };
        tif.SetField(TiffTag.COMPRESSION, comp);
        if (comp == Compression.JPEG)
        {
            tif.SetField(TiffTag.JPEGQUALITY, 75);
            if ((d == 32 && spp == 3) || d == 24)
                tif.SetField(TiffTag.JPEGCOLORMODE, JpegColorMode.RGB);
        }

        WriteCustomTiffTags(tif, natags, savals, satypes, nasizes, func);

        int tiffbpl = tif.ScanlineSize();
        int wpl = workPix.Wpl;
        int bpl = 4 * wpl;
        byte[] linebuf = new byte[Math.Max(bpl, tiffbpl)];

        // Note: For JPEG-in-TIFF, RowsPerStrip must be a multiple of vertical MCU size (8 or 16).
        if (comp == Compression.JPEG)
        {
            int rps = tif.DefaultStripSize(0);
            tif.SetField(TiffTag.ROWSPERSTRIP, rps);
        }
        else
        {
            tif.SetField(TiffTag.ROWSPERSTRIP, h);
        }

        if (d != 24 && d != 32)
        {
            Pix pixt;
            if (d == 1 && cmap == null)
            {
                // PHOTOMETRIC_MINISWHITE: 0 is white, 1 is black, which matches Leptonica's 1 bpp representation directly!
                pixt = workPix.EndianByteSwapNew();
            }
            else if (d == 16)
            {
                pixt = workPix.EndianTwoByteSwapNew();
            }
            else
            {
                pixt = workPix.EndianByteSwapNew();
            }

            ReadOnlySpan<byte> srcBytes = MemoryMarshal.AsBytes(pixt.Data.AsSpan());
            int copyLen = Math.Min(tiffbpl, bpl);
            for (int i = 0; i < h; i++)
            {
                srcBytes.Slice(i * bpl, copyLen).CopyTo(linebuf.AsSpan(0, copyLen));
                if (!tif.WriteScanline(linebuf, i, 0))
                    throw Lept.Fail(func, $"failed writing scanline {i}");
            }
        }
        else if (d == 24)
        {
            ReadOnlySpan<byte> srcBytes = MemoryMarshal.AsBytes(workPix.Data.AsSpan());
            int rowLen = 3 * w;
            for (int i = 0; i < h; i++)
            {
                srcBytes.Slice(i * bpl, rowLen).CopyTo(linebuf.AsSpan(0, rowLen));
                if (!tif.WriteScanline(linebuf, i, 0))
                    throw Lept.Fail(func, $"failed writing scanline {i}");
            }
        }
        else // 32 bpp RGB or RGBA
        {
            uint[] pdata = workPix.Data;
            for (int i = 0; i < h; i++)
            {
                int lineIdx = i * wpl;
                for (int j = 0, k = 0; j < w; j++)
                {
                    uint word = pdata[lineIdx + j];
                    linebuf[k++] = (byte)((word >> L.L_RED_SHIFT) & 0xff);
                    linebuf[k++] = (byte)((word >> L.L_GREEN_SHIFT) & 0xff);
                    linebuf[k++] = (byte)((word >> L.L_BLUE_SHIFT) & 0xff);
                    if (spp == 4)
                        linebuf[k++] = (byte)((word >> L.L_ALPHA_SHIFT) & 0xff);
                }
                if (!tif.WriteScanline(linebuf, i, 0))
                    throw Lept.Fail(func, $"failed writing scanline {i}");
            }
        }
    }

    private static void WriteCustomTiffTags(Tiff tif, Numa? natags, Sarray? savals, Sarray? satypes, Numa? nasizes, string func)
    {
        if (natags == null && savals == null && satypes == null)
            return;
        if (natags == null || savals == null || satypes == null)
            throw Lept.Arg(func, "not all arrays defined");

        int n = natags.Count;
        if (savals.Count != n || satypes.Count != n)
            throw Lept.Arg(func, "not all sa the same size");

        int ns = 0;
        if (nasizes != null)
        {
            ns = nasizes.Count;
            if (ns > n)
                throw Lept.Arg(func, "too many 4-arg tag calls");
            for (int i = 0; i < ns; i++)
            {
                int tagval = natags.GetIValue(i);
                string sval = savals.GetString(i, L.L_NOCOPY);
                string type = satypes.GetString(i, L.L_NOCOPY);
                int size = nasizes.GetIValue(i);
                if (size <= 0 && (type == "char*" || type == "const char*"))
                {
                    RegisterUnknownCustomTag(tif, (TiffTag)tagval, TiffType.ASCII, passCount: false);
                    tif.SetField((TiffTag)tagval, sval);
                    continue;
                }
                byte[] rawBytes = Encoding.Latin1.GetBytes(sval);
                if (rawBytes.Length != size)
                    Array.Resize(ref rawBytes, Math.Max(0, size));
                RegisterUnknownCustomTag(tif, (TiffTag)tagval, (type == "char*") ? TiffType.ASCII : TiffType.BYTE, passCount: true);
                tif.SetField((TiffTag)tagval, size, rawBytes);
            }
        }

        for (int i = ns; i < n; i++)
        {
            int tagval = natags.GetIValue(i);
            string sval = savals.GetString(i, L.L_NOCOPY);
            string type = satypes.GetString(i, L.L_NOCOPY);
            TiffTag tag = (TiffTag)tagval;

            if (type == "char*" || type == "const char*")
            {
                RegisterUnknownCustomTag(tif, tag, TiffType.ASCII, passCount: false);
                tif.SetField(tag, sval);
            }
            else if (type == "l_uint16")
            {
                if (!ushort.TryParse(sval.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort uval))
                    throw Lept.Fail(func, "custom tag(s) not written");
                RegisterUnknownCustomTag(tif, tag, TiffType.SHORT, passCount: false);
                tif.SetField(tag, uval);
            }
            else if (type == "l_uint32")
            {
                if (!uint.TryParse(sval.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint uval))
                    throw Lept.Fail(func, "custom tag(s) not written");
                RegisterUnknownCustomTag(tif, tag, TiffType.LONG, passCount: false);
                tif.SetField(tag, uval);
            }
            else if (type == "l_int32")
            {
                if (!int.TryParse(sval.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int val))
                    throw Lept.Fail(func, "custom tag(s) not written");
                RegisterUnknownCustomTag(tif, tag, TiffType.SLONG, passCount: false);
                tif.SetField(tag, val);
            }
            else if (type == "l_float64")
            {
                if (!double.TryParse(sval.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double dval))
                    throw Lept.Fail(func, "custom tag(s) not written");
                RegisterUnknownCustomTag(tif, tag, TiffType.DOUBLE, passCount: false);
                tif.SetField(tag, dval);
            }
            else if (type == "l_uint16-l_uint16")
            {
                string[] parts = sval.Split('-');
                if (parts.Length != 2 ||
                    !ushort.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort u1) ||
                    !ushort.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort u2))
                {
                    throw Lept.Fail(func, "custom tag(s) not written");
                }
                tif.SetField(tag, u1, u2);
            }
            else
            {
                throw Lept.Fail(func, "unknown type; tag(s) not written");
            }
        }
    }

    private static void RegisterUnknownCustomTag(Tiff tif, TiffTag tag, TiffType tiffType, bool passCount)
    {
        if (tif.FindFieldInfo(tag, TiffType.ANY) != null)
            return;

        short countSpec = (tiffType == TiffType.ASCII || passCount) ? TiffFieldInfo.Variable : (short)1;
        TiffFieldInfo[] custom =
        {
            new TiffFieldInfo(tag, countSpec, countSpec, tiffType, FieldBit.Custom, true, passCount, $"Tag{(int)tag}")
        };
        tif.MergeFieldInfo(custom, custom.Length);
    }

    /*--------------------------------------------------------------*
     *               Reading and writing multipage tiff             *
     *--------------------------------------------------------------*/
    // leptonica: pixReadFromMultipageTiff (tiffio.c)
    /// <summary>Reads the image at a given byte offset of a multipage TIFF file and advances the offset to the next image.</summary>
    /// <param name="fname">Path of the TIFF file.</param>
    /// <param name="poffset">The byte offset of the image to read, with 0 selecting the first image. On return it holds the offset of the next image, or 0 after the last image.</param>
    /// <returns>The decoded image, or <see langword="null"/> if the image cannot be read.</returns>
    /// <remarks>
    /// <para>Using the offset keeps the cost of traversing a multipage file linear in the number of images. This also works
    /// with a single-page file. For reading from a memory buffer, see <see cref="ReadMemFromMultipageTiff"/>.</para>
    /// <para>To read all the images, start with an offset of 0 and stop when the returned offset is 0.</para>
    /// </remarks>
    /// <exception cref="System.ArgumentException"><paramref name="fname"/> is <see langword="null"/>.</exception>
    public static Pix? ReadFromMultipageTiff(string fname, ref long poffset)
    {
        const string func = "pixReadFromMultipageTiff";
        if (fname == null)
            throw Lept.Arg(func, "fname not defined");

        using Tiff? tif = OpenTiffFile(fname, "r");
        if (tif == null)
            return null;

        long offset = poffset;
        bool ok = (offset == 0) ? tif.SetDirectory(0) : tif.SetSubDirectory(offset);
        if (!ok)
            return null;

        Pix? pix = ReadFromTiffHandle(tif);
        if (pix == null)
            return null;

        if (tif.ReadDirectory())
            poffset = tif.CurrentDirOffset();
        else
            poffset = 0;

        return pix;
    }

    // leptonica: writeMultipageTiff (tiffio.c)
    /// <summary>Writes the image files of a directory to a multipage TIFF file, in lexical order of the file names.</summary>
    /// <param name="dirin">The directory that holds the images.</param>
    /// <param name="substr">A substring that the file names must contain, or <see langword="null"/> to use all files.</param>
    /// <param name="fileout">Path of the multipage TIFF file to write.</param>
    /// <exception cref="System.ArgumentException"><paramref name="dirin"/> or <paramref name="fileout"/> is <see langword="null"/>.</exception>
    public static void WriteMultipageTiff(string dirin, string? substr, string fileout)
    {
        const string func = "writeMultipageTiff";
        if (dirin == null)
            throw Lept.Arg(func, "dirin not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");

        Sarray sa = Sarray.GetSortedPathnamesInDirectory(dirin, substr, 0, 0);
        WriteMultipageTiffSA(sa, fileout);
    }

    // leptonica: writeMultipageTiffSA (tiffio.c)
    /// <summary>Writes the image files named in a string array to a multipage TIFF file.</summary>
    /// <param name="sa">The path names of the image files.</param>
    /// <param name="fileout">Path of the multipage TIFF file to write.</param>
    /// <exception cref="System.ArgumentException"><paramref name="sa"/> or <paramref name="fileout"/> is <see langword="null"/>.</exception>
    public static void WriteMultipageTiffSA(Sarray sa, string fileout)
    {
        const string func = "writeMultipageTiffSA";
        if (sa == null)
            throw Lept.Arg(func, "sa not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");

        int nfiles = sa.Count;
        using Tiff? tif = OpenTiffFile(fileout, "w");
        if (tif == null)
            throw Lept.Fail(func, "tif not opened");

        int written = 0;
        for (int i = 0; i < nfiles; i++)
        {
            string fname = sa.GetString(i, L.L_NOCOPY);
            FindFileFormat(fname, out int format);
            if (format == L.IFF_UNKNOWN)
            {
                Log.Info(func, $"format of {fname} not known");
                continue;
            }

            Pix pix;
            try
            {
                pix = Read(fname);
            }
            catch (LeptonicaException)
            {
                Log.Warning(func, $"pix not made for file: {fname}");
                continue;
            }

            int comptype = (pix.Depth == 1) ? L.IFF_TIFF_G4 : L.IFF_TIFF_ZIP;
            WriteToTiffHandle(tif, pix, comptype, null, null, null, null, func);
            tif.WriteDirectory();
            written++;
        }
        tif.Close();
    }

    /*--------------------------------------------------------------*
     *                    Print info to stream                      *
     *--------------------------------------------------------------*/
    // leptonica: fprintTiffInfo (tiffio.c)
    /// <summary>Writes a description of the directory of a TIFF file to a stream.</summary>
    /// <param name="fpout">The stream that receives the text.</param>
    /// <param name="tiffile">Path of the TIFF file.</param>
    /// <exception cref="System.ArgumentException"><paramref name="fpout"/> or <paramref name="tiffile"/> is <see langword="null"/>.</exception>
    /// <exception cref="LeptonicaException">The TIFF file cannot be opened.</exception>
    public static void FprintTiffInfo(Stream fpout, string tiffile)
    {
        const string func = "fprintTiffInfo";
        if (tiffile == null)
            throw Lept.Arg(func, "tiffile not defined");
        if (fpout == null)
            throw Lept.Arg(func, "stream out not defined");

        using Tiff? tif = OpenTiffFile(tiffile, "r");
        if (tif == null)
            throw Lept.Fail(func, "tif not open for read");

        tif.PrintDirectory(fpout);
    }

    /*--------------------------------------------------------------*
     *                        Get page count                        *
     *--------------------------------------------------------------*/
    // leptonica: tiffGetCount (tiffio.c)
    /// <summary>Gets the number of pages in a TIFF image.</summary>
    /// <param name="fp">The stream to examine.</param>
    /// <param name="pn">Receives the number of pages.</param>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> is <see langword="null"/>.</exception>
    public static void TiffGetCount(Stream fp, out int pn)
    {
        const string func = "tiffGetCount";
        pn = 0;
        if (fp == null)
            throw Lept.Arg(func, "stream not defined");

        Stream srcStream = EnsureSeekableReadStream(fp, out MemoryStream? tempCopy);
        try
        {
            using Tiff? tif = FopenTiff(srcStream, "r");
            if (tif == null)
                throw Lept.Fail(func, "tif not open for read");

            int i = 1;
            while (tif.ReadDirectory())
            {
                i++;
                if (i == ManyPagesInTiffFile + 1)
                    Log.Warning(func, $"big file: more than {ManyPagesInTiffFile} pages");
            }
            pn = i;
        }
        finally
        {
            if (fp.CanSeek)
                fp.Position = 0;
            tempCopy?.Dispose();
        }
    }

    /*--------------------------------------------------------------*
     *                   Get resolution from tif                    *
     *--------------------------------------------------------------*/
    // leptonica: getTiffResolution (tiffio.c)
    /// <summary>Gets the resolution of a TIFF image.</summary>
    /// <param name="fp">The stream to examine.</param>
    /// <param name="pxres">Receives the horizontal resolution in ppi, or 0 if it is not specified.</param>
    /// <param name="pyres">Receives the vertical resolution in ppi, or 0 if it is not specified.</param>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> is <see langword="null"/>.</exception>
    public static void GetTiffResolution(Stream fp, out int pxres, out int pyres)
    {
        const string func = "getTiffResolution";
        pxres = pyres = 0;
        if (fp == null)
            throw Lept.Arg(func, "stream not opened");

        Stream srcStream = EnsureSeekableReadStream(fp, out MemoryStream? tempCopy);
        try
        {
            using Tiff? tif = FopenTiff(srcStream, "r");
            if (tif == null)
                throw Lept.Fail(func, "tif not open for read");
            TryGetTiffStreamResolution(tif, out pxres, out pyres);
        }
        finally
        {
            if (fp.CanSeek)
                fp.Position = 0;
            tempCopy?.Dispose();
        }
    }

    private static bool TryGetTiffStreamResolution(Tiff tif, out int pxres, out int pyres)
    {
        pxres = pyres = 0;
        FieldValue[]? unitField = tif.GetFieldDefaulted(TiffTag.RESOLUTIONUNIT);
        ResUnit resunit = (unitField != null && unitField.Length > 0)
            ? (ResUnit)unitField[0].ToInt()
            : ResUnit.INCH;

        FieldValue[]? xField = tif.GetField(TiffTag.XRESOLUTION);
        FieldValue[]? yField = tif.GetField(TiffTag.YRESOLUTION);
        bool foundX = xField != null && xField.Length > 0;
        bool foundY = yField != null && yField.Length > 0;
        if (!foundX && !foundY)
            return false;

        double fxres = foundX ? xField![0].ToDouble() : yField![0].ToDouble();
        double fyres = foundY ? yField![0].ToDouble() : fxres;
        if (double.IsNaN(fxres) || double.IsNaN(fyres) ||
            fxres < 0 || fxres > (1L << 29) || fyres < 0 || fyres > (1L << 29))
        {
            return false;
        }

        if (resunit == ResUnit.CENTIMETER)
        {
            pxres = (int)(2.54 * fxres + 0.5);
            pyres = (int)(2.54 * fyres + 0.5);
        }
        else
        {
            pxres = (int)(fxres + 0.5);
            pyres = (int)(fyres + 0.5);
        }
        return true;
    }

    /*--------------------------------------------------------------*
     *              Get some tiff header information                *
     *--------------------------------------------------------------*/
    // leptonica: readHeaderTiff (tiffio.c)
    /// <summary>Reads the header information of one page of a TIFF file.</summary>
    /// <param name="filename">Path of the TIFF file.</param>
    /// <param name="n">Zero-based page number.</param>
    /// <param name="pw">Receives the image width.</param>
    /// <param name="ph">Receives the image height.</param>
    /// <param name="pbps">Receives the number of bits per sample.</param>
    /// <param name="pspp">Receives the number of samples per pixel.</param>
    /// <param name="pres">Receives the resolution in ppi, or 0 if it is not specified.</param>
    /// <param name="pcmap">Receives <see langword="true"/> if the image has a colormap.</param>
    /// <param name="pformat">Receives the compression format, as an <c>IFF_TIFF*</c> constant.</param>
    /// <exception cref="System.ArgumentException"><paramref name="filename"/> is <see langword="null"/>.</exception>
    public static void ReadHeaderTiff(string filename, int n, out int pw, out int ph,
                                      out int pbps, out int pspp, out int pres,
                                      out bool pcmap, out int pformat)
    {
        const string func = "readHeaderTiff";
        pw = ph = pbps = pspp = pres = pformat = 0;
        pcmap = false;
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");

        using FileStream fp = Utils.FopenReadStream(filename);
        FreadHeaderTiff(fp, n, out pw, out ph, out pbps, out pspp, out pres, out pcmap, out pformat);
    }

    // leptonica: freadHeaderTiff (tiffio.c)
    /// <summary>Reads the header information of one page of a TIFF image from a stream.</summary>
    /// <param name="fp">The stream to read.</param>
    /// <param name="n">Zero-based page number.</param>
    /// <param name="pw">Receives the image width.</param>
    /// <param name="ph">Receives the image height.</param>
    /// <param name="pbps">Receives the number of bits per sample.</param>
    /// <param name="pspp">Receives the number of samples per pixel.</param>
    /// <param name="pres">Receives the resolution in ppi, or 0 if it is not specified.</param>
    /// <param name="pcmap">Receives <see langword="true"/> if the image has a colormap.</param>
    /// <param name="pformat">Receives the compression format, as an <c>IFF_TIFF*</c> constant.</param>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> is <see langword="null"/>.</exception>
    public static void FreadHeaderTiff(Stream fp, int n, out int pw, out int ph,
                                       out int pbps, out int pspp, out int pres,
                                       out bool pcmap, out int pformat)
    {
        const string func = "freadHeaderTiff";
        pw = ph = pbps = pspp = pres = pformat = 0;
        pcmap = false;
        if (fp == null)
            throw Lept.Arg(func, "stream not defined");
        if (n < 0)
            throw Lept.Arg(func, "image index must be >= 0");

        Stream srcStream = EnsureSeekableReadStream(fp, out MemoryStream? tempCopy);
        try
        {
            FindFileFormatStream(srcStream, out int format);
            if (!IsTiffFormatCode(format))
                throw Lept.Fail(func, "file not tiff format");

            using Tiff? tif = FopenTiff(srcStream, "r");
            if (tif == null)
                throw Lept.Fail(func, "tif not open for read");

            for (int i = 0; i < n; i++)
            {
                if (!tif.ReadDirectory())
                    throw Lept.Fail(func, "image n not found in file");
            }

            TiffReadHeaderCore(tif, func, out pw, out ph, out pbps, out pspp, out pres, out pcmap, out pformat);
        }
        finally
        {
            if (fp.CanSeek)
                fp.Position = 0;
            tempCopy?.Dispose();
        }
    }

    // leptonica: readHeaderMemTiff (tiffio.c)
    /// <summary>Reads the header information of one page of a TIFF image in memory.</summary>
    /// <param name="cdata">The encoded TIFF data.</param>
    /// <param name="n">Zero-based page number.</param>
    /// <param name="pw">Receives the image width.</param>
    /// <param name="ph">Receives the image height.</param>
    /// <param name="pbps">Receives the number of bits per sample.</param>
    /// <param name="pspp">Receives the number of samples per pixel.</param>
    /// <param name="pres">Receives the resolution in ppi, or 0 if it is not specified.</param>
    /// <param name="pcmap">Receives <see langword="true"/> if the image has a colormap.</param>
    /// <param name="pformat">Receives the compression format, as an <c>IFF_TIFF*</c> constant.</param>
    public static void ReadHeaderMemTiff(ReadOnlySpan<byte> cdata, int n, out int pw, out int ph,
                                         out int pbps, out int pspp, out int pres,
                                         out bool pcmap, out int pformat)
    {
        const string func = "readHeaderMemTiff";
        pw = ph = pbps = pspp = pres = pformat = 0;
        pcmap = false;
        if (cdata.IsEmpty)
            throw Lept.Arg(func, "cdata not defined");
        if (n < 0)
            throw Lept.Arg(func, "image index must be >= 0");

        using MemoryStream ms = new MemoryStream(cdata.ToArray(), false);
        FreadHeaderTiff(ms, n, out pw, out ph, out pbps, out pspp, out pres, out pcmap, out pformat);
    }

    private static void TiffReadHeaderCore(Tiff tif, string func, out int pw, out int ph,
                                           out int pbps, out int pspp, out int pres,
                                           out bool pcmap, out int pformat)
    {
        FieldValue[]? wField = tif.GetField(TiffTag.IMAGEWIDTH);
        FieldValue[]? hField = tif.GetField(TiffTag.IMAGELENGTH);
        if (wField == null || hField == null)
            throw Lept.Fail(func, "tif w and h not defined");

        int w = wField[0].ToInt();
        int h = hField[0].ToInt();
        FieldValue[]? bpsField = tif.GetFieldDefaulted(TiffTag.BITSPERSAMPLE);
        int bps = (bpsField != null && bpsField.Length > 0) ? bpsField[0].ToInt() : 1;
        FieldValue[]? sppField = tif.GetFieldDefaulted(TiffTag.SAMPLESPERPIXEL);
        int spp = (sppField != null && sppField.Length > 0) ? sppField[0].ToInt() : 1;

        if (w < 1 || h < 1)
            throw Lept.Fail(func, "tif w and h not both > 0");
        if (bps != 1 && bps != 2 && bps != 4 && bps != 8 && bps != 16)
            throw Lept.Fail(func, "bps not in set {1,2,4,8,16}");
        if (spp != 1 && spp != 2 && spp != 3 && spp != 4)
            throw Lept.Fail(func, "spp not in set {1,2,3,4}");

        pw = w;
        ph = h;
        pbps = bps;
        pspp = spp;
        pres = TryGetTiffStreamResolution(tif, out int xres, out _) ? xres : 0;
        pcmap = tif.GetField(TiffTag.COLORMAP) != null;

        FieldValue[]? compField = tif.GetFieldDefaulted(TiffTag.COMPRESSION);
        Compression tiffcomp = (compField != null && compField.Length > 0)
            ? (Compression)compField[0].ToInt()
            : Compression.NONE;
        pformat = GetTiffCompressedFormat(tiffcomp);
    }

    // leptonica: findTiffCompression (tiffio.c)
    /// <summary>Finds the compression used by a TIFF image.</summary>
    /// <param name="fp">The stream to examine.</param>
    /// <param name="pcomptype">Receives the compression format, as an <c>IFF_TIFF*</c> constant.</param>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> is <see langword="null"/>.</exception>
    public static void FindTiffCompression(Stream fp, out int pcomptype)
    {
        const string func = "findTiffCompression";
        pcomptype = L.IFF_UNKNOWN;
        if (fp == null)
            throw Lept.Arg(func, "stream not defined");

        Stream srcStream = EnsureSeekableReadStream(fp, out MemoryStream? tempCopy);
        try
        {
            using Tiff? tif = FopenTiff(srcStream, "r");
            if (tif == null)
                throw Lept.Fail(func, "tif not opened");

            FieldValue[]? compField = tif.GetFieldDefaulted(TiffTag.COMPRESSION);
            Compression tiffcomp = (compField != null && compField.Length > 0)
                ? (Compression)compField[0].ToInt()
                : Compression.NONE;
            pcomptype = GetTiffCompressedFormat(tiffcomp);
        }
        finally
        {
            if (fp.CanSeek)
                fp.Position = 0;
            tempCopy?.Dispose();
        }
    }

    /*--------------------------------------------------------------*
     *                   Extraction of tiff g4 data                 *
     *--------------------------------------------------------------*/
    // leptonica: extractG4DataFromFile (tiffio.c)
    /// <summary>Extracts the CCITT Group 4 encoded data from a TIFF file.</summary>
    /// <param name="filein">Path of the TIFF file, which must be G4 compressed.</param>
    /// <param name="pw">Receives the image width.</param>
    /// <param name="ph">Receives the image height.</param>
    /// <param name="pminisblack">Receives the photometric interpretation: nonzero if 0 means black.</param>
    /// <returns>The G4 encoded data.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="filein"/> is <see langword="null"/>.</exception>
    public static byte[] ExtractG4DataFromFile(string filein, out int pw, out int ph, out int pminisblack)
    {
        const string func = "extractG4DataFromFile";
        pw = ph = pminisblack = 0;
        if (filein == null)
            throw Lept.Arg(func, "filein not defined");

        using (FileStream fpin = Utils.FopenReadStream(filein))
        {
            if (!FileFormatIsTiff(fpin))
                throw Lept.Fail(func, $"filein not tiff: {filein}");
        }

        using Tiff? tif = OpenTiffFile(filein, "r");
        if (tif == null)
            throw Lept.Fail(func, $"tif not open for read: {filein}");

        FieldValue[]? compField = tif.GetFieldDefaulted(TiffTag.COMPRESSION);
        Compression comptype = (compField != null && compField.Length > 0)
            ? (Compression)compField[0].ToInt()
            : Compression.NONE;
        if (comptype != Compression.CCITTFAX4)
            throw Lept.Fail(func, $"filein is not g4 compressed: {filein}");

        FieldValue[]? wField = tif.GetField(TiffTag.IMAGEWIDTH);
        FieldValue[]? hField = tif.GetField(TiffTag.IMAGELENGTH);
        if (wField == null || hField == null)
            throw Lept.Fail(func, "missing width or height");
        pw = wField[0].ToInt();
        ph = hField[0].ToInt();

        FieldValue[]? rpsField = tif.GetField(TiffTag.ROWSPERSTRIP);
        if (rpsField != null && rpsField.Length > 0 && rpsField[0].ToInt() != ph)
            Log.Warning(func, "more than 1 strip");

        FieldValue[]? photoField = tif.GetFieldDefaulted(TiffTag.PHOTOMETRIC);
        Photometric photo = (photoField != null && photoField.Length > 0)
            ? (Photometric)photoField[0].ToInt()
            : Photometric.MINISWHITE;
        pminisblack = (photo == Photometric.MINISBLACK) ? 1 : 0;

        int nstrips = tif.NumberOfStrips();
        if (nstrips <= 0)
            throw Lept.Fail(func, "no strips found in g4 tiff");

        using MemoryStream rawOut = new MemoryStream();
        for (int s = 0; s < nstrips; s++)
        {
            long stripSize = tif.RawStripSize(s);
            if (stripSize < 0 || rawOut.Length + stripSize > MaxNumTiffBytes)
                throw Lept.Fail(func, "g4 strip data too large");
            byte[] stripBuf = new byte[(int)stripSize];
            int nread = tif.ReadRawStrip(s, stripBuf, 0, (int)stripSize);
            if (nread < 0)
                throw Lept.Fail(func, "failed reading raw g4 strip");
            rawOut.Write(stripBuf, 0, nread);
        }

        return rawOut.ToArray();
    }

    /*--------------------------------------------------------------*
     *                   Read and write to memory                   *
     *--------------------------------------------------------------*/
    // leptonica: pixReadMemTiff (tiffio.c)
    /// <summary>Reads one page of a TIFF image from memory.</summary>
    /// <param name="cdata">The encoded TIFF data.</param>
    /// <param name="n">Zero-based page number; use 0 for a single-page image.</param>
    /// <returns>The decoded image, or <see langword="null"/> if the page cannot be read.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="cdata"/> is empty.</exception>
    public static Pix? ReadMemTiff(ReadOnlySpan<byte> cdata, int n = 0)
    {
        const string func = "pixReadMemTiff";
        if (cdata.IsEmpty)
            throw Lept.Arg(func, "cdata not defined");
        if (n < 0)
            return null;

        using MemoryStream ms = new MemoryStream(cdata.ToArray(), false);
        using Tiff? tif = FopenTiff(ms, "r");
        if (tif == null)
            throw Lept.Fail(func, "tiff stream not opened");

        for (int i = 0; ; i++)
        {
            if (i == n)
            {
                Pix? pix = ReadFromTiffHandle(tif);
                return pix;
            }
            if (!tif.ReadDirectory())
                break;
            if (i == ManyPagesInTiffFile + 1)
                Log.Warning(func, $"big file: more than {ManyPagesInTiffFile} pages");
        }
        return null;
    }

    /// <summary>Reads one page of a TIFF image from memory.</summary>
    /// <param name="cdata">The encoded TIFF data.</param>
    /// <param name="n">Zero-based page number; use 0 for a single-page image.</param>
    /// <returns>The decoded image, or <see langword="null"/> if the page cannot be read.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="cdata"/> is <see langword="null"/>.</exception>
    public static Pix? ReadMemTiff(byte[] cdata, int n = 0)
    {
        if (cdata == null)
            throw Lept.Arg("pixReadMemTiff", "cdata not defined");
        return ReadMemTiff(cdata.AsSpan(), n);
    }

    // leptonica: pixReadMemFromMultipageTiff (tiffio.c)
    /// <summary>Reads the image at a given byte offset of a multipage TIFF in memory and advances the offset to the next image.</summary>
    /// <param name="cdata">The encoded multipage TIFF data.</param>
    /// <param name="poffset">The byte offset of the image to read, with 0 selecting the first image. On return it holds the offset of the next image, or 0 after the last image.</param>
    /// <returns>The decoded image, or <see langword="null"/> if the image cannot be read.</returns>
    /// <remarks>See <see cref="ReadFromMultipageTiff"/> for how to traverse all the images.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="cdata"/> is empty.</exception>
    public static Pix? ReadMemFromMultipageTiff(ReadOnlySpan<byte> cdata, ref long poffset)
    {
        const string func = "pixReadMemFromMultipageTiff";
        if (cdata.IsEmpty)
            throw Lept.Arg(func, "cdata not defined");

        using MemoryStream ms = new MemoryStream(cdata.ToArray(), false);
        using Tiff? tif = FopenTiff(ms, "r");
        if (tif == null)
            throw Lept.Fail(func, "tiff stream not opened");

        long offset = poffset;
        bool ok = (offset == 0) ? tif.SetDirectory(0) : tif.SetSubDirectory(offset);
        if (!ok)
            return null;

        Pix? pix = ReadFromTiffHandle(tif);
        if (pix == null)
            return null;

        if (tif.ReadDirectory())
            poffset = tif.CurrentDirOffset();
        else
            poffset = 0;

        return pix;
    }

    // leptonica: pixWriteMemTiff (tiffio.c)
    /// <summary>Encodes an image in TIFF format.</summary>
    /// <param name="pix">The image to encode; any depth is accepted, and a colormap is removed.</param>
    /// <param name="comptype">The compression: <see cref="L.IFF_TIFF"/>, <see cref="L.IFF_TIFF_PACKBITS"/>, <see cref="L.IFF_TIFF_RLE"/>,
    /// <see cref="L.IFF_TIFF_G3"/>, <see cref="L.IFF_TIFF_G4"/>, <see cref="L.IFF_TIFF_LZW"/>, <see cref="L.IFF_TIFF_ZIP"/> or <see cref="L.IFF_TIFF_JPEG"/>.</param>
    /// <returns>The TIFF-encoded data.</returns>
    public static byte[] WriteMemTiff(Pix pix, int comptype)
    {
        return WriteMemTiffCustom(pix, comptype, null, null, null, null);
    }

    // leptonica: pixWriteMemTiffCustom (tiffio.c)
    /// <summary>Encodes an image in TIFF format, with optional custom TIFF tags.</summary>
    /// <param name="pix">The image to encode; any depth is accepted, and a colormap is removed.</param>
    /// <param name="comptype">The compression: <see cref="L.IFF_TIFF"/>, <see cref="L.IFF_TIFF_PACKBITS"/>, <see cref="L.IFF_TIFF_RLE"/>,
    /// <see cref="L.IFF_TIFF_G3"/>, <see cref="L.IFF_TIFF_G4"/>, <see cref="L.IFF_TIFF_LZW"/>, <see cref="L.IFF_TIFF_ZIP"/> or <see cref="L.IFF_TIFF_JPEG"/>.</param>
    /// <param name="natags">The numeric identifiers of the custom tags, or <see langword="null"/> for none.</param>
    /// <param name="savals">The tag values in string format, or <see langword="null"/> for none.</param>
    /// <param name="satypes">The tag types in string format, or <see langword="null"/> for none.</param>
    /// <param name="nasizes">The number of elements for tags whose value is an array of bytes, or <see langword="null"/> for none.</param>
    /// <returns>The TIFF-encoded data.</returns>
    /// <remarks>See <see cref="WriteTiffCustom"/> for the rules on the custom tag arrays.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="pix"/> is <see langword="null"/>.</exception>
    public static byte[] WriteMemTiffCustom(Pix pix, int comptype,
                                            Numa? natags, Sarray? savals, Sarray? satypes, Numa? nasizes)
    {
        const string func = "pixWriteMemTiffCustom";
        if (pix == null)
            throw Lept.Arg(func, "&pix not defined");

        using MemoryStream ms = new MemoryStream();
        using (Tiff? tif = FopenTiff(ms, "w"))
        {
            if (tif == null)
                throw Lept.Fail(func, "tiff stream not opened");
            WriteToTiffHandle(tif, pix, comptype, natags, savals, satypes, nasizes, func);
            tif.Close();
        }
        return ms.ToArray();
    }
}

public sealed partial class Pixa
{
    // leptonica: pixaReadMultipageTiff (tiffio.c)
    /// <summary>Reads all the pages of a multipage TIFF file.</summary>
    /// <param name="filename">Path of the TIFF file.</param>
    /// <returns>A <see cref="Pixa"/> that holds one image for each page.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="filename"/> is <see langword="null"/>.</exception>
    public static Pixa ReadMultipageTiff(string filename)
    {
        const string func = "pixaReadMultipageTiff";
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");

        using FileStream fp = Utils.FopenReadStream(filename);
        if (!Pix.FileFormatIsTiff(fp))
            throw Lept.Fail(func, $"file is not tiff: {filename}");

        Pix.TiffGetCount(fp, out int npages);
        Log.Info(func, $" Tiff: {npages} pages");

        using Tiff? tif = Pix.FopenTiff(fp, "r");
        if (tif == null)
            throw Lept.Fail(func, $"tif not opened: {filename}");

        Pixa pixa = Create(npages);
        for (int i = 0; i < npages; i++)
        {
            Pix? pix = Pix.ReadFromTiffHandle(tif);
            if (pix != null)
                pixa.AddPix(pix, L.L_INSERT);
            else
                Log.Warning(func, $"pix not read for page {i}");

            if (!tif.ReadDirectory())
                break;
        }
        return pixa;
    }

    // leptonica: pixaWriteMultipageTiff (tiffio.c)
    /// <summary>Writes the images of a <see cref="Pixa"/> to a multipage TIFF file.</summary>
    /// <param name="fname">Path of the TIFF file to write.</param>
    /// <param name="pixa">The images to write, one for each page.</param>
    /// <exception cref="System.ArgumentException"><paramref name="fname"/> or <paramref name="pixa"/> is <see langword="null"/>.</exception>
    public static void WriteMultipageTiff(string fname, Pixa pixa)
    {
        const string func = "pixaWriteMultipageTiff";
        if (fname == null)
            throw Lept.Arg(func, "fname not defined");
        if (pixa == null)
            throw Lept.Arg(func, "pixa not defined");

        int n = pixa.Count;
        using Tiff? tif = Pix.OpenTiffFile(fname, "w");
        if (tif == null)
            throw Lept.Fail(func, $"tif not opened: {fname}");

        for (int i = 0; i < n; i++)
        {
            Pix pix1 = pixa.GetPix(i, L.L_CLONE);
            int comptype = (pix1.Depth == 1) ? L.IFF_TIFF_G4 : L.IFF_TIFF_ZIP;
            Pix.WriteToTiffHandle(tif, pix1, comptype, null, null, null, null, func);
            tif.WriteDirectory();
        }
        tif.Close();
    }

    // leptonica: pixaReadMemMultipageTiff (tiffio.c)
    /// <summary>Reads all the pages of a multipage TIFF image in memory.</summary>
    /// <param name="data">The encoded multipage TIFF data.</param>
    /// <returns>A <see cref="Pixa"/> that holds one image for each page.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="data"/> is empty.</exception>
    public static Pixa ReadMemMultipageTiff(ReadOnlySpan<byte> data)
    {
        const string func = "pixaReadMemMultipageTiff";
        if (data.IsEmpty)
            throw Lept.Arg(func, "data not defined");

        using MemoryStream ms = new MemoryStream(data.ToArray(), false);
        using Tiff? tif = Pix.FopenTiff(ms, "r");
        if (tif == null)
            throw Lept.Fail(func, "tiff stream not opened");

        Pixa pixa = Create(0);
        do
        {
            Pix? pix = Pix.ReadFromTiffHandle(tif);
            if (pix != null)
                pixa.AddPix(pix, L.L_INSERT);
        } while (tif.ReadDirectory());

        return pixa;
    }

    /// <summary>Reads all the pages of a multipage TIFF image in memory.</summary>
    /// <param name="data">The encoded multipage TIFF data.</param>
    /// <returns>A <see cref="Pixa"/> that holds one image for each page.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="data"/> is <see langword="null"/>.</exception>
    public static Pixa ReadMemMultipageTiff(byte[] data)
    {
        if (data == null)
            throw Lept.Arg("pixaReadMemMultipageTiff", "data not defined");
        return ReadMemMultipageTiff(data.AsSpan());
    }

    // leptonica: pixaWriteMemMultipageTiff (tiffio.c)
    /// <summary>Encodes the images of a <see cref="Pixa"/> as a multipage TIFF image.</summary>
    /// <param name="pixa">The images to encode, one for each page.</param>
    /// <returns>The multipage TIFF-encoded data.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="pixa"/> is <see langword="null"/>.</exception>
    public static byte[] WriteMemMultipageTiff(Pixa pixa)
    {
        const string func = "pixaWriteMemMultipageTiff";
        if (pixa == null)
            throw Lept.Arg(func, "pixa not defined");

        using MemoryStream ms = new MemoryStream();
        using (Tiff? tif = Pix.FopenTiff(ms, "w"))
        {
            if (tif == null)
                throw Lept.Fail(func, "tiff stream not opened");
            int n = pixa.Count;
            for (int i = 0; i < n; i++)
            {
                Pix pix1 = pixa.GetPix(i, L.L_CLONE);
                int comptype = (pix1.Depth == 1) ? L.IFF_TIFF_G4 : L.IFF_TIFF_ZIP;
                Pix.WriteToTiffHandle(tif, pix1, comptype, null, null, null, null, func);
                tif.WriteDirectory();
            }
            tif.Close();
        }
        return ms.ToArray();
    }
}
