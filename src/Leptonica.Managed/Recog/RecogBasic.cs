using System;
using System.IO;
using System.Text;

namespace Leptonica;

/// <summary>Constants used by the character recognizer.</summary>
public static partial class L
{
    /// <summary>Version number of the serialized <see cref="Recog"/> format.</summary>
    public const int RECOG_VERSION_NUMBER = 2;

    /* Flags for describing limited character sets */
    /// <summary>The character set is unknown.</summary>
    public const int L_UNKNOWN = 0;
    /// <summary>The character set is the Arabic numerals 0 to 9.</summary>
    public const int L_ARABIC_NUMERALS = 1;
    /// <summary>The character set is lower-case Roman numerals.</summary>
    public const int L_LC_ROMAN_NUMERALS = 2;
    /// <summary>The character set is upper-case Roman numerals.</summary>
    public const int L_UC_ROMAN_NUMERALS = 3;
    /// <summary>The character set is lower-case letters.</summary>
    public const int L_LC_ALPHA = 4;
    /// <summary>The character set is upper-case letters.</summary>
    public const int L_UC_ALPHA = 5;

    /* Flags for selecting between using average and all templates */
    /// <summary>Match against all templates.</summary>
    public const int L_USE_ALL_TEMPLATES = 0;
    /// <summary>Match against the average template of each class.</summary>
    public const int L_USE_AVERAGE_TEMPLATES = 1;
}

/*
 *  recogbasic.c: Recog creation, access, character/index lookup, serialization.
 *
 *  Not ported (memory management): recogDestroy.
 */

/// <summary>Data returned from correlation matching on a single character (struct L_Rch).</summary>
public sealed partial class Rch
{
    internal int index;
    internal float score;
    internal string? text;
    internal int sample;
    internal int xloc;
    internal int yloc;
    internal int width;

    internal Rch(int index, float score, string? text, int sample,
                 int xloc, int yloc, int width)
    {
        this.index = index;
        this.score = score;
        this.text = text;
        this.sample = sample;
        this.xloc = xloc;
        this.yloc = yloc;
        this.width = width;
    }

    /// <summary>Gets the index of the best matching character class.</summary>
    public int Index => index;
    /// <summary>Gets the correlation score of the best match.</summary>
    public float Score => score;
    /// <summary>Gets the text label of the best matching class, or <c>null</c> if there is none.</summary>
    public string? Text => text;
    /// <summary>Gets the index of the best matching template within its class.</summary>
    public int Sample => sample;
    /// <summary>Gets the x location of the matched character.</summary>
    public int XLoc => xloc;
    /// <summary>Gets the y location of the matched character.</summary>
    public int YLoc => yloc;
    /// <summary>Gets the width of the matched character.</summary>
    public int Width => width;
}

/// <summary>Data returned from correlation matching on an array of characters (struct L_Rcha).</summary>
public sealed partial class Rcha
{
    internal Numa naindex;
    internal Numa nascore;
    internal Sarray satext;
    internal Numa nasample;
    internal Numa naxloc;
    internal Numa nayloc;
    internal Numa nawidth;

    internal Rcha()
    {
        naindex = Numa.Create(0);
        nascore = Numa.Create(0);
        satext = Sarray.Create(0);
        nasample = Numa.Create(0);
        naxloc = Numa.Create(0);
        nayloc = Numa.Create(0);
        nawidth = Numa.Create(0);
    }

    /// <summary>Gets the indices of the best matching classes.</summary>
    public Numa NaIndex => naindex;
    /// <summary>Gets the correlation scores of the best matches.</summary>
    public Numa NaScore => nascore;
    /// <summary>Gets the text labels of the best matching classes.</summary>
    public Sarray SaText => satext;
    /// <summary>Gets the indices of the best matching templates within their classes.</summary>
    public Numa NaSample => nasample;
    /// <summary>Gets the x locations of the matched characters.</summary>
    public Numa NaXLoc => naxloc;
    /// <summary>Gets the y locations of the matched characters.</summary>
    public Numa NaYLoc => nayloc;
    /// <summary>Gets the widths of the matched characters.</summary>
    public Numa NaWidth => nawidth;
}

/// <summary>Data used for decoding a line of characters (struct L_Rdid).</summary>
public sealed partial class Rdid
{
    internal Pix pixs = null!;
    internal int[][]? counta;
    internal int[][]? delya;
    internal int narray;
    internal int size;
    internal int[] setwidth = Array.Empty<int>();
    internal Numa? nasum;
    internal Numa? namoment;
    internal bool fullarrays;
    internal float[] beta = Array.Empty<float>();
    internal float[] gamma = Array.Empty<float>();
    internal float[] trellisscore = Array.Empty<float>();
    internal int[] trellistempl = Array.Empty<int>();
    internal Numa natempl = null!;
    internal Numa naxloc = null!;
    internal Numa nadely = null!;
    internal Numa nawidth = null!;
    internal Boxa boxa = null!;
    internal Numa nascore = null!;
    internal Numa natempl_r = null!;
    internal Numa nasample_r = null!;
    internal Numa naxloc_r = null!;
    internal Numa nadely_r = null!;
    internal Numa nawidth_r = null!;
    internal Numa nascore_r = null!;

    internal Rdid() { }

    /// <summary>Gets the 1 bpp image of the line of text being decoded.</summary>
    public Pix Pixs => pixs;
    /// <summary>Gets the number of arrays used by the decoder.</summary>
    public int NArray => narray;
    /// <summary>Gets the number of templates in the decoder.</summary>
    public int Size => size;
    /// <summary>Gets a value indicating whether the full count and shift arrays are kept.</summary>
    public bool FullArrays => fullarrays;
    /// <summary>Gets the bounding boxes of the decoded characters.</summary>
    public Boxa Boxa => boxa;
    /// <summary>Gets the template indices of the best path through the trellis.</summary>
    public Numa NaTempl => natempl;
    /// <summary>Gets the x locations of the templates on the best path.</summary>
    public Numa NaXLoc => naxloc;
    /// <summary>Gets the vertical shifts of the templates on the best path.</summary>
    public Numa NaDely => nadely;
    /// <summary>Gets the widths of the templates on the best path.</summary>
    public Numa NaWidth => nawidth;
    /// <summary>Gets the correlation scores of the templates on the best path.</summary>
    public Numa NaScore => nascore;
}

/// <summary>Character recognizer (struct L_Recog).</summary>
public sealed partial class Recog
{
    private const int MaxExamplesInClass = 256;

    /* Default recog parameters that can be changed */
    private const int DefaultCharsetType = L.L_ARABIC_NUMERALS;
    private const int DefaultMinNopad = 1;
    private const float DefaultMaxWHRatio = 3.0f;
    private const float DefaultMaxHTRatio = 2.6f;
    private const int DefaultThreshold = 150;
    private const int DefaultMaxYShift = 1;

    internal int scalew;
    internal int scaleh;
    internal int linew;
    internal int templ_use;
    internal int maxarraysize;
    internal int setsize;
    internal int threshold;
    internal int maxyshift;
    internal int charset_type;
    internal int charset_size;
    internal int min_nopad;
    internal int num_samples;
    internal int minwidth_u;
    internal int maxwidth_u;
    internal int minheight_u;
    internal int maxheight_u;
    internal int minwidth;
    internal int maxwidth;
    internal bool ave_done;
    internal bool train_done;
    internal float max_wh_ratio;
    internal float max_ht_ratio;
    internal int min_splitw;
    internal int max_splith;
    internal Sarray sa_text = null!;
    internal L_Dna dna_tochar = null!;
    internal int[] centtab = Array.Empty<int>();
    internal int[] sumtab = Array.Empty<int>();
    internal Pixaa pixaa_u = null!;
    internal Ptaa? ptaa_u;
    internal Numaa? naasum_u;
    internal Pixaa? pixaa;
    internal Ptaa? ptaa;
    internal Numaa? naasum;
    internal Pixa? pixa_u;
    internal Pta? pta_u;
    internal Numa? nasum_u;
    internal Pixa? pixa;
    internal Pta? pta;
    internal Numa? nasum;
    internal Pixa? pixa_tr;
    internal Pixa? pixadb_ave;
    internal Pixa? pixa_id;
    internal Pix? pixdb_ave;
    internal Pix? pixdb_range;
    internal Pixa pixadb_boot = null!;
    internal Pixa pixadb_split = null!;
    internal L_Bmf? bmf;
    internal int bmf_size;
    internal Rdid? did;
    internal Rch? rch;
    internal Rcha? rcha;

    private Recog() { }

    /// <summary>Gets the width to which all templates are scaled; 0 for no width scaling.</summary>
    public int ScaleW => scalew;
    /// <summary>Gets the height to which all templates are scaled; 0 for no height scaling.</summary>
    public int ScaleH => scaleh;
    /// <summary>Gets the width of the normalized strokes; 0 if the strokes are not normalized.</summary>
    public int LineW => linew;
    /// <summary>Gets or sets whether matching uses all templates (<see cref="L.L_USE_ALL_TEMPLATES"/>) or the class averages (<see cref="L.L_USE_AVERAGE_TEMPLATES"/>).</summary>
    public int TemplUse { get => templ_use; set => templ_use = value; }
    /// <summary>Gets the size of the character set, that is, the number of classes.</summary>
    public int SetSize => setsize;
    /// <summary>Gets the threshold used for binarization of the templates.</summary>
    public int Threshold => threshold;
    /// <summary>Gets the maximum vertical shift from the nominal centroid alignment: 0, 1 or 2.</summary>
    public int MaxYShift => maxyshift;
    /// <summary>Gets the type of the character set, one of the <c>L.L_*</c> character-set flags.</summary>
    public int CharsetType => charset_type;
    /// <summary>Gets the expected number of classes in the character set.</summary>
    public int CharsetSize => charset_size;
    /// <summary>Gets the minimum number of samples required in each class without padding.</summary>
    public int MinNopad => min_nopad;
    /// <summary>Gets the total number of training samples.</summary>
    public int NumSamples => num_samples;
    /// <summary>Gets a value indicating whether the average templates have been computed.</summary>
    public bool AveDone => ave_done;
    /// <summary>Gets or sets a value indicating whether training is finished.</summary>
    public bool TrainDone { get => train_done; set => train_done = value; }
    /// <summary>Gets the result of the most recent match of a single character, or <c>null</c> if there is none.</summary>
    public Rch? Rch => rch;
    /// <summary>Gets the result of the most recent match of an array of characters, or <c>null</c> if there is none.</summary>
    public Rcha? Rcha => rcha;
    /// <summary>Gets the debug image of the average templates, or <c>null</c> if it was not made.</summary>
    public Pix? PixdbAve => pixdb_ave;
    /// <summary>Gets the debug image of the range of template sizes, or <c>null</c> if it was not made.</summary>
    public Pix? PixdbRange => pixdb_range;
    /// <summary>Gets the debug images of the average templates, or <c>null</c> if they were not made.</summary>
    public Pixa? PixadbAve => pixadb_ave;
    /// <summary>Gets the debug images produced when training from a bootstrap recognizer.</summary>
    public Pixa PixadbBoot => pixadb_boot;
    /// <summary>Gets the debug images produced when splitting touching characters.</summary>
    public Pixa PixadbSplit => pixadb_split;

    /*------------------------------------------------------------------------*
     *                Recog: initialization and destruction                   *
     *------------------------------------------------------------------------*/
    // leptonica: recogCreateFromRecog (recogbasic.c)
    /// <summary>Creates a recognizer from the unscaled training data in an existing recognizer.</summary>
    /// <param name="recs">The source recognizer, which may have arbitrary parameters.</param>
    /// <param name="scalew">Width to which all templates are scaled; 0 for no scaling.</param>
    /// <param name="scaleh">Height to which all templates are scaled; 0 for no scaling.</param>
    /// <param name="linew">Width of the normalized strokes; 0 to skip.</param>
    /// <param name="threshold">Threshold for binarization; typically about 128.</param>
    /// <param name="maxyshift">Maximum vertical shift from the nominal centroid alignment; the default is 1.</param>
    /// <returns>The new recognizer.</returns>
    /// <exception cref="ArgumentException"><paramref name="recs"/> is <c>null</c>.</exception>
    /// <remarks>
    /// <para>This is a convenience method that generates a recognizer using the unscaled training data in an existing one.</para>
    /// <para>Using <paramref name="maxyshift"/> = 1 (the default) is recommended.</para>
    /// <para>See <see cref="Create"/> for the use of <paramref name="scalew"/>, <paramref name="scaleh"/> and <paramref name="linew"/>.</para>
    /// </remarks>
    public static Recog CreateFromRecog(Recog recs, int scalew, int scaleh,
                                        int linew, int threshold, int maxyshift)
    {
        const string func = "recogCreateFromRecog";
        if (recs == null)
            throw Lept.Arg(func, "recs not defined");

        Pixa pixa = recs.ExtractPixa();
        return CreateFromPixa(pixa, scalew, scaleh, linew, threshold, maxyshift);
    }

    // leptonica: recogCreateFromPixa (recogbasic.c)
    /// <summary>Creates a recognizer from an array of labeled, 1 bpp images.</summary>
    /// <param name="pixa">Labeled 1 bpp images; the text of each image is its class label.</param>
    /// <param name="scalew">Width to which all templates are scaled; 0 for no scaling.</param>
    /// <param name="scaleh">Height to which all templates are scaled; 0 for no scaling.</param>
    /// <param name="linew">Width of the normalized strokes; 0 to skip.</param>
    /// <param name="threshold">Threshold for binarization; typically about 150.</param>
    /// <param name="maxyshift">Maximum vertical shift from the nominal centroid alignment; the default is 1.</param>
    /// <returns>The trained recognizer.</returns>
    /// <exception cref="ArgumentException"><paramref name="pixa"/> is <c>null</c> or not usable for training.</exception>
    /// <exception cref="LeptonicaException">Training fails because the templates are not valid.</exception>
    /// <remarks>
    /// <para>This is a convenience method for training from labeled data. The array can be read from a file.</para>
    /// <para>The array should contain the unscaled bitmaps used for training.</para>
    /// <para>See <see cref="Create"/> for the use of <paramref name="scalew"/>, <paramref name="scaleh"/> and <paramref name="linew"/>.</para>
    /// <para>All examples in the same class (that is, with the same character label) should be similar. They can be made
    /// similar by removing outliers from <paramref name="pixa"/> before calling this method.</para>
    /// </remarks>
    public static Recog CreateFromPixa(Pixa pixa, int scalew, int scaleh,
                                       int linew, int threshold, int maxyshift)
    {
        const string func = "recogCreateFromPixa";
        if (pixa == null)
            throw Lept.Arg(func, "pixa not defined");

        Recog? recog = CreateFromPixaNoFinish(pixa, scalew, scaleh, linew,
                                              threshold, maxyshift);
        TrainingFinished(ref recog, 1, -1, -1.0f);
        if (recog == null)
            throw Lept.Fail(func, "bad templates");
        return recog;
    }

    // leptonica: recogCreateFromPixaNoFinish (recogbasic.c)
    /// <summary>Creates a recognizer from an array of labeled, 1 bpp images, without finishing the training.</summary>
    /// <param name="pixa">Labeled 1 bpp images; the text of each image is its class label.</param>
    /// <param name="scalew">Width to which all templates are scaled; 0 for no scaling.</param>
    /// <param name="scaleh">Height to which all templates are scaled; 0 for no scaling.</param>
    /// <param name="linew">Width of the normalized strokes; 0 to skip.</param>
    /// <param name="threshold">Threshold for binarization; typically about 150.</param>
    /// <param name="maxyshift">Maximum vertical shift from the nominal centroid alignment; the default is 1.</param>
    /// <returns>The recognizer with the samples added but training not finished.</returns>
    /// <exception cref="ArgumentException"><paramref name="pixa"/> is <c>null</c>, contains images that are not 1 bpp or missing, or has no text labels.</exception>
    /// <remarks>
    /// <para>See <see cref="CreateFromPixa"/> for details.</para>
    /// <para>This is also used to generate an array of templates for each class within an image array.
    /// For that purpose, all arguments except <paramref name="pixa"/> are ignored.</para>
    /// </remarks>
    public static Recog CreateFromPixaNoFinish(Pixa pixa, int scalew, int scaleh,
                                               int linew, int threshold, int maxyshift)
    {
        const string func = "recogCreateFromPixaNoFinish";
        if (pixa == null)
            throw Lept.Arg(func, "pixa not defined");
        pixa.VerifyDepth(out _, out int maxd);
        if (maxd > 1)
            throw Lept.Arg(func, "not all pix are 1 bpp");

        pixa.IsFull(out bool full, out _);
        if (!full)
            throw Lept.Arg(func, "not all pix are present");

        int n = pixa.Count;
        int ntext = pixa.CountText();
        if (ntext == 0)
            throw Lept.Arg(func, "no pix have text strings");
        if (ntext < n)
            Log.Error(func, $"{ntext} text strings < {n} pix");

        Recog recog = Create(scalew, scaleh, linew, threshold, maxyshift);
        for (int i = 0; i < n; i++) {
            Pix pix = pixa.GetPix(i, L.L_CLONE);
            string? text = pix.Text;
            if (string.IsNullOrEmpty(text)) {
                Log.Error(func, $"pix[{i}] has no text");
                continue;
            }
            recog.TrainLabeled(pix, null, text, 0);
        }

        return recog;
    }

    // leptonica: recogCreate (recogbasic.c)
    /// <summary>Creates an empty recognizer.</summary>
    /// <param name="scalew">Width to which all templates are scaled; 0 for no width scaling.</param>
    /// <param name="scaleh">Height to which all templates are scaled; 0 for no height scaling.</param>
    /// <param name="linew">Width of the normalized strokes; 0 to skip the conversion.</param>
    /// <param name="threshold">Threshold for binarization; typically about 128; 0 for the default.</param>
    /// <param name="maxyshift">Maximum vertical shift from the nominal centroid alignment; the default is 1.</param>
    /// <returns>The new recognizer.</returns>
    /// <exception cref="ArgumentException"><paramref name="scalew"/> or <paramref name="scaleh"/> is negative, or <paramref name="linew"/> is greater than 10.</exception>
    /// <remarks>
    /// <para>If <paramref name="scalew"/> and <paramref name="scaleh"/> are both 0, no scaling is done.
    /// If one of them is 0 and the other is positive, scaling is isotropic to the requested size.
    /// Normally both are not set to positive values.</para>
    /// <para>Use <paramref name="linew"/> greater than 0 to convert the templates to images with fixed-width strokes.</para>
    /// <para>The only valid values for <paramref name="maxyshift"/> are 0, 1 and 2, and 1 is recommended.
    /// A value of 0 is much faster but much less likely to find the template with the best correlation.
    /// Using anything but 1 results in a warning.</para>
    /// <para>Scaling is used for finding outliers and for training a book-adapted recognizer from a bootstrap recognizer.
    /// Scaling the height to a fixed value and scaling the width accordingly (for example, <paramref name="scaleh"/> = 40,
    /// <paramref name="scalew"/> = 0) is recommended.</para>
    /// <para>The storage for most of the arrays is allocated when training is finished.</para>
    /// </remarks>
    public static Recog Create(int scalew, int scaleh, int linew,
                               int threshold, int maxyshift)
    {
        const string func = "recogCreate";
        if (scalew < 0 || scaleh < 0)
            throw Lept.Arg(func, "invalid scalew or scaleh");
        if (linew > 10)
            throw Lept.Arg(func, "invalid linew > 10");
        if (threshold == 0) threshold = DefaultThreshold;
        if (threshold < 0 || threshold > 255) {
            Log.Warning(func, "invalid threshold; using default");
            threshold = DefaultThreshold;
        }
        if (maxyshift < 0 || maxyshift > 2) {
            Log.Warning(func, "invalid maxyshift; using default value");
            maxyshift = DefaultMaxYShift;
        } else if (maxyshift == 0) {
            Log.Warning(func, "Using maxyshift = 0; faster, worse correlation results");
        } else if (maxyshift == 2) {
            Log.Warning(func, "Using maxyshift = 2; slower");
        }

        Recog recog = new Recog();
        recog.templ_use = L.L_USE_ALL_TEMPLATES;
        recog.threshold = threshold;
        recog.scalew = scalew;
        recog.scaleh = scaleh;
        recog.linew = linew;
        recog.maxyshift = maxyshift;
        recog.SetParams(1, -1, -1.0f, -1.0f);
        recog.bmf = L_Bmf.Create(null, 6);
        recog.bmf_size = 6;
        recog.maxarraysize = MaxExamplesInClass;

            /* Generate the LUTs */
        recog.centtab = Pix.MakePixelCentroidTab8();
        recog.sumtab = Pix.MakePixelSumTab8();
        recog.sa_text = Sarray.Create(0);
        recog.dna_tochar = L_Dna.Create(0);

            /* Input default values for min component size for splitting.
             * These are overwritten when recogAverageSamples() is called. */
        recog.min_splitw = 6;
        recog.max_splith = 60;

            /* Allocate the paa for the unscaled training bitmaps */
        recog.pixaa_u = Pixaa.Create(recog.maxarraysize);

            /* Generate the storage for debugging */
        recog.pixadb_boot = Pixa.Create(2);
        recog.pixadb_split = Pixa.Create(2);
        return recog;
    }

    /*------------------------------------------------------------------------*
     *                              Recog accessors                           *
     *------------------------------------------------------------------------*/
    // leptonica: recogGetCount (recogbasic.c)
    /// <summary>Gets the number of character classes in the recognizer.</summary>
    public int Count => setsize;

    // leptonica: recogSetParams (recogbasic.c)
    /// <summary>Sets the character-set and splitting parameters of the recognizer.</summary>
    /// <param name="type">Type of the character set, one of the <c>L.L_*</c> character-set flags; -1 for the default.</param>
    /// <param name="minNopad">Minimum number of samples in a class without padding; -1 for the default.</param>
    /// <param name="maxWhRatio">Maximum width-to-height ratio allowed for splitting; -1.0 for the default.</param>
    /// <param name="maxHtRatio">Maximum ratio of the largest to the smallest averaged template height; -1.0 for the default.</param>
    /// <remarks>
    /// <para>This is called when a recognizer is created.</para>
    /// <para>The default <paramref name="minNopad"/> allows for some padding. To disable padding, set it to 0.
    /// To pad only when no samples are available for the class, set it to 1.</para>
    /// <para><paramref name="maxWhRatio"/> limits the width-to-height ratio of components that are split, because splitting long components is expensive.</para>
    /// <para><paramref name="maxHtRatio"/> is a quality requirement on the training data. The recognizer does not run if the
    /// averages are computed and the templates do not satisfy it.</para>
    /// </remarks>
    public void SetParams(int type, int minNopad, float maxWhRatio, float maxHtRatio)
    {
        charset_type = (type >= 0) ? type : DefaultCharsetType;
        charset_size = GetCharsetSize(charset_type);
        min_nopad = (minNopad >= 0) ? minNopad : DefaultMinNopad;
        max_wh_ratio = (maxWhRatio > 0.0f) ? maxWhRatio : DefaultMaxWHRatio;
        max_ht_ratio = (maxHtRatio > 1.0f) ? maxHtRatio : DefaultMaxHTRatio;
    }

    // leptonica: recogGetCharsetSize (recogbasic.c)
    private static int GetCharsetSize(int type)
    {
        switch (type) {
        case L.L_UNKNOWN:
            return 0;
        case L.L_ARABIC_NUMERALS:
            return 10;
        case L.L_LC_ROMAN_NUMERALS:
            return 7;
        case L.L_UC_ROMAN_NUMERALS:
            return 7;
        case L.L_LC_ALPHA:
            return 26;
        case L.L_UC_ALPHA:
            return 26;
        default:
            Log.Error("recogGetCharsetSize", $"invalid charset_type {type}");
            return 0;
        }
    }

    /*------------------------------------------------------------------------*
     *                         Character/index lookup                         *
     *------------------------------------------------------------------------*/
    // leptonica: recogGetClassIndex (recogbasic.c)
    /// <summary>Finds the class index for a character value, adding a new class if the value has not been seen.</summary>
    /// <param name="val">Integer value of the character; can be up to 4 bytes for UTF-8.</param>
    /// <param name="text">Text from which <paramref name="val"/> was derived; used if the class is not found.</param>
    /// <param name="pindex">Receives the index of the class.</param>
    /// <returns>0 if the class was found; 1 if it was not found and has been added.</returns>
    /// <exception cref="ArgumentException"><paramref name="text"/> is <c>null</c>.</exception>
    /// <remarks>
    /// <para>This is used during training. There is one entry in the character-value array (for example, ascii)
    /// and one in the text array (for example, the ascii letter in a string) for each character class.</para>
    /// <para>If the value is not found, the template represents a character class not already seen: the number of
    /// classes is incremented by 1, and both the value array and the text array are augmented.</para>
    /// </remarks>
    public int GetClassIndex(int val, string text, out int pindex)
    {
        const string func = "recogGetClassIndex";
        pindex = -1;
        if (text == null)
            throw Lept.Arg(func, "text not defined");

            /* Search existing characters */
        int n = dna_tochar.Count;
        for (int i = 0; i < n; i++) {
            int ival = dna_tochar.GetIValue(i);
            if (val == ival) {  /* found */
                pindex = i;
                return 0;
            }
        }

            /* If not found... */
        dna_tochar.AddNumber(val);
        sa_text.AddString(text, L.L_COPY);
        setsize++;
        pindex = n;
        return 1;
    }

    // leptonica: recogStringToIndex (recogbasic.c)
    /// <summary>Finds the class index for a text label.</summary>
    /// <param name="text">The text label to look up.</param>
    /// <param name="pindex">Receives the index of the class, or -1 if the label is not found.</param>
    /// <returns>true if the label was found; otherwise false.</returns>
    /// <exception cref="ArgumentException"><paramref name="text"/> is <c>null</c>.</exception>
    public bool StringToIndex(string text, out int pindex)
    {
        const string func = "recogStringToIndex";
        pindex = -1;
        if (text == null)
            throw Lept.Arg(func, "text not defined");

            /* Search existing characters */
        int n = setsize;
        for (int i = 0; i < n; i++) {
            string charstr = GetClassString(i);
            if (text != charstr)
                continue;
            pindex = i;
            return true;
        }

        return false;
    }

    // leptonica: recogGetClassString (recogbasic.c)
    /// <summary>Gets the text label of a character class.</summary>
    /// <param name="index">Index of the class.</param>
    /// <returns>The text label of the class.</returns>
    /// <exception cref="ArgumentException"><paramref name="index"/> is out of range.</exception>
    public string GetClassString(int index)
    {
        const string func = "recogGetClassString";
        if (index < 0 || index >= setsize)
            throw Lept.Arg(func, "invalid index");
        return sa_text.GetString(index, L.L_COPY);
    }

    // leptonica: l_convertCharstrToInt (recogbasic.c)
    /// <summary>Converts a string holding one UTF-8 character to an integer value.</summary>
    /// <param name="str">A string representing one UTF-8 character, with not more than 4 bytes.</param>
    /// <returns>The integer value for the input; think of it as a one-to-one hash code.</returns>
    /// <exception cref="ArgumentException"><paramref name="str"/> is <c>null</c>, empty, or longer than 4 bytes.</exception>
    public static int ConvertCharstrToInt(string str)
    {
        const string func = "l_convertCharstrToInt";
        if (str == null)
            throw Lept.Arg(func, "str not defined");
        if (!TryConvertCharstrToInt(str, out int val, out string errMsg))
            throw Lept.Arg(func, errMsg);
        return val;
    }

    internal static bool TryConvertCharstrToInt(string? str, out int pval, out string errMsg)
    {
        pval = 0;
        errMsg = string.Empty;
        if (str == null) {
            errMsg = "str not defined";
            return false;
        }
        byte[] utf8 = Encoding.UTF8.GetBytes(str);
        int nul = Array.IndexOf(utf8, (byte)0);
        int size = nul >= 0 ? nul : utf8.Length;
        if (size == 0) {
            errMsg = "empty string";
            return false;
        }
        if (size > 4) {
            errMsg = "invalid string: > 4 bytes";
            return false;
        }

        uint val = utf8[0];
        if (size > 1)
            val = (val << 8) + utf8[1];
        if (size > 2)
            val = (val << 8) + utf8[2];
        if (size > 3)
            val = (val << 8) + utf8[3];
        pval = (int)(val & 0x7fffffff);
        return true;
    }

    /*------------------------------------------------------------------------*
     *                             Serialization                              *
     *------------------------------------------------------------------------*/
    // leptonica: recogRead (recogbasic.c)
    /// <summary>Reads a serialized recognizer from a file.</summary>
    /// <param name="filename">Path of the file to read.</param>
    /// <returns>The deserialized recognizer.</returns>
    /// <exception cref="ArgumentException"><paramref name="filename"/> is <c>null</c>.</exception>
    public static Recog Read(string filename)
    {
        const string func = "recogRead";
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");

        using FileStream fp = Utils.FopenReadStream(filename);
        return ReadStream(fp);
    }

    // leptonica: recogReadStream (recogbasic.c)
    /// <summary>Reads a serialized recognizer from a stream.</summary>
    /// <param name="fp">The stream to read.</param>
    /// <returns>The deserialized recognizer.</returns>
    /// <exception cref="ArgumentException"><paramref name="fp"/> is <c>null</c>.</exception>
    /// <exception cref="LeptonicaException">The data are not a valid recognizer.</exception>
    public static Recog ReadStream(Stream fp)
    {
        const string func = "recogReadStream";
        if (fp == null)
            throw Lept.Arg(func, "stream not defined");

        fp = Pixa.Seekable(fp);
        Span<int> v = stackalloc int[1];
        int version, setsize, threshold, maxyshift, scalew, scaleh, linew;

        CScanner sc = new CScanner(fp);
        try {
            if (sc.Scan("\nRecog Version %d\n", v) != 1)
                throw Lept.Fail(func, "not a recog file");
            version = v[0];
            if (version != L.RECOG_VERSION_NUMBER)
                throw Lept.Fail(func, "invalid recog version");
            if (sc.Scan("Size of character set = %d\n", v) != 1)
                throw Lept.Fail(func, "setsize not read");
            setsize = v[0];
            if (sc.Scan("Binarization threshold = %d\n", v) != 1)
                throw Lept.Fail(func, "binary thresh not read");
            threshold = v[0];
            if (sc.Scan("Maxyshift = %d\n", v) != 1)
                throw Lept.Fail(func, "maxyshift not read");
            maxyshift = v[0];
            if (sc.Scan("Scale to width = %d\n", v) != 1)
                throw Lept.Fail(func, "width not read");
            scalew = v[0];
            if (sc.Scan("Scale to height = %d\n", v) != 1)
                throw Lept.Fail(func, "height not read");
            scaleh = v[0];
            if (sc.Scan("Normalized line width = %d\n", v) != 1)
                throw Lept.Fail(func, "line width not read");
            linew = v[0];
            if (sc.Scan("\nLabels for character set:\n", v) < 0)
                throw Lept.Fail(func, "label intro not read");
        } finally {
            sc.Release();
        }

        Recog? recog = Create(scalew, scaleh, linew, threshold, maxyshift);
        recog.dna_tochar = L_Dna.ReadStream(fp);
        recog.sa_text = Sarray.ReadStream(fp);

        CScanner sc2 = new CScanner(fp);
        try {
            if (sc2.Scan("\nPixaa of all samples in the training set:\n", v) < 0)
                throw Lept.Fail(func, "pixaa intro not read");
        } finally {
            sc2.Release();
        }

        Pixaa paa = Pixaa.ReadStream(fp);
        recog.setsize = setsize;
        int nc = paa.Count;
        if (nc != setsize) {
            Log.Error(func, $"(setsize = {setsize}) != (paa count = {nc})");
            throw Lept.Fail(func, $"(setsize = {setsize}) != (paa count = {nc})");
        }

        AddAllSamples(ref recog, paa, 0);  /* this finishes */
        if (recog == null)
            throw Lept.Fail(func, "bad templates");
        return recog;
    }

    // leptonica: recogReadMem (recogbasic.c)
    /// <summary>Reads a serialized recognizer from memory.</summary>
    /// <param name="data">The serialized bytes.</param>
    /// <returns>The deserialized recognizer.</returns>
    public static Recog ReadMem(ReadOnlySpan<byte> data)
    {
        using MemoryStream fp = new MemoryStream(data.ToArray(), false);
        return ReadStream(fp);
    }

    // leptonica: recogWrite (recogbasic.c)
    /// <summary>Serializes the recognizer to a file.</summary>
    /// <param name="filename">Path of the file to write.</param>
    /// <exception cref="ArgumentException"><paramref name="filename"/> is <c>null</c>.</exception>
    public void Write(string filename)
    {
        const string func = "recogWrite";
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");

        using FileStream fp = Utils.FopenWriteStream(filename, "wb");
        WriteStream(fp);
    }

    // leptonica: recogWriteStream (recogbasic.c)
    /// <summary>Serializes the recognizer to a stream.</summary>
    /// <param name="fp">The stream to write to.</param>
    /// <exception cref="ArgumentException"><paramref name="fp"/> is <c>null</c>.</exception>
    /// <exception cref="LeptonicaException">Training is not finished.</exception>
    public void WriteStream(Stream fp)
    {
        const string func = "recogWriteStream";
        if (fp == null)
            throw Lept.Arg(func, "stream not defined");
        if (pixaa == null)
            throw Lept.Fail(func, "pixaa not defined; training not finished");

        CStdio.Fprintf(fp, $"\nRecog Version {L.RECOG_VERSION_NUMBER}\n");
        CStdio.Fprintf(fp, $"Size of character set = {setsize}\n");
        CStdio.Fprintf(fp, $"Binarization threshold = {threshold}\n");
        CStdio.Fprintf(fp, $"Maxyshift = {maxyshift}\n");
        CStdio.Fprintf(fp, $"Scale to width = {scalew}\n");
        CStdio.Fprintf(fp, $"Scale to height = {scaleh}\n");
        CStdio.Fprintf(fp, $"Normalized line width = {linew}\n");
        CStdio.Fprintf(fp, $"\nLabels for character set:\n");
        dna_tochar.WriteStream(fp);
        sa_text.WriteStream(fp);
        CStdio.Fprintf(fp, $"\nPixaa of all samples in the training set:\n");
        pixaa.WriteStream(fp);
    }

    // leptonica: recogWriteMem (recogbasic.c)
    /// <summary>Serializes the recognizer to a byte array.</summary>
    /// <returns>The serialized bytes.</returns>
    public byte[] WriteMem()
    {
        using MemoryStream fp = new MemoryStream();
        WriteStream(fp);
        return fp.ToArray();
    }

    // leptonica: recogExtractPixa (recogbasic.c)
    /// <summary>Generates an array of all the unscaled training images in the recognizer.</summary>
    /// <returns>An array in which each image carries its character class label in its text field.</returns>
    /// <remarks>The array is made by flattening the unscaled training images of all classes.</remarks>
    public Pixa ExtractPixa()
    {
        AddCharstrLabels();
        return pixaa_u.FlattenToPixa(out _, L.L_CLONE);
    }

    // leptonica: recogAddCharstrLabels (recogbasic.c)
    private void AddCharstrLabels()
    {
        Pixaa paa = pixaa_u;
        int n1 = paa.Count;
        for (int i = 0; i < n1; i++) {
            Pixa pixa = paa.GetPixa(i, L.L_CLONE);
            string text = sa_text.GetString(i, L.L_NOCOPY);
            int n2 = pixa.Count;
            for (int j = 0; j < n2; j++) {
                Pix pix = pixa.GetPix(j, L.L_CLONE);
                pix.Text = text;
            }
        }
    }

    // leptonica: recogAddAllSamples (recogbasic.c)
    private static void AddAllSamples(ref Recog? precog, Pixaa paa, int debug)
    {
        const string func = "recogAddAllSamples";
        if (precog == null)
            throw Lept.Arg(func, "recog not defined");
        Recog recog = precog;
        if (paa == null) {
            precog = null;
            throw Lept.Arg(func, "paa not defined");
        }

        int nc = paa.Count;
        for (int i = 0; i < nc; i++) {
            Pixa pixa = paa.GetPixa(i, L.L_CLONE);
            int ns = pixa.Count;
            string text = recog.sa_text.GetString(i, L.L_NOCOPY);
            Pixa pixa1 = Pixa.Create(ns);
            recog.pixaa_u.AddPixa(pixa1, L.L_INSERT);
            for (int j = 0; j < ns; j++) {
                Pix pix = pixa.GetPix(j, L.L_CLONE);
                if (debug != 0)
                    Log.Info(func, $"pix[{i},{j}]: text = {text}");
                recog.pixaa_u.AddPix(i, pix, null, L.L_INSERT);
            }
        }

        TrainingFinished(ref precog, 0, -1, -1.0f);
        if (precog == null)
            throw Lept.Fail(func, "bad templates; recog destroyed");
    }
}
