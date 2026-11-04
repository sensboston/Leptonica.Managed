using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using Leptonica.Internal.Png;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  pngio.c: Read and write png files and memory.
 *
 *  The libpng calls are replaced by Leptonica.Internal.Png (PngDecoder,
 *  PngEncoder); a libpng error (setjmp path) becomes a PngException,
 *  which is reported as "internal png error".
 *
 *  The stream and memory versions of reading and writing are separate
 *  implementations in C and differ in details, which are kept:
 *    - Reading: the check for unsupported depths, and the alpha byte of
 *      spp = 3 images (255 from a stream, 0 from memory).
 *    - Writing: the length of the tRNS palette (max_trans + 1 for a stream,
 *      ncolors for memory).
 *  They share one implementation here, switched by %mem.
 *
 *  Not ported: memio_png_* (libpng memory buffering), the DEBUG_* code.
 */
public sealed partial class Pix
{
    /* ------------------ Set default for read option -------------------- */
        /* Strip 16 bpp --> 8 bpp on reading png; default is for stripping.
         * If you don't strip, you can't read the gray-alpha spp = 2 images. */
    private static int var_PNG_STRIP_16_TO_8 = 1;

    /*---------------------------------------------------------------------*
     *                     Reading png through stream                      *
     *---------------------------------------------------------------------*/
    // leptonica: pixReadStreamPng (pngio.c)
    /// <summary>Reads a png image from the current position of the stream.</summary>
    /// <remarks>
    /// (3) All images with alpha is converted to RGBA (spp = 4, with
    ///     equal red, green and blue channels) on reading.
    ///     There are three cases with alpha:
    ///     (a) RGBA: spp = 4.  The alpha value is the fourth byte
    ///         (aka "channel, "component") in each 4-byte pixel.
    ///     (b) grayscale-with-alpha (spp = 2), where bpp = 8, and each
    ///         pixel has an associated alpha (transparency) value
    ///         in the second component of the image data.
    ///     (c) colormap (spp = 1) with alpha in the trans palette.
    ///         d = 1, 2, 4, 8.  The trans palette in writing is derived
    ///         from the alpha components in the cmap.  Transparency is
    ///         often associated with a white background.
    /// </remarks>
    public static Pix ReadStreamPng(Stream fp)
    {
        return ReadPng(fp, false, "pixReadStreamPng");
    }

    // leptonica: pixReadMemPng (pngio.c)
    /// <summary>Reads a png image from memory.</summary>
    /// <remarks>See <see cref="ReadStreamPng"/>.</remarks>
    public static Pix ReadMemPng(byte[] filedata)
    {
        if (filedata.Length < 1)
            throw Lept.Arg("pixReadMemPng", "invalid filesize");

        using MemoryStream ms = new MemoryStream(filedata, false);
        return ReadPng(ms, true, "pixReadMemPng");
    }

    /// <summary>
    /// Converts one row of 8 bit RGB (3 bytes per pixel) or RGBA (4 bytes) samples
    /// to Leptonica pixels; <paramref name="alpha"/> is used when there is no alpha byte.
    /// </summary>
    internal static void ConvertRgbRow(ReadOnlySpan<byte> src, Span<uint> dst, bool hasAlpha, uint alpha)
    {
        int w = dst.Length;
        if (hasAlpha) {
            for (int j = 0; j < w; j++)
                dst[j] = BinaryPrimitives.ReadUInt32BigEndian(src.Slice(4 * j));
            return;
        }
        /* A 4 byte load overreads into the next pixel; the last pixel is done bytewise. */
        int fast = w - 1;
        for (int j = 0; j < fast; j++) {
            uint v = BinaryPrimitives.ReadUInt32BigEndian(src.Slice(3 * j));
            dst[j] = (v & 0xffffff00u) | alpha;
        }
        if (w > 0) {
            int k = 3 * (w - 1);
            dst[w - 1] = ((uint)src[k] << 24) | ((uint)src[k + 1] << 16) | ((uint)src[k + 2] << 8) | alpha;
        }
    }

    private static Pix ReadPng(Stream fp, bool mem, string func)
    {
            /* ---------------------------------------------------------- *
             *  - Set the transforms flags.  Whatever happens here,
             *    NEVER invert 1 bpp using PNG_TRANSFORM_INVERT_MONO.
             *  - Do not use PNG_TRANSFORM_EXPAND, which would
             *    expand all images with bpp < 8 to 8 bpp.
             *  - Strip 16 --> 8 if reading 16-bit gray+alpha
             * ---------------------------------------------------------- */
            /* To strip 16 --> 8 bit depth, use PNG_TRANSFORM_STRIP_16 */
        bool strip16;
        if (var_PNG_STRIP_16_TO_8 == 1) {  /* our default */
            strip16 = true;
        } else {
            strip16 = false;
            if (!mem)
                Log.Info(func, "not stripping 16 --> 8 in png reading");
        }

            /* Read it */
        PngImage image;
        try {
            image = PngDecoder.Read(fp, strip16);
        } catch (PngException) {
            throw Lept.Fail(func, "internal png error");
        }

        PngInfo info = image.Info;
        byte[] rows = image.Pixels;
        int w = info.Width;
        int h = info.Height;
        int bit_depth = info.BitDepth;
        int rowbytes = info.RowBytes;
        int color_type = info.ColorType;
        int channels = info.Channels;
        int spp = channels;
        bool tRNS = info.HasTrns;

        int d;
        if (spp == 1) {
            d = bit_depth;
        } else {  /* spp == 2 (gray + alpha), spp == 3 (rgb), spp == 4 (rgba) */
            d = 4 * bit_depth;
        }

        if (!mem) {
                /* Remove if/when this is implemented for all bit_depths */
            if (spp != 1 && bit_depth != 8) {
                Log.Error(func, $"spp = {spp} and bps = {bit_depth} != 8\n" +
                                "turn on 16 --> 8 stripping");
                throw Lept.Fail(func, "not implemented for this image");
            }
        } else {
            if (spp == 3 && bit_depth != 8) {
                Log.Error(func, $"Help: spp = 3 and depth = {bit_depth} != 8\n!!");
                throw Lept.Fail(func, "not implemented for this depth");
            }
        }

        PixColormap? cmap = null;
        if (color_type == PNG_COLOR_TYPE_PALETTE ||
            color_type == PNG_COLOR_MASK_PALETTE) {   /* generate a colormap */
            PngColor[] palette = info.Palette ?? Array.Empty<PngColor>();
            int num_palette = palette.Length;
            cmap = PixColormap.Create(d);  /* spp == 1 */
            for (int cindex = 0; cindex < num_palette; cindex++) {
                int rval = palette[cindex].Red;
                int gval = palette[cindex].Green;
                int bval = palette[cindex].Blue;
                cmap.AddColor(rval, gval, bval);
            }
        }

        Pix pix = Create(w, h, d);
        pix.InputFormat = L.IFF_PNG;
        int wpl = pix.Wpl;
        uint[] data = pix.Data;
        pix.Spp = spp;
        try {
            pix.SetColormap(cmap);
        } catch (LeptonicaException) {
            throw Lept.Fail(func, "invalid colormap");
        }

        if (spp == 1 && !tRNS) {  /* copy straight from buffer to pix */
            for (int i = 0; i < h; i++) {
                Span<uint> line = data.AsSpan(i * wpl, wpl);
                ReadOnlySpan<byte> rowptr = rows.AsSpan(i * rowbytes, rowbytes);
                for (int j = 0; j < rowbytes; j++) {
                        SetDataByte(line, j, rowptr[j]);
                }
            }
        } else if (spp == 2) {  /* grayscale + alpha; convert to RGBA */
            Log.Info(func, "converting (gray + alpha) ==> RGBA");
            for (int i = 0; i < h; i++) {
                int ppixel = i * wpl;
                ReadOnlySpan<byte> rowptr = rows.AsSpan(i * rowbytes, rowbytes);
                for (int j = 0, k = 0; j < w; j++) {
                        /* Copy gray value into r, g and b */
                    SetDataByte(ref data[ppixel], L.COLOR_RED, rowptr[k]);
                    SetDataByte(ref data[ppixel], L.COLOR_GREEN, rowptr[k]);
                    SetDataByte(ref data[ppixel], L.COLOR_BLUE, rowptr[k++]);
                    SetDataByte(ref data[ppixel], L.L_ALPHA_CHANNEL, rowptr[k++]);
                    ppixel++;
                }
            }
            pix.Spp = 4;  /* we do not support 2 spp pix */
        } else if (spp == 3 || spp == 4) {
            for (int i = 0; i < h; i++) {
                ReadOnlySpan<byte> rowptr = rows.AsSpan(i * rowbytes, rowbytes);
                ConvertRgbRow(rowptr, data.AsSpan(i * wpl, w), spp == 4, mem ? 0u : 255u);
            }
        }

            /* Special spp == 1 cases with transparency:
             *    (1) 8 bpp without colormap; assume full transparency
             *    (2) 1 bpp with colormap + trans array (for alpha)
             *    (3) 2 bpp with colormap + trans array (for alpha)
             *    (4) 4 bpp with colormap + trans array (for alpha)
             *    (5) 8 bpp with colormap + trans array (for alpha)
             * These all require converting to RGBA */
        if (spp == 1 && tRNS) {
            if (cmap == null) {
                    /* Case 1: make fully transparent RGBA image */
                Log.Info(func, "transparency, 1 spp, no colormap, no transparency array: " +
                               "convention is fully transparent image");
                Log.Info(func, "converting (fully transparent 1 spp) ==> RGBA");
                pix = Create(w, h, 32);  /* init to alpha = 0 (transparent) */
                pix.Spp = 4;
            } else {
                Log.Info(func, "converting (cmap + alpha) ==> RGBA");

                    /* Grab the transparency array */
                byte[] trans = info.Trans;
                int num_trans = trans.Length;
                if (num_trans == 0)  /* invalid png file */
                    throw Lept.Fail(func, "cmap, tRNS, but no transparency array");

                    /* Save the cmap and destroy the pix */
                cmap = pix.Colormap!.Copy();

                    /* Start over with 32 bit RGBA */
                pix = Create(w, h, 32);
                wpl = pix.Wpl;
                data = pix.Data;
                pix.Spp = 4;

                    /* Extract the data and convert to RGBA */
                if (d == 1) {
                        /* Case 2: 1 bpp with transparency (usually) behind white */
                    Log.Info(func, "converting 1 bpp cmap with alpha ==> RGBA");
                    if (num_trans == 1)
                        Log.Info(func, "num_trans = 1; second color opaque by default");
                    for (int i = 0; i < h; i++) {
                        int ppixel = i * wpl;
                        ReadOnlySpan<byte> rowptr = rows.AsSpan(i * rowbytes, rowbytes);
                        for (int j = 0, index = 0; j < rowbytes; j++) {
                            byte b = rowptr[j];
                            for (int k = 0; k < 8 && index < w; k++, index++) {
                                int bitval = (b >> (7 - k)) & 1;
                                cmap.GetColor(bitval, out int rval, out int gval, out int bval);
                                ComposeRGBPixel(rval, gval, bval, out data[ppixel]);
                                SetDataByte(ref data[ppixel], L.L_ALPHA_CHANNEL,
                                            bitval < num_trans ? trans[bitval] : 255);
                                ppixel++;
                            }
                        }
                    }
                } else if (d == 2) {
                        /* Case 3: 2 bpp with cmap and associated transparency */
                    Log.Info(func, "converting 2 bpp cmap with alpha ==> RGBA");
                    for (int i = 0; i < h; i++) {
                        int ppixel = i * wpl;
                        ReadOnlySpan<byte> rowptr = rows.AsSpan(i * rowbytes, rowbytes);
                        for (int j = 0, index = 0; j < rowbytes; j++) {
                            byte b = rowptr[j];
                            for (int k = 0; k < 4 && index < w; k++, index++) {
                                int bival = (b >> 2 * (3 - k)) & 3;
                                cmap.GetColor(bival, out int rval, out int gval, out int bval);
                                ComposeRGBPixel(rval, gval, bval, out data[ppixel]);
                                    /* Assume missing entries to be 255 (opaque)
                                     * according to the spec:
                                     * http://www.w3.org/TR/PNG/#11tRNS */
                                SetDataByte(ref data[ppixel], L.L_ALPHA_CHANNEL,
                                            bival < num_trans ? trans[bival] : 255);
                                ppixel++;
                            }
                        }
                    }
                } else if (d == 4) {
                        /* Case 4: 4 bpp with cmap and associated transparency */
                    Log.Info(func, "converting 4 bpp cmap with alpha ==> RGBA");
                    for (int i = 0; i < h; i++) {
                        int ppixel = i * wpl;
                        ReadOnlySpan<byte> rowptr = rows.AsSpan(i * rowbytes, rowbytes);
                        for (int j = 0, index = 0; j < rowbytes; j++) {
                            byte b = rowptr[j];
                            for (int k = 0; k < 2 && index < w; k++, index++) {
                                int quadval = (b >> 4 * (1 - k)) & 0xf;
                                cmap.GetColor(quadval, out int rval, out int gval, out int bval);
                                ComposeRGBPixel(rval, gval, bval, out data[ppixel]);
                                    /* Assume missing entries to be 255 (opaque) */
                                SetDataByte(ref data[ppixel], L.L_ALPHA_CHANNEL,
                                            quadval < num_trans ? trans[quadval] : 255);
                                ppixel++;
                            }
                        }
                    }
                } else if (d == 8) {
                        /* Case 5: 8 bpp with cmap and associated transparency */
                    Log.Info(func, "converting 8 bpp cmap with alpha ==> RGBA");
                    for (int i = 0; i < h; i++) {
                        int ppixel = i * wpl;
                        ReadOnlySpan<byte> rowptr = rows.AsSpan(i * rowbytes, rowbytes);
                        for (int j = 0; j < w; j++) {
                            int index = rowptr[j];
                            cmap.GetColor(index, out int rval, out int gval, out int bval);
                            ComposeRGBPixel(rval, gval, bval, out data[ppixel]);
                                /* Assume missing entries to be 255 (opaque) */
                            SetDataByte(ref data[ppixel], L.L_ALPHA_CHANNEL,
                                        index < num_trans ? trans[index] : 255);
                            ppixel++;
                        }
                    }
                } else {
                    Log.Error(func, $"spp == 1, cmap, trans array, invalid depth: {d}");
                }
            }
        }

            /* Final adjustments for bpp = 1.
             *   + If there is no colormap, the image must be inverted because
             *     png stores black pixels as 0.
             *   + We have already handled the case of cmapped, 1 bpp pix
             *     with transparency, where the output pix is 32 bpp RGBA.
             *     If there is no transparency but the pix has a colormap,
             *     we remove the colormap, because functions operating on
             *     1 bpp images in leptonica assume no colormap.
             *   + The colormap must be removed in such a way that the pixel
             *     values are not changed.  If the values are only black and
             *     white, we return a 1 bpp image; if gray, return an 8 bpp pix;
             *     otherwise, return a 32 bpp rgb pix.
             */
        if (pix.Depth == 1) {
            if (cmap == null) {
                Invert(pix, pix);
            } else {
                if (!mem)
                    Log.Info(func, "removing opaque cmap from 1 bpp");
                pix = pix.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
            }
        }

        uint xres = info.XPixelsPerMeter;
        uint yres = info.YPixelsPerMeter;
        pix.XRes = (int)((float)xres / 39.37 + 0.5);  /* to ppi */
        pix.YRes = (int)((float)yres / 39.37 + 0.5);  /* to ppi */

            /* Get the text if there is any */
        if (info.Texts.Count > 0)
            pix.Text = info.Texts[0].Text;

            /* Final validity check on the colormap */
        if ((cmap = pix.Colormap) != null) {
            if (!cmap.IsValid(pix))
                throw Lept.Fail(func, "colormap is not valid");
        }

        pix.SetPadBits(0);
        return pix;
    }

    /*---------------------------------------------------------------------*
     *                          Reading png header                         *
     *---------------------------------------------------------------------*/
    // leptonica: readHeaderPng (pngio.c)
    /// <summary>Reads the header information of a PNG file without decoding the pixels.</summary>
    /// <param name="filename">Path of the PNG file.</param>
    /// <param name="pw">Receives the image width.</param>
    /// <param name="ph">Receives the image height.</param>
    /// <param name="pbps">Receives the number of bits per sample.</param>
    /// <param name="pspp">Receives the number of samples per pixel.</param>
    /// <param name="piscmap"><see langword="true"/> if there is a colormap.</param>
    /// <remarks>
    /// (2) For gray+alpha, although the png records bps = 16, we
    ///     consider this as two 8 bpp samples (gray and alpha).
    ///     When a gray+alpha is read, it is converted to 32 bpp RGBA.
    /// </remarks>
    public static void ReadHeaderPng(string filename, out int pw, out int ph, out int pbps,
                                     out int pspp, out bool piscmap)
    {
        using FileStream fp = Utils.FopenReadStream(filename);
        FreadHeaderPng(fp, out pw, out ph, out pbps, out pspp, out piscmap);
    }

    // leptonica: freadHeaderPng (pngio.c)
    /// <summary>Reads the header information of a PNG image from a stream.</summary>
    /// <param name="fp">The stream to read; only the first 40 bytes are needed.</param>
    /// <param name="pw">Receives the image width.</param>
    /// <param name="ph">Receives the image height.</param>
    /// <param name="pbps">Receives the number of bits per sample.</param>
    /// <param name="pspp">Receives the number of samples per pixel.</param>
    /// <param name="piscmap"><see langword="true"/> if there is a colormap.</param>
    /// <remarks>See <see cref="ReadHeaderPng"/>.</remarks>
    /// <exception cref="LeptonicaException">The stream is too small or cannot be read.</exception>
    public static void FreadHeaderPng(Stream fp, out int pw, out int ph, out int pbps,
                                      out int pspp, out bool piscmap)
    {
        long nbytes = Utils.FnbytesInFile(fp);
        if (nbytes < 40)
            throw Lept.Fail("freadHeaderPng", "file too small to be png");
        byte[] data = new byte[40];
        if (fp.ReadAtLeast(data, 40, false) != 40)
            throw Lept.Fail("freadHeaderPng", "error reading data");
        ReadHeaderMemPng(data, out pw, out ph, out pbps, out pspp, out piscmap);
    }

    // leptonica: readHeaderMemPng (pngio.c)
    /// <summary>Reads the header information of a PNG image in memory.</summary>
    /// <param name="data">The encoded PNG data; the first 40 bytes are sufficient.</param>
    /// <param name="pw">Receives the image width.</param>
    /// <param name="ph">Receives the image height.</param>
    /// <param name="pbps">Receives the number of bits per sample.</param>
    /// <param name="pspp">Receives the number of samples per pixel.</param>
    /// <param name="piscmap"><see langword="true"/> if there is a colormap.</param>
    /// <remarks>
    /// (2) png colortypes (see png.h: PNG_COLOR_TYPE_*):
    ///     0:  gray; fully transparent (with tRNS) (1 spp)
    ///     2:  RGB (3 spp)
    ///     3:  colormap; colormap+alpha (with tRNS) (1 spp)
    ///     4:  gray + alpha (2 spp)
    ///     6:  RGBA (4 spp)
    ///     Note:
    ///       0 and 3 have the alpha information in a tRNS chunk
    ///       4 and 6 have separate alpha samples with each pixel.
    /// </remarks>
    public static void ReadHeaderMemPng(ReadOnlySpan<byte> data, out int pw, out int ph,
                                        out int pbps, out int pspp, out bool piscmap)
    {
        const string func = "readHeaderMemPng";
        pw = ph = pbps = pspp = 0;
        piscmap = false;
        if (data.Length < 40)
            throw Lept.Arg(func, "size < 40");

            /* Check password */
        if (data[0] != 137 || data[1] != 80 || data[2] != 78 ||
            data[3] != 71 || data[4] != 13 || data[5] != 10 ||
            data[6] != 26 || data[7] != 10)
            throw Lept.Fail(func, "not a valid png file");

        int w = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(16));
        int h = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(20));
        if (w < 1 || h < 1)
            throw Lept.Fail(func, "invalid w or h");
        int twobytes = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(24));  /* contains depth/sample  */
                                                                              /* and the color type     */
        int colortype = twobytes & 0xff;  /* color type */
        int bps = twobytes >> 8;   /* bits/sample */

            /* Special case with alpha that is extracted as RGBA.
             * Note that the cmap+alpha is also extracted as RGBA,
             * but only if the tRNS chunk exists, which we can't tell
             * by this simple parser.*/
        if (colortype == 4)
            Log.Info(func, "gray + alpha: will extract as RGBA (spp = 4)");

        int spp;
        if (colortype == 2) {  /* RGB */
            spp = 3;
        } else if (colortype == 6) {  /* RGBA */
            spp = 4;
        } else if (colortype == 4) {  /* gray + alpha */
            spp = 2;
            bps = 8;  /* both the gray and alpha are 8-bit samples */
        } else {  /* gray (0) or cmap (3) or cmap+alpha (3) */
            spp = 1;
        }
        if (bps < 1 || bps > 16) {
            Log.Error(func, $"invalid bps = {bps}");
            throw Lept.Fail(func, $"invalid bps = {bps}");
        }
        pw = w;
        ph = h;
        pbps = bps;
        pspp = spp;
        if ((colortype & 1) != 0)  /* palette */
            piscmap = true;
        else
            piscmap = false;
    }

    // leptonica: fgetPngResolution (pngio.c)
    /// <summary>Reads the resolution (ppi) from the png metadata; the stream is rewound.</summary>
    public static void FgetPngResolution(Stream fp, out int pxres, out int pyres)
    {
        pxres = pyres = 0;

            /* Read the metadata */
        fp.Position = 0;
        PngInfo info;
        try {
            info = PngDecoder.ReadInfo(fp);
        } catch (PngException) {
            throw Lept.Fail("fgetPngResolution", "internal png error");
        }
        uint xres = info.XPixelsPerMeter;
        uint yres = info.YPixelsPerMeter;
        pxres = (int)((float)xres / 39.37 + 0.5);  /* to ppi */
        pyres = (int)((float)yres / 39.37 + 0.5);
        fp.Position = 0;
    }

    // leptonica: isPngInterlaced (pngio.c)
    /// <summary>True if the png file is interlaced.</summary>
    public static bool IsPngInterlaced(string filename)
    {
        byte[] buf = new byte[32];
        using (FileStream fp = Utils.FopenReadStream(filename)) {
            if (fp.ReadAtLeast(buf, 32, false) != 32)
                throw Lept.Fail("isPngInterlaced", $"data not read: {filename}");
        }
        return buf[28] != 0;
    }

    // leptonica: fgetPngColormapInfo (pngio.c)
    /// <summary>Reads the colormap and checks for transparency; the stream is rewound.</summary>
    /// <param name="fp">The stream to read.</param>
    /// <param name="pcmap">Receives the colormap with alpha values from the tRNS chunk, or <see langword="null"/> if there is none.</param>
    /// <param name="ptransparency">Receives <see langword="true"/> if the colormap has a non-opaque entry.</param>
    public static void FgetPngColormapInfo(Stream fp, out PixColormap? pcmap, out bool ptransparency)
    {
        const string func = "fgetPngColormapInfo";
        pcmap = null;
        ptransparency = false;

            /* Read the metadata and check if there is a colormap */
        fp.Position = 0;
        PngInfo info;
        try {
            info = PngDecoder.ReadInfo(fp);
        } catch (PngException) {
            throw Lept.Fail(func, "internal png error");
        }
        int color_type = info.ColorType;
        if (color_type != PNG_COLOR_TYPE_PALETTE &&
            color_type != PNG_COLOR_MASK_PALETTE)
            return;

            /* Read the colormap */
        int bit_depth = info.BitDepth;
        PngColor[] palette = info.Palette ?? Array.Empty<PngColor>();
        int num_palette = palette.Length;
        PixColormap cmap = PixColormap.Create(bit_depth);  /* spp == 1 */
        for (int cindex = 0; cindex < num_palette; cindex++) {
            int rval = palette[cindex].Red;
            int gval = palette[cindex].Green;
            int bval = palette[cindex].Blue;
            cmap.AddColor(rval, gval, bval);
        }
        pcmap = cmap;

            /* Look for transparency.  Note that the colormap
             * has been initialized to fully opaque. */
        if (info.HasTrns) {
            byte[] trans = info.Trans;
            int num_trans = trans.Length;
            if (num_trans > 0) {
                for (int i = 0; i < num_trans; i++) {
                    if (trans[i] < 255) {  /* not fully opaque */
                        ptransparency = true;
                        cmap.SetAlpha(i, trans[i]);
                    }
                }
            } else {
                Log.Error(func, "transparency array not returned");
            }
        }
        fp.Position = 0;
    }

    /*---------------------------------------------------------------------*
     *                         Writing png to file                         *
     *---------------------------------------------------------------------*/
    // leptonica: pixWritePng (pngio.c)
    /// <summary>Writes an image to a file in PNG format, with a specified gamma.</summary>
    /// <param name="filename">Path of the file to write.</param>
    /// <param name="pix">The image to write.</param>
    /// <param name="gamma">The gamma value to store in the file; use 0.0 if gamma is not defined.</param>
    /// <remarks>The general file-writing functions have no parameter for gamma.</remarks>
    public static void WritePng(string filename, Pix pix, float gamma)
    {
        using FileStream fp = Utils.FopenWriteStream(filename, "wb+");
        try {
            WriteStreamPng(fp, pix, gamma);
        } catch (LeptonicaException) {
            throw Lept.Fail("pixWritePng", $"pix not written to stream: {filename}");
        }
    }

    // leptonica: pixWriteStreamPng (pngio.c)
    /// <summary>Writes an image to a stream in PNG format.</summary>
    /// <param name="fp">The stream to write.</param>
    /// <param name="pix">The image to write.</param>
    /// <param name="gamma">The gamma value to store in the file; use 0.0 if gamma is not defined.</param>
    /// <remarks>
    /// (1) If called from pixWriteStream(), the stream is positioned
    ///     at the beginning of the file.
    /// (2) To do sequential writes of png format images to a stream,
    ///     use pixWriteStreamPng() directly.
    /// (3) gamma is an optional png chunk.  If no gamma value is to be
    ///     placed into the file, use gamma = 0.0.  Otherwise, if
    ///     gamma > 0.0, its value is written into the header.
    /// (4) The use of gamma in png is highly problematic.  For an illuminating
    ///     discussion, see:  http://hsivonen.iki.fi/png-gamma/
    /// (5) What is the effect/meaning of gamma in the png file?  This
    ///     gamma, which we can call the 'source' gamma, is the
    ///     inverse of the gamma that was used in enhance.c to brighten
    ///     or darken images.  The 'source' gamma is supposed to indicate
    ///     the intensity mapping that was done at the time the
    ///     image was captured.  Display programs typically apply a
    ///     'display' gamma of 2.2 to the output, which is intended
    ///     to linearize the intensity based on the response of
    ///     thermionic tubes (CRTs).  Flat panel LCDs have typically
    ///     been designed to give a similar response as CRTs (call it
    ///     "backward compatibility").  The 'display' gamma is
    ///     in some sense the inverse of the 'source' gamma.
    ///     jpeg encoders attached to scanners and cameras will lighten
    ///     the pixels, applying a gamma corresponding to approximately
    ///     a square-root relation of output vs input:
    ///           output = input^(gamma)
    ///     where gamma is often set near 0.4545  (1/gamma is 2.2).
    ///     This is stored in the image file.  Then if the display
    ///     program reads the gamma, it will apply a display gamma,
    ///     typically about 2.2; the product is 1.0, and the
    ///     display program produces a linear output.  This works because
    ///     the dark colors were appropriately boosted by the scanner,
    ///     as described by the 'source' gamma, so they should not
    ///     be further boosted by the display program.
    /// (6) As an example, with xv and display, if no gamma is stored,
    ///     the program acts as if gamma were 0.4545, multiplies this by 2.2,
    ///     and does a linear rendering.  Taking this as a baseline
    ///     brightness, if the stored gamma is:
    ///         &gt; 0.4545, the image is rendered lighter than baseline
    ///         &lt; 0.4545, the image is rendered darker than baseline
    ///     In contrast, gqview seems to ignore the gamma chunk in png.
    /// (7) The only valid pixel depths in leptonica are 1, 2, 4, 8, 16
    ///     and 32.  However, it is possible, and in some cases desirable,
    ///     to write out a png file using an rgb pix that has 24 bpp.
    ///     For example, the open source xpdf SplashBitmap class generates
    ///     24 bpp rgb images.  Consequently, we enable writing 24 bpp pix.
    ///     To generate such a pix, you can make a 24 bpp pix without data
    ///     and assign the data array to the pix; e.g.,
    ///         pix = pixCreateHeader(w, h, 24);
    ///         pixSetData(pix, rgbdata);
    ///     See pixConvert32To24() for an example, where we get rgbdata
    ///     from the 32 bpp pix.  Caution: do not call pixSetPadBits(),
    ///     because the alignment is wrong and you may erase part of the
    ///     last pixel on each line.
    /// (8) If the pix has a colormap, it is written to file.  In most
    ///     situations, the alpha component is 255 for each colormap entry,
    ///     which is opaque and indicates that it should be ignored.
    ///     However, if any alpha component is not 255, it is assumed that
    ///     the alpha values are valid, and they are written to the png
    ///     file in a tRNS segment.  On readback, the tRNS segment is
    ///     identified, and the colormapped image with alpha is converted
    ///     to a 4 spp rgba image.
    /// </remarks>
    public static void WriteStreamPng(Stream fp, Pix pix, float gamma)
    {
        WritePngCore(fp, pix, gamma, false, "pixWriteStreamPng");
    }

    // leptonica: pixWriteMemPng (pngio.c)
    /// <summary>Encodes an image in PNG format.</summary>
    /// <param name="pix">The image to encode.</param>
    /// <param name="gamma">The gamma value to store in the data; use 0.0 if gamma is not defined.</param>
    /// <returns>The PNG-encoded data.</returns>
    /// <remarks>See <see cref="WriteStreamPng"/>.</remarks>
    public static byte[] WriteMemPng(Pix pix, float gamma)
    {
        using MemoryStream ms = new MemoryStream();
        WritePngCore(ms, pix, gamma, true, "pixWriteMemPng");
        return ms.ToArray();
    }

    private static void WritePngCore(Stream fp, Pix pix, float gamma, bool mem, string func)
    {
        int w = pix.Width;
        int h = pix.Height;
        int d = pix.Depth;
        int spp = pix.Spp;

            /* A cmap validity check should prevent low-level colormap errors. */
        PixColormap? cmap;
        bool cmflag;
        if ((cmap = pix.Colormap) != null) {
            cmflag = true;
            if (!cmap.IsValid(pix))
                throw Lept.Fail(func, "colormap is not valid");
        } else {
            cmflag = false;
        }

            /* Do not set pad bits for d = 24 ! */
        if (d != 24) pix.SetPadBits(0);

            /* Set the color type and bit depth. */
        int bit_depth, color_type;
        if (d == 32 && spp == 4) {
            bit_depth = 8;
            color_type = PNG_COLOR_TYPE_RGBA;   /* 6 */
            cmflag = false;  /* ignore if it exists */
        } else if (d == 24 || d == 32) {
            bit_depth = 8;
            color_type = PNG_COLOR_TYPE_RGB;   /* 2 */
            cmflag = false;  /* ignore if it exists */
        } else {
            bit_depth = d;
            color_type = PNG_COLOR_TYPE_GRAY;  /* 0 */
        }
        if (cmflag)
            color_type = PNG_COLOR_TYPE_PALETTE;  /* 3 */

            /* With best zlib compression (9), get between 1 and 10% improvement
             * over default (6), but the compression is 3 to 10 times slower.
             * Use the zlib default (6) as our default compression unless
             * pix->special falls in the range [10 ... 19]; then subtract 10
             * to get the compression value.  */
        int compval = Z_DEFAULT_COMPRESSION;
        if (pix.Special >= 10 && pix.Special < 20)
            compval = pix.Special - 10;

        PngWriteInfo info = new PngWriteInfo {
            Width = w,
            Height = h,
            BitDepth = bit_depth,
            ColorType = color_type,
            CompressionLevel = compval,
        };

            /* Store resolution in ppm, if known */
        uint xres = (uint)(39.37 * (float)pix.XRes + 0.5);
        uint yres = (uint)(39.37 * (float)pix.YRes + 0.5);
        if ((xres == 0) || (yres == 0)) {
            info.PhysX = 0;   /* PNG_RESOLUTION_UNKNOWN */
            info.PhysY = 0;
        } else {
            info.PhysX = xres;   /* PNG_RESOLUTION_METER */
            info.PhysY = yres;
        }

        if (cmflag) {
                /* Make and save the palette */
            int ncolors = cmap!.Count;
            PngColor[] palette = new PngColor[ncolors];
            byte[] alpha = new byte[256];
            cmap.ToArrays(out int[] rmap, out int[] gmap, out int[] bmap, out int[] amap);
            for (int i = 0; i < ncolors; i++) {
                palette[i] = new PngColor((byte)rmap[i], (byte)gmap[i], (byte)bmap[i]);
                alpha[i] = (byte)amap[i];
            }
            info.Palette = palette;

                /* Add the tRNS chunk.  If the non-opaque colors are listed
                 * first in the colormap, as in the spec, we can use that in
                 * the 4th arg of png_set_tRNS.  Otherwise, transparency will
                 * be lost for some colors.  To prevent that, see the comments
                 * in pixcmapNonOpaqueColorsInfo(). */
            bool opaque = cmap.IsOpaque();
            if (!opaque) {  /* alpha channel has some transparency; assume valid */
                int num_trans;
                if (!mem) {
                    cmap.NonOpaqueColorsInfo(out _, out int max_trans, out _);
                    num_trans = max_trans + 1;
                } else {
                    num_trans = ncolors;
                }
                info.Trans = alpha.AsSpan(0, num_trans).ToArray();
            }
        }

            /* 0.4545 is treated as the default by some image
             * display programs (not gqview).  A value > 0.4545 will
             * lighten an image as displayed by xv, display, etc. */
        if (gamma > 0.0)
            info.Gamma = (double)gamma;

        string? text;
        if ((text = pix.Text) != null)
            info.Texts.Add(new PngText("Comment", text));

        byte[] rows;
        if ((d != 32) && (d != 24)) {  /* not rgb color */
                /* Generate a temporary pix with bytes swapped.
                 * For writing a 1 bpp image as png:
                 *    ~ if no colormap, invert the data, because png writes
                 *      black as 0
                 *    ~ if colormapped, do not invert the data; the two RGBA
                 *      colors can have any value.  */
            Pix pix1;
            if (d == 1 && cmap == null) {
                pix1 = Invert(null, pix);
                pix1.EndianByteSwap();
            } else {
                pix1 = pix.EndianByteSwapNew();
            }

                /* Row bytes as seen by png: the start of each raster line */
            int rowbytes = (w * bit_depth + 7) / 8;
            int wpl = pix1.Wpl;
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(pix1.Data.AsSpan());
            rows = new byte[(long)rowbytes * h];
            for (int i = 0; i < h; i++)
                bytes.Slice(4 * i * wpl, rowbytes).CopyTo(rows.AsSpan(i * rowbytes));
        } else if (d == 24) {  /* See note 7 above */
            int rowbytes = 3 * w;
            int wpl = pix.Wpl;
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(pix.Data.AsSpan());
            rows = new byte[(long)rowbytes * h];
            for (int i = 0; i < h; i++)
                bytes.Slice(4 * i * wpl, rowbytes).CopyTo(rows.AsSpan(i * rowbytes));
        } else {  /* 32 bpp rgb and rgba.  If spp = 4, write the alpha channel */
            int rowbytes = (spp == 4) ? 4 * w : 3 * w;
            uint[] data = pix.Data;
            int wpl = pix.Wpl;
            rows = new byte[(long)rowbytes * h];
            for (int i = 0; i < h; i++) {
                int ppixel = i * wpl;
                int k = i * rowbytes;
                for (int j = 0; j < w; j++) {
                    uint word = data[ppixel];
                    rows[k++] = (byte)GetDataByte(word, L.COLOR_RED);
                    rows[k++] = (byte)GetDataByte(word, L.COLOR_GREEN);
                    rows[k++] = (byte)GetDataByte(word, L.COLOR_BLUE);
                    if (spp == 4)
                        rows[k++] = (byte)GetDataByte(word, L.L_ALPHA_CHANNEL);
                    ppixel++;
                }
            }
        }

        try {
            PngEncoder.Write(fp, info, rows);
        } catch (PngException) {
            throw Lept.Fail(func, "internal png error");
        }
    }

    // leptonica: pixSetZlibCompression (pngio.c)
    /// <param name="compval">zlib compression value</param>
    /// <remarks>
    /// (1) Valid zlib compression values are in the interval [0 ... 9],
    ///     where, as defined in zlib.h:
    ///       0         Z_NO_COMPRESSION
    ///       1         Z_BEST_SPEED    (poorest compression)
    ///       9         Z_BEST_COMPRESSION
    ///     For the default value, use either of these:
    ///       6         Z_DEFAULT_COMPRESSION
    ///      -1         (resolves to Z_DEFAULT_COMPRESSION)
    /// </remarks>
    public void SetZlibCompression(int compval)
    {
        if (compval < 0 || compval > 9) {
            Log.Error("pixSetZlibCompression", "Invalid zlib comp val; using default");
            compval = Z_DEFAULT_COMPRESSION;
        }
        Special = 10 + compval;  /* valid range [10 ... 19] */
    }

    // leptonica: l_pngSetReadStrip16To8 (pngio.c)
    /// <param name="flag">1 for stripping 16 bpp to 8 bpp on reading; 0 for leaving 16 bpp</param>
    public static void PngSetReadStrip16To8(int flag)
    {
        var_PNG_STRIP_16_TO_8 = flag;
    }

    /* png.h / zlib.h constants used here */
    private const int PNG_COLOR_MASK_PALETTE = 1;
    private const int PNG_COLOR_TYPE_GRAY = 0;
    private const int PNG_COLOR_TYPE_PALETTE = 3;
    private const int PNG_COLOR_TYPE_RGB = 2;
    private const int PNG_COLOR_TYPE_RGBA = 6;
    private const int Z_DEFAULT_COMPRESSION = -1;
}
