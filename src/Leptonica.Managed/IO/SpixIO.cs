using System;
using System.Buffers.Binary;
using System.IO;

namespace Leptonica;

/*
 *  spixio.c:
 *
 *      Reading spix from file
 *           PIX        *pixReadStreamSpix()
 *           l_int32     readHeaderSpix()
 *           l_int32     freadHeaderSpix()
 *           l_int32     sreadHeaderSpix()
 *
 *      Writing spix to file
 *           l_int32     pixWriteStreamSpix()
 *
 *      Low-level serialization of pix to/from memory (uncompressed)
 *           PIX        *pixReadMemSpix()
 *           l_int32     pixWriteMemSpix()
 *           l_int32     pixSerializeToMemory()
 *           PIX        *pixDeserializeFromMemory()
 */

public sealed partial class Pix
{
        /* Image dimension limits */
    private const int SpixMaxAllowedWidth = 1000000;
    private const int SpixMaxAllowedHeight = 1000000;
    private const long SpixMaxAllowedArea = 400000000L;

    /*-----------------------------------------------------------------------*
     *                          Reading spix from file                       *
     *-----------------------------------------------------------------------*/
    // leptonica: pixReadStreamSpix (spixio.c)
    /// <summary>Reads a serialized image (SPIX format) from a stream.</summary>
    /// <param name="fp">The stream to read.</param>
    /// <returns>The deserialized image.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> is <see langword="null"/>.</exception>
    /// <exception cref="LeptonicaException">The data is not a valid SPIX image.</exception>
    public static Pix ReadStreamSpix(Stream fp)
    {
        const string func = "pixReadStreamSpix";
        if (fp == null)
            throw Lept.Arg(func, "stream not defined");

        byte[] data = Utils.BinaryReadStream(fp);
        return ReadMemSpix(data);
    }

    // leptonica: readHeaderSpix (spixio.c)
    /// <summary>Reads the header information of a SPIX file.</summary>
    /// <param name="filename">Path of the SPIX file.</param>
    /// <param name="pwidth">Receives the image width.</param>
    /// <param name="pheight">Receives the image height.</param>
    /// <param name="pbps">Receives the number of bits per sample.</param>
    /// <param name="pspp">Receives the number of samples per pixel.</param>
    /// <param name="piscmap">Receives <see langword="true"/> if the image has a colormap.</param>
    /// <exception cref="System.ArgumentException"><paramref name="filename"/> is <see langword="null"/>.</exception>
    /// <exception cref="LeptonicaException">The file is too small or is not a valid SPIX file.</exception>
    public static void ReadHeaderSpix(string filename, out int pwidth, out int pheight,
                                      out int pbps, out int pspp, out bool piscmap)
    {
        const string func = "readHeaderSpix";
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");

        using FileStream fp = Utils.FopenReadStream(filename);
        FreadHeaderSpix(fp, out pwidth, out pheight, out pbps, out pspp, out piscmap);
    }

    // leptonica: freadHeaderSpix (spixio.c)
    /// <summary>Reads the header information of a SPIX image from a stream.</summary>
    /// <param name="fp">The stream to read.</param>
    /// <param name="pwidth">Receives the image width.</param>
    /// <param name="pheight">Receives the image height.</param>
    /// <param name="pbps">Receives the number of bits per sample.</param>
    /// <param name="pspp">Receives the number of samples per pixel.</param>
    /// <param name="piscmap">Receives <see langword="true"/> if the image has a colormap.</param>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> is <see langword="null"/>.</exception>
    /// <exception cref="LeptonicaException">The data is too small or is not a valid SPIX image.</exception>
    public static void FreadHeaderSpix(Stream fp, out int pwidth, out int pheight,
                                       out int pbps, out int pspp, out bool piscmap)
    {
        const string func = "freadHeaderSpix";
        if (fp == null)
            throw Lept.Arg(func, "stream not defined");

        int nbytes = (int)Utils.FnbytesInFile(fp);
        if (nbytes < 32)
            throw Lept.Fail(func, "file too small to be spix");
        Span<byte> data = stackalloc byte[24];
        if (fp.ReadAtLeast(data, 24, false) != 24)
            throw Lept.Fail(func, "error reading data");
        SreadHeaderSpixCore(data, nbytes, out pwidth, out pheight, out pbps, out pspp, out piscmap);
    }

    // leptonica: sreadHeaderSpix (spixio.c)
    /// <summary>Reads the header information of a SPIX image in memory.</summary>
    /// <param name="data">The serialized image data; at least the first 24 bytes are needed.</param>
    /// <param name="pwidth">Receives the image width.</param>
    /// <param name="pheight">Receives the image height.</param>
    /// <param name="pbps">Receives the number of bits per sample.</param>
    /// <param name="pspp">Receives the number of samples per pixel.</param>
    /// <param name="piscmap">Receives <see langword="true"/> if the image has a colormap.</param>
    /// <exception cref="LeptonicaException">The data is too small or is not a valid SPIX image.</exception>
    public static void SreadHeaderSpix(ReadOnlySpan<byte> data, out int pwidth, out int pheight,
                                       out int pbps, out int pspp, out bool piscmap)
    {
        SreadHeaderSpixCore(data, data.Length, out pwidth, out pheight, out pbps, out pspp, out piscmap);
    }

    private static void SreadHeaderSpixCore(ReadOnlySpan<byte> data, int size, out int pwidth, out int pheight,
                                            out int pbps, out int pspp, out bool piscmap)
    {
        const string func = "sreadHeaderSpix";
        pwidth = pheight = pbps = pspp = 0;
        piscmap = false;
        if (size < 28 || data.Length < 24)
            throw Lept.Fail(func, "size too small");

            /* Check file id */
        if (data[0] != (byte)'s' || data[1] != (byte)'p' ||
            data[2] != (byte)'i' || data[3] != (byte)'x')
            throw Lept.Fail(func, "not a valid spix file");

        pwidth = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4, 4));
        pheight = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(8, 4));
        int d = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(12, 4));
        if (d <= 16) {
            pbps = d;
            pspp = 1;
        } else {
            pbps = 8;
            pspp = d / 8;  /* if the pix is 32 bpp, call it 4 samples */
        }
        int ncolors = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(20, 4));
        piscmap = ncolors != 0;
    }

    /*-----------------------------------------------------------------------*
     *                            Writing spix to file                       *
     *-----------------------------------------------------------------------*/
    // leptonica: pixWriteStreamSpix (spixio.c)
    /// <summary>Writes an image to a stream in the serialized SPIX format.</summary>
    /// <param name="fp">The stream to write.</param>
    /// <param name="pix">The image to write.</param>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> or <paramref name="pix"/> is <see langword="null"/>.</exception>
    public static void WriteStreamSpix(Stream fp, Pix pix)
    {
        const string func = "pixWriteStreamSpix";
        if (fp == null)
            throw Lept.Arg(func, "stream not defined");
        if (pix == null)
            throw Lept.Arg(func, "pix not defined");

        byte[] data = WriteMemSpix(pix);
        fp.Write(data, 0, data.Length);
    }

    /*-----------------------------------------------------------------------*
     *       Low-level serialization of pix to/from memory (uncompressed)    *
     *-----------------------------------------------------------------------*/
    // leptonica: pixReadMemSpix (spixio.c)
    /// <summary>Reads a serialized image (SPIX format) from memory.</summary>
    /// <param name="data">The serialized image data.</param>
    /// <returns>The deserialized image.</returns>
    /// <exception cref="LeptonicaException">The data is not a valid SPIX image.</exception>
    public static Pix ReadMemSpix(ReadOnlySpan<byte> data)
    {
        return DeserializeFromMemory(data);
    }

    // leptonica: pixWriteMemSpix (spixio.c)
    /// <summary>Serializes an image to memory in the SPIX format.</summary>
    /// <param name="pix">The image to serialize.</param>
    /// <returns>The serialized data.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="pix"/> is <see langword="null"/>.</exception>
    public static byte[] WriteMemSpix(Pix pix)
    {
        const string func = "pixWriteMemSpix";
        if (pix == null)
            throw Lept.Arg(func, "pixs not defined");
        return pix.SerializeToMemory();
    }

    // leptonica: pixSerializeToMemory (spixio.c)
    /// <summary>Serializes this image to memory, without compression.</summary>
    /// <returns>The serialized data: a header, the colormap if any, and the raster data.</returns>
    /// <remarks>
    /// <para>The serialized format is a header of 24 bytes (the identifier <c>spix</c>, width, height, depth,
    /// words per line and number of colormap entries), followed by the colormap entries, the size of the raster
    /// data and the raster data. All integers are stored in little-endian order.</para>
    /// </remarks>
    /// <exception cref="LeptonicaException">The colormap is not valid.</exception>
    public byte[] SerializeToMemory()
    {
        const string func = "pixSerializeToMemory";
        GetDimensions(out int w, out int h, out int d);
        int wpl = Wpl;
        uint[] rdata = Data;
        int rdatasize = 4 * wpl * h;
        int ncolors = 0;
        byte[]? cdata = null;
        PixColormap? cmap = Colormap;
        if (cmap != null) {
            if (!cmap.IsValid(this))
                throw Lept.Fail(func, "colormap not valid");
            cdata = cmap.SerializeToMemory(4, out ncolors);
        }

        int nbytes = 24 + 4 * ncolors + 4 + rdatasize;
        byte[] data = new byte[nbytes];
        data[0] = (byte)'s';
        data[1] = (byte)'p';
        data[2] = (byte)'i';
        data[3] = (byte)'x';
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4, 4), w);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8, 4), h);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12, 4), d);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16, 4), wpl);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20, 4), ncolors);
        if (ncolors > 0 && cdata != null)
            Buffer.BlockCopy(cdata, 0, data, 24, 4 * ncolors);

        int indexByte = 24 + 4 * ncolors;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(indexByte, 4), rdatasize);
        int rpos = indexByte + 4;
        int nwords = wpl * h;
        for (int i = 0; i < nwords; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rpos + 4 * i, 4), rdata[i]);

        return data;
    }

    // leptonica: pixDeserializeFromMemory (spixio.c)
    /// <summary>Creates an image from data serialized by <see cref="SerializeToMemory"/>.</summary>
    /// <param name="data">The serialized image data.</param>
    /// <returns>The deserialized image.</returns>
    /// <exception cref="LeptonicaException">The data is too small, has an invalid identifier, or describes an invalid image.</exception>
    public static Pix DeserializeFromMemory(ReadOnlySpan<byte> data)
    {
        const string func = "pixDeserializeFromMemory";
        int nbytes = data.Length;
        if (nbytes < 28)
            throw Lept.Fail(func, $"invalid nbytes = {nbytes}");

        if (data[0] != (byte)'s' || data[1] != (byte)'p' ||
            data[2] != (byte)'i' || data[3] != (byte)'x')
            throw Lept.Fail(func, "invalid id string");
        int w = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4, 4));
        int h = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(8, 4));
        int d = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(12, 4));
        int ncolors = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(20, 4));

            /* Sanity checks on the amount of image data */
        if (w < 1 || w > SpixMaxAllowedWidth)
            throw Lept.Fail(func, "invalid width");
        if (h < 1 || h > SpixMaxAllowedHeight)
            throw Lept.Fail(func, "invalid height");
        if (1L * w * h > SpixMaxAllowedArea)
            throw Lept.Fail(func, "area too large");
        if (ncolors < 0 || ncolors > 256 || (ulong)(ncolors + 7) >= (ulong)nbytes / 4UL)
            throw Lept.Fail(func, "invalid ncolors");

        Pix pix1;
        try {
            pix1 = CreateHeader(w, h, d);
        } catch (ArgumentException) {
            throw Lept.Fail(func, "failed to make header");
        }
        int pixdata_size = 4 * h * pix1.Wpl;
        int memdata_size = nbytes - 24 - 4 * ncolors - 4;
        int imdata_size = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(24 + 4 * ncolors, 4));
        if (pixdata_size != memdata_size || pixdata_size != imdata_size)
            throw Lept.Fail(func,
                $"pixdata_size = {pixdata_size}, memdata_size = {memdata_size}, imdata_size = {imdata_size} not all equal!");

        Pix pixd = Create(w, h, d);
        PixColormap? cmap = null;
        if (ncolors > 0) {
            cmap = PixColormap.DeserializeFromMemory(data.Slice(24, 4 * ncolors), 4, ncolors);
            pixd.SetColormap(cmap);
        }

            /* Read the raster data */
        uint[] imdata = pixd.Data;
        int rpos = 28 + 4 * ncolors;
        int nwords = imdata_size / 4;
        for (int i = 0; i < nwords; i++)
            imdata[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(rpos + 4 * i, 4));

            /* Verify that the colormap is valid with the pix */
        if (ncolors > 0 && !cmap!.IsValid(pixd))
            throw Lept.Fail(func, "cmap is invalid with pix");

        return pixd;
    }
}
