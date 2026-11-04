using System;

namespace Leptonica;

/*
 *  readbarcode.c: locate, deskew, extract crossings/widths, and decode 1D barcodes.
 */

public sealed partial class Pix
{
    private const int MaxSpaceWidth = 19;
    private const int MaxNoiseWidth = 50;
    private const int MaxNoiseHeight = 30;
    private const int MinBcWidth = 50;
    private const int MinBcHeight = 50;

    /*------------------------------------------------------------------------*
     *                               Top level                                *
     *------------------------------------------------------------------------*/
    /// <summary>Locates, deskews and decodes all 1D barcodes in the image.</summary>
    /// <param name="format">
    /// The barcode format, for example <see cref="L.L_BF_CODEI2OF5"/> or <see cref="L.L_BF_CODE93"/>,
    /// or <see cref="L.L_BF_ANY"/> to identify the format automatically.
    /// </param>
    /// <param name="method">The width extraction method, <see cref="L.L_USE_WIDTHS"/> or <see cref="L.L_USE_WINDOWS"/>.</param>
    /// <param name="psaw">Receives the strings of bar widths, one per barcode, or <c>null</c> if none were found.</param>
    /// <param name="debugflag">Use 1 to generate debug output.</param>
    /// <returns>The text of the barcodes found, or <c>null</c> if none were found.</returns>
    /// <exception cref="ArgumentException">The format is unsupported or the method is invalid.</exception>
    /// <remarks>The image may have any depth; it is converted to 8 bpp without a colormap if needed.</remarks>
    // leptonica: pixProcessBarcodes (readbarcode.c)
    public Sarray? ProcessBarcodes(int format, int method, out Sarray? psaw, int debugflag)
    {
        const string func = "pixProcessBarcodes";
        psaw = null;
        if (format != L.L_BF_ANY && !Barcode.FormatIsSupported(format)) {
            Log.Error(func, "unsupported format");
            return null;
        }
        if (method != L.L_USE_WIDTHS && method != L.L_USE_WINDOWS)
            throw Lept.Arg(func, "invalid method");

        Pix pixg = (Depth == 8 && Colormap == null) ? Clone(this) : ConvertTo8(false);
        Pixa? pixa = pixg.ExtractBarcodes(debugflag);
        if (pixa == null) {
            Log.Error(func, "no barcode(s) found");
            return null;
        }

        return pixa.ReadBarcodes(format, method, out psaw, debugflag);
    }

    /// <summary>Locates the barcodes in an image and returns them deskewed and cropped.</summary>
    /// <param name="debugflag">Use 1 to generate debug output.</param>
    /// <returns>The deskewed and cropped barcodes, or <c>null</c> if none were found.</returns>
    /// <exception cref="ArgumentException">The image is not 8 bpp or has a colormap.</exception>
    // leptonica: pixExtractBarcodes (readbarcode.c)
    public Pixa? ExtractBarcodes(int debugflag)
    {
        const string func = "pixExtractBarcodes";
        if (Depth != 8 || Colormap != null)
            throw Lept.Arg(func, "pixs undefined or not 8 bpp");

        Boxa boxa = LocateBarcodes(20, out Pix pix2, out Pix pix1);
        int n = boxa.Count;
        Log.Info(func, $"{n} possible barcode(s) found");
        if (n == 0)
            return null;

        if (debugflag != 0) {
            boxa.WriteStderr();
            pix2.Display(100, 100);
            pix1.Display(800, 100);
        }

        Pixa pixa = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            Box box = boxa.GetBox(i, L.L_CLONE);
            Pix? pix3 = DeskewBarcode(pix2, box, 15, 20, out float angle, out float conf);
            if (pix3 == null)
                conf = 0.0f;
            Log.Info(func, $"angle = {angle,6:F2}, conf = {conf,6:F2}");
            if (conf > 5.0 && pix3 != null) {
                pixa.AddPix(pix3, L.L_INSERT);
                pixa.AddBox(box, L.L_INSERT);
            }
        }

        return pixa;
    }

    /// <summary>Reads the sequence of bar widths from a deskewed, cropped barcode image.</summary>
    /// <param name="method">The width extraction method, <see cref="L.L_USE_WIDTHS"/> or <see cref="L.L_USE_WINDOWS"/>.</param>
    /// <param name="debugflag">Use 1 to generate debug output.</param>
    /// <returns>The bar widths, each in the set {1,2,3,4}, or <c>null</c> if they could not be extracted.</returns>
    /// <exception cref="ArgumentException">The image is not 8 bpp.</exception>
    // leptonica: pixReadBarcodeWidths (readbarcode.c)
    public Numa? ReadBarcodeWidths(int method, int debugflag)
    {
        const string func = "pixReadBarcodeWidths";
        if (Depth != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (method != L.L_USE_WIDTHS && method != L.L_USE_WINDOWS)
            throw Lept.Arg(func, "invalid method");

        Numa? na = method == L.L_USE_WIDTHS
            ? ExtractBarcodeWidths1(120.0f, 0.25f, out _, out _, debugflag)
            : ExtractBarcodeWidths2(120.0f, out _, out _, debugflag);
        if (na == null)
            Log.Error(func, "barcode widths invalid");
        return na;
    }

    /*------------------------------------------------------------------------*
     *                        Locate barcode in image                         *
     *------------------------------------------------------------------------*/
    /// <summary>Locates the barcodes in an image using an edge filter and morphological masking.</summary>
    /// <param name="thresh">Threshold for binarizing the edge filter output; typically 20.</param>
    /// <param name="ppixb">Receives the binarized edge-filtered image.</param>
    /// <param name="ppixm">Receives the mask over the barcodes.</param>
    /// <returns>The locations of the barcodes; the array is empty if none were found.</returns>
    /// <remarks>The image may have any depth.</remarks>
    // leptonica: pixLocateBarcodes (readbarcode.c)
    public Boxa LocateBarcodes(int thresh, out Pix ppixb, out Pix ppixm)
    {
        Pix pix8 = (Depth == 8 && Colormap == null) ? Clone(this) : ConvertTo8(false);

        Pix pixe = pix8.SobelEdgeFilter(L.L_ALL_EDGES);
        Pix pixb = pixe.ThresholdToBinary(thresh);
        Invert(pixb, pixb);

        Pix pixm = GenerateBarcodeMask(pixb, MaxSpaceWidth, MaxNoiseWidth, MaxNoiseHeight);
        Boxa boxa = pixm.ConnComp(8);
        ppixb = pixb;
        ppixm = pixm;
        return boxa;
    }

    /// <summary>Locates the barcodes in an image, discarding the intermediate images.</summary>
    /// <param name="thresh">Threshold for binarizing the edge filter output; typically 20.</param>
    /// <returns>The locations of the barcodes; the array is empty if none were found.</returns>
    /// <seealso cref="LocateBarcodes(int, out Pix, out Pix)"/>
    public Boxa LocateBarcodes(int thresh)
    {
        return LocateBarcodes(thresh, out _, out _);
    }

    // leptonica: pixGenerateBarcodeMask (readbarcode.c)
    private static Pix GenerateBarcodeMask(Pix pixs, int maxspace, int nwidth, int nheight)
    {
        const string func = "pixGenerateBarcodeMask";
        if (pixs == null || pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not defined");

            /* Identify horizontal barcodes */
        Pix pixt1 = CloseBrick(null, pixs, maxspace + 1, 1);
        Pix pixt2 = OpenBrick(null, pixs, maxspace + 1, 1);
        Xor(pixt2, pixt2, pixt1);
        OpenBrick(pixt2, pixt2, nwidth, nheight);

            /* Identify vertical barcodes */
        pixt1 = CloseBrick(null, pixs, 1, maxspace + 1);
        Pix pixd = OpenBrick(null, pixs, 1, maxspace + 1);
        Xor(pixd, pixd, pixt1);
        OpenBrick(pixd, pixd, nheight, nwidth);

            /* Combine to get all barcodes */
        Or(pixd, pixd, pixt2);
        return pixd;
    }

    /*------------------------------------------------------------------------*
     *                        Extract and deskew barcode                      *
     *------------------------------------------------------------------------*/
    /// <summary>Extracts a barcode region from the image and deskews it.</summary>
    /// <param name="pixb">The binarized edge-filtered image, used to measure the skew.</param>
    /// <param name="box">The region containing the barcode.</param>
    /// <param name="margin">Number of extra pixels around <paramref name="box"/> to extract.</param>
    /// <param name="threshold">Threshold for binarization; about 20.</param>
    /// <param name="pangle">Receives the angle in degrees by which the image was rotated; clockwise is positive.</param>
    /// <param name="pconf">Receives the confidence of the skew estimate.</param>
    /// <returns>The deskewed barcode, or <c>null</c> if the deskewing failed.</returns>
    /// <exception cref="ArgumentException">An argument is invalid.</exception>
    // leptonica: pixDeskewBarcode (readbarcode.c)
    public Pix? DeskewBarcode(Pix pixb, Box box, int margin, int threshold,
                              out float pangle, out float pconf)
    {
        const string func = "pixDeskewBarcode";
        pangle = 0.0f;
        pconf = 0.0f;
        if (Depth != 8)
            throw Lept.Arg(func, "pixs undefined or not 8 bpp");
        if (pixb == null || pixb.Depth != 1)
            throw Lept.Arg(func, "pixb undefined or not 1 bpp");
        if (box == null)
            throw Lept.Arg(func, "box not defined");

        const float deg2rad = 3.1415926535f / 180.0f;
        box.GetGeometry(out int x, out int y, out int w, out int h);
        Box box2 = Box.Create(x - 25, y - 25, w + 51, h + 51);
        Pix? pix1 = pixb.ClipRectangle(box2, out _);
        Pix? pix2 = ClipRectangle(box2, out _);
        if (pix1 == null || pix2 == null)
            return null;

        Pix pix3 = pix1.RotateOrth(1);
        Pix pix4 = Clone(pix1);
        pix3.FindSkewSweepAndSearchScore(out float angle1, out float conf1, out float score1,
                                         1, 1, 0.0f, 45.0f, 2.5f, 0.01f);
        pix4.FindSkewSweepAndSearchScore(out float angle2, out float conf2, out float score2,
                                         1, 1, 0.0f, 45.0f, 2.5f, 0.01f);

        float angle, conf;
        Pix pix5;
        if (score1 >= score2) {
            conf = conf1;
            if (conf1 > 6.0 && Math.Abs(angle1) > 0.1) {
                angle = angle1;
                pix5 = pix2.Rotate(deg2rad * angle1, L.L_ROTATE_AREA_MAP,
                                   L.L_BRING_IN_WHITE, 0, 0);
            } else {
                angle = 0.0f;
                pix5 = Clone(pix2);
            }
        } else {
            conf = conf2;
            Pix pix6 = pix2.RotateOrth(1);
            if (conf2 > 6.0 && Math.Abs(angle2) > 0.1) {
                angle = 90.0f + angle2;
                pix5 = pix6.Rotate(deg2rad * angle2, L.L_ROTATE_AREA_MAP,
                                   L.L_BRING_IN_WHITE, 0, 0);
            } else {
                angle = 90.0f;
                pix5 = Clone(pix6);
            }
        }

        Boxa boxa1 = pix5.LocateBarcodes(threshold);
        int n = boxa1.Count;
        if (n == 0) {
            Log.Error(func, "pixd not made");
            return null;
        }
        Boxa boxa2;
        if (n != 1) {
            Log.Warning(func, $"barcode mask in {n} components");
            boxa2 = boxa1.Sort(L.L_SORT_BY_AREA, L.L_SORT_DECREASING, out _);
        } else {
            boxa2 = boxa1.Copy(L.L_CLONE);
        }
        Box box1 = boxa2.GetBox(0, L.L_CLONE);
        box1.GetGeometry(out x, out y, out w, out h);
        box2 = Box.Create(x - margin, y - margin, w + 2 * margin, h + 2 * margin);
        Pix? pixd = pix5.ClipRectangle(box2, out _);

        pangle = angle;
        pconf = conf;
        if (pixd == null)
            Log.Error(func, "pixd not made");
        return pixd;
    }

    /*------------------------------------------------------------------------*
     *                        Process to get line widths                      *
     *------------------------------------------------------------------------*/
    /// <summary>Extracts the bar widths from a barcode image using the measured widths of the bars.</summary>
    /// <param name="thresh">Estimated pixel threshold for a black/white crossing; typically about 120.</param>
    /// <param name="binfract">Histogram bin size as a fraction of the minimum bar width.</param>
    /// <param name="pnaehist">Receives the histogram of the even (black) bar widths.</param>
    /// <param name="pnaohist">Receives the histogram of the odd (white) bar widths.</param>
    /// <param name="debugflag">Use 1 to generate debug output.</param>
    /// <returns>The bar widths in encoded integer units, or <c>null</c> on failure.</returns>
    /// <remarks>
    /// <para>The widths alternate between black and white, starting and ending with black.</para>
    /// <para>The widths are measured in pixels between transitions; histograms of the black and white widths are built and interpreted.</para>
    /// </remarks>
    // leptonica: pixExtractBarcodeWidths1 (readbarcode.c)
    public Numa? ExtractBarcodeWidths1(float thresh, float binfract,
                                       out Numa? pnaehist, out Numa? pnaohist,
                                       int debugflag)
    {
        const string func = "pixExtractBarcodeWidths1";
        pnaehist = null;
        pnaohist = null;
        if (Depth != 8)
            throw Lept.Arg(func, "pixs undefined or not 8 bpp");

        Numa? nac = ExtractBarcodeCrossings(thresh, debugflag);
        if (nac == null) {
            Log.Error(func, "nac not made");
            return null;
        }

        return nac.QuantizeCrossingsByWidth(binfract, out pnaehist, out pnaohist, debugflag);
    }

    /// <summary>Extracts the bar widths from a barcode image by analyzing the crossings in a sliding window.</summary>
    /// <param name="thresh">Estimated pixel threshold for a black/white crossing; typically about 120.</param>
    /// <param name="pwidth">Receives the best window width in pixels.</param>
    /// <param name="pnac">Receives the number of crossings in each window.</param>
    /// <param name="debugflag">Use 1 to generate debug output.</param>
    /// <returns>The bar widths in encoded integer units, or <c>null</c> on failure.</returns>
    /// <remarks>The widths alternate between black and white, starting and ending with black.</remarks>
    // leptonica: pixExtractBarcodeWidths2 (readbarcode.c)
    public Numa? ExtractBarcodeWidths2(float thresh, out float pwidth,
                                       out Numa? pnac, int debugflag)
    {
        const string func = "pixExtractBarcodeWidths2";
        pwidth = 0.0f;
        pnac = null;
        if (Depth != 8)
            throw Lept.Arg(func, "pixs undefined or not 8 bpp");

        Numa? nacp = ExtractBarcodeCrossings(thresh, debugflag);
        if (nacp == null) {
            Log.Error(func, "nacp not made");
            return null;
        }

        return nacp.QuantizeCrossingsByWindow(2.0f, out pwidth, out _, out pnac, debugflag);
    }

    /// <summary>Finds the positions where the averaged raster scans of a barcode image cross the threshold.</summary>
    /// <param name="thresh">Estimated pixel threshold for a white/black crossing; typically about 120.</param>
    /// <param name="debugflag">Use 1 to generate debug output.</param>
    /// <returns>The crossing locations in pixel units, or <c>null</c> if there are fewer than 10 crossings.</returns>
    /// <exception cref="ArgumentException">The image is not 8 bpp.</exception>
    // leptonica: pixExtractBarcodeCrossings (readbarcode.c)
    public Numa? ExtractBarcodeCrossings(float thresh, int debugflag)
    {
        const string func = "pixExtractBarcodeCrossings";
        if (Depth != 8)
            throw Lept.Arg(func, "pixs undefined or not 8 bpp");

        Numa nas = AverageRasterScans(50);
        int w = Width;
        Numa.InterpolateEqxInterval(0.0f, 1.0f, nas, L.L_QUADRATIC_INTERP,
                                    0.0f, (float)(w - 1), 4 * w + 1,
                                    out Numa nax, out Numa nay);

        if (debugflag != 0) {
            Utils.LeptMkdir("lept/barcode");
            Gplot gplot = Gplot.Create("/tmp/lept/barcode/signal", L.GPLOT_PNG,
                                       "Pixel values", "dist in pixels", "value");
            gplot.AddPlot(nax, nay, L.GPLOT_LINES, "plot 1");
            gplot.MakeOutput();
        }

        Numa.SelectCrossingThreshold(nax, nay, thresh, out float bestthresh);
        Numa nad = Numa.CrossingsByThreshold(nax, nay, bestthresh);
        if (nad.Count < 10) {
            Log.Error(func, $"Only {nad.Count} crossings; failure");
            return null;
        }
        return nad;
    }

    /*------------------------------------------------------------------------*
     *                         Average adjacent rasters                       *
     *------------------------------------------------------------------------*/
    // leptonica: pixAverageRasterScans (readbarcode.c)
    private Numa AverageRasterScans(int nscans)
    {
        const string func = "pixAverageRasterScans";
        if (Depth != 8)
            throw Lept.Arg(func, "pixs undefined or not 8 bpp");

        GetDimensions(out int w, out int h, out _);
        int first, last;
        if (nscans > h) {
            first = 0;
            last = h - 1;
            nscans = h;
        } else {
            first = (h - nscans) / 2;
            last = first + nscans - 1;
        }

        Numa nad = Numa.Create(w);
        nad.Count = w;
        float[] array = nad.GetFArray(L.L_NOCOPY);
        int wpl = Wpl;
        uint[] data = Data;
        for (int j = 0; j < w; j++) {
            for (int i = first; i <= last; i++) {
                ReadOnlySpan<uint> line = data.AsSpan(i * wpl, wpl);
                array[j] += ArrayAccess.GetDataByte(line, j);
            }
            array[j] /= (float)nscans;
        }

        return nad;
    }
}

public sealed partial class Pixa
{
    private const int BarcodeMinWidth = 50;
    private const int BarcodeMinHeight = 50;

    /// <summary>Decodes each deskewed, cropped barcode image in the array.</summary>
    /// <param name="format">
    /// The barcode format, for example <see cref="L.L_BF_CODEI2OF5"/> or <see cref="L.L_BF_CODE93"/>,
    /// or <see cref="L.L_BF_ANY"/> to identify the format automatically.
    /// </param>
    /// <param name="method">The width extraction method, <see cref="L.L_USE_WIDTHS"/> or <see cref="L.L_USE_WINDOWS"/>.</param>
    /// <param name="psaw">Receives the strings of bar widths, one per barcode.</param>
    /// <param name="debugflag">Use 1 to generate debug output.</param>
    /// <returns>The decoded text, one string for each barcode found, or <c>null</c> on failure.</returns>
    /// <exception cref="ArgumentException">The format is unsupported or the method is invalid.</exception>
    /// <remarks>Each image in the array is an 8 bpp deskewed and cropped barcode.</remarks>
    // leptonica: pixReadBarcodes (readbarcode.c)
    public Sarray? ReadBarcodes(int format, int method, out Sarray? psaw, int debugflag)
    {
        const string func = "pixReadBarcodes";
        psaw = null;
        if (format != L.L_BF_ANY && !Barcode.FormatIsSupported(format)) {
            Log.Error(func, "unsupported format");
            return null;
        }
        if (method != L.L_USE_WIDTHS && method != L.L_USE_WINDOWS)
            throw Lept.Arg(func, "invalid method");

        int count = Count;
        Sarray saw = Sarray.Create(count);
        Sarray sad = Sarray.Create(count);
        for (int i = 0; i < count; i++) {
            Pix pix1 = GetPix(i, L.L_CLONE);
            pix1.GetDimensions(out int w, out int h, out _);
            if (w < BarcodeMinWidth || h < BarcodeMinHeight) {
                Log.Error(func, $"pix is too small: w = {w}, h = {h}");
                continue;
            }
            Numa? na = pix1.ReadBarcodeWidths(method, debugflag);
            if (na == null) {
                Log.Error(func, "valid barcode widths not returned");
                continue;
            }

            int nbars = na.Count;
            char[] barChars = new char[nbars];
            for (int j = 0; j < nbars; j++)
                barChars[j] = (char)('0' + na.GetIValue(j));
            string barstr = new string(barChars);
            saw.AddString(barstr, L.L_INSERT);

            string? data = Barcode.DispatchDecoder(barstr, format, debugflag);
            if (data == null) {
                Log.Error(func, "barcode not decoded");
                sad.AddString(string.Empty, L.L_COPY);
                continue;
            }
            sad.AddString(data, L.L_INSERT);
        }

        if (saw.Count == 0) {
            Log.Error(func, "no valid barcode data");
            return null;
        }

        psaw = saw;
        return sad;
    }
}

public sealed partial class Numa
{
    /*------------------------------------------------------------------------*
     *                   Signal processing for barcode widths                 *
     *------------------------------------------------------------------------*/
    /// <summary>Converts a sequence of crossing locations into bar widths in unit sizes, using histograms of the measured widths.</summary>
    /// <param name="binfract">Histogram bin size as a fraction of the minimum bar width; for example, 0.25 gives four bins across the narrowest bar.</param>
    /// <param name="pnaehist">Receives the histogram of the even (black) bar widths.</param>
    /// <param name="pnaohist">Receives the histogram of the odd (white) bar widths.</param>
    /// <param name="debugflag">Use 1 to generate debug plots of intermediate results.</param>
    /// <returns>The sequence of bar widths, each in the set {1,2,3,4}, or <c>null</c> on failure.</returns>
    /// <remarks>
    /// The histograms of black and white bar widths should show well-defined peaks, each
    /// corresponding to a specific width.
    /// </remarks>
    // leptonica: numaQuantizeCrossingsByWidth (readbarcode.c)
    public Numa? QuantizeCrossingsByWidth(float binfract, out Numa? pnaehist,
                                          out Numa? pnaohist, int debugflag)
    {
        const string func = "numaQuantizeCrossingsByWidth";
        pnaehist = null;
        pnaohist = null;
        if (Count < 10)
            throw Lept.Arg(func, "n < 10");
        if (binfract <= 0.0f)
            throw Lept.Arg(func, "binfract <= 0.0");

        int ret = GetCrossingDistances(this, out Numa? naedist, out Numa? naodist,
                                       out float minsize, out float maxsize);
        if (ret != 0 || naedist == null || naodist == null ||
            minsize < 1.0 || maxsize / minsize > 8.0) {
            Log.Error(func, $"bad data, or minsize = {minsize,5:F2} < 1.0 or max/min = {maxsize / minsize} > 4.0");
            return null;
        }

        Numa naehist = naedist.MakeHistogramClipped(binfract * minsize,
                                                    (float)(1.25 / binfract) * maxsize);
        Numa naohist = naodist.MakeHistogramClipped(binfract * minsize,
                                                    (float)(1.25 / binfract) * maxsize);

        if (debugflag != 0) {
            Utils.LeptMkdir("lept/barcode");
            Gplot gplot = Gplot.Create("/tmp/lept/barcode/histw", L.GPLOT_PNG,
                                       "Raw width histogram", "Width", "Number");
            gplot.AddPlot(null, naehist, L.GPLOT_LINES, "plot black");
            gplot.AddPlot(null, naohist, L.GPLOT_LINES, "plot white");
            gplot.MakeOutput();
        }

        Numa naerange = LocatePeakRanges(naehist, 1.0f / binfract, 1.0f / binfract, 0.0f);
        Numa naorange = LocatePeakRanges(naohist, 1.0f / binfract, 1.0f / binfract, 0.0f);

        Numa naecent = GetPeakCentroids(naehist, naerange);
        Numa naocent = GetPeakCentroids(naohist, naorange);

        Numa? naelut = GetPeakWidthLUT(naerange, naecent);
        Numa? naolut = GetPeakWidthLUT(naorange, naocent);
        if (naelut == null || naolut == null)
            return null;

        Numa nad = Create(0);
        int ned = naedist.Count;
        int nod = naodist.Count;
        if (nod != ned - 1)
            Log.Warning(func, "ned != nod + 1");
        float factor = 1.0f / (binfract * minsize);
        for (int i = 0; i < ned - 1; i++) {
            float val = naedist.GetFValue(i);
            int width = (int)(factor * val);
            int iw = (width >= 0 && width < naelut.Count) ? naelut.GetIValue(width) : 0;
            nad.AddNumber(iw);

            val = naodist.GetFValue(i);
            width = (int)(factor * val);
            iw = (width >= 0 && width < naolut.Count) ? naolut.GetIValue(width) : 0;
            nad.AddNumber(iw);
        }
        {
            float val = naedist.GetFValue(ned - 1);
            int width = (int)(factor * val);
            int iw = (width >= 0 && width < naelut.Count) ? naelut.GetIValue(width) : 0;
            nad.AddNumber(iw);
        }

        if (debugflag != 0) {
            Console.Error.WriteLine(" ---- Black bar widths (pixels) ------ ");
            naedist.WriteStderr();
            Console.Error.WriteLine(" ---- Histogram of black bar widths ------ ");
            naehist.WriteStderr();
            Console.Error.WriteLine(" ---- Peak ranges in black bar histogram bins --- ");
            naerange.WriteStderr();
            Console.Error.WriteLine(" ---- Peak black bar centroid width values ------ ");
            naecent.WriteStderr();
            Console.Error.WriteLine(" ---- Black bar lookup table ------ ");
            naelut.WriteStderr();
            Console.Error.WriteLine(" ---- White bar widths (pixels) ------ ");
            naodist.WriteStderr();
            Console.Error.WriteLine(" ---- Histogram of white bar widths ------ ");
            naohist.WriteStderr();
            Console.Error.WriteLine(" ---- Peak ranges in white bar histogram bins --- ");
            naorange.WriteStderr();
            Console.Error.WriteLine(" ---- Peak white bar centroid width values ------ ");
            naocent.WriteStderr();
            Console.Error.WriteLine(" ---- White bar lookup table ------ ");
            naolut.WriteStderr();
        }

        pnaehist = naehist;
        pnaohist = naohist;
        return nad;
    }

    // leptonica: numaGetCrossingDistances (readbarcode.c)
    private static int GetCrossingDistances(Numa nas, out Numa? pnaedist, out Numa? pnaodist,
                                            out float pmindist, out float pmaxdist)
    {
        const string func = "numaGetCrossingDistances";
        pnaedist = null;
        pnaodist = null;
        pmindist = 0.0f;
        pmaxdist = 0.0f;
        if (nas == null || nas.Count < 2)
            return 1;

        int n = nas.Count;
        Numa naedist = Create(n / 2 + 1);
        Numa naodist = Create(n / 2);
        float val = nas.GetFValue(0);
        for (int i = 1; i < n; i++) {
            float newval = nas.GetFValue(i);
            if (i % 2 != 0)
                naedist.AddNumber(newval - val);
            else
                naodist.AddNumber(newval - val);
            val = newval;
        }

        Numa na1 = naedist.Copy();
        na1.Join(naodist, 0, -1);
        Numa na2 = na1.MakeHistogram(100, out _);
        float mindist = na2.HistogramGetValFromRank(0.1f);
        float maxdist = na2.HistogramGetValFromRank(0.9f);
        Log.Info(func, $"mindist = {mindist,7:F3}, maxdist = {maxdist,7:F3}");

        pnaedist = naedist;
        pnaodist = naodist;
        pmindist = mindist;
        pmaxdist = maxdist;
        return 0;
    }

    // leptonica: numaLocatePeakRanges (readbarcode.c)
    private static Numa LocatePeakRanges(Numa nas, float minfirst, float minsep, float maxmin)
    {
        int n = nas.Count;
        Numa nad = Create(0);

        bool inpeak = false;
        int left = 0;
        float prevcenter = minfirst - minsep - 1.0f;
        for (int i = 0; i < n; i++) {
            float val = nas.GetFValue(i);
            if (!inpeak && val > maxmin) {
                inpeak = true;
                left = i;
            } else if (inpeak && val <= maxmin) {
                float center = (float)((left + i - 1.0) / 2.0);
                if (center - prevcenter >= minsep) {
                    inpeak = false;
                    nad.AddNumber(left);
                    nad.AddNumber(i - 1);
                    prevcenter = center;
                } else if (nad.Count > 0) {
                    nad.SetValue(nad.Count - 1, i - 1);
                }
            }
        }
        if (inpeak) {
            nad.AddNumber(left);
            nad.AddNumber(n - 1);
        }

        return nad;
    }

    // leptonica: numaGetPeakCentroids (readbarcode.c)
    private static Numa GetPeakCentroids(Numa nahist, Numa narange)
    {
        int nr = narange.Count / 2;
        Numa nad = Create(4);
        for (int i = 0; i < nr; i++) {
            int low = narange.GetIValue(2 * i);
            int high = narange.GetIValue(2 * i + 1);
            float cent = 0.0f;
            float sum = 0.0f;
            for (int j = low; j <= high; j++) {
                float val = nahist.GetFValue(j);
                cent += j * val;
                sum += val;
            }
            if (sum > 0.0f)
                nad.AddNumber(cent / sum);
        }
        return nad;
    }

    // leptonica: numaGetPeakWidthLUT (readbarcode.c)
    private static Numa? GetPeakWidthLUT(Numa narange, Numa nacent)
    {
        const string func = "numaGetPeakWidthLUT";
        int nc = nacent.Count;
        if (nc < 1 || nc > 4) {
            Log.Error(func, "nc must be 1, 2, 3, or 4");
            return null;
        }

        float rat32 = 0.0f;
        if (nc > 1) {
            float[] warray = nacent.GetFArray(L.L_NOCOPY);
            if (warray[0] == 0.0f) {
                Log.Error(func, "first peak has width 0.0");
                return null;
            }
            float rat21 = warray[1] / warray[0];
            if (rat21 < 1.5 || rat21 > 2.6)
                Log.Warning(func, $"width ratio 2/1 = {rat21}");
            if (nc > 2) {
                rat32 = warray[2] / warray[1];
                if (rat32 < 1.3 || rat32 > 2.25)
                    Log.Warning(func, $"width ratio 3/2 = {rat32}");
            }
            if (nc == 4) {
                float rat42 = warray[3] / warray[1];
                if (rat42 < 1.7 || rat42 > 2.3)
                    Log.Warning(func, $"width ratio 4/2 = {rat42}");
            }
        }

        Span<int> assign = stackalloc int[4] { 1, 2, 3, 4 };
        if (nc == 3 && rat32 > 1.75)
            assign[2] = 4;

        narange.GetMax(out float max, out _);
        int imax = (int)max;
        Numa nalut = Create(imax + 1);
        nalut.Count = imax + 1;
        for (int i = 0; i < nc; i++) {
            int low = narange.GetIValue(2 * i);
            if (i == 0) low--;
            int high = narange.GetIValue(2 * i + 1);
            for (int j = low; j <= high; j++) {
                if (j >= 0 && j < nalut.Count)
                    nalut.SetValue(j, assign[i]);
            }
        }

        return nalut;
    }

    /// <summary>Converts a sequence of crossing locations into bar widths in unit sizes, using a sliding window.</summary>
    /// <param name="ratio">Ratio of the window width to the minimum distance between crossings.</param>
    /// <param name="pwidth">Receives the best window width in pixels, approximately the width of the narrowest bars.</param>
    /// <param name="pfirstloc">Receives the location of the first crossing.</param>
    /// <param name="pnac">Receives the number of crossings in each window: 0, 1 or occasionally 2.</param>
    /// <param name="debugflag">Use 1 to generate debug plots of intermediate results.</param>
    /// <returns>The sequence of bar widths in unit sizes, or <c>null</c> on failure.</returns>
    /// <remarks>
    /// <para>The minimum window size is set by the minimum distance between zero crossings.</para>
    /// <para>A value of 2 in <paramref name="pnac"/> is interpreted as ending two runs: the previous one and another one of length 1.</para>
    /// </remarks>
    // leptonica: numaQuantizeCrossingsByWindow (readbarcode.c)
    public Numa? QuantizeCrossingsByWindow(float ratio, out float pwidth,
                                           out float pfirstloc, out Numa? pnac,
                                           int debugflag)
    {
        const string func = "numaQuantizeCrossingsByWindow";
        pwidth = 0.0f;
        pfirstloc = 0.0f;
        pnac = null;
        if (Count < 2)
            throw Lept.Arg(func, "nas size < 2");

        GetCrossingDistances(this, out _, out _, out float minsize, out _);

        EvalBestWidthAndShift(this, 100, 10, minsize, ratio * minsize,
                              out float minwidth, out float minshift, out _);
        EvalBestWidthAndShift(this, 100, 10, 0.98f * minwidth, 1.02f * minwidth,
                              out minwidth, out minshift, out _);

        Log.Info(func, $"best width = {minwidth,7:F3}, best shift = {minshift,7:F3}");

        EvalSyncError(this, 0, 0, minwidth, minshift, out _, out Numa? nac);
        if (nac == null)
            return null;

        pwidth = minwidth;
        pfirstloc = GetFValue(0) + minshift;

        Numa nad = Create(0);
        int nw = nac.Count;
        bool started = false;
        int count = 0;
        for (int i = 0; i < nw; i++) {
            int trans = nac.GetIValue(i);
            if (trans > 2)
                Log.Warning(func, $"trans = {trans} > 2 !!!");
            if (started) {
                if (trans > 1) {
                    nad.AddNumber(count);
                    trans--;
                    count = 1;
                }
                if (trans == 1) {
                    nad.AddNumber(count);
                    count = 1;
                } else {
                    count++;
                }
            }
            if (!started && trans != 0) {
                started = true;
                if (trans == 2)
                    nad.AddNumber(1);
                count = 1;
            }
        }

        pnac = nac;
        return nad;
    }

    // leptonica: numaEvalBestWidthAndShift (readbarcode.c)
    private static int EvalBestWidthAndShift(Numa nas, int nwidth, int nshift,
                                             float minwidth, float maxwidth,
                                             out float pbestwidth, out float pbestshift,
                                             out float pbestscore)
    {
        pbestwidth = 0.0f;
        pbestshift = 0.0f;
        pbestscore = 1.0f;
        if (nas == null)
            return 1;

        float bestwidth = 0.0f;
        float bestshift = 0.0f;
        float bestscore = 1.0f;
        float delwidth = (float)((maxwidth - minwidth) / (nwidth - 1.0));
        for (int i = 0; i < nwidth; i++) {
            float width = minwidth + delwidth * i;
            float delshift = width / (float)nshift;
            for (int j = 0; j < nshift; j++) {
                float shift = (float)(-0.5 * (width - delshift) + j * delshift);
                EvalSyncError(nas, 0, 0, width, shift, out float score, out _);
                if (score < bestscore) {
                    bestscore = score;
                    bestwidth = width;
                    bestshift = shift;
                }
            }
        }

        pbestwidth = bestwidth;
        pbestshift = bestshift;
        pbestscore = bestscore;
        return 0;
    }

    // leptonica: numaEvalSyncError (readbarcode.c)
    private static int EvalSyncError(Numa nas, int ifirst, int ilast,
                                     float width, float shift,
                                     out float pscore, out Numa? pnad)
    {
        pscore = 0.0f;
        pnad = null;
        if (nas == null || nas.Count < 2)
            return 1;
        int n = nas.Count;
        if (ifirst < 0) ifirst = 0;
        if (ilast <= 0) ilast = n - 1;
        if (ifirst >= ilast)
            return 1;
        int nc = ilast - ifirst + 1;

        float score = 0.0f;
        float xfirst = nas.GetFValue(ifirst);
        float xlast = nas.GetFValue(ilast);
        int nw = (int)((xlast - xfirst + 2.0 * width) / width);
        Numa nad = Create(nw);
        nad.Count = nw;
        float xleft = (float)(xfirst - width / 2.0 + shift);
        for (int i = ifirst; i <= ilast; i++) {
            float xc = nas.GetFValue(i);
            int iw = (int)((xc - xleft) / width);
            float xwc = (float)(xleft + (iw + 0.5) * width);
            score += (xwc - xc) * (xwc - xc);
            if (iw >= 0 && iw < nad.Count)
                nad.SetValue(iw, nad.GetIValue(iw) + 1);
        }

        pscore = (float)(4.0 * score / (width * width * (float)nc));
        pnad = nad;
        return 0;
    }
}
