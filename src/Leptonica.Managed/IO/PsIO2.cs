using System;
using System.IO;
using System.Text;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  psio2.c
 *
 *     Lower-level functions that implement a PostScript "device driver"
 *     for wrapping images in PostScript.
 *
 *     For uncompressed images
 *          PsIo.WritePSEmbed (pixWritePSEmbed)
 *          Pix.WriteStreamPS (pixWriteStreamPS)
 *          Pix.WriteStringPS (pixWriteStringPS)
 *          PsIo.GenerateUncompressedPS (generateUncompressedPS)
 *          PsIo.GetScaledParametersPS (getScaledParametersPS)
 *          PsIo.ConvertByteToHexAscii (convertByteToHexAscii)
 *
 *     For jpeg compressed images (use dct compression)
 *          PsIo.ConvertJpegToPSEmbed (convertJpegToPSEmbed)
 *          PsIo.ConvertJpegToPS (convertJpegToPS)
 *          PsIo.ConvertJpegToPSString (convertJpegToPSString)
 *          PsIo.GenerateJpegPS (generateJpegPS)
 *
 *     For g4 fax compressed images (use ccitt g4 compression)
 *          PsIo.ConvertG4ToPSEmbed (convertG4ToPSEmbed)
 *          PsIo.ConvertG4ToPS (convertG4ToPS)
 *          PsIo.ConvertG4ToPSString (convertG4ToPSString)
 *          PsIo.GenerateG4PS (generateG4PS)
 *
 *     For multipage tiff images
 *          PsIo.ConvertTiffMultipageToPS (convertTiffMultipageToPS)
 *
 *     For flate (gzip) compressed images (e.g., png)
 *          PsIo.ConvertFlateToPSEmbed (convertFlateToPSEmbed)
 *          PsIo.ConvertFlateToPS (convertFlateToPS)
 *          PsIo.ConvertFlateToPSString (convertFlateToPSString)
 *          PsIo.GenerateFlatePS (generateFlatePS)
 *
 *     Write to memory
 *          Pix.WriteMemPS (pixWriteMemPS)
 *
 *     Converting resolution
 *          PsIo.GetResLetterPage (getResLetterPage)
 *          PsIo.GetResA4Page (getResA4Page)
 *
 *     Setting flag for writing bounding box hint
 *          PsIo.WriteBoundingBox (l_psWriteBoundingBox)
 */
public sealed partial class Pix
{
    // leptonica: pixWriteStreamPS (psio2.c)
    /// <summary>Writes an image to a stream as uncompressed PostScript.</summary>
    /// <param name="fp">The stream to write.</param>
    /// <param name="pix">The image to write.</param>
    /// <param name="box">The region of the image to write, or <see langword="null"/> for the whole image.</param>
    /// <param name="res">Resolution in ppi; use 0 for the default.</param>
    /// <param name="scale">Scaling factor; use 0.0 for the default.</param>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> or <paramref name="pix"/> is <see langword="null"/>.</exception>
    public static void WriteStreamPS(Stream fp, Pix pix, Box? box, int res, float scale)
    {
        const string func = "pixWriteStreamPS";
        if (fp == null)
            throw Lept.Arg(func, "stream not open");
        if (pix == null)
            throw Lept.Arg(func, "pix not defined");

        Pix pixc = pix.ConvertForPSWrap();
        string outstr = pixc.WriteStringPS(box, res, scale);
        byte[] bytes = Encoding.Latin1.GetBytes(outstr);
        fp.Write(bytes, 0, bytes.Length);
    }

    // leptonica: pixWriteStringPS (psio2.c)
    /// <summary>Generates an uncompressed PostScript string representation of this Pix.</summary>
    public string WriteStringPS(Box? box, int res, float scale)
    {
        Pix pix = ConvertForPSWrap();
        pix.GetDimensions(out int w, out int h, out int d);

        int boxflag = (box == null) ? 0 : 1;
        PsIo.GetScaledParametersPS(box, w, h, res, scale,
                                   out float xpt, out float ypt, out float wpt, out float hpt);

        int bps = (d == 1) ? 1 : 8;
        int wpl = pix.Wpl;
        int psbpl = (d == 1 || d == 8) ? (w * d + 7) / 8 : 3 * w;
        uint[] data = pix.Data;
        int hexbytes = 2 * psbpl * h;
        char[] hexchars = new char[hexbytes];

        if (d == 1 || d == 8) {
            int k = 0;
            for (int i = 0; i < h; i++) {
                ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
                for (int j = 0; j < psbpl; j++) {
                    byte byteval = (byte)GetDataByte(line, j);
                    PsIo.ConvertByteToHexAscii(byteval, out char nib1, out char nib2);
                    hexchars[k++] = nib1;
                    hexchars[k++] = nib2;
                }
            }
        } else {  /* d == 32; hexdata bytes packed RGBRGB..., 2 per sample */
            int k = 0;
            for (int i = 0; i < h; i++) {
                int lineOff = i * wpl;
                for (int j = 0; j < w; j++) {
                    uint word = data[lineOff + j];
                    PsIo.ConvertByteToHexAscii((byte)GetDataByte(word, 0), out char nib1, out char nib2);
                    hexchars[k++] = nib1;
                    hexchars[k++] = nib2;
                    PsIo.ConvertByteToHexAscii((byte)GetDataByte(word, 1), out nib1, out nib2);
                    hexchars[k++] = nib1;
                    hexchars[k++] = nib2;
                    PsIo.ConvertByteToHexAscii((byte)GetDataByte(word, 2), out nib1, out nib2);
                    hexchars[k++] = nib1;
                    hexchars[k++] = nib2;
                }
            }
        }

        string hexdata = new string(hexchars);
        return PsIo.GenerateUncompressedPS(hexdata, w, h, d, psbpl, bps,
                                           xpt, ypt, wpt, hpt, boxflag);
    }

    // leptonica: pixWriteMemPS (psio2.c)
    /// <summary>Encodes an image as uncompressed PostScript.</summary>
    /// <param name="pix">The image to encode.</param>
    /// <param name="box">The region of the image to write, or <see langword="null"/> for the whole image.</param>
    /// <param name="res">Resolution in ppi; use 0 for the default.</param>
    /// <param name="scale">Scaling factor; use 0.0 for the default.</param>
    /// <returns>The PostScript data.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="pix"/> is <see langword="null"/>.</exception>
    public static byte[] WriteMemPS(Pix pix, Box? box, int res, float scale)
    {
        const string func = "pixWriteMemPS";
        if (pix == null)
            throw Lept.Arg(func, "&pix not defined");

        string str = pix.WriteStringPS(box, res, scale);
        return Encoding.Latin1.GetBytes(str);
    }
}

public static partial class PsIo
{
    private const int DefaultInputRes = 300;
    private const int MinRes = 5;
    private const int MaxRes = 3000;
    private const int LetterWidth = 612;
    private const int LetterHeight = 792;
    private const int A4Width = 595;
    private const int A4Height = 842;
    private const float DefaultFillFraction = 0.95f;

        /* Thread-local inverted flag so default on every thread is 1 (true) */
    [ThreadStatic]
    private static bool omitBoundingBox;

    internal static bool ShouldWriteBoundingBox => !omitBoundingBox;

    /*-------------------------------------------------------------*
     *                  For uncompressed images                    *
     *-------------------------------------------------------------*/
    // leptonica: pixWritePSEmbed (psio2.c)
    /// <summary>Generates an uncompressed PostScript file with a bounding box from an image file.</summary>
    /// <param name="filein">The input image file; any depth is accepted, and a colormap is removed.</param>
    /// <param name="fileout">Path of the PostScript file to write.</param>
    /// <remarks>The bounding box is required when a program such as TeX places and rescales the image. It is sized for fitting the image to an 8.5 by 11 inch page.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="filein"/> or <paramref name="fileout"/> is <see langword="null"/>.</exception>
    public static void WritePSEmbed(string filein, string fileout)
    {
        const string func = "pixWritePSEmbed";
        if (filein == null)
            throw Lept.Arg(func, "filein not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");

        Pix pix = Pix.Read(filein);
        int w = pix.Width;
        int h = pix.Height;
        float scale = (w * 11.0 > h * 8.5)
            ? 8.5f * 300.0f / (float)w
            : 11.0f * 300.0f / (float)h;

        using FileStream fp = Utils.FopenWriteStream(fileout, "wb");
        Pix.WriteStreamPS(fp, pix, null, 0, scale);
    }

    // leptonica: generateUncompressedPS (psio2.c)
    /// <summary>Generates the PostScript string for an uncompressed image from its hexadecimal raster data (a low-level function).</summary>
    /// <param name="hexdata">The raster data as a hexadecimal string.</param>
    /// <param name="w">Width of the raster image in pixels.</param>
    /// <param name="h">Height of the raster image in pixels.</param>
    /// <param name="d">Image depth in bits per pixel; RGB is 32.</param>
    /// <param name="psbpl">Raster bytes per line, when packed to the byte boundary.</param>
    /// <param name="bps">Bits per sample: 1 or 8.</param>
    /// <param name="xpt">Horizontal location of the lower-left corner of the image, in points, relative to the PostScript origin at the lower-left corner of the page.</param>
    /// <param name="ypt">Vertical location of the lower-left corner of the image, in points, relative to the PostScript origin at the lower-left corner of the page.</param>
    /// <param name="wpt">Rendered image width in points.</param>
    /// <param name="hpt">Rendered image height in points.</param>
    /// <param name="boxflag">1 to write the bounding box hint; 0 to skip it.</param>
    /// <returns>The PostScript string.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="hexdata"/> is <see langword="null"/>.</exception>
    public static string GenerateUncompressedPS(string hexdata, int w, int h, int d,
                                                int psbpl, int bps, float xpt, float ypt,
                                                float wpt, float hpt, int boxflag)
    {
        const string func = "generateUncompressedPS";
        if (hexdata == null)
            throw Lept.Arg(func, "hexdata not defined");

        Sarray sa = Sarray.Create(0);
        sa.AddString("%!Adobe-PS", L.L_COPY);
        if (boxflag == 0) {
            sa.AddString(CFormat.Sprintf("%%%%BoundingBox: %7.2f %7.2f %7.2f %7.2f",
                                         xpt, ypt, xpt + wpt, ypt + hpt), L.L_COPY);
        } else {
            sa.AddString("gsave", L.L_COPY);
        }

        if (d == 1)
            sa.AddString("{1 exch sub} settransfer    %invert binary", L.L_COPY);

        sa.AddString(CFormat.Sprintf("/bpl %d string def         %%bpl as a string", psbpl), L.L_COPY);
        sa.AddString(CFormat.Sprintf("%7.2f %7.2f translate         %%set image origin in pts", xpt, ypt), L.L_COPY);
        sa.AddString(CFormat.Sprintf("%7.2f %7.2f scale             %%set image size in pts", wpt, hpt), L.L_COPY);
        sa.AddString(CFormat.Sprintf("%d %d %d                 %%image dimensions in pixels", w, h, bps), L.L_COPY);
        sa.AddString(CFormat.Sprintf("[%d %d %d %d %d %d]     %%mapping matrix: [w 0 0 -h 0 h]",
                                     w, 0, 0, -h, 0, h), L.L_COPY);

        if (boxflag == 0) {
            if (d == 1 || d == 8)
                sa.AddString("{currentfile bpl readhexstring pop} image", L.L_COPY);
            else
                sa.AddString("{currentfile bpl readhexstring pop} false 3 colorimage", L.L_COPY);
        } else {
            if (d == 1 || d == 8)
                sa.AddString("{currentfile bpl readhexstring pop} bind image", L.L_COPY);
            else
                sa.AddString("{currentfile bpl readhexstring pop} bind false 3 colorimage", L.L_COPY);
        }

        sa.AddString(hexdata, L.L_INSERT);

        if (boxflag == 0)
            sa.AddString("\nshowpage", L.L_COPY);
        else
            sa.AddString("\ngrestore", L.L_COPY);

        return sa.ToString(1);
    }

    // leptonica: getScaledParametersPS (psio2.c)
    internal static void GetScaledParametersPS(Box? box, int wpix, int hpix, int res, float scale,
                                               out float pxpt, out float pypt,
                                               out float pwpt, out float phpt)
    {
        if (res == 0)
            res = DefaultInputRes;
        float fres = (float)res;

        if (scale == 0.0f)
            scale = 1.0f;
        if (scale != 1.0f) {
            fres = (float)res / scale;
            res = (int)fres;
        }

        if (res < MinRes || res > MaxRes) {
            Log.Warning("getScaledParametersPS",
                        $"res {res} out of bounds; using default res; no scaling");
            res = DefaultInputRes;
            fres = (float)res;
        }

        float winch, hinch, xinch, yinch;
        if (box == null) {
            winch = (float)wpix / fres;
            hinch = (float)hpix / fres;
            xinch = (8.5f - winch) / 2.0f;
            yinch = (11.0f - hinch) / 2.0f;
        } else {
            box.GetGeometry(out int bx, out int by, out int bw, out int bh);
            winch = (bw == 0) ? (float)wpix / fres : (float)bw / 1000.0f;
            hinch = (bh == 0) ? (float)hpix / fres : (float)bh / 1000.0f;
            xinch = (float)bx / 1000.0f;
            yinch = (float)by / 1000.0f;
        }

        if (xinch < 0.0f)
            Log.Warning("getScaledParametersPS", "left edge < 0.0 inch");
        if (xinch + winch > 8.5f)
            Log.Warning("getScaledParametersPS", "right edge > 8.5 inch");
        if (yinch < 0.0f)
            Log.Warning("getScaledParametersPS", "bottom edge < 0.0 inch");
        if (yinch + hinch > 11.0f)
            Log.Warning("getScaledParametersPS", "top edge > 11.0 inch");

        pwpt = 72.0f * winch;
        phpt = 72.0f * hinch;
        pxpt = 72.0f * xinch;
        pypt = 72.0f * yinch;
    }

    // leptonica: convertByteToHexAscii (psio2.c)
    internal static void ConvertByteToHexAscii(byte byteval, out char pnib1, out char pnib2)
    {
        int nib = byteval >> 4;
        pnib1 = (nib < 10) ? (char)('0' + nib) : (char)('a' + (nib - 10));
        nib = byteval & 0xf;
        pnib2 = (nib < 10) ? (char)('0' + nib) : (char)('a' + (nib - 10));
    }

    /*-------------------------------------------------------------*
     *                  For jpeg compressed images                 *
     *-------------------------------------------------------------*/
    // leptonica: convertJpegToPSEmbed (psio2.c)
    /// <summary>Converts a JPEG file to a DCT-compressed, ASCII85-encoded PostScript file with a bounding box.</summary>
    /// <param name="filein">The input JPEG file.</param>
    /// <param name="fileout">Path of the PostScript file to write.</param>
    /// <remarks>The bounding box is required when a program such as TeX places and rescales the image. It is sized for fitting the image to an 8.5 by 11 inch page.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="filein"/> or <paramref name="fileout"/> is <see langword="null"/>.</exception>
    public static void ConvertJpegToPSEmbed(string filein, string fileout)
    {
        const string func = "convertJpegToPSEmbed";
        if (filein == null)
            throw Lept.Arg(func, "filein not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");

        L_Compressed_Data cid = PdfIo.GenerateJpegData(filein, 1);
        int w = cid.W;
        int h = cid.H;

        float xpt = 20.0f, ypt = 20.0f, wpt, hpt;
        if (w * 11.0 > h * 8.5) {
            wpt = 572.0f;
            hpt = wpt * (float)h / (float)w;
        } else {
            hpt = 752.0f;
            wpt = hpt * (float)w / (float)h;
        }

        string outstr = GenerateJpegPS(null, cid, xpt, ypt, wpt, hpt, 1, 1);
        PdfIo.CIDataDestroy(ref cid!);
        Utils.BinaryWrite(fileout, "w", Encoding.Latin1.GetBytes(outstr));
    }

    // leptonica: convertJpegToPS (psio2.c)
    /// <summary>Converts a JPEG file to level 2 PostScript, DCT-compressed and ASCII85-encoded, and places it on a page.</summary>
    /// <param name="filein">The input JPEG file.</param>
    /// <param name="fileout">Path of the PostScript file to write.</param>
    /// <param name="operation"><c>"w"</c> to write and clear the file, which is used for the first image; <c>"a"</c> to append, which is used for all subsequent images.</param>
    /// <param name="x">Horizontal location of the lower-left corner of the image, in pixels, relative to the PostScript origin at the lower-left corner of the page.</param>
    /// <param name="y">Vertical location of the lower-left corner of the image, in pixels, relative to the PostScript origin at the lower-left corner of the page.</param>
    /// <param name="res">Resolution of the input image in ppi; use 0 for the default.</param>
    /// <param name="scale">Scaling by the printer; use 0.0 or 1.0 for no scaling.</param>
    /// <param name="pageno">Page number, starting at 1; 0 can be used if there is only one page.</param>
    /// <param name="endpage"><see langword="true"/> if this is the last image added to the page; otherwise <see langword="false"/>.</param>
    /// <remarks>
    /// <para>A file can contain multiple pages, each with multiple images; the arguments control the placement of the images on the pages.
    /// To render several images on the same page, pass <paramref name="endpage"/> as <see langword="false"/> for each image until the last,
    /// for which it is <see langword="true"/>; this emits the <c>showpage</c> command, which outputs the page and clears the raster buffer.</para>
    /// <para>The position (<paramref name="x"/>, <paramref name="y"/>) is in pixels when <paramref name="scale"/> is 1.0. With a scale of 2.0 the image
    /// is placed at (2x, 2y) and its dimensions are doubled.</para>
    /// <para>The geometry is given to the printer in points (1/72 inch), so the rendered size does not depend on the printer's own resolution.
    /// For example, an image created at 300 ppi prints at the size it has on a 75 ppi display if <paramref name="scale"/> is 4.0 or <paramref name="res"/> is 75.</para>
    /// </remarks>
    /// <exception cref="System.ArgumentException">A required argument is <see langword="null"/>, or <paramref name="operation"/> is neither <c>"w"</c> nor <c>"a"</c>.</exception>
    public static void ConvertJpegToPS(string filein, string fileout, string operation,
                                       int x, int y, int res, float scale, int pageno, bool endpage)
    {
        const string func = "convertJpegToPS";
        if (filein == null)
            throw Lept.Arg(func, "filein not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");
        if (operation != "w" && operation != "a")
            throw Lept.Arg(func, "operation must be \"w\" or \"a\"");

        string outstr = ConvertJpegToPSString(filein, x, y, res, scale, pageno, endpage ? 1 : 0);
        Utils.BinaryWrite(fileout, operation, Encoding.Latin1.GetBytes(outstr));
    }

    // leptonica: convertJpegToPSString (psio2.c)
    private static string ConvertJpegToPSString(string filein, int x, int y, int res,
                                                float scale, int pageno, int endpage)
    {
        const string func = "convertJpegToPSString";
        if (filein == null)
            throw Lept.Arg(func, "filein not defined");

        L_Compressed_Data cid = PdfIo.GenerateJpegData(filein, 1);
        if (scale == 0.0f)
            scale = 1.0f;
        if (res <= 0)
            res = (cid.Res > 0) ? cid.Res : DefaultInputRes;

        float xpt = scale * x * 72.0f / res;
        float ypt = scale * y * 72.0f / res;
        float wpt = scale * cid.W * 72.0f / res;
        float hpt = scale * cid.H * 72.0f / res;
        if (pageno == 0)
            pageno = 1;

        string outstr = GenerateJpegPS(null, cid, xpt, ypt, wpt, hpt, pageno, endpage);
        PdfIo.CIDataDestroy(ref cid!);
        return outstr;
    }

    // leptonica: generateJpegPS (psio2.c)
    private static string GenerateJpegPS(string? filein, L_Compressed_Data cid,
                                         float xpt, float ypt, float wpt, float hpt,
                                         int pageno, int endpage)
    {
        const string func = "generateJpegPS";
        if (cid == null)
            throw Lept.Arg(func, "jpeg data not defined");
        int w = cid.W;
        int h = cid.H;
        int bps = cid.Bps;
        int spp = cid.Spp;

        Sarray sa = Sarray.Create(50);
        sa.AddString("%!PS-Adobe-3.0", L.L_COPY);
        sa.AddString("%%Creator: leptonica", L.L_COPY);
        sa.AddString(filein != null ? $"%%Title: {filein}" : "%%Title: Jpeg compressed PS", L.L_COPY);
        sa.AddString("%%DocumentData: Clean7Bit", L.L_COPY);

        if (ShouldWriteBoundingBox) {
            sa.AddString(CFormat.Sprintf("%%%%BoundingBox: %7.2f %7.2f %7.2f %7.2f",
                                         xpt, ypt, xpt + wpt, ypt + hpt), L.L_COPY);
        }

        sa.AddString("%%LanguageLevel: 2", L.L_COPY);
        sa.AddString("%%EndComments", L.L_COPY);
        sa.AddString(CFormat.Sprintf("%%%%Page: %d %d", pageno, pageno), L.L_COPY);

        sa.AddString("save", L.L_COPY);
        sa.AddString("/RawData currentfile /ASCII85Decode filter def", L.L_COPY);
        sa.AddString("/Data RawData << >> /DCTDecode filter def", L.L_COPY);

        sa.AddString(CFormat.Sprintf("%7.2f %7.2f translate         %%set image origin in pts", xpt, ypt), L.L_COPY);
        sa.AddString(CFormat.Sprintf("%7.2f %7.2f scale             %%set image size in pts", wpt, hpt), L.L_COPY);

        if (spp == 1)
            sa.AddString("/DeviceGray setcolorspace", L.L_COPY);
        else if (spp == 3)
            sa.AddString("/DeviceRGB setcolorspace", L.L_COPY);
        else
            sa.AddString("/DeviceCMYK setcolorspace", L.L_COPY);

        sa.AddString("{ << /ImageType 1", L.L_COPY);
        sa.AddString(CFormat.Sprintf("     /Width %d", w), L.L_COPY);
        sa.AddString(CFormat.Sprintf("     /Height %d", h), L.L_COPY);
        sa.AddString(CFormat.Sprintf("     /ImageMatrix [ %d 0 0 %d 0 %d ]", w, -h, h), L.L_COPY);
        sa.AddString("     /DataSource Data", L.L_COPY);
        sa.AddString(CFormat.Sprintf("     /BitsPerComponent %d", bps), L.L_COPY);

        if (spp == 1)
            sa.AddString("     /Decode [0 1]", L.L_COPY);
        else if (spp == 3)
            sa.AddString("     /Decode [0 1 0 1 0 1]", L.L_COPY);
        else
            sa.AddString("     /Decode [0 1 0 1 0 1 0 1]", L.L_COPY);

        sa.AddString("  >> image", L.L_COPY);
        sa.AddString("  Data closefile", L.L_COPY);
        sa.AddString("  RawData flushfile", L.L_COPY);
        if (endpage != 0)
            sa.AddString("  showpage", L.L_COPY);
        sa.AddString("  restore", L.L_COPY);
        sa.AddString("} exec", L.L_COPY);

        sa.AddString(cid.Data85!, L.L_INSERT);
        cid.Data85 = null;

        return sa.ToString(1);
    }

    /*-------------------------------------------------------------*
     *                  For ccitt g4 compressed images             *
     *-------------------------------------------------------------*/
    // leptonica: convertG4ToPSEmbed (psio2.c)
    /// <summary>Converts a G4 (CCITT Group 4) TIFF file to a compressed, ASCII85-encoded PostScript file with a bounding box.</summary>
    /// <param name="filein">The input TIFF file, which must be G4 compressed.</param>
    /// <param name="fileout">Path of the PostScript file to write.</param>
    /// <remarks>The bounding box is required when a program such as TeX places and rescales the image. It is sized for fitting the image to an 8.5 by 11 inch page.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="filein"/> or <paramref name="fileout"/> is <see langword="null"/>.</exception>
    public static void ConvertG4ToPSEmbed(string filein, string fileout)
    {
        const string func = "convertG4ToPSEmbed";
        if (filein == null)
            throw Lept.Arg(func, "filein not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");

        L_Compressed_Data cid = PdfIo.GenerateG4Data(filein, 1);
        int w = cid.W;
        int h = cid.H;

        float xpt = 20.0f, ypt = 20.0f, wpt, hpt;
        if (w * 11.0 > h * 8.5) {
            wpt = 572.0f;
            hpt = wpt * (float)h / (float)w;
        } else {
            hpt = 752.0f;
            wpt = hpt * (float)w / (float)h;
        }

        string outstr = GenerateG4PS(null, cid, xpt, ypt, wpt, hpt, 1, 1, 1);
        PdfIo.CIDataDestroy(ref cid!);
        Utils.BinaryWrite(fileout, "w", Encoding.Latin1.GetBytes(outstr));
    }

    // leptonica: convertG4ToPS (psio2.c)
    /// <summary>Converts a G4 (CCITT Group 4) TIFF file to level 2 PostScript, ASCII85-encoded, and places it on a page.</summary>
    /// <param name="filein">The input TIFF file, which must be G4 compressed.</param>
    /// <param name="fileout">Path of the PostScript file to write.</param>
    /// <param name="operation"><c>"w"</c> to write and clear the file, which is used for the first image; <c>"a"</c> to append, which is used for all subsequent images.</param>
    /// <param name="x">Horizontal location of the lower-left corner of the image, in pixels, relative to the PostScript origin at the lower-left corner of the page.</param>
    /// <param name="y">Vertical location of the lower-left corner of the image, in pixels, relative to the PostScript origin at the lower-left corner of the page.</param>
    /// <param name="res">Resolution of the input image in ppi, typically 300 or 600; use 0 to determine it automatically from the image size.</param>
    /// <param name="scale">Scaling by the printer; use 0.0 or 1.0 for no scaling.</param>
    /// <param name="pageno">Page number, starting at 1; 0 can be used if there is only one page.</param>
    /// <param name="maskflag"><see langword="true"/> to paint only through the foreground; <see langword="false"/> to paint both foreground and background.</param>
    /// <param name="endpage"><see langword="true"/> if this is the last image added to the page; otherwise <see langword="false"/>.</param>
    /// <remarks>
    /// <para>See <see cref="ConvertJpegToPS"/> for how the placement parameters work. The generated PostScript is about 5/4 the size of the G4 data because of the ASCII85
    /// encoding; when it is converted to PDF the ASCII85 decoder is invoked automatically, so the wrapped G4 data is essentially the same size as the original.
    /// The ASCII85 encoding is useful because many printers do not accept binary PostScript.</para>
    /// </remarks>
    /// <exception cref="System.ArgumentException">A required argument is <see langword="null"/>, or <paramref name="operation"/> is neither <c>"w"</c> nor <c>"a"</c>.</exception>
    public static void ConvertG4ToPS(string filein, string fileout, string operation,
                                     int x, int y, int res, float scale, int pageno,
                                     bool maskflag, bool endpage)
    {
        const string func = "convertG4ToPS";
        if (filein == null)
            throw Lept.Arg(func, "filein not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");
        if (operation != "w" && operation != "a")
            throw Lept.Arg(func, "operation must be \"w\" or \"a\"");

        string outstr = ConvertG4ToPSString(filein, x, y, res, scale, pageno,
                                            maskflag ? 1 : 0, endpage ? 1 : 0);
        Utils.BinaryWrite(fileout, operation, Encoding.Latin1.GetBytes(outstr));
    }

    // leptonica: convertG4ToPSString (psio2.c)
    private static string ConvertG4ToPSString(string filein, int x, int y, int res,
                                              float scale, int pageno, int maskflag, int endpage)
    {
        const string func = "convertG4ToPSString";
        if (filein == null)
            throw Lept.Arg(func, "filein not defined");

        L_Compressed_Data cid = PdfIo.GenerateG4Data(filein, 1);
        if (scale == 0.0f)
            scale = 1.0f;
        if (res <= 0) {
            if (cid.Res > 0)
                res = cid.Res;
            else
                res = (cid.H <= 3509) ? 300 : 600;
        }
        float xpt = scale * x * 72.0f / res;
        float ypt = scale * y * 72.0f / res;
        float wpt = scale * cid.W * 72.0f / res;
        float hpt = scale * cid.H * 72.0f / res;
        if (pageno == 0)
            pageno = 1;

        string outstr = GenerateG4PS(null, cid, xpt, ypt, wpt, hpt, maskflag, pageno, endpage);
        PdfIo.CIDataDestroy(ref cid!);
        return outstr;
    }

    // leptonica: generateG4PS (psio2.c)
    private static string GenerateG4PS(string? filein, L_Compressed_Data cid,
                                       float xpt, float ypt, float wpt, float hpt,
                                       int maskflag, int pageno, int endpage)
    {
        const string func = "generateG4PS";
        if (cid == null)
            throw Lept.Arg(func, "g4 data not defined");
        int w = cid.W;
        int h = cid.H;

        Sarray sa = Sarray.Create(50);
        sa.AddString("%!PS-Adobe-3.0", L.L_COPY);
        sa.AddString("%%Creator: leptonica", L.L_COPY);
        sa.AddString(filein != null ? $"%%Title: {filein}" : "%%Title: G4 compressed PS", L.L_COPY);
        sa.AddString("%%DocumentData: Clean7Bit", L.L_COPY);

        if (ShouldWriteBoundingBox) {
            sa.AddString(CFormat.Sprintf("%%%%BoundingBox: %7.2f %7.2f %7.2f %7.2f",
                                         xpt, ypt, xpt + wpt, ypt + hpt), L.L_COPY);
        }

        sa.AddString("%%LanguageLevel: 2", L.L_COPY);
        sa.AddString("%%EndComments", L.L_COPY);
        sa.AddString(CFormat.Sprintf("%%%%Page: %d %d", pageno, pageno), L.L_COPY);

        sa.AddString("save", L.L_COPY);
        sa.AddString("100 dict begin", L.L_COPY);
        sa.AddString(CFormat.Sprintf("%7.2f %7.2f translate         %%set image origin in pts", xpt, ypt), L.L_COPY);
        sa.AddString(CFormat.Sprintf("%7.2f %7.2f scale             %%set image size in pts", wpt, hpt), L.L_COPY);
        sa.AddString("/DeviceGray setcolorspace", L.L_COPY);

        sa.AddString("{", L.L_COPY);
        sa.AddString("  /RawData currentfile /ASCII85Decode filter def", L.L_COPY);
        sa.AddString("  << ", L.L_COPY);
        sa.AddString("    /ImageType 1", L.L_COPY);
        sa.AddString(CFormat.Sprintf("    /Width %d", w), L.L_COPY);
        sa.AddString(CFormat.Sprintf("    /Height %d", h), L.L_COPY);
        sa.AddString(CFormat.Sprintf("    /ImageMatrix [ %d 0 0 %d 0 %d ]", w, -h, h), L.L_COPY);
        sa.AddString("    /BitsPerComponent 1", L.L_COPY);
        sa.AddString("    /Interpolate true", L.L_COPY);
        sa.AddString(cid.MinisBlack != 0 ? "    /Decode [1 0]" : "    /Decode [0 1]", L.L_COPY);
        sa.AddString("    /DataSource RawData", L.L_COPY);
        sa.AddString("        <<", L.L_COPY);
        sa.AddString("          /K -1", L.L_COPY);
        sa.AddString(CFormat.Sprintf("          /Columns %d", w), L.L_COPY);
        sa.AddString(CFormat.Sprintf("          /Rows %d", h), L.L_COPY);
        sa.AddString("        >> /CCITTFaxDecode filter", L.L_COPY);
        sa.AddString(maskflag != 0 ? "  >> imagemask" : "  >> image", L.L_COPY);
        sa.AddString("  RawData flushfile", L.L_COPY);
        if (endpage != 0)
            sa.AddString("  showpage", L.L_COPY);
        sa.AddString("}", L.L_COPY);

        sa.AddString("%%BeginData:", L.L_COPY);
        sa.AddString("exec", L.L_COPY);
        sa.AddString(cid.Data85!, L.L_INSERT);
        sa.AddString("%%EndData", L.L_COPY);
        sa.AddString("end", L.L_COPY);
        sa.AddString("restore", L.L_COPY);

        string outstr = sa.ToString(1);
        cid.Data85 = null;
        return outstr;
    }

    /*-------------------------------------------------------------*
     *                     For tiff multipage files                *
     *-------------------------------------------------------------*/
    // leptonica: convertTiffMultipageToPS (psio2.c)
    /// <summary>Converts a multipage TIFF file of binary page images to a G4-compressed PostScript file.</summary>
    /// <param name="filein">The input multipage TIFF file.</param>
    /// <param name="fileout">Path of the PostScript file to write.</param>
    /// <param name="fillfract">Factor for filling an 8.5 by 11 inch page; use 0.0 for the default.</param>
    /// <remarks>If the images come from a standard-resolution fax, the vertical resolution is doubled to give a normal-looking aspect ratio.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="filein"/> or <paramref name="fileout"/> is <see langword="null"/>.</exception>
    public static void ConvertTiffMultipageToPS(string filein, string fileout, float fillfract)
    {
        const string func = "convertTiffMultipageToPS";
        if (filein == null)
            throw Lept.Arg(func, "filein not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");

        int npages;
        using (FileStream fp = Utils.FopenReadStream(filein)) {
            if (!Pix.FileFormatIsTiff(fp))
                throw Lept.Fail(func, $"file not tiff format: {filein}");
            Pix.TiffGetCount(fp, out npages);
        }

        if (fillfract == 0.0f)
            fillfract = DefaultFillFraction;

        for (int i = 0; i < npages; i++) {
            Pix pix = Pix.ReadTiff(filein, i) ?? throw Lept.Fail(func, $"pix not made: {filein}");
            pix.GetDimensions(out int w, out int h, out _);
            Pix pixs = (w == 1728 && h < w) ? pix.Scale(1.0f, 2.0f) : Pix.Clone(pix);

            string tempfile = Utils.LMakeTempFilename();
            try {
                Pix.Write(tempfile, pixs, L.IFF_TIFF_G4);
                float scale = Math.Min(fillfract * 2550.0f / w, fillfract * 3300.0f / h);
                ConvertG4ToPS(tempfile, fileout, i == 0 ? "w" : "a", 0, 0, 300, scale,
                              i + 1, false, true);
            } finally {
                Utils.LeptRmfile(tempfile);
            }
        }
    }

    /*---------------------------------------------------------------------*
     *            For flate (gzip) compressed images (e.g., png)           *
     *---------------------------------------------------------------------*/
    // leptonica: convertFlateToPSEmbed (psio2.c)
    /// <summary>Converts an image file of any format to a Flate-compressed, ASCII85-encoded PostScript file with a bounding box.</summary>
    /// <param name="filein">The input image file, in any format.</param>
    /// <param name="fileout">Path of the PostScript file to write.</param>
    /// <remarks>The bounding box is required when a program such as TeX places and rescales the image. It is sized for fitting the image to an 8.5 by 11 inch page.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="filein"/> or <paramref name="fileout"/> is <see langword="null"/>.</exception>
    public static void ConvertFlateToPSEmbed(string filein, string fileout)
    {
        const string func = "convertFlateToPSEmbed";
        if (filein == null)
            throw Lept.Arg(func, "filein not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");

        L_Compressed_Data cid = PdfIo.GenerateFlateData(filein, 1);
        int w = cid.W;
        int h = cid.H;

        float xpt = 20.0f, ypt = 20.0f, wpt, hpt;
        if (w * 11.0 > h * 8.5) {
            wpt = 572.0f;
            hpt = wpt * (float)h / (float)w;
        } else {
            hpt = 752.0f;
            wpt = hpt * (float)w / (float)h;
        }

        string outstr = GenerateFlatePS(null, cid, xpt, ypt, wpt, hpt, 1, 1);
        PdfIo.CIDataDestroy(ref cid!);
        Utils.BinaryWrite(fileout, "w", Encoding.Latin1.GetBytes(outstr));
    }

    // leptonica: convertFlateToPS (psio2.c)
    /// <summary>Converts an image file of any format to level 3 PostScript, Flate-compressed and ASCII85-encoded, and places it on a page.</summary>
    /// <param name="filein">The input image file, in any format.</param>
    /// <param name="fileout">Path of the PostScript file to write.</param>
    /// <param name="operation"><c>"w"</c> to write and clear the file, which is used for the first image; <c>"a"</c> to append, which is used for all subsequent images.</param>
    /// <param name="x">Horizontal location of the lower-left corner of the image, in pixels, relative to the PostScript origin at the lower-left corner of the page.</param>
    /// <param name="y">Vertical location of the lower-left corner of the image, in pixels, relative to the PostScript origin at the lower-left corner of the page.</param>
    /// <param name="res">Resolution of the input image in ppi; use 0 for the default.</param>
    /// <param name="scale">Scaling by the printer; use 0.0 or 1.0 for no scaling.</param>
    /// <param name="pageno">Page number, starting at 1; 0 can be used if there is only one page.</param>
    /// <param name="endpage"><see langword="true"/> if this is the last image added to the page; otherwise <see langword="false"/>.</param>
    /// <remarks>See <see cref="ConvertJpegToPS"/> for how the placement parameters work.</remarks>
    /// <exception cref="System.ArgumentException">A required argument is <see langword="null"/>, or <paramref name="operation"/> is neither <c>"w"</c> nor <c>"a"</c>.</exception>
    public static void ConvertFlateToPS(string filein, string fileout, string operation,
                                        int x, int y, int res, float scale, int pageno, bool endpage)
    {
        const string func = "convertFlateToPS";
        if (filein == null)
            throw Lept.Arg(func, "filein not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");
        if (operation != "w" && operation != "a")
            throw Lept.Arg(func, "operation must be \"w\" or \"a\"");

        string outstr = ConvertFlateToPSString(filein, x, y, res, scale, pageno, endpage ? 1 : 0);
        Utils.BinaryWrite(fileout, operation, Encoding.Latin1.GetBytes(outstr));
    }

    // leptonica: convertFlateToPSString (psio2.c)
    private static string ConvertFlateToPSString(string filein, int x, int y, int res,
                                                 float scale, int pageno, int endpage)
    {
        const string func = "convertFlateToPSString";
        if (filein == null)
            throw Lept.Arg(func, "filein not defined");

        L_Compressed_Data cid = PdfIo.GenerateFlateData(filein, 1);
        if (scale == 0.0f)
            scale = 1.0f;
        if (res <= 0)
            res = (cid.Res > 0) ? cid.Res : DefaultInputRes;

        float xpt = scale * x * 72.0f / res;
        float ypt = scale * y * 72.0f / res;
        float wpt = scale * cid.W * 72.0f / res;
        float hpt = scale * cid.H * 72.0f / res;
        if (pageno == 0)
            pageno = 1;

        string outstr = GenerateFlatePS(null, cid, xpt, ypt, wpt, hpt, pageno, endpage);
        PdfIo.CIDataDestroy(ref cid!);
        return outstr;
    }

    // leptonica: generateFlatePS (psio2.c)
    private static string GenerateFlatePS(string? filein, L_Compressed_Data cid,
                                          float xpt, float ypt, float wpt, float hpt,
                                          int pageno, int endpage)
    {
        const string func = "generateFlatePS";
        if (cid == null)
            throw Lept.Arg(func, "flate data not defined");
        int w = cid.W;
        int h = cid.H;
        int bps = cid.Bps;
        int spp = cid.Spp;

        Sarray sa = Sarray.Create(50);
        sa.AddString("%!PS-Adobe-3.0 EPSF-3.0", L.L_COPY);
        sa.AddString("%%Creator: leptonica", L.L_COPY);
        sa.AddString(filein != null ? $"%%Title: {filein}" : "%%Title: Flate compressed PS", L.L_COPY);
        sa.AddString("%%DocumentData: Clean7Bit", L.L_COPY);

        if (ShouldWriteBoundingBox) {
            sa.AddString(CFormat.Sprintf("%%%%BoundingBox: %7.2f %7.2f %7.2f %7.2f",
                                         xpt, ypt, xpt + wpt, ypt + hpt), L.L_COPY);
        }

        sa.AddString("%%LanguageLevel: 3", L.L_COPY);
        sa.AddString("%%EndComments", L.L_COPY);
        sa.AddString(CFormat.Sprintf("%%%%Page: %d %d", pageno, pageno), L.L_COPY);

        sa.AddString("save", L.L_COPY);
        sa.AddString(CFormat.Sprintf("%7.2f %7.2f translate         %%set image origin in pts", xpt, ypt), L.L_COPY);
        sa.AddString(CFormat.Sprintf("%7.2f %7.2f scale             %%set image size in pts", wpt, hpt), L.L_COPY);

        if (cid.CmapData85 != null) {
            sa.AddString(CFormat.Sprintf("[ /Indexed /DeviceRGB %d          %%set colormap type/size",
                                         cid.Ncolors - 1), L.L_COPY);
            sa.AddString("  <~", L.L_COPY);
            sa.AddString(cid.CmapData85, L.L_INSERT);
            sa.AddString("  ] setcolorspace", L.L_COPY);
        } else if (spp == 1) {
            sa.AddString("/DeviceGray setcolorspace", L.L_COPY);
        } else {
            sa.AddString("/DeviceRGB setcolorspace", L.L_COPY);
        }

        sa.AddString("/RawData currentfile /ASCII85Decode filter def", L.L_COPY);
        sa.AddString("/Data RawData << >> /FlateDecode filter def", L.L_COPY);

        sa.AddString("{ << /ImageType 1", L.L_COPY);
        sa.AddString(CFormat.Sprintf("     /Width %d", w), L.L_COPY);
        sa.AddString(CFormat.Sprintf("     /Height %d", h), L.L_COPY);
        sa.AddString(CFormat.Sprintf("     /BitsPerComponent %d", bps), L.L_COPY);
        sa.AddString(CFormat.Sprintf("     /ImageMatrix [ %d 0 0 %d 0 %d ]", w, -h, h), L.L_COPY);

        if (cid.CmapData85 != null) {
            sa.AddString("     /Decode [0 255]", L.L_COPY);
        } else if (spp == 1) {
            sa.AddString(bps == 1 ? "     /Decode [1 0]" : "     /Decode [0 1]", L.L_COPY);
        } else {
            sa.AddString("     /Decode [0 1 0 1 0 1]", L.L_COPY);
        }

        sa.AddString("     /DataSource Data", L.L_COPY);
        sa.AddString("  >> image", L.L_COPY);
        sa.AddString("  Data closefile", L.L_COPY);
        sa.AddString("  RawData flushfile", L.L_COPY);
        if (endpage != 0)
            sa.AddString("  showpage", L.L_COPY);
        sa.AddString("  restore", L.L_COPY);
        sa.AddString("} exec", L.L_COPY);

        sa.AddString(cid.Data85!, L.L_INSERT);

        string outstr = sa.ToString(1);
        cid.CmapData85 = null;
        cid.Data85 = null;
        return outstr;
    }

    /*-------------------------------------------------------------*
     *                    Converting resolution                    *
     *-------------------------------------------------------------*/
    // leptonica: getResLetterPage (psio2.c)
    /// <summary>Computes the resolution at which an image fits a letter-size page.</summary>
    /// <param name="w">Image width in pixels.</param>
    /// <param name="h">Image height in pixels.</param>
    /// <param name="fillfract">The fraction of the page, in each linear dimension, that must not be exceeded; use 0 for the default.</param>
    /// <returns>The resolution in ppi.</returns>
    public static int GetResLetterPage(int w, int h, float fillfract)
    {
        if (fillfract == 0.0f)
            fillfract = DefaultFillFraction;
        int resw = (int)((w * 72.0) / (LetterWidth * fillfract));
        int resh = (int)((h * 72.0) / (LetterHeight * fillfract));
        return Math.Max(resw, resh);
    }

    // leptonica: getResA4Page (psio2.c)
    /// <summary>Computes the resolution at which an image fits an A4 page.</summary>
    /// <param name="w">Image width in pixels.</param>
    /// <param name="h">Image height in pixels.</param>
    /// <param name="fillfract">The fraction of the page, in each linear dimension, that must not be exceeded; use 0 for the default.</param>
    /// <returns>The resolution in ppi.</returns>
    public static int GetResA4Page(int w, int h, float fillfract)
    {
        if (fillfract == 0.0f)
            fillfract = DefaultFillFraction;
        int resw = (int)((w * 72.0) / (A4Width * fillfract));
        int resh = (int)((h * 72.0) / (A4Height * fillfract));
        return Math.Max(resw, resh);
    }

    /*-------------------------------------------------------------*
     *           Setting flag for writing bounding box hint        *
     *-------------------------------------------------------------*/
    // leptonica: l_psWriteBoundingBox (psio2.c)
    /// <summary>Sets whether the low-level PostScript writers include a bounding box hint.</summary>
    /// <param name="flag"><see langword="true"/> to write the bounding box hint; <see langword="false"/> to omit it. The default is to write it.</param>
    public static void WriteBoundingBox(bool flag)
    {
        omitBoundingBox = !flag;
    }
}
