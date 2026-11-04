using System;
using System.IO;

namespace Leptonica;

/*
 *  dewarp2.c: build the page and line disparity models.
 */

public sealed partial class Dewarp
{
    private const float MinRatioLinesToHeight = 0.45f;
    private const int MinLinesForHoriz1 = 10;
    private const int MinLinesForHoriz2 = 3;
    private const float AllowedWidthFract = 0.05f;

    /*----------------------------------------------------------------------*
     *                   Build basic page disparity model                   *
     *----------------------------------------------------------------------*/
    /// <summary>Builds the vertical and horizontal disparity arrays for a page of text.</summary>
    /// <param name="debugfile">The path of a file for debug output, or <c>null</c> to skip debug output.</param>
    /// <returns>0 if successful; 1 if the model could not be built.</returns>
    /// <remarks>
    /// <para>
    /// The disparity arrays give, for each pixel of the dewarped image, the corresponding pixel of the
    /// input image. <see cref="VSuccess"/> is set if the vertical disparity array is built. The horizontal
    /// disparity array is always attempted, even if it will not be used, and <see cref="HSuccess"/> is set if it is built.
    /// </para>
    /// <para>The method is as follows:</para>
    /// <list type="number">
    /// <item><description>Estimate the points along the centers of all the long text lines. If there are too few lines, no disparity models are built.</description></item>
    /// <item><description>From the vertical deviation of the lines, estimate the vertical disparity.</description></item>
    /// <item><description>From the ends of the lines, estimate the horizontal disparity, assuming that the text is close to left and right justified.</description></item>
    /// </list>
    /// <para>
    /// For the vertical disparity, a least-squares quadratic is fit to the center locations along each line, each
    /// curve is sampled at a regular interval and compared with the mid-point value, and a second quadratic is fit to each
    /// set of vertically aligned samples. The result is a regular grid of smoothed vertical disparity values.
    /// </para>
    /// <para>
    /// The sampled array can be interpolated to full resolution and applied to the image to make all text lines
    /// horizontal. Likewise, the horizontal disparity array is used to left- and right-align the longest text lines.
    /// </para>
    /// </remarks>
    // leptonica: dewarpBuildPageModel (dewarp2.c)
    public int BuildPageModel(string? debugfile)
    {
        const string func = "dewarpBuildPageModel";
        debug = debugfile != null ? 1 : 0;
        vsuccess = hsuccess = 0;
        Pix src = pixs!;
        if (debugfile != null) {
            Utils.LeptRmdir("lept/dewmod");
            Utils.LeptMkdir("lept/dewmod");
            src.DisplayWithTitle(0, 0, "pixs", 1);
            src.WriteDebug("/tmp/lept/dewmod/0010.png", L.IFF_PNG);
        }

            /* Make initial estimate of centers of textlines */
        Ptaa? ptaa1 = GetTextlineCenters(src, debugfile != null ? 1 : 0);
        if (ptaa1 == null) {
            Log.Warning(func, "textline centers not found; model not built");
            return 1;
        }
        if (debugfile != null) {
            Pix pix1 = src.ConvertTo32();
            Pta ptaCirc = Pix.GeneratePtaFilledCircle(1);
            Pix pix2 = Pix.GenerateFromPta(ptaCirc, 5, 5);
            Pix pix3 = Pix.DisplayPtaaPattern(null, pix1, ptaa1, pix2, 2, 2);
            pix3.WriteDebug("/tmp/lept/dewmod/0020.png", L.IFF_PNG);
        }

            /* Remove all lines that are not at least 0.8 times the length of the longest line */
        Ptaa ptaa2 = RemoveShortLines(src, ptaa1, 0.8f, debugfile != null ? 1 : 0);
        if (debugfile != null) {
            Pix pix1 = src.ConvertTo32();
            Pta ptaCirc = Pix.GeneratePtaFilledCircle(1);
            Pix pix2 = Pix.GenerateFromPta(ptaCirc, 5, 5);
            Pix pix3 = Pix.DisplayPtaaPattern(null, pix1, ptaa2, pix2, 2, 2);
            pix3.WriteDebug("/tmp/lept/dewmod/0030.png", pix3.InputFormat != 0 ? L.IFF_PNG : L.IFF_PNG);
        }

            /* Verify that there are sufficient "long" lines */
        int linecount = ptaa2.Count;
        if (linecount < minlines) {
            Log.Warning(func, $"linecount {linecount} < min req'd number of lines ({minlines}) for model");
            return 1;
        }

            /* Verify that the lines have a reasonable coverage of the vertical extent */
        if (!IsLineCoverageValid(ptaa2, src.Height, out int ntop, out int nbot,
                                 out int ytop, out int ybot)) {
            Log.Warning(func, $"invalid line coverage: ntop = {ntop}, nbot = {nbot}; spanning [{ytop} ... {ybot}] in height {src.Height}");
            return 1;
        }

            /* Get the sampled vertical disparity from the textline centers */
        if (FindVertDisparity(ptaa2, 0) != 0) {
            Log.Warning(func, "vertical disparity not built");
            return 1;
        }

            /* Get the sampled horizontal disparity from the left and right edges */
        int ret = FindHorizDisparity(ptaa2);
        if (ret == 0)
            Log.Info(func, "hsuccess = 1");

        if (debugfile != null) {
            PopulateFullRes(null, 0, 0);
            Pix pix1 = fullvdispar!.RenderContours(3.0f, 0.15f);
            pix1.WriteDebug("/tmp/lept/dewmod/0060.png", L.IFF_PNG);
            pix1.Display(1000, 0);
            if (ret == 0) {
                pix1 = fullhdispar!.RenderContours(3.0f, 0.15f);
                pix1.WriteDebug("/tmp/lept/dewmod/0070.png", L.IFF_PNG);
                pix1.Display(1000, 0);
            }
            PdfIo.ConvertFilesToPdf("/tmp/lept/dewmod", null, 135, 1.0f, 0, 0,
                                    "Dewarp Build Model", debugfile);
            Console.Error.WriteLine($"pdf file: {debugfile}");
        }

        return 0;
    }

    /// <summary>Builds the sampled vertical disparity array from points along the centers of the text lines.</summary>
    /// <param name="ptaa">The unsmoothed text lines, not vertically ordered.</param>
    /// <param name="rotflag">0 if the page image is used as it is; 1 if it is rotated by 90 degrees clockwise.</param>
    /// <returns>0 if successful; 1 on error.</returns>
    /// <remarks>
    /// <para>
    /// This fits quadratics, first along the lines and then in the vertical direction, to generate the
    /// sampled vertical disparity array. The array can then be interpolated to full resolution and used to remove
    /// the vertical line warping.
    /// </para>
    /// <para>Use <paramref name="rotflag"/> = 1 when dewarping vertical lines, as <see cref="BuildLineModel"/> does.</para>
    /// <para>The model is not checked against the constraints for validity; that is done after the models are built and before reference models are inserted.</para>
    /// <para><see cref="VSuccess"/> is set on success.</para>
    /// </remarks>
    // leptonica: dewarpFindVertDisparity (dewarp2.c)
    public int FindVertDisparity(Ptaa ptaa, int rotflag)
    {
        const string func = "dewarpFindVertDisparity";
        vsuccess = 0;
        if (ptaa == null)
            throw Lept.Arg(func, "ptaa not defined");

        if (debug != 0)
            Log.Info(func, "finding vertical disparity");

        int samp = sampling;
        int curNx = rotflag != 0 ? ny : nx;
        int curNy = rotflag != 0 ? nx : ny;
        int numLines = ptaa.Count;
        nlines = numLines;
        Ptaa ptaa0 = Ptaa.Create(numLines);
        Numa nacurve0 = Numa.Create(numLines);
        Pix pixdb = rotflag != 0 ? pixs!.RotateOrth(1) : Pix.Clone(pixs!);
        for (int i = 0; i < numLines; i++) {
            Pta ptaLine = ptaa.GetPta(i, L.L_CLONE);
            ptaLine.GetQuadraticLSF(out float c2, out float c1, out float c0, out _);
            nacurve0.AddNumber(c2);
            Pta ptad = Pta.Create(curNx);
            for (int j = 0; j < curNx; j++) {
                float x = j * samp;
                float y = Pta.ApplyQuadraticFit(c2, c1, c0, x);
                ptad.AddPt(x, y);
            }
            ptaa0.AddPta(ptad, L.L_INSERT);
        }

        if (debug != 0) {
            Utils.LeptMkdir("lept/dewarp");
            Utils.LeptMkdir("lept/dewdebug");
            Utils.LeptMkdir("lept/dewmod");
            Ptaa ptaat = Ptaa.Create(numLines);
            for (int i = 0; i < numLines; i++) {
                Pta ptaLine = ptaa.GetPta(i, L.L_CLONE);
                ptaLine.GetArrays(out Numa nax, out _);
                ptaLine.GetQuadraticLSF(out _, out _, out _, out Numa nafit);
                Pta ptad = Pta.CreateFromNuma(nax, nafit);
                ptaat.AddPta(ptad, L.L_INSERT);
            }
            Pix pix1 = pixdb.ConvertTo32();
            Pta ptaCirc = Pix.GeneratePtaFilledCircle(1);
            Pix pixcirc = Pix.GenerateFromPta(ptaCirc, 5, 5);
            Pix pix2 = Pix.DisplayPtaaPattern(null, pix1, ptaat, pixcirc, 2, 2);
            pix2.WriteDebug("/tmp/lept/dewmod/0041.png", L.IFF_PNG);
        }

            /* Remove lines with outlier curvatures */
        nacurve0.GetMedianDevFromMedian(out float medval, out float meddev);
        Log.Info(func, $"\nPage {pageno}");
        Log.Info(func, $"Pass 1: Curvature: medval = {medval}, meddev = {meddev}");
        Ptaa ptaa1 = Ptaa.Create(numLines);
        Numa nacurve1 = Numa.Create(numLines);
        for (int i = 0; i < numLines; i++) {
            float val = nacurve0.GetFValue(i);
            if (Math.Abs(val - medval) > 7.0 * meddev)
                continue;
            Pta ptaLine = ptaa0.GetPta(i, L.L_CLONE);
            ptaa1.AddPta(ptaLine, L.L_INSERT);
            nacurve1.AddNumber(val);
        }
        numLines = ptaa1.Count;

            /* Save the min and max curvature (in micro-units) */
        nacurve1.GetMin(out float minval, out _);
        nacurve1.GetMax(out float maxval, out _);
        mincurv = Utils.RoundFToI((float)(1000000.0 * minval));
        maxcurv = Utils.RoundFToI((float)(1000000.0 * maxval));
        Log.Info(func, $"Pass 2: Min/max curvature = ({mincurv}, {maxcurv})");

            /* Find and save the y values at the mid-points in each curve */
        Numa namidy = Numa.Create(numLines);
        for (int i = 0; i < numLines; i++) {
            Pta ptaLine = ptaa1.GetPta(i, L.L_CLONE);
            int npts = ptaLine.Count;
            ptaLine.GetPt(npts / 2, out _, out float midy);
            namidy.AddNumber(midy);
        }

            /* Sort the lines in ptaa1 by their vertical position, going down */
        Numa namidysi = namidy.GetSortIndex(L.L_SORT_INCREASING);
        Numa sortedMidys = namidy.SortByIndex(namidysi);
        Numa sortedCurves = nacurve1.SortByIndex(namidysi);
        namidys = sortedMidys;
        nacurves = sortedCurves;
        Ptaa ptaa2 = ptaa1.SortByIndex(namidysi);

        if (debug != 0) {
            sortedMidys.WriteDebug("/tmp/lept/dewdebug/midys.na");
            sortedCurves.WriteDebug("/tmp/lept/dewdebug/curves.na");
            Pix pix1 = pixdb.ConvertTo32();
            Pta ptacirc = Pix.GeneratePtaFilledCircle(5);
            Pix pixcirc = Pix.GenerateFromPta(ptacirc, 11, 11);
            CRand.Seed(3);
            Pix.DisplayPtaaPattern(pix1, pix1, ptaa2, pixcirc, 5, 5);
            CRand.Seed(3);
            RenderMidYs(pix1, sortedMidys, 2);
            Pix pix2 = rotflag != 0 ? pix1.RotateOrth(3) : Pix.Clone(pix1);
            pix2.WriteDebug("/tmp/lept/dewmod/0042.png", L.IFF_PNG);
            pix2.Display(0, 0);
        }

            /* Convert sampled points in ptaa2 to sampled disparity wrt midpoint y */
        Ptaa ptaa3 = Ptaa.Create(numLines);
        for (int i = 0; i < numLines; i++) {
            Pta ptaLine = ptaa2.GetPta(i, L.L_CLONE);
            float midy = sortedMidys.GetFValue(i);
            Pta ptad = Pta.Create(curNx);
            for (int j = 0; j < curNx; j++) {
                ptaLine.GetPt(j, out float x, out float y);
                ptad.AddPt(x, midy - y);
            }
            ptaa3.AddPta(ptad, L.L_INSERT);
        }
        if (debug != 0)
            ptaa3.WriteDebug("/tmp/lept/dewdebug/ptaa3.ptaa", 0);

            /* Generate ptaa4 by taking vertical columns from ptaa3 */
        Ptaa ptaa4 = Ptaa.Create(curNx);
        float[] famidys = sortedMidys.GetFArray(L.L_NOCOPY);
        for (int j = 0; j < curNx; j++) {
            Pta ptaCol = Pta.Create(numLines);
            for (int i = 0; i < numLines; i++) {
                float y = famidys[i];
                ptaa3.GetPt(i, j, out _, out float val);
                ptaCol.AddPt(y, val);
            }
            ptaa4.AddPta(ptaCol, L.L_INSERT);
        }
        if (debug != 0)
            ptaa4.WriteDebug("/tmp/lept/dewdebug/ptaa4.ptaa", 0);

            /* Quadratic fit vertically on each column in ptaa4 */
        Ptaa ptaa5 = Ptaa.Create(curNx);
        for (int j = 0; j < curNx; j++) {
            Pta ptaCol = ptaa4.GetPta(j, L.L_CLONE);
            ptaCol.GetQuadraticLSF(out float c2, out float c1, out float c0, out _);
            Pta ptad = Pta.Create(curNy);
            for (int i = 0; i < curNy; i++) {
                float y = i * samp;
                float val = Pta.ApplyQuadraticFit(c2, c1, c0, y);
                ptad.AddPt(y, val);
            }
            ptaa5.AddPta(ptad, L.L_INSERT);
        }
        if (debug != 0) {
            ptaa5.WriteDebug("/tmp/lept/dewdebug/ptaa5.ptaa", 0);
            PdfIo.ConvertFilesToPdf("/tmp/lept/dewmod", "004", 135, 1.0f, 0, 0,
                                    "Dewarp Vert Disparity",
                                    "/tmp/lept/dewarp/vert_disparity.pdf");
            Console.Error.WriteLine("pdf file: /tmp/lept/dewarp/vert_disparity.pdf");
        }

            /* Save the result in an fpix at the specified subsampling */
        FPix fpix = FPix.Create(curNx, curNy);
        for (int i = 0; i < curNy; i++) {
            for (int j = 0; j < curNx; j++) {
                ptaa5.GetPt(j, i, out _, out float val);
                fpix.SetPixel(j, i, val);
            }
        }
        sampvdispar = fpix;
        vsuccess = 1;
        return 0;
    }

    /// <summary>Builds the sampled horizontal disparity array from the ends of the text lines.</summary>
    /// <param name="ptaa">The unsmoothed text lines, not vertically ordered.</param>
    /// <returns>0 if successful; 1 if the horizontal disparity array is not built or on error.</returns>
    /// <remarks>
    /// <para>The model is not checked against the constraints for validity; that is done at rendering time.</para>
    /// <para>
    /// A horizontal disparity array is not required for a successful model; only the vertical array is. This is not
    /// called if building the vertical array fails.
    /// </para>
    /// <para><see cref="HSuccess"/> is set on success.</para>
    /// </remarks>
    // leptonica: dewarpFindHorizDisparity (dewarp2.c)
    public int FindHorizDisparity(Ptaa ptaa)
    {
        const string func = "dewarpFindHorizDisparity";
        hsuccess = 0;
        if (ptaa == null)
            throw Lept.Arg(func, "ptaa not defined");

        if (debug != 0)
            Log.Info(func, "finding horizontal disparity");

        int height = pixs!.Height;
        if (GetLineEndPoints(height, ptaa, out Pta? ptal1, out Pta? ptar1) != 0) {
            Log.Info(func, "Horiz disparity not built");
            return 1;
        }
        if (debug != 0) {
            Utils.LeptMkdir("lept/dewdebug");
            Utils.LeptMkdir("lept/dewarp");
            ptal1!.WriteDebug("/tmp/lept/dewdebug/endpts_left1.pta", 1);
            ptar1!.WriteDebug("/tmp/lept/dewdebug/endpts_right1.pta", 1);
        }

        if (FilterLineEndPoints(this, ptal1!, ptar1!, out Pta? ptal2, out Pta? ptar2) != 0) {
            Log.Info(func, "Not enough filtered end points");
            return 1;
        }

        int samp = sampling;
        int curNx = nx;
        int curNy = ny;
        bool linearEdgefit = dewa!.max_edgecurv == 0;

        float cl0, cl1, cl2 = 0.0f, cr0, cr1, cr2 = 0.0f;
        Pta ptal3, ptar3;
        if (linearEdgefit) {
            LinearLSF(ptal2!, out cl1, out cl0, out float mederr);
            leftslope = Utils.RoundFToI((float)(1000.0 * cl1));
            leftcurv = 0;
            Log.Info(func, $"Left linear LSF median error = {mederr:F2}");
            Log.Info(func, $"Left edge slope = {leftslope}");
            ptal3 = Pta.Create(curNy);
            for (int i = 0; i < curNy; i++) {
                float y = i * samp;
                float x = Pta.ApplyLinearFit(cl1, cl0, y);
                ptal3.AddPt(x, y);
            }

            LinearLSF(ptar2!, out cr1, out cr0, out mederr);
            rightslope = Utils.RoundFToI((float)(1000.0 * cr1));
            rightcurv = 0;
            Log.Info(func, $"Right linear LSF median error = {mederr:F2}");
            Log.Info(func, $"Right edge slope = {rightslope}");
            ptar3 = Pta.Create(curNy);
            for (int i = 0; i < curNy; i++) {
                float y = i * samp;
                float x = Pta.ApplyLinearFit(cr1, cr0, y);
                ptar3.AddPt(x, y);
            }
        } else {
            QuadraticLSF(ptal2!, out cl2, out cl1, out cl0, out float mederr);
            leftslope = Utils.RoundFToI((float)(1000.0 * cl1));
            leftcurv = Utils.RoundFToI((float)(1000000.0 * cl2));
            Log.Info(func, $"Left quad LSF median error = {mederr:F2}");
            Log.Info(func, $"Left edge slope = {leftslope}");
            Log.Info(func, $"Left edge curvature = {leftcurv}");
            ptal3 = Pta.Create(curNy);
            for (int i = 0; i < curNy; i++) {
                float y = i * samp;
                float x = Pta.ApplyQuadraticFit(cl2, cl1, cl0, y);
                ptal3.AddPt(x, y);
            }

            QuadraticLSF(ptar2!, out cr2, out cr1, out cr0, out mederr);
            rightslope = Utils.RoundFToI((float)(1000.0 * cr1));
            rightcurv = Utils.RoundFToI((float)(1000000.0 * cr2));
            Log.Info(func, $"Right quad LSF median error = {mederr:F2}");
            Log.Info(func, $"Right edge slope = {rightslope}");
            Log.Info(func, $"Right edge curvature = {rightcurv}");
            ptar3 = Pta.Create(curNy);
            for (int i = 0; i < curNy; i++) {
                float y = i * samp;
                float x = Pta.ApplyQuadraticFit(cr2, cr1, cr0, y);
                ptar3.AddPt(x, y);
            }
        }

        if (debug != 0) {
            height = pixs.Height;
            Pta pta1 = Pta.Create(height);
            Pta pta2 = Pta.Create(height);
            if (linearEdgefit) {
                for (int i = 0; i < height; i++) {
                    pta1.AddPt(Pta.ApplyLinearFit(cl1, cl0, i), i);
                    pta2.AddPt(Pta.ApplyLinearFit(cr1, cr0, i), i);
                }
            } else {
                for (int i = 0; i < height; i++) {
                    pta1.AddPt(Pta.ApplyQuadraticFit(cl2, cl1, cl0, i), i);
                    pta2.AddPt(Pta.ApplyQuadraticFit(cr2, cr1, cr0, i), i);
                }
            }
            Pix pix1 = Pix.DisplayPta(null, pixs, pta1);
            Pix.DisplayPta(pix1, pix1, pta2);
            RenderHorizEndPoints(pix1, ptal2!, ptar2!, 0xff000000u);
            pix1.Display(600, 800);
            pix1.WriteDebug("/tmp/lept/dewmod/0051.png", L.IFF_PNG);

            pix1 = Pix.DisplayPta(null, pixs, pta1);
            Pix.DisplayPta(pix1, pix1, pta2);
            Pta ptalft = ptal3.Transpose();
            Pta ptarft = ptar3.Transpose();
            RenderHorizEndPoints(pix1, ptalft, ptarft, 0x0000ff00u);
            pix1.Display(800, 800);
            pix1.WriteDebug("/tmp/lept/dewmod/0052.png", L.IFF_PNG);
            PdfIo.ConvertFilesToPdf("/tmp/lept/dewmod", "005", 135, 1.0f, 0, 0,
                                    "Dewarp Horiz Disparity",
                                    "/tmp/lept/dewarp/horiz_disparity.pdf");
            Console.Error.WriteLine("pdf file: /tmp/lept/dewarp/horiz_disparity.pdf");
        }

        ptal3.GetPt(curNy / 2, out float refl, out _);
        ptar3.GetPt(curNy / 2, out float refr, out _);
        Numa nald = Numa.Create(curNy);
        Numa nard = Numa.Create(curNy);
        for (int i = 0; i < curNy; i++) {
            ptal3.GetPt(i, out float x, out _);
            nald.AddNumber(refl - x);
            ptar3.GetPt(i, out x, out _);
            nard.AddNumber(refr - x);
        }

        Ptaa ptaah = Ptaa.Create(curNy);
        for (int i = 0; i < curNy; i++) {
            Pta ptaPair = Pta.Create(2);
            ptaPair.AddPt(refl, nald.GetFValue(i));
            ptaPair.AddPt(refr, nard.GetFValue(i));
            ptaPair.GetLinearLSF(out float c1, out float c0, out _);
            Pta ptat = Pta.Create(curNx);
            for (int j = 0; j < curNx; j++) {
                float x = j * samp;
                float val = Pta.ApplyLinearFit(c1, c0, x);
                ptat.AddPt(x, val);
            }
            ptaah.AddPta(ptat, L.L_INSERT);
        }

        FPix fpix = FPix.Create(curNx, curNy);
        for (int i = 0; i < curNy; i++) {
            for (int j = 0; j < curNx; j++) {
                ptaah.GetPt(i, j, out _, out float val);
                fpix.SetPixel(j, i, val);
            }
        }
        samphdispar = fpix;
        hsuccess = 1;
        return 0;
    }

    /// <summary>Finds the points along the centers of the text lines in a binary image.</summary>
    /// <param name="pixs">The 1 bpp image of text.</param>
    /// <param name="debugflag">Use 1 to generate debug output.</param>
    /// <returns>The center points of the text lines, one set of points per line.</returns>
    /// <exception cref="ArgumentException"><paramref name="pixs"/> is <c>null</c> or is not 1 bpp.</exception>
    /// <remarks>
    /// In general there is not a point for every x value, because there are gaps between words. This does not
    /// matter because a quadratic is fit to the points that are found.
    /// </remarks>
    // leptonica: dewarpGetTextlineCenters (dewarp2.c)
    public static Ptaa? GetTextlineCenters(Pix pixs, int debugflag)
    {
        const string func = "dewarpGetTextlineCenters";
        if (pixs == null || pixs.Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        pixs.GetDimensions(out int w, out int h, out _);

        if (debugflag != 0)
            Log.Info(func, "finding text line centers");

        int csize1 = Math.Max(15, w / 80);
        int csize2 = Math.Max(40, w / 30);
        string seq = $"o1.3 + c{csize1}.1 + o{csize1}.1 + c{csize2}.1";
        Pix pix1 = pixs.MorphSequence(seq, 0);

        Pix pix2 = pix1.MorphSequence("e1.50", 0);
        Pix.SeedfillBinary(pix2, pix2, pix1, 8);
        Pix.Xor(pix2, pix2, pix1);

        if (debugflag != 0) {
            Utils.LeptMkdir("lept/dewmod");
            pix1.WriteDebug("/tmp/lept/dewmod/0011.tif", L.IFF_TIFF_G4);
            pix1.DisplayWithTitle(0, 600, "pix1", 1);
            pix2.WriteDebug("/tmp/lept/dewmod/0012.tif", L.IFF_TIFF_G4);
            pix2.DisplayWithTitle(0, 800, "pix2", 1);
        }

        pix2.ConnComp(out Pixa pixa1, 8);
        if (pixa1.Count == 0)
            return null;

        Pixa pixa2 = pixa1.SelectBySize(100, 4, L.L_SELECT_IF_BOTH, L.L_SELECT_IF_GT, out _);
        int nsegs = pixa2.Count;
        if (nsegs == 0)
            return null;

        if (debugflag != 0) {
            Pix pixDisp = pixa2.Display(w, h);
            pixDisp.WriteDebug("/tmp/lept/dewmod/0013.tif", L.IFF_TIFF_G4);
            pixDisp.DisplayWithTitle(0, 1000, "pix2", 1);
        }

        Ptaa ptaa = Ptaa.Create(nsegs);
        for (int i = 0; i < nsegs; i++) {
            pixa2.GetBoxGeometry(i, out int bx, out int by, out _, out _);
            Pix pixComp = pixa2.GetPix(i, L.L_CLONE);
            Pta pta = GetMeanVerticals(pixComp, bx, by);
            ptaa.AddPta(pta, L.L_INSERT);
        }
        if (debugflag != 0) {
            Pix pixTpl = Pix.CreateTemplate(pixs);
            Pix pixDisp = pixTpl.DisplayPtaa(ptaa);
            pixDisp.WriteDebug("/tmp/lept/dewmod/0014.tif", L.IFF_PNG);
            pixDisp.DisplayWithTitle(0, 1200, "pix3", 1);
        }

        return ptaa;
    }

    // leptonica: dewarpGetMeanVerticals (dewarp2.c)
    private static Pta GetMeanVerticals(Pix pixs, int x, int y)
    {
        const string func = "dewarpGetMeanVerticals";
        if (pixs == null || pixs.Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");

        pixs.GetDimensions(out int w, out int h, out _);
        Pta pta = Pta.Create(w);
        uint[] data = pixs.Data;
        int wpl = pixs.Wpl;
        for (int j = 0; j < w; j++) {
            int wordOffset = j >> 5;
            uint mask = 0x80000000u >> (j & 31);
            int sum = 0;
            int count = 0;
            int lineIdx = 0;
            for (int i = 0; i < h; i++) {
                if ((data[lineIdx + wordOffset] & mask) != 0) {
                    sum += i;
                    count += 1;
                }
                lineIdx += wpl;
            }
            if (count == 0)
                continue;
            pta.AddPt(x + j, y + (sum / count));
        }

        return pta;
    }

    /// <summary>Removes the text lines that are too short.</summary>
    /// <param name="pixs">The 1 bpp image of text.</param>
    /// <param name="ptaas">The input text lines.</param>
    /// <param name="fract">The minimum length of a line to keep, as a fraction of the longest line.</param>
    /// <param name="debugflag">Use 1 to generate debug output.</param>
    /// <returns>The lines that are long enough.</returns>
    /// <exception cref="ArgumentException"><paramref name="pixs"/> is <c>null</c> or is not 1 bpp.</exception>
    // leptonica: dewarpRemoveShortLines (dewarp2.c)
    public static Ptaa RemoveShortLines(Pix pixs, Ptaa ptaas, float fract, int debugflag)
    {
        const string func = "dewarpRemoveShortLines";
        if (pixs == null || pixs.Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (ptaas == null)
            throw Lept.Arg(func, "ptaas undefined");

        int w = pixs.Width;
        int n = ptaas.Count;
        Ptaa ptaad = Ptaa.Create(n);
        if (n == 0)
            return ptaad;

        Numa na = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            Pta pta = ptaas.GetPta(i, L.L_CLONE);
            pta.GetRange(out float minx, out float maxx, out _, out _);
            na.AddNumber(maxx - minx + 1);
        }

        Numa naindex = na.GetSortIndex(L.L_SORT_DECREASING);
        int index = naindex.GetIValue(0);
        int maxlen = na.GetIValue(index);
        if (maxlen < 0.5 * w)
            Log.Warning(func, "lines are relatively short");
        ptaad.AddPta(ptaas.GetPta(index, L.L_CLONE), L.L_INSERT);
        for (int i = 1; i < n; i++) {
            index = naindex.GetIValue(i);
            int len = na.GetIValue(index);
            if (len < fract * maxlen)
                break;
            ptaad.AddPta(ptaas.GetPta(index, L.L_CLONE), L.L_INSERT);
        }

        if (debugflag != 0) {
            Pix pix1 = Pix.Copy(null, pixs);
            Pix pix2 = pix1.DisplayPtaa(ptaad);
            pix2.DisplayWithTitle(0, 200, "pix4", 1);
        }

        return ptaad;
    }

    // leptonica: dewarpGetLineEndPoints (dewarp2.c)
    private static int GetLineEndPoints(int h, Ptaa ptaa, out Pta? pptal, out Pta? pptar)
    {
        const string func = "dewarpGetLineEndPoints";
        pptal = null;
        pptar = null;
        if (ptaa == null) {
            Log.Error(func, "ptaa undefined");
            return 1;
        }

        int n = ptaa.Count;
        if (n < MinLinesForHoriz1) {
            Log.Info(func, $"only {n} lines; too few");
            return 1;
        }

        Pta ptal1 = Pta.Create(n);
        Pta ptar1 = Pta.Create(n);
        for (int i = 0; i < n; i++) {
            Pta pta = ptaa.GetPta(i, L.L_CLONE);
            pta.GetIPt(0, out int x, out int y);
            ptal1.AddPt(y, x);
            int npt = pta.Count;
            pta.GetIPt(npt - 1, out x, out y);
            ptar1.AddPt(y, x);
        }

        ptal1.GetRange(out float miny, out float maxy, out _, out _);
        float ratio = (maxy - miny) / (float)h;
        if (ratio < MinRatioLinesToHeight) {
            Log.Info(func, $"ratio lines to height, {ratio}, too small");
            return 1;
        }

        pptal = ptal1.Sort(L.L_SORT_BY_X, L.L_SORT_INCREASING, out _);
        pptar = ptar1.Sort(L.L_SORT_BY_X, L.L_SORT_INCREASING, out _);
        return 0;
    }

    // leptonica: dewarpFilterLineEndPoints (dewarp2.c)
    private static int FilterLineEndPoints(Dewarp dew, Pta ptal, Pta ptar,
                                           out Pta? pptalf, out Pta? pptarf)
    {
        const string func = "dewarpFilterLineEndPoints";
        pptalf = null;
        pptarf = null;
        if (ptal == null || ptar == null) {
            Log.Error(func, "ptal or ptar not defined");
            return 1;
        }

        int w = dew.pixs!.Width;
        ptal.GetMinMax(out _, out float ymin, out _, out _);
        ptar.GetMinMax(out _, out _, out _, out float ymax);
        int n = ptal.Count;
        Pta ptal1 = Pta.Create(n);
        Pta ptar1 = Pta.Create(n);
        for (int i = 0; i < n; i++) {
            ptal.GetPt(i, out float xvall, out float yvall);
            ptar.GetPt(i, out float xvalr, out float yvalr);
            if (yvall < ymin + 0.20 * (w - ymin) && yvalr > 0.80 * ymax) {
                ptal1.AddPt(xvall, yvall);
                ptar1.AddPt(xvalr, yvalr);
            }
        }
        if (dew.debug != 0) {
            ptal1.WriteDebug("/tmp/lept/dewdebug/endpts_left2.pta", 1);
            ptar1.WriteDebug("/tmp/lept/dewdebug/endpts_right2.pta", 1);
        }

        n = Math.Min(ptal1.Count, ptar1.Count);
        if (n < MinLinesForHoriz1 - 2) {
            Log.Info(func, $"First filter: only {n} endpoints; needed 8");
            return 1;
        }

        Pta? ptal2 = RemoveBadEndPoints(w, ptal1);
        Pta? ptar2 = RemoveBadEndPoints(w, ptar1);
        if (ptal2 == null || ptar2 == null) {
            Log.Info(func, "Second filter: too few endpoints left after outliers removed");
            return 1;
        }
        if (dew.debug != 0) {
            ptal2.WriteDebug("/tmp/lept/dewdebug/endpts_left3.pta", 1);
            ptar2.WriteDebug("/tmp/lept/dewdebug/endpts_right3.pta", 1);
        }

        pptalf = ptal2;
        pptarf = ptar2;
        return 0;
    }

    // leptonica: dewarpRemoveBadEndPoints (dewarp2.c)
    private static Pta? RemoveBadEndPoints(int w, Pta ptas)
    {
        const string func = "dewarpRemoveBadEndPoints";
        if (ptas == null)
            return null;

        float delta = AllowedWidthFract * w;
        int n = ptas.Count;

            /* Check the upper half */
        Pta ptau1 = ptas.SelectRange(0, n / 2);
        float rval = ptau1.GetRankValue(0.5f, null, L.L_SORT_BY_Y);
        int nu = ptau1.Count;
        Pta ptau2 = Pta.Create(nu);
        for (int i = 0; i < nu; i++) {
            ptau1.GetPt(i, out float xval, out float yval);
            if (Math.Abs(rval - yval) <= delta)
                ptau2.AddPt(xval, yval);
        }
        if (ptau2.Count < MinLinesForHoriz2) {
            Log.Info(func, "Second filter: upper set is too small after outliers removed");
            return null;
        }

            /* Check the lower half */
        Pta ptad1 = ptas.SelectRange(n / 2 + 1, -1);
        rval = ptad1.GetRankValue(0.5f, null, L.L_SORT_BY_Y);
        int nd = ptad1.Count;
        Pta ptad2 = Pta.Create(nd);
        for (int i = 0; i < nd; i++) {
            ptad1.GetPt(i, out float xval, out float yval);
            if (Math.Abs(rval - yval) <= delta)
                ptad2.AddPt(xval, yval);
        }
        if (ptad2.Count < MinLinesForHoriz2) {
            Log.Info(func, "Second filter: lower set is too small after outliers removed");
            return null;
        }

        ptau2.Join(ptad2, 0, -1);
        return ptau2;
    }

    // leptonica: dewarpIsLineCoverageValid (dewarp2.c)
    private static bool IsLineCoverageValid(Ptaa ptaa, int h,
                                            out int pntop, out int pnbot,
                                            out int pytop, out int pybot)
    {
        const string func = "dewarpIsLineCoverageValid";
        pntop = pnbot = pytop = pybot = 0;
        if (ptaa == null || ptaa.Count == 0 || h <= 0) {
            Log.Error(func, "invalid arguments");
            return false;
        }

        int n = ptaa.Count;
        Numa na = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            ptaa.GetPt(i, 0, out _, out float y);
            na.AddNumber(y);
        }
        Numa.Sort(na, na, L.L_SORT_INCREASING);
        int ntop = 0, ytop = 0, ybot = 0;
        for (int i = 0; i < n; i++) {
            int iy = na.GetIValue(i);
            if (i == 0) ytop = iy;
            if (i == n - 1) ybot = iy;
            if (iy < 0.5 * h)
                ntop++;
        }
        int nbot = n - ntop;
        pntop = ntop;
        pnbot = nbot;
        pytop = ytop;
        pybot = ybot;
        const int nmin = 4;
        bool bothHalves = ntop >= nmin && nbot >= nmin;
        float fraction = (float)(ybot - ytop) / (float)h;
        return bothHalves && fraction > 0.50;
    }

    // leptonica: dewarpLinearLSF (dewarp2.c)
    private static void LinearLSF(Pta ptad, out float pa, out float pb, out float pmederr)
    {
        ptad.GetLinearLSF(out float c1, out float c0, out _);
        pa = c1;
        pb = c0;

        int n = ptad.Count;
        Numa naerr = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            ptad.GetPt(i, out float y, out float xp);
            float x = Pta.ApplyLinearFit(c1, c0, y);
            naerr.AddNumber(Math.Abs(x - xp));
        }
        pmederr = naerr.GetMedian();
    }

    // leptonica: dewarpQuadraticLSF (dewarp2.c)
    private static void QuadraticLSF(Pta ptad, out float pa, out float pb, out float pc,
                                     out float pmederr)
    {
        ptad.GetQuadraticLSF(out float c2, out float c1, out float c0, out _);
        pa = c2;
        pb = c1;
        pc = c0;

        int n = ptad.Count;
        Numa naerr = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            ptad.GetPt(i, out float y, out float xp);
            float x = Pta.ApplyQuadraticFit(c2, c1, c0, y);
            naerr.AddNumber(Math.Abs(x - xp));
        }
        pmederr = naerr.GetMedian();
    }

    /*----------------------------------------------------------------------*
     *              Build disparity model for slope near binding            *
     *----------------------------------------------------------------------*/
    /// <summary>Estimates the foreshortening of the characters on the binding side of a page and, if it is significant, builds a one-dimensional horizontal disparity function to compensate.</summary>
    /// <param name="pixb">A 1 bpp image from which both the vertical and the horizontal disparity have been removed.</param>
    /// <param name="fractthresh">The threshold on the fractional difference in stroke density between the left and right sides; the disparity is processed only if the absolute difference equals or exceeds it.</param>
    /// <param name="parity">0 for an even page, with the binding on the left; 1 for an odd page, with the binding on the right.</param>
    /// <returns>0 if successful; 1 on error.</returns>
    /// <remarks>
    /// <para>The text lines of <paramref name="pixb"/> are straight and the line end points are vertically aligned.</para>
    /// <para>
    /// The density of vertical strokes is measured after a vertical opening of 10 pixels. The relative density is found by
    /// counting connected components in a full-height sliding window 50 pixels wide, evaluated every 25 pixels.
    /// </para>
    /// </remarks>
    // leptonica: dewarpFindHorizSlopeDisparity (dewarp2.c)
    public int FindHorizSlopeDisparity(Pix pixb, float fractthresh, int parity)
    {
        const string func = "dewarpFindHorizSlopeDisparity";
        if (vvalid == 0 || hvalid == 0) {
            Log.Error(func, "invalid vert or horiz disparity model");
            return 1;
        }
        if (pixb == null || pixb.Depth != 1)
            throw Lept.Arg(func, "pixb not defined or not 1 bpp");

        if (debug != 0)
            Log.Info(func, "finding slope horizontal disparity");

        Pix pix1 = pixb.MorphSequence("o1.10", 0);
        pix1.Display(100, 100);
        Boxa boxa1 = pix1.ConnCompBB(4);
        Boxa boxa2 = boxa1.SelectBySize(0, 5, L.L_SELECT_HEIGHT, L.L_SELECT_IF_GT, out _);
        int nb = boxa2.Count;
        Console.Error.WriteLine($"number of components: {nb}");

        Numa na1 = Numa.Create(0);
        na1.SetParameters(0, 25);
        pixb.GetDimensions(out int bw, out int bh, out _);
        for (int x = 0; x + 50 < bw; x += 25) {
            Box box = Box.Create(x, 0, 50, bh);
            int count = boxa2.ContainedInBoxCount(box);
            na1.AddNumber(count);
        }
        if (debug != 0) {
            Utils.LeptMkdir("lept/dew");
            Gplot.Simple1(na1, L.GPLOT_PNG, "/tmp/lept/dew/0091", null);
            Utils.LeptMv("/tmp/lept/dew/0091.png", "lept/dewmod", null, out _);
            pix1.WriteDebug("/tmp/lept/dewmod/0090.png", L.IFF_PNG);
        }

        int n1 = na1.Count;
        int prev = 0, istart = 0, first = 0;
        for (int i = 0; i < n1; i++) {
            int ival = na1.GetIValue(i);
            if (ival >= prev) {
                prev = ival;
            } else {
                first = prev;
                istart = i - 1;
                break;
            }
        }
        prev = 0;
        int last = 0, iend = n1 - 1;
        for (int i = n1 - 1; i >= 0; i--) {
            int ival = na1.GetIValue(i);
            if (ival >= prev) {
                prev = ival;
            } else {
                last = prev;
                iend = i + 1;
                break;
            }
        }
        Numa na2 = na1.ClipToInterval(istart, iend);
        int n2 = na2.Count;
        float delta = parity == 0 ? last - first : first - last;
        float denom = Math.Max(1.0f, (float)Math.Min(first, last));
        float fract = delta / denom;
        if (debug != 0) {
            Log.Info(func, $"Slope-disparity: first = {first}, last = {last}, fract = {fract,7:F3}");
            Gplot.Simple1(na2, L.GPLOT_PNG, "/tmp/lept/dew/0092", null);
            Utils.LeptMv("/tmp/lept/dew/0092.png", "lept/dewmod", null, out _);
        }
        if (fract < fractthresh) {
            Log.Info(func, $"Small slope-disparity: first = {first}, last = {last}, fract = {fract,7:F3}");
            return 0;
        }

        int ne = n2 - (n2 % 2);
        float sum = parity == 0
            ? na2.GetSumOnInterval(0, ne / 2 - 1)
            : na2.GetSumOnInterval(ne / 2, ne - 1);
        denom = Math.Max(1.0f, (float)(ne / 2));
        float aveval = sum / denom;
        Numa na3 = Numa.MakeConstant(aveval, n2);
        Numa.ArithOp(na2, na2, na3, L.L_ARITH_DIVIDE);
        if (debug != 0) {
            Log.Info(func, $"Average background density: {aveval,5:F1}");
            Gplot.Simple1(na2, L.GPLOT_PNG, "/tmp/lept/dew/0093", null);
            Utils.LeptMv("/tmp/lept/dew/0093.png", "lept/dewmod", null, out _);
        }

        Pta pta1 = na2.ConvertToPta1();
        using (Stream stderr = Console.OpenStandardError()) {
            pta1.WriteStream(stderr, 0);
        }
        pta1.GetQuarticLSF(out float ca, out float cb, out float cc,
                           out float cd, out float ce, out na3);
        pta1.GetArrays(out Numa na4, out _);
        if (debug != 0) {
            Gplot.SimpleXY1(na4, na3, L.GPLOT_LINES, L.GPLOT_PNG,
                            "/tmp/lept/dew/0094", null);
            Utils.LeptMv("/tmp/lept/dew/0094.png", "lept/dewmod", null, out _);
        }

        Numa nasum = Numa.MakeConstant(0, bw);
        if (parity == 0) {
            int i;
            for (i = n2 - 1; i >= 0; i--) {
                if (na3.GetFValue(i) < 1.0f)
                    break;
            }
            int x0 = na4.GetIValue(i + 1);
            int x1 = na4.GetIValue(n2 - 1);
            nasum.SetParameters(x0, 1);
            sum = 0.0f;
            for (int x = x0; x < x1; x++) {
                float y = Pta.ApplyQuarticFit(ca, cb, cc, cd, ce, x);
                sum += (float)(y - 1.0);
                nasum.ReplaceNumber(x, sum);
            }
            for (int x = x1; x < bw; x++)
                nasum.ReplaceNumber(x, sum);
        } else {
            int i;
            for (i = 0; i < n2; i++) {
                if (na3.GetFValue(i) < 1.0f)
                    break;
            }
            int x0 = na4.GetIValue(0);
            int x1 = na4.GetIValue(i - 1);
            nasum.SetParameters(x0, 1);
            sum = 0.0f;
            for (int x = x1; x >= x0; x--) {
                float y = Pta.ApplyQuarticFit(ca, cb, cc, cd, ce, x);
                sum += (float)(y - 1.0);
                nasum.ReplaceNumber(x, sum);
            }
            for (int x = x0; x >= 0; x--)
                nasum.ReplaceNumber(x, sum);
        }

        FPix fpix = FPix.Create(nx, ny);
        float del = (float)bw / (float)nx;
        for (int i = 0; i < ny; i++) {
            for (int j = 0; j < nx; j++) {
                int x = (int)(del * j);
                float fval = nasum.GetFValue(x);
                fpix.SetPixel(j, i, fval);
            }
        }
        sampydispar = fpix;
        ysuccess = 1;
        return 0;
    }

    /*----------------------------------------------------------------------*
     *                      Build line disparity model                      *
     *----------------------------------------------------------------------*/
    /// <summary>Builds the vertical and horizontal disparity arrays from an image of ruled lines, typically for calibration.</summary>
    /// <param name="opensize">The size of the opening used to remove perpendicular lines; typically about 8 and larger than the thickness of the lines. A value less than 3 selects the default.</param>
    /// <param name="debugfile">The path of a file for debug output, or <c>null</c> to skip debug output.</param>
    /// <returns>0 if successful; 1 if the model could not be built.</returns>
    /// <remarks>
    /// <para>
    /// In book scanning, ruled paper can be laid over a page. The disparity correction of the line model can then be used
    /// to dewarp that page and several below it.
    /// </para>
    /// <para>The model must have been created from an image of ruled lines. The lines must be continuous; a small amount of preprocessing is done to ensure that.</para>
    /// <para><see cref="VSuccess"/> and <see cref="HSuccess"/> are set if the vertical and horizontal arrays are built. See <see cref="BuildPageModel"/> for the method.</para>
    /// </remarks>
    // leptonica: dewarpBuildLineModel (dewarp2.c)
    public int BuildLineModel(int opensize, string? debugfile)
    {
        const string func = "dewarpBuildLineModel";
        if (opensize < 3) {
            Log.Warning(func, "opensize should be >= 3; setting to 8");
            opensize = 8;
        }

        debug = debugfile != null ? 1 : 0;
        vsuccess = hsuccess = 0;
        Pix src = pixs!;
        if (debugfile != null) {
            Utils.LeptRmdir("lept/dewline");
            Utils.LeptMkdir("lept/dewline");
            Utils.LeptRmdir("lept/dewmod");
            Utils.LeptMkdir("lept/dewmod");
            Utils.LeptMkdir("lept/dewarp");
            src.DisplayWithTitle(0, 0, "pixs", 1);
            src.WriteDebug("/tmp/lept/dewline/001.png", L.IFF_PNG);
        }

        string bufH = $"d1.3 + c{opensize - 2}.1 + o{opensize}.1";
        Pix pixh = src.MorphSequence(bufH, 0);
        string bufV = $"d3.1 + c1.{opensize - 2} + o1.{opensize}";
        Pix pix1 = src.MorphSequence(bufV, 0);
        Pix pixv = pix1.RotateOrth(1);
        Pixa? pixa1 = Pixa.Create(2);
        pixa1.AddPix(pixv, L.L_INSERT);
        pixa1.AddPix(pixh, L.L_INSERT);

        for (int i = 0; i < 2; i++) {
            if (pixa1 == null) {
                Log.Warning(func, "only found 0 lines");
                continue;
            }
            Pix pix = pixa1.GetPix(i, L.L_CLONE);
            pix.Display(0, 900);
            Boxa boxa = pix.ConnComp(out Pixa pixa2, 8);
            int numLines = boxa.Count;
            if (numLines < minlines) {
                Log.Warning(func, $"only found {numLines} lines");
                // NOTE(upstream): C calls pixaDestroy(&pixa1) here, causing pass 1 to also skip.
                pixa1 = null;
                continue;
            }

            Ptaa ptaa1 = Ptaa.Create(numLines);
            for (int j = 0; j < numLines; j++) {
                pixa2.GetBoxGeometry(j, out int bx, out int by, out _, out _);
                Pix pixComp = pixa2.GetPix(j, L.L_CLONE);
                Pta pta = GetMeanVerticals(pixComp, bx, by);
                ptaa1.AddPta(pta, L.L_INSERT);
            }
            if (debugfile != null) {
                Pix pix32 = pix.ConvertTo32();
                Pix pixDisp = pix32.DisplayPtaa(ptaa1);
                pixDisp.WriteDebug($"/tmp/lept/dewline/{2 + 2 * i:D3}.png", L.IFF_PNG);
            }

            Ptaa ptaa2 = RemoveShortLines(pix, ptaa1, 0.75f, 0);
            if (debugfile != null) {
                Pix pix32 = pix.ConvertTo32();
                Pix pixDisp = pix32.DisplayPtaa(ptaa2);
                pixDisp.WriteDebug($"/tmp/lept/dewline/{3 + 2 * i:D3}.png", L.IFF_PNG);
            }
            numLines = ptaa2.Count;
            if (numLines < minlines) {
                Log.Warning(func, $"{numLines} lines: too few to build model");
                continue;
            }

            int ret = FindVertDisparity(ptaa2, 1 - i);
            if (i == 0) {
                if (ret != 0) {
                    Log.Warning(func, "horizontal disparity not built");
                } else {
                    Log.Info(func, "hsuccess = 1");
                    samphdispar = sampvdispar!.RotateOrth(3);
                    sampvdispar = null;
                    if (debugfile != null)
                        Utils.LeptMv("/tmp/lept/dewarp/vert_disparity.pdf",
                                     "lept/dewarp", "horiz_disparity.pdf", out _);
                }
                hsuccess = vsuccess;
                vsuccess = 0;
            } else {
                if (ret != 0)
                    Log.Warning(func, "vertical disparity not built");
                else
                    Log.Info(func, "vsuccess = 1");
            }
        }

        if (debugfile != null) {
            if (vsuccess == 1) {
                PopulateFullRes(null, 0, 0);
                Pix pixContours = fullvdispar!.RenderContours(3.0f, 0.15f);
                pixContours.WriteDebug("/tmp/lept/dewline/006.png", L.IFF_PNG);
                pixContours.Display(1000, 0);
            }
            if (hsuccess == 1) {
                Pix pixContours = fullhdispar!.RenderContours(3.0f, 0.15f);
                pixContours.WriteDebug("/tmp/lept/dewline/007.png", L.IFF_PNG);
                pixContours.Display(1000, 0);
            }
            PdfIo.ConvertFilesToPdf("/tmp/lept/dewline", null, 135, 1.0f, 0, 0,
                                    "Dewarp Build Line Model", debugfile);
            Console.Error.WriteLine($"pdf file: {debugfile}");
        }

        return 0;
    }

    /*----------------------------------------------------------------------*
     *                          Rendering helpers                           *
     *----------------------------------------------------------------------*/
    // leptonica: pixRenderMidYs (dewarp2.c)
    private static void RenderMidYs(Pix pixs, Numa namidys, int linew)
    {
        int w = pixs.Width;
        int n = namidys.Count;
        PixColormap cmap = PixColormap.CreateRandom(8, false, false);
        for (int i = 0; i < n; i++) {
            cmap.GetColor(i % 256, out int rval, out int gval, out int bval);
            int yval = namidys.GetIValue(i);
            pixs.RenderLineArb(0, yval, w, yval, linew, (byte)rval, (byte)gval, (byte)bval);
        }
    }

    // leptonica: pixRenderHorizEndPoints (dewarp2.c)
    private static void RenderHorizEndPoints(Pix pixs, Pta ptal, Pta ptar, uint color)
    {
        Pta ptacirc = Pix.GeneratePtaFilledCircle(5);
        Pix pixcirc = Pix.GenerateFromPta(ptacirc, 11, 11);
        Pta ptalt = ptal.Transpose();
        Pta ptart = ptar.Transpose();

        Pix.DisplayPtaPattern(pixs, pixs, ptalt, pixcirc, 5, 5, color);
        Pix.DisplayPtaPattern(pixs, pixs, ptart, pixcirc, 5, 5, color);
    }
}

public sealed partial class Dewarpa
{
    /*----------------------------------------------------------------------*
     *                         Query model status                           *
     *----------------------------------------------------------------------*/
    /// <summary>Tests whether the models for a page have been built.</summary>
    /// <param name="pageno">The page number.</param>
    /// <param name="pvsuccess">Receives 1 if the vertical disparity array was built.</param>
    /// <param name="phsuccess">Receives 1 if the horizontal disparity array was built.</param>
    /// <returns>0 if successful; 1 on error.</returns>
    /// <remarks>This tests whether a model has been built, not whether it is valid.</remarks>
    // leptonica: dewarpaModelStatus (dewarp2.c)
    public int ModelStatus(int pageno, out int pvsuccess, out int phsuccess)
    {
        const string func = "dewarpaModelStatus";
        pvsuccess = 0;
        phsuccess = 0;
        Dewarp? dew = GetDewarp(pageno);
        if (dew == null) {
            Log.Error(func, "dew not retrieved");
            return 1;
        }
        pvsuccess = dew.vsuccess;
        phsuccess = dew.hsuccess;
        return 0;
    }
}
