using System;
using System.IO;

namespace Leptonica;

/*
 *  dewarp4.c: single-page dewarper, book-level reference model operations,
 *  and debugging output.
 */

public static partial class L
{
    /// <summary>Selects JPEG encoding for the images stored in a compressed image array.</summary>
    public const int L_JPEG_ENCODE = 1;
}

public sealed partial class Dewarp
{
    /*----------------------------------------------------------------------*
     *                   Top-level single page dewarper                     *
     *----------------------------------------------------------------------*/
    /// <summary>Dewarps a single page of text, using default values for all the model parameters.</summary>
    /// <param name="pixs">The image of text, of any depth.</param>
    /// <param name="thresh">The threshold for global binarization to 1 bpp; ignored unless <paramref name="adaptive"/> is 0 and the image is not 1 bpp.</param>
    /// <param name="adaptive">1 for adaptive thresholding; 0 for a global threshold. Ignored if the image is 1 bpp.</param>
    /// <param name="useboth">1 to use both the horizontal and the vertical disparity; 0 for vertical only.</param>
    /// <param name="checkColumns">1 to skip the horizontal disparity if there are multiple columns; 0 otherwise. The usual choice is to skip.</param>
    /// <param name="ppixd">Receives the dewarped image; if no model can be built, a copy of <paramref name="pixs"/>.</param>
    /// <param name="pdewa">Receives the array of models holding the single page.</param>
    /// <param name="debug">Use 1 to generate debug output.</param>
    /// <returns>0 if successful; 1 on error.</returns>
    // leptonica: dewarpSinglePage (dewarp4.c)
    public static int SinglePage(Pix pixs, int thresh, int adaptive,
                                 int useboth, int checkColumns,
                                 out Pix ppixd, out Dewarpa? pdewa, int debug)
    {
        return SinglePageCore(pixs, thresh, adaptive, useboth, checkColumns,
                              true, out ppixd, out pdewa, debug);
    }

    /// <summary>Dewarps a single page of text, using default values for all the model parameters, without returning the array of models.</summary>
    /// <param name="pixs">The image of text, of any depth.</param>
    /// <param name="thresh">The threshold for global binarization to 1 bpp; ignored unless <paramref name="adaptive"/> is 0 and the image is not 1 bpp.</param>
    /// <param name="adaptive">1 for adaptive thresholding; 0 for a global threshold. Ignored if the image is 1 bpp.</param>
    /// <param name="useboth">1 to use both the horizontal and the vertical disparity; 0 for vertical only.</param>
    /// <param name="checkColumns">1 to skip the horizontal disparity if there are multiple columns; 0 otherwise. The usual choice is to skip.</param>
    /// <param name="ppixd">Receives the dewarped image; if no model can be built, a copy of <paramref name="pixs"/>.</param>
    /// <param name="debug">Use 1 to generate debug output.</param>
    /// <returns>0 if successful; 1 on error.</returns>
    /// <seealso cref="SinglePage(Pix, int, int, int, int, out Pix, out Dewarpa, int)"/>
    public static int SinglePage(Pix pixs, int thresh, int adaptive,
                                 int useboth, int checkColumns,
                                 out Pix ppixd, int debug)
    {
        return SinglePageCore(pixs, thresh, adaptive, useboth, checkColumns,
                              false, out ppixd, out _, debug);
    }

    private static int SinglePageCore(Pix pixs, int thresh, int adaptive,
                                      int useboth, int checkColumns, bool wantDewa,
                                      out Pix ppixd, out Dewarpa? pdewa, int debug)
    {
        const string func = "dewarpSinglePage";
        pdewa = null;
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");

        SinglePageInit(pixs, thresh, adaptive, useboth, checkColumns,
                       out Pix? pixb, out Dewarpa? dewa);
        if (pixb == null || dewa == null) {
            ppixd = Pix.Copy(null, pixs);
            Log.Error(func, "pixb not made");
            return 1;
        }

        SinglePageRun(pixs, pixb, dewa, out ppixd, debug);
        if (wantDewa)
            pdewa = dewa;
        return 0;
    }

    /// <summary>Binarizes the input image if necessary and initializes an array of models with default parameters.</summary>
    /// <param name="pixs">The image of text, of any depth.</param>
    /// <param name="thresh">The threshold for global binarization to 1 bpp; ignored unless <paramref name="adaptive"/> is 0 and the image is not 1 bpp.</param>
    /// <param name="adaptive">1 for adaptive thresholding; 0 for a global threshold. Ignored if the image is 1 bpp.</param>
    /// <param name="useboth">1 to use both the horizontal and the vertical disparity; 0 for vertical only.</param>
    /// <param name="checkColumns">1 to skip the horizontal disparity if there are multiple columns; 0 otherwise.</param>
    /// <param name="ppixb">Receives the 1 bpp image.</param>
    /// <param name="pdewa">Receives the initialized array of models.</param>
    /// <returns>0 if successful; 1 on error.</returns>
    /// <remarks>
    /// To change the model parameters, call <see cref="Dewarpa.SetCurvatures"/> on the array before calling
    /// <see cref="SinglePageRun"/>.
    /// </remarks>
    // leptonica: dewarpSinglePageInit (dewarp4.c)
    public static int SinglePageInit(Pix pixs, int thresh, int adaptive,
                                     int useboth, int checkColumns,
                                     out Pix? ppixb, out Dewarpa? pdewa)
    {
        const string func = "dewarpSinglePageInit";
        ppixb = null;
        pdewa = null;
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");

        if (pixs.Depth > 1) {
            Pix pix1 = pixs.ConvertTo8(false);
            Pix pix2 = adaptive != 0
                ? pix1.AdaptThresholdToBinary(null, 1.0f)
                : pix1.ThresholdToBinary(thresh);
            ppixb = pix2;
        } else {
            ppixb = Pix.Clone(pixs);
        }

        Dewarpa dewa = Dewarpa.Create(1, 0, 1, 0, -1);
        dewa.UseBothArrays(useboth);
        dewa.SetCheckColumns(checkColumns);
        pdewa = dewa;
        return 0;
    }

    /// <summary>Dewarps a single page using an array of models prepared by <see cref="SinglePageInit"/>.</summary>
    /// <param name="pixs">The image of text, of any depth.</param>
    /// <param name="pixb">The 1 bpp version of the image.</param>
    /// <param name="dewa">The initialized array of models.</param>
    /// <param name="ppixd">Receives the dewarped image; if a model cannot be built, a copy of <paramref name="pixs"/>.</param>
    /// <param name="debug">Use 1 to generate debug output.</param>
    /// <returns>0 if successful; 1 on error.</returns>
    /// <remarks>Any non-default model parameters must be set before this is called.</remarks>
    // leptonica: dewarpSinglePageRun (dewarp4.c)
    public static int SinglePageRun(Pix pixs, Pix pixb, Dewarpa dewa,
                                    out Pix ppixd, int debug)
    {
        const string func = "dewarpSinglePageRun";
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");
        if (pixb == null)
            throw Lept.Arg(func, "pixb not defined");
        if (dewa == null)
            throw Lept.Arg(func, "dewa not defined");

        if (debug != 0)
            Utils.LeptMkdir("lept/dewarp");

        Dewarp dew = Create(pixb, 0);
        dewa.InsertDewarp(dew);
        string? debugfile = debug != 0 ? "/tmp/lept/dewarp/singlepage_model.pdf" : null;
        dew.BuildPageModel(debugfile);
        dewa.ModelStatus(0, out int vsuccess, out _);
        if (vsuccess == 0) {
            Log.Error(func, "failure to build model for vertical disparity");
            ppixd = Pix.Copy(null, pixs);
            return 0;
        }

        debugfile = debug != 0 ? "/tmp/lept/dewarp/singlepage_apply.pdf" : null;
        int ret = dewa.ApplyDisparity(0, pixs, 255, 0, 0, out ppixd, debugfile);
        if (ret != 0)
            Log.Error(func, "invalid model; failure to apply disparity");
        return 0;
    }

    /// <summary>Prints the fields of the model and generates contour images of the disparity arrays.</summary>
    /// <param name="subdirs">One or more subdirectories of the temporary directory, for example <c>dew1</c>, that receive the output.</param>
    /// <param name="index">A number that helps label the output images, for example the page number.</param>
    /// <returns>0 if successful; 1 on error.</returns>
    /// <exception cref="ArgumentException"><paramref name="subdirs"/> is <c>null</c>.</exception>
    // leptonica: dewarpDebug (dewarp4.c)
    public int Debug(string subdirs, int index)
    {
        const string func = "dewarpDebug";
        if (subdirs == null)
            throw Lept.Arg(func, "subdirs not defined");

        Console.Error.WriteLine($"pageno = {pageno}, hasref = {hasref}, refpage = {refpage}");
        Console.Error.WriteLine($"sampling = {sampling}, redfactor = {redfactor}, minlines = {minlines}");
        int svd = 0, shd = 0;
        if (hasref == 0) {
            if (sampvdispar != null) svd = 1;
            if (samphdispar != null) shd = 1;
            Console.Error.WriteLine($"sampv = {svd}, samph = {shd}");
            Console.Error.WriteLine($"w = {w}, h = {h}");
            Console.Error.WriteLine($"nx = {nx}, ny = {ny}");
            Console.Error.WriteLine($"nlines = {nlines}");
            if (svd != 0)
                Console.Error.WriteLine($"(min,max,abs-diff) line curvature = ({mincurv},{maxcurv},{maxcurv - mincurv})");
            if (shd != 0) {
                Console.Error.WriteLine($"(left edge slope = {leftslope}, right edge slope = {rightslope}");
                Console.Error.WriteLine($"(left,right,abs-diff) edge curvature = ({leftcurv},{rightcurv},{Math.Abs(leftcurv - rightcurv)})");
            }
        }
        if (svd == 0 && shd == 0) {
            Console.Error.WriteLine("No disparity arrays");
            return 0;
        }

        PopulateFullRes(null, 0, 0);
        Utils.LeptMkdir(subdirs);
        string outdir = Utils.PathJoin("/tmp", subdirs);
        if (svd != 0) {
            Pix pixv = fullvdispar!.RenderContours(3.0f, 0.15f);
            pixv.WriteDebug($"{outdir}/pixv_{index}.png", L.IFF_PNG);
        }
        if (shd != 0) {
            Pix pixh = fullhdispar!.RenderContours(3.0f, 0.15f);
            pixh.WriteDebug($"{outdir}/pixh_{index}.png", L.IFF_PNG);
        }
        return 0;
    }
}

public sealed partial class Dewarpa
{
    private const int GrayInValue = 200;

    /*----------------------------------------------------------------------*
     *                        Operations on dewarpa                         *
     *----------------------------------------------------------------------*/
    /// <summary>Generates the lists of page numbers that have models, and stores them in the array.</summary>
    /// <remarks>
    /// <para>This generates two lists: the page numbers of the pages that have a page model, and the page numbers of the pages that have either a page model or a reference model.</para>
    /// <para>It can be called at any time, and is called by the serializer before writing.</para>
    /// </remarks>
    // leptonica: dewarpaListPages (dewarp4.c)
    public void ListPages()
    {
        Numa models = Numa.Create(maxpage + 1);
        Numa pages = Numa.Create(maxpage + 1);
        namodels = models;
        napages = pages;
        for (int i = 0; i <= maxpage; i++) {
            Dewarp? dew = GetDewarp(i);
            if (dew != null) {
                if (dew.hasref == 0)
                    models.AddNumber(dew.pageno);
                pages.AddNumber(dew.pageno);
            }
        }
    }

    /// <summary>Determines which page models are valid for rendering.</summary>
    /// <param name="notests">Nonzero to ignore the curvature constraints and treat all successfully built models as valid.</param>
    /// <param name="debug">Nonzero to output information on invalid page models.</param>
    /// <remarks>
    /// <para>A valid model meets the rendering requirements: a vertical disparity model exists, and the curvatures of the vertical and horizontal disparity models satisfy the limits.</para>
    /// <para>The application does not need to call this; <see cref="InsertRefModels"/> calls it, and then discards all invalid models. To inspect an invalid model, do it before calling <see cref="InsertRefModels"/>.</para>
    /// </remarks>
    // leptonica: dewarpaSetValidModels (dewarp4.c)
    public void SetValidModels(int notests, int debug)
    {
        const string func = "dewarpaSetValidModels";
        int n = maxpage + 1;
        for (int i = 0; i < n; i++) {
            Dewarp? dew = GetDewarp(i);
            if (dew == null)
                continue;

            if (debug != 0) {
                if (dew.hasref == 1) {
                    Log.Info(func, $"page {i}: has only a ref model");
                } else if (dew.vsuccess == 0) {
                    Log.Info(func, $"page {i}: no model successfully built");
                } else if (notests == 0) {
                    int maxcurv = Math.Max(Math.Abs(dew.mincurv), Math.Abs(dew.maxcurv));
                    int diffcurv = dew.maxcurv - dew.mincurv;
                    if (useboth != 0 && dew.hsuccess == 0)
                        Log.Info(func, $"page {i}: useboth, but no horiz disparity");
                    if (maxcurv > max_linecurv)
                        Log.Info(func, $"page {i}: max curvature {diffcurv} > max_linecurv");
                    if (diffcurv < min_diff_linecurv)
                        Log.Info(func, $"page {i}: diff curv {diffcurv} < min_diff_linecurv");
                    if (diffcurv > max_diff_linecurv)
                        Log.Info(func, $"page {i}: abs diff curv {diffcurv} > max_diff_linecurv");
                    if (dew.hsuccess != 0) {
                        if (Math.Abs(dew.leftslope) > max_edgeslope)
                            Log.Info(func, $"page {i}: abs left slope {dew.leftslope} > max_edgeslope");
                        if (Math.Abs(dew.rightslope) > max_edgeslope)
                            Log.Info(func, $"page {i}: abs right slope {dew.rightslope} > max_edgeslope");
                        int diffedge = Math.Abs(dew.leftcurv - dew.rightcurv);
                        if (Math.Abs(dew.leftcurv) > max_edgecurv)
                            Log.Info(func, $"page {i}: left curvature {dew.leftcurv} > max_edgecurv");
                        if (Math.Abs(dew.rightcurv) > max_edgecurv)
                            Log.Info(func, $"page {i}: right curvature {dew.rightcurv} > max_edgecurv");
                        if (diffedge > max_diff_edgecurv)
                            Log.Info(func, $"page {i}: abs diff left-right curv {diffedge} > max_diff_edgecurv");
                    }
                }
            }

            TestForValidModel(dew, notests);
        }
    }

    /// <summary>Discards the invalid page models and inserts reference models where possible.</summary>
    /// <param name="notests">Nonzero to ignore the curvature constraints on the models.</param>
    /// <param name="debug">Nonzero to output information on invalid page models.</param>
    /// <remarks>
    /// <para>For each page without a valid model, this clears any existing invalid and reference models, finds the nearest valid model of the same parity, and inserts an empty model that refers to that page.</para>
    /// <para>If both disparity arrays are used, it tries to replace any model or reference without a valid horizontal array by a reference that has one; if none is found, it uses the closest valid model.</para>
    /// <para>A reference model must be within the maximum distance. Because of the parity constraint, no reference models are used if the maximum distance is less than 2.</para>
    /// <para>This must be called even if reference models will not be used, after building the models on all available pages and after setting the rendering parameters. It is also called when a serialized array is read back, and whenever the rendering parameters change.</para>
    /// <para>If it was called with both arrays in use and that setting is then turned off, call <see cref="RestoreModels"/> first to bring the real models back from the cache.</para>
    /// </remarks>
    // leptonica: dewarpaInsertRefModels (dewarp4.c)
    public void InsertRefModels(int notests, int debug)
    {
        const string func = "dewarpaInsertRefModels";
        if (maxdist < 2)
            Log.Info(func, "maxdist < 2; no ref models can be used");

        SetValidModels(notests, debug);
        int n = maxpage + 1;
        Numa na = Numa.MakeConstant(0, n);
        for (int i = 0; i < n; i++) {
            Dewarp? dew = GetDewarp(i);
            if (dew != null && dew.vvalid != 0)
                na.ReplaceNumber(i, 1);
        }

        RestoreModels();

        for (int i = 0; i < n; i++) {
            int val = na.GetIValue(i);
            if (val == 1) continue;
            Dewarp? dew = dewarp[i];
            if (dew != null) {
                dewarpcache[i] = dew;
                dewarp[i] = null;
            }
            if (maxdist < 2) continue;

            int distdown = maxdist + 1;
            int distup = maxdist + 1;
            for (int j = i - 2; j >= 0 && distdown > maxdist; j -= 2) {
                if (na.GetIValue(j) == 1)
                    distdown = i - j;
            }
            for (int j = i + 2; j < n && distup > maxdist; j += 2) {
                if (na.GetIValue(j) == 1)
                    distup = j - i;
            }
            int min = Math.Min(distdown, distup);
            if (min > maxdist) continue;
            if (distdown <= distup)
                InsertDewarp(Dewarp.CreateRef(i, i - distdown));
            else
                InsertDewarp(Dewarp.CreateRef(i, i + distup));
        }

        if (useboth == 0) {
            modelsready = 1;
            return;
        }

        Numa nah = Numa.MakeConstant(0, n);
        for (int i = 0; i < n; i++) {
            Dewarp? dew = GetDewarp(i);
            if (dew != null && dew.hvalid != 0)
                nah.ReplaceNumber(i, 1);
        }
        for (int i = 0; i < n; i++) {
            if (nah.GetIValue(i) == 1) continue;
            if (maxdist < 2) continue;
            int distdown = 100000;
            int distup = 100000;
            for (int j = i - 2; j >= 0; j -= 2) {
                if (nah.GetIValue(j) == 1) {
                    distdown = i - j;
                    break;
                }
            }
            for (int j = i + 2; j < n; j += 2) {
                if (nah.GetIValue(j) == 1) {
                    distup = j - i;
                    break;
                }
            }
            int min = Math.Min(distdown, distup);
            if (min > maxdist) continue;

            Dewarp? dew = GetDewarp(i);
            if (dew == null) {
                Log.Error(func, $"dew is null for page {i}!");
            } else if (dew.hasref == 0) {
                dewarpcache[i] = dew;
                dewarp[i] = null;
            }
            if (distdown <= distup)
                InsertDewarp(Dewarp.CreateRef(i, i - distdown));
            else
                InsertDewarp(Dewarp.CreateRef(i, i + distup));
        }

        modelsready = 1;
    }

    /// <summary>Removes all the models that refer to nearby pages instead of having a page model of their own.</summary>
    /// <remarks>The removed references were generated by <see cref="InsertRefModels"/>.</remarks>
    // leptonica: dewarpaStripRefModels (dewarp4.c)
    public void StripRefModels()
    {
        for (int i = 0; i <= maxpage; i++) {
            Dewarp? dew = GetDewarp(i);
            if (dew != null && dew.hasref != 0)
                dewarp[i] = null;
        }
        modelsready = 0;
        ListPages();
    }

    /// <summary>Puts all the real models, and only the real models, in the primary array.</summary>
    /// <remarks>
    /// This first removes the models that only refer to other pages, and then moves the cached models back into the
    /// primary array. The reference models must then be recomputed and inserted before the models are ready for use.
    /// </remarks>
    // leptonica: dewarpaRestoreModels (dewarp4.c)
    public void RestoreModels()
    {
        const string func = "dewarpaRestoreModels";
        StripRefModels();

        for (int i = 0; i <= maxpage; i++) {
            Dewarp? dew = dewarpcache[i];
            if (dew != null) {
                if (dewarp[i] != null) {
                    Log.Error(func, $"dew in both cache and main array!: page {i}");
                } else {
                    dewarp[i] = dew;
                    dewarpcache[i] = null;
                }
            }
        }
        modelsready = 0;
        ListPages();
    }

    /*----------------------------------------------------------------------*
     *                      Dewarp debugging output                         *
     *----------------------------------------------------------------------*/
    /// <summary>Writes a summary of the array of models to a stream.</summary>
    /// <param name="fp">The stream to write to.</param>
    /// <exception cref="ArgumentException"><paramref name="fp"/> is <c>null</c>.</exception>
    // leptonica: dewarpaInfo (dewarp4.c)
    public void Info(Stream fp)
    {
        const string func = "dewarpaInfo";
        if (fp == null)
            throw Lept.Arg(func, "stream not defined");

        CStdio.Fprintf(fp, $"\nDewarpaInfo\n");
        CStdio.Fprintf(fp, $"nalloc = {nalloc}, maxpage = {maxpage}\n");
        CStdio.Fprintf(fp, $"sampling = {sampling}, redfactor = {redfactor}, minlines = {minlines}\n");
        CStdio.Fprintf(fp, $"maxdist = {maxdist}, useboth = {useboth}\n");

        ModelStats(out int nnone, out int nvsuccess, out int nvvalid,
                   out int nhsuccess, out int nhvalid, out int nref);
        int n = napages!.Count;
        Console.Error.WriteLine($"Total number of pages with a dew = {n}");
        Console.Error.WriteLine($"Number of pages without any models = {nnone}");
        Console.Error.WriteLine($"Number of pages with a vert model = {nvsuccess}");
        Console.Error.WriteLine($"Number of pages with a valid vert model = {nvvalid}");
        Console.Error.WriteLine($"Number of pages with both models = {nhsuccess}");
        Console.Error.WriteLine($"Number of pages with both models valid = {nhvalid}");
        Console.Error.WriteLine($"Number of pages with a ref model = {nref}");

        for (int i = 0; i < n; i++) {
            int pageno = napages.GetIValue(i);
            Dewarp? dew = GetDewarp(pageno);
            if (dew == null) continue;
            Console.Error.WriteLine($"Page: {dew.pageno}");
            Console.Error.WriteLine($"  hasref = {dew.hasref}, refpage = {dew.refpage}");
            Console.Error.WriteLine($"  nlines = {dew.nlines}");
            Console.Error.WriteLine($"  w = {dew.w}, h = {dew.h}, nx = {dew.nx}, ny = {dew.ny}");
            if (dew.sampvdispar != null) {
                Console.Error.WriteLine("  Vertical disparity builds:");
                Console.Error.WriteLine($"    (min,max,abs-diff) line curvature = ({dew.mincurv},{dew.maxcurv},{dew.maxcurv - dew.mincurv})");
            }
            if (dew.samphdispar != null) {
                Console.Error.WriteLine("  Horizontal disparity builds:");
                Console.Error.WriteLine($"    left edge slope = {dew.leftslope}, right edge slope = {dew.rightslope}");
                Console.Error.WriteLine($"    (left,right,abs-diff) edge curvature = ({dew.leftcurv},{dew.rightcurv},{Math.Abs(dew.leftcurv - dew.rightcurv)})");
            }
        }
    }

    /// <summary>Counts the pages by the kind of model they have.</summary>
    /// <param name="pnnone">Receives the number of pages without a model.</param>
    /// <param name="pnvsuccess">Receives the number of pages with a vertical model.</param>
    /// <param name="pnvvalid">Receives the number of pages with a valid vertical model.</param>
    /// <param name="pnhsuccess">Receives the number of pages with both a vertical and a horizontal model.</param>
    /// <param name="pnhvalid">Receives the number of pages with both models valid.</param>
    /// <param name="pnref">Receives the number of pages with a reference model.</param>
    /// <remarks>
    /// <para>A page without a model most likely failed to produce a vertical model and was not assigned a reference model from a neighboring page.</para>
    /// <para>A model may be built yet invalid; <see cref="InsertRefModels"/> then caches it and tries to replace it with a valid reference model.</para>
    /// <para>This ignores the stored validity flags and tests each model against the constraints.</para>
    /// </remarks>
    // leptonica: dewarpaModelStats (dewarp4.c)
    public void ModelStats(out int pnnone, out int pnvsuccess, out int pnvvalid,
                           out int pnhsuccess, out int pnhvalid, out int pnref)
    {
        ListPages();
        int n = napages!.Count;
        int nnone = 0, nref = 0, nvsuccess = 0, nvvalid = 0, nhsuccess = 0, nhvalid = 0;
        for (int i = 0; i < n; i++) {
            int pageno = napages.GetIValue(i);
            Dewarp? dew = GetDewarp(pageno);
            if (dew == null) {
                nnone++;
                continue;
            }
            if (dew.hasref == 1) nref++;
            if (dew.vsuccess == 1) nvsuccess++;
            if (dew.hsuccess == 1) nhsuccess++;
            TestForValidModel(dew, 0);
            if (dew.vvalid == 1) nvvalid++;
            if (dew.hvalid == 1) nhvalid++;
        }

        pnnone = nnone;
        pnref = nref;
        pnvsuccess = nvsuccess;
        pnvvalid = nvvalid;
        pnhsuccess = nhsuccess;
        pnhvalid = nhvalid;
    }

    // leptonica: dewarpaTestForValidModel (dewarp4.c)
    private void TestForValidModel(Dewarp dew, int notests)
    {
        const string func = "dewarpaTestForValidModel";
        if (dew == null)
            return;

        if (notests != 0) {
            dew.vvalid = dew.vsuccess;
            dew.hvalid = dew.hsuccess;
            return;
        }

        if (dew.vsuccess == 0 || dew.hasref == 1)
            return;

        int maxcurv = Math.Max(Math.Abs(dew.mincurv), Math.Abs(dew.maxcurv));
        int diffcurv = dew.maxcurv - dew.mincurv;
        if (maxcurv <= max_linecurv &&
            diffcurv >= min_diff_linecurv &&
            diffcurv <= max_diff_linecurv) {
            dew.vvalid = 1;
        } else {
            Log.Info(func, $"invalid vert model for page {dew.pageno}:");
        }

        if (dew.hsuccess != 0) {
            int diffedge = Math.Abs(dew.leftcurv - dew.rightcurv);
            if (Math.Abs(dew.leftslope) <= max_edgeslope &&
                Math.Abs(dew.rightslope) <= max_edgeslope &&
                Math.Abs(dew.leftcurv) <= max_edgecurv &&
                Math.Abs(dew.rightcurv) <= max_edgecurv &&
                diffedge <= max_diff_edgecurv) {
                dew.hvalid = 1;
            } else {
                Log.Info(func, $"invalid horiz model for page {dew.pageno}:");
            }
        }
    }

    /// <summary>Generates a PDF of contour plots of the disparity arrays.</summary>
    /// <param name="scalefact">The scale factor applied to the contour images; typically 0.5.</param>
    /// <param name="first">The first page model to render.</param>
    /// <param name="last">The last page model to render; 0 means to the end.</param>
    /// <returns>0 if successful; 1 on error.</returns>
    /// <remarks>Only actual models are shown, not reference models.</remarks>
    // leptonica: dewarpaShowArrays (dewarp4.c)
    public int ShowArrays(float scalefact, int first, int last)
    {
        const string func = "dewarpaShowArrays";
        if (first < 0 || first > maxpage) {
            Log.Error(func, "first out of bounds");
            return 1;
        }
        if (last <= 0 || last > maxpage)
            last = maxpage;
        if (last < first) {
            Log.Error(func, "last < first");
            return 1;
        }

        Utils.LeptRmdir("lept/dewarp1");
        Utils.LeptMkdir("lept/dewarp1");
        L_Bmf bmf = L_Bmf.Create(null, 8);

        Console.Error.WriteLine("Generating contour plots");
        for (int i = first; i <= last; i++) {
            if (i != 0 && (i % 10) == 0)
                Console.Error.Write($" .. {i}");
            Dewarp? dew = GetDewarp(i);
            if (dew == null || dew.hasref == 1)
                continue;
            bool svd = dew.sampvdispar != null;
            bool shd = dew.samphdispar != null;
            if (!svd) {
                Log.Error(func, $"sampvdispar not made for page {i}!");
                continue;
            }

            dew.PopulateFullRes(null, 0, 0);
            Pix pixv = dew.fullvdispar!.RenderContours(3.0f, 0.15f);
            Pix pixvs = pixv.ScaleBySampling(scalefact, scalefact);
            Pix? pixhs = null;
            if (shd) {
                Pix pixh = dew.fullhdispar!.RenderContours(3.0f, 0.15f);
                pixhs = pixh.ScaleBySampling(scalefact, scalefact);
            }
            dew.Minimize();

            Pixa pixa = Pixa.Create(2);
            pixa.AddPix(pixvs, L.L_INSERT);
            if (shd && pixhs != null)
                pixa.AddPix(pixhs, L.L_INSERT);
            Pix pixt = pixa.DisplayTiledInRows(32, 1500, 1.0f, 0, 30, 2);
            Pix pixd = pixt.AddSingleTextblock(bmf, $"Page {i}", 0x0000ff00u,
                                               L.L_ADD_BELOW, out _);
            pixd.WriteDebug($"/tmp/lept/dewarp1/arrays_{i:D4}.png", L.IFF_PNG);
        }
        Console.Error.WriteLine();

        Console.Error.WriteLine("Generating pdf of contour plots");
        PdfIo.ConvertFilesToPdf("/tmp/lept/dewarp1", "arrays_", 90, 1.0f,
                                L.L_FLATE_ENCODE, 0, "Disparity arrays",
                                "/tmp/lept/disparity_arrays.pdf");
        Console.Error.WriteLine("Output written to: /tmp/lept/disparity_arrays.pdf");
        return 0;
    }

    /// <summary>Generates a PDF of before and after image pairs for a range of pages.</summary>
    /// <param name="sa">The indexed input image file names.</param>
    /// <param name="boxa">The crop boxes for the input images, or <c>null</c>. Its elements are aligned with the numbers in the file names.</param>
    /// <param name="firstpage">The first page to include.</param>
    /// <param name="lastpage">The last page to include.</param>
    /// <param name="pdfout">The path of the output PDF file.</param>
    /// <returns>0 if successful; 1 on error.</returns>
    /// <exception cref="ArgumentException"><paramref name="sa"/> is <c>null</c>.</exception>
    /// <remarks>If crop boxes are given, the models are assumed to have been generated from the cropped images; no undercropping is applied before rendering.</remarks>
    // leptonica: dewarpShowResults (dewarp4.c)
    public int ShowResults(Sarray sa, Boxa? boxa, int firstpage, int lastpage, string pdfout)
    {
        const string func = "dewarpShowResults";
        if (sa == null)
            throw Lept.Arg(func, "sa not defined");
        if (pdfout == null)
            throw Lept.Arg(func, "pdfout not defined");
        if (firstpage > lastpage) {
            Log.Error(func, "invalid first/last page numbers");
            return 1;
        }

        Utils.LeptRmdir("lept/dewarp_pdfout");
        Utils.LeptMkdir("lept/dewarp_pdfout");
        L_Bmf bmf = L_Bmf.Create(null, 6);

        Console.Error.WriteLine("Dewarping and generating s/by/s view");
        for (int i = firstpage; i <= lastpage; i++) {
            if (i != 0 && (i % 10 == 0))
                Console.Error.Write($".. {i} ");
            Pix? pixs = Pix.ReadIndexed(sa, i);
            if (pixs == null)
                throw Lept.Fail(func, $"pixs not read at index {i}");
            Pix pixc;
            if (boxa != null) {
                Box box = boxa.GetBox(i, L.L_CLONE);
                pixc = pixs.ClipRectangle(box, out _)!;
            } else {
                pixc = Pix.Clone(pixs);
            }
            Dewarp? dew = GetDewarp(i);
            Pix? pixd = null;
            if (dew != null) {
                ApplyDisparity(dew.pageno, pixc, GrayInValue, 0, 0, out pixd, null);
                dew.Minimize();
            }
            Pixa pixa = Pixa.Create(2);
            pixa.AddPix(pixc, L.L_INSERT);
            if (pixd != null)
                pixa.AddPix(pixd, L.L_INSERT);
            Pix pixt1 = pixa.DisplayTiledAndScaled(32, 500, 2, 0, 35, 2);
            string bufstr;
            if (dew != null) {
                int modelpage = dew.hasref != 0 ? dew.refpage : dew.pageno;
                bufstr = $"Page {i}; using {modelpage}\n";
            } else {
                bufstr = $"Page {i}; no dewarp\n";
            }
            Pix pixt2 = pixt1.AddSingleTextblock(bmf, bufstr, 0x0000ff00u,
                                                 L.L_ADD_BELOW, out _);
            pixt2.WriteDebug($"/tmp/lept/dewarp_pdfout/{i:D5}", L.IFF_JFIF_JPEG);
        }
        Console.Error.WriteLine();

        Console.Error.WriteLine("Generating pdf of result");
        PdfIo.ConvertFilesToPdf("/tmp/lept/dewarp_pdfout", null, 100, 1.0f,
                                L.L_JPEG_ENCODE, 0, "Dewarp sequence", pdfout);
        Console.Error.WriteLine($"Output written to: {pdfout}");
        return 0;
    }
}
