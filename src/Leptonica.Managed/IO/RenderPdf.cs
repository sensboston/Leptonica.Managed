namespace Leptonica;

/*
 *  renderpdf.c
 *
 *    Rendering pdf files using an external library
 *          PdfIo.RenderFile (l_pdfRenderFile)
 *          PdfIo.RenderFiles (l_pdfRenderFiles)
 *
 *  Differences from C:
 *    - Running external commands via callSystemDebug("pdftoppm ...") is not
 *      performed in the managed library; directory setup,
 *      validation, resolution computation, and output discovery are preserved.
 */
public static partial class PdfIo
{
    // leptonica: l_pdfRenderFile (renderpdf.c)
    /// <summary>Renders the pages of a single PDF file as images; this is a wrapper for <see cref="RenderFiles"/>.</summary>
    /// <param name="filename">Path of the input PDF file.</param>
    /// <param name="res">Output resolution in ppi: 0 for the default of 300, or a value from 50 to 300.</param>
    /// <returns>The file names of the rendered images, or <see langword="null"/> if rendering is disabled.</returns>
    /// <exception cref="System.ArgumentException"><paramref name="filename"/> is <see langword="null"/>, or <paramref name="res"/> is out of range.</exception>
    public static Sarray? RenderFile(string filename, int res)
    {
        const string func = "l_pdfRenderFile";
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");

        Sarray sain = Sarray.Create(1);
        sain.AddString(filename, L.L_COPY);
        return RenderFiles(null, sain, res);
    }

    // leptonica: l_pdfRenderFiles (renderpdf.c)
    /// <summary>Renders the pages of PDF files as images in a temporary directory.</summary>
    /// <param name="dir">Directory of input PDF files; used only if <paramref name="sain"/> is <see langword="null"/>.</param>
    /// <param name="sain">The paths of the input PDF files, or <see langword="null"/> to use all files with extension <c>pdf</c> in <paramref name="dir"/>.</param>
    /// <param name="res">Output resolution in ppi: 0 for the default of 300, or a value from 50 to 300.</param>
    /// <returns>The file names of the rendered images, or <see langword="null"/> if rendering is disabled.</returns>
    /// <remarks>
    /// <para>Rendering is disabled by default. The managed library does not run the external
    /// <c>pdftoppm</c> program: it prepares the output directory, validates the arguments and
    /// returns the names of the files found in the output directory.</para>
    /// <para>Images are expected in the directory <c>/tmp/lept/renderpdf</c>, named in lexical
    /// order of the input file names. Any existing files in that directory are removed first.</para>
    /// </remarks>
    /// <exception cref="System.ArgumentException">
    /// <paramref name="res"/> is not 0 and not in the range 50 to 300, or neither <paramref name="dir"/> nor <paramref name="sain"/> is given.
    /// </exception>
    public static Sarray? RenderFiles(string? dir, Sarray? sain, int res)
    {
        const string func = "l_pdfRenderFiles";
        if (!Pix.LeptDebugOK)
        {
            Log.Info(func, "running pdftoppm is disabled; use setLeptDebugOK(1) to enable\n");
            return null;
        }

        if (res == 0)
            res = 300;
        if (res < 50 || res > 300)
            throw Lept.Arg(func, "res not in range [50 ... 300]");
        if (dir == null && sain == null)
            throw Lept.Arg(func, "neither dir or sain are defined");

        Sarray sa = sain != null
            ? sain.Copy()
            : Sarray.GetSortedPathnamesInDirectory(dir!, "pdf", 0, 0);

        Utils.LeptRmdir("lept/renderpdf");
        Utils.LeptMkdir("lept/renderpdf");
        string imagedir = Utils.GenPathname("/tmp/lept/renderpdf", null);

        if (sa.Count > 0)
        {
            string firstfile = sa.GetString(0, L.L_NOCOPY);
            int renderRes = GetRendererResolution(firstfile, imagedir);
            _ = (renderRes * res) / 300;
        }

        /* NOTE: callSystemDebug("pdftoppm ...") is not invoked. */
        return Sarray.GetSortedPathnamesInDirectory(imagedir, null, 0, 0);
    }
}
