using System;

namespace Leptonica;

/*
 *  convertfiles.c
 *
 *      Conversion to 1 bpp
 *          Pix.ConvertFilesTo1bpp (convertFilesTo1bpp)
 */
public sealed partial class Pix
{
    /*------------------------------------------------------------------*
     *                        Conversion to 1 bpp                       *
     *------------------------------------------------------------------*/
    // leptonica: convertFilesTo1bpp (convertfiles.c)
    /// <summary>Converts selected image files in a directory to 1 bpp and writes them to an output directory.</summary>
    /// <param name="dirin">input directory</param>
    /// <param name="substr">[optional] substring filter on filenames; can be null</param>
    /// <param name="upscaling">1, 2 or 4; only for input color or grayscale</param>
    /// <param name="thresh">global threshold for binarization; 0 for default</param>
    /// <param name="firstpage">0-based first page index</param>
    /// <param name="npages">use 0 to do all from firstpage to the end</param>
    /// <param name="dirout">output directory</param>
    /// <param name="outformat">IFF_PNG, IFF_TIFF_G4</param>
    /// <remarks>
    /// (1) Images are sorted lexicographically, and the names in the
    ///     output directory are retained except for the extension.
    /// </remarks>
    public static void ConvertFilesTo1bpp(string dirin, string? substr, int upscaling,
                                          int thresh, int firstpage, int npages,
                                          string dirout, int outformat)
    {
        const string func = "convertFilesTo1bpp";
        if (dirin == null)
            throw Lept.Arg(func, "dirin");
        if (dirout == null)
            throw Lept.Arg(func, "dirout");
        if (upscaling != 1 && upscaling != 2 && upscaling != 4)
            throw Lept.Arg(func, "invalid upscaling factor");
        if (thresh <= 0) thresh = 180;
        if (firstpage < 0) firstpage = 0;
        if (npages < 0) npages = 0;
        if (outformat != L.IFF_TIFF_G4)
            outformat = L.IFF_PNG;

        Sarray safiles = Sarray.GetSortedPathnamesInDirectory(dirin, substr, firstpage, npages);
        int nfiles = safiles.Count;
        if (nfiles == 0)
            throw Lept.Fail(func, "no matching files in the directory");

        for (int i = 0; i < nfiles; i++) {
            string fname = safiles.GetString(i, L.L_NOCOPY);
            Pix pixs;
            try {
                pixs = Read(fname);
            } catch (Exception ex) when (ex is ArgumentException or LeptonicaException) {
                Log.Warning(func, $"Couldn't read file {fname}");
                continue;
            }

            Pix pixg1 = pixs.Depth == 32
                ? pixs.ConvertRGBToLuminance()
                : Clone(pixs);
            Pix pixg2 = pixg1.RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
            Pix pixb;
            if (pixg2.Depth == 1) {
                pixb = Clone(pixg2);
            } else {
                if (upscaling == 1)
                    pixb = pixg2.ThresholdToBinary(thresh);
                else if (upscaling == 2)
                    pixb = pixg2.ScaleGray2xLIThresh(thresh);
                else  /* upscaling == 4 */
                    pixb = pixg2.ScaleGray4xLIThresh(thresh);
            }

            Utils.SplitPathAtDirectory(fname, out _, out string tail);
            Utils.SplitPathAtExtension(tail, out string basename, out _);
            if (outformat == L.IFF_TIFF_G4) {
                string buf = $"{dirout}/{basename}.tif";
                Write(buf, pixb, L.IFF_TIFF_G4);
            } else {
                string buf = $"{dirout}/{basename}.png";
                Write(buf, pixb, L.IFF_PNG);
            }
        }
    }
}
