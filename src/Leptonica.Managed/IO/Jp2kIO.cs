using System;
using System.Globalization;
using System.IO;
using CoreJ2K;
using CoreJ2K.Configuration;
using CoreJ2K.j2k.codestream;
using CoreJ2K.j2k.codestream.reader;
using CoreJ2K.j2k.fileformat.metadata;
using CoreJ2K.j2k.fileformat.reader;
using CoreJ2K.j2k.util;
using CoreJ2K.Util;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  jp2kio.c
 *
 *    Read jp2k from file
 *          PIX                *pixReadJp2k()  [special top level]
 *          PIX                *pixReadStreamJp2k()
 *
 *    Write jp2k to file
 *          l_int32             pixWriteJp2k()  [special top level]
 *          l_int32             pixWriteStreamJp2k()
 *
 *    Read/write to memory
 *          PIX                *pixReadMemJp2k()
 *          l_int32             pixWriteMemJp2k()
 */
public sealed partial class Pix
{
    static Pix()
    {
        /* Silence CoreJ2K console chatter unless explicitly requested. */
        FacilityManager.DefaultMsgLogger = new QuietJp2kLogger();
    }

    private sealed class QuietJp2kLogger : IMsgLogger
    {
        public void printmsg(int sev, string msg) { }
        public void println(string str, int flind, int ind) { }
        public void flush() { }
    }

    /*---------------------------------------------------------------------*
     *                        Read jp2k from file                          *
     *---------------------------------------------------------------------*/
    // leptonica: pixReadJp2k (jp2kio.c)
    /// <summary>Reads a JPEG 2000 image from a file.</summary>
    /// <param name="filename">Path of the file to read.</param>
    /// <param name="reduction">Scaling factor: 1, 2, 4, 8 or 16.</param>
    /// <param name="box">A region to extract, or <see langword="null"/> to read the whole image.</param>
    /// <param name="hint">Decoding hints as a bitwise OR of <c>L_JP2K_*</c> values; 0 for the default.</param>
    /// <param name="debug">Nonzero to output diagnostic messages.</param>
    /// <returns>The decoded 8 bpp or 32 bpp image.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="filename"/> is <see langword="null"/>.</exception>
    /// <exception cref="LeptonicaException">The reduction is not a power of 2, the image is not 8 bits per sample, or decoding fails.</exception>
    public static Pix ReadJp2k(string filename, int reduction = 1, Box? box = null, int hint = 0, int debug = 0)
    {
        const string func = "pixReadJp2k";
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");

        using FileStream fp = Utils.FopenReadStream(filename);
        return ReadStreamJp2k(fp, reduction, box, hint, debug);
    }

    // leptonica: pixReadStreamJp2k (jp2kio.c)
    /// <summary>Reads a JPEG 2000 image from a stream.</summary>
    /// <param name="fp">The stream to read; if it is seekable, reading starts at the beginning.</param>
    /// <param name="reduction">Scaling factor: 1, 2, 4, 8 or 16.</param>
    /// <param name="box">A region to extract, or <see langword="null"/> to read the whole image.</param>
    /// <param name="hint">Decoding hints as a bitwise OR of <c>L_JP2K_*</c> values; 0 for the default.</param>
    /// <param name="debug">Nonzero to output diagnostic messages.</param>
    /// <returns>The decoded 8 bpp or 32 bpp image.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> is <see langword="null"/>.</exception>
    /// <exception cref="LeptonicaException">The reduction is not a power of 2, the image is not 8 bits per sample, or decoding fails.</exception>
    public static Pix ReadStreamJp2k(Stream fp, int reduction = 1, Box? box = null, int hint = 0, int debug = 0)
    {
        const string func = "pixReadStreamJp2k";
        if (fp == null)
            throw Lept.Arg(func, "fp not defined");

        if (fp.CanSeek)
            fp.Position = 0;
        byte[] data = Utils.BinaryReadStream(fp);
        return ReadMemJp2k(data, reduction, box, hint, debug);
    }

    // leptonica: pixReadMemJp2k (jp2kio.c)
    /// <summary>Reads a JPEG 2000 image from memory.</summary>
    /// <param name="bytes">The encoded JPEG 2000 data.</param>
    /// <param name="reduction">Scaling factor: 1, 2, 4, 8 or 16.</param>
    /// <param name="box">A region to extract, or <see langword="null"/> to read the whole image.</param>
    /// <param name="hint">Decoding hints as a bitwise OR of <c>L_JP2K_*</c> values; 0 for the default.</param>
    /// <param name="debug">Nonzero to output diagnostic messages.</param>
    /// <returns>The decoded 8 bpp or 32 bpp image.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="bytes"/> is empty.</exception>
    /// <exception cref="LeptonicaException">The reduction is not a power of 2, the image is not 8 bits per sample, or decoding fails.</exception>
    public static Pix ReadMemJp2k(ReadOnlySpan<byte> bytes, int reduction = 1, Box? box = null, int hint = 0, int debug = 0)
    {
        const string func = "pixReadMemJp2k";
        if (bytes.IsEmpty)
            throw Lept.Arg(func, "data not defined");
        if (reduction < 1)
            throw Lept.Fail(func, $"invalid reduction {reduction}; not power of 2");

        int reduce = 0;
        while ((1L << reduce) < reduction)
            reduce++;
        if ((1L << reduce) != reduction)
        {
            Log.Error(func, $"invalid reduction {reduction}; not power of 2");
            throw Lept.Fail(func, $"invalid reduction {reduction}; not power of 2");
        }

        ReadResolutionMemJp2k(bytes, out int xres, out int yres);
        ReadHeaderMemJp2k(bytes, out _, out _, out int bps, out _, out int codec);
        if (codec != L.L_J2K_CODEC && codec != L.L_JP2_CODEC)
            throw Lept.Fail(func, "valid codec not identified");
        if (bps != 8)
            throw Lept.Fail(func, $"found {bps} bps; can only handle 8 bps");

        byte[] raw = bytes.ToArray();

        try
        {
            int maxDecompLevels = GetJp2kMaxDecompLevels(raw);
            if (reduce > maxDecompLevels)
            {
                Log.Error(func, "failed to read the header");
                throw Lept.Fail(func, "failed to read the header");
            }

            J2KDecoderConfiguration cfg = new J2KDecoderConfiguration()
                .WithVerbose(false)
                .WithColorSpace(true)
                .WithResolutionLevel(maxDecompLevels - reduce);

            using InterleavedImage img = J2kImage.FromBytes(raw, cfg);
            int fullW = img.Width;
            int fullH = img.Height;
            int spp = img.NumberOfComponents;
            if (spp < 1 || spp > 4)
                throw Lept.Fail(func, $"unsupported spp = {spp}");

            int rx = 0, ry = 0, rw = fullW, rh = fullH;
            if (box != null)
            {
                box.GetGeometry(out int bx, out int by, out int bw, out int bh);
                if (bx < 0 || by < 0 || bw <= 0 || bh <= 0)
                    throw Lept.Fail(func, "failed to set the region for decoding");

                int x0 = bx / reduction;
                int y0 = by / reduction;
                int x1 = (bx + bw + reduction - 1) / reduction;
                int y1 = (by + bh + reduction - 1) / reduction;
                x0 = Math.Clamp(x0, 0, fullW);
                y0 = Math.Clamp(y0, 0, fullH);
                x1 = Math.Clamp(x1, 0, fullW);
                y1 = Math.Clamp(y1, 0, fullH);
                if (x1 <= x0 || y1 <= y0)
                    throw Lept.Fail(func, "failed to set the region for decoding");
                rx = x0;
                ry = y0;
                rw = x1 - x0;
                rh = y1 - y0;
            }

            Pix pix = (spp == 1) ? Create(rw, rh, 8) : Create(rw, rh, 32);
            if (spp == 2 || spp == 4)
                pix.Spp = 4;

            pix.InputFormat = L.IFF_JP2;
            pix.SetResolution(xres, yres);

            uint[] pdata = pix.Data;
            int wpl = pix.Wpl;
            int[] samples = img.Data;

            if (spp == 1)
            {
                for (int i = 0; i < rh; i++)
                {
                    Span<uint> line = pdata.AsSpan(i * wpl, wpl);
                    int srcRow = (ry + i) * fullW + rx;
                    for (int j = 0; j < rw; j++)
                    {
                        int val = Math.Clamp(samples[srcRow + j], 0, 255);
                        SetDataByte(line, j, val);
                    }
                }
            }
            else if (spp == 2)
            {
                for (int i = 0; i < rh; i++)
                {
                    int lineIdx = i * wpl;
                    int srcBase = ((ry + i) * fullW + rx) * 2;
                    for (int j = 0; j < rw; j++)
                    {
                        int gray = Math.Clamp(samples[srcBase + 2 * j], 0, 255);
                        int aval = Math.Clamp(samples[srcBase + 2 * j + 1], 0, 255);
                        ComposeRGBAPixel(gray, gray, gray, aval, out pdata[lineIdx + j]);
                    }
                }
            }
            else if (spp == 3)
            {
                for (int i = 0; i < rh; i++)
                {
                    int lineIdx = i * wpl;
                    int srcBase = ((ry + i) * fullW + rx) * 3;
                    for (int j = 0; j < rw; j++)
                    {
                        int rval = Math.Clamp(samples[srcBase + 3 * j], 0, 255);
                        int gval = Math.Clamp(samples[srcBase + 3 * j + 1], 0, 255);
                        int bval = Math.Clamp(samples[srcBase + 3 * j + 2], 0, 255);
                        ComposeRGBPixel(rval, gval, bval, out pdata[lineIdx + j]);
                    }
                }
            }
            else // spp == 4
            {
                for (int i = 0; i < rh; i++)
                {
                    int lineIdx = i * wpl;
                    int srcBase = ((ry + i) * fullW + rx) * 4;
                    for (int j = 0; j < rw; j++)
                    {
                        int rval = Math.Clamp(samples[srcBase + 4 * j], 0, 255);
                        int gval = Math.Clamp(samples[srcBase + 4 * j + 1], 0, 255);
                        int bval = Math.Clamp(samples[srcBase + 4 * j + 2], 0, 255);
                        int aval = Math.Clamp(samples[srcBase + 4 * j + 3], 0, 255);
                        ComposeRGBAPixel(rval, gval, bval, aval, out pdata[lineIdx + j]);
                    }
                }
            }

            return pix;
        }
        catch (LeptonicaException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Lept.Fail(func, $"failed to decode jp2k image: {ex.Message}");
        }
    }

    /// <summary>Reads a JPEG 2000 image from memory.</summary>
    /// <param name="data">The encoded JPEG 2000 data.</param>
    /// <param name="reduction">Scaling factor: 1, 2, 4, 8 or 16.</param>
    /// <param name="box">A region to extract, or <see langword="null"/> to read the whole image.</param>
    /// <param name="hint">Decoding hints as a bitwise OR of <c>L_JP2K_*</c> values; 0 for the default.</param>
    /// <param name="debug">Nonzero to output diagnostic messages.</param>
    /// <returns>The decoded 8 bpp or 32 bpp image.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="data"/> is <see langword="null"/>.</exception>
    /// <exception cref="LeptonicaException">The reduction is not a power of 2, the image is not 8 bits per sample, or decoding fails.</exception>
    public static Pix ReadMemJp2k(byte[] data, int reduction = 1, Box? box = null, int hint = 0, int debug = 0)
    {
        if (data == null)
            throw Lept.Arg("pixReadMemJp2k", "data not defined");
        return ReadMemJp2k(data.AsSpan(), reduction, box, hint, debug);
    }

    private static int GetJp2kMaxDecompLevels(byte[] raw)
    {
        using MemoryStream ms = new MemoryStream(raw, false);
        ISRandomAccessIO io = new ISRandomAccessIO(ms);
        FileFormatReader ff = new FileFormatReader(io);
        ff.readFileFormat();
        if (ff.JP2FFUsed)
            io.seek(ff.FirstCodeStreamPos);

        ParameterList pl = new ParameterList(J2kImage.GetDefaultDecoderParameterList(null!));
        HeaderInfo hi = new HeaderInfo();
        HeaderDecoder hd = new HeaderDecoder(io, pl, hi);
        return hd.DecoderSpecs.dls.Min;
    }

    /*---------------------------------------------------------------------*
     *                        Write jp2k to file                           *
     *---------------------------------------------------------------------*/
    // leptonica: pixWriteJp2k (jp2kio.c)
    /// <summary>Writes an image to a file in JPEG 2000 (JP2) format.</summary>
    /// <param name="filename">Path of the file to write.</param>
    /// <param name="pix">The image to write; any depth is accepted, and a colormap is removed.</param>
    /// <param name="quality">Signal-to-noise ratio, greater than 0: 0 for the default of 34, and 100 for lossless.</param>
    /// <param name="nlevels">Number of resolution levels: 6 or 7; 0 for the default of 6.</param>
    /// <param name="hint">Encoding hints as a bitwise OR of <c>L_JP2K_*</c> values; 0 for the default.</param>
    /// <param name="debug">Nonzero to output diagnostic messages.</param>
    /// <exception cref="System.ArgumentException"><paramref name="pix"/> or <paramref name="filename"/> is <see langword="null"/>.</exception>
    public static void WriteJp2k(string filename, Pix pix, int quality = 34, int nlevels = 0, int hint = 0, int debug = 0)
    {
        const string func = "pixWriteJp2k";
        if (pix == null)
            throw Lept.Arg(func, "pix not defined");
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");

        using FileStream fp = Utils.FopenWriteStream(filename, "wb+");
        WriteStreamJp2k(fp, pix, quality, nlevels, L.L_JP2_CODEC, hint, debug);
    }

    // leptonica: pixWriteStreamJp2k (jp2kio.c)
    /// <summary>Writes an image to a stream in JPEG 2000 format.</summary>
    /// <param name="fp">The stream to write; if it is seekable, writing starts at the beginning.</param>
    /// <param name="pix">The image to write; any depth is accepted, and a colormap is removed.</param>
    /// <param name="quality">Signal-to-noise ratio, greater than 0: 0 for the default of 34, and 100 for lossless.</param>
    /// <param name="nlevels">Number of resolution levels, at most 10; 0 for the default.</param>
    /// <param name="codec">The output format: <see cref="L.L_J2K_CODEC"/> for a raw codestream or <see cref="L.L_JP2_CODEC"/> for the file format.</param>
    /// <param name="hint">Encoding hints as a bitwise OR of <c>L_JP2K_*</c> values; 0 for the default.</param>
    /// <param name="debug">Nonzero to output diagnostic messages.</param>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> or <paramref name="pix"/> is <see langword="null"/>, or <paramref name="codec"/> is not valid.</exception>
    public static void WriteStreamJp2k(Stream fp, Pix pix, int quality = 34, int nlevels = 0, int codec = L.L_JP2_CODEC, int hint = 0, int debug = 0)
    {
        const string func = "pixWriteStreamJp2k";
        if (fp == null)
            throw Lept.Arg(func, "stream not open");
        if (pix == null)
            throw Lept.Arg(func, "pix not defined");

        byte[] encoded = EncodeJp2kCore(pix, quality, nlevels, codec, func);
        if (fp.CanSeek)
            fp.Position = 0;
        fp.Write(encoded, 0, encoded.Length);
    }

    // leptonica: pixWriteMemJp2k (jp2kio.c)
    /// <summary>Encodes an image in JPEG 2000 (JP2) format.</summary>
    /// <param name="pix">The image to encode; any depth is accepted, and a colormap is removed.</param>
    /// <param name="quality">Signal-to-noise ratio, greater than 0: 0 for the default of 34, and 100 for lossless.</param>
    /// <param name="nlevels">Number of resolution levels: 6 or 7; 0 for the default of 6.</param>
    /// <param name="hint">Encoding hints as a bitwise OR of <c>L_JP2K_*</c> values; 0 for the default.</param>
    /// <param name="debug">Nonzero to output diagnostic messages.</param>
    /// <returns>The JPEG 2000 encoded data.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="pix"/> is <see langword="null"/>.</exception>
    public static byte[] WriteMemJp2k(Pix pix, int quality = 34, int nlevels = 0, int hint = 0, int debug = 0)
    {
        const string func = "pixWriteMemJp2k";
        if (pix == null)
            throw Lept.Arg(func, "&pix not defined");

        return EncodeJp2kCore(pix, quality, nlevels, L.L_JP2_CODEC, func);
    }

    private static byte[] EncodeJp2kCore(Pix pix, int quality, int nlevels, int codec, string func)
    {
        if (codec != L.L_JP2_CODEC && codec != L.L_J2K_CODEC)
            throw Lept.Arg(func, "valid codec not identified");

        double snr = quality;
        if (snr <= 0.0)
            snr = 34.0;
        if (snr > 0.0 && snr < 27.0)
            Log.Warning(func, $"SNR = {(int)snr} < 27; very low");
        bool lossless = false;
        if (snr == 100.0)
        {
            lossless = true;
        }
        else if (snr > 45.0)
        {
            Log.Warning(func, "SNR > 45; using lossless encoding");
            lossless = true;
        }

        if (nlevels == 0)
            nlevels = 6;
        if (nlevels < 6)
        {
            Log.Warning(func, $"nlevels = {nlevels} < 6; setting to 6");
            nlevels = 6;
        }
        if (nlevels > 7)
        {
            Log.Warning(func, $"nlevels = {nlevels} > 7; setting to 7");
            nlevels = 7;
        }

        pix.GetDimensions(out int w, out int h, out int d);
        Pix pixs;
        if (d == 24)
        {
            pixs = pix.Convert24To32();
        }
        else if (d == 32)
        {
            pixs = Clone(pix);
        }
        else if (pix.Colormap == null)
        {
            pixs = pix.ConvertTo8(false);
        }
        else
        {
            Log.Info(func, "removing colormap; may be better to compress losslessly");
            pixs = pix.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        }

        int depth = pixs.Depth; // 8 or 32
        int spp = (depth == 32) ? ((pixs.Spp == 4) ? 4 : 3) : 1;

        // Reduce nlevels (number of resolution levels) if image has small dimensions.
        for (int i = 1; i < 7; i++)
        {
            if (w < (1 << i) || h < (1 << i))
            {
                if (i < nlevels)
                {
                    Log.Info(func, $"small image: w = {w}, h = {h}; setting nlevels to {i}");
                    nlevels = i;
                }
                break;
            }
        }
        int decompLevels = Math.Max(0, nlevels - 1);

        pixs.SetPadBits(0);
        uint[] pdata = pixs.Data;
        int wpl = pixs.Wpl;
        int npix = w * h;

        // CoreJ2K InterleavedImageSource expects unsigned 8-bit samples shifted by -128 (matching ImgReaderPGM.DC_OFFSET = 128).
        const int DcOffset = 128;
        int[][] comps = new int[spp][];
        bool[] sgnd = new bool[spp];
        for (int c = 0; c < spp; c++)
            comps[c] = new int[npix];

        if (spp == 1)
        {
            int[] c0 = comps[0];
            for (int i = 0; i < h; i++)
            {
                ReadOnlySpan<uint> line = pdata.AsSpan(i * wpl, wpl);
                int rowBase = i * w;
                for (int j = 0; j < w; j++)
                    c0[rowBase + j] = GetDataByte(line, j) - DcOffset;
            }
        }
        else if (spp == 3)
        {
            int[] c0 = comps[0], c1 = comps[1], c2 = comps[2];
            for (int i = 0; i < h; i++)
            {
                int lineIdx = i * wpl;
                int rowBase = i * w;
                for (int j = 0; j < w; j++)
                {
                    uint word = pdata[lineIdx + j];
                    c0[rowBase + j] = (int)((word >> L.L_RED_SHIFT) & 0xff) - DcOffset;
                    c1[rowBase + j] = (int)((word >> L.L_GREEN_SHIFT) & 0xff) - DcOffset;
                    c2[rowBase + j] = (int)((word >> L.L_BLUE_SHIFT) & 0xff) - DcOffset;
                }
            }
        }
        else // spp == 4
        {
            int[] c0 = comps[0], c1 = comps[1], c2 = comps[2], c3 = comps[3];
            for (int i = 0; i < h; i++)
            {
                int lineIdx = i * wpl;
                int rowBase = i * w;
                for (int j = 0; j < w; j++)
                {
                    uint word = pdata[lineIdx + j];
                    c0[rowBase + j] = (int)((word >> L.L_RED_SHIFT) & 0xff) - DcOffset;
                    c1[rowBase + j] = (int)((word >> L.L_GREEN_SHIFT) & 0xff) - DcOffset;
                    c2[rowBase + j] = (int)((word >> L.L_BLUE_SHIFT) & 0xff) - DcOffset;
                    c3[rowBase + j] = (int)((word >> L.L_ALPHA_SHIFT) & 0xff) - DcOffset;
                }
            }
        }

        InterleavedImageSource imgsrc = new InterleavedImageSource(w, h, spp, 8, sgnd, comps);

        ParameterList pl = new ParameterList(J2kImage.GetDefaultEncoderParameterList(null!));
        pl["file_format"] = (codec == L.L_JP2_CODEC) ? "on" : "off";
        pl["Wlev"] = decompLevels.ToString(CultureInfo.InvariantCulture);
        pl["Mct"] = (spp >= 3) ? "on" : "off";

        if (lossless)
        {
            pl["lossless"] = "on";
            pl["Ffilters"] = "w5x3";
            pl["Qtype"] = "reversible";
            pl["rate"] = "-1";
        }
        else
        {
            pl["lossless"] = "off";
            pl["Ffilters"] = "w9x7";
            pl["Qtype"] = "expounded";
            // Map Leptonica SNR [20..45 dB] (default 34 dB) to target bits/pixel
            double clampedSnr = Math.Clamp(snr, 20.0, 45.0);
            double chanScale = (spp >= 3) ? 2.0 : 1.0;
            double bpp = chanScale * 0.25 * Math.Pow(2.0, (clampedSnr - 22.0) / 4.5);
            double minBytes = (codec == L.L_JP2_CODEC) ? 384.0 : 256.0;
            double minBpp = (minBytes * 8.0) / Math.Max(1.0, (double)w * h);
            if (bpp < minBpp)
                bpp = minBpp;
            if (bpp >= 8.0 * spp)
            {
                pl["lossless"] = "on";
                pl["Ffilters"] = "w5x3";
                pl["Qtype"] = "reversible";
                pl["rate"] = "-1";
            }
            else
            {
                pl["rate"] = bpp.ToString("F4", CultureInfo.InvariantCulture);
            }
        }

        J2KMetadata? metadata = null;
        if (codec == L.L_JP2_CODEC && pix.XRes > 0 && pix.YRes > 0)
        {
            metadata = new J2KMetadata();
            ResolutionData resData = new ResolutionData();
            resData.SetCaptureDpi(pix.XRes, pix.YRes);
            metadata.Resolution = resData;
        }

        try
        {
            return J2kImage.ToBytes(imgsrc, metadata, pl);
        }
        catch (Exception ex)
        {
            throw Lept.Fail(func, $"failed to encode jp2k image: {ex.Message}");
        }
    }
}
