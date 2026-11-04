using System;
using System.Buffers.Binary;
using System.IO;

namespace Leptonica;

/*
 *  bmpio.c:
 *
 *      Read bmp
 *           PIX          *pixReadStreamBmp()
 *           PIX          *pixReadMemBmp()
 *
 *      Write bmp
 *           l_int32       pixWriteStreamBmp()
 *           l_int32       pixWriteMemBmp()
 */

public sealed partial class Pix
{
    private const int BMP_FHBYTES = 14;    /* sizeof(BMP_FH) */
    private const int BMP_IHBYTES = 40;    /* sizeof(BMP_IH) */

        /* Here we're setting the pixel value 0 to white (255) and the
         * value 1 to black (0).  This is the convention for grayscale, but
         * the opposite of the convention for 1 bpp, where 0 is white
         * and 1 is black.  Both colormap entries are opaque (alpha = 255) */
    private static ReadOnlySpan<byte> BmpBwMap =>
        new byte[8] { 255, 255, 255, 255, 0, 0, 0, 255 };

        /* Image dimension limits */
    private const int L_MAX_ALLOWED_BMP_WIDTH = 1000000;
    private const int L_MAX_ALLOWED_BMP_HEIGHT = 1000000;
    private const long L_MAX_ALLOWED_BMP_PIXELS = 400000000L;
    private const int L_MAX_ALLOWED_BMP_RES = 10000000;  /* pixels/meter */

    /*--------------------------------------------------------------*
     *                              Read bmp                        *
     *--------------------------------------------------------------*/
    // leptonica: pixReadStreamBmp (bmpio.c)
    /// <summary>Reads a BMP image from a stream.</summary>
    /// <param name="fp">The stream to read; if it is seekable, reading starts at the beginning.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> is <see langword="null"/>.</exception>
    /// <exception cref="LeptonicaException">The data is not a supported uncompressed BMP image.</exception>
    public static Pix ReadStreamBmp(Stream fp)
    {
        const string func = "pixReadStreamBmp";
        if (fp == null)
            throw Lept.Arg(func, "fp not defined");

        if (fp.CanSeek)
            fp.Position = 0;
        byte[] data = Utils.BinaryReadStream(fp);
        return ReadMemBmp(data);
    }

    // leptonica: pixReadMemBmp (bmpio.c)
    /// <summary>Reads a BMP image from memory.</summary>
    /// <param name="cdata">The encoded BMP data.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="LeptonicaException">The data is not a supported uncompressed BMP image.</exception>
    public static Pix ReadMemBmp(ReadOnlySpan<byte> cdata)
    {
        const string func = "pixReadMemBmp";
        if (cdata.Length < BMP_FHBYTES + BMP_IHBYTES)
            throw Lept.Fail(func, "bmf size error");

            /* Verify this is an uncompressed bmp */
        short bftype = (short)(cdata[0] + ((int)cdata[1] << 8));
        if (bftype != BMP_ID)
            throw Lept.Fail(func, "not bmf format");
        ReadOnlySpan<byte> bmpih = cdata.Slice(BMP_FHBYTES, BMP_IHBYTES);
        int compression = BinaryPrimitives.ReadInt32LittleEndian(bmpih.Slice(16, 4));
        if (compression != 0)
            throw Lept.Fail(func, "cannot read compressed BMP files");

            /* Find the offset from the beginning of the file to the image data */
        int offset = cdata[10];
        offset += (int)cdata[11] << 8;
        offset += (int)cdata[12] << 16;
        offset = unchecked(offset + (int)((uint)cdata[13] << 24));

            /* Read the remaining useful data in the infoheader.
             * Note that the first 4 bytes give the infoheader size. */
        uint ihbytes = (uint)bmpih[0] | ((uint)bmpih[1] << 8) |
                       ((uint)bmpih[2] << 16) | ((uint)bmpih[3] << 24);
        int width = BinaryPrimitives.ReadInt32LittleEndian(bmpih.Slice(4, 4));
        int height = BinaryPrimitives.ReadInt32LittleEndian(bmpih.Slice(8, 4));
        short depth = BinaryPrimitives.ReadInt16LittleEndian(bmpih.Slice(14, 2));
        int imagebytes = BinaryPrimitives.ReadInt32LittleEndian(bmpih.Slice(20, 4));
        int xres = BinaryPrimitives.ReadInt32LittleEndian(bmpih.Slice(24, 4));
        int yres = BinaryPrimitives.ReadInt32LittleEndian(bmpih.Slice(28, 4));

            /* Some sanity checking. */
        if (width < 1)
            throw Lept.Fail(func, "width < 1");
        if (width > L_MAX_ALLOWED_BMP_WIDTH)
            throw Lept.Fail(func, "width too large");
        if (height == 0 || height < -L_MAX_ALLOWED_BMP_HEIGHT ||
            height > L_MAX_ALLOWED_BMP_HEIGHT)
            throw Lept.Fail(func, "invalid height");
        if (xres < 0 || xres > L_MAX_ALLOWED_BMP_RES ||
            yres < 0 || yres > L_MAX_ALLOWED_BMP_RES)
            throw Lept.Fail(func, "invalid resolution");
        bool height_neg = false;
        if (height < 0) {
            height_neg = true;
            height = -height;
        }
        if (ihbytes != 40 && ihbytes != 108 && ihbytes != 124)
            throw Lept.Fail(func, $"invalid ihbytes = {ihbytes}; not in {{40, 108, 124}}");
        long npixels = 1L * width * height;
        if (npixels > L_MAX_ALLOWED_BMP_PIXELS)
            throw Lept.Fail(func, "npixels too large");
        if (depth != 1 && depth != 2 && depth != 4 && depth != 8 &&
            depth != 16 && depth != 24 && depth != 32)
            throw Lept.Fail(func, $"invalid depth = {depth}; not in {{1, 2, 4, 8, 16, 24, 32}}");
        int fdatabpl = (int)(4 * ((1L * width * depth + 31) / 32));
        int fdatabytes = unchecked(fdatabpl * height);
        if (imagebytes != 0 && imagebytes != fdatabytes)
            throw Lept.Fail(func, $"invalid imagebytes = {imagebytes}; not equal to fdatabytes = {fdatabytes}");

        int cmapbytes = unchecked((int)((uint)offset - (uint)BMP_FHBYTES - ihbytes));
        int ncolors = unchecked((int)((ulong)(long)cmapbytes / 4UL));
        if (ncolors < 0 || ncolors == 1)
            throw Lept.Fail(func, "invalid: cmap size < 0 or 1");
        if (ncolors > 0 && depth > 8)
            throw Lept.Fail(func, "can't have cmap for d > 8");
        int maxcolors = (depth <= 8) ? (1 << depth) : 0;
        if (ncolors > maxcolors)
            throw Lept.Fail(func, $"cmap too large for depth {depth}: ncolors = {ncolors} > maxcolors = {maxcolors}");
        if ((long)cdata.Length != 1L * offset + 1L * fdatabytes)
            throw Lept.Fail(func, "size incommensurate with image data");

            /* Make a 32 bpp pix if file depth is 24 bpp */
        short d = (depth == 24) ? (short)32 : depth;
        Pix pix = Create(width, height, d);
        pix.XRes = (int)((float)xres / 39.37 + 0.5);  /* to ppi */
        pix.YRes = (int)((float)yres / 39.37 + 0.5);  /* to ppi */
        pix.InputFormat = L.IFF_BMP;
        int pixWpl = pix.Wpl;
        int spp;
        if (depth <= 16)
            spp = 1;
        else if (depth == 24)
            spp = 3;
        else  /* depth == 32 */
            spp = 4;
        pix.Spp = spp;

            /* Convert the bmp colormap to a pixcmap */
        PixColormap? cmap = null;
        if (ncolors > 0) {  /* import the colormap to the pix cmap */
            cmap = PixColormap.Create(Math.Min((int)d, 8));
            int nc = Math.Min(ncolors, 256);
            int cmapPos = (int)(BMP_FHBYTES + ihbytes);
            for (int i = 0; i < nc; i++) {
                byte blue = cdata[cmapPos + 4 * i];
                byte green = cdata[cmapPos + 4 * i + 1];
                byte red = cdata[cmapPos + 4 * i + 2];
                cmap.AddRGBA(red, green, blue, 255);  /* set all colors opaque */
            }
        }
        pix.SetColormap(cmap);

            /* Acquire the image data.  Image origin for bmp is at lower right. */
        int fpos = offset;
        uint[] pixdata = pix.Data;
        if (depth != 24 && depth != 32) {  /* typ. 1 or 8 bpp */
            int fWpl = fdatabpl / 4;
            int line = pixWpl * (height - 1);
            for (int i = 0; i < height; i++) {
                for (int j = 0; j < fWpl; j++)
                    pixdata[line + j] = BinaryPrimitives.ReadUInt32LittleEndian(cdata.Slice(fpos + 4 * j, 4));
                fpos += fdatabpl;
                line -= pixWpl;
            }
        } else {  /* 24 or 32 bpp file; 32 bpp pix */
            int filebpp = (depth == 24) ? 3 : 4;
            int extrabytes = fdatabpl - filebpp * width;
            int line = pixWpl * (height - 1);
            for (int i = 0; i < height; i++) {
                for (int j = 0; j < width; j++) {
                    byte pel0 = cdata[fpos];
                    byte pel1 = cdata[fpos + 1];
                    byte pel2 = cdata[fpos + 2];
                    byte alpha = (depth == 24) ? (byte)255 : cdata[fpos + 3];
                    fpos += filebpp;
                    pixdata[line + j] = (uint)pel2 | ((uint)pel1 << 8) |
                                        ((uint)pel0 << 16) | ((uint)alpha << 24);
                }
                if (extrabytes != 0)
                    fpos += extrabytes;
                line -= pixWpl;
            }
        }

        pix.EndianByteSwap();
        if (height_neg)
            FlipTB(pix, pix);

            /* We do not use 1 bpp pix with colormaps in leptonica. */
        if (depth == 1 && cmap != null) {
            Log.Info(func, "removing opaque cmap from 1 bpp");
            Pix pix1 = pix.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
            pix = pix1;
        }

        return pix;
    }

    /*--------------------------------------------------------------*
     *                            Write bmp                         *
     *--------------------------------------------------------------*/
    // leptonica: pixWriteStreamBmp (bmpio.c)
    /// <summary>Writes an image to a stream in BMP format.</summary>
    /// <param name="fp">The stream to write; if it is seekable, writing starts at the beginning.</param>
    /// <param name="pix">The image to write.</param>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> or <paramref name="pix"/> is <see langword="null"/>.</exception>
    public static void WriteStreamBmp(Stream fp, Pix pix)
    {
        const string func = "pixWriteStreamBmp";
        if (fp == null)
            throw Lept.Arg(func, "stream not defined");
        if (pix == null)
            throw Lept.Arg(func, "pix not defined");

        byte[] data = WriteMemBmp(pix);
        if (fp.CanSeek)
            fp.Position = 0;
        fp.Write(data, 0, data.Length);
    }

    // leptonica: pixWriteMemBmp (bmpio.c)
    /// <summary>Encodes an image in BMP format.</summary>
    /// <param name="pixs">The image to encode.</param>
    /// <returns>The BMP-encoded data.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="pixs"/> is <see langword="null"/>, or its colormap is not valid.</exception>
    public static byte[] WriteMemBmp(Pix pixs)
    {
        const string func = "pixWriteMemBmp";
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");

            /* Verify validity of colormap */
        PixColormap? cmap = pixs.Colormap;
        if (cmap != null) {
            if (!cmap.IsValid(pixs))
                throw Lept.Fail(func, "colormap is not valid");
        }

        pixs.GetDimensions(out int w, out int h, out int d);
        int spp = pixs.Spp;
        if (spp != 1 && spp != 3 && spp != 4)
            throw Lept.Fail(func, $"unsupported spp = {spp}");
        Pix pix;
        if (d == 2) {
            Log.Warning(func, "2 bpp files can't be read; converting to 8 bpp");
            pix = pixs.Convert2To8(0, 85, 170, 255, true);
            d = 8;
        } else if (d == 24) {
            pix = pixs.Convert24To32();
            d = 32;
        } else {
            pix = Copy(null, pixs);
        }

            /* Find the bits/pixel to be written to file */
        int fdepth;
        if (spp == 1)
            fdepth = d;
        else if (spp == 3)
            fdepth = 24;
        else  /* spp == 4 */
            fdepth = 32;

            /* Resolution is given in pixels/meter */
        int xres = (int)(39.37 * (float)pix.XRes + 0.5);
        int yres = (int)(39.37 * (float)pix.YRes + 0.5);

        int pixWpl = pix.Wpl;
        int fWpl = (w * fdepth + 31) / 32;
        int fBpl = 4 * fWpl;
        uint fimagebytes = unchecked((uint)(h * fBpl));
        if (fimagebytes > 4L * L_MAX_ALLOWED_BMP_PIXELS)
            throw Lept.Fail(func, "image data is too large");

            /* If not rgb or 16 bpp, the bmp data is required to have a colormap */
        int ncolors;
        int cmaplen;
        byte[]? cta = null;
        if (d == 32 || d == 16) {   /* 24 bpp rgb or 16 bpp: no colormap */
            ncolors = 0;
            cmaplen = 0;
            cmap = pix.Colormap;
        } else if ((cmap = pix.Colormap) != null) {   /* existing colormap */
            ncolors = cmap.Count;
            cmaplen = ncolors * 4;
            cta = new byte[cmaplen];
            for (int i = 0; i < ncolors; i++) {
                cmap.GetRGBA(i, out int r, out int g, out int b, out int a);
                cta[4 * i] = (byte)b;
                cta[4 * i + 1] = (byte)g;
                cta[4 * i + 2] = (byte)r;
                cta[4 * i + 3] = (byte)a;
            }
        } else {   /* no existing colormap; d <= 8; make a binary or gray one */
            if (d == 1) {
                cmaplen = 8;
                ncolors = 2;
                cta = BmpBwMap.ToArray();
            } else {   /* d = 2,4,8; use a grayscale output colormap */
                ncolors = 1 << d;
                cmaplen = ncolors * 4;
                cta = new byte[cmaplen];
                int stepsize = 255 / (ncolors - 1);
                for (int i = 0, val = 0; i < ncolors; i++, val += stepsize) {
                    cta[4 * i] = (byte)val;
                    cta[4 * i + 1] = (byte)val;
                    cta[4 * i + 2] = (byte)val;
                    cta[4 * i + 3] = 255;  /* opaque */
                }
            }
        }

        uint offbytes = (uint)(BMP_FHBYTES + BMP_IHBYTES + cmaplen);
        long fsize64 = (long)offbytes + (long)fimagebytes;
        byte[] fdata = new byte[checked((int)fsize64)];

            /* Write little-endian file header data */
        fdata[0] = unchecked((byte)(BMP_ID >> 0));
        fdata[1] = (byte)(BMP_ID >> 8);
        fdata[2] = (byte)(fsize64 >> 0);
        fdata[3] = (byte)(fsize64 >> 8);
        fdata[4] = (byte)(fsize64 >> 16);
        fdata[5] = (byte)(fsize64 >> 24);
        fdata[10] = (byte)(offbytes >> 0);
        fdata[11] = (byte)(offbytes >> 8);
        fdata[12] = (byte)(offbytes >> 16);
        fdata[13] = (byte)(offbytes >> 24);

            /* Convert to little-endian and write the info header data */
        Span<byte> ih = fdata.AsSpan(BMP_FHBYTES, BMP_IHBYTES);
        BinaryPrimitives.WriteInt32LittleEndian(ih.Slice(0, 4), BMP_IHBYTES);
        BinaryPrimitives.WriteInt32LittleEndian(ih.Slice(4, 4), w);
        BinaryPrimitives.WriteInt32LittleEndian(ih.Slice(8, 4), h);
        BinaryPrimitives.WriteInt16LittleEndian(ih.Slice(12, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(ih.Slice(14, 2), (short)fdepth);
        BinaryPrimitives.WriteInt32LittleEndian(ih.Slice(16, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(ih.Slice(20, 4), unchecked((int)fimagebytes));
        BinaryPrimitives.WriteInt32LittleEndian(ih.Slice(24, 4), xres);
        BinaryPrimitives.WriteInt32LittleEndian(ih.Slice(28, 4), yres);
        BinaryPrimitives.WriteInt32LittleEndian(ih.Slice(32, 4), ncolors);
        BinaryPrimitives.WriteInt32LittleEndian(ih.Slice(36, 4), ncolors);

            /* Copy the colormap data */
        if (ncolors > 0 && cta != null)
            Buffer.BlockCopy(cta, 0, fdata, BMP_FHBYTES + BMP_IHBYTES, cmaplen);

            /* When you write a binary image with a colormap
             * that sets BLACK to 0, you must invert the data */
        if (fdepth == 1 && cmap != null) {
            cmap.GetRGBA(0, out _, out _, out int b0, out _);
            if (b0 == 0)
                Invert(pix, pix);
        }

            /* An endian byte swap is also required */
        pix.EndianByteSwap();

            /* Transfer the image data.  Image origin for bmp is at lower right. */
        int fmPos = (int)offbytes;
        uint[] pixdata = pix.Data;
        if (fdepth != 24 && fdepth != 32) {   /* typ 1 or 8 bpp */
            int line = pixWpl * (h - 1);
            for (int i = 0; i < h; i++) {
                for (int j = 0; j < fWpl; j++)
                    BinaryPrimitives.WriteUInt32LittleEndian(fdata.AsSpan(fmPos + 4 * j, 4), pixdata[line + j]);
                line -= pixWpl;
                fmPos += fBpl;
            }
        } else {   /* 32 bpp pix; 24 bpp or 32 bpp file */
            int extrabytes = fBpl - spp * w;
            int line = pixWpl * (h - 1);
            byte pel0 = 0;
            for (int i = 0; i < h; i++) {
                for (int j = 0; j < w; j++) {
                    uint word = pixdata[line + j];
                    byte r = (byte)(word & 0xff);
                    byte g = (byte)((word >> 8) & 0xff);
                    pel0 = (byte)((word >> 16) & 0xff);
                    fdata[fmPos] = pel0;
                    fdata[fmPos + 1] = g;
                    fdata[fmPos + 2] = r;
                    if (spp == 4)
                        fdata[fmPos + 3] = (byte)(word >> 24);
                    fmPos += spp;
                }
                if (extrabytes != 0) {
                    for (int k = 0; k < extrabytes; k++)
                        fdata[fmPos++] = pel0;
                }
                line -= pixWpl;
            }
        }

        return fdata;
    }
}
