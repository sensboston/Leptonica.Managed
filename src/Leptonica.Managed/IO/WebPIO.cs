using System;
using System.IO;
using System.Runtime.InteropServices;
using Leptonica.Internal.WebP;

namespace Leptonica;

/*
 *  webpio.c
 *
 *    Reading WebP
 *          PIX             *pixReadStreamWebP()
 *          PIX             *pixReadMemWebP()
 *
 *    Reading WebP header
 *          l_int32          readHeaderWebP()
 *          l_int32          readHeaderMemWebP()
 *
 *    Writing WebP
 *          l_int32          pixWriteWebP()  [ special top level ]
 *          l_int32          pixWriteStreamWebP()
 *          l_int32          pixWriteMemWebP()
 */
public sealed partial class Pix
{
    /*---------------------------------------------------------------------*
     *                             Reading WebP                            *
     *---------------------------------------------------------------------*/
    // leptonica: pixReadStreamWebP (webpio.c)
    /// <summary>Reads a WebP image from a stream.</summary>
    /// <param name="fp">The stream to read; if it is seekable, reading starts at the beginning.</param>
    /// <returns>The decoded 32 bpp image.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> is <see langword="null"/>.</exception>
    /// <exception cref="LeptonicaException">The data is not a valid WebP image.</exception>
    public static Pix ReadStreamWebP(Stream fp)
    {
        const string func = "pixReadStreamWebP";
        if (fp == null)
            throw Lept.Arg(func, "fp not defined");

        if (fp.CanSeek)
            fp.Position = 0;

        byte[] filedata = Utils.BinaryReadStream(fp);
        return ReadMemWebP(filedata);
    }

    // leptonica: pixReadMemWebP (webpio.c)
    /// <summary>Reads a WebP image from memory.</summary>
    /// <param name="filedata">The encoded WebP data.</param>
    /// <returns>The decoded 32 bpp image.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="filedata"/> is empty.</exception>
    /// <exception cref="LeptonicaException">The data is not a valid WebP image.</exception>
    public static Pix ReadMemWebP(ReadOnlySpan<byte> filedata)
    {
        const string func = "pixReadMemWebP";
        if (filedata.IsEmpty)
            throw Lept.Arg(func, "filedata not defined");

        if (!WebPContainer.TryDecodeRgba(filedata, out byte[] rgba, out int w, out int h, out bool hasAlpha))
            throw Lept.Fail(func, "WebP decode failed");

        Pix pix = Create(w, h, 32);
        pix.InputFormat = L.IFF_WEBP;
        if (hasAlpha)
            pix.Spp = 4;

        uint[] data = pix.Data;
        int wpl = pix.Wpl;
        for (int y = 0; y < h; y++)
        {
            int srcRow = y * w * 4;
            int dstRow = y * wpl;
            for (int x = 0; x < w; x++)
            {
                int k = srcRow + x * 4;
                if (hasAlpha)
                    ComposeRGBAPixel(rgba[k], rgba[k + 1], rgba[k + 2], rgba[k + 3], out data[dstRow + x]);
                else
                    ComposeRGBAPixel(rgba[k], rgba[k + 1], rgba[k + 2], 255, out data[dstRow + x]);
            }
        }
        return pix;
    }

    /// <summary>Reads a WebP image from memory.</summary>
    /// <param name="filedata">The encoded WebP data.</param>
    /// <returns>The decoded 32 bpp image.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="filedata"/> is <see langword="null"/>.</exception>
    /// <exception cref="LeptonicaException">The data is not a valid WebP image.</exception>
    public static Pix ReadMemWebP(byte[] filedata)
    {
        if (filedata == null)
            throw Lept.Arg("pixReadMemWebP", "filedata not defined");
        return ReadMemWebP(filedata.AsSpan());
    }

    /*---------------------------------------------------------------------*
     *                         Reading WebP header                         *
     *---------------------------------------------------------------------*/
    // leptonica: readHeaderWebP (webpio.c)
    /// <summary>Reads the header information of a WebP file.</summary>
    /// <param name="filename">Path of the WebP file.</param>
    /// <param name="pw">Receives the image width.</param>
    /// <param name="ph">Receives the image height.</param>
    /// <param name="pspp">Receives the number of samples per pixel: 3 without alpha, 4 with alpha.</param>
    /// <exception cref="System.ArgumentException"><paramref name="filename"/> is <see langword="null"/>.</exception>
    /// <exception cref="LeptonicaException">The file is empty, cannot be read, or is not a valid WebP file.</exception>
    public static void ReadHeaderWebP(string filename, out int pw, out int ph, out int pspp)
    {
        const string func = "readHeaderWebP";
        pw = ph = pspp = 0;
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");

        long filesize = Utils.NbytesInFile(filename);
        if (filesize == 0)
            throw Lept.Fail(func, $"no file size found: {filename}");
        if (filesize < 100)
            Log.Warning(func, $"very small webp file: {filename}");

        int nbytes = (int)Math.Min(filesize, 100L);
        byte[] data = new byte[nbytes];
        using (FileStream fp = Utils.FopenReadStream(filename))
        {
            int bytesread = fp.Read(data, 0, nbytes);
            if (bytesread != nbytes)
                throw Lept.Fail(func, "failed to read requested data");
        }

        ReadHeaderMemWebP(data, out pw, out ph, out pspp);
    }

    // leptonica: readHeaderMemWebP (webpio.c)
    /// <summary>Reads the header information of a WebP image in memory.</summary>
    /// <param name="data">The encoded WebP data; the first 100 bytes are sufficient.</param>
    /// <param name="pw">Receives the image width.</param>
    /// <param name="ph">Receives the image height.</param>
    /// <param name="pspp">Receives the number of samples per pixel: 3 without alpha, 4 with alpha.</param>
    /// <exception cref="System.ArgumentException"><paramref name="data"/> is empty.</exception>
    /// <exception cref="LeptonicaException">The data is not a valid WebP image.</exception>
    public static void ReadHeaderMemWebP(ReadOnlySpan<byte> data, out int pw, out int ph, out int pspp)
    {
        const string func = "readHeaderMemWebP";
        pw = ph = pspp = 0;
        if (data.IsEmpty)
            throw Lept.Arg(func, "data not defined");

        if (!WebPContainer.TryGetFeatures(data, out pw, out ph, out bool hasAlpha))
            throw Lept.Fail(func, "invalid WebP file");

        pspp = hasAlpha ? 4 : 3;
    }

    /*---------------------------------------------------------------------*
     *                            Writing WebP                             *
     *---------------------------------------------------------------------*/
    // leptonica: pixWriteWebP (webpio.c)
    /// <summary>Writes an image to a file in WebP format, with a specified quality.</summary>
    /// <param name="filename">Path of the file to write.</param>
    /// <param name="pixs">The image to write; images of all depths are accepted, and a colormap is removed.</param>
    /// <param name="quality">Quality from 0 to 100 for lossy encoding; a typical value is 80.</param>
    /// <param name="lossless">1 for lossless encoding; 0 for lossy encoding.</param>
    /// <exception cref="System.ArgumentException"><paramref name="pixs"/> or <paramref name="filename"/> is <see langword="null"/>, or <paramref name="quality"/> is out of range for lossy encoding.</exception>
    public static void WriteWebP(string filename, Pix pixs, int quality, int lossless)
    {
        const string func = "pixWriteWebP";
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");

        using FileStream fp = Utils.FopenWriteStream(filename, "wb+");
        WriteStreamWebP(fp, pixs, quality, lossless);
    }

    // leptonica: pixWriteStreamWebP (webpio.c)
    /// <summary>Writes an image to a stream in WebP format.</summary>
    /// <param name="fp">The stream to write; if it is seekable, writing starts at the beginning.</param>
    /// <param name="pixs">The image to write; images of all depths are accepted, and a colormap is removed.</param>
    /// <param name="quality">Quality from 0 to 100 for lossy encoding; a typical value is 80.</param>
    /// <param name="lossless">1 for lossless encoding; 0 for lossy encoding.</param>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> or <paramref name="pixs"/> is <see langword="null"/>, or <paramref name="quality"/> is out of range for lossy encoding.</exception>
    public static void WriteStreamWebP(Stream fp, Pix pixs, int quality, int lossless)
    {
        const string func = "pixWriteStreamWebP";
        if (fp == null)
            throw Lept.Arg(func, "stream not open");
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");

        pixs.SetPadBits(0);
        byte[] filedata = WriteMemWebP(pixs, quality, lossless);
        if (fp.CanSeek)
            fp.Position = 0;
        fp.Write(filedata, 0, filedata.Length);
        fp.Flush();
    }

    // leptonica: pixWriteMemWebP (webpio.c)
    /// <summary>Encodes an image in WebP format.</summary>
    /// <param name="pixs">The image to encode; images of all depths are accepted, and a colormap is removed.</param>
    /// <param name="quality">Quality from 0 to 100 for lossy encoding; a typical value is 80. It is ignored for lossless encoding.</param>
    /// <param name="lossless">1 for lossless encoding; 0 for lossy encoding.</param>
    /// <returns>The WebP-encoded data.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="pixs"/> is <see langword="null"/>, or <paramref name="quality"/> is out of range for lossy encoding.</exception>
    /// <exception cref="LeptonicaException">The image dimensions exceed the WebP maximum of 16383, or encoding fails.</exception>
    public static byte[] WriteMemWebP(Pix pixs, int quality, int lossless)
    {
        const string func = "pixWriteMemWebP";
        if (pixs == null)
            throw Lept.Arg(func, "&pixs not defined");
        if (lossless == 0 && (quality < 0 || quality > 100))
            throw Lept.Arg(func, "quality not in [0 ... 100]");

        Pix pix1 = pixs.RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR);
        Pix pix2 = pix1.Depth != 32 ? pix1.ConvertTo32() : Copy(null, pix1);
        pix2.GetDimensions(out int w, out int h, out int d);
        if (w <= 0 || h <= 0 || d != 32)
            throw Lept.Fail(func, "pix2 not 32 bpp or of 0 size");
        if (w > 16383 || h > 16383)
            throw Lept.Fail(func, "WebP dimensions exceed maximum 16383");

        if (pix2.Spp == 3)
            pix2.SetComponentArbitrary(L.L_ALPHA_CHANNEL, 255);

        pix2.EndianByteSwap();
        int stride = pix2.Wpl * 4;
        ReadOnlySpan<byte> rgbaBytes = MemoryMarshal.AsBytes(pix2.Data.AsSpan());
        return WebPContainer.EncodeStillWebP(rgbaBytes, w, h, stride, quality, lossless != 0);
    }
}
