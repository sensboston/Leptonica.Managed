using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Leptonica.Internal.WebP;

namespace Leptonica;

/*
 *  webpanimio.c
 *
 *    Writing animated WebP
 *          l_int32          pixaWriteWebPAnim()
 *          l_int32          pixaWriteStreamWebPAnim()
 *          l_int32          pixaWriteMemWebPAnim()
 */
public sealed partial class Pixa
{
    // leptonica: pixaWriteWebPAnim (webpanimio.c)
    /// <summary>Writes the images of a <see cref="Pixa"/> to a file as an animated WebP image.</summary>
    /// <param name="filename">Path of the file to write.</param>
    /// <param name="pixa">The images to write; images of all depths are accepted, and colormaps are removed.</param>
    /// <param name="loopcount">Number of times the animation loops; 0 for infinite.</param>
    /// <param name="duration">Display time of each image, in milliseconds.</param>
    /// <param name="quality">Quality from 0 to 100 for lossy encoding; a typical value is 80.</param>
    /// <param name="lossless">1 for lossless encoding; 0 for lossy encoding.</param>
    /// <exception cref="System.ArgumentException"><paramref name="filename"/> or <paramref name="pixa"/> is <see langword="null"/>, or <paramref name="quality"/> is out of range for lossy encoding.</exception>
    public static void WriteWebPAnim(string filename, Pixa pixa, int loopcount, int duration, int quality, int lossless)
    {
        const string func = "pixaWriteWebPAnim";
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");
        if (pixa == null)
            throw Lept.Arg(func, "pixa not defined");

        using FileStream fp = Utils.FopenWriteStream(filename, "wb+");
        WriteStreamWebPAnim(fp, pixa, loopcount, duration, quality, lossless);
    }

    // leptonica: pixaWriteStreamWebPAnim (webpanimio.c)
    /// <summary>Writes the images of a <see cref="Pixa"/> to a stream as an animated WebP image.</summary>
    /// <param name="fp">The stream to write; if it is seekable, writing starts at the beginning.</param>
    /// <param name="pixa">The images to write; images of all depths are accepted, and colormaps are removed.</param>
    /// <param name="loopcount">Number of times the animation loops; 0 for infinite.</param>
    /// <param name="duration">Display time of each image, in milliseconds.</param>
    /// <param name="quality">Quality from 0 to 100 for lossy encoding; a typical value is 80.</param>
    /// <param name="lossless">1 for lossless encoding; 0 for lossy encoding.</param>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> or <paramref name="pixa"/> is <see langword="null"/>, or <paramref name="quality"/> is out of range for lossy encoding.</exception>
    public static void WriteStreamWebPAnim(Stream fp, Pixa pixa, int loopcount, int duration, int quality, int lossless)
    {
        const string func = "pixaWriteStreamWebPAnim";
        if (fp == null)
            throw Lept.Arg(func, "stream not open");
        if (pixa == null)
            throw Lept.Arg(func, "pixa not defined");

        byte[] filedata = WriteMemWebPAnim(pixa, loopcount, duration, quality, lossless);
        if (fp.CanSeek)
            fp.Position = 0;
        fp.Write(filedata, 0, filedata.Length);
        fp.Flush();
    }

    // leptonica: pixaWriteMemWebPAnim (webpanimio.c)
    /// <summary>Encodes the images of a <see cref="Pixa"/> as an animated WebP image.</summary>
    /// <param name="pixa">The images to encode; images of all depths are accepted, and colormaps are removed.</param>
    /// <param name="loopcount">Number of times the animation loops; 0 for infinite, and a negative value is treated as 0.</param>
    /// <param name="duration">Display time of each image, in milliseconds.</param>
    /// <param name="quality">Quality from 0 to 100 for lossy encoding; a typical value is 80.</param>
    /// <param name="lossless">1 for lossless encoding; 0 for lossy encoding.</param>
    /// <returns>The animated WebP-encoded data.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="pixa"/> is <see langword="null"/> or empty, or <paramref name="quality"/> is out of range for lossy encoding.</exception>
    public static byte[] WriteMemWebPAnim(Pixa pixa, int loopcount, int duration, int quality, int lossless)
    {
        const string func = "pixaWriteMemWebPAnim";
        if (pixa == null)
            throw Lept.Arg(func, "&pixa not defined");
        int n = pixa.Count;
        if (n == 0)
            throw Lept.Arg(func, "no images in pixa");
        if (loopcount < 0)
            loopcount = 0;
        if (lossless == 0 && (quality < 0 || quality > 100))
            throw Lept.Arg(func, "quality not in [0 ... 100]");

        pixa.VerifyDimensions(out bool same, out int w, out int h);
        if (!same)
            throw Lept.Fail(func, "sizes of all pix are not the same");
        if (w <= 0 || h <= 0 || w > 16383 || h > 16383)
            throw Lept.Fail(func, "invalid WebP animation dimensions");

        List<byte[]> framesRgba = new List<byte[]>(n);
        for (int i = 0; i < n; i++)
        {
            Pix pix1 = pixa.GetPix(i, L.L_CLONE);
            Pix pix2 = pix1.ConvertTo32();
            pix2.SetComponentArbitrary(L.L_ALPHA_CHANNEL, 255);
            pix2.EndianByteSwap();

            int srcStride = pix2.Wpl * 4;
            int dstStride = w * 4;
            byte[] rgba = new byte[dstStride * h];
            ReadOnlySpan<byte> srcBytes = MemoryMarshal.AsBytes(pix2.Data.AsSpan());
            for (int y = 0; y < h; y++)
                srcBytes.Slice(y * srcStride, dstStride).CopyTo(rgba.AsSpan(y * dstStride, dstStride));

            framesRgba.Add(rgba);
        }

        byte[] encoded = WebPContainer.EncodeAnimatedWebP(framesRgba, w, h, loopcount, duration, quality, lossless != 0);
        Log.Info(func, $"data size = {encoded.Length}\n");
        return encoded;
    }
}
