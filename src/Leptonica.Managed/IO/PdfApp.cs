using System;

namespace Leptonica;

/*
 *  pdfapp.c
 *
 *    Image processing operations on multiple images followed by wrapping
 *    them into a pdf.
 *
 *     Compression of images for prog/compresspdf
 *          PdfIo.CompressFilesToPdf (compressFilesToPdf)
 *
 *     Crop images for prog/croppdf
 *          PdfIo.CropFilesToPdf (cropFilesToPdf)
 *
 *     Cleanup and binarization of images for prog/cleanpdf
 *          PdfIo.CleanTo1bppFilesToPdf (cleanTo1bppFilesToPdf)
 */
/// <summary>Functions that convert images and PDF files into PDF output, and the application-level PDF helpers.</summary>
public static partial class PdfIo
{
    /*---------------------------------------------------------------------*
     *              Compression of images for prog/compresspdf             *
     *---------------------------------------------------------------------*/
    // leptonica: compressFilesToPdf (pdfapp.c)
    /// <summary>Optionally scales and compresses a set of images and wraps them in a PDF file, in the order given.</summary>
    /// <param name="sa">The sorted full path names of the images.</param>
    /// <param name="onebit">1 to enforce 1 bpp G4 (CCITT Group 4) encoding.</param>
    /// <param name="savecolor">If <paramref name="onebit"/> is 1, 1 to preserve the color of images that have color.</param>
    /// <param name="scalefactor">Scaling factor applied to each image; a value of 0.0 or less is set to 1.0 and the maximum is 2.0.</param>
    /// <param name="quality">JPEG quality: 0 for the default of 50; otherwise from 25 to 95.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <param name="fileout">Path of the PDF file to write.</param>
    /// <remarks>
    /// <para>Images in the output are encoded with either G4 or JPEG (DCT) compression, or a mixture, depending on
    /// <paramref name="onebit"/> and <paramref name="savecolor"/>. A 1 bpp image is G4 encoded; otherwise DCT is used.</para>
    /// <list type="bullet">
    /// <item><description><paramref name="onebit"/> = 0: no depth conversion; the encoding depends on the depth.</description></item>
    /// <item><description><paramref name="onebit"/> = 1, <paramref name="savecolor"/> = 0: all images are converted to 1 bpp.</description></item>
    /// <item><description><paramref name="onebit"/> = 1, <paramref name="savecolor"/> = 1: images without color are converted to 1 bpp and images with color keep their color.</description></item>
    /// </list>
    /// <para>If most pages are 1 bpp but some have color to preserve, set both parameters to 1. For a set of low-resolution
    /// 8 bpp images, set <paramref name="onebit"/> to 1 and <paramref name="scalefactor"/> to 2.0 to upscale before binarizing.</para>
    /// <para>Page images at 300 ppi are about 8 Mpixels. When there are more than 25 images, they are stored compressed
    /// after processing, and decompressed one at a time when the PDF is written.</para>
    /// </remarks>
    /// <exception cref="System.ArgumentException"><paramref name="sa"/> or <paramref name="fileout"/> is <see langword="null"/>.</exception>
    public static void CompressFilesToPdf(Sarray sa, int onebit, int savecolor,
                                          float scalefactor, int quality,
                                          string? title, string fileout)
    {
        const string func = "compressFilesToPdf";
        const int maxsmallset = 25;

        if (sa == null)
            throw Lept.Arg(func, "sa not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");
        if (scalefactor <= 0.0f)
            scalefactor = 1.0f;
        if (scalefactor > 2.0f) {
            Log.Warning(func, $"scalefactor {scalefactor} too big; setting to 2.0");
            scalefactor = 2.0f;
        }
        if (quality <= 0)
            quality = 50;
        if (quality < 25) {
            Log.Warning(func, $"quality {quality} too low; setting to 25");
            quality = 25;
        }
        if (quality > 95) {
            Log.Warning(func, $"quality {quality} too high; setting to 95");
            quality = 95;
        }
        int n = sa.Count;
        if (n == 0)
            throw Lept.Arg(func, "sa is empty");

        Pixa? pixa1 = (n <= maxsmallset) ? Pixa.Create(n) : null;
        Pixacomp? pixac1 = (n > maxsmallset) ? Pixacomp.Create(n) : null;
        for (int i = 0; i < n; i++) {
            string fname = sa.GetString(i, L.L_NOCOPY);
            bool processcolor = false;
            Pix pixs = Pix.Read(fname);
            Pix pix1 = pixs.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
            Pix? pix2 = null;
            if (onebit == 0) {
                pix2 = pix1.Scale(scalefactor, scalefactor);
                processcolor = true;
            } else if (savecolor != 0) {
                pix1.ColorFraction(40, 224, 60, 4, out _, out float colorfract);
                if (colorfract > 0.01f) {
                    processcolor = true;
                    pix2 = pix1.Scale(scalefactor, scalefactor);
                }
            }
            if (!processcolor) {
                if (pix1.Depth != 1) {
                    Pix pix3 = pix1.ConvertTo8(false);
                    if (scalefactor < 1.0f || (scalefactor > 1.0f && scalefactor < 2.0f)) {
                        Pix pix4 = pix3.Scale(scalefactor, scalefactor);
                        pix2 = pix4.ConvertTo1(180);
                    } else if (scalefactor == 1.0f) {
                        pix2 = pix3.ConvertTo1(180);
                    } else {  /* scalefactor == 2.0 */
                        pix2 = pix3.ScaleGray2xLIThresh(180);
                    }
                } else {
                    pix2 = pix1.Scale(scalefactor, scalefactor);
                }
            }
            if (n <= maxsmallset)
                pixa1!.AddPix(pix2!, L.L_INSERT);
            else
                pixac1!.AddPix(pix2!, L.IFF_DEFAULT);
        }

        Pix first = (n <= maxsmallset) ? pixa1!.GetPix(0, L.L_CLONE) : pixac1!.GetPix(0);
        int res = first.InferResolution(11.0f);
        if (title == "none")
            title = null;
        if (n <= maxsmallset)
            pixa1!.ConvertToPdf(res, 1.0f, L.L_DEFAULT_ENCODE, quality, title, fileout);
        else
            pixac1!.ConvertToPdf(res, 1.0f, L.L_DEFAULT_ENCODE, quality, title, fileout);
    }

    /*---------------------------------------------------------------------*
     *                       Crop images for prog/croppdf                  *
     *---------------------------------------------------------------------*/
    // leptonica: cropFilesToPdf (pdfapp.c)
    /// <summary>Removes white space around the page images and wraps them in a PDF file that prints with the foreground filling most of the page.</summary>
    /// <param name="sa">The sorted full path names of the images.</param>
    /// <param name="lr_clear">Number of full-resolution pixels cleared at the left and right sides.</param>
    /// <param name="tb_clear">Number of full-resolution pixels cleared at the top and bottom.</param>
    /// <param name="edgeclean">Parameter for removing edge noise, from -1 to 15. The default of 0 removes nothing, 15 is the most aggressive for random noise, -1 aggressively removes side noise, and -2 extracts a page embedded in a black background.</param>
    /// <param name="lr_border">Number of full-resolution pixels added at the left and right sides of the final image.</param>
    /// <param name="tb_border">Number of full-resolution pixels added at the top and bottom of the final image.</param>
    /// <param name="maxwiden">Maximum fractional horizontal stretch allowed.</param>
    /// <param name="printwiden">0 to skip, 1 to fit 8.5 by 11 inch paper, or 2 to fit A4 paper.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <param name="fileout">Path of the PDF file to write.</param>
    /// <remarks>
    /// <para>The images in the output are 1 bpp and G4 (CCITT Group 4) encoded, and are concatenated in the order given.
    /// Output page images are at 300 ppi and are held in memory; up to 200 pages are stored uncompressed, and larger sets are stored G4 compressed.</para>
    /// </remarks>
    /// <exception cref="System.ArgumentException"><paramref name="sa"/> or <paramref name="fileout"/> is <see langword="null"/>, or <paramref name="sa"/> is empty.</exception>
    public static void CropFilesToPdf(Sarray sa, int lr_clear, int tb_clear, int edgeclean,
                                      int lr_border, int tb_border, float maxwiden,
                                      int printwiden, string? title, string fileout)
    {
        const string func = "cropFilesToPdf";
        const int maxsmallset = 200;

        if (sa == null)
            throw Lept.Arg(func, "sa not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");
        int n = sa.Count;
        if (n == 0)
            throw Lept.Arg(func, "sa is empty");

        Pixa? pixa1 = (n <= maxsmallset) ? Pixa.Create(n) : null;
        Pixacomp? pixac1 = (n > maxsmallset) ? Pixacomp.Create(n) : null;
        for (int i = 0; i < n; i++) {
            string fname = sa.GetString(i, L.L_NOCOPY);
            Pix pixs = Pix.Read(fname);
            Pix? pix1;
            try {
                pix1 = pixs.CropImage(lr_clear, tb_clear, edgeclean, lr_border, tb_border,
                                      maxwiden, printwiden, null, out _);
            } catch (Exception) {
                Log.Error(func, $"pix1 not made for i = {i}");
                continue;
            }
            if (n <= maxsmallset)
                pixa1!.AddPix(pix1, L.L_INSERT);
            else
                pixac1!.AddPix(pix1, L.IFF_TIFF_G4);
        }

        Pix first = (n <= maxsmallset) ? pixa1!.GetPix(0, L.L_CLONE) : pixac1!.GetPix(0);
        int res = first.InferResolution(11.0f);
        if (title == "none")
            title = null;
        if (n <= maxsmallset)
            pixa1!.ConvertToPdf(res, 1.0f, L.L_G4_ENCODE, 0, title, fileout);
        else
            pixac1!.ConvertToPdf(res, 1.0f, L.L_G4_ENCODE, 0, title, fileout);
    }

    /*---------------------------------------------------------------------*
     *        Cleanup and binarization of images for prog/cleanpdf         *
     *---------------------------------------------------------------------*/
    // leptonica: cleanTo1bppFilesToPdf (pdfapp.c)
    /// <summary>Deskews, cleans and binarizes a set of images and wraps them in a G4-encoded PDF file, in the order given.</summary>
    /// <param name="sa">The sorted full path names of the images.</param>
    /// <param name="res">Output resolution: 300 or 600 ppi; 0 selects 300.</param>
    /// <param name="contrast">Contrast of the binarization, from 1 (lightest) to 10 (darkest); use 1 unless light features are being lost.</param>
    /// <param name="rotation">Clockwise rotation in units of 90 degrees: 0, 1, 2 or 3.</param>
    /// <param name="opensize">Size of the structuring element for noise removal by opening: 0 or 1 to skip, or 2 or 3 to open.</param>
    /// <param name="title">The PDF title, or <see langword="null"/> for none.</param>
    /// <param name="fileout">Path of the PDF file to write.</param>
    /// <remarks>
    /// <para>Each image is deskewed, optionally rotated and darkened, its background is cleaned to white, and it is binarized,
    /// with optional removal of small noise. For color and gray input, local background normalization is done to 200 and a
    /// threshold of 180 sets the maximum foreground value of the normalized image. If the input is gray or color and
    /// <paramref name="res"/> is 600, the image is expanded by an interpolated factor of 2 before binarizing.</para>
    /// </remarks>
    /// <exception cref="System.ArgumentException"><paramref name="sa"/> or <paramref name="fileout"/> is <see langword="null"/>, or <paramref name="res"/>, <paramref name="contrast"/>, <paramref name="rotation"/> or <paramref name="opensize"/> is invalid.</exception>
    public static void CleanTo1bppFilesToPdf(Sarray sa, int res, int contrast, int rotation,
                                             int opensize, string? title, string fileout)
    {
        const string func = "cleanTo1bppFilesToPdf";
        const int maxsmallset = 200;

        if (sa == null)
            throw Lept.Arg(func, "sa not defined");
        if (fileout == null)
            throw Lept.Arg(func, "fileout not defined");
        if (res == 0)
            res = 300;
        if (res != 300 && res != 600) {
            Log.Error(func, $"invalid res = {res}; res must be in {{0, 300, 600}}");
            throw Lept.Arg(func, "invalid res");
        }
        if (contrast < 1 || contrast > 10) {
            Log.Error(func, $"invalid contrast = {contrast}; contrast must be in [1...10]");
            throw Lept.Arg(func, "invalid contrast");
        }
        if (rotation < 0 || rotation > 3) {
            Log.Error(func, $"invalid rotation = {rotation}; rotation must be in {{0,1,2,3}}");
            throw Lept.Arg(func, "invalid rotation");
        }
        if (opensize > 3) {
            Log.Error(func, $"invalid opensize = {opensize}; opensize must be <= 3");
            throw Lept.Arg(func, "invalid opensize");
        }
        int scale = (res == 300) ? 1 : 2;
        int n = sa.Count;
        if (n == 0)
            throw Lept.Arg(func, "sa is empty");

        Pixa? pixa1 = (n <= maxsmallset) ? Pixa.Create(n) : null;
        Pixacomp? pixac1 = (n > maxsmallset) ? Pixacomp.Create(n) : null;
        for (int i = 0; i < n; i++) {
            string fname = sa.GetString(i, L.L_NOCOPY);
            Pix pixs;
            try {
                pixs = Pix.Read(fname);
            } catch (Exception) {
                Log.Error(func, $"pixs not read from {fname}");
                continue;
            }
            Pix pix1 = pixs.CleanImage(contrast, rotation, scale, opensize);
            if (n <= maxsmallset)
                pixa1!.AddPix(pix1, L.L_INSERT);
            else
                pixac1!.AddPix(pix1, L.IFF_TIFF_G4);
        }

        Pix first = (n <= maxsmallset) ? pixa1!.GetPix(0, L.L_CLONE) : pixac1!.GetPix(0);
        int inferredRes = first.InferResolution(11.0f);
        if (title == "none")
            title = null;
        if (n <= maxsmallset)
            pixa1!.ConvertToPdf(inferredRes, 1.0f, L.L_G4_ENCODE, 0, title, fileout);
        else
            pixac1!.ConvertToPdf(inferredRes, 1.0f, L.L_G4_ENCODE, 0, title, fileout);
    }
}
