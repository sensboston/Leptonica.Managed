using System;
using System.IO;

namespace Leptonica;

/*
 *  pdfio1.c
 *
 *    Higher-level operations for generating pdf from images.
 *
 *     1. Convert specified image files to pdf (one image file per page)
 *          PdfIo.ConvertFilesToPdf (convertFilesToPdf)
 *          PdfIo.SaConvertFilesToPdf (saConvertFilesToPdf)
 *          PdfIo.SaConvertFilesToPdfData (saConvertFilesToPdfData)
 *          PdfIo.SelectDefaultPdfEncoding (selectDefaultPdfEncoding)
 *
 *     2. Convert specified image files to pdf without scaling
 *          PdfIo.ConvertUnscaledFilesToPdf (convertUnscaledFilesToPdf)
 *          PdfIo.SaConvertUnscaledFilesToPdf (saConvertUnscaledFilesToPdf)
 *          PdfIo.SaConvertUnscaledFilesToPdfData (saConvertUnscaledFilesToPdfData)
 *          PdfIo.ConvertUnscaledToPdfData (convertUnscaledToPdfData)
 *
 *     3. Convert multiple images to pdf (one image per page)
 *          Pixa.ConvertToPdf (pixaConvertToPdf)
 *          Pixa.ConvertToPdfData (pixaConvertToPdfData)
 *
 *     4. Single page, multi-image converters
 *          PdfIo.ConvertToPdf (convertToPdf)
 *          PdfIo.ConvertImageDataToPdf (convertImageDataToPdf)
 *          PdfIo.ConvertToPdfData (convertToPdfData)
 *          PdfIo.ConvertImageDataToPdfData (convertImageDataToPdfData)
 *          Pix.ConvertToPdf (pixConvertToPdf)
 *          Pix.WriteStreamPdf (pixWriteStreamPdf)
 *          Pix.WriteMemPdf (pixWriteMemPdf)
 *
 *     5. Segmented multi-page, multi-image converter
 *          PdfIo.ConvertSegmentedFilesToPdf (convertSegmentedFilesToPdf)
 *          PdfIo.ConvertNumberedMasksToBoxaa (convertNumberedMasksToBoxaa)
 *
 *     6. Segmented single page, multi-image converters
 *          PdfIo.ConvertToPdfSegmented (convertToPdfSegmented)
 *          Pix.ConvertToPdfSegmented (pixConvertToPdfSegmented)
 *          PdfIo.ConvertToPdfDataSegmented (convertToPdfDataSegmented)
 *          Pix.ConvertToPdfDataSegmented (pixConvertToPdfDataSegmented)
 *
 *     7. Multipage concatenation
 *          PdfIo.ConcatenatePdf (concatenatePdf)
 *          PdfIo.SaConcatenatePdf (saConcatenatePdf)
 *          PdfIo.PtraConcatenatePdf (ptraConcatenatePdf)
 *          PdfIo.ConcatenatePdfToData (concatenatePdfToData)
 *          PdfIo.SaConcatenatePdfToData (saConcatenatePdfToData)
 */
public static partial class PdfIo
{
    /*---------------------------------------------------------------------*
     *    Convert specified image files to pdf (one image file per page)   *
     *---------------------------------------------------------------------*/
    // leptonica: convertFilesToPdf (pdfio1.c)
    /// <summary>Converts the image files in a directory to a multi-page PDF file, with one image on each page.</summary>
    /// <param name="dirname">The directory that contains the images.</param>
    /// <param name="substr">A substring that the file names must contain, or <see langword="null"/> to use all files in the directory.</param>
    /// <param name="res">Input resolution of all images.</param>
    /// <param name="scalefactor">Scaling factor applied to each image; a value of 0.0 or less is set to 1.0.</param>
    /// <param name="type">Encoding type: <see cref="L.L_JPEG_ENCODE"/>, <see cref="L.L_G4_ENCODE"/>, <see cref="L.L_FLATE_ENCODE"/>, <see cref="L.L_JP2K_ENCODE"/>, or <see cref="L.L_DEFAULT_ENCODE"/> to choose the type for each image.</param>
    /// <param name="quality">For JPEG: 1 to 100, with 0 for the default of 75. For JPEG 2000: 27 to 45, with 0 for the default of 34.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <param name="fileout">Path of the PDF file to write.</param>
    /// <remarks>
    /// <para>The files in the directory, after the optional filtering by the substring, are sorted lexically in increasing order before concatenation.</para>
    /// <para>Specifying one of the four encoding types forces all images to be compressed with that type. With the default type, the encoding is chosen for each
    /// image from its depth and whether it has a colormap.</para>
    /// </remarks>
    /// <exception cref="System.ArgumentException"><paramref name="dirname"/> or <paramref name="fileout"/> is <see langword="null"/>.</exception>
    public static void ConvertFilesToPdf(string dirname, string? substr, int res, float scalefactor,
                                         int type, int quality, string? title, string fileout)
    {
        const string func = "convertFilesToPdf";
        if (dirname == null)
            throw Lept.Arg(func, "dirname not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");

        Sarray sa = Sarray.GetSortedPathnamesInDirectory(dirname, substr, 0, 0);
        SaConvertFilesToPdf(sa, res, scalefactor, type, quality, title, fileout);
    }

    // leptonica: saConvertFilesToPdf (pdfio1.c)
    /// <summary>Converts the image files named in a string array to a multi-page PDF file, with one image on each page.</summary>
    /// <param name="sa">The path names of the images.</param>
    /// <param name="res">Input resolution of all images.</param>
    /// <param name="scalefactor">Scaling factor applied to each image; a value of 0.0 or less is set to 1.0.</param>
    /// <param name="type">Encoding type: <see cref="L.L_JPEG_ENCODE"/>, <see cref="L.L_G4_ENCODE"/>, <see cref="L.L_FLATE_ENCODE"/>, <see cref="L.L_JP2K_ENCODE"/>, or <see cref="L.L_DEFAULT_ENCODE"/> to choose the type for each image.</param>
    /// <param name="quality">For JPEG: 1 to 100, with 0 for the default of 75. For JPEG 2000: 27 to 45, with 0 for the default of 34.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <param name="fileout">Path of the PDF file to write.</param>
    /// <remarks>See <see cref="ConvertFilesToPdf"/>.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="sa"/> or <paramref name="fileout"/> is <see langword="null"/>.</exception>
    public static void SaConvertFilesToPdf(Sarray sa, int res, float scalefactor, int type,
                                           int quality, string? title, string fileout)
    {
        const string func = "saConvertFilesToPdf";
        if (sa == null)
            throw Lept.Arg(func, "sa not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");

        byte[] data = SaConvertFilesToPdfData(sa, res, scalefactor, type, quality, title);
        Utils.BinaryWrite(fileout, "w", data);
    }

    // leptonica: saConvertFilesToPdfData (pdfio1.c)
    /// <summary>Converts the image files named in a string array to multi-page PDF data, with one image on each page.</summary>
    /// <param name="sa">The path names of the images.</param>
    /// <param name="res">Input resolution of all images.</param>
    /// <param name="scalefactor">Scaling factor applied to each image; a value of 0.0 or less is set to 1.0.</param>
    /// <param name="type">Encoding type: <see cref="L.L_JPEG_ENCODE"/>, <see cref="L.L_G4_ENCODE"/>, <see cref="L.L_FLATE_ENCODE"/>, <see cref="L.L_JP2K_ENCODE"/>, or <see cref="L.L_DEFAULT_ENCODE"/> to choose the type for each image.</param>
    /// <param name="quality">For JPEG: 1 to 100, with 0 for the default of 75. For JPEG 2000: 27 to 45, with 0 for the default of 34.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <returns>The PDF data for all the images.</returns>
    /// <remarks>See <see cref="ConvertFilesToPdf"/>. Images that cannot be read or encoded are skipped.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="sa"/> is <see langword="null"/>.</exception>
    /// <exception cref="LeptonicaException">No page could be made.</exception>
    public static byte[] SaConvertFilesToPdfData(Sarray sa, int res, float scalefactor,
                                                 int type, int quality, string? title)
    {
        const string func = "saConvertFilesToPdfData";
        if (sa == null)
            throw Lept.Arg(func, "sa not defined");
        if (scalefactor <= 0.0f)
            scalefactor = 1.0f;
        if (type != L.L_JPEG_ENCODE && type != L.L_G4_ENCODE &&
            type != L.L_FLATE_ENCODE && type != L.L_JP2K_ENCODE) {
            type = L.L_DEFAULT_ENCODE;
        }

        int n = sa.Count;
        var pa_data = L_Ptra<L_Bytea>.Create(n);
        for (int i = 0; i < n; i++) {
            string fname = sa.GetString(i, L.L_NOCOPY);
            Pix pixs;
            try {
                pixs = Pix.Read(fname);
            } catch (Exception) {
                Log.Error(func, $"image not readable from file {fname}");
                continue;
            }
            Pix pix = (scalefactor != 1.0f)
                ? pixs.Scale(scalefactor, scalefactor)
                : Pix.Clone(pixs);
            int scaledres = (int)(res * scalefactor);

            int pagetype = (type != L.L_DEFAULT_ENCODE)
                ? type
                : SelectDefaultPdfEncoding(pix);

            try {
                byte[] imdata = pix.ConvertToPdfDataSingle(pagetype, quality, 0, 0, scaledres, title);
                L_Bytea ba = L_Bytea.InitFromMem(imdata);
                pa_data.Add(ba);
            } catch (Exception) {
                Log.Error(func, $"pdf encoding failed for {fname}");
            }
        }

        if (pa_data.ActualCount == 0)
            throw Lept.Fail(func, "no pdf files made");

        return PtraConcatenateToData(pa_data, null);
    }

    // leptonica: selectDefaultPdfEncoding (pdfio1.c)
    /// <summary>Chooses the PDF encoding that should give the smallest file for an image.</summary>
    /// <param name="pix">The image.</param>
    /// <returns><see cref="L.L_G4_ENCODE"/>, <see cref="L.L_JPEG_ENCODE"/> or <see cref="L.L_FLATE_ENCODE"/>.</returns>
    /// <remarks>
    /// <para>The decision assumes that a JPEG image would be encoded with quality 75, and it is approximate: all colormapped images are encoded losslessly with
    /// Flate, and an image with fewer than about 20 colors is likely to be smaller when Flate encoded than when JPEG encoded. For example, an image
    /// made by a scale-to-gray operation has 10 colors, and Flate encoding gives about twice the compression of JPEG with quality 75.</para>
    /// <para>JPEG 2000 could be used instead of JPEG, but it is not much better and the JPEG codec is more commonly available.</para>
    /// </remarks>
    /// <exception cref="System.ArgumentException"><paramref name="pix"/> is <see langword="null"/>.</exception>
    public static int SelectDefaultPdfEncoding(Pix pix)
    {
        const string func = "selectDefaultPdfEncoding";
        if (pix == null)
            throw Lept.Arg(func, "pix not defined");

        pix.GetDimensions(out int w, out int h, out int d);
        PixColormap? cmap = pix.Colormap;
        if (d == 8 && cmap == null) {
            int factor = Math.Max(1, (int)Math.Sqrt((double)(w * h) / 20000.0));
            int ncolors = pix.NumColors(factor);
            return (ncolors < 20) ? L.L_FLATE_ENCODE : L.L_JPEG_ENCODE;
        } else if (d == 1) {
            return L.L_G4_ENCODE;
        } else if (cmap != null || d == 2 || d == 4) {
            return L.L_FLATE_ENCODE;
        } else if (d == 8 || d == 32) {
            return L.L_JPEG_ENCODE;
        } else if (d == 16) {
            return L.L_FLATE_ENCODE;
        } else {
            throw Lept.Fail(func, "type selection failure");
        }
    }

    /*---------------------------------------------------------------------*
     *          Convert specified image files to pdf without scaling       *
     *---------------------------------------------------------------------*/
    // leptonica: convertUnscaledFilesToPdf (pdfio1.c)
    /// <summary>Converts the image files in a directory to a multi-page PDF file without scaling or recompressing the images where possible.</summary>
    /// <param name="dirname">The directory that contains the images.</param>
    /// <param name="substr">A substring that the file names must contain, or <see langword="null"/> to use all files in the directory.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <param name="fileout">Path of the PDF file to write.</param>
    /// <remarks>
    /// <para>The files in the directory, after the optional filtering by the substring, are sorted lexically in increasing order before concatenation.</para>
    /// <para>This is very fast for JPEG, JPEG 2000 and some PNG files, because the compressed data is wrapped and concatenated. For other types of PNG, the images must be read and recompressed.</para>
    /// </remarks>
    public static void ConvertUnscaledFilesToPdf(string dirname, string? substr,
                                                 string? title, string fileout)
    {
        const string func = "convertUnscaledFilesToPdf";
        if (dirname == null)
            throw Lept.Arg(func, "dirname not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");

        Sarray sa = Sarray.GetSortedPathnamesInDirectory(dirname, substr, 0, 0);
        SaConvertUnscaledFilesToPdf(sa, title, fileout);
    }

    // leptonica: saConvertUnscaledFilesToPdf (pdfio1.c)
    /// <summary>Converts the image files named in a string array to a multi-page PDF file without scaling or recompressing the images where possible.</summary>
    /// <param name="sa">The path names of the images.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <param name="fileout">Path of the PDF file to write.</param>
    /// <remarks>See <see cref="ConvertUnscaledFilesToPdf"/>.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="sa"/> is <see langword="null"/>.</exception>
    public static void SaConvertUnscaledFilesToPdf(Sarray sa, string? title, string fileout)
    {
        const string func = "saConvertUnscaledFilesToPdf";
        if (sa == null)
            throw Lept.Arg(func, "sa not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");

        byte[] data = SaConvertUnscaledFilesToPdfData(sa, title);
        Utils.BinaryWrite(fileout, "w", data);
    }

    // leptonica: saConvertUnscaledFilesToPdfData (pdfio1.c)
    /// <summary>Converts the image files named in a string array to multi-page PDF data without scaling or recompressing the images where possible.</summary>
    /// <param name="sa">The path names of the image files.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <returns>The PDF data for all the images.</returns>
    /// <remarks>This is very fast for JPEG, JPEG 2000 and some PNG files, because the compressed data is wrapped and concatenated. For other types of PNG, the images must be read and recompressed.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="sa"/> is <see langword="null"/>.</exception>
    public static byte[] SaConvertUnscaledFilesToPdfData(Sarray sa, string? title)
    {
        const string func = "saConvertUnscaledFilesToPdfData";
        if (sa == null)
            throw Lept.Arg(func, "sa not defined");

        int n = sa.Count;
        var pa_data = L_Ptra<L_Bytea>.Create(n);
        for (int i = 0; i < n; i++) {
            string fname = sa.GetString(i, L.L_NOCOPY);
            try {
                byte[] imdata = ConvertUnscaledToPdfData(fname, title);
                pa_data.Add(L_Bytea.InitFromMem(imdata));
            } catch (Exception) {
                continue;
            }
        }

        if (pa_data.ActualCount == 0)
            throw Lept.Fail(func, "no pdf files made");

        return PtraConcatenateToData(pa_data, null);
    }

    // leptonica: convertUnscaledToPdfData (pdfio1.c)
    /// <summary>Converts an image file to single-page PDF data without scaling or recompressing the image where possible.</summary>
    /// <param name="fname">Path of the image file, in any format.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <returns>The PDF data for the image.</returns>
    /// <remarks>This is very fast for JPEG, JPEG 2000 and some PNG files, because the compressed data is wrapped. For other types of PNG, the image must be read and recompressed.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="fname"/> is <see langword="null"/>.</exception>
    public static byte[] ConvertUnscaledToPdfData(string fname, string? title)
    {
        const string func = "convertUnscaledToPdfData";
        if (fname == null)
            throw Lept.Arg(func, "fname not defined");

        Pix.FindFileFormat(fname, out int format);
        if (format == L.IFF_UNKNOWN) {
            Log.Warning(func, $"file {fname} format is unknown; skip");
            throw Lept.Fail(func, "unknown format");
        }
        if (format == L.IFF_PS || format == L.IFF_LPDF) {
            Log.Warning(func, $"file {fname} format is {format}; skip");
            throw Lept.Fail(func, "unsupported format");
        }

        L_Compressed_Data cid = GenerateCIDataForPdf(fname, null, 0);
        return CidConvertToPdfData(cid, title);
    }

    /*---------------------------------------------------------------------*
     *                Single page, multi-image converters                  *
     *---------------------------------------------------------------------*/
    // leptonica: convertToPdf (pdfio1.c)
    /// <summary>Wraps an image file in PDF, either as a single-image page or as one image of several placed on the same page.</summary>
    /// <param name="filein">The input image file, in any format.</param>
    /// <param name="type">Encoding type: <see cref="L.L_JPEG_ENCODE"/>, <see cref="L.L_G4_ENCODE"/>, <see cref="L.L_FLATE_ENCODE"/> or <see cref="L.L_JP2K_ENCODE"/>.</param>
    /// <param name="quality">For JPEG: 1 to 100, with 0 for the default of 75. For JPEG 2000: 27 to 45, with 0 for the default of 34.</param>
    /// <param name="fileout">Path of the PDF file to write; it is needed only for the last image on the page.</param>
    /// <param name="x">Horizontal location of the lower-left corner of the image, in pixels, relative to the origin at the lower-left corner of the page.</param>
    /// <param name="y">Vertical location of the lower-left corner of the image, in pixels, relative to the origin at the lower-left corner of the page.</param>
    /// <param name="res">Overrides the resolution of the input image, in ppi; 0 respects the resolution embedded in the image.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <param name="plpd">The state of the page being composed. It is created on the first call and returned until the last image is processed, when it is released and set to <see langword="null"/>.</param>
    /// <param name="position">The position in the image sequence: <see cref="L.L_FIRST_IMAGE"/>, <see cref="L.L_NEXT_IMAGE"/> or <see cref="L.L_LAST_IMAGE"/>.</param>
    /// <remarks>
    /// <para>To wrap only one image, use the overload without the <paramref name="plpd"/> and <paramref name="position"/> parameters.</para>
    /// <para>To wrap several images on a single page, call this once for each image, passing the same <paramref name="plpd"/> variable (initially <see langword="null"/>)
    /// with <see cref="L.L_FIRST_IMAGE"/>, then <see cref="L.L_NEXT_IMAGE"/> for the following images, and <see cref="L.L_LAST_IMAGE"/> for the last. The
    /// result is written to the <paramref name="fileout"/> of the last call, and nothing needs to be cleaned up afterwards.</para>
    /// <para>Pass 0 as <paramref name="res"/> to respect the resolution embedded in the file; if none is embedded, a default of 300 ppi is used.
    /// If the input resolution equals the resolution of the output device, the image is displayed at its original size; at 72 ppi the device
    /// renders it at one point per pixel.</para>
    /// </remarks>
    public static void ConvertToPdf(string filein, int type, int quality, string? fileout,
                                    int x, int y, int res, string? title,
                                    ref L_Pdf_Data? plpd, int position)
    {
        ConvertToPdfInternal(filein, type, quality, fileout, x, y, res, title,
                             useMultiImage: true, ref plpd, position);
    }

    /// <summary>Wraps a single image file in a one-page PDF file.</summary>
    /// <param name="filein">The input image file, in any format.</param>
    /// <param name="type">Encoding type: <see cref="L.L_JPEG_ENCODE"/>, <see cref="L.L_G4_ENCODE"/>, <see cref="L.L_FLATE_ENCODE"/> or <see cref="L.L_JP2K_ENCODE"/>.</param>
    /// <param name="quality">For JPEG: 1 to 100, with 0 for the default of 75. For JPEG 2000: 27 to 45, with 0 for the default of 34.</param>
    /// <param name="fileout">Path of the PDF file to write.</param>
    /// <param name="x">Horizontal location of the lower-left corner of the image, in pixels, relative to the origin at the lower-left corner of the page.</param>
    /// <param name="y">Vertical location of the lower-left corner of the image, in pixels, relative to the origin at the lower-left corner of the page.</param>
    /// <param name="res">Overrides the resolution of the input image, in ppi; 0 respects the resolution embedded in the image.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    public static void ConvertToPdf(string filein, int type, int quality, string fileout,
                                    int x, int y, int res, string? title)
    {
        L_Pdf_Data? unused = null;
        ConvertToPdfInternal(filein, type, quality, fileout, x, y, res, title,
                             useMultiImage: false, ref unused, 0);
    }

    private static void ConvertToPdfInternal(string filein, int type, int quality, string? fileout,
                                             int x, int y, int res, string? title,
                                             bool useMultiImage, ref L_Pdf_Data? plpd, int position)
    {
        const string func = "convertToPdf";
        if (filein == null)
            throw Lept.Arg(func, "filein not defined");
        if (!useMultiImage || position == L.L_LAST_IMAGE) {
            if (fileout == null)
                throw Lept.Arg(func, "fileout not defined");
        }

        byte[]? data = ConvertToPdfDataInternal(filein, type, quality, x, y, res, title,
                                                useMultiImage, ref plpd, position);
        if (!useMultiImage || position == L.L_LAST_IMAGE)
            Utils.BinaryWrite(fileout!, "w", data!);
    }

    // leptonica: convertImageDataToPdf (pdfio1.c)
    /// <summary>Wraps formatted image data (for example PNG or JPEG) in PDF, either as a single-image page or as one image of several placed on the same page.</summary>
    /// <param name="imdata">The formatted image data.</param>
    /// <param name="type">Encoding type: <see cref="L.L_JPEG_ENCODE"/>, <see cref="L.L_G4_ENCODE"/>, <see cref="L.L_FLATE_ENCODE"/> or <see cref="L.L_JP2K_ENCODE"/>.</param>
    /// <param name="quality">For JPEG: 1 to 100, with 0 for the default of 75. For JPEG 2000: 27 to 45, with 0 for the default of 34.</param>
    /// <param name="fileout">Path of the PDF file to write; it is needed only for the last image on the page.</param>
    /// <param name="x">Horizontal location of the lower-left corner of the image, in pixels, relative to the origin at the lower-left corner of the page.</param>
    /// <param name="y">Vertical location of the lower-left corner of the image, in pixels, relative to the origin at the lower-left corner of the page.</param>
    /// <param name="res">Overrides the resolution of the input image, in ppi; 0 respects the resolution embedded in the image.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <param name="plpd">The state of the page being composed. It is created on the first call and returned until the last image is processed, when it is released and set to <see langword="null"/>.</param>
    /// <param name="position">The position in the image sequence: <see cref="L.L_FIRST_IMAGE"/>, <see cref="L.L_NEXT_IMAGE"/> or <see cref="L.L_LAST_IMAGE"/>.</param>
    /// <remarks>If <paramref name="res"/> is 0 and the image has no resolution, a default of 300 ppi is used. See <see cref="ConvertToPdf(string, int, int, string, int, int, int, string, ref L_Pdf_Data, int)"/>.</remarks>
    public static void ConvertImageDataToPdf(ReadOnlySpan<byte> imdata, int type, int quality,
                                             string? fileout, int x, int y, int res, string? title,
                                             ref L_Pdf_Data? plpd, int position)
    {
        ConvertImageDataToPdfInternal(imdata, type, quality, fileout, x, y, res, title,
                                      useMultiImage: true, ref plpd, position);
    }

    /// <summary>Wraps formatted image data (for example PNG or JPEG) in a one-page PDF file.</summary>
    /// <param name="imdata">The formatted image data.</param>
    /// <param name="type">Encoding type: <see cref="L.L_JPEG_ENCODE"/>, <see cref="L.L_G4_ENCODE"/>, <see cref="L.L_FLATE_ENCODE"/> or <see cref="L.L_JP2K_ENCODE"/>.</param>
    /// <param name="quality">For JPEG: 1 to 100, with 0 for the default of 75. For JPEG 2000: 27 to 45, with 0 for the default of 34.</param>
    /// <param name="fileout">Path of the PDF file to write.</param>
    /// <param name="x">Horizontal location of the lower-left corner of the image, in pixels, relative to the origin at the lower-left corner of the page.</param>
    /// <param name="y">Vertical location of the lower-left corner of the image, in pixels, relative to the origin at the lower-left corner of the page.</param>
    /// <param name="res">Overrides the resolution of the input image, in ppi; 0 respects the resolution embedded in the image.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    public static void ConvertImageDataToPdf(ReadOnlySpan<byte> imdata, int type, int quality,
                                             string fileout, int x, int y, int res, string? title)
    {
        L_Pdf_Data? unused = null;
        ConvertImageDataToPdfInternal(imdata, type, quality, fileout, x, y, res, title,
                                      useMultiImage: false, ref unused, 0);
    }

    private static void ConvertImageDataToPdfInternal(ReadOnlySpan<byte> imdata, int type, int quality,
                                                      string? fileout, int x, int y, int res,
                                                      string? title, bool useMultiImage,
                                                      ref L_Pdf_Data? plpd, int position)
    {
        const string func = "convertImageDataToPdf";
        if (imdata.IsEmpty)
            throw Lept.Arg(func, "image data not defined");
        if (!useMultiImage || position == L.L_LAST_IMAGE) {
            if (fileout == null)
                throw Lept.Arg(func, "fileout not defined");
        }

        Pix pix = Pix.ReadMem(imdata);
        if (type != L.L_JPEG_ENCODE && type != L.L_G4_ENCODE &&
            type != L.L_FLATE_ENCODE && type != L.L_JP2K_ENCODE) {
            type = SelectDefaultPdfEncoding(pix);
        }
        pix.ConvertToPdfInternal(type, quality, fileout, x, y, res, title,
                                 useMultiImage, ref plpd, position);
    }

    // leptonica: convertToPdfData (pdfio1.c)
    /// <summary>Wraps an image file in PDF data, either as a single-image page or as one image of several placed on the same page.</summary>
    /// <param name="filein">The input image file, in any format.</param>
    /// <param name="type">Encoding type: <see cref="L.L_JPEG_ENCODE"/>, <see cref="L.L_G4_ENCODE"/>, <see cref="L.L_FLATE_ENCODE"/> or <see cref="L.L_JP2K_ENCODE"/>.</param>
    /// <param name="quality">For JPEG: 1 to 100, with 0 for the default of 75. For JPEG 2000: 27 to 45, with 0 for the default of 34.</param>
    /// <param name="x">Horizontal location of the lower-left corner of the image, in pixels, relative to the origin at the lower-left corner of the page.</param>
    /// <param name="y">Vertical location of the lower-left corner of the image, in pixels, relative to the origin at the lower-left corner of the page.</param>
    /// <param name="res">Overrides the resolution of the input image, in ppi; 0 respects the resolution embedded in the image.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <param name="plpd">The state of the page being composed. It is created on the first call and returned until the last image is processed, when it is released and set to <see langword="null"/>.</param>
    /// <param name="position">The position in the image sequence: <see cref="L.L_FIRST_IMAGE"/>, <see cref="L.L_NEXT_IMAGE"/> or <see cref="L.L_LAST_IMAGE"/>.</param>
    /// <returns>The PDF data after the last image of the page has been added; otherwise <see langword="null"/>.</returns>
    /// <remarks>If <paramref name="res"/> is 0 and the image has no resolution, a default of 300 ppi is used. See <see cref="ConvertToPdf(string, int, int, string, int, int, int, string, ref L_Pdf_Data, int)"/>.</remarks>
    public static byte[]? ConvertToPdfData(string filein, int type, int quality, int x, int y,
                                           int res, string? title, ref L_Pdf_Data? plpd, int position)
    {
        return ConvertToPdfDataInternal(filein, type, quality, x, y, res, title,
                                        useMultiImage: true, ref plpd, position);
    }

    /// <summary>Wraps a single image file in one-page PDF data.</summary>
    /// <param name="filein">The input image file, in any format.</param>
    /// <param name="type">Encoding type: <see cref="L.L_JPEG_ENCODE"/>, <see cref="L.L_G4_ENCODE"/>, <see cref="L.L_FLATE_ENCODE"/> or <see cref="L.L_JP2K_ENCODE"/>.</param>
    /// <param name="quality">For JPEG: 1 to 100, with 0 for the default of 75. For JPEG 2000: 27 to 45, with 0 for the default of 34.</param>
    /// <param name="x">Horizontal location of the lower-left corner of the image, in pixels, relative to the origin at the lower-left corner of the page.</param>
    /// <param name="y">Vertical location of the lower-left corner of the image, in pixels, relative to the origin at the lower-left corner of the page.</param>
    /// <param name="res">Overrides the resolution of the input image, in ppi; 0 respects the resolution embedded in the image.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <returns>The PDF data.</returns>
    public static byte[] ConvertToPdfData(string filein, int type, int quality, int x, int y,
                                          int res, string? title)
    {
        L_Pdf_Data? unused = null;
        return ConvertToPdfDataInternal(filein, type, quality, x, y, res, title,
                                        useMultiImage: false, ref unused, 0)!;
    }

    private static byte[]? ConvertToPdfDataInternal(string filein, int type, int quality,
                                                    int x, int y, int res, string? title,
                                                    bool useMultiImage, ref L_Pdf_Data? plpd,
                                                    int position)
    {
        const string func = "convertToPdfData";
        if (filein == null)
            throw Lept.Arg(func, "filein not defined");

        Pix pix = Pix.Read(filein);
        return PixConvertToPdfDataCore(pix, type, quality, x, y, res, title,
                                       useMultiImage, ref plpd, position);
    }

    // leptonica: convertImageDataToPdfData (pdfio1.c)
    /// <summary>Wraps formatted image data (for example PNG or JPEG) in PDF data, either as a single-image page or as one image of several placed on the same page.</summary>
    /// <param name="imdata">The formatted image data.</param>
    /// <param name="type">Encoding type: <see cref="L.L_JPEG_ENCODE"/>, <see cref="L.L_G4_ENCODE"/>, <see cref="L.L_FLATE_ENCODE"/> or <see cref="L.L_JP2K_ENCODE"/>.</param>
    /// <param name="quality">For JPEG: 1 to 100, with 0 for the default of 75. For JPEG 2000: 27 to 45, with 0 for the default of 34.</param>
    /// <param name="x">Horizontal location of the lower-left corner of the image, in pixels, relative to the origin at the lower-left corner of the page.</param>
    /// <param name="y">Vertical location of the lower-left corner of the image, in pixels, relative to the origin at the lower-left corner of the page.</param>
    /// <param name="res">Overrides the resolution of the input image, in ppi; 0 respects the resolution embedded in the image.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <param name="plpd">The state of the page being composed. It is created on the first call and returned until the last image is processed, when it is released and set to <see langword="null"/>.</param>
    /// <param name="position">The position in the image sequence: <see cref="L.L_FIRST_IMAGE"/>, <see cref="L.L_NEXT_IMAGE"/> or <see cref="L.L_LAST_IMAGE"/>.</param>
    /// <returns>The PDF data after the last image of the page has been added; otherwise <see langword="null"/>.</returns>
    /// <remarks>If <paramref name="res"/> is 0 and the image has no resolution, a default of 300 ppi is used. See <see cref="ConvertToPdf(string, int, int, string, int, int, int, string, ref L_Pdf_Data, int)"/>.</remarks>
    public static byte[]? ConvertImageDataToPdfData(ReadOnlySpan<byte> imdata, int type, int quality,
                                                    int x, int y, int res, string? title,
                                                    ref L_Pdf_Data? plpd, int position)
    {
        return ConvertImageDataToPdfDataInternal(imdata, type, quality, x, y, res, title,
                                                 useMultiImage: true, ref plpd, position);
    }

    /// <summary>Wraps formatted image data (for example PNG or JPEG) in one-page PDF data.</summary>
    /// <param name="imdata">The formatted image data.</param>
    /// <param name="type">Encoding type: <see cref="L.L_JPEG_ENCODE"/>, <see cref="L.L_G4_ENCODE"/>, <see cref="L.L_FLATE_ENCODE"/> or <see cref="L.L_JP2K_ENCODE"/>.</param>
    /// <param name="quality">For JPEG: 1 to 100, with 0 for the default of 75. For JPEG 2000: 27 to 45, with 0 for the default of 34.</param>
    /// <param name="x">Horizontal location of the lower-left corner of the image, in pixels, relative to the origin at the lower-left corner of the page.</param>
    /// <param name="y">Vertical location of the lower-left corner of the image, in pixels, relative to the origin at the lower-left corner of the page.</param>
    /// <param name="res">Overrides the resolution of the input image, in ppi; 0 respects the resolution embedded in the image.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <returns>The PDF data.</returns>
    public static byte[] ConvertImageDataToPdfData(ReadOnlySpan<byte> imdata, int type, int quality,
                                                   int x, int y, int res, string? title)
    {
        L_Pdf_Data? unused = null;
        return ConvertImageDataToPdfDataInternal(imdata, type, quality, x, y, res, title,
                                                 useMultiImage: false, ref unused, 0)!;
    }

    private static byte[]? ConvertImageDataToPdfDataInternal(ReadOnlySpan<byte> imdata, int type,
                                                             int quality, int x, int y, int res,
                                                             string? title, bool useMultiImage,
                                                             ref L_Pdf_Data? plpd, int position)
    {
        const string func = "convertImageDataToPdfData";
        if (imdata.IsEmpty)
            throw Lept.Arg(func, "image data not defined");
        if (useMultiImage && position == L.L_FIRST_IMAGE)
            plpd = null;

        Pix pix = Pix.ReadMem(imdata);
        if (type != L.L_JPEG_ENCODE && type != L.L_G4_ENCODE &&
            type != L.L_FLATE_ENCODE && type != L.L_JP2K_ENCODE) {
            type = SelectDefaultPdfEncoding(pix);
        }
        return PixConvertToPdfDataCore(pix, type, quality, x, y, res, title,
                                       useMultiImage, ref plpd, position);
    }

    /*---------------------------------------------------------------------*
     *            Segmented multi-page, multi-image converter              *
     *---------------------------------------------------------------------*/
    // leptonica: convertSegmentedFilesToPdf (pdfio1.c)
    /// <summary>Converts the image files in a directory to a multi-page PDF file, compressing the image regions of each page separately from the rest.</summary>
    /// <param name="dirname">The directory that contains the images.</param>
    /// <param name="substr">A substring that the file names must contain, or <see langword="null"/> to use all files in the directory.</param>
    /// <param name="res">Input resolution of all images.</param>
    /// <param name="type">Compression type for the non-image regions; the image regions are always compressed with <see cref="L.L_JPEG_ENCODE"/>.</param>
    /// <param name="thresh">Threshold used for converting gray to 1 bpp with <see cref="L.L_G4_ENCODE"/>.</param>
    /// <param name="baa">The image regions of each page, as one <see cref="Boxa"/> per image file aligned with the sorted images, or <see langword="null"/> if there are none.</param>
    /// <param name="quality">JPEG quality, used for the JPEG regions only; 0 for the default of 75.</param>
    /// <param name="scalefactor">Scaling factor applied to each image region.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <param name="fileout">Path of the PDF file to write.</param>
    /// <remarks>
    /// <para>The files in the directory, after the optional filtering by the substring, are sorted lexically in increasing order before concatenation.</para>
    /// <para>The scale factor is typically less than 1.0, to save bytes in the final PDF, because the resolution is often not critical in non-text regions.</para>
    /// <para>If the non-image regions have a depth greater than 1 and the encoding type is G4, they are scaled up by 2 and thresholded; otherwise they are not scaled.
    /// This function can generate a multipage G4-compressed PDF from any input by passing <see langword="null"/> for <paramref name="baa"/> and <see cref="L.L_G4_ENCODE"/> as <paramref name="type"/>.</para>
    /// </remarks>
    public static void ConvertSegmentedFilesToPdf(string dirname, string? substr, int res,
                                                  int type, int thresh, Boxaa? baa, int quality,
                                                  float scalefactor, string? title, string fileout)
    {
        const string func = "convertSegmentedFilesToPdf";
        if (dirname == null)
            throw Lept.Arg(func, "dirname not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");

        Sarray sa = Sarray.GetNumberedPathnamesInDirectory(dirname, substr, 0, 0, 10000);
        int npages = sa.Count;
        if (baa != null && baa.Count < npages) {
            Boxa empty = Boxa.Create(1);
            baa.ExtendWithInit(npages, empty);
        }

        var pa_data = L_Ptra<L_Bytea>.Create(npages);
        for (int i = 0; i < npages; i++) {
            string fname = sa.GetString(i, L.L_NOCOPY);
            if (fname.Length == 0)
                continue;
            Boxa? boxa = null;
            if (baa != null) {
                boxa = baa.GetBoxa(i, L.L_CLONE);
                if (boxa.Count == 0)
                    boxa = null;
            }
            try {
                byte[] imdata = ConvertToPdfDataSegmented(fname, res, type, thresh, boxa,
                                                          quality, scalefactor, title);
                pa_data.Add(L_Bytea.InitFromMem(imdata));
            } catch (Exception) {
                Log.Error(func, $"pdf encoding failed for {fname}");
            }
        }

        if (pa_data.ActualCount == 0)
            throw Lept.Fail(func, "no pdf files made");

        byte[] data = PtraConcatenateToData(pa_data, null);
        Utils.BinaryWrite(fileout, "w", data);
    }

    // leptonica: convertNumberedMasksToBoxaa (pdfio1.c)
    /// <summary>Converts numbered mask images in a directory to the image regions of each page.</summary>
    /// <param name="dirname">The directory that contains the mask images.</param>
    /// <param name="substr">A substring that the file names must contain, or <see langword="null"/> to use all files in the directory.</param>
    /// <param name="numpre">Number of characters in the file name before the number.</param>
    /// <param name="numpost">Number of characters in the file name after the number, up to the dot before the extension.</param>
    /// <returns>A <see cref="Boxaa"/> with the mask regions of each page.</returns>
    /// <remarks>This is a convenient way to make the region input for <see cref="ConvertSegmentedFilesToPdf"/>. It guarantees that each <see cref="Boxa"/> is aligned with the page images, even if some are empty.</remarks>
    public static Boxaa ConvertNumberedMasksToBoxaa(string dirname, string? substr,
                                                    int numpre, int numpost)
    {
        const string func = "convertNumberedMasksToBoxaa";
        if (dirname == null)
            throw Lept.Arg(func, "dirname not defined");

        Sarray sa = Sarray.GetNumberedPathnamesInDirectory(dirname, substr, numpre, numpost, 10000);
        int n = sa.Count;
        Boxaa baa = Boxaa.Create(n);
        Boxa init = Boxa.Create(1);
        baa.InitFull(init);
        for (int i = 0; i < n; i++) {
            string fname = sa.GetString(i, L.L_NOCOPY);
            if (fname.Length == 0)
                continue;
            Pix pix;
            try {
                pix = Pix.Read(fname);
            } catch (Exception) {
                Log.Warning(func, $"invalid image on page {i}");
                continue;
            }
            Boxa boxa = pix.ConnComp(8);
            baa.ReplaceBoxa(i, boxa);
        }
        return baa;
    }

    /*---------------------------------------------------------------------*
     *            Segmented single page, multi-image converters            *
     *---------------------------------------------------------------------*/
    // leptonica: convertToPdfSegmented (pdfio1.c)
    /// <summary>Converts an image file to a one-page PDF file, compressing the image regions separately from the rest.</summary>
    /// <param name="filein">The input image file, in any format.</param>
    /// <param name="res">Input image resolution, typically 300 ppi; use 0 for the default.</param>
    /// <param name="type">Compression type for the non-image regions; the image regions are always compressed with <see cref="L.L_JPEG_ENCODE"/>.</param>
    /// <param name="thresh">Threshold used for converting gray to 1 bpp with <see cref="L.L_G4_ENCODE"/>.</param>
    /// <param name="boxa">The image regions, or <see langword="null"/> if there are none.</param>
    /// <param name="quality">JPEG quality for the image regions; 0 for the default.</param>
    /// <param name="scalefactor">Scaling factor for the JPEG regions; it must not exceed 1.0, and 0 means 1.0.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <param name="fileout">Path of the PDF file to write.</param>
    /// <remarks>
    /// <para>If there are no image regions, <paramref name="quality"/> and <paramref name="scalefactor"/> are ignored. Typically <paramref name="scalefactor"/> is less than 1.0,
    /// because the image regions can be rendered at a lower resolution than the text regions for better compression. If the input image is 1 bpp and the
    /// scale factor is less than 1.0, the image regions are downsampled to gray before they are compressed.</para>
    /// <para>If the compression type for the non-image regions is G4 and the depth is greater than 1, the image is scaled up by 2 and thresholded to 1 bpp;
    /// this is the only case in which <paramref name="thresh"/> is used. <paramref name="quality"/> is used only for the image regions; when
    /// <paramref name="type"/> is <see cref="L.L_JPEG_ENCODE"/>, the non-image regions use the default JPEG quality of 75.</para>
    /// <para>How the non-image regions are processed:</para>
    /// <list type="table">
    /// <listheader><term>Input</term><description>G4 / JPEG / Flate</description></listheader>
    /// <item><term>1 bpp</term><description>G4 at 1x, 1 bpp / Flate at 1x, 1 bpp / Flate at 1x, 1 bpp</description></item>
    /// <item><term>colormap</term><description>G4 at 2x, 1 bpp / Flate at 1x, colormap / Flate at 1x, colormap</description></item>
    /// <item><term>2 or 4 bpp, no colormap</term><description>G4 at 2x, 1 bpp / Flate at 1x, 2 or 4 bpp / Flate at 1x, 2 or 4 bpp</description></item>
    /// <item><term>8 or 32 bpp, no colormap</term><description>G4 at 2x, 1 bpp / JPEG at 1x, 8 or 32 bpp / Flate at 1x, 8 or 32 bpp</description></item>
    /// </list>
    /// <para>In short: if G4 is requested, G4 is used with 2x upscaling in all cases except 1 bpp; if JPEG is requested, Flate is used for all cases except
    /// 8 bpp without a colormap and 32 bpp; if Flate is requested, Flate is used with no transformation of the raster data.</para>
    /// </remarks>
    public static void ConvertToPdfSegmented(string filein, int res, int type, int thresh,
                                             Boxa? boxa, int quality, float scalefactor,
                                             string? title, string fileout)
    {
        const string func = "convertToPdfSegmented";
        if (filein == null)
            throw Lept.Arg(func, "filein not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");
        if (type != L.L_G4_ENCODE && type != L.L_JPEG_ENCODE && type != L.L_FLATE_ENCODE)
            throw Lept.Arg(func, "invalid conversion type");
        if (boxa != null && scalefactor > 1.0f) {
            Log.Warning(func, "setting scalefactor to 1.0");
            scalefactor = 1.0f;
        }

        Pix pixs = Pix.Read(filein);
        pixs.ConvertToPdfSegmented(res, type, thresh, boxa, quality, scalefactor, title, fileout);
    }

    // leptonica: convertToPdfDataSegmented (pdfio1.c)
    /// <summary>Converts an image file to one-page PDF data, compressing the image regions separately from the rest.</summary>
    /// <param name="filein">The input image file, in any format.</param>
    /// <param name="res">Input image resolution, typically 300 ppi; use 0 for the default.</param>
    /// <param name="type">Compression type for the non-image regions; the image regions are always compressed with <see cref="L.L_JPEG_ENCODE"/>.</param>
    /// <param name="thresh">Threshold used for converting gray to 1 bpp with <see cref="L.L_G4_ENCODE"/>.</param>
    /// <param name="boxa">The image regions, or <see langword="null"/> if there are none.</param>
    /// <param name="quality">JPEG quality for the image regions; 0 for the default.</param>
    /// <param name="scalefactor">Scaling factor for the JPEG regions; it must not exceed 1.0, and 0 means 1.0.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <returns>The PDF data.</returns>
    /// <remarks>See <see cref="ConvertToPdfSegmented"/>.</remarks>
    public static byte[] ConvertToPdfDataSegmented(string filein, int res, int type, int thresh,
                                                   Boxa? boxa, int quality, float scalefactor,
                                                   string? title)
    {
        const string func = "convertToPdfDataSegmented";
        if (filein == null)
            throw Lept.Arg(func, "filein not defined");
        if (type != L.L_G4_ENCODE && type != L.L_JPEG_ENCODE && type != L.L_FLATE_ENCODE)
            throw Lept.Arg(func, "invalid conversion type");
        if (boxa != null && scalefactor > 1.0f) {
            Log.Warning(func, "setting scalefactor to 1.0");
            scalefactor = 1.0f;
        }

        Pix pixs = Pix.Read(filein);
        return pixs.ConvertToPdfDataSegmented(res, type, thresh, boxa, quality, scalefactor, title);
    }

    /*---------------------------------------------------------------------*
     *                         Multi-page concatenation                    *
     *---------------------------------------------------------------------*/
    // leptonica: concatenatePdf (pdfio1.c)
    /// <summary>Concatenates the single-page PDF files of a directory into one multi-page PDF file.</summary>
    /// <param name="dirname">The directory that contains the single-page PDF files.</param>
    /// <param name="substr">A substring that the file names must contain, or <see langword="null"/> to use all files in the directory.</param>
    /// <param name="fileout">Path of the concatenated PDF file to write.</param>
    /// <remarks>This only works with PDF files made by this library. The files are sorted lexically in increasing order before concatenation.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="dirname"/> or <paramref name="fileout"/> is <see langword="null"/>.</exception>
    public static void ConcatenatePdf(string dirname, string? substr, string fileout)
    {
        const string func = "concatenatePdf";
        if (dirname == null)
            throw Lept.Arg(func, "dirname not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");

        Sarray sa = Sarray.GetSortedPathnamesInDirectory(dirname, substr, 0, 0);
        SaConcatenatePdf(sa, fileout);
    }

    // leptonica: saConcatenatePdf (pdfio1.c)
    /// <summary>Concatenates the single-page PDF files named in a string array into one multi-page PDF file.</summary>
    /// <param name="sa">The path names of the single-page PDF files.</param>
    /// <param name="fileout">Path of the concatenated PDF file to write.</param>
    /// <remarks>This only works with PDF files made by this library.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="sa"/> or <paramref name="fileout"/> is <see langword="null"/>.</exception>
    public static void SaConcatenatePdf(Sarray sa, string fileout)
    {
        const string func = "saConcatenatePdf";
        if (sa == null)
            throw Lept.Arg(func, "sa not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");

        byte[] data = SaConcatenatePdfToData(sa);
        Utils.BinaryWrite(fileout, "w", data);
    }

    // leptonica: ptraConcatenatePdf (pdfio1.c)
    /// <summary>Concatenates single-page PDF data held in memory into one multi-page PDF file.</summary>
    /// <param name="pa">The data of each single-page PDF file.</param>
    /// <param name="fileout">Path of the concatenated PDF file to write.</param>
    /// <remarks>This only works with PDF files made by this library.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="pa"/> or <paramref name="fileout"/> is <see langword="null"/>.</exception>
    public static void PtraConcatenatePdf(L_Ptra<L_Bytea> pa, string fileout)
    {
        const string func = "ptraConcatenatePdf";
        if (pa == null)
            throw Lept.Arg(func, "pa not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");

        byte[] data = PtraConcatenateToData(pa, null);
        Utils.BinaryWrite(fileout, "w", data);
    }

    // leptonica: concatenatePdfToData (pdfio1.c)
    /// <summary>Concatenates the single-page PDF files of a directory into multi-page PDF data.</summary>
    /// <param name="dirname">The directory that contains the single-page PDF files.</param>
    /// <param name="substr">A substring that the file names must contain, or <see langword="null"/> to use all files in the directory.</param>
    /// <returns>The concatenated PDF data.</returns>
    /// <remarks>This only works with PDF files made by this library. The files are sorted lexically in increasing order before concatenation.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="dirname"/> is <see langword="null"/>.</exception>
    public static byte[] ConcatenatePdfToData(string dirname, string? substr)
    {
        const string func = "concatenatePdfToData";
        if (dirname == null)
            throw Lept.Arg(func, "dirname not defined");

        Sarray sa = Sarray.GetSortedPathnamesInDirectory(dirname, substr, 0, 0);
        return SaConcatenatePdfToData(sa);
    }

    // leptonica: saConcatenatePdfToData (pdfio1.c)
    /// <summary>Concatenates the single-page PDF files named in a string array into multi-page PDF data.</summary>
    /// <param name="sa">The path names of the single-page PDF files.</param>
    /// <returns>The concatenated PDF data.</returns>
    /// <remarks>This only works with PDF files made by this library.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="sa"/> is <see langword="null"/>.</exception>
    public static byte[] SaConcatenatePdfToData(Sarray sa)
    {
        const string func = "saConcatenatePdfToData";
        if (sa == null)
            throw Lept.Arg(func, "sa not defined");

        int npages = sa.Count;
        if (npages == 0)
            throw Lept.Fail(func, "no filenames found");

        var pa_data = L_Ptra<L_Bytea>.Create(npages);
        for (int i = 0; i < npages; i++) {
            string fname = sa.GetString(i, L.L_NOCOPY);
            L_Bytea bas = L_Bytea.InitFromFile(fname);
            pa_data.Add(bas);
        }

        return PtraConcatenateToData(pa_data, sa);
    }
}

public sealed partial class Pixa
{
    // leptonica: pixaConvertToPdf (pdfio1.c)
    /// <summary>Writes the images of this <see cref="Pixa"/> as a multi-page PDF file.</summary>
    /// <param name="res">Overrides the resolution of each image, in ppi; 0 respects the resolution embedded in the images.</param>
    /// <param name="scalefactor">Scaling factor applied to each image; a value of 0.0 or less is set to 1.0.</param>
    /// <param name="type">Encoding type: <see cref="L.L_JPEG_ENCODE"/>, <see cref="L.L_G4_ENCODE"/>, <see cref="L.L_FLATE_ENCODE"/>, <see cref="L.L_JP2K_ENCODE"/>, or <see cref="L.L_DEFAULT_ENCODE"/> to choose the type for each image.</param>
    /// <param name="quality">For JPEG: 1 to 100, with 0 for the default of 75. For JPEG 2000: 27 to 45, with 0 for the default of 34.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <param name="fileout">Path of the PDF file to write.</param>
    /// <remarks>With the default type, images are encoded with G4 if 1 bpp, with JPEG if 8 bpp without a colormap and with many colors or if 32 bpp, and with Flate otherwise.
    /// Specifying one of the encoding types forces all images to be compressed with that type.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="fileout"/> is <see langword="null"/>.</exception>
    public void ConvertToPdf(int res, float scalefactor, int type, int quality,
                             string? title, string fileout)
    {
        const string func = "pixaConvertToPdf";
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");

        byte[] data = ConvertToPdfData(res, scalefactor, type, quality, title);
        Utils.BinaryWrite(fileout, "w", data);
    }

    // leptonica: pixaConvertToPdfData (pdfio1.c)
    /// <summary>Converts the images of this <see cref="Pixa"/> to multi-page PDF data.</summary>
    /// <param name="res">Input resolution of all images.</param>
    /// <param name="scalefactor">Scaling factor applied to each image; a value of 0.0 or less is set to 1.0.</param>
    /// <param name="type">Encoding type: <see cref="L.L_JPEG_ENCODE"/>, <see cref="L.L_G4_ENCODE"/>, <see cref="L.L_FLATE_ENCODE"/>, <see cref="L.L_JP2K_ENCODE"/>, or <see cref="L.L_DEFAULT_ENCODE"/> to choose the type for each image.</param>
    /// <param name="quality">For JPEG: 1 to 100, with 0 for the default of 75. For JPEG 2000: 27 to 45, with 0 for the default of 34.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <returns>The PDF data for all the images.</returns>
    /// <remarks>See <see cref="ConvertToPdf(int, float, int, int, string, string)"/>.</remarks>
    public byte[] ConvertToPdfData(int res, float scalefactor, int type, int quality, string? title)
    {
        const string func = "pixaConvertToPdfData";
        if (scalefactor <= 0.0f)
            scalefactor = 1.0f;
        if (scalefactor >= 50.0f)
            throw Lept.Arg(func, "scalefactor too large");
        if (type != L.L_DEFAULT_ENCODE && type != L.L_JPEG_ENCODE &&
            type != L.L_G4_ENCODE && type != L.L_FLATE_ENCODE &&
            type != L.L_JP2K_ENCODE) {
            Log.Warning(func, "invalid compression type; using per-page default");
            type = L.L_DEFAULT_ENCODE;
        }
        if (quality < 0 || quality > 100)
            throw Lept.Arg(func, "invalid quality");

        int n = Count;
        var pa_data = L_Ptra<L_Bytea>.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pixs = GetPix(i, L.L_CLONE);
            Pix pix = (scalefactor != 1.0f)
                ? pixs.Scale(scalefactor, scalefactor)
                : Pix.Clone(pixs);
            int scaledres = (int)(res * scalefactor);

            int pagetype = (type != L.L_DEFAULT_ENCODE)
                ? type
                : PdfIo.SelectDefaultPdfEncoding(pix);

            byte[] imdata = pix.ConvertToPdfDataSingle(pagetype, quality, 0, 0, scaledres, title);
            pa_data.Add(L_Bytea.InitFromMem(imdata));
        }

        if (pa_data.ActualCount == 0)
            throw Lept.Fail(func, "no pdf files made");

        return PdfIo.PtraConcatenateToData(pa_data, null);
    }
}

public sealed partial class Pix
{
    private const int PdfDefaultInputRes = 300;

    // leptonica: pixConvertToPdf (pdfio1.c)
    /// <summary>Wraps this image in PDF, either as a single-image page or as one image of several placed on the same page.</summary>
    /// <param name="type">Encoding type: <see cref="L.L_JPEG_ENCODE"/>, <see cref="L.L_G4_ENCODE"/>, <see cref="L.L_FLATE_ENCODE"/> or <see cref="L.L_JP2K_ENCODE"/>.</param>
    /// <param name="quality">For JPEG: 1 to 100, with 0 for the default of 75. For JPEG 2000: 27 to 45, with 0 for the default of 34.</param>
    /// <param name="fileout">Path of the PDF file to write; it is needed only for the last image on the page.</param>
    /// <param name="x">Horizontal location of the lower-left corner of the image, in pixels, relative to the origin at the lower-left corner of the page.</param>
    /// <param name="y">Vertical location of the lower-left corner of the image, in pixels, relative to the origin at the lower-left corner of the page.</param>
    /// <param name="res">Overrides the resolution of the image, in ppi; 0 respects the resolution embedded in the image.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <param name="plpd">The state of the page being composed. It is created on the first call and returned until the last image is processed, when it is released and set to <see langword="null"/>.</param>
    /// <param name="position">The position in the image sequence: <see cref="L.L_FIRST_IMAGE"/>, <see cref="L.L_NEXT_IMAGE"/> or <see cref="L.L_LAST_IMAGE"/>.</param>
    /// <remarks>Data is written to the file only for the last image on the page. If <paramref name="res"/> is 0 and the image has no resolution, a default of 300 ppi is used.
    /// See <see cref="PdfIo.ConvertToPdf(string, int, int, string, int, int, int, string, ref L_Pdf_Data, int)"/>.</remarks>
    /// <exception cref="System.ArgumentException">A file name is required but <paramref name="fileout"/> is <see langword="null"/>.</exception>
    public void ConvertToPdf(int type, int quality, string? fileout, int x, int y,
                             int res, string? title, ref L_Pdf_Data? plpd, int position)
    {
        ConvertToPdfInternal(type, quality, fileout, x, y, res, title,
                             useMultiImage: true, ref plpd, position);
    }

    /// <summary>Wraps this image in a one-page PDF file.</summary>
    /// <param name="type">Encoding type: <see cref="L.L_JPEG_ENCODE"/>, <see cref="L.L_G4_ENCODE"/>, <see cref="L.L_FLATE_ENCODE"/> or <see cref="L.L_JP2K_ENCODE"/>.</param>
    /// <param name="quality">For JPEG: 1 to 100, with 0 for the default of 75. For JPEG 2000: 27 to 45, with 0 for the default of 34.</param>
    /// <param name="fileout">Path of the PDF file to write.</param>
    /// <param name="x">Horizontal location of the lower-left corner of the image, in pixels, relative to the origin at the lower-left corner of the page.</param>
    /// <param name="y">Vertical location of the lower-left corner of the image, in pixels, relative to the origin at the lower-left corner of the page.</param>
    /// <param name="res">Overrides the resolution of the image, in ppi; 0 respects the resolution embedded in the image.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    public void ConvertToPdf(int type, int quality, string fileout, int x, int y,
                             int res, string? title)
    {
        L_Pdf_Data? unused = null;
        ConvertToPdfInternal(type, quality, fileout, x, y, res, title,
                             useMultiImage: false, ref unused, 0);
    }

    internal void ConvertToPdfInternal(int type, int quality, string? fileout, int x, int y,
                                       int res, string? title, bool useMultiImage,
                                       ref L_Pdf_Data? plpd, int position)
    {
        const string func = "pixConvertToPdf";
        if (!useMultiImage || position == L.L_LAST_IMAGE) {
            if (fileout == null)
                throw Lept.Arg(func, "fileout not defined");
        }

        byte[]? data = PdfIo.PixConvertToPdfDataCore(this, type, quality, x, y, res, title,
                                                     useMultiImage, ref plpd, position);
        if (!useMultiImage || position == L.L_LAST_IMAGE)
            Utils.BinaryWrite(fileout!, "w", data!);
    }

    // leptonica: pixWriteStreamPdf (pdfio1.c)
    /// <summary>Writes an image to a stream as a one-page PDF.</summary>
    /// <param name="fp">The stream to write.</param>
    /// <param name="pix">The image to write; any depth is accepted, and a colormap is allowed.</param>
    /// <param name="res">Overrides the resolution of the image, in ppi; 0 respects the resolution embedded in the image.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <remarks>This is the simplest interface for writing a single image as PDF. It uses G4 encoding for 1 bpp, JPEG encoding for 8 bpp without a colormap and for 32 bpp, and Flate encoding for everything else.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="fp"/> or <paramref name="pix"/> is <see langword="null"/>.</exception>
    public static void WriteStreamPdf(Stream fp, Pix pix, int res, string? title)
    {
        const string func = "pixWriteStreamPdf";
        if (fp == null)
            throw Lept.Arg(func, "stream not opened");
        if (pix == null)
            throw Lept.Arg(func, "pix not defined");

        byte[] data = WriteMemPdf(pix, res, title);
        fp.Write(data, 0, data.Length);
    }

    // leptonica: pixWriteMemPdf (pdfio1.c)
    /// <summary>Encodes an image as a one-page PDF.</summary>
    /// <param name="pix">The image to encode; any depth is accepted, and a colormap is allowed.</param>
    /// <param name="res">Overrides the resolution of the image, in ppi; 0 respects the resolution embedded in the image.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <returns>The PDF data.</returns>
    /// <remarks>This is the simplest interface for encoding a single image as PDF. It uses G4 encoding for 1 bpp and chooses between JPEG and Flate encoding for everything else.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="pix"/> is <see langword="null"/>.</exception>
    public static byte[] WriteMemPdf(Pix pix, int res, string? title)
    {
        const string func = "pixWriteMemPdf";
        if (pix == null)
            throw Lept.Arg(func, "pix not defined");

        int type = PdfIo.SelectDefaultPdfEncoding(pix);
        return pix.ConvertToPdfDataSingle(type, 75, 0, 0, res, title);
    }

    // leptonica: pixConvertToPdfSegmented (pdfio1.c)
    /// <summary>Converts this image to a one-page PDF file, compressing the image regions separately from the rest.</summary>
    /// <param name="res">Input image resolution, typically 300 ppi; use 0 for the default.</param>
    /// <param name="type">Compression type for the non-image regions; the image regions are always compressed with <see cref="L.L_JPEG_ENCODE"/>.</param>
    /// <param name="thresh">Threshold used for converting gray to 1 bpp with <see cref="L.L_G4_ENCODE"/>.</param>
    /// <param name="boxa">The image regions, or <see langword="null"/> if there are none.</param>
    /// <param name="quality">JPEG quality for the image regions; 0 for the default.</param>
    /// <param name="scalefactor">Scaling factor for the JPEG regions; it must not exceed 1.0, and 0 means 1.0.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <param name="fileout">Path of the PDF file to write.</param>
    /// <remarks>See <see cref="PdfIo.ConvertToPdfSegmented"/> for details.</remarks>
    public void ConvertToPdfSegmented(int res, int type, int thresh, Boxa? boxa,
                                      int quality, float scalefactor, string? title, string fileout)
    {
        const string func = "pixConvertToPdfSegmented";
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");
        if (type != L.L_G4_ENCODE && type != L.L_JPEG_ENCODE && type != L.L_FLATE_ENCODE)
            throw Lept.Arg(func, "invalid conversion type");
        if (boxa != null && scalefactor > 1.0f) {
            Log.Warning(func, "setting scalefactor to 1.0");
            scalefactor = 1.0f;
        }

        byte[] data = ConvertToPdfDataSegmented(res, type, thresh, boxa, quality, scalefactor, title);
        Utils.BinaryWrite(fileout, "w", data);
    }

    // leptonica: pixConvertToPdfDataSegmented (pdfio1.c)
    /// <summary>Converts this image to one-page PDF data, compressing the image regions separately from the rest.</summary>
    /// <param name="res">Input image resolution, typically 300 ppi; use 0 for the default.</param>
    /// <param name="type">Compression type for the non-image regions; the image regions are always compressed with <see cref="L.L_JPEG_ENCODE"/>.</param>
    /// <param name="thresh">Threshold used for converting gray to 1 bpp with <see cref="L.L_G4_ENCODE"/>.</param>
    /// <param name="boxa">The image regions, or <see langword="null"/> if there are none.</param>
    /// <param name="quality">JPEG quality for the image regions; 0 for the default.</param>
    /// <param name="scalefactor">Scaling factor for the JPEG regions; it must not exceed 1.0, and 0 means 1.0.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <returns>The PDF data.</returns>
    /// <remarks>See <see cref="PdfIo.ConvertToPdfSegmented"/> for details.</remarks>
    public byte[] ConvertToPdfDataSegmented(int res, int type, int thresh, Boxa? boxa,
                                            int quality, float scalefactor, string? title)
    {
        const string func = "pixConvertToPdfDataSegmented";
        if (type != L.L_G4_ENCODE && type != L.L_JPEG_ENCODE && type != L.L_FLATE_ENCODE)
            throw Lept.Arg(func, "invalid conversion type");
        if (boxa != null && (scalefactor <= 0.0f || scalefactor > 1.0f)) {
            Log.Warning(func, "setting scalefactor to 1.0");
            scalefactor = 1.0f;
        }

        if (res <= 0)
            res = PdfDefaultInputRes;
        float scale = (float)((int)(scalefactor * res + 0.5)) / (float)res;
        PixColormap? cmap = Colormap;

            /* Simple case: single image to be encoded */
        if (boxa == null || boxa.Count == 0) {
            if (Depth > 1 && type == L.L_G4_ENCODE) {
                Pix pixt1s = (cmap != null)
                    ? RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE)
                    : ConvertTo8(false);
                Pix pixt2s = pixt1s.ScaleGray2xLIThresh(thresh);
                return pixt2s.ConvertToPdfDataSingle(type, quality, 0, 0, 2 * res, title);
            }
            return ConvertToPdfDataSingle(type, quality, 0, 0, res, title);
        }

        Pix pixt1 = SetBlackOrWhiteBoxa(boxa, L.L_SET_WHITE);  /* non-image */
        int nbox = boxa.Count;
        L_Pdf_Data? lpd = null;
        if (type == L.L_G4_ENCODE) {
            Pix pixt2 = CreateTemplate(this);
            pixt2.SetBlackOrWhite(L.L_SET_WHITE);
            for (int i = 0; i < nbox; i++) {
                Box box = boxa.GetBox(i, L.L_CLONE);
                Pix? pix = ClipRectangle(box, out Box? boxc);
                if (pix != null && boxc != null) {
                    boxc.GetGeometry(out int bx, out int by, out int bw, out int bh);
                    pixt2.Rasterop(bx, by, bw, bh, L.PIX_SRC, pix, 0, 0);
                }
            }
            Pix pixt3 = pixt2.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
            Pix pixt4 = (pixt3.Depth == 1)
                ? pixt3.ScaleToGray(scale)
                : pixt3.Scale(scale, scale);
            pixt4.ConvertToPdfData(L.L_JPEG_ENCODE, quality, 0, 0, (int)(scale * res),
                                   title, ref lpd, L.L_FIRST_IMAGE);

            Pix pixt5;
            int upscale;
            if (pixt1.Depth == 1) {
                pixt5 = Clone(pixt1);
                upscale = 1;
            } else {
                Pix pixt6 = pixt1.ConvertTo8(false);
                pixt5 = pixt6.ScaleGray2xLIThresh(thresh);
                upscale = 2;
            }
            return pixt5.ConvertToPdfData(L.L_G4_ENCODE, quality, 0, 0, upscale * res,
                                          title, ref lpd, L.L_LAST_IMAGE)!;
        } else {
            pixt1.ConvertToPdfData(type, quality, 0, 0, res, title, ref lpd, L.L_FIRST_IMAGE);
            byte[]? data = null;
            for (int i = 0; i < nbox; i++) {
                Box box = boxa.GetBox(i, L.L_CLONE);
                Pix pixt2 = ClipRectangle(box, out Box? boxc)!;
                Pix pixt3 = pixt2.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
                Pix pixt4 = (pixt3.Depth == 1)
                    ? pixt3.ScaleToGray(scale)
                    : pixt3.Scale(scale, scale);
                Box box2 = boxc!.Transform(0, 0, scale, scale);
                box2.GetGeometry(out int bx, out int by, out _, out _);
                int seq = (i == nbox - 1) ? L.L_LAST_IMAGE : L.L_NEXT_IMAGE;
                data = pixt4.ConvertToPdfData(L.L_JPEG_ENCODE, quality, bx, by,
                                              (int)(scale * res), title, ref lpd, seq);
            }
            return data!;
        }
    }
}
