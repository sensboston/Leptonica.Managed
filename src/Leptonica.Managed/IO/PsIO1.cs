using System;

namespace Leptonica;

/*
 *  psio1.c
 *
 *     PostScript "device driver" for wrapping images in PostScript.
 *
 *     Convert specified files to PS
 *          PsIo.ConvertFilesToPS (convertFilesToPS)
 *          PsIo.SarrayConvertFilesToPS (sarrayConvertFilesToPS)
 *          PsIo.ConvertFilesFittedToPS (convertFilesFittedToPS)
 *          PsIo.SarrayConvertFilesFittedToPS (sarrayConvertFilesFittedToPS)
 *          PsIo.WriteImageCompressedToPSFile (writeImageCompressedToPSFile)
 *
 *     Convert mixed text/image files to PS
 *          PsIo.ConvertSegmentedPagesToPS (convertSegmentedPagesToPS)
 *          Pix.WriteSegmentedPageToPS (pixWriteSegmentedPageToPS)
 *          Pix.WriteMixedToPS (pixWriteMixedToPS)
 *
 *     Convert any image file to PS for embedding
 *          PsIo.ConvertToPSEmbed (convertToPSEmbed)
 *
 *     Write all images in a pixa out to PS
 *          Pixa.WriteCompressedToPS (pixaWriteCompressedToPS)
 *          Pix.WriteCompressedToPS (pixWriteCompressedToPS)
 */
/// <summary>
/// A PostScript "device driver" that wraps images in PostScript, for viewing, printing, or conversion to PDF.
/// </summary>
/// <remarks>
/// <para>The converters are used in three ways: to embed a PostScript file in a program such as TeX
/// (<see cref="ConvertToPSEmbed"/>, for PostScript levels 1, 2 and 3); to compose a set of pages with any number of images
/// painted on them (<see cref="ConvertFilesToPS"/>, <see cref="ConvertSegmentedPagesToPS"/>); and to print page images at a resolution that
/// optimally fills the page (<see cref="ConvertFilesFittedToPS"/>).</para>
/// <para>All images are output with page numbers. Bounding box hints are needed when embedding images, for example in TeX,
/// so the low-level writers include them by default. They are not wanted in multi-page PostScript that is composed of a sequence of
/// images, so the higher-level functions do not write them; <see cref="WriteBoundingBox"/> controls this.</para>
/// </remarks>
public static partial class PsIo
{
    /*-------------------------------------------------------------*
     *                Convert files in a directory to PS           *
     *-------------------------------------------------------------*/
    // leptonica: convertFilesToPS (psio1.c)
    /// <summary>Generates a PostScript file with one page for each image file in a directory.</summary>
    /// <param name="dirin">The input directory.</param>
    /// <param name="substr">A substring that the file names must contain, or <see langword="null"/> to use all files.</param>
    /// <param name="res">Resolution of the output device, typically 300 or 600 ppi.</param>
    /// <param name="fileout">Path of the PostScript file to write.</param>
    /// <remarks>
    /// <para>All images are written compressed: G4 (CCITT Group 4) TIFF images use G4 encoding, JPEG images use DCT encoding,
    /// and all others use Flate encoding. For JPEG and G4 images the existing compressed data is used; otherwise the image is read
    /// and Flate encoded.</para>
    /// <para>The resolution is that of the output display device: if the input image was digitized at 300 ppi and <paramref name="res"/> is 100,
    /// the image is rendered three times larger. The size of the PostScript file does not depend on the resolution. For the smallest file
    /// without loss of visual information, downscale the image to the output resolution before wrapping it if that resolution is less than 300 ppi.</para>
    /// <para>The image is rendered on a canvas of standard page size (8.5 by 11 inches) at the given resolution.</para>
    /// </remarks>
    /// <exception cref="System.ArgumentException"><paramref name="dirin"/> or <paramref name="fileout"/> is <see langword="null"/>.</exception>
    public static void ConvertFilesToPS(string dirin, string? substr, int res, string fileout)
    {
        const string func = "convertFilesToPS";
        if (dirin == null)
            throw Lept.Arg(func, "dirin not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");
        if (res <= 0) {
            Log.Info(func, "setting res to 300 ppi");
            res = 300;
        }
        if (res < 10 || res > 4000)
            Log.Warning(func, "res is typically in the range 300-600 ppi");

        Sarray sa = Sarray.GetSortedPathnamesInDirectory(dirin, substr, 0, 0);
        WriteBoundingBox(false);
        try {
            SarrayConvertFilesToPS(sa, res, fileout);
        } finally {
            WriteBoundingBox(true);
        }
    }

    // leptonica: sarrayConvertFilesToPS (psio1.c)
    /// <summary>Generates a PostScript file with one page for each image file named in a string array.</summary>
    /// <param name="sa">The full path names of the image files.</param>
    /// <param name="res">Resolution of the output device, typically 300 or 600 ppi.</param>
    /// <param name="fileout">Path of the PostScript file to write.</param>
    /// <remarks>See <see cref="ConvertFilesToPS"/>.</remarks>
    public static void SarrayConvertFilesToPS(Sarray sa, int res, string fileout)
    {
        const string func = "sarrayConvertFilesToPS";
        if (sa == null)
            throw Lept.Arg(func, "sa not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");
        if (res <= 0) {
            Log.Info(func, "setting res to 300 ppi");
            res = 300;
        }
        if (res < 10 || res > 4000)
            Log.Warning(func, "res is typically in the range 300-600 ppi");

        int nfiles = sa.Count;
        int index = 0;
        for (int i = 0; i < nfiles; i++) {
            string fname = sa.GetString(i, L.L_NOCOPY);
            int format;
            try {
                Pix.ReadHeader(fname, out format, out _, out _, out _, out _, out _);
            } catch (Exception) {
                continue;
            }
            if (format == L.IFF_UNKNOWN)
                continue;

            WriteImageCompressedToPSFile(fname, fileout, res, ref index);
        }
    }

    // leptonica: convertFilesFittedToPS (psio1.c)
    /// <summary>Generates a PostScript file with one page for each image file in a directory, with each image fitted to a given size.</summary>
    /// <param name="dirin">The input directory.</param>
    /// <param name="substr">A substring that the file names must contain, or <see langword="null"/> to use all files.</param>
    /// <param name="xpts">Desired width in printer points; 0 for the default of 612.</param>
    /// <param name="ypts">Desired height in printer points; 0 for the default of 792.</param>
    /// <param name="fileout">Path of the PostScript file to write.</param>
    /// <remarks>
    /// <para>All images are written compressed: G4 (CCITT Group 4) TIFF images use G4 encoding, JPEG images use DCT encoding,
    /// and all others use Flate encoding.</para>
    /// <para>The resolution is determined internally so that the images are rendered, in at least one direction, at 100% of the given size in
    /// printer points. The size of the PostScript file does not depend on the resolution.</para>
    /// </remarks>
    /// <exception cref="System.ArgumentException"><paramref name="dirin"/> or <paramref name="fileout"/> is <see langword="null"/>.</exception>
    public static void ConvertFilesFittedToPS(string dirin, string? substr,
                                              float xpts, float ypts, string fileout)
    {
        const string func = "convertFilesFittedToPS";
        if (dirin == null)
            throw Lept.Arg(func, "dirin not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");
        if (xpts <= 0.0f) {
            Log.Info(func, "setting xpts to 612.0 ppi");
            xpts = 612.0f;
        }
        if (ypts <= 0.0f) {
            Log.Info(func, "setting ypts to 792.0 ppi");
            ypts = 792.0f;
        }
        if (xpts < 100.0f || xpts > 2000.0f || ypts < 100.0f || ypts > 2000.0f)
            Log.Warning(func, "xpts,ypts are typically in the range 500-800");

        Sarray sa = Sarray.GetSortedPathnamesInDirectory(dirin, substr, 0, 0);
        WriteBoundingBox(false);
        try {
            SarrayConvertFilesFittedToPS(sa, xpts, ypts, fileout);
        } finally {
            WriteBoundingBox(true);
        }
    }

    // leptonica: sarrayConvertFilesFittedToPS (psio1.c)
    /// <summary>Generates a PostScript file with one page for each image file named in a string array, with each image fitted to a given size.</summary>
    /// <param name="sa">The full path names of the image files.</param>
    /// <param name="xpts">Desired width in printer points; 0 for the default of 612.</param>
    /// <param name="ypts">Desired height in printer points; 0 for the default of 792.</param>
    /// <param name="fileout">Path of the PostScript file to write.</param>
    /// <remarks>See <see cref="ConvertFilesFittedToPS"/>.</remarks>
    public static void SarrayConvertFilesFittedToPS(Sarray sa, float xpts, float ypts, string fileout)
    {
        const string func = "sarrayConvertFilesFittedToPS";
        if (sa == null)
            throw Lept.Arg(func, "sa not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");
        if (xpts <= 0.0f) {
            Log.Info(func, "setting xpts to 612.0");
            xpts = 612.0f;
        }
        if (ypts <= 0.0f) {
            Log.Info(func, "setting ypts to 792.0");
            ypts = 792.0f;
        }
        if (xpts < 100.0f || xpts > 2000.0f || ypts < 100.0f || ypts > 2000.0f)
            Log.Warning(func, "xpts,ypts are typically in the range 500-800");

        int nfiles = sa.Count;
        int index = 0;
        for (int i = 0; i < nfiles; i++) {
            string fname = sa.GetString(i, L.L_NOCOPY);
            int format, w, h;
            try {
                Pix.ReadHeader(fname, out format, out w, out h, out _, out _, out _);
            } catch (Exception) {
                continue;
            }
            if (format == L.IFF_UNKNOWN)
                continue;

            int res = (xpts * h < ypts * w)
                ? (int)((float)w * 72.0 / xpts)
                : (int)((float)h * 72.0 / ypts);

            WriteImageCompressedToPSFile(fname, fileout, res, ref index);
        }
    }

    // leptonica: writeImageCompressedToPSFile (psio1.c)
    /// <summary>Wraps a single page image file in PostScript and writes or appends it to a PostScript file.</summary>
    /// <param name="filein">The input image file, in any format.</param>
    /// <param name="fileout">Path of the PostScript file to write.</param>
    /// <param name="res">Output printer resolution.</param>
    /// <param name="pindex">The index of the image in the output file. Set it to 0 before the first call; it is incremented when the page is written. The file is created when the index is 0 and appended to when it is greater than 0.</param>
    /// <remarks>The image is compressed with G4 if it is a G4 TIFF, with DCT if it is JPEG, and with Flate otherwise.</remarks>
    public static void WriteImageCompressedToPSFile(string filein, string fileout,
                                                    int res, ref int pindex)
    {
        const string func = "writeImageCompressedToPSFile";
        Pix.FindFileFormat(filein, out int format);
        if (format == L.IFF_UNKNOWN) {
            Log.Error(func, $"format of {filein} not known");
            throw Lept.Fail(func, "unknown format");
        }

        string op = (pindex == 0) ? "w" : "a";
        if (format == L.IFF_JFIF_JPEG) {
            ConvertJpegToPS(filein, fileout, op, 0, 0, res, 1.0f, pindex + 1, true);
        } else if (format == L.IFF_TIFF_G4) {
            ConvertG4ToPS(filein, fileout, op, 0, 0, res, 1.0f, pindex + 1, false, true);
        } else {
            ConvertFlateToPS(filein, fileout, op, 0, 0, res, 1.0f, pindex + 1, true);
        }
        pindex++;
    }

    /*-------------------------------------------------------------*
     *              Convert mixed text/image files to PS           *
     *-------------------------------------------------------------*/
    // leptonica: convertSegmentedPagesToPS (psio1.c)
    /// <summary>Generates a PostScript file from page images and their segmentation masks in two directories.</summary>
    /// <param name="pagedir">The directory of page images.</param>
    /// <param name="pagestr">A substring that the page file names must contain, or <see langword="null"/> to use all files.</param>
    /// <param name="page_numpre">Number of characters in the page file name before the page number.</param>
    /// <param name="maskdir">The directory of mask images.</param>
    /// <param name="maskstr">A substring that the mask file names must contain, or <see langword="null"/> to use all files.</param>
    /// <param name="mask_numpre">Number of characters in the mask file name before the page number.</param>
    /// <param name="numpost">Number of characters in the file names after the page number.</param>
    /// <param name="maxnum">Only page numbers up to this value are considered.</param>
    /// <param name="textscale">Scale of the text output relative to the page image.</param>
    /// <param name="imagescale">Scale of the image output relative to the page image.</param>
    /// <param name="threshold">Threshold for binarization, typically about 190; 0 for the default.</param>
    /// <param name="fileout">Path of the PostScript file to write.</param>
    /// <remarks>
    /// <para>The two directories can be the same, in which case the page and mask files are told apart by the two substrings. Page images are taken
    /// in lexicographic order. A mask image whose number matches a page image is used to segment it; a page image without a matching mask is scaled,
    /// thresholded and rendered entirely as text.</para>
    /// <para>Each PostScript page is a compressed representation of the page image: the part under the mask is scaled and compressed as DCT (JPEG),
    /// and the rest is scaled, thresholded, G4 compressed, and rendered by painting black through the resulting text mask. Typically
    /// <paramref name="imagescale"/> is 0.5 and <paramref name="textscale"/> is 2.0. The resolution is set automatically to fit a letter-size
    /// (8.5 by 11 inch) page, and both encodings are PostScript level 2.</para>
    /// <para>The page number is assumed to be part of the base name of the file (the name without directory or extension). For example, for the mask name
    /// <c>mask_006.tif</c>, <paramref name="mask_numpre"/> is 5.</para>
    /// <para>To render a page as it is, with no thresholding, use a full-size mask with all pixels set to 1. A 1 bpp page does not need a mask.</para>
    /// </remarks>
    public static void ConvertSegmentedPagesToPS(string pagedir, string? pagestr, int page_numpre,
                                                 string maskdir, string? maskstr, int mask_numpre,
                                                 int numpost, int maxnum, float textscale,
                                                 float imagescale, int threshold, string fileout)
    {
        const string func = "convertSegmentedPagesToPS";
        if (pagedir == null)
            throw Lept.Arg(func, "pagedir not defined");
        if (maskdir == null)
            throw Lept.Arg(func, "maskdir not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");
        if (threshold <= 0) {
            Log.Info(func, "setting threshold to 190");
            threshold = 190;
        }

        Sarray sapage = Sarray.GetNumberedPathnamesInDirectory(pagedir, pagestr, page_numpre, numpost, maxnum);
        Sarray samask = Sarray.GetNumberedPathnamesInDirectory(maskdir, maskstr, mask_numpre, numpost, maxnum);
        sapage.PadToSameSize(samask, "");
        int npages = sapage.Count;
        if (npages == 0)
            throw Lept.Fail(func, "no matching pages found");

        int pageno = 1;
        for (int i = 0; i < npages; i++) {
            Pix? pixs = Pix.ReadIndexed(sapage, i);
            if (pixs == null)
                continue;
            Pix? pixm = Pix.ReadIndexed(samask, i);
            pixs.WriteSegmentedPageToPS(pixm, textscale, imagescale, threshold, pageno, fileout);
            pageno++;
        }
    }

    /*-------------------------------------------------------------*
     *            Convert any image file to PS for embedding       *
     *-------------------------------------------------------------*/
    // leptonica: convertToPSEmbed (psio1.c)
    /// <summary>Generates a PostScript file with a bounding box from an image file of any format.</summary>
    /// <param name="filein">The input image file.</param>
    /// <param name="fileout">Path of the PostScript file to write.</param>
    /// <param name="level">The PostScript compression level: 1 (uncompressed), 2 or 3.</param>
    /// <remarks>
    /// <para>The best compression available for the level is used. Level 3 uses Flate compression for anything that is not a G4 TIFF (1 bpp),
    /// a JPEG (8 bpp or RGB) or a WebP image. At level 2, a file that is neither G4 TIFF nor JPEG is first written as JPEG with quality 75, which removes
    /// the colormap and degrades the image somewhat.</para>
    /// <para>The bounding box is required when a program such as TeX places and rescales the image. It is sized for fitting the image to an
    /// 8.5 by 11 inch page.</para>
    /// </remarks>
    public static void ConvertToPSEmbed(string filein, string fileout, int level)
    {
        const string func = "convertToPSEmbed";
        if (filein == null)
            throw Lept.Arg(func, "filein not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");
        if (level != 1 && level != 2 && level != 3) {
            Log.Error(func, "invalid level specified; using level 2");
            level = 2;
        }

        if (level == 1) {
            WritePSEmbed(filein, fileout);
            return;
        }

        Pix.FindFileFormat(filein, out int format);
        if (format == L.IFF_JFIF_JPEG) {
            ConvertJpegToPSEmbed(filein, fileout);
            return;
        } else if (format == L.IFF_TIFF_G4) {
            ConvertG4ToPSEmbed(filein, fileout);
            return;
        } else if (format == L.IFF_UNKNOWN) {
            Log.Error(func, $"format of {filein} not known");
            throw Lept.Fail(func, "unknown format");
        }

        if (level == 3 && format == L.IFF_WEBP) {
            Pix pixWebp = Pix.Read(filein);
            Utils.SplitPathAtExtension(filein, out string basename, out _);
            string tmpfile = basename + ".tmpjpg";
            try {
                Pix.Write(tmpfile, pixWebp, L.IFF_JFIF_JPEG);
                ConvertJpegToPSEmbed(tmpfile, fileout);
            } finally {
                Utils.LeptRmfile(tmpfile);
            }
            return;
        }

        if (level == 3) {
            ConvertFlateToPSEmbed(filein, fileout);
            return;
        }

            /* Level 2: convert to jpeg or tiff g4 */
        Pix pixs = Pix.Read(filein);
        int d = pixs.Depth;
        Pix pix;
        if ((d == 2 || d == 4) && pixs.Colormap == null)
            pix = pixs.ConvertTo8(false);
        else if (d == 16)
            pix = pixs.Convert16To8(L.L_MS_BYTE);
        else
            pix = pixs.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);

        d = pix.Depth;
        string tname = Utils.LMakeTempFilename();
        try {
            if (d == 1) {
                Pix.Write(tname, pix, L.IFF_TIFF_G4);
                ConvertG4ToPSEmbed(tname, fileout);
            } else {
                Pix.Write(tname, pix, L.IFF_JFIF_JPEG);
                ConvertJpegToPSEmbed(tname, fileout);
            }
        } finally {
            Utils.LeptRmfile(tname);
        }
    }
}

public sealed partial class Pix
{
    // leptonica: pixWriteSegmentedPageToPS (psio1.c)
    /// <summary>Writes this image as a mixed text and image page to a PostScript file, using a segmentation mask.</summary>
    /// <param name="pixm">A 1 bpp segmentation mask over the image region, or <see langword="null"/> if the page has only text.</param>
    /// <param name="textscale">Scale of the text output relative to this image.</param>
    /// <param name="imagescale">Scale of the image output relative to this image.</param>
    /// <param name="threshold">Threshold for binarization, typically about 190; 0 for the default.</param>
    /// <param name="pageno">The page number in the set; use 1 for a new output file. The page is appended to an existing file if the number is greater than 1.</param>
    /// <param name="fileout">Path of the PostScript file to write.</param>
    /// <remarks>
    /// <para>The page and the mask have the same resolution, typically 300 ppi. They are used to generate two compressed images that go directly into
    /// the PostScript file. The text component is produced by scaling this image by <paramref name="textscale"/> and thresholding to 1 bpp; in
    /// PostScript it is a mask through which black is painted. The image component is the part of this image under the mask, scaled by
    /// <paramref name="imagescale"/>. Typical values are 2.0 for <paramref name="textscale"/> and 0.5 for <paramref name="imagescale"/>.</para>
    /// <para>If the mask is <see langword="null"/>, the page has only text; if it is all black, the page is all image. The output is fitted to a letter-size
    /// (8.5 by 11 inch) page.</para>
    /// <para>Use sequential page numbers with the same output file to write a multi-page file, or different output files with page number 0 or 1 to
    /// write a separate file for each page.</para>
    /// </remarks>
    public void WriteSegmentedPageToPS(Pix? pixm, float textscale, float imagescale,
                                       int threshold, int pageno, string fileout)
    {
        const string func = "pixWriteSegmentedPageToPS";
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");
        if (imagescale <= 0.0f || textscale <= 0.0f)
            throw Lept.Arg(func, "relative scales must be > 0.0");

        bool alltext = true;
        bool notext = false;
        float scaleratio = 1.0f;
        if (pixm != null) {
            alltext = pixm.Zero();
            if (alltext) {
                pixm = null;
            } else {
                Pix pixmi = Invert(null, pixm);
                notext = pixmi.Zero();
                scaleratio = textscale / imagescale;
            }
        }

        Pix? pixb = null;
        Pix? pixc = null;
        if (Depth == 1) {
            pixb = Clone(this);
        } else {
            Pix pixt = ConvertTo8Or32(L.L_CLONE, 0);
            int d = pixt.Depth;

            if (!notext) {
                Pix pixg = (d == 8) ? Copy(null, pixt) : pixt.ConvertRGBToLuminance();
                if (pixm != null)
                    pixg.SetMasked(pixm, 255);
                Pix pixsc;
                if (textscale == 1.0f)
                    pixsc = Clone(pixg);
                else if (textscale >= 0.7f)
                    pixsc = pixg.ScaleGrayLI(textscale, textscale);
                else
                    pixsc = pixg.ScaleAreaMap(textscale, textscale);
                pixb = pixsc.ThresholdToBinary(threshold);
            }

            if (pixm != null) {
                Pix pixsc = (imagescale == 1.0f)
                    ? Clone(pixt)
                    : pixt.Scale(imagescale, imagescale);
                if (!notext) {
                    Pix pixmis = pixm.Scale(imagescale, imagescale);
                    Pix pixmi = Invert(null, pixmis);
                    uint val = (d == 8) ? 0xffu : 0xffffff00u;
                    pixc = Copy(null, pixsc);
                    pixc.SetMasked(pixmi, val);
                } else {
                    pixc = Clone(pixsc);
                }
            }
        }

        PsIo.WriteBoundingBox(false);
        try {
            WriteMixedToPS(pixb, pixc, scaleratio, pageno, fileout);
        } finally {
            PsIo.WriteBoundingBox(true);
        }
    }

    // leptonica: pixWriteMixedToPS (psio1.c)
    /// <summary>Writes a mixed text and image page, given as a text mask and an image, to a PostScript file.</summary>
    /// <param name="pixb">A 1 bpp mask, typically the text, or <see langword="null"/>.</param>
    /// <param name="pixc">An 8 bpp or 32 bpp image of the image regions, or <see langword="null"/>.</param>
    /// <param name="scale">Scale factor for rendering <paramref name="pixb"/> relative to <paramref name="pixc"/>, typically 4.0. It is ignored if one of the images is <see langword="null"/>.</param>
    /// <param name="pageno">The page number in the set; use 1 for a new output file. The page is appended to an existing file if the number is greater than 1.</param>
    /// <param name="fileout">Path of the PostScript file to write.</param>
    /// <remarks>
    /// <para>The two images are typically generated at the resolution at which they are rendered in the PostScript file. The text mask is painted in black.
    /// The image component is white in the rest of the page; to minimize the size of the file it should be rendered at a resolution at least equal to its
    /// actual resolution. Typical resolutions are 600 ppi for the mask and 150 ppi for the image, giving a scale of 4.0.</para>
    /// <para>The image is written with DCT (JPEG) compression, followed by painting the text as black through the mask. If there is no image (the page is all
    /// text), the text is written with the PostScript <c>image</c> operator rather than <c>imagemask</c>, because the latter is unreliable with
    /// some PostScript-to-PDF converters. The output resolution is determined by fitting the result to a letter-size (8.5 by 11 inch) page.</para>
    /// </remarks>
    public static void WriteMixedToPS(Pix? pixb, Pix? pixc, float scale, int pageno, string fileout)
    {
        const string func = "pixWriteMixedToPS";
        if (pixb == null && pixc == null)
            throw Lept.Arg(func, "pixb and pixc both undefined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");

        int resb = 0, resc = 0;
        if (pixc == null) {
            resb = PsIo.GetResLetterPage(pixb!.Width, pixb.Height, 0.0f);
        } else {
            resc = PsIo.GetResLetterPage(pixc.Width, pixc.Height, 0.0f);
            if (pixb != null)
                resb = (int)(scale * resc);
        }

        if (pixc != null) {
            string tname = Utils.LMakeTempFilename();
            try {
                Write(tname, pixc, L.IFF_JFIF_JPEG);
                bool endpage = (pixb == null);
                string op = (pageno <= 1) ? "w" : "a";
                PsIo.ConvertJpegToPS(tname, fileout, op, 0, 0, resc, 1.0f, pageno, endpage);
            } finally {
                Utils.LeptRmfile(tname);
            }
        }

        if (pixb != null) {
            string tname = Utils.LMakeTempFilename();
            try {
                Write(tname, pixb, L.IFF_TIFF_G4);
                string op = (pageno <= 1 && pixc == null) ? "w" : "a";
                bool maskop = (pixc != null);
                PsIo.ConvertG4ToPS(tname, fileout, op, 0, 0, resb, 1.0f, pageno, maskop, true);
            } finally {
                Utils.LeptRmfile(tname);
            }
        }
    }

    // leptonica: pixWriteCompressedToPS (psio1.c)
    /// <summary>Writes this image, compressed and with a bounding box, to a PostScript file.</summary>
    /// <param name="fileout">Path of the PostScript file to write.</param>
    /// <param name="res">Resolution of the image.</param>
    /// <param name="level">The PostScript compression capability: 2 or 3.</param>
    /// <param name="pindex">The number of images already written to the output file. Set it to 0 before the first call; it is incremented when the image is written. The PostScript string is appended to the file if the index is greater than 0.</param>
    /// <remarks>
    /// <para>PostScript level 2 enables lossless G4 (CCITT Group 4) and lossy JPEG compression; level 3 adds lossless Flate compression.
    /// For images with a colormap, Flate is often better than JPEG in both quality and size. For images without a colormap, level 2 (JPEG) usually compresses
    /// better than level 3 (Flate). Because JPEG cannot handle 16 bpp, at level 2 a 16 bpp image is converted to 8 bpp, using the most significant byte.</para>
    /// <para>The encoding is chosen as follows:</para>
    /// <list type="table">
    /// <listheader><term>Image</term><description>Encoding</description></listheader>
    /// <item><term>Colormap, level 2</term><description>JPEG</description></item>
    /// <item><term>Colormap, level 3</term><description>Flate</description></item>
    /// <item><term>1 bpp</term><description>G4</description></item>
    /// <item><term>2, 4, 8, 16 or 32 bpp, level 2</term><description>JPEG</description></item>
    /// <item><term>2, 4, 8, 16 or 32 bpp, level 3</term><description>Flate</description></item>
    /// </list>
    /// </remarks>
    public void WriteCompressedToPS(string fileout, int res, int level, ref int pindex)
    {
        const string func = "pixWriteCompressedToPS";
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");
        if (level != 2 && level != 3) {
            Log.Error(func, "only levels 2 and 3 permitted; using level 2");
            level = 2;
        }

        string tname = Utils.LMakeTempFilename();
        bool writeout = true;
        int d = Depth;
        PixColormap? cmap = Colormap;
        try {
            if (d == 1) {
                Write(tname, this, L.IFF_TIFF_G4);
            } else if (level == 3) {
                Write(tname, this, L.IFF_PNG);
            } else {  /* level == 2 */
                if (cmap != null) {
                    Pix pixt = ConvertForPSWrap();
                    Write(tname, pixt, L.IFF_JFIF_JPEG);
                } else if (d == 16) {
                    Log.Warning(func, "d = 16; converting to 8 bpp for jpeg");
                    Pix pixt = Convert16To8(L.L_MS_BYTE);
                    Write(tname, pixt, L.IFF_JFIF_JPEG);
                } else if (d == 2 || d == 4) {
                    Pix pixt = ConvertTo8(false);
                    Write(tname, pixt, L.IFF_JFIF_JPEG);
                } else if (d == 8 || d == 32) {
                    Write(tname, this, L.IFF_JFIF_JPEG);
                } else {
                    Log.Error(func, $"invalid depth with level 2: {d}");
                    writeout = false;
                }
            }

            if (writeout)
                PsIo.WriteImageCompressedToPSFile(tname, fileout, res, ref pindex);
            else
                throw Lept.Fail(func, "compressed PS image not written");
        } finally {
            Utils.LeptRmfile(tname);
        }
    }
}

public sealed partial class Pixa
{
    // leptonica: pixaWriteCompressedToPS (psio1.c)
    /// <summary>Writes all the images of this <see cref="Pixa"/>, compressed and each with a bounding box, to a multi-page PostScript file.</summary>
    /// <param name="fileout">Path of the PostScript file to write.</param>
    /// <param name="res">Resolution of the images.</param>
    /// <param name="level">The PostScript compression capability: 2 or 3.</param>
    /// <remarks>See <see cref="Pix.WriteCompressedToPS(string, int, int, ref int)"/> for the encoding rules. To make a PDF from the output, convert it with a PostScript-to-PDF tool.</remarks>
    public void WriteCompressedToPS(string fileout, int res, int level)
    {
        const string func = "pixaWriteCompressedToPS";
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");
        if (level != 2 && level != 3) {
            Log.Error(func, "only levels 2 and 3 permitted; using level 2");
            level = 2;
        }

        int index = 0;
        int n = Count;
        for (int i = 0; i < n; i++) {
            Pix pix = GetPix(i, L.L_CLONE);
            try {
                pix.WriteCompressedToPS(fileout, res, level, ref index);
            } catch (Exception) {
                Log.Error(func, $"PS string not written for image {i}");
            }
        }
    }
}
