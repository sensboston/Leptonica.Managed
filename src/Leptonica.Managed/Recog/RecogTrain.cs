using System;
using System.Globalization;
using System.IO;

namespace Leptonica;

/*
 *  recogtrain.c: training on labeled and unlabeled data, outlier removal,
 *  padding, boot digit recognizer helpers, and debugging views.
 */

public sealed partial class Recog
{
    private const int DefaultMinSetSize = 1;
    private const float DefaultMinSetFract = 0.4f;
    private const float DefaultMinScore = 0.75f;
    private const int DefaultMinTarget = 3;
    private const float LowerScoreThreshold = 0.5f;

    /*------------------------------------------------------------------------*
     *                                Training                                *
     *------------------------------------------------------------------------*/
    // leptonica: recogTrainLabeled (recogtrain.c)
    /// <summary>Adds one labeled character image to the training set.</summary>
    /// <param name="pixs">The character image; if its depth is greater than 1, it is thresholded to 1 bpp.</param>
    /// <param name="box">Optional cropping box giving the location of the character in <paramref name="pixs"/>.</param>
    /// <param name="text">Optional text label; if <c>null</c>, the text field of the image is used.</param>
    /// <param name="debug">1 to display images of samples that are not captured.</param>
    /// <returns>0 on success; 1 if the sample could not be processed.</returns>
    /// <exception cref="ArgumentException"><paramref name="pixs"/> is <c>null</c>.</exception>
    /// <remarks>
    /// Training is restricted to the addition of a single character in an arbitrary (for example, UTF-8) character set.
    /// </remarks>
    public int TrainLabeled(Pix pixs, Box? box, string? text, int debug)
    {
        const string func = "recogTrainLabeled";
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");

        int ret = ProcessLabeled(pixs, box, text, out Pix? pix);
        if (ret != 0 || pix == null) {
            Log.Warning(func, $"failure to get sample '{text}' for training");
            return 1;
        }

        AddSample(pix, debug);
        return 0;
    }

    // leptonica: recogProcessLabeled (recogtrain.c)
    /// <summary>Crops and binarizes a labeled character image so that it can be added as a training sample.</summary>
    /// <param name="pixs">The character image; if its depth is greater than 1, it is thresholded to 1 bpp.</param>
    /// <param name="box">Optional cropping box giving the location of the character in <paramref name="pixs"/>.</param>
    /// <param name="text">Optional text label; if <c>null</c>, the text field of the image is used.</param>
    /// <param name="ppix">Receives the 1 bpp, labeled image of one character.</param>
    /// <returns>0 on success; 1 if the image has no label, no foreground, or more than one separated component.</returns>
    /// <exception cref="ArgumentException"><paramref name="pixs"/> is <c>null</c>.</exception>
    public int ProcessLabeled(Pix pixs, Box? box, string? text, out Pix? ppix)
    {
        const string func = "recogProcessLabeled";
        ppix = null;
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");

        bool textin = !string.IsNullOrEmpty(text);
        bool textinpix = !string.IsNullOrEmpty(pixs.Text);
        if (!textin && !textinpix) {
            Log.Error(func, $"no text: {num_samples}");
            return 1;
        }
        string textdata = textin ? text! : pixs.Text!;

        Pix pix1 = box != null ? pixs.ClipRectangle(box, out _)! : Pix.Clone(pixs);
        Pix pix2 = pix1.Depth > 1 ? pix1.ConvertTo1(threshold) : Pix.Clone(pix1);

            /* Remove isolated noise removed by a vertical opening of size 5 */
        Pix pix3 = pix2.MorphSequence("o1.5", 0);
        Pix.SeedfillBinary(pix3, pix3, pix2, 8);

            /* Clip to foreground */
        pix3.ClipToForeground(out Pix? pix4, out _);
        if (pix4 == null) {
            Log.Error(func, "pix4 is empty");
            return 1;
        }

            /* Verify that if there is more than 1 c.c., they all have horizontal overlap */
        Numa na = pix4.CountByColumn(null);
        int nsets = na.CountNonzeroRuns();
        if (nsets > 1) {
            Log.Warning(func, $"found {nsets} sets of horiz separated c.c.; skipping");
            return 1;
        }

        pix4.Text = textdata;
        ppix = pix4;
        return 0;
    }

    // leptonica: recogAddSample (recogtrain.c)
    /// <summary>Adds a labeled sample of a single character to the training set.</summary>
    /// <param name="pix">A 1 bpp image of a single character, with its class label in the text field.</param>
    /// <param name="debug">1 to log the identified class.</param>
    /// <returns>0 on success.</returns>
    /// <remarks>
    /// The array of unscaled samples is initialized to accept up to 256 different classes. When training is finished,
    /// the arrays are truncated to the actual number of classes. To pad an existing recognizer from the boot recognizers,
    /// training is started again; if samples from a new class are added, the array is extended with a new set to hold them.
    /// </remarks>
    public int AddSample(Pix pix, int debug)
    {
        const string func = "recogAddSample";
        if (pix == null || pix.Depth != 1)
            throw Lept.Arg(func, "pix not defined or not 1 bpp");
        if (train_done) {
            Log.Error(func, "not added: training has been completed");
            return 1;
        }
        Pixaa paa = pixaa_u;

        string? text = pix.Text;
        if (!TryConvertCharstrToInt(text, out int charint, out _)) {
            Log.Error(func, $"invalid text: {text}");
            return 1;
        }

        if (GetClassIndex(charint, text!, out int index) == 1) {
            int npa = paa.Count;
            if (index > npa) {
                Log.Error(func, $"oops: bad index {index} > npa {npa}!!");
                return 1;
            }
            if (index == npa) {
                Log.Info(func, $"Adding new class and pixa: index = {index}, text = {text}");
                Pixa pixa1 = Pixa.Create(10);
                paa.AddPixa(pixa1, L.L_INSERT);
            }
        }
        if (debug != 0) {
            Log.Info(func, $"Identified text label: {text}");
            Log.Info(func, $"Identified: charint = {charint}, index = {index}");
        }

        num_samples++;
        paa.AddPix(index, pix, null, L.L_COPY);
        return 0;
    }

    // leptonica: recogModifyTemplate (recogtrain.c)
    /// <summary>Optionally scales a template and converts it into strokes of fixed width.</summary>
    /// <param name="pixs">A 1 bpp template image.</param>
    /// <returns>The modified template image.</returns>
    public Pix? ModifyTemplate(Pix pixs)
    {
        const string func = "recogModifyTemplate";
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");

        pixs.GetDimensions(out int w, out int h, out _);
        Pix pix1;
        if ((scalew == 0 || scalew == w) && (scaleh == 0 || scaleh == h))
            pix1 = Pix.Copy(null, pixs);
        else
            pix1 = pixs.ScaleToSize(scalew, scaleh);

        Pix pix2 = linew <= 0 ? Pix.Clone(pix1) : pix1.SetStrokeWidth(linew, 1, 8);
        if (pix2.Zero()) {
            Log.Error(func, "modified template has no pixels");
            return null;
        }
        return pix2;
    }

    // leptonica: recogAverageSamples (recogtrain.c)
    /// <summary>Computes the average template of each class, from both the unscaled and the scaled samples.</summary>
    /// <param name="debug">1 to display the resulting templates and their centroids.</param>
    /// <returns>0 on success; 1 on failure.</returns>
    /// <remarks>
    /// <para>This is only called when splitting characters using either the document image decoding method or the greedy
    /// splitter, and by a special recognizer that is used to remove outliers.</para>
    /// <para>If the data in any class is nonexistent (no samples) or very bad (no foreground pixels in the average), or if
    /// the ratio of the maximum to the minimum average unscaled class template height is greater than the allowed ratio,
    /// this method fails. The caller must check the return value and discard the recognizer on failure.</para>
    /// </remarks>
    public int AverageSamples(int debug)
    {
        const string func = "recogAverageSamples";
        if (ave_done) {
            if (debug != 0)
                ShowAverageTemplates();
            return 0;
        }

        int size = setsize;
        pixa_u = Pixa.Create(size);
        pta_u = Pta.Create(size);
        nasum_u = Numa.Create(size);

        pixa = Pixa.Create(size);
        pta = Pta.Create(size);
        nasum = Numa.Create(size);

        bool badclass = false;
        for (int i = 0; i < size; i++) {
            Pixa pixa1 = pixaa_u.GetPixa(i, L.L_CLONE);
            Pta pta1 = ptaa_u!.GetPta(i, L.L_CLONE);
            int nsamp = Math.Min(pixa1.Count, 256);
            if (nsamp == 0) {
                Log.Error(func, $"no samples in class {i}");
                badclass = true;
                break;
            }
            pixa1.AccumulateSamples(pta1, out Pix pix1, out float x, out float y);
            Pix pix2 = pix1.ThresholdToBinary(Math.Max(1, nsamp / 2));
            Pix.Invert(pix2, pix2);
            pix2.ClipToForeground(out Pix? pix3, out Box? box);
            if (box == null || pix3 == null) {
                Log.Error(func, $"no fg pixels in average for uclass {i}");
                badclass = true;
                break;
            }
            box.GetGeometry(out int bx, out int by, out _, out _);
            pixa_u.AddPix(pix3, L.L_INSERT);
            pta_u.AddPt(x - bx, y - by);
            int area = pix3.CountPixels(sumtab);
            nasum_u.AddNumber(area);
        }

        if (badclass) {
            Log.Error(func, "at least 1 bad class");
            return 1;
        }

        pixa_u.SizeRange(out minwidth_u, out minheight_u, out maxwidth_u, out maxheight_u);
        float hratio = (float)maxheight_u / (float)minheight_u;
        if (hratio > max_ht_ratio) {
            Log.Error(func, $"ratio of max/min height of average templates = {hratio:F1}");
            return 1;
        }

        for (int i = 0; i < size; i++) {
            Pixa pixa1 = pixaa!.GetPixa(i, L.L_CLONE);
            Pta pta1 = ptaa!.GetPta(i, L.L_CLONE);
            int nsamp = Math.Min(pixa1.Count, 256);
            pixa1.AccumulateSamples(pta1, out Pix pix1, out float x, out float y);
            Pix pix2 = pix1.ThresholdToBinary(Math.Max(1, nsamp / 2));
            Pix.Invert(pix2, pix2);
            pix2.ClipToForeground(out Pix? pix3, out Box? box);
            if (box == null || pix3 == null) {
                Log.Error(func, $"no fg pixels in average for class {i}");
                badclass = true;
                break;
            }
            box.GetGeometry(out int bx, out int by, out _, out _);
            pixa.AddPix(pix3, L.L_INSERT);
            pta.AddPt(x - bx, y - by);
            int area = pix3.CountPixels(sumtab);
            nasum.AddNumber(area);
        }

        if (badclass) {
            Log.Error(func, "no fg pixels in at least 1 class");
            return 1;
        }

        pixa.SizeRange(out minwidth, out _, out maxwidth, out _);
        min_splitw = Math.Max(5, minwidth_u - 5);
        max_splith = maxheight_u + 12;

        if (debug != 0)
            ShowAverageTemplates();

        ave_done = true;
        return 0;
    }

    // leptonica: recogTrainingFinished (recogtrain.c)
    /// <summary>Completes training after all samples have been added, generating the templates used for matching.</summary>
    /// <param name="precog">The recognizer; it is set to <c>null</c> if the templates are not good enough.</param>
    /// <param name="modifyflag">1 to apply <see cref="ModifyTemplate"/> to generate the templates; 0 otherwise.</param>
    /// <param name="minsize">Minimum number of samples required in a class; -1 for the default.</param>
    /// <param name="minfract">Minimum fraction of the largest class that each class must contain; -1.0 for the default.</param>
    /// <returns>0 on success; 1 on error, in which case <paramref name="precog"/> is discarded.</returns>
    /// <remarks>
    /// <para>This must be called after all training samples have been added.</para>
    /// <para>Usually <paramref name="modifyflag"/> is 1, because the modified templates are the ones used for matching.
    /// The exception is reading a serialized recognizer, where the same set of templates is put in both the unscaled
    /// and the modified sets.</para>
    /// <para>This allocates (or reallocates) storage for the possibly modified bitmaps, centroids and foreground areas;
    /// generates the possibly modified bitmaps; computes centroid and foreground area data for both the unscaled and
    /// modified bitmaps; and truncates the arrays from 256 down to the actual number of classes.</para>
    /// <para>Putting these operations here makes it simple to recompute the recognizer with different modifications on
    /// the bitmaps. Call <see cref="ShowContent"/> to display the templates.</para>
    /// </remarks>
    public static int TrainingFinished(ref Recog? precog, int modifyflag,
                                       int minsize, float minfract)
    {
        const string func = "recogTrainingFinished";
        if (precog == null)
            throw Lept.Arg(func, "recog not defined");
        Recog recog = precog;
        if (recog.train_done)
            return 0;

        TemplatesAreOK(recog, minsize, minfract, out bool ok);
        if (!ok) {
            precog = null;
            Log.Error(func, "bad templates");
            return 1;
        }

        int size = recog.maxarraysize;
        Pixaa paa = Pixaa.Create(size);
        Pixa pixaInit = Pixa.Create(1);
        paa.InitFull(pixaInit);
        recog.pixaa = paa;

        Ptaa ptaaU = Ptaa.Create(size);
        Pta ptaInit = Pta.Create(0);
        ptaaU.InitFull(ptaInit);
        recog.ptaa_u = ptaaU;

        Ptaa ptaaS = Ptaa.Create(size);
        ptaaS.InitFull(ptaInit);
        recog.ptaa = ptaaS;

        recog.naasum_u = Numaa.CreateFull(size, 0);
        recog.naasum = Numaa.CreateFull(size, 0);

        Pixaa paaU = recog.pixaa_u;
        int nc = recog.setsize;
        for (int i = 0; i < nc; i++) {
            Pixa pixaClass = paaU.GetPixa(i, L.L_CLONE);
            int ns = pixaClass.Count;
            for (int j = 0; j < ns; j++) {
                Pix pix = pixaClass.GetPix(j, L.L_CLONE);
                pix.Centroid(recog.centtab, recog.sumtab, out float xave, out float yave);
                recog.ptaa_u.AddPt(i, xave, yave);
                int area = pix.CountPixels(recog.sumtab);
                recog.naasum_u.AddNumber(i, area);

                Pix? pixd = modifyflag == 1 ? recog.ModifyTemplate(pix) : Pix.Clone(pix);
                if (pixd != null) {
                    recog.pixaa.AddPix(i, pixd, null, L.L_INSERT);
                    pixd.Centroid(recog.centtab, recog.sumtab, out xave, out yave);
                    recog.ptaa.AddPt(i, xave, yave);
                    area = pixd.CountPixels(recog.sumtab);
                    recog.naasum.AddNumber(i, area);
                } else {
                    Log.Error(func, $"failed: modified template for class {i}, sample {j}");
                }
            }
        }

        recog.pixaa_u.Truncate();
        recog.pixaa.Truncate();
        recog.ptaa_u.Truncate();
        recog.ptaa.Truncate();
        recog.naasum_u.Truncate();
        recog.naasum.Truncate();

        recog.train_done = true;
        return 0;
    }

    // leptonica: recogTemplatesAreOK (recogtrain.c)
    private static void TemplatesAreOK(Recog recog, int minsize, float minfract, out bool pok)
    {
        minsize = minsize < 0 ? DefaultMinSetSize : minsize;
        minfract = minfract < 0.0f ? DefaultMinSetFract : minfract;
        int n = recog.pixaa_u.GetCount(out Numa na);
        int validsets = 0;
        for (int i = 0; i < n; i++) {
            int nt = na.GetIValue(i);
            if (nt >= minsize)
                validsets++;
        }
        float ratio = recog.charset_size > 0
            ? (float)validsets / (float)recog.charset_size
            : 1.0f;
        pok = ratio >= minfract;
    }

    // leptonica: recogFilterPixaBySize (recogtrain.c)
    /// <summary>Filters labeled templates by size, keeping the most common and larger templates of each class.</summary>
    /// <param name="pixas">The labeled templates.</param>
    /// <param name="setsize">Size of the character set, that is, the number of classes.</param>
    /// <param name="maxkeep">Maximum number of templates to keep in a class.</param>
    /// <param name="maxHtRatio">Maximum allowed height ratio.</param>
    /// <param name="pna">Receives the number of templates in each class after filtering, for debugging.</param>
    /// <returns>The filtered templates.</returns>
    /// <exception cref="ArgumentException"><paramref name="pixas"/> is <c>null</c>.</exception>
    /// <remarks>
    /// <para>The basic assumption is that the most common and larger templates in each class are more likely to represent
    /// the characters of interest. For example, larger digits are more likely to represent page numbers, and smaller
    /// digits could be data in tables. The first stage of filtering is therefore biased toward the larger characters by
    /// removing very small ones, and selecting based on proximity of the remaining characters to the median height.</para>
    /// <para>For each class, the templates are ordered by increasing height and the height at rank 0.9 is taken.
    /// All templates that are shorter by more than <paramref name="maxHtRatio"/> are eliminated. Of the remaining ones,
    /// up to <paramref name="maxkeep"/> are selected that are closest in rank-order height to the median template.</para>
    /// </remarks>
    public static Pixa FilterPixaBySize(Pixa pixas, int setsize, int maxkeep,
                                        float maxHtRatio, out Numa pna)
    {
        const string func = "recogFilterPixaBySize";
        if (pixas == null)
            throw Lept.Arg(func, "pixas not defined");

        Pixaa paa = SortPixaByClass(pixas, setsize);
        int nc = paa.Count;
        pna = Numa.Create(0);
        Pixa pixa5 = Pixa.Create(0);
        for (int i = 0; i < nc; i++) {
            Pixa pixa1 = paa.GetPixa(i, L.L_CLONE);
            int n = pixa1.Count;
            if (n == 0)
                continue;
            Pixa pixa2 = pixa1.Sort(L.L_SORT_BY_HEIGHT, L.L_SORT_INCREASING, out _, L.L_COPY);
            int j90 = (int)(0.9 * n);
            pixa2.GetPixDimensions(j90, out _, out int h90, out _);
            Pixa pixa3 = Pixa.Create(n);
            for (int j = 0; j < n; j++) {
                pixa2.GetPixDimensions(j, out _, out int hj, out _);
                float ratio = (float)h90 / (float)hj;
                if (ratio <= maxHtRatio)
                    pixa3.AddPix(pixa2.GetPix(j, L.L_COPY), L.L_INSERT);
            }
            n = pixa3.Count;
            Pixa pixa4;
            if (n <= maxkeep) {
                pixa4 = pixa3.Copy(L.L_CLONE);
            } else {
                int j1 = (n - maxkeep) / 2;
                int j2 = j1 + maxkeep - 1;
                pixa4 = pixa3.SelectRange(j1, j2, L.L_CLONE);
            }
            pna.AddNumber(pixa4.Count);
            pixa5.Join(pixa4, 0, -1);
        }

        return pixa5;
    }

    // leptonica: recogSortPixaByClass (recogtrain.c)
    /// <summary>Sorts labeled templates into one array per class.</summary>
    /// <param name="pixa">The labeled templates.</param>
    /// <param name="setsize">Size of the character set, that is, the number of classes.</param>
    /// <returns>An array of image arrays, where each one holds the templates of one class.</returns>
    public static Pixaa SortPixaByClass(Pixa pixa, int setsize)
    {
        const string func = "recogSortPixaByClass";
        if (pixa == null)
            throw Lept.Arg(func, "pixa not defined");

        Recog recog = CreateFromPixaNoFinish(pixa, 0, 0, 0, 0, 0);
        return recog.pixaa_u;
    }

    // leptonica: recogRemoveOutliers1 (recogtrain.c)
    /// <summary>Removes outlier templates from a recognizer, using the default parameters of the recognizer.</summary>
    /// <param name="precog">A recognizer with unscaled labeled templates; replaced by the new recognizer on success.</param>
    /// <param name="minscore">Keep everything with at least this score.</param>
    /// <param name="mintarget">Minimum desired number of templates to retain, if possible.</param>
    /// <param name="minsize">Minimum number of samples required for a class.</param>
    /// <remarks>
    /// This is a convenience wrapper. See <see cref="Pixa.RemoveOutliers1(float, int, int)"/> for details.
    /// If this succeeds, the new recognizer replaces the input one; if it fails, the input recognizer is discarded.
    /// </remarks>
    public static void RemoveOutliers1(ref Recog? precog, float minscore,
                                       int mintarget, int minsize)
    {
        RemoveOutliers1Core(ref precog, minscore, mintarget, minsize,
                            false, out _, out _);
    }

    /// <summary>Removes outlier templates from a recognizer, and also returns debug images of the kept and removed templates.</summary>
    /// <param name="precog">A recognizer with unscaled labeled templates; replaced by the new recognizer on success.</param>
    /// <param name="minscore">Keep everything with at least this score.</param>
    /// <param name="mintarget">Minimum desired number of templates to retain, if possible.</param>
    /// <param name="minsize">Minimum number of samples required for a class.</param>
    /// <param name="ppixsave">Receives an image of the saved templates, with scores.</param>
    /// <param name="ppixrem">Receives an image of the removed templates, with scores.</param>
    public static void RemoveOutliers1(ref Recog? precog, float minscore,
                                       int mintarget, int minsize,
                                       out Pix? ppixsave, out Pix? ppixrem)
    {
        RemoveOutliers1Core(ref precog, minscore, mintarget, minsize,
                            true, out ppixsave, out ppixrem);
    }

    private static void RemoveOutliers1Core(ref Recog? precog, float minscore,
                                            int mintarget, int minsize, bool wantdb,
                                            out Pix? ppixsave, out Pix? ppixrem)
    {
        const string func = "recogRemoveOutliers1";
        ppixsave = null;
        ppixrem = null;
        if (precog == null)
            throw Lept.Arg(func, "recog not defined");

        Pixa pixa1 = precog.ExtractPixa();
        precog = null;

        Pixa pixa2 = wantdb
            ? pixa1.RemoveOutliers1(minscore, mintarget, minsize, out ppixsave, out ppixrem)
            : pixa1.RemoveOutliers1(minscore, mintarget, minsize);
        precog = CreateFromPixa(pixa2, 0, 0, 0, 150, 1);
    }

    // leptonica: recogRemoveOutliers2 (recogtrain.c)
    /// <summary>Removes outlier templates from a recognizer by comparing each to the class average, using the default parameters of the recognizer.</summary>
    /// <param name="precog">A recognizer with unscaled labeled templates; replaced by the new recognizer on success.</param>
    /// <param name="minscore">Keep everything with at least this score.</param>
    /// <param name="minsize">Minimum number of samples required for a class.</param>
    /// <remarks>
    /// This is a convenience wrapper. See <see cref="Pixa.RemoveOutliers2(float, int)"/> for details.
    /// If this succeeds, the new recognizer replaces the input one; if it fails, the input recognizer is discarded.
    /// </remarks>
    public static void RemoveOutliers2(ref Recog? precog, float minscore, int minsize)
    {
        RemoveOutliers2Core(ref precog, minscore, minsize, false, out _, out _);
    }

    /// <summary>Removes outlier templates from a recognizer, and also returns debug images of the kept and removed templates.</summary>
    /// <param name="precog">A recognizer with unscaled labeled templates; replaced by the new recognizer on success.</param>
    /// <param name="minscore">Keep everything with at least this score.</param>
    /// <param name="minsize">Minimum number of samples required for a class.</param>
    /// <param name="ppixsave">Receives an image of the saved templates, with scores.</param>
    /// <param name="ppixrem">Receives an image of the removed templates, with scores.</param>
    public static void RemoveOutliers2(ref Recog? precog, float minscore, int minsize,
                                       out Pix? ppixsave, out Pix? ppixrem)
    {
        RemoveOutliers2Core(ref precog, minscore, minsize, true, out ppixsave, out ppixrem);
    }

    private static void RemoveOutliers2Core(ref Recog? precog, float minscore,
                                            int minsize, bool wantdb,
                                            out Pix? ppixsave, out Pix? ppixrem)
    {
        const string func = "recogRemoveOutliers2";
        ppixsave = null;
        ppixrem = null;
        if (precog == null)
            throw Lept.Arg(func, "recog not defined");

        Pixa pixa1 = precog.ExtractPixa();
        precog = null;

        Pixa pixa2 = wantdb
            ? pixa1.RemoveOutliers2(minscore, minsize, out ppixsave, out ppixrem)
            : pixa1.RemoveOutliers2(minscore, minsize);
        precog = CreateFromPixa(pixa2, 0, 0, 0, 150, 1);
    }

    /*------------------------------------------------------------------------*
     *                       Training on unlabeled data                       *
     *------------------------------------------------------------------------*/
    // leptonica: recogTrainFromBoot (recogtrain.c)
    /// <summary>Labels a set of unlabeled characters by matching them against this bootstrap recognizer.</summary>
    /// <param name="pixas">A set of unlabeled, unscaled single-character images.</param>
    /// <param name="minscore">Minimum score for accepting an example, for example 0.75.</param>
    /// <param name="threshold">Threshold for binarization, if needed.</param>
    /// <param name="debug">1 to save debug output to this recognizer; 0 otherwise.</param>
    /// <returns>The labeled versions of the input images that matched well enough.</returns>
    /// <remarks>
    /// <para>This takes unscaled single characters and this bootstrap recognizer, which has been set up with a scaled
    /// height and the width of normalized strokes. It modifies the input images accordingly and correlates them with the
    /// templates. It returns those input images whose best correlation with the bootstrap recognizer is at or above
    /// <paramref name="minscore"/>. The returned images have text labels for the class to which the best correlated
    /// template belongs.</para>
    /// <para>Identification occurs in scaled mode (typically with a height of 40), optionally using width-normalized
    /// line images derived from the input images.</para>
    /// </remarks>
    public Pixa TrainFromBoot(Pixa pixas, float minscore, int threshold, int debug)
    {
        const string func = "recogTrainFromBoot";
        if (pixas == null)
            throw Lept.Arg(func, "pixas not defined");

        int n = pixas.Count;
        if (n == 0)
            throw Lept.Arg(func, "no pix in pixa");
        pixas.VerifyDepth(out _, out int maxd);
        Pixa pixa1;
        if (maxd == 1) {
            pixa1 = pixas.Copy(L.L_COPY);
        } else {
            pixa1 = Pixa.Create(n);
            for (int i = 0; i < n; i++) {
                Pix pix1 = pixas.GetPix(i, L.L_CLONE);
                Pix pix2 = pix1.ConvertTo1(threshold);
                pixa1.AddPix(pix2, L.L_INSERT);
            }
        }

        int sh = scaleh;
        int lw = linew;
        Pixa pixa2 = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixa1.GetPix(i, L.L_CLONE);
            Pix pix2 = sh > 0 ? pix1.ScaleToSize(0, sh) : Pix.Copy(null, pix1);
            pixa2.AddPix(pix2, L.L_INSERT);
        }

        Pixa pixa3 = lw > 0 ? pixa2.SetStrokeWidth(lw, 4, 8) : pixa2.Copy(L.L_CLONE);

        n = pixa3.Count;
        Pixa pixad = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixa3.GetPix(i, L.L_COPY);
            pix1.Text = null;
            Pix? pixdb = null;
            if (debug == 0) {
                IdentifyPix(pix1);
            } else {
                IdentifyPix(pix1, out pixdb);
                if (pixdb != null)
                    pixadb_boot.AddPix(pixdb, L.L_INSERT);
            }
            rch!.Extract(out _, out float score, out string? text,
                         out _, out _, out _, out _);
            if (score >= minscore) {
                Pix pix2 = pixas.GetPix(i, L.L_COPY);
                pix2.Text = text;
                pixad.AddPix(pix2, L.L_INSERT);
                if (pixdb != null)
                    pixadb_boot.AddPix(pixdb, L.L_COPY);
            }
        }

        return pixad;
    }

    /*------------------------------------------------------------------------*
     *                     Padding the digit training set                     *
     *------------------------------------------------------------------------*/
    // leptonica: recogPadDigitTrainingSet (recogtrain.c)
    /// <summary>Pads a trained digit recognizer with templates from a boot recognizer, if padding is needed.</summary>
    /// <param name="precog">A trained recognizer; if padding is needed, it is replaced by a new padded recognizer.</param>
    /// <param name="scaleh">Height to which the templates are scaled; must be positive, and about 40 is suggested.</param>
    /// <param name="linew">Width of the normalized strokes; 0 for the original scanned images.</param>
    /// <remarks>
    /// This does nothing if padding is not needed. Otherwise it replaces the input recognizer with a new one, padded
    /// appropriately with templates from a boot recognizer and set up with correlation templates derived from
    /// <paramref name="scaleh"/> and <paramref name="linew"/>.
    /// </remarks>
    public static void PadDigitTrainingSet(ref Recog? precog, int scaleh, int linew)
    {
        const string func = "recogPadDigitTrainingSet";
        if (precog == null)
            throw Lept.Arg(func, "recog not defined");
        Recog recog1 = precog;

        recog1.IsPaddingNeeded(out Sarray? sa);
        if (sa == null || sa.Count == 0)
            return;

        Pixa pixa = recog1.AddDigitPadTemplates(sa);
        if (scaleh <= 0) {
            Log.Warning(func, "templates must be scaled to fixed height; using 40");
            scaleh = 40;
        }

        precog = CreateFromPixa(pixa, 0, scaleh, linew, recog1.threshold, recog1.maxyshift);
    }

    // leptonica: recogIsPaddingNeeded (recogtrain.c)
    /// <summary>Determines which character classes need additional padding templates.</summary>
    /// <param name="psa">Receives the character values for which extra templates are needed, or <c>null</c> if no padding is needed.</param>
    /// <remarks>
    /// The returned string array is used by <see cref="AddDigitPadTemplates"/>.
    /// </remarks>
    public void IsPaddingNeeded(out Sarray? psa)
    {
        psa = null;
        int nclass = pixaa_u.GetCount(out Numa naclass);
        bool allclasses = nclass == charset_size;

        naclass.GetMin(out float minval, out _);
        if (allclasses && minval >= min_nopad)
            return;

        Sarray sa = AddMissingClassStrings();
        psa = sa;

        for (int i = 0; i < nclass; i++) {
            int nt = naclass.GetIValue(i);
            if (nt < min_nopad) {
                string str = sa_text.GetString(i, L.L_COPY);
                sa.AddString(str, L.L_INSERT);
            }
        }
    }

    // leptonica: recogAddMissingClassStrings (recogtrain.c)
    private Sarray AddMissingClassStrings()
    {
        int nclass = pixaa_u.Count;
        if (charset_type != 1 || nclass == 10)
            return Sarray.Create(0);

        Numa na = Numa.Create(0);
        Sarray sa = Sarray.Create(0);
        for (int i = 0; i < charset_size; i++)
            na.AddNumber(1);
        for (int i = 0; i < nclass; i++) {
            string text = sa_text.GetString(i, L.L_NOCOPY);
            int index = text[0] - '0';
            if (index >= 0 && index < na.Count)
                na.SetValue(index, 0);
        }

        // NOTE(upstream): loops i < nclass instead of i < charset_size.
        for (int i = 0; i < nclass; i++) {
            int ival = na.GetIValue(i);
            if (ival == 1)
                sa.AddString(((char)('0' + i)).ToString(), L.L_COPY);
        }
        return sa;
    }

    // leptonica: recogAddDigitPadTemplates (recogtrain.c)
    /// <summary>Collects all the templates of this recognizer together with additional pad templates from a boot recognizer.</summary>
    /// <param name="sa">The text strings of the classes that need to be padded.</param>
    /// <returns>An array of all templates from this recognizer and the additional pad templates.</returns>
    /// <remarks>Call <see cref="IsPaddingNeeded"/> first, which returns the strings of the classes where more templates are needed.</remarks>
    public Pixa AddDigitPadTemplates(Sarray sa)
    {
        const string func = "recogAddDigitPadTemplates";
        if (sa == null)
            throw Lept.Arg(func, "sa not defined");
        if (!CharsetAvailable(charset_type))
            throw Lept.Fail(func, "boot charset not available");

        Pixa pixa1 = MakeBootDigitTemplates(0, 0);
        int n = pixa1.Count;

        Pixa pixa2 = ExtractPixa();
        int nt = sa.Count;
        for (int i = 0; i < n; i++) {
            Pix pix = pixa1.GetPix(i, L.L_CLONE);
            string? text = pix.Text;
            for (int j = 0; j < nt; j++) {
                string str = sa.GetString(j, L.L_NOCOPY);
                if (text == str) {
                    pixa2.AddPix(pix, L.L_COPY);
                    break;
                }
            }
        }

        return pixa2;
    }

    // leptonica: recogCharsetAvailable (recogtrain.c)
    private static bool CharsetAvailable(int type)
    {
        const string func = "recogCharsetAvailable";
        switch (type) {
        case L.L_ARABIC_NUMERALS:
            return true;
        case L.L_LC_ROMAN_NUMERALS:
        case L.L_UC_ROMAN_NUMERALS:
        case L.L_LC_ALPHA:
        case L.L_UC_ALPHA:
            Log.Info(func, $"charset type {type} not available");
            return false;
        default:
            Log.Info(func, $"charset type {type} is unknown");
            return false;
        }
    }

    /*------------------------------------------------------------------------*
     *                      Making a boot digit recognizer                    *
     *------------------------------------------------------------------------*/
    // leptonica: recogMakeBootDigitRecog (recogtrain.c)
    /// <summary>Generates a bootstrap digit recognizer from pre-computed, labeled sets of single digits.</summary>
    /// <param name="nsamp">Number of samples of each digit; 0 to use the built-in sets.</param>
    /// <param name="scaleh">Height to which all templates are scaled; typically 40.</param>
    /// <param name="linew">Normalized line width; typically 5; 0 to skip.</param>
    /// <param name="maxyshift">Maximum vertical shift from the nominal centroid alignment; typically 0 or 1.</param>
    /// <param name="debug">1 to show the templates; 0 otherwise.</param>
    /// <returns>The bootstrap recognizer.</returns>
    /// <remarks>
    /// <para>The templates used in the recognizer can be modified by scaling (isotropically to a fixed height) and by
    /// generating a skeleton and thickening it so that all strokes have the same width.</para>
    /// <para>The resulting templates are scaled versions of either the input bitmaps or images with fixed line widths.
    /// To use the input bitmaps, set <paramref name="linew"/> to 0; otherwise set it to the desired line width.</para>
    /// <para>If <paramref name="nsamp"/> is 0, this uses and extends the output from the first three boot generators.
    /// Otherwise it uses exactly <paramref name="nsamp"/> templates of each digit, extracted from the fourth generator.</para>
    /// </remarks>
    public static Recog MakeBootDigitRecog(int nsamp, int scaleh, int linew,
                                           int maxyshift, int debug)
    {
        Pixa pixa = MakeBootDigitTemplates(nsamp, debug);
        Recog recog = CreateFromPixa(pixa, 0, scaleh, linew, 128, maxyshift);
        if (debug != 0) {
            using Stream stderr = Console.OpenStandardError();
            recog.ShowContent(stderr, 0, 1);
        }
        return recog;
    }

    // leptonica: recogMakeBootDigitTemplates (recogtrain.c)
    /// <summary>Generates the labeled digit templates used by <see cref="MakeBootDigitRecog"/>.</summary>
    /// <param name="nsamp">Number of samples of each digit; 0 to use the built-in sets.</param>
    /// <param name="debug">1 to display the templates.</param>
    /// <returns>An array of the labeled templates.</returns>
    public static Pixa MakeBootDigitTemplates(int nsamp, int debug)
    {
        if (nsamp > 0) {
            Pixa pixaGen4 = BootNumGen.Gen4(nsamp);
            if (debug != 0) {
                Pix pix1 = pixaGen4.DisplayTiledWithText(1500, 1.0f, 10, 2, 6, 0xff000000u);
                pix1.Display(0, 0);
            }
            return pixaGen4;
        }

        Pixa pixa1 = BootNumGen.Gen1();
        Pixa pixa2 = BootNumGen.Gen2();
        Pixa pixa3 = BootNumGen.Gen3();
        if (debug != 0) {
            Pix pix1 = pixa1.DisplayTiledWithText(1500, 1.0f, 10, 2, 6, 0xff000000u);
            Pix pix2 = pixa2.DisplayTiledWithText(1500, 1.0f, 10, 2, 6, 0xff000000u);
            Pix pix3 = pixa3.DisplayTiledWithText(1500, 1.0f, 10, 2, 6, 0xff000000u);
            pix1.Display(0, 0);
            pix2.Display(600, 0);
            pix3.Display(1200, 0);
        }
        pixa1.Join(pixa2, 0, -1);
        pixa1.Join(pixa3, 0, -1);

        Numa na1 = Numa.Create(4);
        na1.AddNumber(0.9f);
        na1.AddNumber(1.1f);
        na1.AddNumber(1.2f);
        return pixa1.ExtendByScaling(na1, L.L_HORIZ, 1);
    }

    /*------------------------------------------------------------------------*
     *                               Debugging                                *
     *------------------------------------------------------------------------*/
    // leptonica: recogShowContent (recogtrain.c)
    /// <summary>Writes a description of the recognizer content to a stream, and optionally displays the template images.</summary>
    /// <param name="fp">The stream to write to.</param>
    /// <param name="index">Index used to name the output files of the template images.</param>
    /// <param name="display">1 to show the template images; 0 otherwise.</param>
    public void ShowContent(Stream fp, int index, int display)
    {
        const string func = "recogShowContent";
        if (fp == null)
            throw Lept.Arg(func, "stream not defined");

        CStdio.Fprintf(fp, $"Debug print of recog contents\n");
        CStdio.Fprintf(fp, $"  Setsize: {setsize}\n");
        CStdio.Fprintf(fp, $"  Binarization threshold: {threshold}\n");
        CStdio.Fprintf(fp, $"  Maximum matching y-jiggle: {maxyshift}\n");
        if (linew <= 0)
            CStdio.Fprintf(fp, $"  Using image templates for matching\n");
        else
            CStdio.Fprintf(fp, $"  Using templates with fixed line width for matching\n");
        if (scalew == 0)
            CStdio.Fprintf(fp, $"  No width scaling of templates\n");
        else
            CStdio.Fprintf(fp, $"  Template width scaled to {scalew}\n");
        if (scaleh == 0)
            CStdio.Fprintf(fp, $"  No height scaling of templates\n");
        else
            CStdio.Fprintf(fp, $"  Template height scaled to {scaleh}\n");
        CStdio.Fprintf(fp, $"  Number of samples in each class:\n");
        pixaa_u.GetCount(out Numa na);
        for (int i = 0; i < setsize; i++) {
            int val = dna_tochar.GetIValue(i);
            int count = na.GetIValue(i);
            if (val < 128)
                CStdio.Fprintf(fp, $"    class {i}, char {(char)val}:   {count}\n");
            else
                CStdio.Fprintf(fp, $"    class {i}, val {val}:   {count}\n");
        }

        if (display != 0) {
            Utils.LeptMkdir("lept/recog");
            Pix pix = pixaa_u.DisplayByPixa(50, 1.0f, 20, 20, 0);
            pix.WriteDebug($"/tmp/lept/recog/templates_u.{index}.png", L.IFF_PNG);
            pix.Display(0, 200 * index);
            if (train_done && pixaa != null) {
                pix = pixaa.DisplayByPixa(50, 1.0f, 20, 20, 0);
                pix.WriteDebug($"/tmp/lept/recog/templates.{index}.png", L.IFF_PNG);
                pix.Display(800, 200 * index);
            }
        }
    }

    // leptonica: recogDebugAverages (recogtrain.c)
    /// <summary>Generates debug output that pairs each training image with the average template it correlates with best.</summary>
    /// <param name="debug">0 for no output; 1 for images; 2 for text; 3 for both.</param>
    /// <remarks>
    /// <para>The image that pairs each of the input images used in training with its best-correlated average template is
    /// stored in the recognizer.</para>
    /// <para>It also generates an array of all the input training images, which can be used, for example, with
    /// <see cref="ShowMatchesInRange"/>.</para>
    /// <para>This fails if the averaging finds bad classes.</para>
    /// </remarks>
    public void DebugAverages(int debug)
    {
        const string func = "recogDebugAverages";
        if (AverageSamples(0) != 0)
            throw Lept.Fail(func, "averaging failed");

        Pixaa paa1 = pixaa!;
        pixa_tr ??= paa1.FlattenToPixa(out _, L.L_CLONE);

        int n = paa1.Count;
        Pixaa paa2 = Pixaa.Create(n);
        for (int i = 0; i < n; i++) {
            Pixa pixaOut = Pixa.Create(0);
            Pixa pixat = paa1.GetPixa(i, L.L_CLONE);
            int np = pixat.Count;
            for (int j = 0; j < np; j++) {
                Pix pix1 = paa1.GetPix(i, j, L.L_CLONE);
                IdentifyPix(pix1, out Pix? pix2);
                rch!.Extract(out int index, out float score, out _, out _, out _, out _, out _);
                if (debug >= 2)
                    Log.Info(func, $"index = {index}, score = {score:F3}");
                Pix pix3 = pix2!.AddBorder(2, 1);
                pixaOut.AddPix(pix3, L.L_INSERT);
            }
            paa2.AddPixa(pixaOut, L.L_INSERT);
        }
        pixdb_ave = paa2.DisplayByPixa(50, 1.0f, 20, 20, 0);
        if (debug % 2 != 0) {
            Utils.LeptMkdir("lept/recog");
            pixdb_ave.WriteDebug("/tmp/lept/recog/templ_match.png", L.IFF_PNG);
            pixdb_ave.Display(100, 100);
        }
    }

    // leptonica: recogShowAverageTemplates (recogtrain.c)
    /// <summary>Generates a display of the averaged templates, both scaled and unscaled, with the centroid shown in red.</summary>
    /// <remarks>This is a debugging routine.</remarks>
    public void ShowAverageTemplates()
    {
        Pix pixr = Pix.Create(3, 3, 32);
        pixr.SetAllArbitrary(0xff000000u);
        Pixa pixadb = Pixa.Create(2);

        int size = setsize;
        Pixa pixat = Pixa.Create(size);
        for (int i = 0; i < size; i++) {
            Pix pix1 = pixa_u!.GetPix(i, L.L_CLONE);
            Pix pix2 = pix1.ConvertTo32();
            pta_u!.GetPt(i, out float x, out float y);
            pix2.Rasterop((int)(x - 0.5), (int)(y - 0.5), 3, 3, L.PIX_SRC, pixr, 0, 0);
            pixat.AddPix(pix2, L.L_INSERT);
        }
        Pix tiledU = pixat.DisplayTiledInRows(32, 3000, 1.0f, 0, 20, 0);
        pixadb.AddPix(tiledU, L.L_INSERT);
        tiledU.Display(100, 100);

        pixat = Pixa.Create(size);
        for (int i = 0; i < size; i++) {
            Pix pix1 = pixa!.GetPix(i, L.L_CLONE);
            Pix pix2 = pix1.ConvertTo32();
            pta!.GetPt(i, out float x, out float y);
            pix2.Rasterop((int)(x - 0.5), (int)(y - 0.5), 3, 3, L.PIX_SRC, pixr, 0, 0);
            pixat.AddPix(pix2, L.L_INSERT);
        }
        Pix tiledS = pixat.DisplayTiledInRows(32, 3000, 1.0f, 0, 20, 0);
        pixadb.AddPix(tiledS, L.L_INSERT);
        tiledS.Display(100, 100);
        pixadb_ave = pixadb;
    }

    // leptonica: pixDisplayOutliers (recogtrain.c)
    internal static Pix DisplayOutliers(Pixa pixas, Numa nas)
    {
        const string func = "pixDisplayOutliers";
        if (pixas == null)
            throw Lept.Arg(func, "pixas not defined");
        if (nas == null)
            throw Lept.Arg(func, "nas not defined");
        int n = pixas.Count;
        if (nas.Count != n)
            throw Lept.Arg(func, "pixas and nas sizes differ");

        Pixa pixa1 = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_CLONE);
            Pix pix2 = pix1.AddBlackOrWhiteBorder(25, 25, 0, 0, L.L_GET_WHITE_VAL);
            string? text = pix1.Text;
            float fval = nas.GetFValue(i);
            pix2.Text = string.Format(CultureInfo.InvariantCulture, "'{0}': {1,5:F2}", text, fval);
            pixa1.AddPix(pix2, L.L_INSERT);
        }
        return pixa1.DisplayTiledWithText(1500, 1.0f, 20, 2, 6, 0xff000000u);
    }

    // leptonica: recogDisplayOutlier (recogtrain.c)
    internal Pix DisplayOutlier(int iclass, int jsamp, int maxclass, float maxscore)
    {
        Pix pix1 = pixaa!.GetPix(iclass, jsamp, L.L_CLONE);
        Pix pix2 = pixa!.GetPix(iclass, L.L_CLONE);
        Pix pix3 = pixa.GetPix(maxclass, L.L_CLONE);
        Pixa pixa3 = Pixa.Create(3);
        pixa3.AddPix(pix1, L.L_INSERT);
        pixa3.AddPix(pix2, L.L_INSERT);
        pixa3.AddPix(pix3, L.L_INSERT);
        Pix pix4 = pixa3.DisplayTiledInRows(32, 400, 2.0f, 0, 12, 2);
        string buf = string.Format(CultureInfo.InvariantCulture,
                                   "C={0}, BAC={1}, S={2:F2}", iclass, maxclass, maxscore);
        return pix4.AddSingleTextblock(bmf, buf, 0xff000000u, L.L_ADD_BELOW, out _);
    }

    // leptonica: recogShowMatchesInRange (recogtrain.c)
    /// <summary>Gives a visual output of the best matches for images whose scores are in a given range.</summary>
    /// <param name="pixa">The 1 bpp images to match.</param>
    /// <param name="minscore">Minimum score to include in the output.</param>
    /// <param name="maxscore">Maximum score to include in the output.</param>
    /// <param name="display">1 to display the result.</param>
    /// <remarks>
    /// Each pair of images can optionally be labeled with the index of the best match and the correlation.
    /// To use this, save a set of 1 bpp images (labeled or unlabeled) that can be given to a recognizer,
    /// then call this method with the set and the parameters that filter a range of scores.
    /// </remarks>
    public void ShowMatchesInRange(Pixa pixa, float minscore, float maxscore, int display)
    {
        const string func = "recogShowMatchesInRange";
        if (pixa == null)
            throw Lept.Arg(func, "pixa not defined");

        int n = pixa.Count;
        Numa nascore = Numa.Create(n);
        Numa naindex = Numa.Create(n);
        Pixa pixa1 = Pixa.Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixa.GetPix(i, L.L_CLONE);
            IdentifyPix(pix1, out Pix? pix2);
            rch!.Extract(out int index, out float score, out _, out _, out _, out _, out _);
            nascore.AddNumber(score);
            naindex.AddNumber(index);
            pixa1.AddPix(pix2!, L.L_INSERT);
        }

        Pixa pixa2 = Pixa.Create(n);
        int depth = 1;
        for (int i = 0; i < n; i++) {
            float score = nascore.GetFValue(i);
            if (score < minscore || score > maxscore) continue;
            Pix pix1 = pixa1.GetPix(i, L.L_CLONE);
            int index = naindex.GetIValue(i);
            Pix pix2 = ShowMatch(pix1, null, null, index, score);
            if (pixa2.Count == 0) depth = pix2.Depth;
            pixa2.AddPix(pix2, L.L_INSERT);
        }

        pixdb_range = null;
        if (pixa2.Count > 0) {
            pixdb_range = pixa2.DisplayTiledInRows(depth, 2500, 1.0f, 0, 20, 1);
            if (display != 0)
                pixdb_range.Display(300, 100);
        } else {
            Log.Info(func, "no character matches in the range of scores");
        }
    }

    // leptonica: recogShowMatch (recogtrain.c)
    /// <summary>Renders an input image next to its best-matching template, optionally with matching information.</summary>
    /// <param name="pix1">The input image: either the input alone (a single character, or several characters to be segmented), or both the input and the matching template.</param>
    /// <param name="pix2">Optional matching template.</param>
    /// <param name="box">Optional region in <paramref name="pix1"/> for which <paramref name="pix2"/> matches; if given, the region is outlined.</param>
    /// <param name="index">Index of the matching template; -1 disables printing.</param>
    /// <param name="score">Score of the match.</param>
    /// <returns>A pair of images showing the input and the best template.</returns>
    /// <remarks>
    /// If the bitmap font has been made and <paramref name="index"/> is not negative, the text label, match score and index
    /// are rendered; otherwise their values are ignored.
    /// </remarks>
    public Pix ShowMatch(Pix pix1, Pix? pix2, Box? box, int index, float score)
    {
        const string func = "recogShowMatch";
        if (pix1 == null)
            throw Lept.Arg(func, "pix1 not defined");

        L_Bmf? useBmf = (bmf != null && index >= 0) ? bmf : null;
        if (pix2 == null && box == null && useBmf == null)
            return Pix.Copy(null, pix1);

        Pix pix3 = pix1.ConvertTo32();
        if (box != null)
            pix3.RenderBoxArb(box, 1, 255, 0, 0);

        Pix pix4;
        if (pix2 != null) {
            Pixa pa = Pixa.Create(2);
            pa.AddPix(pix3, L.L_CLONE);
            pa.AddPix(pix2, L.L_CLONE);
            pix4 = pa.DisplayTiledInRows(1, 500, 1.0f, 0, 15, 0);
        } else {
            pix4 = Pix.Copy(null, pix3);
        }

        if (useBmf != null) {
            Pix pix5 = pix4.AddBorderGeneral(55, 55, 0, 0, 0xffffff00u);
            string text = GetClassString(index);
            string buf = string.Format(CultureInfo.InvariantCulture,
                                       "C={0}, S={1:F3}, I={2}", text, score, index);
            return pix5.AddSingleTextblock(useBmf, buf, 0xff000000u, L.L_ADD_BELOW, out _);
        }

        return Pix.Clone(pix4);
    }
}

public sealed partial class Pixa
{
    private const float OutliersDefaultMinScore = 0.75f;
    private const int OutliersDefaultMinTarget = 3;
    private const float OutliersLowerScoreThreshold = 0.5f;
    private const int OutliersDefaultMinSetSize = 1;

    // leptonica: pixaAccumulateSamples (recogtrain.c)
    /// <summary>Generates an aligned (by centroid) sum of the 1 bpp samples of one class.</summary>
    /// <param name="pta">Optional centroids of the samples; computed if <c>null</c>.</param>
    /// <param name="ppixd">Receives the accumulated samples, as an 8 bpp image.</param>
    /// <param name="px">Receives the average x coordinate of the centroids.</param>
    /// <param name="py">Receives the average y coordinate of the centroids.</param>
    /// <remarks>
    /// <para>Only the first 256 samples are used.</para>
    /// <para>If the centroids are not supplied, two tables are generated and discarded after use.
    /// If this is called many times, it is better to precompute the centroids.</para>
    /// </remarks>
    public void AccumulateSamples(Pta? pta, out Pix ppixd, out float px, out float py)
    {
        const string func = "pixaAccumulateSamples";
        px = 0.0f;
        py = 0.0f;
        int count = Count;
        if (pta != null && pta.Count != count)
            throw Lept.Arg(func, "pta count differs from pixa count");
        count = Math.Min(count, 256);
        if (count == 0)
            throw Lept.Arg(func, "pixa array empty");

        Pta ptac;
        if (pta != null) {
            ptac = pta.Clone();
        } else {
            ptac = Pta.Create(count);
            int[] centtab = Pix.MakePixelCentroidTab8();
            int[] sumtab = Pix.MakePixelSumTab8();
            for (int i = 0; i < count; i++) {
                Pix pixSample = GetPix(i, L.L_CLONE);
                pixSample.Centroid(centtab, sumtab, out float xc, out float yc);
                ptac.AddPt(xc, yc);
            }
        }

        // NOTE(upstream): C passes pta instead of ptac to ptaGetPt; ptac == pta when pta != null.
        float xave = 0.0f, yave = 0.0f;
        for (int i = 0; i < count; i++) {
            ptac.GetPt(i, out float xc, out float yc);
            xave += xc;
            yave += yc;
        }
        xave /= (float)count;
        yave /= (float)count;
        px = xave;
        py = yave;

        SizeRange(out _, out _, out int maxw, out int maxh);
        Pix pixsum = Pix.InitAccumulate(maxw + 5, maxh + 5, 0);
        Pix pix1 = Pix.Create(maxw, maxh, 1);
        for (int i = 0; i < count; i++) {
            Pix pix2 = GetPix(i, L.L_CLONE);
            ptac.GetPt(i, out float xc, out float yc);
            int xdiff = (int)(xave - xc);
            int ydiff = (int)(yave - yc);
            pix1.ClearAll();
            pix1.Rasterop(xdiff, ydiff, maxw, maxh, L.PIX_SRC, pix2, 0, 0);
            pixsum.Accumulate(pix1, L.L_ARITH_ADD);
        }
        ppixd = pixsum.FinalAccumulate(0, 8);
    }

    // leptonica: pixaRemoveOutliers1 (recogtrain.c)
    /// <summary>Removes outlier templates by matching each against a scaled recognizer built from the templates.</summary>
    /// <param name="minscore">Keep everything with at least this score; -1.0 for the default.</param>
    /// <param name="mintarget">Minimum desired number of templates to retain, if possible; -1 for the default.</param>
    /// <param name="minsize">Minimum number of samples required for a class; -1 for the default.</param>
    /// <returns>The unscaled templates to be kept.</returns>
    /// <remarks>
    /// <para>Removing outliers is particularly important when recognition goes against all the samples in the training
    /// set, as opposed to the averages for each class, because an identification error occurs if a mislabeled template is
    /// the best match for an input sample.</para>
    /// <para>Because the score values depend strongly on the quality of the character images, a minimum score for
    /// retention is supplemented with a score necessary to acquire the minimum target number of templates. A lower
    /// threshold is accepted for that purpose, so with poor quality templates samples with a score less than
    /// <paramref name="minscore"/> may be kept, but never below the lower threshold. If the number of samples in a class
    /// is less than <paramref name="minsize"/>, none are used.</para>
    /// <para>This is meant to be used on a book-adapted recognizer, where the templates all come from the same book;
    /// use <paramref name="minscore"/> of about 0.75.</para>
    /// <para>Method: make a scaled recognizer from the templates. Then, for each class, generate the averages, match each
    /// scaled template against the average, and save the unscaled templates that had a sufficiently good match.</para>
    /// </remarks>
    public Pixa RemoveOutliers1(float minscore, int mintarget, int minsize)
    {
        return RemoveOutliers1Core(minscore, mintarget, minsize, false, out _, out _);
    }

    /// <summary>Removes outlier templates, and also returns debug images of the kept and removed templates.</summary>
    /// <param name="minscore">Keep everything with at least this score; -1.0 for the default.</param>
    /// <param name="mintarget">Minimum desired number of templates to retain, if possible; -1 for the default.</param>
    /// <param name="minsize">Minimum number of samples required for a class; -1 for the default.</param>
    /// <param name="ppixsave">Receives an image of the saved templates, with scores.</param>
    /// <param name="ppixrem">Receives an image of the removed templates, with scores.</param>
    /// <returns>The unscaled templates to be kept.</returns>
    public Pixa RemoveOutliers1(float minscore, int mintarget, int minsize,
                                out Pix? ppixsave, out Pix? ppixrem)
    {
        return RemoveOutliers1Core(minscore, mintarget, minsize, true, out ppixsave, out ppixrem);
    }

    private Pixa RemoveOutliers1Core(float minscore, int mintarget, int minsize,
                                     bool wantdb, out Pix? ppixsave, out Pix? ppixrem)
    {
        const string func = "pixaRemoveOutliers1";
        ppixsave = null;
        ppixrem = null;
        minscore = Math.Min(minscore, 1.0f);
        if (minscore <= 0.0f)
            minscore = OutliersDefaultMinScore;
        mintarget = Math.Min(mintarget, 3);
        if (mintarget <= 0)
            mintarget = OutliersDefaultMinTarget;
        if (minsize < 0)
            minsize = OutliersDefaultMinSetSize;

        int debug = wantdb ? 1 : 0;
        Recog recog = Recog.CreateFromPixa(this, 0, 40, 0, 128, 1);
        if (recog.AverageSamples(debug) != 0)
            throw Lept.Fail(func, "bad templates");

        Numa? nasave = wantdb ? Numa.Create(0) : null;
        Pixa? pixarem = wantdb ? Create(0) : null;
        Numa? narem = wantdb ? Numa.Create(0) : null;

        Pixa pixad = Create(0);
        for (int i = 0; i < recog.setsize; i++) {
            Pix pix1 = recog.pixa!.GetPix(i, L.L_CLONE);
            recog.pta!.GetPt(i, out float x1, out float y1);
            int area1 = recog.nasum!.GetIValue(i);

            Pixa pixaClass = recog.pixaa!.GetPixa(i, L.L_CLONE);
            Pta ptaClass = recog.ptaa!.GetPta(i, L.L_CLONE);
            Numa nasumClass = recog.naasum!.GetNuma(i, L.L_CLONE);
            int classCount = pixaClass.Count;
            Numa nascore = Numa.Create(classCount);
            for (int j = 0; j < classCount; j++) {
                Pix pix2 = pixaClass.GetPix(j, L.L_CLONE);
                ptaClass.GetPt(j, out float x2, out float y2);
                int area2 = nasumClass.GetIValue(j);
                float score = pix1.CorrelationScoreSimple(pix2, area1, area2,
                                                          x1 - x2, y1 - y2, 5, 5,
                                                          recog.sumtab);
                nascore.AddNumber(score);
            }

            float minfract = classCount > 0 ? (float)mintarget / (float)classCount : 1.0f;
            float fract = 1.0f - minfract;
            float rankscore = (classCount > 0 && fract >= 0.0f && fract <= 1.0f)
                ? nascore.GetRankValue(fract, null, 0)
                : 0.0f;
            float threshscore = Math.Max(OutliersLowerScoreThreshold,
                                         Math.Min(minscore, rankscore));

            for (int j = 0; j < classCount; j++) {
                float score = nascore.GetFValue(j);
                Pix pixSample = recog.pixaa_u.GetPix(i, j, L.L_COPY);
                if (score >= threshscore && classCount >= minsize) {
                    pixad.AddPix(pixSample, L.L_INSERT);
                    nasave?.AddNumber(score);
                } else if (wantdb) {
                    pixarem!.AddPix(pixSample, L.L_INSERT);
                    narem!.AddNumber(score);
                }
            }
        }

        if (wantdb) {
            if (pixad.Count > 0)
                ppixsave = Recog.DisplayOutliers(pixad, nasave!);
            if (pixarem!.Count > 0)
                ppixrem = Recog.DisplayOutliers(pixarem, narem!);
        }
        return pixad;
    }

    // leptonica: pixaRemoveOutliers2 (recogtrain.c)
    /// <summary>Removes outlier templates by comparing each against the average template of every class.</summary>
    /// <param name="minscore">Keep everything with at least this score; -1.0 for the default.</param>
    /// <param name="minsize">Minimum number of samples required for a class; -1 for the default.</param>
    /// <returns>The unscaled templates to be kept.</returns>
    /// <remarks>
    /// <para>Removing outliers is particularly important when recognition goes against all the samples in the training
    /// set, as opposed to the averages for each class, because an identification error occurs if a mislabeled template is
    /// the best match for an input sample.</para>
    /// <para>This method compares each template against the average templates of each class, and discards any template
    /// that has a higher correlation to a class different from its own. It also sets a lower bound on the correlation
    /// score with its class average.</para>
    /// <para>This is meant to be used on a book-adapted recognizer, where the templates all come from the same book;
    /// use <paramref name="minscore"/> of about 0.75.</para>
    /// </remarks>
    public Pixa RemoveOutliers2(float minscore, int minsize)
    {
        return RemoveOutliers2Core(minscore, minsize, false, out _, out _);
    }

    /// <summary>Removes outlier templates, and also returns debug images of the kept and removed templates.</summary>
    /// <param name="minscore">Keep everything with at least this score; -1.0 for the default.</param>
    /// <param name="minsize">Minimum number of samples required for a class; -1 for the default.</param>
    /// <param name="ppixsave">Receives an image of the saved templates, with scores.</param>
    /// <param name="ppixrem">Receives an image of the removed templates, with scores.</param>
    /// <returns>The unscaled templates to be kept.</returns>
    public Pixa RemoveOutliers2(float minscore, int minsize,
                                out Pix? ppixsave, out Pix? ppixrem)
    {
        return RemoveOutliers2Core(minscore, minsize, true, out ppixsave, out ppixrem);
    }

    private Pixa RemoveOutliers2Core(float minscore, int minsize, bool wantdb,
                                     out Pix? ppixsave, out Pix? ppixrem)
    {
        const string func = "pixaRemoveOutliers2";
        ppixsave = null;
        ppixrem = null;
        minscore = Math.Min(minscore, 1.0f);
        if (minscore <= 0.0f)
            minscore = OutliersDefaultMinScore;
        if (minsize < 0)
            minsize = OutliersDefaultMinSetSize;

        int debug = wantdb ? 1 : 0;
        Recog recog = Recog.CreateFromPixa(this, 0, 40, 0, 128, 1);
        if (recog.AverageSamples(debug) != 0)
            throw Lept.Fail(func, "bad templates");

        Numa? nasave = wantdb ? Numa.Create(0) : null;
        Pixa? pixarem = wantdb ? Create(0) : null;

        Pixa pixad = Create(0);
        recog.pixaa!.GetCount(out Numa nan);
        for (int i = 0; i < recog.setsize; i++) {
            int classCount = nan.GetIValue(i);
            for (int j = 0; j < classCount; j++) {
                Pix pix1 = recog.pixaa.GetPix(i, j, L.L_CLONE);
                recog.ptaa!.GetPt(i, j, out float x1, out float y1);
                recog.naasum!.GetValue(i, j, out _, out int area1);
                Numa nascore = Numa.Create(classCount);
                for (int k = 0; k < recog.setsize; k++) {
                    Pix pix2 = recog.pixa!.GetPix(k, L.L_CLONE);
                    recog.pta!.GetPt(k, out float x2, out float y2);
                    int area2 = recog.nasum!.GetIValue(k);
                    float score = pix1.CorrelationScoreSimple(pix2, area1, area2,
                                                              x1 - x2, y1 - y2, 5, 5,
                                                              recog.sumtab);
                    nascore.AddNumber(score);
                }

                nascore.GetMax(out float maxscore, out int maxk);
                if (maxk == i && maxscore >= minscore && classCount >= minsize) {
                    Pix pix3 = recog.pixaa_u.GetPix(i, j, L.L_COPY);
                    pixad.AddPix(pix3, L.L_INSERT);
                    nasave?.AddNumber(maxscore);
                } else if (wantdb) {
                    Pix pix3 = recog.DisplayOutlier(i, j, maxk, maxscore);
                    pixarem!.AddPix(pix3, L.L_INSERT);
                }
            }
        }

        if (wantdb) {
            if (pixad.Count > 0)
                ppixsave = Recog.DisplayOutliers(pixad, nasave!);
            if (pixarem!.Count > 0)
                ppixrem = pixarem.DisplayTiledInRows(32, 1500, 1.0f, 0, 20, 2);
        }
        return pixad;
    }
}
