using System;
using System.IO;
using Leptonica.Internal.Gif;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  gifio.c
 *
 *    Read gif from stream
 *          PIX        *pixReadStreamGif()
 *          PIX        *pixReadMemGif()
 *
 *    Write gif to stream
 *          l_int32     pixWriteStreamGif()
 *          l_int32     pixWriteMemGif()
 */
public sealed partial class Pix
{
    /*---------------------------------------------------------------------*
     *                            Reading gif                              *
     *---------------------------------------------------------------------*/
    // leptonica: pixReadStreamGif (gifio.c)
    /// <summary>Reads a GIF image from a stream.</summary>
    /// <param name="fp">The stream to read; if it is seekable, reading starts at the beginning.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> is <see langword="null"/>, or the data is not a valid GIF image.</exception>
    public static Pix ReadStreamGif(Stream fp)
    {
        const string func = "pixReadStreamGif";
        if (fp == null)
            throw Lept.Arg(func, "fp not defined");

        if (fp.CanSeek)
            fp.Position = 0;

        byte[] filedata = Utils.BinaryReadStream(fp);
        return ReadMemGif(filedata);
    }

    // leptonica: pixReadMemGif (gifio.c)
    /// <summary>Reads a GIF image from memory.</summary>
    /// <param name="cdata">The encoded GIF data.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="cdata"/> is empty, or the data is not a valid GIF image.</exception>
    public static Pix ReadMemGif(ReadOnlySpan<byte> cdata)
    {
        const string func = "pixReadMemGif";
        if (cdata.IsEmpty)
            throw Lept.Arg(func, "cdata not defined");

        GifDecodedImage decoded;
        try
        {
            decoded = GifCodec.Decode(cdata);
        }
        catch (GifException ex)
        {
            if (ex.IsAnimatedUnsupported)
                Log.Error(func, "gifanim is not supported");
            throw Lept.Fail(func, ex.Message);
        }

        int w = decoded.Width;
        int h = decoded.Height;
        int ncolors = decoded.ColorCount;
        if (ncolors <= 0 || ncolors > 256)
            throw Lept.Fail(func, "ncolors is invalid");

        int d;
        if (ncolors <= 2)
            d = 1;
        else if (ncolors <= 4)
            d = 2;
        else if (ncolors <= 16)
            d = 4;
        else
            d = 8;

        PixColormap cmap = PixColormap.Create(d);
        byte[] pal = decoded.PaletteRgb;
        for (int cindex = 0; cindex < ncolors; cindex++)
        {
            int rval = pal[3 * cindex];
            int gval = pal[3 * cindex + 1];
            int bval = pal[3 * cindex + 2];
            cmap.AddColor(rval, gval, bval);
        }

        Pix pixd = Create(w, h, d);
        pixd.InputFormat = L.IFF_GIF;
        if (!cmap.IsValid(pixd))
            throw Lept.Fail(func, "colormap is invalid");
        pixd.SetColormap(cmap);

        int wpl = pixd.Wpl;
        uint[] data = pixd.Data;
        byte[] raster = decoded.Indices;

        for (int i = 0; i < h; i++)
        {
            Span<uint> line = data.AsSpan(i * wpl, wpl);
            int rowBase = i * w;
            if (d == 1)
            {
                for (int j = 0; j < w; j++)
                {
                    if (raster[rowBase + j] != 0)
                        SetDataBit(line, j);
                }
            }
            else if (d == 2)
            {
                for (int j = 0; j < w; j++)
                    SetDataDibit(line, j, raster[rowBase + j] & 3);
            }
            else if (d == 4)
            {
                for (int j = 0; j < w; j++)
                    SetDataQbit(line, j, raster[rowBase + j] & 15);
            }
            else
            {
                for (int j = 0; j < w; j++)
                    SetDataByte(line, j, raster[rowBase + j]);
            }
        }

        return pixd;
    }

    /// <summary>Reads a GIF image from memory.</summary>
    /// <param name="cdata">The encoded GIF data.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="cdata"/> is <see langword="null"/>, or the data is not a valid GIF image.</exception>
    public static Pix ReadMemGif(byte[] cdata)
    {
        if (cdata == null)
            throw Lept.Arg("pixReadMemGif", "cdata not defined");
        return ReadMemGif(cdata.AsSpan());
    }

    /*---------------------------------------------------------------------*
     *                            Writing gif                              *
     *---------------------------------------------------------------------*/
    // leptonica: pixWriteStreamGif (gifio.c)
    /// <summary>Writes an image to a stream in GIF format.</summary>
    /// <param name="fp">The stream to write; if it is seekable, writing starts at the beginning.</param>
    /// <param name="pix">The image to write.</param>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> or <paramref name="pix"/> is <see langword="null"/>.</exception>
    public static void WriteStreamGif(Stream fp, Pix pix)
    {
        const string func = "pixWriteStreamGif";
        if (fp == null)
            throw Lept.Arg(func, "stream not open");
        if (pix == null)
            throw Lept.Arg(func, "pix not defined");

        pix.SetPadBits(0);
        byte[] filedata = WriteMemGif(pix);

        if (fp.CanSeek)
            fp.Position = 0;
        fp.Write(filedata, 0, filedata.Length);
    }

    // leptonica: pixWriteMemGif (gifio.c)
    /// <summary>Encodes an image in GIF format.</summary>
    /// <param name="pix">The image to encode.</param>
    /// <returns>The GIF-encoded data.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="pix"/> is <see langword="null"/>.</exception>
    public static byte[] WriteMemGif(Pix pix)
    {
        const string func = "pixWriteMemGif";
        if (pix == null)
            throw Lept.Arg(func, "&pix not defined");

        int d = pix.Depth;
        Pix pixd;
        if (d == 32)
        {
            pixd = pix.ConvertRGBToColormap(1);
        }
        else if (d > 1)
        {
            pixd = pix.ConvertTo8(true);
        }
        else
        {
            pixd = Copy(null, pix);
            if (pixd.Colormap == null)
            {
                PixColormap cmap1 = PixColormap.Create(1);
                cmap1.AddColor(255, 255, 255);
                cmap1.AddColor(0, 0, 0);
                pixd.SetColormap(cmap1);
            }
        }

        d = pixd.Depth;
        PixColormap? cmap = pixd.Colormap;
        if (cmap == null)
            throw Lept.Fail(func, "cmap is missing");
        if (!cmap.IsValid(pixd))
            throw Lept.Fail(func, "colormap is not valid");
        if (d != 1 && d != 2 && d != 4 && d != 8)
            throw Lept.Fail(func, "image depth is not in {1, 2, 4, 8}");

        int ncolor = cmap.Count;
        int colorBits = 1;
        for (int i = 1; i <= 8; i++)
        {
            if ((1 << i) >= ncolor)
            {
                colorBits = i;
                break;
            }
        }
        int gifNcolor = 1 << colorBits;

        byte[] paletteRgb = new byte[gifNcolor * 3];
        for (int i = 0; i < ncolor && i < gifNcolor; i++)
        {
            cmap.GetColor(i, out int rval, out int gval, out int bval);
            paletteRgb[3 * i] = (byte)rval;
            paletteRgb[3 * i + 1] = (byte)gval;
            paletteRgb[3 * i + 2] = (byte)bval;
        }

        pixd.GetDimensions(out int w, out int h, out _);
        uint[] data = pixd.Data;
        int wpl = pixd.Wpl;
        byte[] indices = new byte[(long)w * h];

        for (int i = 0; i < h; i++)
        {
            ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
            int rowBase = i * w;
            switch (d)
            {
                case 8:
                    for (int j = 0; j < w; j++)
                        indices[rowBase + j] = (byte)GetDataByte(line, j);
                    break;
                case 4:
                    for (int j = 0; j < w; j++)
                        indices[rowBase + j] = (byte)GetDataQbit(line, j);
                    break;
                case 2:
                    for (int j = 0; j < w; j++)
                        indices[rowBase + j] = (byte)GetDataDibit(line, j);
                    break;
                case 1:
                    for (int j = 0; j < w; j++)
                        indices[rowBase + j] = (byte)GetDataBit(line, j);
                    break;
            }
        }

        using MemoryStream ms = new MemoryStream();
        try
        {
            GifCodec.Encode(ms, w, h, paletteRgb, colorBits, indices, pix.Text);
        }
        catch (GifException ex)
        {
            throw Lept.Fail(func, ex.Message);
        }
        return ms.ToArray();
    }
}
