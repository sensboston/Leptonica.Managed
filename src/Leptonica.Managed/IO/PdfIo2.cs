using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;

namespace Leptonica;

public static partial class L
{
        /* Pdf formatted encoding types (L_DEFAULT_ENCODE, L_JPEG_ENCODE,
         * L_G4_ENCODE, L_FLATE_ENCODE are defined in PageSeg/Dewarp4/PixaFunc2/MorphSeq) */
    /// <summary>PDF encoding type: JPEG 2000, for 8 bpp and 32 bpp images without a colormap.</summary>
    public const int L_JP2K_ENCODE = 4;

        /* Pdf multi image flags */
    /// <summary>Position in a sequence of images on one PDF page: the first image.</summary>
    public const int L_FIRST_IMAGE = 1;
    /// <summary>Position in a sequence of images on one PDF page: an intermediate image, neither first nor last.</summary>
    public const int L_NEXT_IMAGE  = 2;
    /// <summary>Position in a sequence of images on one PDF page: the last image.</summary>
    public const int L_LAST_IMAGE  = 3;
}

/// <summary>Compressed image data for PostScript and PDF generation (struct L_Compressed_Data).</summary>
public sealed class L_Compressed_Data
{
    /// <summary>Gets or sets the encoding type, such as <see cref="L.L_JPEG_ENCODE"/>.</summary>
    public int Type { get; set; }
    /// <summary>Gets or sets the compressed (for example, gzipped) raster data.</summary>
    public byte[]? DataComp { get; set; }
    /// <summary>Gets or sets the number of compressed bytes.</summary>
    public int NbytesComp { get; set; }
    /// <summary>Gets or sets the ASCII85-encoded compressed raster data.</summary>
    public string? Data85 { get; set; }
    /// <summary>Gets or sets the number of ASCII85-encoded bytes.</summary>
    public int Nbytes85 { get; set; }
    /// <summary>Gets or sets the ASCII85-encoded uncompressed colormap.</summary>
    public string? CmapData85 { get; set; }
    /// <summary>Gets or sets the colormap as a hexadecimal PDF array.</summary>
    public string? CmapDataHex { get; set; }
    /// <summary>Gets or sets the number of colors in the colormap.</summary>
    public int Ncolors { get; set; }
    /// <summary>Gets or sets the image width in pixels.</summary>
    public int W { get; set; }
    /// <summary>Gets or sets the image height in pixels.</summary>
    public int H { get; set; }
    /// <summary>Gets or sets the number of bits per sample; typically 1, 2, 4 or 8.</summary>
    public int Bps { get; set; }
    /// <summary>Gets or sets the number of samples per pixel; typically 1 or 3.</summary>
    public int Spp { get; set; }
    /// <summary>Gets or sets the TIFF G4 photometry: nonzero if 0 means black.</summary>
    public int MinisBlack { get; set; }
    /// <summary>Gets or sets a value that is nonzero if the Flate data has PNG predictors.</summary>
    public int Predictor { get; set; }
    /// <summary>Gets or sets the number of uncompressed raster bytes.</summary>
    public int Nbytes { get; set; }
    /// <summary>Gets or sets the resolution in ppi.</summary>
    public int Res { get; set; }
}

/// <summary>Intermediate single-page PDF generation state (struct L_Pdf_Data).</summary>
public sealed class L_Pdf_Data
{
    /// <summary>Gets or sets the optional PDF title.</summary>
    public string? Title { get; set; }
    /// <summary>Gets or sets the number of images.</summary>
    public int N { get; set; }
    /// <summary>Gets or sets the number of colormaps.</summary>
    public int Ncmap { get; set; }
    /// <summary>Gets or sets the array of compressed image data.</summary>
    public L_Ptra<L_Compressed_Data> Cida { get; set; } = null!;
    /// <summary>Gets or sets the PDF identification string, such as <c>%PDF-1.2</c>.</summary>
    public string? Id { get; set; }
    /// <summary>Gets or sets the catalog string.</summary>
    public string? Obj1 { get; set; }
    /// <summary>Gets or sets the metadata string.</summary>
    public string? Obj2 { get; set; }
    /// <summary>Gets or sets the pages string.</summary>
    public string? Obj3 { get; set; }
    /// <summary>Gets or sets the page string, which holds variable data.</summary>
    public string? Obj4 { get; set; }
    /// <summary>Gets or sets the content string, which holds variable data.</summary>
    public string? Obj5 { get; set; }
    /// <summary>Gets or sets the string written after the binary stream.</summary>
    public string? PostStream { get; set; }
    /// <summary>Gets or sets the trailer string, which holds variable data.</summary>
    public string? Trailer { get; set; }
    /// <summary>Gets or sets the points that store the (x, y) location of each image, in points.</summary>
    public Pta Xy { get; set; } = null!;
    /// <summary>Gets or sets the points that store the (width, height) of each image, in points.</summary>
    public Pta Wh { get; set; } = null!;
    /// <summary>Gets or sets the bounding region for all the images.</summary>
    public Box? Mediabox { get; set; }
    /// <summary>Gets or sets the strings for the objects that come before the binary stream.</summary>
    public Sarray Saprex { get; set; } = null!;
    /// <summary>Gets or sets the PDF object strings for the colormaps.</summary>
    public Sarray Sacmap { get; set; } = null!;
    /// <summary>Gets or sets the sizes of each PDF string object.</summary>
    public L_Dna Objsize { get; set; } = null!;
    /// <summary>Gets or sets the location of each PDF string object.</summary>
    public L_Dna Objloc { get; set; } = null!;
    /// <summary>Gets or sets the location of the cross-reference table.</summary>
    public int Xrefloc { get; set; }
}

/*
 *  pdfio2.c
 *
 *    Lower-level operations for generating pdf.
 *
 *     Intermediate function for single page, multi-image conversion
 *          Pix.ConvertToPdfData (pixConvertToPdfData)
 *
 *     Intermediate function for generating multipage pdf output
 *          PdfIo.PtraConcatenateToData (ptraConcatenatePdfToData)
 *
 *     Convert tiff multipage to pdf file
 *          PdfIo.ConvertTiffMultipageToPdf (convertTiffMultipageToPdf)
 *
 *     Generates the CID, transcoding under some conditions
 *          PdfIo.GenerateCIDataForPdf (l_generateCIDataForPdf)
 *          PdfIo.GenerateCIData (l_generateCIData)
 *
 *     Lower-level CID generation without transcoding
 *          PdfIo.GenerateFlateDataPdf (l_generateFlateDataPdf)
 *          PdfIo.GenerateJpegData (l_generateJpegData)
 *          PdfIo.GenerateJpegDataMem (l_generateJpegDataMem)
 *          PdfIo.GenerateJp2kData (l_generateJp2kData)
 *          PdfIo.GenerateG4Data (l_generateG4Data)
 *
 *     Lower-level CID generation with transcoding
 *          Pix.GenerateCIData (pixGenerateCIData)
 *          PdfIo.GenerateFlateData (l_generateFlateData)
 *          PixGenerateFlateData, PixGenerateJpegData, PixGenerateJp2kData, PixGenerateG4Data
 *
 *     Other CID operations
 *          PdfIo.CidConvertToPdfData (cidConvertToPdfData)
 *          PdfIo.CIDataDestroy (l_CIDataDestroy)
 *
 *     Find number of pages, sizes, mediaboxes, and renderer resolution in a pdf
 *          PdfIo.GetPageCount (getPdfPageCount)
 *          PdfIo.GetPageSizes (getPdfPageSizes)
 *          PdfIo.GetMediaBoxSizes (getPdfMediaBoxSizes)
 *          PdfIo.GetRendererResolution (getPdfRendererResolution)
 *
 *     Set flags for special modes
 *          PdfIo.SetG4ImageMask (l_pdfSetG4ImageMask)
 *          PdfIo.SetDateAndVersion (l_pdfSetDateAndVersion)
 */
public sealed partial class Pix
{
    // leptonica: pixConvertToPdfData (pdfio2.c)
    /// <summary>Converts a Pix to PDF byte data (single image or part of a multi-image page).</summary>
    public byte[]? ConvertToPdfData(int type, int quality, int x, int y, int res,
                                    string? title, ref L_Pdf_Data? plpd, int position)
    {
        return PdfIo.PixConvertToPdfDataCore(this, type, quality, x, y, res, title,
                                             useMultiImage: true, ref plpd, position);
    }

    internal byte[] ConvertToPdfDataSingle(int type, int quality, int x, int y, int res, string? title)
    {
        L_Pdf_Data? unused = null;
        return PdfIo.PixConvertToPdfDataCore(this, type, quality, x, y, res, title,
                                             useMultiImage: false, ref unused, 0)!;
    }

    // leptonica: pixGenerateCIData (pdfio2.c)
    /// <summary>Generates compressed image data (CID) for a Pix.</summary>
    public L_Compressed_Data GenerateCIData(int type, int quality, int ascii85)
    {
        return PdfIo.PixGenerateCIDataCore(this, type, quality, ascii85);
    }
}

public static partial class PdfIo
{
    private const int DefaultInputRes = 300;

        /* Thread-local inverted flags so default on every thread is 1 (true) */
    [ThreadStatic]
    private static bool omitG4ImageMask;

    [ThreadStatic]
    private static bool omitDateAndVersion;

    internal static bool WriteG4ImageMask => !omitG4ImageMask;
    internal static bool WriteDateAndVersion => !omitDateAndVersion;

    private static ReadOnlySpan<byte> PdfHeaderSig => "%PDF-1."u8;
    private static ReadOnlySpan<byte> StartXrefSeq => "startxref\n"u8;
    private static ReadOnlySpan<byte> ObjZeroSeq => " 0 obj\n"u8;
    private static ReadOnlySpan<byte> XrefSeq => "xref"u8;
    private static ReadOnlySpan<byte> SpaceByte => " "u8;
    private static ReadOnlySpan<byte> RefSeq => " 0 R"u8;
    private static ReadOnlySpan<byte> CountTag => "/Count"u8;
    private static ReadOnlySpan<byte> WidthTag => "/Width"u8;
    private static ReadOnlySpan<byte> HeightTag => "/Height"u8;
    private static ReadOnlySpan<byte> MediaBoxTag => "/MediaBox"u8;
    private static ReadOnlySpan<byte> PngIdatTag => "IDAT"u8;
    private static ReadOnlySpan<byte> PngPlteTag => "PLTE"u8;

    /*---------------------------------------------------------------------*
     *       Intermediate function for single page, multi-image pdf        *
     *---------------------------------------------------------------------*/
    internal static byte[]? PixConvertToPdfDataCore(Pix pix, int type, int quality,
                                                    int x, int y, int res, string? title,
                                                    bool useMultiImage, ref L_Pdf_Data? plpd,
                                                    int position)
    {
        const string func = "pixConvertToPdfData";
        if (pix == null)
            throw Lept.Arg(func, "pix not defined");
        if (type != L.L_JPEG_ENCODE && type != L.L_G4_ENCODE &&
            type != L.L_FLATE_ENCODE && type != L.L_JP2K_ENCODE) {
            type = SelectDefaultPdfEncoding(pix);
        }
        if (quality < 0 || quality > 100)
            throw Lept.Arg(func, "invalid quality");

        if (useMultiImage && position == L.L_FIRST_IMAGE)
            plpd = null;

            /* Generate the compressed image data.  It must NOT
             * be ascii85 encoded. */
        L_Compressed_Data cid = PixGenerateCIDataCore(pix, type, quality, 0);

            /* Get media box in pts. */
        int pixres = cid.Res;
        int w = cid.W;
        int h = cid.H;
        if (res <= 0)
            res = (pixres > 0) ? pixres : DefaultInputRes;
        float xpt = x * 72.0f / res;
        float ypt = y * 72.0f / res;
        float wpt = w * 72.0f / res;
        float hpt = h * 72.0f / res;

            /* Set up lpd */
        L_Pdf_Data lpd;
        if (!useMultiImage) {  /* single image */
            lpd = PdfdataCreate(title);
        } else if (position == L.L_FIRST_IMAGE) {  /* first of multiple images */
            lpd = PdfdataCreate(title);
            plpd = lpd;
        } else {  /* not the first of multiple images */
            if (plpd == null)
                throw Lept.Fail(func, "lpd not defined for continuation image");
            lpd = plpd;
        }

            /* Add the data to the lpd */
        lpd.Cida.Add(cid);
        lpd.N++;
        lpd.Xy.AddPt(xpt, ypt);
        lpd.Wh.AddPt(wpt, hpt);

            /* If a single image or the last of multiple images,
             * generate the pdf and destroy the lpd */
        if (!useMultiImage || position == L.L_LAST_IMAGE) {
            byte[] data = GeneratePdf(lpd);
            PdfdataDestroy(ref lpd!);
            if (useMultiImage)
                plpd = null;
            return data;
        }

        return null;
    }

    /*---------------------------------------------------------------------*
     *      Intermediate function for generating multipage pdf output      *
     *---------------------------------------------------------------------*/
    // leptonica: ptraConcatenatePdfToData (pdfio2.c)
    /// <summary>Concatenates single-page Leptonica PDF byte arrays into a multi-page PDF.</summary>
    public static byte[] PtraConcatenateToData(L_Ptra<L_Bytea> pa_data, Sarray? sa)
    {
        const string func = "ptraConcatenatePdfToData";
        if (pa_data == null)
            throw Lept.Arg(func, "pa_data not defined");

            /* Parse the files and find the object locations.
             * Remove file data that cannot be parsed. */
        int npages = pa_data.ActualCount;
        L_Dnaa daa_locs = L_Dnaa.Create(npages);
        for (int i = 0; i < npages; i++) {
            L_Bytea? bas = pa_data.GetPtrToItem(i);
            if (bas == null || !ParseTrailerPdf(bas, out L_Dna? da_locs)) {
                pa_data.Remove(i, L.L_NO_COMPACTION);
                if (sa != null) {
                    string fname = sa.GetString(i, L.L_NOCOPY);
                    Log.Error(func, $"can't parse file {fname}; skipping");
                } else {
                    Log.Error(func, $"can't parse file {i}; skipping");
                }
            } else {
                daa_locs.AddDna(da_locs!, L.L_INSERT);
            }
        }

            /* Recompute npages in case some of the files were not pdf */
        pa_data.CompactArray();
        npages = pa_data.ActualCount;
        if (npages == 0)
            throw Lept.Fail(func, "no parsable pdf files found");

            /* Find the mapping from initial to final object numbers */
        Numaa naa_objs = Numaa.Create(npages);  /* stores final object numbers */
        Numa napage = Numa.Create(npages);      /* stores "Page" object numbers */
        int index = 0;
        for (int i = 0; i < npages; i++) {
            L_Dna da = daa_locs.GetDna(i, L.L_CLONE);
            int nobj = da.Count;
            Numa na_objs;
            if (i == 0) {
                napage.AddNumber(4);  /* object 4 on first page */
                na_objs = Numa.MakeSequence(0.0f, 1.0f, nobj - 1);
                index = nobj - 1;
            } else {  /* skip the first 3 objects in each file */
                napage.AddNumber(index);  /* Page object is first we add */
                na_objs = Numa.MakeConstant(0.0f, nobj - 1);
                na_objs.ReplaceNumber(3, 3);  /* refers to parent of all */
                for (int j = 4; j < nobj - 1; j++)
                    na_objs.SetValue(j, index++);
            }
            naa_objs.AddNuma(na_objs, L.L_INSERT);
        }

            /* Make the Pages object (#3) */
        string str_pages = GeneratePagesObjStringPdf(napage);

            /* Build the output */
        L_Bytea bad = L_Bytea.Create(5000);
        L_Dna da_outlocs = L_Dna.Create(0);  /* locations of all output objects */
        for (int i = 0; i < npages; i++) {
            L_Bytea bas = pa_data.GetPtrToItem(i)!;
            byte[] pdfdata = bas.GetData(out int _);
            L_Dna da_locs = daa_locs.GetDna(i, L.L_CLONE);
            Numa na_objs = naa_objs.GetNuma(i, L.L_CLONE);
            int nobj = da_locs.Count - 1;
            L_Dna da_sizes = da_locs.DiffAdjValues();
            int[] sizes = da_sizes.GetIArray();
            int[] locs = da_locs.GetIArray();
            if (i == 0) {
                bad.AppendData(pdfdata.AsSpan(0, sizes[0]));
                bad.AppendData(pdfdata.AsSpan(locs[1], sizes[1]));
                bad.AppendData(pdfdata.AsSpan(locs[2], sizes[2]));
                bad.AppendData(Encoding.Latin1.GetBytes(str_pages));
                for (int j = 0; j < 4; j++)
                    da_outlocs.AddNumber(locs[j]);
            }
            for (int j = 4; j < nobj; j++) {
                da_outlocs.AddNumber(bad.Size);
                L_Bytea bat1 = L_Bytea.InitFromMem(pdfdata.AsSpan(locs[j], sizes[j]));
                L_Bytea bat2 = SubstituteObjectNumbers(bat1, na_objs);
                byte[] data = bat2.GetData(out int sz);
                bad.AppendData(data.AsSpan(0, sz));
            }
            if (i == npages - 1)  /* last one */
                da_outlocs.AddNumber(bad.Size);
        }

            /* Add the trailer */
        string str_trailer = MakeTrailerStringPdf(da_outlocs);
        bad.AppendData(Encoding.Latin1.GetBytes(str_trailer));

            /* Transfer the output data (exact length, without L_Bytea.CopyData's +4 zero padding) */
        byte[] outraw = bad.GetData(out int outsize);
        return outraw.AsSpan(0, outsize).ToArray();
    }

    /*---------------------------------------------------------------------*
     *                  Convert tiff multipage to pdf file                 *
     *---------------------------------------------------------------------*/
    // leptonica: convertTiffMultipageToPdf (pdfio2.c)
    /// <summary>Converts a multipage TIFF file to a PDF file.</summary>
    /// <param name="filein">The input TIFF file.</param>
    /// <param name="fileout">Path of the PDF file to write.</param>
    /// <remarks>A multipage TIFF file can also be converted to PostScript with <see cref="PsIo.ConvertTiffMultipageToPS"/>.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="filein"/> or <paramref name="fileout"/> is <see langword="null"/>.</exception>
    public static void ConvertTiffMultipageToPdf(string filein, string fileout)
    {
        const string func = "convertTiffMultipageToPdf";
        if (filein == null)
            throw Lept.Arg(func, "filein not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");

        using (FileStream fp = Utils.FopenReadStream(filein)) {
            if (!Pix.FileFormatIsTiff(fp))
                throw Lept.Fail(func, $"file not tiff format: {filein}");
        }

        Pixa pixa = Pixa.ReadMultipageTiff(filein);
        pixa.ConvertToPdf(0, 1.0f, 0, 0, "weasel2", fileout);
    }

    /*---------------------------------------------------------------------*
     *                          CID-based operations                       *
     *---------------------------------------------------------------------*/
    // leptonica: l_generateCIDataForPdf (pdfio2.c)
    /// <summary>Generates compressed image data that is compatible with PDF, preferably without transcoding.</summary>
    /// <param name="fname">Path of the image file, or <see langword="null"/> if <paramref name="pix"/> is given.</param>
    /// <param name="pix">The raster of the image, or <see langword="null"/>. It saves work when transcoding is needed and the caller already has the image.</param>
    /// <param name="quality">For JPEG if transcoded: 1 to 100, with 0 for the default of 75. For JPEG 2000 if transcoded: 27 to 45, with 0 for the default of 34.</param>
    /// <returns>The compressed image data.</returns>
    /// <remarks>
    /// <para>Either <paramref name="fname"/> or <paramref name="pix"/> must be set. Files named <c>stdin</c> or <c>-</c> are not opened.</para>
    /// <para>G4 TIFF data must be transcoded to handle the byte order and perhaps the photometry (for example, min-is-black). For a multipage TIFF
    /// file, data is extracted only from the first page, so this function should not be used for such files.</para>
    /// </remarks>
    /// <exception cref="System.ArgumentException">Both <paramref name="fname"/> and <paramref name="pix"/> are <see langword="null"/>.</exception>
    public static L_Compressed_Data GenerateCIDataForPdf(string? fname, Pix? pix, int quality)
    {
        const string func = "l_generateCIDataForPdf";
        if (fname == null && pix == null)
            throw Lept.Arg(func, "neither fname nor pix are defined");

        L_Compressed_Data? cid = null;
        if (fname != null && fname != "-" && fname != "stdin") {
            Pix.FindFileFormat(fname, out int format);
            if (format == L.IFF_UNKNOWN)
                Log.Warning(func, $"file {fname} format is unknown");
            if (format == L.IFF_PS || format == L.IFF_LPDF) {
                Log.Error(func, $"file {fname} is unsupported format {format}");
                throw Lept.Fail(func, "unsupported format");
            }
            if (format == L.IFF_JFIF_JPEG) {
                cid = GenerateJpegData(fname, 0);
            } else if (format == L.IFF_JP2) {
                cid = GenerateJp2kData(fname);
            } else if (format == L.IFF_PNG) {
                cid = GenerateFlateDataPdf(fname, pix);
            }
        }

            /* Otherwise, use the pix to generate the pdf output */
        if (cid == null) {
            Pix pixt = (pix == null) ? Pix.Read(fname!) : Pix.Clone(pix);
            int type = SelectDefaultPdfEncoding(pixt);
            cid = PixGenerateCIDataCore(pixt, type, quality, 0);
        }
        return cid;
    }

    // leptonica: l_generateCIData (pdfio2.c)
    /// <summary>Generates compressed image data of a requested type, for PostScript or PDF.</summary>
    /// <param name="fname">Path of the image file.</param>
    /// <param name="type">The encoding: <see cref="L.L_G4_ENCODE"/>, <see cref="L.L_JPEG_ENCODE"/>, <see cref="L.L_FLATE_ENCODE"/> or <see cref="L.L_JP2K_ENCODE"/>.</param>
    /// <param name="quality">For JPEG if transcoded: 1 to 100, with 0 for the default of 75. For JPEG 2000 if transcoded: 27 to 45, with 0 for the default of 34.</param>
    /// <param name="ascii85">0 for binary data, which is for PDF only; 1 for ASCII85-encoded data, which is for PostScript only.</param>
    /// <returns>The compressed image data.</returns>
    /// <remarks>This tries to compress according to the requested type and falls back to ordinary Flate encoding if that cannot be done. It differs from
    /// <see cref="GenerateCIDataForPdf"/>, which determines the file format and works for PDF only.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="fname"/> is <see langword="null"/>.</exception>
    public static L_Compressed_Data GenerateCIData(string fname, int type, int quality, int ascii85)
    {
        const string func = "l_generateCIData";
        if (fname == null)
            throw Lept.Arg(func, "fname not defined");
        if (type != L.L_G4_ENCODE && type != L.L_JPEG_ENCODE &&
            type != L.L_FLATE_ENCODE && type != L.L_JP2K_ENCODE)
            throw Lept.Arg(func, "invalid conversion type");
        if (ascii85 != 0 && ascii85 != 1)
            throw Lept.Arg(func, "invalid ascii85");

            /* Sanity check on requested encoding */
        Pix.ReadHeader(fname, out int format, out _, out _, out int bps, out int spp, out bool iscmap);
        int d = bps * spp;
        if (d == 24)
            d = 32;
        if (iscmap && type != L.L_FLATE_ENCODE) {
            Log.Warning(func, "pixs has cmap; using flate encoding");
            type = L.L_FLATE_ENCODE;
        } else if (d < 8 && type == L.L_JPEG_ENCODE) {
            Log.Warning(func, "pixs has < 8 bpp; using flate encoding");
            type = L.L_FLATE_ENCODE;
        } else if (d < 8 && type == L.L_JP2K_ENCODE) {
            Log.Warning(func, "pixs has < 8 bpp; using flate encoding");
            type = L.L_FLATE_ENCODE;
        } else if (d > 1 && type == L.L_G4_ENCODE) {
            Log.Warning(func, "pixs has > 1 bpp; using flate encoding");
            type = L.L_FLATE_ENCODE;
        }

        if (type == L.L_JPEG_ENCODE) {
            if (format == L.IFF_JFIF_JPEG)
                return GenerateJpegData(fname, ascii85);
            Pix pix = Pix.Read(fname);
            return PixGenerateJpegData(pix, ascii85, quality);
        } else if (type == L.L_JP2K_ENCODE) {
            if (format == L.IFF_JP2)
                return GenerateJp2kData(fname);
            Pix pix = Pix.Read(fname);
            return PixGenerateJp2kData(pix, quality);
        } else if (type == L.L_G4_ENCODE) {
            Pix pix = Pix.Read(fname);
            return PixGenerateG4Data(pix, ascii85);
        } else {
            return GenerateFlateData(fname, ascii85);
        }
    }

    /*---------------------------------------------------------------------*
     *                     Low-level CID-based operations                  *
     *---------------------------------------------------------------------*/
    // leptonica: l_generateFlateDataPdf (pdfio2.c)
    /// <summary>Generates Flate-compressed image data for PDF, using the PNG data of a file where possible.</summary>
    /// <param name="fname">Path of the image file, preferably PNG.</param>
    /// <param name="pixs">The raster of the image, or <see langword="null"/>. It avoids reading the file when transcoding is needed.</param>
    /// <returns>The compressed image data.</returns>
    /// <remarks>If the file is PNG, the PNG predictors are embedded in the Flate data. The exceptions are interlaced PNG files and RGBA files, which are transcoded.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="fname"/> is <see langword="null"/>.</exception>
    public static L_Compressed_Data GenerateFlateDataPdf(string fname, Pix? pixs)
    {
        const string func = "l_generateFlateDataPdf";
        if (fname == null)
            throw Lept.Arg(func, "fname not defined");

        Pix.FindFileFormat(fname, out int format);
        int spp = 0;
        bool interlaced = false;
        int bps = 0;
        if (format == L.IFF_PNG) {
            interlaced = Pix.IsPngInterlaced(fname);
            Pix.ReadHeaderPng(fname, out _, out _, out bps, out spp, out _);
        }

        if (format != L.IFF_PNG ||
            (format == L.IFF_PNG && (interlaced || bps == 1 || spp == 4 || spp == 2))) {
            Pix pix = (pixs == null) ? Pix.Read(fname) : Pix.Clone(pixs);
            return PixGenerateFlateData(pix, 0);
        }

            /* It's png.  Generate the pdf data without transcoding. */
        int w, h, cmapflag, xres, yres;
        using (FileStream fp = Utils.FopenReadStream(fname)) {
            Pix.FreadHeaderPng(fp, out w, out h, out bps, out spp, out bool iscmap);
            cmapflag = iscmap ? 1 : 0;
        }
        using (FileStream fp = Utils.FopenReadStream(fname)) {
            Pix.FgetPngResolution(fp, out xres, out yres);
        }

            /* We get pdf corruption when inlining the data from 16 bpp png. */
        if (bps == 16)
            return GenerateFlateData(fname, 0);

        byte[] pngcomp = Utils.BinaryRead(fname);
        int nbytespng = pngcomp.Length;
        byte[] datacomp = new byte[nbytespng];
        int nbytescomp = 0;
        PixColormap? cmap = null;

        for (uint i = 16; i < (uint)nbytespng; i += 12) {
            uint n = BinaryPrimitives.ReadUInt32BigEndian(pngcomp.AsSpan((int)i - 8, 4));
            if (n >= (uint)nbytespng - i) {
                Log.Error(func, $"invalid png: i = {i}, n = {n}, nbytes = {nbytespng}");
                throw Lept.Fail(func, "invalid png");
            }

                /* Is it a data chunk? */
            if (pngcomp.AsSpan((int)i - 4, 4).SequenceEqual(PngIdatTag)) {
                Buffer.BlockCopy(pngcomp, (int)i, datacomp, nbytescomp, (int)n);
                nbytescomp += (int)n;
            }

                /* Is it a palette chunk? */
            if (cmapflag != 0 && cmap == null &&
                pngcomp.AsSpan((int)i - 4, 4).SequenceEqual(PngPlteTag)) {
                if ((n / 3) > (uint)(1 << bps)) {
                    Log.Error(func, $"invalid png: i = {i}, n = {n}, cmapsize = {1 << bps}");
                    throw Lept.Fail(func, "invalid png palette");
                }
                cmap = PixColormap.Create(bps);
                for (uint j = i; j < i + n; j += 3)
                    cmap.AddColor(pngcomp[j], pngcomp[j + 1], pngcomp[j + 2]);
            }
            i += n;
        }

        if (nbytescomp == 0)
            throw Lept.Fail(func, "invalid PNG file");

        string? cmapdatahex = null;
        int ncolors = 0;
        if (cmap != null) {
            byte[] cmapdata = cmap.SerializeToMemory(3, out ncolors);
            cmapdatahex = PixColormap.ConvertToHex(cmapdata, ncolors);
        }

        byte[] trimmedComp = new byte[nbytescomp];
        Buffer.BlockCopy(datacomp, 0, trimmedComp, 0, nbytescomp);

        return new L_Compressed_Data {
            DataComp = trimmedComp,
            Type = L.L_FLATE_ENCODE,
            CmapDataHex = cmapdatahex,
            NbytesComp = nbytescomp,
            Ncolors = ncolors,
            Predictor = 1,
            W = w,
            H = h,
            Bps = bps,
            Spp = spp,
            Res = xres
        };
    }

    // leptonica: l_generateJpegData (pdfio2.c)
    /// <summary>Generates compressed image data from a JPEG file, using the JPEG data as it is.</summary>
    /// <param name="fname">Path of the JPEG file.</param>
    /// <param name="ascii85flag">0 for binary data, which is for PDF only; 1 for ASCII85-encoded data, which is for PostScript only.</param>
    /// <returns>The compressed image data.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="fname"/> is <see langword="null"/>.</exception>
    public static L_Compressed_Data GenerateJpegData(string fname, int ascii85flag)
    {
        const string func = "l_generateJpegData";
        if (fname == null)
            throw Lept.Arg(func, "fname not defined");
        if (ascii85flag != 0 && ascii85flag != 1)
            throw Lept.Arg(func, "wrong ascii85flags");

        Pix.ReadHeaderJpeg(fname, out int w, out int h, out int spp, out _, out _);
        int bps = 8;
        int xres;
        using (FileStream fp = Utils.FopenReadStream(fname)) {
            Pix.FgetJpegResolution(fp, out xres, out _);
        }

        byte[] data = Utils.BinaryRead(fname);
        string? data85 = null;
        int nbytes85 = 0;
        if (ascii85flag == 1) {
            string encoded = LeptEncoding.EncodeAscii85(data);
            nbytes85 = encoded.Length;
            data85 = encoded.EndsWith('\n') ? encoded.Substring(0, encoded.Length - 1) : encoded;
        }

        return new L_Compressed_Data {
            DataComp = (ascii85flag == 0) ? data : null,
            Data85 = (ascii85flag == 1) ? data85 : null,
            Nbytes85 = (ascii85flag == 1) ? nbytes85 : 0,
            Type = L.L_JPEG_ENCODE,
            NbytesComp = data.Length,
            W = w,
            H = h,
            Bps = bps,
            Spp = spp,
            Res = xres
        };
    }

    // leptonica: l_generateJpegDataMem (pdfio2.c)
    /// <summary>Generates compressed image data from JPEG data in memory, using the JPEG data as it is.</summary>
    /// <param name="data">The JPEG-encoded data.</param>
    /// <param name="ascii85flag">0 for binary data, which is for PDF only; 1 for ASCII85-encoded data, which is for PostScript only.</param>
    /// <returns>The compressed image data.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="data"/> is empty.</exception>
    public static L_Compressed_Data GenerateJpegDataMem(ReadOnlySpan<byte> data, int ascii85flag)
    {
        const string func = "l_generateJpegDataMem";
        if (data.IsEmpty)
            throw Lept.Arg(func, "data not defined");

        Pix.ReadHeaderMemJpeg(data, out int w, out int h, out int spp, out _, out _);
        int bps = 8;
        Pix.ReadResolutionMemJpeg(data, out int xres, out _);

        string? data85 = null;
        int nbytes85 = 0;
        byte[]? dataCopy = null;
        if (ascii85flag == 1) {
            string encoded = LeptEncoding.EncodeAscii85(data);
            nbytes85 = encoded.Length;
            data85 = encoded.EndsWith('\n') ? encoded.Substring(0, encoded.Length - 1) : encoded;
        } else {
            dataCopy = data.ToArray();
        }

        return new L_Compressed_Data {
            DataComp = dataCopy,
            Data85 = data85,
            Nbytes85 = nbytes85,
            Type = L.L_JPEG_ENCODE,
            NbytesComp = data.Length,
            W = w,
            H = h,
            Bps = bps,
            Spp = spp,
            Res = xres
        };
    }

    // leptonica: l_generateJp2kData (pdfio2.c)
    private static L_Compressed_Data GenerateJp2kData(string fname)
    {
        const string func = "l_generateJp2kData";
        if (fname == null)
            throw Lept.Arg(func, "fname not defined");

        Pix.ReadHeaderJp2k(fname, out int w, out int h, out int bps, out int spp, out _);
        byte[] datacomp = Utils.BinaryRead(fname);

        int xres = 0;
        using (FileStream fp = Utils.FopenReadStream(fname)) {
            Pix.FgetJp2kResolution(fp, out xres, out _);
        }

        return new L_Compressed_Data {
            DataComp = datacomp,
            Type = L.L_JP2K_ENCODE,
            NbytesComp = datacomp.Length,
            W = w,
            H = h,
            Bps = bps,
            Spp = spp,
            Res = xres
        };
    }

    // leptonica: l_generateG4Data (pdfio2.c)
    /// <summary>Generates compressed image data from a G4 (CCITT Group 4) TIFF file.</summary>
    /// <param name="fname">Path of the G4-compressed TIFF file.</param>
    /// <param name="ascii85flag">0 for binary data, which is for PDF only; 1 for ASCII85-encoded data, which is for PostScript only.</param>
    /// <returns>The compressed image data.</returns>
    /// <remarks>This does not work for multipage TIFF files.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="fname"/> is <see langword="null"/>.</exception>
    public static L_Compressed_Data GenerateG4Data(string fname, int ascii85flag)
    {
        const string func = "l_generateG4Data";
        if (fname == null)
            throw Lept.Arg(func, "fname not defined");

        int npages;
        using (FileStream fp = Utils.FopenReadStream(fname)) {
            Pix.TiffGetCount(fp, out npages);
        }
        if (npages != 1) {
            Log.Error(func, $" {npages} page tiff; only works with 1 page (file: {fname})");
            throw Lept.Fail(func, "only works with 1 page tiff");
        }

        int xres;
        using (FileStream fp = Utils.FopenReadStream(fname)) {
            Pix.GetTiffResolution(fp, out xres, out _);
        }

        byte[] datacomp = Pix.ExtractG4DataFromFile(fname, out int w, out int h, out int minisblack);
        string? data85 = null;
        int nbytes85 = 0;
        if (ascii85flag == 1) {
            string encoded = LeptEncoding.EncodeAscii85(datacomp);
            nbytes85 = encoded.Length;
            data85 = encoded.EndsWith('\n') ? encoded.Substring(0, encoded.Length - 1) : encoded;
        }

        return new L_Compressed_Data {
            DataComp = (ascii85flag == 0) ? datacomp : null,
            Data85 = (ascii85flag == 1) ? data85 : null,
            Nbytes85 = (ascii85flag == 1) ? nbytes85 : 0,
            Type = L.L_G4_ENCODE,
            NbytesComp = datacomp.Length,
            W = w,
            H = h,
            Bps = 1,
            Spp = 1,
            MinisBlack = minisblack,
            Res = xres
        };
    }

    internal static L_Compressed_Data PixGenerateCIDataCore(Pix pixs, int type, int quality, int ascii85)
    {
        const string func = "pixGenerateCIData";
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");
        if (type != L.L_G4_ENCODE && type != L.L_JPEG_ENCODE &&
            type != L.L_FLATE_ENCODE && type != L.L_JP2K_ENCODE) {
            type = SelectDefaultPdfEncoding(pixs);
        }
        if (ascii85 != 0 && ascii85 != 1)
            throw Lept.Arg(func, "invalid ascii85");
        pixs.GetDimensions(out int w, out int h, out _);
        if (w == 0 || h == 0)
            throw Lept.Arg(func, "invalid w or h");
        int maxAsp = Math.Max(w / h, h / w);
        if (maxAsp > 50)
            throw Lept.Fail(func, "max asperity > 50");

            /* Sanity check on requested encoding */
        int d = pixs.Depth;
        PixColormap? cmap = pixs.Colormap;
        if (cmap != null && type != L.L_FLATE_ENCODE) {
            Log.Warning(func, "pixs has cmap; using flate encoding");
            type = L.L_FLATE_ENCODE;
        } else if (d < 8 && (type == L.L_JPEG_ENCODE || type == L.L_JP2K_ENCODE)) {
            Log.Warning(func, "pixs has < 8 bpp; using flate encoding");
            type = L.L_FLATE_ENCODE;
        } else if (d > 1 && type == L.L_G4_ENCODE) {
            Log.Warning(func, "pixs has > 1 bpp; using flate encoding");
            type = L.L_FLATE_ENCODE;
        }

        if (type == L.L_JPEG_ENCODE)
            return PixGenerateJpegData(pixs, ascii85, quality);
        if (type == L.L_JP2K_ENCODE)
            return PixGenerateJp2kData(pixs, quality);
        if (type == L.L_G4_ENCODE)
            return PixGenerateG4Data(pixs, ascii85);
        return PixGenerateFlateData(pixs, ascii85);
    }

    // leptonica: l_generateFlateData (pdfio2.c)
    /// <summary>Generates Flate-compressed image data from an image file, always decoding the file first.</summary>
    /// <param name="fname">Path of the image file.</param>
    /// <param name="ascii85flag">0 for binary data, which is for PDF only; 1 for ASCII85-encoded data, which is for PostScript only.</param>
    /// <returns>The compressed image data.</returns>
    /// <remarks>The input image is converted to 1 bpp, 8 bpp without a colormap, 8 bpp with a colormap, or 32 bpp RGB before it is compressed.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="fname"/> is <see langword="null"/>.</exception>
    public static L_Compressed_Data GenerateFlateData(string fname, int ascii85flag)
    {
        const string func = "l_generateFlateData";
        if (fname == null)
            throw Lept.Arg(func, "fname not defined");

        Pix pixs = Pix.Read(fname);
        return PixGenerateFlateData(pixs, ascii85flag);
    }

    // leptonica: pixGenerateFlateData (pdfio2.c)
    private static L_Compressed_Data PixGenerateFlateData(Pix pixs, int ascii85flag)
    {
        const string func = "pixGenerateFlateData";
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");

        pixs.GetDimensions(out int w, out int h, out int d);
        PixColormap? cmap = pixs.Colormap;
        bool cmapflag = cmap != null;
        Pix pixt;
        if (d == 2 || d == 4 || d == 16) {
            pixt = pixs.ConvertTo8(cmapflag);
            cmap = pixt.Colormap;
            d = pixt.Depth;
        } else if (d == 32 && pixs.Spp == 4) {  /* remove alpha */
            pixt = pixs.AlphaBlendUniform(0xffffff00u);
        } else {
            pixt = Pix.Clone(pixs);
        }
        int spp = (d == 32) ? 3 : 1;
        int bps = (d == 32) ? 8 : d;

            /* Extract and encode the colormap data as both ascii85 and hexascii */
        string? cmapdata85 = null;
        string? cmapdatahex = null;
        int ncolors = 0;
        if (cmap != null) {
            byte[] cmapdata = cmap.SerializeToMemory(3, out ncolors);
            cmapdata85 = LeptEncoding.EncodeAscii85(cmapdata);
            cmapdatahex = PixColormap.ConvertToHex(cmapdata, ncolors);
        }

            /* Extract and compress the raster data */
        byte[] data = pixt.GetRasterData();
        int nbytes = data.Length;
        byte[] datacomp = ZlibMem.ZlibCompress(data);
        int nbytescomp = datacomp.Length;

            /* Optionally, encode the compressed data */
        string? data85 = null;
        int nbytes85 = 0;
        if (ascii85flag == 1) {
            string encoded = LeptEncoding.EncodeAscii85(datacomp);
            nbytes85 = encoded.Length;
            data85 = encoded.EndsWith('\n') ? encoded.Substring(0, encoded.Length - 1) : encoded;
        }

        return new L_Compressed_Data {
            DataComp = (ascii85flag == 0) ? datacomp : null,
            Data85 = (ascii85flag == 1) ? data85 : null,
            Nbytes85 = (ascii85flag == 1) ? nbytes85 : 0,
            Type = L.L_FLATE_ENCODE,
            CmapDataHex = cmapdatahex,
            CmapData85 = cmapdata85,
            NbytesComp = nbytescomp,
            Ncolors = ncolors,
            W = w,
            H = h,
            Bps = bps,
            Spp = spp,
            Res = pixs.XRes,
            Nbytes = nbytes
        };
    }

    // leptonica: pixGenerateJpegData (pdfio2.c)
    private static L_Compressed_Data PixGenerateJpegData(Pix pixs, int ascii85flag, int quality)
    {
        const string func = "pixGenerateJpegData";
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");
        if (pixs.Colormap != null)
            throw Lept.Arg(func, "pixs has colormap");
        int d = pixs.Depth;
        if (d != 8 && d != 16 && d != 32)
            throw Lept.Arg(func, "pixs not 8, 16 or 32 bpp");

        string fname = Utils.LMakeTempFilename();
        try {
            Pix.WriteJpeg(fname, pixs, quality, 0);
            return GenerateJpegData(fname, ascii85flag);
        } finally {
            Utils.LeptRmfile(fname);
        }
    }

    // leptonica: pixGenerateJp2kData (pdfio2.c)
    private static L_Compressed_Data PixGenerateJp2kData(Pix pixs, int quality)
    {
        const string func = "pixGenerateJp2kData";
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");
        if (pixs.Colormap != null)
            throw Lept.Arg(func, "pixs has colormap");
        int d = pixs.Depth;
        if (d != 8 && d != 32)
            throw Lept.Arg(func, "pixs not 8 or 32 bpp");

        string fname = Utils.LMakeTempFilename();
        try {
            Pix.WriteJp2k(fname, pixs, quality, 5, 0, 0);
            return GenerateJp2kData(fname);
        } finally {
            Utils.LeptRmfile(fname);
        }
    }

    // leptonica: pixGenerateG4Data (pdfio2.c)
    private static L_Compressed_Data PixGenerateG4Data(Pix pixs, int ascii85flag)
    {
        const string func = "pixGenerateG4Data";
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");
        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (pixs.Colormap != null)
            throw Lept.Arg(func, "pixs has colormap");

        string fname = Utils.LMakeTempFilename();
        try {
            Pix.Write(fname, pixs, L.IFF_TIFF_G4);
            return GenerateG4Data(fname, ascii85flag);
        } finally {
            Utils.LeptRmfile(fname);
        }
    }

    // leptonica: cidConvertToPdfData (pdfio2.c)
    /// <summary>Converts compressed image data to one-page PDF data.</summary>
    /// <param name="cid">The compressed image data. It is absorbed into the PDF state and released by this method, so the caller must not release it.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <returns>The PDF data.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="cid"/> is <see langword="null"/>.</exception>
    public static byte[] CidConvertToPdfData(L_Compressed_Data cid, string? title)
    {
        const string func = "cidConvertToPdfData";
        if (cid == null)
            throw Lept.Arg(func, "cid not defined");

        int res = cid.Res;
        if (res <= 0)
            res = DefaultInputRes;
        float wpt = cid.W * 72.0f / res;
        float hpt = cid.H * 72.0f / res;

        L_Pdf_Data lpd = PdfdataCreate(title);
        lpd.Cida.Add(cid);
        lpd.N++;
        lpd.Xy.AddPt(0.0f, 0.0f);
        lpd.Wh.AddPt(wpt, hpt);

        byte[] data = GeneratePdf(lpd);
        PdfdataDestroy(ref lpd!);
        return data;
    }

    // leptonica: l_CIDataDestroy (pdfio2.c)
    /// <summary>Releases the data held by compressed image data and sets the reference to <see langword="null"/>.</summary>
    /// <param name="pcid">The compressed image data to release; it may be <see langword="null"/>.</param>
    public static void CIDataDestroy(ref L_Compressed_Data? pcid)
    {
        if (pcid == null)
            return;
        pcid.DataComp = null;
        pcid.Data85 = null;
        pcid.CmapData85 = null;
        pcid.CmapDataHex = null;
        pcid = null;
    }

    /*---------------------------------------------------------------------*
     *         Helper functions for generating the output pdf string       *
     *---------------------------------------------------------------------*/
    // leptonica: l_generatePdf (pdfio2.c)
    private static byte[] GeneratePdf(L_Pdf_Data lpd)
    {
        const string func = "l_generatePdf";
        if (lpd == null)
            throw Lept.Arg(func, "lpd not defined");

        GenerateFixedStringsPdf(lpd);
        GenerateMediaboxPdf(lpd);
        GeneratePageStringPdf(lpd);
        GenerateContentStringPdf(lpd);
        GeneratePreXStringsPdf(lpd);
        GenerateColormapStringsPdf(lpd);
        GenerateTrailerPdf(lpd);
        return GenerateOutputDataPdf(lpd);
    }

    // leptonica: generateFixedStringsPdf (pdfio2.c)
    private static void GenerateFixedStringsPdf(L_Pdf_Data lpd)
    {
        lpd.Id = "%PDF-1.5\n";
        lpd.Objsize.AddNumber(lpd.Id.Length);

        lpd.Obj1 = "1 0 obj\n<<\n/Type /Catalog\n/Pages 3 0 R\n>>\nendobj\n";
        lpd.Objsize.AddNumber(lpd.Obj1.Length);

        Sarray sa = Sarray.Create(0);
        sa.AddString("2 0 obj\n<<\n", L.L_COPY);
        if (WriteDateAndVersion) {
            string datestr = Utils.GetFormattedDate();
            sa.AddString($"/CreationDate (D:{datestr})\n", L.L_COPY);
            string version = Utils.GetLeptonicaVersion();
            sa.AddString($"/Producer (leptonica: {version})\n", L.L_COPY);
        } else {
            sa.AddString("/Producer (leptonica)\n", L.L_COPY);
        }
        if (lpd.Title != null) {
            string? hexstr = GenerateEscapeString(lpd.Title);
            if (hexstr != null)
                sa.AddString($"/Title {hexstr}\n", L.L_COPY);
            else
                Log.Error("generateFixedStringsPdf", "title string is not ascii");
        }
        sa.AddString(">>\nendobj\n", L.L_COPY);
        lpd.Obj2 = sa.ToString(0);
        lpd.Objsize.AddNumber(lpd.Obj2.Length);

        lpd.Obj3 = "3 0 obj\n<<\n/Type /Pages\n/Kids [ 4 0 R ]\n/Count 1\n>>\n";
        lpd.Objsize.AddNumber(lpd.Obj3.Length);

        lpd.PostStream = "\nendstream\nendobj\n";
    }

    // leptonica: generateEscapeString (pdfio2.c)
    private static string? GenerateEscapeString(string? str)
    {
        const string func = "generateEscapeString";
        if (str == null) {
            Log.Error(func, "str not defined");
            return null;
        }
        for (int i = 0; i < str.Length; i++) {
            if (str[i] >= 0x80) {
                Log.Error(func, "str not all ascii");
                return null;
            }
        }

        var sb = new StringBuilder(4 * str.Length + 10);
        sb.Append("<feff");
        for (int i = 0; i < str.Length; i++)
            sb.Append(((int)str[i]).ToString("x4", CultureInfo.InvariantCulture));
        sb.Append('>');
        return sb.ToString();
    }

    // leptonica: generateMediaboxPdf (pdfio2.c)
    private static void GenerateMediaboxPdf(L_Pdf_Data lpd)
    {
        float maxx = 0.0f, maxy = 0.0f;
        for (int i = 0; i < lpd.N; i++) {
            lpd.Xy.GetPt(i, out float xpt, out float ypt);
            lpd.Wh.GetPt(i, out float wpt, out float hpt);
            maxx = Math.Max(maxx, xpt + wpt);
            maxy = Math.Max(maxy, ypt + hpt);
        }

        lpd.Mediabox = Box.Create(0, 0, (int)(maxx + 0.5), (int)(maxy + 0.5));

        for (int i = 0; i < lpd.N; i++) {
            lpd.Xy.GetPt(i, out float xpt, out float ypt);
            lpd.Wh.GetPt(i, out _, out float hpt);
            lpd.Xy.SetPt(i, xpt, maxy - ypt - hpt);
        }
    }

    // leptonica: generatePageStringPdf (pdfio2.c)
    private static void GeneratePageStringPdf(L_Pdf_Data lpd)
    {
        lpd.Mediabox!.GetGeometry(out _, out _, out int wpt, out int hpt);
        Sarray sa = Sarray.Create(lpd.N);
        for (int i = 0; i < lpd.N; i++)
            sa.AddString($"/Im{i + 1} {6 + i} 0 R   ", L.L_COPY);
        string xstr = sa.ToString(0);

        lpd.Obj4 = string.Create(CultureInfo.InvariantCulture,
            $"4 0 obj\n<<\n/Type /Page\n/Parent 3 0 R\n/MediaBox [0 0 {wpt} {hpt}]\n" +
            $"/Contents 5 0 R\n/Resources\n<<\n/XObject << {xstr} >>\n" +
            $"/ProcSet [ /ImageB /ImageI /ImageC ]\n>>\n>>\nendobj\n");
        lpd.Objsize.AddNumber(lpd.Obj4.Length);
    }

    // leptonica: generateContentStringPdf (pdfio2.c)
    private static void GenerateContentStringPdf(L_Pdf_Data lpd)
    {
        Sarray sa = Sarray.Create(lpd.N);
        for (int i = 0; i < lpd.N; i++) {
            lpd.Xy.GetPt(i, out float xpt, out float ypt);
            lpd.Wh.GetPt(i, out float wpt, out float hpt);
            string line = CFormat.Sprintf("q %.4f %.4f %.4f %.4f %.4f %.4f cm /Im%d Do Q\n",
                                          wpt, 0.0, 0.0, hpt, xpt, ypt, i + 1);
            sa.AddString(line, L.L_COPY);
        }
        string cstr = sa.ToString(0);

        lpd.Obj5 = string.Create(CultureInfo.InvariantCulture,
            $"5 0 obj\n<< /Length {cstr.Length} >>\nstream\n{cstr}endstream\nendobj\n");
        lpd.Objsize.AddNumber(lpd.Obj5.Length);
    }

    // leptonica: generatePreXStringsPdf (pdfio2.c)
    private static void GeneratePreXStringsPdf(L_Pdf_Data lpd)
    {
        const string func = "generatePreXStringsPdf";
        Sarray sa = lpd.Saprex;
        int cmindex = 6 + lpd.N;
        for (int i = 0; i < lpd.N; i++) {
            string? pstr = null;
            string cstr = "";
            string bstr;
            string fstr;
            L_Compressed_Data cid = PdfdataGetCid(lpd, i);

            if (cid.Type == L.L_G4_ENCODE) {
                cstr = WriteG4ImageMask
                    ? "/ImageMask true\n/ColorSpace /DeviceGray"
                    : "/ColorSpace /DeviceGray";
                bstr = "/BitsPerComponent 1\n/Interpolate true";
                string photometry = (cid.MinisBlack != 0) ? "true" : "false";
                fstr = string.Create(CultureInfo.InvariantCulture,
                    $"/Filter /CCITTFaxDecode\n/DecodeParms\n<<\n/BlackIs1 {photometry}\n/K -1\n/Columns {cid.W}\n>>");
            } else if (cid.Type == L.L_JPEG_ENCODE) {
                if (cid.Spp == 1)
                    cstr = "/ColorSpace /DeviceGray";
                else if (cid.Spp == 3)
                    cstr = "/ColorSpace /DeviceRGB";
                else if (cid.Spp == 4)
                    cstr = "/ColorSpace /DeviceCMYK";
                else
                    Log.Error(func, "in jpeg: spp != 1, 3 or 4");
                bstr = "/BitsPerComponent 8";
                fstr = "/Filter /DCTDecode";
            } else if (cid.Type == L.L_JP2K_ENCODE) {
                if (cid.Spp == 1)
                    cstr = "/ColorSpace /DeviceGray";
                else if (cid.Spp == 3)
                    cstr = "/ColorSpace /DeviceRGB";
                else
                    Log.Error(func, "in jp2k: spp != 1 && spp != 3");
                bstr = "/BitsPerComponent 8";
                fstr = "/Filter /JPXDecode";
            } else {  /* type == L_FLATE_ENCODE */
                if (cid.Ncolors > 0) {
                    cstr = string.Create(CultureInfo.InvariantCulture, $"/ColorSpace {cmindex++} 0 R");
                } else {
                    if (cid.Spp == 1 && cid.Bps == 1)
                        cstr = "/ColorSpace /DeviceGray\n/Decode [1 0]";
                    else if (cid.Spp == 1)
                        cstr = "/ColorSpace /DeviceGray";
                    else if (cid.Spp == 3)
                        cstr = "/ColorSpace /DeviceRGB";
                    else
                        Log.Error(func, $"unknown colorspace: spp = {cid.Spp}");
                }
                bstr = string.Create(CultureInfo.InvariantCulture, $"/BitsPerComponent {cid.Bps}");
                fstr = "/Filter /FlateDecode";
                if (cid.Predictor != 0) {
                    pstr = string.Create(CultureInfo.InvariantCulture,
                        $"/DecodeParms\n<<\n  /Columns {cid.W}\n  /Predictor 14\n  /Colors {cid.Spp}\n  /BitsPerComponent {cid.Bps}\n>>\n");
                }
            }
            pstr ??= "";

            string xstr = string.Create(CultureInfo.InvariantCulture,
                $"{6 + i} 0 obj\n<<\n/Length {cid.NbytesComp}\n/Subtype /Image\n{cstr}\n" +
                $"/Width {cid.W}\n/Height {cid.H}\n{bstr}\n{fstr}\n{pstr}>>\nstream\n");
            sa.AddString(xstr, L.L_INSERT);
            lpd.Objsize.AddNumber(xstr.Length + cid.NbytesComp + lpd.PostStream!.Length);
        }
    }

    // leptonica: generateColormapStringsPdf (pdfio2.c)
    private static void GenerateColormapStringsPdf(L_Pdf_Data lpd)
    {
        Sarray sa = lpd.Sacmap;
        int cmindex = 6 + lpd.N;
        int ncmap = 0;
        for (int i = 0; i < lpd.N; i++) {
            L_Compressed_Data cid = PdfdataGetCid(lpd, i);
            if (cid.Ncolors == 0)
                continue;

            ncmap++;
            string cmstr = string.Create(CultureInfo.InvariantCulture,
                $"{cmindex} 0 obj\n[ /Indexed /DeviceRGB\n{cid.Ncolors - 1}\n{cid.CmapDataHex}\n]\nendobj\n");
            cmindex++;
            lpd.Objsize.AddNumber(cmstr.Length);
            sa.AddString(cmstr, L.L_INSERT);
        }

        lpd.Ncmap = ncmap;
    }

    // leptonica: generateTrailerPdf (pdfio2.c)
    private static void GenerateTrailerPdf(L_Pdf_Data lpd)
    {
        L_Dna dasize = lpd.Objsize;
        L_Dna daloc = lpd.Objloc;
        int linestart = 0;
        daloc.AddNumber(linestart);
        int n = dasize.Count;
        for (int i = 0; i < n; i++) {
            int size = dasize.GetIValue(i);
            linestart += size;
            daloc.AddNumber(linestart);
        }
        lpd.Xrefloc = daloc.GetIValue(n);
        lpd.Trailer = MakeTrailerStringPdf(daloc);
    }

    // leptonica: makeTrailerStringPdf (pdfio2.c)
    private static string MakeTrailerStringPdf(L_Dna daloc)
    {
        const string func = "makeTrailerStringPdf";
        if (daloc == null)
            throw Lept.Arg(func, "daloc not defined");
        int n = daloc.Count - 1;

        Sarray sa = Sarray.Create(0);
        sa.AddString(string.Create(CultureInfo.InvariantCulture,
            $"xref\n0 {n}\n0000000000 65535 f \n"), L.L_COPY);
        for (int i = 1; i < n; i++) {
            int linestart = daloc.GetIValue(i);
            sa.AddString(string.Create(CultureInfo.InvariantCulture,
                $"{linestart:D10} 00000 n \n"), L.L_COPY);
        }

        int xrefloc = daloc.GetIValue(n);
        sa.AddString(string.Create(CultureInfo.InvariantCulture,
            $"trailer\n<<\n/Size {n}\n/Root 1 0 R\n/Info 2 0 R\n>>\nstartxref\n{xrefloc}\n%%EOF\n"),
            L.L_COPY);
        return sa.ToString(0);
    }

    // leptonica: generateOutputDataPdf (pdfio2.c)
    private static byte[] GenerateOutputDataPdf(L_Pdf_Data lpd)
    {
        int nbytes = lpd.Xrefloc + lpd.Trailer!.Length;
        byte[] data = new byte[nbytes];

        int[] sizes = lpd.Objsize.GetIArray();
        int[] locs = lpd.Objloc.GetIArray();
        Encoding.Latin1.GetBytes(lpd.Id!, 0, sizes[0], data, 0);
        Encoding.Latin1.GetBytes(lpd.Obj1!, 0, sizes[1], data, locs[1]);
        Encoding.Latin1.GetBytes(lpd.Obj2!, 0, sizes[2], data, locs[2]);
        Encoding.Latin1.GetBytes(lpd.Obj3!, 0, sizes[3], data, locs[3]);
        Encoding.Latin1.GetBytes(lpd.Obj4!, 0, sizes[4], data, locs[4]);
        Encoding.Latin1.GetBytes(lpd.Obj5!, 0, sizes[5], data, locs[5]);

        int nimages = lpd.N;
        for (int i = 0; i < nimages; i++) {
            L_Compressed_Data cid = PdfdataGetCid(lpd, i);
            string str = lpd.Saprex.GetString(i, L.L_NOCOPY);
            int len = str.Length;
            Encoding.Latin1.GetBytes(str, 0, len, data, locs[6 + i]);
            Buffer.BlockCopy(cid.DataComp!, 0, data, locs[6 + i] + len, cid.NbytesComp);
            Encoding.Latin1.GetBytes(lpd.PostStream!, 0, lpd.PostStream!.Length,
                                     data, locs[6 + i] + len + cid.NbytesComp);
        }

        for (int i = 0; i < lpd.Ncmap; i++) {
            string str = lpd.Sacmap.GetString(i, L.L_NOCOPY);
            Encoding.Latin1.GetBytes(str, 0, str.Length, data, locs[6 + nimages + i]);
        }

        Encoding.Latin1.GetBytes(lpd.Trailer!, 0, lpd.Trailer!.Length, data, lpd.Xrefloc);
        return data;
    }

    /*---------------------------------------------------------------------*
     *          Helper functions for generating multipage pdf output       *
     *---------------------------------------------------------------------*/
    // leptonica: parseTrailerPdf (pdfio2.c)
    private static bool ParseTrailerPdf(L_Bytea bas, out L_Dna? pda)
    {
        const string func = "parseTrailerPdf";
        pda = null;
        if (bas == null)
            return false;

        byte[] raw = bas.GetData(out int size);
        ReadOnlySpan<byte> data = raw.AsSpan(0, size);
        if (size < 7 || !data.Slice(0, 7).SequenceEqual(PdfHeaderSig)) {
            Log.Error(func, "PDF header signature not found");
            return false;
        }

        int start = (size > 50) ? size - 50 : 0;
        if (!Utils.ArrayFindSequence(data.Slice(start), StartXrefSeq, out int loc)) {
            Log.Error(func, "startxref not found!");
            return false;
        }
        if (!TryScanLeadingInt(data.Slice(start + loc + 10), out int xrefloc)) {
            Log.Error(func, "xrefloc not found!");
            return false;
        }
        if (xrefloc < 0 || xrefloc >= size) {
            Log.Error(func, "invalid xrefloc!");
            return false;
        }

        Sarray sa = Sarray.CreateLinesFromString(Encoding.Latin1.GetString(data.Slice(xrefloc)), 0);
        if (sa.Count < 2)
            return false;
        string hdrLine = sa.GetString(1, L.L_NOCOPY);
        if (!hdrLine.StartsWith("0 ", StringComparison.Ordinal) ||
            !TryScanLeadingInt(Encoding.Latin1.GetBytes(hdrLine.Substring(2)), out int nobj)) {
            Log.Error(func, "nobj not found");
            return false;
        }

        L_Dna da = L_Dna.Create(nobj + 1);
        pda = da;
        for (int i = 0; i < nobj; i++) {
            string str = sa.GetString(i + 2, L.L_NOCOPY);
            TryScanLeadingInt(Encoding.Latin1.GetBytes(str), out int startloc);
            da.AddNumber(startloc);
        }
        da.AddNumber(xrefloc);

            /* Verify correct parsing */
        bool trailer_ok = true;
        for (int i = 1; i < nobj; i++) {
            int startloc = da.GetIValue(i);
            if (startloc < 0 || startloc >= size || !TryMatchObjHeader(data.Slice(startloc), out _)) {
                Log.Error(func, $"bad trailer for object {i}");
                trailer_ok = false;
                break;
            }
        }

            /* If the trailer is broken, reconstruct the correct obj locations */
        if (!trailer_ok) {
            Log.Info(func, "rebuilding pdf trailer");
            da.Empty();
            da.AddNumber(0);
            L_Dna? daobj = bas.FindEachSequence(ObjZeroSeq);
            nobj = daobj?.Count ?? 0;
            for (int i = 0; i < nobj; i++) {
                int objloc = daobj!.GetIValue(i);
                int j = objloc - 1;
                for (; j > 0; j--) {
                    if (data[j] == (byte)'\n')
                        break;
                }
                da.AddNumber(j + 1);
            }
            L_Dna? daxref = bas.FindEachSequence(XrefSeq);
            if (daxref != null && daxref.Count > 0)
                da.AddNumber(daxref.GetIValue(0));
        }

        return true;
    }

    // leptonica: generatePagesObjStringPdf (pdfio2.c)
    private static string GeneratePagesObjStringPdf(Numa napage)
    {
        const string func = "generatePagesObjStringPdf";
        if (napage == null)
            throw Lept.Arg(func, "napage not defined");

        int n = napage.Count;
        Sarray sa = Sarray.Create(n);
        for (int i = 0; i < n; i++) {
            int index = napage.GetIValue(i);
            sa.AddString(string.Create(CultureInfo.InvariantCulture, $" {index} 0 R "), L.L_COPY);
        }
        string str = sa.ToString(0);
        return string.Create(CultureInfo.InvariantCulture,
            $"3 0 obj\n<<\n/Type /Pages\n/Kids [{str}]\n/Count {n}\n>>\nendobj\n");
    }

    // leptonica: substituteObjectNumbers (pdfio2.c)
    private static L_Bytea SubstituteObjectNumbers(L_Bytea bas, Numa na_objs)
    {
        const string func = "substituteObjectNumbers";
        if (bas == null)
            throw Lept.Arg(func, "bas not defined");
        if (na_objs == null)
            throw Lept.Arg(func, "na_objs not defined");

        byte[] raw = bas.GetData(out int size);
        ReadOnlySpan<byte> datas = raw.AsSpan(0, size);
        L_Bytea bad = L_Bytea.Create(100);
        int[] objs = na_objs.GetIArray();
        int nobjs = na_objs.Count;

            /* Substitute the object number on the first line */
        TryScanLeadingInt(datas, out int objin);
        if (objin < 0 || objin >= nobjs) {
            Log.Error(func, $"index {objin} into array of size {nobjs}");
            return bad;
        }
        bad.AppendString(objs[objin].ToString(CultureInfo.InvariantCulture));

            /* Find the set of matching locations for object references */
        Utils.ArrayFindSequence(datas, SpaceByte, out int start);
        L_Dna? da_match = Utils.ArrayFindEachSequence(datas, RefSeq);
        if (da_match == null) {
            bad.AppendData(datas.Slice(start, size - start));
            return bad;
        }

            /* Substitute all the object reference numbers */
        int nrepl = da_match.Count;
        int[] matches = da_match.GetIArray();
        for (int i = 0; i < nrepl; i++) {
            int j = matches[i] - 1;
            for (; j > 0; j--) {
                if (datas[j] == (byte)' ')
                    break;
            }
            bad.AppendData(datas.Slice(start, j - start + 1));
            TryScanLeadingInt(datas.Slice(j + 1), out objin);
            if (objin < 0 || objin >= nobjs) {
                Log.Error(func, $"index {objin} into array of size {nobjs}");
                return bad;
            }
            bad.AppendString(objs[objin].ToString(CultureInfo.InvariantCulture));
            start = matches[i];
        }
        bad.AppendData(datas.Slice(start, size - start));
        return bad;
    }

    /*---------------------------------------------------------------------*
     *                     Create/destroy/access pdf data                  *
     *---------------------------------------------------------------------*/
    // leptonica: pdfdataCreate (pdfio2.c)
    private static L_Pdf_Data PdfdataCreate(string? title)
    {
        return new L_Pdf_Data {
            Title = title,
            Cida = L_Ptra<L_Compressed_Data>.Create(10),
            Xy = Pta.Create(10),
            Wh = Pta.Create(10),
            Saprex = Sarray.Create(10),
            Sacmap = Sarray.Create(10),
            Objsize = L_Dna.Create(20),
            Objloc = L_Dna.Create(20)
        };
    }

    // leptonica: pdfdataDestroy (pdfio2.c)
    private static void PdfdataDestroy(ref L_Pdf_Data? plpd)
    {
        if (plpd == null)
            return;
        for (int i = 0; i < plpd.N; i++) {
            L_Compressed_Data? cid = plpd.Cida.Remove(i, L.L_NO_COMPACTION);
            CIDataDestroy(ref cid);
        }
        plpd = null;
    }

    // leptonica: pdfdataGetCid (pdfio2.c)
    private static L_Compressed_Data PdfdataGetCid(L_Pdf_Data lpd, int index)
    {
        const string func = "pdfdataGetCid";
        if (lpd == null)
            throw Lept.Arg(func, "lpd not defined");
        if (index < 0 || index >= lpd.N)
            throw Lept.Arg(func, "invalid image index");

        return lpd.Cida.GetPtrToItem(index)!;
    }

    /*---------------------------------------------------------------------*
     *                     Find number of pages in a pdf                   *
     *---------------------------------------------------------------------*/
    // leptonica: getPdfPageCount (pdfio2.c)
    /// <summary>Gets the number of pages in a PDF file.</summary>
    /// <param name="fname">Path of the PDF file.</param>
    /// <returns>The number of pages.</returns>
    /// <remarks>
    /// <para>This looks for the argument of the first <c>/Count</c> in the file, first in the initial 10000 bytes and then in the entire file.</para>
    /// <para>It does not work on encrypted PDF files or on files where the <c>/Count</c> field is binary compressed. Not finding the field is not an error, but a warning is given.</para>
    /// </remarks>
    /// <exception cref="System.ArgumentException"><paramref name="fname"/> is <see langword="null"/>.</exception>
    public static int GetPageCount(string fname)
    {
        const string func = "getPdfPageCount";
        if (fname == null)
            throw Lept.Arg(func, "fname not defined");

        Pix.FindFileFormat(fname, out int format);
        if (format != L.IFF_LPDF)
            throw Lept.Fail(func, "file is not pdf");

        byte[] data = Utils.BinaryReadSelect(fname, 0, 10000);
        bool found = Utils.ArrayFindSequence(data, CountTag, out int loc);
        if (!found) {
            data = Utils.BinaryRead(fname);
            found = Utils.ArrayFindSequence(data, CountTag, out loc);
            if (!found) {
                Log.Warning(func, "/Count not found");
                return 0;
            }
        }

        if (data.Length - loc < 12)
            throw Lept.Fail(func, "data may not include page count field");

        if (!TryScanAfterPrefix(data.AsSpan(loc), CountTag, out int npages))
            throw Lept.Fail(func, "npages not found");
        return npages;
    }

    /*---------------------------------------------------------------------*
     *      Find widths and heights of pages and media boxes in a pdf      *
     *---------------------------------------------------------------------*/
    // leptonica: getPdfPageSizes (pdfio2.c)
    /// <summary>Gets the widths and heights of the pages of a PDF file, from its <c>/Width</c> and <c>/Height</c> fields.</summary>
    /// <param name="fname">Path of the PDF file.</param>
    /// <param name="pnaw">Receives the array of page widths.</param>
    /// <param name="pnah">Receives the array of page heights.</param>
    /// <param name="pmedw">Receives the median page width.</param>
    /// <param name="pmedh">Receives the median page height.</param>
    /// <remarks>This does not work on encrypted PDF files or on files where the <c>/Width</c> and <c>/Height</c> fields are binary compressed. Not finding the fields is not an error, but a warning is given.</remarks>
    /// <exception cref="System.ArgumentException"><paramref name="fname"/> is <see langword="null"/>.</exception>
    public static void GetPageSizes(string fname, out Numa pnaw, out Numa pnah,
                                    out int pmedw, out int pmedh)
    {
        const string func = "getPdfPageSizes";
        pmedw = 0;
        pmedh = 0;
        if (fname == null)
            throw Lept.Arg(func, "fname not defined");

        Pix.FindFileFormat(fname, out int format);
        if (format != L.IFF_LPDF)
            throw Lept.Fail(func, "file is not pdf");

        byte[] data = Utils.BinaryRead(fname);
        L_Dna? dnaw = Utils.ArrayFindEachSequence(data, WidthTag);
        L_Dna? dnah = Utils.ArrayFindEachSequence(data, HeightTag);
        if (dnaw == null)
            Log.Warning(func, "unable to find widths");
        if (dnah == null)
            Log.Warning(func, "unable to find heights");
        if (dnaw == null && dnah == null) {
            Log.Warning(func, "no fields found");
            pnaw = Numa.Create(0);
            pnah = Numa.Create(0);
            return;
        }

        int nw = dnaw?.Count ?? 0;
        Numa naw = Numa.Create(nw);
        for (int i = 0; i < nw; i++) {
            int loc = dnaw!.GetIValue(i);
            if (!TryScanAfterPrefix(data.AsSpan(loc), WidthTag, out int width)) {
                Log.Error(func, $"width not found for item {i} at loc {loc}");
                continue;
            }
            naw.AddNumber(width);
        }

        int nh = dnah?.Count ?? 0;
        Numa nah = Numa.Create(nh);
        for (int i = 0; i < nh; i++) {
            int loc = dnah!.GetIValue(i);
            if (!TryScanAfterPrefix(data.AsSpan(loc), HeightTag, out int height)) {
                Log.Error(func, $"height not found for item {i} at loc {loc}");
                continue;
            }
            nah.AddNumber(height);
        }

        if (naw.Count > 0)
            pmedw = Utils.RoundFToI(naw.GetMedian());
        if (nah.Count > 0)
            pmedh = Utils.RoundFToI(nah.GetMedian());
        pnaw = naw;
        pnah = nah;
    }

    // leptonica: getPdfMediaBoxSizes (pdfio2.c)
    /// <summary>Gets the widths and heights of the media boxes of a PDF file, from its <c>/MediaBox</c> fields.</summary>
    /// <param name="fname">Path of the PDF file.</param>
    /// <param name="pnaw">Receives the array of media box widths.</param>
    /// <param name="pnah">Receives the array of media box heights.</param>
    /// <param name="pmedw">Receives the median media box width.</param>
    /// <param name="pmedh">Receives the median media box height.</param>
    /// <remarks>
    /// <para>This does not work on encrypted PDF files or on files where the <c>/MediaBox</c> field is binary compressed. Not finding the field is not an error, but a warning is given.</para>
    /// <para>It is useful for detecting incorrectly assigned media boxes, such as a resolution assumed to be 72 ppi: if the renderer then assumes 300 ppi,
    /// the rendered images are over 4 times too large in each dimension. An image dimension of 11 inches corresponds to a media box value of 792;
    /// a value above 850 is considered oversized and is not taken literally.</para>
    /// </remarks>
    /// <exception cref="System.ArgumentException"><paramref name="fname"/> is <see langword="null"/>.</exception>
    public static void GetMediaBoxSizes(string fname, out Numa pnaw, out Numa pnah,
                                        out int pmedw, out int pmedh)
    {
        const string func = "getPdfMediaBoxSizes";
        pmedw = 0;
        pmedh = 0;
        if (fname == null)
            throw Lept.Arg(func, "fname not defined");

        Pix.FindFileFormat(fname, out int format);
        if (format != L.IFF_LPDF)
            throw Lept.Fail(func, "file is not pdf");

        byte[] data = Utils.BinaryRead(fname);
        L_Dna? dna = Utils.ArrayFindEachSequence(data, MediaBoxTag);
        if (dna == null) {
            Log.Warning(func, "no mediaboxes found");
            throw Lept.Fail(func, "no mediaboxes found");
        }

        int n = dna.Count;
        Numa naw = Numa.Create(n);
        Numa nah = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            int loc = dna.GetIValue(i);
            if (!TryScanMediaBox(data.AsSpan(loc), out float w, out float h)) {
                Log.Error(func, $"mediabox sizes not found for item {i} at loc {loc}");
                continue;
            }
            naw.AddNumber(w);
            nah.AddNumber(h);
        }

        if (naw.Count > 0)
            pmedw = Utils.RoundFToI(naw.GetMedian());
        if (nah.Count > 0)
            pmedh = Utils.RoundFToI(nah.GetMedian());
        pnaw = naw;
        pnah = nah;
    }

    /*---------------------------------------------------------------------*
     *       Find effective resolution of images rendered from a pdf       *
     *---------------------------------------------------------------------*/
    // leptonica: getPdfRendererResolution (pdfio2.c)
    /// <summary>Finds the resolution to give a PDF renderer so that it produces images of a full page at 300 ppi, that is, with a largest dimension of about 3300 pixels.</summary>
    /// <param name="infile">Path of the input PDF file.</param>
    /// <param name="outdir">The directory of the rendered output images.</param>
    /// <returns>The resolution, in ppi, to use with the renderer.</returns>
    /// <remarks>
    /// <para>It is most important to make sure the renderer does not make huge images because of an error in a media box. A value above 850 is oversized,
    /// and in that case a lower resolution is chosen. If the media boxes are not accessible, an image is rendered at a low known resolution (say, 72 ppi) and
    /// the resolution needed for 3300 pixels in the largest dimension is computed from its size.</para>
    /// <para>This needs the external <c>pdftoppm</c> program, and it is disabled unless debug output is enabled.</para>
    /// </remarks>
    public static int GetRendererResolution(string infile, string outdir)
    {
        const string func = "getPdfRendererResolution";
        if (!Pix.LeptDebugOK) {
            Log.Info(func, "Running pdftoppm is disabled; use setLeptDebugOK(1) to enable");
            throw Lept.Fail(func, "LeptDebugOK is false");
        }
        if (infile == null)
            throw Lept.Arg(func, "infile not defined");
        if (outdir == null)
            throw Lept.Arg(func, "outdir not defined");

        int res = 300;
        try {
            GetMediaBoxSizes(infile, out _, out _, out int medw, out int medh);
            int medmax = Math.Max(medw, medh);
            if (medmax > 850)
                res = (int)(300 * (792.0f / medmax));
            return res;
        } catch (InvalidOperationException) {
                /* No mediaboxes; callSystemDebug("pdftoppm ...") is not ported. */
            Sarray sa = Sarray.GetSortedPathnamesInDirectory(outdir, null, 0, 0);
            if (sa.Count > 0) {
                string fname = sa.GetString(0, L.L_NOCOPY);
                Pix.ReadHeader(fname, out _, out int w, out int h, out _, out _, out _);
                if (w > 0 && h > 0)
                    res = Math.Min(72 * 3300 / Math.Max(w, h), 600);
            } else {
                Log.Error(func, "page size not found; assuming res = 300");
            }
            return res;
        }
    }

    /*---------------------------------------------------------------------*
     *                      Set flags for special modes                    *
     *---------------------------------------------------------------------*/
    // leptonica: l_pdfSetG4ImageMask (pdfio2.c)
    /// <summary>Sets the flag for writing G4 data as foreground-only through a mask.</summary>
    public static void SetG4ImageMask(int flag)
    {
        omitG4ImageMask = (flag == 0);
    }

    // leptonica: l_pdfSetDateAndVersion (pdfio2.c)
    /// <summary>Sets the flag for writing the date and the version in a pdf.</summary>
    /// <param name="flag">1 for writing date/time and leptonica version; 0 for omitting them</param>
    public static void SetDateAndVersion(int flag)
    {
        omitDateAndVersion = (flag == 0);
    }

    /*---------------------------------------------------------------------*
     *                      Byte-level sscanf helpers                      *
     *---------------------------------------------------------------------*/
    private static bool TryScanLeadingInt(ReadOnlySpan<byte> span, out int value)
    {
        value = 0;
        int i = 0;
        while (i < span.Length && IsAsciiSpace(span[i]))
            i++;
        if (i >= span.Length)
            return false;

        int sign = 1;
        if (span[i] == (byte)'-') {
            sign = -1;
            i++;
        } else if (span[i] == (byte)'+') {
            i++;
        }
        if (i >= span.Length || span[i] < (byte)'0' || span[i] > (byte)'9')
            return false;

        long acc = 0;
        while (i < span.Length && span[i] >= (byte)'0' && span[i] <= (byte)'9') {
            acc = acc * 10 + (span[i] - (byte)'0');
            i++;
        }
        value = (int)(sign * acc);
        return true;
    }

    private static bool TryMatchObjHeader(ReadOnlySpan<byte> span, out int objno)
    {
        objno = 0;
        int i = 0;
        while (i < span.Length && IsAsciiSpace(span[i]))
            i++;
        if (i >= span.Length || span[i] < (byte)'0' || span[i] > (byte)'9')
            return false;
        while (i < span.Length && span[i] >= (byte)'0' && span[i] <= (byte)'9') {
            objno = objno * 10 + (span[i] - (byte)'0');
            i++;
        }
        return span.Slice(i).StartsWith(" 0 obj"u8);
    }

    private static bool TryScanAfterPrefix(ReadOnlySpan<byte> span, ReadOnlySpan<byte> prefix, out int value)
    {
        value = 0;
        if (!span.StartsWith(prefix))
            return false;
        return TryScanLeadingInt(span.Slice(prefix.Length), out value);
    }

    private static bool TryScanMediaBox(ReadOnlySpan<byte> span, out float w, out float h)
    {
        w = 0;
        h = 0;
        if (!span.StartsWith(MediaBoxTag))
            return false;
        int i = MediaBoxTag.Length;
        while (i < span.Length && IsAsciiSpace(span[i]))
            i++;
        if (i >= span.Length || span[i] != (byte)'[')
            return false;
        i++;

        if (!TryScanFloat(span, ref i, out _)) return false;
        if (!TryScanFloat(span, ref i, out _)) return false;
        if (!TryScanFloat(span, ref i, out w)) return false;
        if (!TryScanFloat(span, ref i, out h)) return false;
        return true;
    }

    private static bool TryScanFloat(ReadOnlySpan<byte> span, ref int i, out float val)
    {
        val = 0;
        while (i < span.Length && IsAsciiSpace(span[i]))
            i++;
        int start = i;
        while (i < span.Length && !IsAsciiSpace(span[i]) && span[i] != (byte)']')
            i++;
        if (i == start)
            return false;
        string token = Encoding.Latin1.GetString(span.Slice(start, i - start));
        return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out val);
    }

    private static bool IsAsciiSpace(byte b) =>
        b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n';
}
