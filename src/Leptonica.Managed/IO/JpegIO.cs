using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;
using BitMiracle.LibJpeg.Classic;
using static Leptonica.ArrayAccess;

namespace Leptonica;

public static partial class L
{
    /* Hinting bit flags in jpeg reader (imageio.h) */
    /// <summary>Hint for the JPEG reader: read only the luminance channel of a color image.</summary>
    public const int L_JPEG_READ_LUMINANCE = 1;
    /// <summary>Hint for the JPEG reader: continue reading when the data is corrupt, counting the warnings.</summary>
    public const int L_JPEG_CONTINUE_WITH_BAD_DATA = 2;

    /* Special flag for chroma sampling on write (pix.h) */
    /// <summary>Special flag that disables chroma subsampling (4:4:4) when a JPEG image is written.</summary>
    public const int L_NO_CHROMA_SAMPLING_JPEG = 1;
}

/*
 *  jpegio.c
 *
 *    Read jpeg from file
 *          PIX             *pixReadJpeg()  [special top level]
 *          PIX             *pixReadStreamJpeg()
 *
 *    Read jpeg metadata from file
 *          l_int32          readHeaderJpeg()
 *          l_int32          freadHeaderJpeg()
 *          l_int32          fgetJpegResolution()
 *          l_int32          fgetJpegComment()
 *
 *    Write jpeg to file
 *          l_int32          pixWriteJpeg()  [special top level]
 *          l_int32          pixWriteStreamJpeg()
 *
 *    Read/write to memory
 *          PIX             *pixReadMemJpeg()
 *          l_int32          readHeaderMemJpeg()
 *          l_int32          readResolutionMemJpeg()
 *          l_int32          pixWriteMemJpeg()
 *
 *    Setting special flag for chroma sampling on write
 *          l_int32          pixSetChromaSampling()
 */
public sealed partial class Pix
{
    private sealed class JpegCodecException : Exception
    {
        public JpegCodecException(string message) : base(message) { }
    }

    private sealed class SilentJpegErrorMgr : jpeg_error_mgr
    {
        public override void output_message()
        {
            /* Suppress console/debug output; errors/warnings are handled by Leptonica.Managed. */
        }

        public override void error_exit()
        {
            throw new JpegCodecException(format_message());
        }
    }

    private static Stream EnsureSeekableReadStream(Stream fp, out MemoryStream? tempCopy)
    {
        if (fp.CanSeek)
        {
            tempCopy = null;
            fp.Position = 0;
            return fp;
        }

        tempCopy = new MemoryStream();
        fp.CopyTo(tempCopy);
        tempCopy.Position = 0;
        return tempCopy;
    }

    /*---------------------------------------------------------------------*
     *                 Read jpeg from file (special function)              *
     *---------------------------------------------------------------------*/
    // leptonica: pixReadJpeg (jpegio.c)
    /// <summary>Reads a JPEG image from a file; any JPEG comment is stored as the text of the image.</summary>
    /// <param name="filename">Path of the JPEG file.</param>
    /// <param name="cmapflag">0 for no colormap; 1 to return an 8 bpp colormapped image if the file has 3 or 4 samples per pixel.</param>
    /// <param name="reduction">Scaling factor: 1, 2, 4 or 8.</param>
    /// <param name="pnwarn">Receives the number of warnings about corrupted data.</param>
    /// <param name="hint">Bitwise OR of <c>L_JPEG_*</c> values; 0 for the default.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="filename"/> is <see langword="null"/>, or <paramref name="reduction"/> is not 1, 2, 4 or 8.</exception>
    public static Pix ReadJpeg(string filename, int cmapflag, int reduction, out int pnwarn, int hint = 0)
    {
        const string func = "pixReadJpeg";
        pnwarn = 0;
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");
        if (cmapflag != 0 && cmapflag != 1)
            cmapflag = 0;
        if (reduction != 1 && reduction != 2 && reduction != 4 && reduction != 8)
            throw Lept.Arg(func, "reduction not in {1,2,4,8}");

        using FileStream fp = Utils.FopenReadStream(filename);
        Pix pix = ReadStreamJpeg(fp, cmapflag, reduction, out pnwarn, hint);
        if (TryFgetJpegComment(fp, out byte[]? comment) && comment != null && comment.Length > 0)
            pix.Text = Encoding.Latin1.GetString(comment);
        return pix;
    }

    /// <summary>Reads a JPEG image from a file; any JPEG comment is stored as the text of the image.</summary>
    /// <param name="filename">Path of the JPEG file.</param>
    /// <param name="cmapflag">0 for no colormap; 1 to return an 8 bpp colormapped image if the file has 3 or 4 samples per pixel.</param>
    /// <param name="reduction">Scaling factor: 1, 2, 4 or 8.</param>
    /// <param name="hint">Bitwise OR of <c>L_JPEG_*</c> values; 0 for the default.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="filename"/> is <see langword="null"/>, or <paramref name="reduction"/> is not 1, 2, 4 or 8.</exception>
    public static Pix ReadJpeg(string filename, int cmapflag = 0, int reduction = 1, int hint = 0)
        => ReadJpeg(filename, cmapflag, reduction, out _, hint);

    // leptonica: pixReadStreamJpeg (jpegio.c)
    /// <summary>Reads a JPEG image from a stream.</summary>
    /// <param name="fp">The stream to read.</param>
    /// <param name="cmapflag">0 for no colormap; 1 to return an 8 bpp colormapped image if the data has 3 or 4 samples per pixel.</param>
    /// <param name="reduction">Scaling factor: 1, 2, 4 or 8.</param>
    /// <param name="pnwarn">Receives the number of warnings about corrupted data.</param>
    /// <param name="hint">Bitwise OR of <c>L_JPEG_*</c> values; 0 for the default.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> is <see langword="null"/>, or <paramref name="reduction"/> is not 1, 2, 4 or 8.</exception>
    public static Pix ReadStreamJpeg(Stream fp, int cmapflag, int reduction, out int pnwarn, int hint = 0)
    {
        const string func = "pixReadStreamJpeg";
        pnwarn = 0;
        if (fp == null)
            throw Lept.Arg(func, "fp not defined");
        if (cmapflag != 0 && cmapflag != 1)
            cmapflag = 0;
        if (reduction != 1 && reduction != 2 && reduction != 4 && reduction != 8)
            throw Lept.Arg(func, "reduction not in {1,2,4,8}");

        Stream srcStream = EnsureSeekableReadStream(fp, out MemoryStream? tempCopy);
        SilentJpegErrorMgr jerr = new SilentJpegErrorMgr();
        jpeg_decompress_struct cinfo = new jpeg_decompress_struct(jerr);

        try
        {
            cinfo.jpeg_stdio_src(srcStream);
            cinfo.jpeg_read_header(true);
            cinfo.Scale_denom = reduction;
            cinfo.Scale_num = 1;
            cinfo.jpeg_calc_output_dimensions();

            int spp;
            if ((hint & L.L_JPEG_READ_LUMINANCE) != 0)
            {
                cinfo.Out_color_space = J_COLOR_SPACE.JCS_GRAYSCALE;
                cinfo.jpeg_calc_output_dimensions();
                spp = 1;
                Log.Info(func, "reading luminance channel only");
            }
            else
            {
                spp = cinfo.Out_color_components;
            }

            int w = cinfo.Output_width;
            int h = cinfo.Output_height;
            bool ycck = (cinfo.Jpeg_color_space == J_COLOR_SPACE.JCS_YCCK && spp == 4 && cmapflag == 0);
            bool cmyk = (cinfo.Jpeg_color_space == J_COLOR_SPACE.JCS_CMYK && spp == 4 && cmapflag == 0);
            if (spp != 1 && spp != 3 && !ycck && !cmyk)
            {
                cinfo.jpeg_destroy();
                throw Lept.Fail(func, "spp must be 1 or 3, or YCCK or CMYK");
            }

            byte[] rowbuffer;
            Pix pix;
            if ((spp == 3 && cmapflag == 0) || ycck || cmyk)
            {
                rowbuffer = new byte[spp * w];
                pix = Create(w, h, 32);
            }
            else
            {
                rowbuffer = new byte[w];
                pix = Create(w, h, 8);
            }
            pix.InputFormat = L.IFF_JFIF_JPEG;

            if (spp == 1)
            {
                cinfo.jpeg_start_decompress();
            }
            else if (cmapflag == 0)
            {
                cinfo.Quantize_colors = false;
                cinfo.jpeg_start_decompress();
            }
            else
            {
                cinfo.Quantize_colors = true;
                cinfo.Desired_number_of_colors = 256;
                cinfo.jpeg_start_decompress();

                PixColormap cmap = PixColormap.Create(8);
                int ncolors = cinfo.Actual_number_of_colors;
                byte[][] jcmap = cinfo.Colormap;
                for (int cindex = 0; cindex < ncolors; cindex++)
                {
                    int rval = jcmap[0][cindex];
                    int gval = jcmap[1][cindex];
                    int bval = jcmap[2][cindex];
                    cmap.AddColor(rval, gval, bval);
                }
                pix.SetColormap(cmap);
            }

            int wpl = pix.Wpl;
            uint[] data = pix.Data;
            byte[][] scanlines = new byte[][] { rowbuffer };
            bool abortOnWarning = (hint & L.L_JPEG_CONTINUE_WITH_BAD_DATA) == 0;
            int nwarn = 0;

            for (int i = 0; i < h; i++)
            {
                int nlinesread = cinfo.jpeg_read_scanlines(scanlines, 1);
                nwarn = jerr.Num_warnings;
                if (nlinesread == 0 || (abortOnWarning && nwarn > 0))
                {
                    Log.Error(func, $"read error at scanline {i}; nwarn = {nwarn}");
                    cinfo.jpeg_destroy();
                    pnwarn = nwarn;
                    throw Lept.Fail(func, "bad data");
                }

                int lineIdx = i * wpl;
                if ((spp == 3 && cmapflag == 0) || ycck || cmyk)
                {
                    if (spp == 3)
                    {
                        int j = 0, k = 0;
                        if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian)
                        {
                            /* 4 RGB triplets -> 4 words 0xRRGGBB00 per step */
                            Vector128<byte> rgbToWord = Vector128.Create(
                                (byte)0xFF, 2, 1, 0, 0xFF, 5, 4, 3, 0xFF, 8, 7, 6, 0xFF, 11, 10, 9);
                            Span<uint> line = data.AsSpan(lineIdx, wpl);
                            for (; j + 4 <= w && k + 16 <= rowbuffer.Length; j += 4, k += 12)
                            {
                                Vector128<byte> v = Vector128.LoadUnsafe(ref rowbuffer[k]);
                                Vector128.Shuffle(v, rgbToWord).AsUInt32().StoreUnsafe(ref line[j]);
                            }
                        }
                        for (; j + 3 < w; j += 4, k += 12)
                        {
                            uint r0 = rowbuffer[k], g0 = rowbuffer[k + 1], b0 = rowbuffer[k + 2];
                            uint r1 = rowbuffer[k + 3], g1 = rowbuffer[k + 4], b1 = rowbuffer[k + 5];
                            uint r2 = rowbuffer[k + 6], g2 = rowbuffer[k + 7], b2 = rowbuffer[k + 8];
                            uint r3 = rowbuffer[k + 9], g3 = rowbuffer[k + 10], b3 = rowbuffer[k + 11];
                            data[lineIdx + j] = (r0 << L.L_RED_SHIFT) | (g0 << L.L_GREEN_SHIFT) | (b0 << L.L_BLUE_SHIFT);
                            data[lineIdx + j + 1] = (r1 << L.L_RED_SHIFT) | (g1 << L.L_GREEN_SHIFT) | (b1 << L.L_BLUE_SHIFT);
                            data[lineIdx + j + 2] = (r2 << L.L_RED_SHIFT) | (g2 << L.L_GREEN_SHIFT) | (b2 << L.L_BLUE_SHIFT);
                            data[lineIdx + j + 3] = (r3 << L.L_RED_SHIFT) | (g3 << L.L_GREEN_SHIFT) | (b3 << L.L_BLUE_SHIFT);
                        }
                        for (; j < w; j++)
                        {
                            uint r = rowbuffer[k++];
                            uint g = rowbuffer[k++];
                            uint b = rowbuffer[k++];
                            data[lineIdx + j] = (r << L.L_RED_SHIFT) | (g << L.L_GREEN_SHIFT) | (b << L.L_BLUE_SHIFT);
                        }
                    }
                    else
                    {
                        bool sawAdobe = cinfo.m_saw_Adobe_marker;
                        for (int j = 0, k = 0; j < w; j++)
                        {
                            int cyan = rowbuffer[k++];
                            int magenta = rowbuffer[k++];
                            int yellow = rowbuffer[k++];
                            int black = rowbuffer[k++];
                            int rval, gval, bval;
                            if (sawAdobe)
                            {
                                rval = (black * cyan) / 255;
                                gval = (black * magenta) / 255;
                                bval = (black * yellow) / 255;
                            }
                            else
                            {
                                rval = black * (255 - cyan) / 255;
                                gval = black * (255 - magenta) / 255;
                                bval = black * (255 - yellow) / 255;
                            }
                            rval = Math.Clamp(rval, 0, 255);
                            gval = Math.Clamp(gval, 0, 255);
                            bval = Math.Clamp(bval, 0, 255);
                            ComposeRGBPixel(rval, gval, bval, out data[lineIdx + j]);
                        }
                    }
                }
                else
                {
                    Span<uint> lineSpan = data.AsSpan(lineIdx, wpl);
                    for (int j = 0; j < w; j++)
                        SetDataByte(lineSpan, j, rowbuffer[j]);
                }
            }

            if (cinfo.Density_unit == DensityUnit.DotsInch)
            {
                pix.XRes = cinfo.X_density;
                pix.YRes = cinfo.Y_density;
            }
            else if (cinfo.Density_unit == DensityUnit.DotsCm)
            {
                pix.XRes = (int)((float)cinfo.X_density * 2.54f + 0.5f);
                pix.YRes = (int)((float)cinfo.Y_density * 2.54f + 0.5f);
            }

            cinfo.jpeg_finish_decompress();
            cinfo.jpeg_destroy();
            pnwarn = nwarn;
            if (nwarn > 0)
                Log.Warning(func, $"{nwarn} warning(s) of bad data");
            return pix;
        }
        catch (JpegCodecException)
        {
            cinfo.jpeg_destroy();
            throw Lept.Fail(func, "internal jpeg error");
        }
        finally
        {
            tempCopy?.Dispose();
        }
    }

    /// <summary>Reads a JPEG image from a stream.</summary>
    /// <param name="fp">The stream to read.</param>
    /// <param name="cmapflag">0 for no colormap; 1 to return an 8 bpp colormapped image if the data has 3 or 4 samples per pixel.</param>
    /// <param name="reduction">Scaling factor: 1, 2, 4 or 8.</param>
    /// <param name="hint">Bitwise OR of <c>L_JPEG_*</c> values; 0 for the default.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> is <see langword="null"/>, or <paramref name="reduction"/> is not 1, 2, 4 or 8.</exception>
    public static Pix ReadStreamJpeg(Stream fp, int cmapflag = 0, int reduction = 1, int hint = 0)
        => ReadStreamJpeg(fp, cmapflag, reduction, out _, hint);

    /*---------------------------------------------------------------------*
     *                     Read jpeg metadata from file                    *
     *---------------------------------------------------------------------*/
    // leptonica: readHeaderJpeg (jpegio.c)
    /// <summary>Reads the header information of a JPEG file without decoding the pixels.</summary>
    /// <param name="filename">Path of the JPEG file.</param>
    /// <param name="pw">Receives the image width.</param>
    /// <param name="ph">Receives the image height.</param>
    /// <param name="pspp">Receives the number of samples per pixel: 1, 3 or 4.</param>
    /// <param name="pycck">Receives <see langword="true"/> if the color space is YCCK.</param>
    /// <param name="pcmyk">Receives <see langword="true"/> if the color space is CMYK.</param>
    /// <exception cref="System.ArgumentException"><paramref name="filename"/> is <see langword="null"/>.</exception>
    public static void ReadHeaderJpeg(string filename, out int pw, out int ph,
                                      out int pspp, out bool pycck, out bool pcmyk)
    {
        const string func = "readHeaderJpeg";
        pw = ph = pspp = 0;
        pycck = pcmyk = false;
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");

        using FileStream fp = Utils.FopenReadStream(filename);
        FreadHeaderJpeg(fp, out pw, out ph, out pspp, out pycck, out pcmyk);
    }

    // leptonica: freadHeaderJpeg (jpegio.c)
    /// <summary>Reads the header information of a JPEG image from a stream.</summary>
    /// <param name="fp">The stream to read.</param>
    /// <param name="pw">Receives the image width.</param>
    /// <param name="ph">Receives the image height.</param>
    /// <param name="pspp">Receives the number of samples per pixel: 1, 3 or 4.</param>
    /// <param name="pycck">Receives <see langword="true"/> if the color space is YCCK.</param>
    /// <param name="pcmyk">Receives <see langword="true"/> if the color space is CMYK.</param>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> is <see langword="null"/>.</exception>
    public static void FreadHeaderJpeg(Stream fp, out int pw, out int ph,
                                       out int pspp, out bool pycck, out bool pcmyk)
    {
        const string func = "freadHeaderJpeg";
        pw = ph = pspp = 0;
        pycck = pcmyk = false;
        if (fp == null)
            throw Lept.Arg(func, "stream not defined");

        Stream srcStream = EnsureSeekableReadStream(fp, out MemoryStream? tempCopy);
        SilentJpegErrorMgr jerr = new SilentJpegErrorMgr();
        jpeg_decompress_struct cinfo = new jpeg_decompress_struct(jerr);

        try
        {
            cinfo.jpeg_stdio_src(srcStream);
            cinfo.jpeg_read_header(true);
            cinfo.jpeg_calc_output_dimensions();
            int spp = cinfo.Out_color_components;
            int w = cinfo.Output_width;
            int h = cinfo.Output_height;
            if (w < 1 || h < 1 || spp < 1 || spp > 4)
            {
                cinfo.jpeg_destroy();
                if (fp.CanSeek)
                    fp.Position = 0;
                throw Lept.Fail(func, "bad jpeg image parameters");
            }

            pspp = spp;
            pw = w;
            ph = h;
            pycck = (cinfo.Jpeg_color_space == J_COLOR_SPACE.JCS_YCCK && spp == 4);
            pcmyk = (cinfo.Jpeg_color_space == J_COLOR_SPACE.JCS_CMYK && spp == 4);
            cinfo.jpeg_destroy();
            if (fp.CanSeek)
                fp.Position = 0;
        }
        catch (JpegCodecException)
        {
            cinfo.jpeg_destroy();
            if (fp.CanSeek)
                fp.Position = 0;
            throw Lept.Fail(func, "internal jpeg error");
        }
        finally
        {
            tempCopy?.Dispose();
        }
    }

    // leptonica: fgetJpegResolution (jpegio.c)
    /// <summary>Reads the resolution of a JPEG image from its metadata.</summary>
    /// <param name="fp">The stream to read.</param>
    /// <param name="pxres">Receives the horizontal resolution in ppi, or 0 if it is not specified.</param>
    /// <param name="pyres">Receives the vertical resolution in ppi, or 0 if it is not specified.</param>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> is <see langword="null"/>.</exception>
    public static void FgetJpegResolution(Stream fp, out int pxres, out int pyres)
    {
        const string func = "fgetJpegResolution";
        pxres = pyres = 0;
        if (fp == null)
            throw Lept.Arg(func, "stream not opened");

        Stream srcStream = EnsureSeekableReadStream(fp, out MemoryStream? tempCopy);
        SilentJpegErrorMgr jerr = new SilentJpegErrorMgr();
        jpeg_decompress_struct cinfo = new jpeg_decompress_struct(jerr);

        try
        {
            cinfo.jpeg_stdio_src(srcStream);
            cinfo.jpeg_read_header(true);

            if (cinfo.Density_unit == DensityUnit.DotsInch)
            {
                pxres = cinfo.X_density;
                pyres = cinfo.Y_density;
            }
            else if (cinfo.Density_unit == DensityUnit.DotsCm)
            {
                pxres = (int)((float)cinfo.X_density * 2.54f + 0.5f);
                pyres = (int)((float)cinfo.Y_density * 2.54f + 0.5f);
            }

            cinfo.jpeg_destroy();
            if (fp.CanSeek)
                fp.Position = 0;
        }
        catch (JpegCodecException)
        {
            cinfo.jpeg_destroy();
            if (fp.CanSeek)
                fp.Position = 0;
            throw Lept.Fail(func, "internal jpeg error");
        }
        finally
        {
            tempCopy?.Dispose();
        }
    }

    // leptonica: fgetJpegComment (jpegio.c)
    /// <summary>Reads the comment of a JPEG image, if there is one.</summary>
    /// <param name="fp">The stream to read.</param>
    /// <param name="pcomment">Receives the comment bytes, or <see langword="null"/> if the image has no comment.</param>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> is <see langword="null"/>.</exception>
    /// <exception cref="LeptonicaException">The JPEG data cannot be parsed.</exception>
    public static void FgetJpegComment(Stream fp, out byte[]? pcomment)
    {
        const string func = "fgetJpegComment";
        pcomment = null;
        if (fp == null)
            throw Lept.Arg(func, "stream not opened");

        if (!TryFgetJpegComment(fp, out pcomment))
            throw Lept.Fail(func, "internal jpeg error");
    }

    private static bool TryFgetJpegComment(Stream fp, out byte[]? pcomment)
    {
        pcomment = null;
        Stream srcStream = EnsureSeekableReadStream(fp, out MemoryStream? tempCopy);
        SilentJpegErrorMgr jerr = new SilentJpegErrorMgr();
        jpeg_decompress_struct cinfo = new jpeg_decompress_struct(jerr);

        try
        {
            cinfo.jpeg_save_markers((int)JPEG_MARKER.COM, 0xFFFF);
            cinfo.jpeg_stdio_src(srcStream);
            cinfo.jpeg_read_header(true);

            var markers = cinfo.Marker_list;
            if (markers != null)
            {
                for (int i = 0; i < markers.Count; i++)
                {
                    if (markers[i].Marker == (byte)JPEG_MARKER.COM && markers[i].Data != null && markers[i].Data.Length > 0)
                        pcomment = (byte[])markers[i].Data.Clone();
                }
            }

            cinfo.jpeg_destroy();
            if (fp.CanSeek)
                fp.Position = 0;
            return true;
        }
        catch (JpegCodecException)
        {
            cinfo.jpeg_destroy();
            pcomment = null;
            if (fp.CanSeek)
                fp.Position = 0;
            return false;
        }
        finally
        {
            tempCopy?.Dispose();
        }
    }

    /*---------------------------------------------------------------------*
     *                             Writing Jpeg                            *
     *---------------------------------------------------------------------*/
    // leptonica: pixWriteJpeg (jpegio.c)
    /// <summary>Writes an image to a file in JPEG format.</summary>
    /// <param name="filename">Path of the file to write.</param>
    /// <param name="pix">The image to write.</param>
    /// <param name="quality">Quality from 1 to 100; 75 is the default.</param>
    /// <param name="progressive">0 for baseline sequential encoding; 1 for progressive encoding.</param>
    /// <exception cref="System.ArgumentException"><paramref name="pix"/> or <paramref name="filename"/> is <see langword="null"/>.</exception>
    public static void WriteJpeg(string filename, Pix pix, int quality, int progressive)
    {
        const string func = "pixWriteJpeg";
        if (pix == null)
            throw Lept.Arg(func, "pix not defined");
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");

        using FileStream fp = Utils.FopenWriteStream(filename, "wb+");
        WriteStreamJpeg(fp, pix, quality, progressive);
    }

    // leptonica: pixWriteStreamJpeg (jpegio.c)
    /// <summary>Writes an image to a stream in JPEG format.</summary>
    /// <param name="fp">The stream to write.</param>
    /// <param name="pixs">The image to write.</param>
    /// <param name="quality">Quality from 1 to 100; 75 is the default.</param>
    /// <param name="progressive">0 for baseline sequential encoding; 1 for progressive encoding.</param>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> or <paramref name="pixs"/> is <see langword="null"/>.</exception>
    public static void WriteStreamJpeg(Stream fp, Pix pixs, int quality, int progressive)
    {
        const string func = "pixWriteStreamJpeg";
        if (fp == null)
            throw Lept.Arg(func, "stream not open");
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");
        if (quality <= 0)
            quality = 75;
        if (quality > 100)
        {
            Log.Error(func, "invalid jpeg quality; setting to 75");
            quality = 75;
        }

        pixs.GetDimensions(out int w, out int h, out int d);
        Pix pix;
        if (pixs.Colormap != null)
        {
            Log.Info(func, "removing colormap; may be better to compress losslessly");
            pix = pixs.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        }
        else if (d >= 8 && d != 16)
        {
            pix = Clone(pixs);
        }
        else if (d < 8 || d == 16)
        {
            Log.Info(func, $"converting from {d} to 8 bpp");
            pix = pixs.ConvertTo8(false);
        }
        else
        {
            Log.Error(func, $"unknown pix type with d = {d} and no cmap");
            throw Lept.Fail(func, $"unknown pix type with d = {d} and no cmap");
        }
        pix.SetPadBits(0);

        if (fp.CanSeek)
            fp.Position = 0;

        SilentJpegErrorMgr jerr = new SilentJpegErrorMgr();
        jpeg_compress_struct cinfo = new jpeg_compress_struct(jerr);

        try
        {
            cinfo.jpeg_stdio_dest(fp);
            cinfo.Image_width = w;
            cinfo.Image_height = h;

            d = pix.Depth;
            int colorflag;
            if (d == 8)
            {
                colorflag = 0;
                cinfo.Input_components = 1;
                cinfo.In_color_space = J_COLOR_SPACE.JCS_GRAYSCALE;
            }
            else
            {
                colorflag = 1;
                cinfo.Input_components = 3;
                cinfo.In_color_space = J_COLOR_SPACE.JCS_RGB;
            }

            cinfo.jpeg_set_defaults();
            cinfo.Optimize_coding = false;

            int xres = pix.XRes;
            int yres = pix.YRes;
            if (xres != 0 && yres != 0)
            {
                cinfo.Density_unit = DensityUnit.DotsInch;
                cinfo.X_density = (short)Math.Clamp(xres, 1, short.MaxValue);
                cinfo.Y_density = (short)Math.Clamp(yres, 1, short.MaxValue);
            }

            cinfo.jpeg_set_quality(quality, true);
            if (progressive != 0)
                cinfo.jpeg_simple_progression();

            if (pixs.Special == L.L_NO_CHROMA_SAMPLING_JPEG && colorflag == 1)
            {
                cinfo.Component_info[0].H_samp_factor = 1;
                cinfo.Component_info[0].V_samp_factor = 1;
                cinfo.Component_info[1].H_samp_factor = 1;
                cinfo.Component_info[1].V_samp_factor = 1;
                cinfo.Component_info[2].H_samp_factor = 1;
                cinfo.Component_info[2].V_samp_factor = 1;
            }

            cinfo.jpeg_start_compress(true);

            string? text = pix.Text;
            if (!string.IsNullOrEmpty(text))
            {
                byte[] textBytes = Encoding.Latin1.GetBytes(text);
                if (textBytes.Length > 65433)
                {
                    Log.Warning(func, $"text is {textBytes.Length} bytes; clipping to 65433");
                    Array.Resize(ref textBytes, 65433);
                }
                cinfo.jpeg_write_marker((int)JPEG_MARKER.COM, textBytes);
            }

            int spp = cinfo.Input_components;
            byte[] rowbuffer = new byte[spp * w];
            byte[][] scanlines = new byte[][] { rowbuffer };
            uint[] data = pix.Data;
            int wpl = pix.Wpl;

            for (int i = 0; i < h; i++)
            {
                int lineIdx = i * wpl;
                if (colorflag == 0)
                {
                    ReadOnlySpan<uint> lineSpan = data.AsSpan(lineIdx, wpl);
                    int j = 0;
                    if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian)
                    {
                        /* 4 words (16 pixels, first pixel in the high byte) per step */
                        Vector128<byte> wordToBytes = Vector128.Create(
                            (byte)3, 2, 1, 0, 7, 6, 5, 4, 11, 10, 9, 8, 15, 14, 13, 12);
                        for (; j + 16 <= w && (j >> 2) + 4 <= lineSpan.Length; j += 16)
                        {
                            Vector128<byte> v = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(lineSpan), (nuint)(j >> 2)).AsByte();
                            Vector128.Shuffle(v, wordToBytes).StoreUnsafe(ref rowbuffer[j]);
                        }
                    }
                    for (; j < w; j++)
                        rowbuffer[j] = (byte)GetDataByte(lineSpan, j);
                }
                else if (d == 24)
                {
                    ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(data.AsSpan(lineIdx, wpl));
                    bytes.Slice(0, 3 * w).CopyTo(rowbuffer);
                }
                else
                {
                    int j = 0, k = 0;
                    if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian)
                    {
                        /* 4 words 0xRRGGBB00 -> 4 RGB triplets per step (16-byte store, 12 used) */
                        Vector128<byte> wordToRgb = Vector128.Create(
                            (byte)3, 2, 1, 7, 6, 5, 11, 10, 9, 15, 14, 13, 0xFF, 0xFF, 0xFF, 0xFF);
                        for (; j + 4 <= w && k + 16 <= rowbuffer.Length; j += 4, k += 12)
                        {
                            Vector128<byte> v = Vector128.LoadUnsafe(ref data[lineIdx + j]).AsByte();
                            Vector128.Shuffle(v, wordToRgb).StoreUnsafe(ref rowbuffer[k]);
                        }
                    }
                    for (; j < w; j++)
                    {
                        uint word = data[lineIdx + j];
                        rowbuffer[k++] = (byte)((word >> L.L_RED_SHIFT) & 0xff);
                        rowbuffer[k++] = (byte)((word >> L.L_GREEN_SHIFT) & 0xff);
                        rowbuffer[k++] = (byte)((word >> L.L_BLUE_SHIFT) & 0xff);
                    }
                }
                cinfo.jpeg_write_scanlines(scanlines, 1);
            }

            cinfo.jpeg_finish_compress();
            cinfo.jpeg_destroy();
        }
        catch (JpegCodecException)
        {
            cinfo.jpeg_destroy();
            throw Lept.Fail(func, "internal jpeg error");
        }
    }

    /*---------------------------------------------------------------------*
     *                         Read/write to memory                        *
     *---------------------------------------------------------------------*/
    // leptonica: pixReadMemJpeg (jpegio.c)
    /// <summary>Reads a JPEG image from memory; any JPEG comment is stored as the text of the image.</summary>
    /// <param name="data">The encoded JPEG data.</param>
    /// <param name="cmflag">0 for no colormap; 1 to return an 8 bpp colormapped image if the data has 3 or 4 samples per pixel.</param>
    /// <param name="reduction">Scaling factor: 1, 2, 4 or 8.</param>
    /// <param name="pnwarn">Receives the number of warnings about corrupted data.</param>
    /// <param name="hint">Bitwise OR of <c>L_JPEG_*</c> values; 0 for the default.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="data"/> is empty, or <paramref name="reduction"/> is not 1, 2, 4 or 8.</exception>
    public static Pix ReadMemJpeg(ReadOnlySpan<byte> data, int cmflag, int reduction, out int pnwarn, int hint = 0)
    {
        const string func = "pixReadMemJpeg";
        pnwarn = 0;
        if (data.IsEmpty)
            throw Lept.Arg(func, "data not defined");

        byte[] buf = data.ToArray();
        using MemoryStream fp = new MemoryStream(buf, false);
        Pix pix = ReadStreamJpeg(fp, cmflag, reduction, out pnwarn, hint);
        if (TryFgetJpegComment(fp, out byte[]? comment) && comment != null && comment.Length > 0)
            pix.Text = Encoding.Latin1.GetString(comment);
        return pix;
    }

    /// <summary>Reads a JPEG image from memory; any JPEG comment is stored as the text of the image.</summary>
    /// <param name="data">The encoded JPEG data.</param>
    /// <param name="cmflag">0 for no colormap; 1 to return an 8 bpp colormapped image if the data has 3 or 4 samples per pixel.</param>
    /// <param name="reduction">Scaling factor: 1, 2, 4 or 8.</param>
    /// <param name="hint">Bitwise OR of <c>L_JPEG_*</c> values; 0 for the default.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="data"/> is empty, or <paramref name="reduction"/> is not 1, 2, 4 or 8.</exception>
    public static Pix ReadMemJpeg(ReadOnlySpan<byte> data, int cmflag = 0, int reduction = 1, int hint = 0)
        => ReadMemJpeg(data, cmflag, reduction, out _, hint);

    // leptonica: readHeaderMemJpeg (jpegio.c)
    /// <summary>Reads the header information of a JPEG image in memory.</summary>
    /// <param name="data">The encoded JPEG data.</param>
    /// <param name="pw">Receives the image width.</param>
    /// <param name="ph">Receives the image height.</param>
    /// <param name="pspp">Receives the number of samples per pixel: 1, 3 or 4.</param>
    /// <param name="pycck">Receives <see langword="true"/> if the color space is YCCK.</param>
    /// <param name="pcmyk">Receives <see langword="true"/> if the color space is CMYK.</param>
    /// <exception cref="System.ArgumentException"><paramref name="data"/> is empty.</exception>
    public static void ReadHeaderMemJpeg(ReadOnlySpan<byte> data, out int pw, out int ph,
                                         out int pspp, out bool pycck, out bool pcmyk)
    {
        const string func = "readHeaderMemJpeg";
        pw = ph = pspp = 0;
        pycck = pcmyk = false;
        if (data.IsEmpty)
            throw Lept.Arg(func, "data not defined");

        using MemoryStream fp = new MemoryStream(data.ToArray(), false);
        FreadHeaderJpeg(fp, out pw, out ph, out pspp, out pycck, out pcmyk);
    }

    // leptonica: readResolutionMemJpeg (jpegio.c)
    /// <summary>Reads the resolution of a JPEG image in memory from its metadata.</summary>
    /// <param name="data">The encoded JPEG data.</param>
    /// <param name="pxres">Receives the horizontal resolution in ppi, or 0 if it is not specified.</param>
    /// <param name="pyres">Receives the vertical resolution in ppi, or 0 if it is not specified.</param>
    /// <exception cref="System.ArgumentException"><paramref name="data"/> is empty.</exception>
    public static void ReadResolutionMemJpeg(ReadOnlySpan<byte> data, out int pxres, out int pyres)
    {
        const string func = "readResolutionMemJpeg";
        pxres = pyres = 0;
        if (data.IsEmpty)
            throw Lept.Arg(func, "data not defined");

        using MemoryStream fp = new MemoryStream(data.ToArray(), false);
        FgetJpegResolution(fp, out pxres, out pyres);
    }

    // leptonica: pixWriteMemJpeg (jpegio.c)
    /// <summary>Encodes an image in JPEG format.</summary>
    /// <param name="pix">The image to encode.</param>
    /// <param name="quality">Quality from 1 to 100; 75 is the default.</param>
    /// <param name="progressive">0 for baseline sequential encoding; 1 for progressive encoding.</param>
    /// <returns>The JPEG-encoded data.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="pix"/> is <see langword="null"/>.</exception>
    public static byte[] WriteMemJpeg(Pix pix, int quality, int progressive)
    {
        const string func = "pixWriteMemJpeg";
        if (pix == null)
            throw Lept.Arg(func, "&pix not defined");

        using MemoryStream ms = new MemoryStream();
        WriteStreamJpeg(ms, pix, quality, progressive);
        return ms.ToArray();
    }

    /*---------------------------------------------------------------------*
     *           Setting special flag for chroma sampling on write         *
     *---------------------------------------------------------------------*/
    // leptonica: pixSetChromaSampling (jpegio.c)
    /// <param name="sampling">1 for subsampling (default 4:2:0); 0 for no subsampling (4:4:4)</param>
    public void SetChromaSampling(int sampling)
    {
        Special = (sampling != 0) ? 0 : L.L_NO_CHROMA_SAMPLING_JPEG;
    }

    /// <summary>Sets the chroma sampling used when the image is written as JPEG.</summary>
    /// <param name="pix">The image to modify.</param>
    /// <param name="sampling">1 for subsampling (the default 4:2:0); 0 for no subsampling (4:4:4).</param>
    /// <exception cref="System.ArgumentException"><paramref name="pix"/> is <see langword="null"/>.</exception>
    public static void SetChromaSampling(Pix pix, int sampling)
    {
        if (pix == null)
            throw Lept.Arg("pixSetChromaSampling", "pix not defined");
        pix.SetChromaSampling(sampling);
    }
}
