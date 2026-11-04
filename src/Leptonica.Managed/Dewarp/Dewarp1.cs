using System;
using System.IO;

namespace Leptonica;

/*
 *  dewarp1.c: basic operations and serialization for Dewarp and Dewarpa.
 */

public static partial class L
{
    /// <summary>Version number used in the serialized <see cref="Dewarp"/> and <see cref="Dewarpa"/> formats.</summary>
    public const int DEWARP_VERSION_NUMBER = 4;
}

/// <summary>Data structure for a single page disparity model (L_Dewarp).</summary>
public sealed partial class Dewarp
{
    internal Dewarpa? dewa;
    internal Pix? pixs;
    internal FPix? sampvdispar;
    internal FPix? samphdispar;
    internal FPix? sampydispar;
    internal FPix? fullvdispar;
    internal FPix? fullhdispar;
    internal FPix? fullydispar;
    internal Numa? namidys;
    internal Numa? nacurves;
    internal int w;
    internal int h;
    internal int pageno;
    internal int sampling;
    internal int redfactor;
    internal int minlines;
    internal int nlines;
    internal int mincurv;
    internal int maxcurv;
    internal int leftslope;
    internal int rightslope;
    internal int leftcurv;
    internal int rightcurv;
    internal int nx;
    internal int ny;
    internal int hasref;
    internal int refpage;
    internal int vsuccess;
    internal int hsuccess;
    internal int ysuccess;
    internal int vvalid;
    internal int hvalid;
    internal int skip_horiz;
    internal int debug;

    internal Dewarp()
    {
    }

    /// <summary>Gets the page number of the model.</summary>
    public int PageNo => pageno;

    /// <summary>Gets the width of the source image in pixels.</summary>
    public int Width => w;

    /// <summary>Gets the height of the source image in pixels.</summary>
    public int Height => h;

    /// <summary>Gets the sampling factor of the disparity arrays.</summary>
    public int Sampling => sampling;

    /// <summary>Gets the reduction factor (1 or 2) of the image used to build the model.</summary>
    public int RedFactor => redfactor;

    /// <summary>Gets the minimum number of text lines required to build the model.</summary>
    public int MinLines => minlines;

    /// <summary>Gets the number of text lines found in the image.</summary>
    public int NumLines => nlines;

    /// <summary>Gets the number of sample columns in the disparity arrays.</summary>
    public int Nx => nx;

    /// <summary>Gets the number of sample rows in the disparity arrays.</summary>
    public int Ny => ny;

    /// <summary>Gets a value indicating whether this page uses the model of a reference page instead of its own.</summary>
    public bool HasRef => hasref != 0;

    /// <summary>Gets the page number of the reference page, if <see cref="HasRef"/> is <c>true</c>.</summary>
    public int RefPage => refpage;

    /// <summary>Gets a value indicating whether a vertical disparity model was built successfully.</summary>
    public bool VSuccess => vsuccess != 0;

    /// <summary>Gets a value indicating whether a horizontal disparity model was built successfully.</summary>
    public bool HSuccess => hsuccess != 0;

    /// <summary>Gets a value indicating whether a model based on the left and right edge slopes was built successfully.</summary>
    public bool YSuccess => ysuccess != 0;

    /// <summary>Gets a value indicating whether the vertical disparity model passed validation.</summary>
    public bool VValid => vvalid != 0;

    /// <summary>Gets a value indicating whether the horizontal disparity model passed validation.</summary>
    public bool HValid => hvalid != 0;

    /// <summary>Gets the vertical disparity array at the sampled resolution, or <c>null</c> if it has not been built.</summary>
    public FPix? SampVDispar => sampvdispar;

    /// <summary>Gets the horizontal disparity array at the sampled resolution, or <c>null</c> if it has not been built.</summary>
    public FPix? SampHDispar => samphdispar;

    /// <summary>Gets the slope-based disparity array at the sampled resolution, or <c>null</c> if it has not been built.</summary>
    public FPix? SampYDispar => sampydispar;

    /// <summary>Gets the vertical disparity array at full image resolution, or <c>null</c> if it has not been built.</summary>
    public FPix? FullVDispar => fullvdispar;

    /// <summary>Gets the horizontal disparity array at full image resolution, or <c>null</c> if it has not been built.</summary>
    public FPix? FullHDispar => fullhdispar;

    /// <summary>Gets the slope-based disparity array at full image resolution, or <c>null</c> if it has not been built.</summary>
    public FPix? FullYDispar => fullydispar;

    /*----------------------------------------------------------------------*
     *                           Create/destroy Dewarp                      *
     *----------------------------------------------------------------------*/
    /// <summary>Creates a single page disparity model for a binary page image.</summary>
    /// <param name="pixs">The 1 bpp page image, which is cloned.</param>
    /// <param name="pageno">The page number.</param>
    /// <returns>The new model, which has no disparity arrays yet.</returns>
    /// <exception cref="ArgumentException"><paramref name="pixs"/> is <c>null</c> or is not 1 bpp.</exception>
    // leptonica: dewarpCreate (dewarp1.c)
    public static Dewarp Create(Pix pixs, int pageno)
    {
        const string func = "dewarpCreate";
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");
        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");

        return new Dewarp {
            pixs = Pix.Clone(pixs),
            pageno = pageno,
            w = pixs.Width,
            h = pixs.Height
        };
    }

    /// <summary>Creates a model that refers to the model of another page.</summary>
    /// <param name="pageno">The page number of the new model.</param>
    /// <param name="refpage">The page number of the page whose model is used.</param>
    /// <returns>The new reference model.</returns>
    // leptonica: dewarpCreateRef (dewarp1.c)
    public static Dewarp CreateRef(int pageno, int refpage)
    {
        return new Dewarp {
            pageno = pageno,
            hasref = 1,
            refpage = refpage
        };
    }

    /*----------------------------------------------------------------------*
     *                       Dewarp serialized I/O                          *
     *----------------------------------------------------------------------*/
    /// <summary>Reads a serialized page model from a file.</summary>
    /// <param name="filename">The path of the file to read.</param>
    /// <returns>The model read from the file.</returns>
    /// <exception cref="ArgumentException"><paramref name="filename"/> is <c>null</c>.</exception>
    // leptonica: dewarpRead (dewarp1.c)
    public static Dewarp Read(string filename)
    {
        const string func = "dewarpRead";
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");
        using FileStream fp = Utils.FopenReadStream(filename);
        return ReadStream(fp);
    }

    /// <summary>Reads a serialized page model from a stream.</summary>
    /// <param name="fp">The stream to read from.</param>
    /// <returns>The model read from the stream.</returns>
    /// <exception cref="ArgumentException"><paramref name="fp"/> is <c>null</c>.</exception>
    // leptonica: dewarpReadStream (dewarp1.c)
    public static Dewarp ReadStream(Stream fp)
    {
        const string func = "dewarpReadStream";
        if (fp == null)
            throw Lept.Arg(func, "stream not defined");
        fp = DewarpIo.Seekable(fp);

        Span<int> v = stackalloc int[2];
        int version, pageno, hasref, refpage, sampling, redfactor;
        int nlines, minlines, w, h, nx, ny, vdispar, hdispar;
        int mincurv = 0, maxcurv = 0;
        int leftslope = 0, rightslope = 0, leftcurv = 0, rightcurv = 0;

        CScanner sc = new CScanner(fp);
        try {
            if (sc.Scan("\nDewarp Version %d\n", v) != 1)
                throw Lept.Arg(func, "not a dewarp file");
            version = v[0];
            if (version != L.DEWARP_VERSION_NUMBER)
                throw Lept.Arg(func, "invalid dewarp version");
            if (sc.Scan("pageno = %d\n", v) != 1)
                throw Lept.Arg(func, "read fail for pageno");
            pageno = v[0];
            if (sc.Scan("hasref = %d, refpage = %d\n", v) != 2)
                throw Lept.Arg(func, "read fail for hasref, refpage");
            hasref = v[0];
            refpage = v[1];
            if (sc.Scan("sampling = %d, redfactor = %d\n", v) != 2)
                throw Lept.Arg(func, "read fail for sampling/redfactor");
            sampling = v[0];
            redfactor = v[1];
            if (sc.Scan("nlines = %d, minlines = %d\n", v) != 2)
                throw Lept.Arg(func, "read fail for nlines/minlines");
            nlines = v[0];
            minlines = v[1];
            if (sc.Scan("w = %d, h = %d\n", v) != 2)
                throw Lept.Arg(func, "read fail for w, h");
            w = v[0];
            h = v[1];
            if (sc.Scan("nx = %d, ny = %d\n", v) != 2)
                throw Lept.Arg(func, "read fail for nx, ny");
            nx = v[0];
            ny = v[1];
            if (sc.Scan("vert_dispar = %d, horiz_dispar = %d\n", v) != 2)
                throw Lept.Arg(func, "read fail for flags");
            vdispar = v[0];
            hdispar = v[1];
            if (vdispar != 0) {
                if (sc.Scan("min line curvature = %d, max line curvature = %d\n", v) != 2)
                    throw Lept.Arg(func, "read fail for mincurv & maxcurv");
                mincurv = v[0];
                maxcurv = v[1];
            }
            if (hdispar != 0) {
                if (sc.Scan("left edge slope = %d, right edge slope = %d\n", v) != 2)
                    throw Lept.Arg(func, "read fail for leftslope & rightslope");
                leftslope = v[0];
                rightslope = v[1];
                if (sc.Scan("left edge curvature = %d, right edge curvature = %d\n", v) != 2)
                    throw Lept.Arg(func, "read fail for leftcurv & rightcurv");
                leftcurv = v[0];
                rightcurv = v[1];
            }
        } finally {
            sc.Release();
        }

        FPix? fpixv = vdispar != 0 ? FPix.ReadStream(fp) : null;
        FPix? fpixh = hdispar != 0 ? FPix.ReadStream(fp) : null;
        fp.ReadByte();

        Dewarp dew = new Dewarp {
            w = w,
            h = h,
            pageno = pageno,
            sampling = sampling,
            redfactor = redfactor,
            minlines = minlines,
            nlines = nlines,
            hasref = hasref,
            refpage = refpage,
            nx = nx,
            ny = ny
        };
        if (hasref == 0)
            dew.vsuccess = 1;
        if (vdispar != 0) {
            dew.mincurv = mincurv;
            dew.maxcurv = maxcurv;
            dew.vsuccess = 1;
            dew.sampvdispar = fpixv;
        }
        if (hdispar != 0) {
            dew.leftslope = leftslope;
            dew.rightslope = rightslope;
            dew.leftcurv = leftcurv;
            dew.rightcurv = rightcurv;
            dew.hsuccess = 1;
            dew.samphdispar = fpixh;
        }

        return dew;
    }

    /// <summary>Reads a serialized page model from memory.</summary>
    /// <param name="data">The serialized model.</param>
    /// <returns>The model read from the data.</returns>
    // leptonica: dewarpReadMem (dewarp1.c)
    public static Dewarp ReadMem(ReadOnlySpan<byte> data)
    {
        using MemoryStream fp = new MemoryStream(data.ToArray(), writable: false);
        return ReadStream(fp);
    }

    /// <summary>Writes the page model to a file in serialized form.</summary>
    /// <param name="filename">The path of the file to write.</param>
    /// <exception cref="ArgumentException"><paramref name="filename"/> is <c>null</c>.</exception>
    // leptonica: dewarpWrite (dewarp1.c)
    public void Write(string filename)
    {
        const string func = "dewarpWrite";
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");
        using FileStream fp = Utils.FopenWriteStream(filename, "wb");
        WriteStream(fp);
    }

    /// <summary>Writes the page model to a stream in serialized form.</summary>
    /// <param name="fp">The stream to write to.</param>
    /// <exception cref="ArgumentException"><paramref name="fp"/> is <c>null</c>.</exception>
    // leptonica: dewarpWriteStream (dewarp1.c)
    public void WriteStream(Stream fp)
    {
        const string func = "dewarpWriteStream";
        if (fp == null)
            throw Lept.Arg(func, "stream not defined");

        CStdio.Fprintf(fp, $"\nDewarp Version {L.DEWARP_VERSION_NUMBER}\n");
        CStdio.Fprintf(fp, $"pageno = {pageno}\n");
        CStdio.Fprintf(fp, $"hasref = {hasref}, refpage = {refpage}\n");
        CStdio.Fprintf(fp, $"sampling = {sampling}, redfactor = {redfactor}\n");
        CStdio.Fprintf(fp, $"nlines = {nlines}, minlines = {minlines}\n");
        CStdio.Fprintf(fp, $"w = {w}, h = {h}\n");
        CStdio.Fprintf(fp, $"nx = {nx}, ny = {ny}\n");
        int vdispar = sampvdispar != null ? 1 : 0;
        int hdispar = samphdispar != null ? 1 : 0;
        CStdio.Fprintf(fp, $"vert_dispar = {vdispar}, horiz_dispar = {hdispar}\n");
        if (vdispar != 0)
            CStdio.Fprintf(fp, $"min line curvature = {mincurv}, max line curvature = {maxcurv}\n");
        if (hdispar != 0) {
            CStdio.Fprintf(fp, $"left edge slope = {leftslope}, right edge slope = {rightslope}\n");
            CStdio.Fprintf(fp, $"left edge curvature = {leftcurv}, right edge curvature = {rightcurv}\n");
        }
        if (vdispar != 0)
            sampvdispar!.WriteStream(fp);
        if (hdispar != 0)
            samphdispar!.WriteStream(fp);
        CStdio.Fprintf(fp, $"\n");

        if (vdispar == 0)
            Log.Warning(func, "no disparity arrays!");
    }

    /// <summary>Serializes the page model to memory.</summary>
    /// <returns>The serialized model.</returns>
    // leptonica: dewarpWriteMem (dewarp1.c)
    public byte[] WriteMem()
    {
        using MemoryStream fp = new MemoryStream();
        WriteStream(fp);
        return fp.ToArray();
    }
}

/// <summary>Data structure holding page Dewarp models across a book/document (L_Dewarpa).</summary>
public sealed partial class Dewarpa
{
    private const int InitialPtrArraySize = 20;
    private const int MaxPtrArraySize = 10000;
    private const int DefaultArraySampling = 30;
    private const int MinArraySampling = 8;
    private const int DefaultMinLines = 15;
    private const int MinMinLines = 4;
    private const int DefaultMaxRefDist = 16;
    private const int DefaultUseBoth = 1;
    private const int DefaultCheckColumns = 1;

    private const int DefaultMaxLineCurv = 150;
    private const int DefaultMinDiffLineCurv = 0;
    private const int DefaultMaxDiffLineCurv = 170;
    private const int DefaultMaxEdgeCurv = 50;
    private const int DefaultMaxDiffEdgeCurv = 40;
    private const int DefaultMaxEdgeSlope = 80;

    internal int nalloc;
    internal int maxpage;
    internal Dewarp?[] dewarp;
    internal Dewarp?[] dewarpcache;
    internal Numa? namodels;
    internal Numa? napages;
    internal int redfactor;
    internal int sampling;
    internal int minlines;
    internal int maxdist;
    internal int max_linecurv;
    internal int min_diff_linecurv;
    internal int max_diff_linecurv;
    internal int max_edgeslope;
    internal int max_edgecurv;
    internal int max_diff_edgecurv;
    internal int useboth;
    internal int check_columns;
    internal int modelsready;

    private Dewarpa(int nptrs)
    {
        nalloc = nptrs;
        dewarp = new Dewarp?[nptrs];
        dewarpcache = new Dewarp?[nptrs];
    }

    /// <summary>Gets the largest page number for which a model has been stored.</summary>
    public int MaxPage => maxpage;

    /// <summary>Gets the sampling factor of the disparity arrays.</summary>
    public int Sampling => sampling;

    /// <summary>Gets the reduction factor (1 or 2) of the images used to build the models.</summary>
    public int RedFactor => redfactor;

    /// <summary>Gets the minimum number of text lines required to build a model.</summary>
    public int MinLines => minlines;

    /// <summary>Gets the maximum distance, in pages, to a page whose model can be used as a reference.</summary>
    public int MaxDist => maxdist;

    /// <summary>Gets the page numbers of the pages that have valid models, or <c>null</c> if not yet computed.</summary>
    public Numa? NaModels => namodels;

    /// <summary>Gets the page numbers of the pages that have a model of their own, or <c>null</c> if not yet computed.</summary>
    public Numa? NaPages => napages;

    /*----------------------------------------------------------------------*
     *                          Create/destroy Dewarpa                      *
     *----------------------------------------------------------------------*/
    /// <summary>Creates an array of page models.</summary>
    /// <param name="nptrs">The number of page slots to allocate; a value of 0 or less selects the default.</param>
    /// <param name="sampling">The sampling factor of the disparity arrays; 0 selects the default, and a value that is too small is raised to the minimum.</param>
    /// <param name="redfactor">The reduction factor of the images used to build the models: 1 or 2.</param>
    /// <param name="minlines">The minimum number of text lines required to build a model; 0 selects the default.</param>
    /// <param name="maxdist">The maximum distance, in pages, to a page whose model can be used as a reference; a negative value selects the default.</param>
    /// <returns>The new array, with no models.</returns>
    /// <exception cref="ArgumentException"><paramref name="nptrs"/> is too large or <paramref name="redfactor"/> is not 1 or 2.</exception>
    // leptonica: dewarpaCreate (dewarp1.c)
    public static Dewarpa Create(int nptrs, int sampling, int redfactor,
                                 int minlines, int maxdist)
    {
        const string func = "dewarpaCreate";
        if (nptrs <= 0)
            nptrs = InitialPtrArraySize;
        if (nptrs > MaxPtrArraySize)
            throw Lept.Arg(func, "too many pages");
        if (redfactor != 1 && redfactor != 2)
            throw Lept.Arg(func, "redfactor not in {1,2}");
        if (sampling == 0) {
            sampling = DefaultArraySampling;
        } else if (sampling < MinArraySampling) {
            Log.Warning(func, $"sampling too small; setting to {MinArraySampling}");
            sampling = MinArraySampling;
        }
        if (minlines == 0) {
            minlines = DefaultMinLines;
        } else if (minlines < MinMinLines) {
            // NOTE(upstream): warns MinMinLines (4) then sets DefaultMinLines (15).
            Log.Warning(func, $"minlines too small; setting to {MinMinLines}");
            minlines = DefaultMinLines;
        }
        if (maxdist < 0)
            maxdist = DefaultMaxRefDist;

        Dewarpa dewa = new Dewarpa(nptrs) {
            sampling = sampling,
            redfactor = redfactor,
            minlines = minlines,
            maxdist = maxdist,
            max_linecurv = DefaultMaxLineCurv,
            min_diff_linecurv = DefaultMinDiffLineCurv,
            max_diff_linecurv = DefaultMaxDiffLineCurv,
            max_edgeslope = DefaultMaxEdgeSlope,
            max_edgecurv = DefaultMaxEdgeCurv,
            max_diff_edgecurv = DefaultMaxDiffEdgeCurv,
            check_columns = DefaultCheckColumns,
            useboth = DefaultUseBoth
        };
        return dewa;
    }

    /// <summary>Creates an array of page models, with disparity arrays computed, from a set of page images.</summary>
    /// <param name="pixac">The compressed 1 bpp page images, assumed to be cropped to the page regions.</param>
    /// <param name="useboth">Nonzero to apply both vertical and horizontal disparity arrays when dewarping.</param>
    /// <param name="sampling">The sampling factor of the disparity arrays.</param>
    /// <param name="minlines">The minimum number of text lines required to build a model.</param>
    /// <param name="maxdist">The maximum distance, in pages, to a page whose model can be used as a reference.</param>
    /// <returns>The array of models, indexed by page number and ready for serialization or for dewarping.</returns>
    /// <exception cref="ArgumentException"><paramref name="pixac"/> is <c>null</c> or an argument is invalid.</exception>
    /// <remarks>
    /// <para>The sampling, minlines and maxdist arguments are applied to all images; see <see cref="Create"/> for details.</para>
    /// <para>
    /// Placeholder entries in the input, if any, are 1 x 1 x 1 images. The offset of the input array gives the
    /// mapping between the index in the array and the page number.
    /// </para>
    /// <para>
    /// Direct models are made only for pages with images. Reference models are made for pages of the same parity
    /// that are within <paramref name="maxdist"/> of the nearest direct model.
    /// </para>
    /// </remarks>
    // leptonica: dewarpaCreateFromPixacomp (dewarp1.c)
    public static Dewarpa CreateFromPixacomp(Pixacomp pixac, int useboth,
                                             int sampling, int minlines, int maxdist)
    {
        const string func = "dewarpaCreateFromPixacomp";
        if (pixac == null)
            throw Lept.Arg(func, "pixac not defined");

        int nptrs = pixac.Count;
        Dewarpa dewa = Create(pixac.Offset + nptrs, sampling, 1, minlines, maxdist);
        dewa.UseBothArrays(useboth);

        for (int i = 0; i < nptrs; i++) {
            int pageno = pixac.Offset + i;
            Pix pixt = pixac.GetPix(pageno);
            if (pixt.Width > 1) {
                Dewarp dew;
                try {
                    dew = Dewarp.Create(pixt, pageno);
                } catch (Exception e) when (e is ArgumentException || e is LeptonicaException) {
                    Log.Error(func, "unable to make dew!");
                    continue;
                }
                dewa.InsertDewarp(dew);
                dew.BuildPageModel(null);
                if (dew.vsuccess == 0) {
                    dewa.DestroyDewarp(pageno);
                    Log.Error(func, $"unable to build model for page {i}");
                    continue;
                }
                dew.Minimize();
            }
        }
        dewa.InsertRefModels(0, 0);
        return dewa;
    }

    /// <summary>Removes and discards the model of a page.</summary>
    /// <param name="pageno">The page number of the model to remove.</param>
    /// <returns>0 if successful; 1 if the page number is out of bounds or there is no model for the page.</returns>
    // leptonica: dewarpaDestroyDewarp (dewarp1.c)
    public int DestroyDewarp(int pageno)
    {
        const string func = "dewarpaDestroyDewarp";
        if (pageno < 0 || pageno > maxpage) {
            Log.Error(func, "page out of bounds");
            return 1;
        }
        if (dewarp[pageno] == null) {
            Log.Error(func, "dew not defined");
            return 1;
        }

        dewarp[pageno] = null;
        return 0;
    }

    /*----------------------------------------------------------------------*
     *                       Dewarpa insertion/extraction                   *
     *----------------------------------------------------------------------*/
    /// <summary>Inserts a page model into the array, which then owns it.</summary>
    /// <param name="dew">The model to insert.</param>
    /// <returns>0 if successful; 1 if the page number is too large.</returns>
    /// <exception cref="ArgumentException"><paramref name="dew"/> is <c>null</c>.</exception>
    /// <remarks>
    /// <para>The array keeps track of the largest page number stored. This must be done before the disparity model is built.</para>
    /// <para>Unlike most Leptonica arrays, the model can be added at any page number, even beyond the initial allocation.</para>
    /// </remarks>
    // leptonica: dewarpaInsertDewarp (dewarp1.c)
    public int InsertDewarp(Dewarp dew)
    {
        const string func = "dewarpaInsertDewarp";
        if (dew == null)
            throw Lept.Arg(func, "dew not defined");

        dew.dewa = this;
        int pageno = dew.pageno;
        if (pageno > MaxPtrArraySize) {
            Log.Error(func, "too many pages");
            return 1;
        }
        if (pageno > maxpage)
            maxpage = pageno;
        modelsready = 0;

        int n = nalloc;
        int newsize = n;
        if (pageno >= 2 * n)
            newsize = 2 * pageno;
        else if (pageno >= n)
            newsize = 2 * n;
        if (newsize > n)
            ExtendArraysToSize(newsize);

        dewarp[pageno] = dew;
        dew.sampling = sampling;
        dew.redfactor = redfactor;
        dew.minlines = minlines;

        dew.nx = (dew.w + 2 * dew.sampling - 2) / dew.sampling;
        dew.ny = (dew.h + 2 * dew.sampling - 2) / dew.sampling;
        return 0;
    }

    // leptonica: dewarpaExtendArraysToSize (dewarp1.c)
    private void ExtendArraysToSize(int size)
    {
        if (size > nalloc) {
            Array.Resize(ref dewarp, size);
            Array.Resize(ref dewarpcache, size);
            nalloc = size;
        }
    }

    /// <summary>Gets the model for a page.</summary>
    /// <param name="index">The page number.</param>
    /// <returns>The model, which remains owned by the array, or <c>null</c> if the index is invalid or there is no model for the page.</returns>
    // leptonica: dewarpaGetDewarp (dewarp1.c)
    public Dewarp? GetDewarp(int index)
    {
        const string func = "dewarpaGetDewarp";
        if (index < 0 || index > maxpage) {
            Log.Error(func, $"index = {index} is invalid; max index = {maxpage}");
            return null;
        }
        return dewarp[index];
    }

    /*----------------------------------------------------------------------*
     *         Setting parameters to control rendering from the model       *
     *----------------------------------------------------------------------*/
    /// <summary>Sets the limits on curvature and slope that a model must satisfy to be used for dewarping.</summary>
    /// <param name="maxLinecurv">The maximum absolute value of the vertical disparity line curvatures, in micro-units; -1 selects the default.</param>
    /// <param name="minDiffLinecurv">The minimum absolute value of the largest difference in vertical disparity line curvatures; 0 accepts all models and -1 selects the default.</param>
    /// <param name="maxDiffLinecurv">The maximum absolute value of the largest difference in vertical disparity line curvatures; -1 selects the default.</param>
    /// <param name="maxEdgecurv">The maximum absolute value of the left and right edge curvature of the horizontal disparity; 0 fits a straight line and -1 selects the default.</param>
    /// <param name="maxDiffEdgecurv">The maximum absolute value of the difference between the left and right edge curvatures, ignored if <paramref name="maxEdgecurv"/> is 0; -1 selects the default.</param>
    /// <param name="maxEdgeslope">The maximum slope coefficient of the left and right line edges, in milli-units; -1 selects the default.</param>
    /// <remarks>
    /// <para>Curvature is the coefficient of the quadratic term when a text line is approximated by a quadratic, in pixel units. Because curvatures are very small, the limits are expressed in micro-units (multiplied by 10^6) and the slope limit in milli-units (multiplied by 10^3).</para>
    /// <para>An image whose line curvature is less than about 10 micro-units has fairly straight text lines.</para>
    /// <para>A model whose maximum line curvature is larger than about 150 micro-units, or whose left or right edge curvature is larger than about 50 micro-units, should probably not be used.</para>
    /// </remarks>
    // leptonica: dewarpaSetCurvatures (dewarp1.c)
    public void SetCurvatures(int maxLinecurv, int minDiffLinecurv, int maxDiffLinecurv,
                              int maxEdgecurv, int maxDiffEdgecurv, int maxEdgeslope)
    {
        max_linecurv = maxLinecurv == -1 ? DefaultMaxLineCurv : Math.Abs(maxLinecurv);
        min_diff_linecurv = minDiffLinecurv == -1 ? DefaultMinDiffLineCurv : Math.Abs(minDiffLinecurv);
        max_diff_linecurv = maxDiffLinecurv == -1 ? DefaultMaxDiffLineCurv : Math.Abs(maxDiffLinecurv);
        max_edgecurv = maxEdgecurv == -1 ? DefaultMaxEdgeCurv : Math.Abs(maxEdgecurv);
        max_diff_edgecurv = maxDiffEdgecurv == -1 ? DefaultMaxDiffEdgeCurv : Math.Abs(maxDiffEdgecurv);
        max_edgeslope = maxEdgeslope == -1 ? DefaultMaxEdgeSlope : Math.Abs(maxEdgeslope);
        modelsready = 0;
    }

    /// <summary>Sets whether both the vertical and the horizontal disparity arrays are applied when dewarping.</summary>
    /// <param name="useboth">Nonzero to try to apply both arrays; zero to apply the vertical array only.</param>
    /// <remarks>A model with only a vertical disparity array is always valid.</remarks>
    // leptonica: dewarpaUseBothArrays (dewarp1.c)
    public void UseBothArrays(int useboth)
    {
        this.useboth = useboth;
        modelsready = 0;
    }

    /// <summary>Sets whether the number of text columns is checked before horizontal disparity is applied.</summary>
    /// <param name="checkColumns">Nonzero to check the columns; zero to skip the check.</param>
    /// <remarks>
    /// <para>
    /// If this is set, and if both arrays are in use, the number of text columns is counted, and if there is more
    /// than one column the horizontal disparity array is not applied. The default is to check.
    /// </para>
    /// <para>
    /// For horizontal disparity correction on a single column of text, a valid horizontal disparity array is needed
    /// and both arrays must be in use. For several columns, the check must also be turned off.
    /// </para>
    /// </remarks>
    // leptonica: dewarpaSetCheckColumns (dewarp1.c)
    public void SetCheckColumns(int checkColumns)
    {
        check_columns = checkColumns;
    }

    /// <summary>Sets the maximum distance, in pages, to a page whose model can be used as a reference.</summary>
    /// <param name="maxdist">The maximum distance in pages.</param>
    // leptonica: dewarpaSetMaxDistance (dewarp1.c)
    public void SetMaxDistance(int maxdist)
    {
        this.maxdist = maxdist;
        modelsready = 0;
    }

    /*----------------------------------------------------------------------*
     *                       Dewarpa serialized I/O                         *
     *----------------------------------------------------------------------*/
    /// <summary>Reads a serialized array of page models from a file.</summary>
    /// <param name="filename">The path of the file to read.</param>
    /// <returns>The array of models read from the file.</returns>
    /// <exception cref="ArgumentException"><paramref name="filename"/> is <c>null</c>.</exception>
    // leptonica: dewarpaRead (dewarp1.c)
    public static Dewarpa Read(string filename)
    {
        const string func = "dewarpaRead";
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");
        using FileStream fp = Utils.FopenReadStream(filename);
        return ReadStream(fp);
    }

    /// <summary>Reads a serialized array of page models from a stream.</summary>
    /// <param name="fp">The stream to read from.</param>
    /// <returns>The array of models read from the stream.</returns>
    /// <exception cref="ArgumentException"><paramref name="fp"/> is <c>null</c>.</exception>
    // leptonica: dewarpaReadStream (dewarp1.c)
    public static Dewarpa ReadStream(Stream fp)
    {
        const string func = "dewarpaReadStream";
        if (fp == null)
            throw Lept.Arg(func, "stream not defined");
        fp = DewarpIo.Seekable(fp);

        Span<int> v = stackalloc int[4];
        int version, ndewarp, maxpage;
        int sampling, redfactor, minlines, maxdist, useboth;
        int maxLinecurv, minDiffLinecurv, maxDiffLinecurv;
        int maxEdgeslope, maxEdgecurv, maxDiffEdgecurv;

        CScanner sc = new CScanner(fp);
        try {
            if (sc.Scan("\nDewarpa Version %d\n", v) != 1)
                throw Lept.Arg(func, "not a dewarpa file");
            version = v[0];
            if (version != L.DEWARP_VERSION_NUMBER)
                throw Lept.Arg(func, "invalid dewarp version");
            if (sc.Scan("ndewarp = %d, maxpage = %d\n", v) != 2)
                throw Lept.Arg(func, "read fail for maxpage+");
            ndewarp = v[0];
            maxpage = v[1];
            if (ndewarp < 1)
                throw Lept.Arg(func, "pages not >= 1");
            if (ndewarp > MaxPtrArraySize)
                throw Lept.Arg(func, "too many pages");
            if (sc.Scan("sampling = %d, redfactor = %d, minlines = %d, maxdist = %d\n", v) != 4)
                throw Lept.Arg(func, "read fail for 4 params");
            sampling = v[0];
            redfactor = v[1];
            minlines = v[2];
            maxdist = v[3];
            if (sc.Scan("max_linecurv = %d, min_diff_linecurv = %d, max_diff_linecurv = %d\n", v) != 3)
                throw Lept.Arg(func, "read fail for linecurv");
            maxLinecurv = v[0];
            minDiffLinecurv = v[1];
            maxDiffLinecurv = v[2];
            if (sc.Scan("max_edgeslope = %d, max_edgecurv = %d, max_diff_edgecurv = %d\n", v) != 3)
                throw Lept.Arg(func, "read fail for edgecurv");
            maxEdgeslope = v[0];
            maxEdgecurv = v[1];
            maxDiffEdgecurv = v[2];
            if (sc.Scan("fullmodel = %d\n", v) != 1)
                throw Lept.Arg(func, "read fail for useboth");
            useboth = v[0];
        } finally {
            sc.Release();
        }

        Dewarpa dewa = Create(maxpage + 1, sampling, redfactor, minlines, maxdist);
        dewa.maxpage = maxpage;
        dewa.max_linecurv = maxLinecurv;
        dewa.min_diff_linecurv = minDiffLinecurv;
        dewa.max_diff_linecurv = maxDiffLinecurv;
        dewa.max_edgeslope = maxEdgeslope;
        dewa.max_edgecurv = maxEdgecurv;
        dewa.max_diff_edgecurv = maxDiffEdgecurv;
        dewa.useboth = useboth;
        Numa namodels = Numa.Create(ndewarp);
        dewa.namodels = namodels;
        for (int i = 0; i < ndewarp; i++) {
            Dewarp dew = Dewarp.ReadStream(fp);
            dewa.InsertDewarp(dew);
            namodels.AddNumber(dew.pageno);
        }

        dewa.InsertRefModels(0, 0);
        return dewa;
    }

    /// <summary>Reads a serialized array of page models from memory.</summary>
    /// <param name="data">The serialized array of models.</param>
    /// <returns>The array of models read from the data.</returns>
    // leptonica: dewarpaReadMem (dewarp1.c)
    public static Dewarpa ReadMem(ReadOnlySpan<byte> data)
    {
        using MemoryStream fp = new MemoryStream(data.ToArray(), writable: false);
        return ReadStream(fp);
    }

    /// <summary>Writes the array of page models to a file in serialized form.</summary>
    /// <param name="filename">The path of the file to write.</param>
    /// <exception cref="ArgumentException"><paramref name="filename"/> is <c>null</c>.</exception>
    // leptonica: dewarpaWrite (dewarp1.c)
    public void Write(string filename)
    {
        const string func = "dewarpaWrite";
        if (filename == null)
            throw Lept.Arg(func, "filename not defined");
        using FileStream fp = Utils.FopenWriteStream(filename, "wb");
        WriteStream(fp);
    }

    /// <summary>Writes the array of page models to a stream in serialized form.</summary>
    /// <param name="fp">The stream to write to.</param>
    /// <exception cref="ArgumentException"><paramref name="fp"/> is <c>null</c>.</exception>
    // leptonica: dewarpaWriteStream (dewarp1.c)
    public void WriteStream(Stream fp)
    {
        const string func = "dewarpaWriteStream";
        if (fp == null)
            throw Lept.Arg(func, "stream not defined");

        ListPages();
        if (namodels == null)
            throw Lept.Fail(func, "dewa->namodels not made");
        int ndewarp = namodels.Count;

        CStdio.Fprintf(fp, $"\nDewarpa Version {L.DEWARP_VERSION_NUMBER}\n");
        CStdio.Fprintf(fp, $"ndewarp = {ndewarp}, maxpage = {maxpage}\n");
        CStdio.Fprintf(fp, $"sampling = {sampling}, redfactor = {redfactor}, minlines = {minlines}, maxdist = {maxdist}\n");
        CStdio.Fprintf(fp, $"max_linecurv = {max_linecurv}, min_diff_linecurv = {min_diff_linecurv}, max_diff_linecurv = {max_diff_linecurv}\n");
        CStdio.Fprintf(fp, $"max_edgeslope = {max_edgeslope}, max_edgecurv = {max_edgecurv}, max_diff_edgecurv = {max_diff_edgecurv}\n");
        CStdio.Fprintf(fp, $"fullmodel = {useboth}\n");
        for (int i = 0; i < ndewarp; i++) {
            int pageno = namodels.GetIValue(i);
            Dewarp? dew = GetDewarp(pageno);
            dew!.WriteStream(fp);
        }
    }

    /// <summary>Serializes the array of page models to memory.</summary>
    /// <returns>The serialized array of models.</returns>
    // leptonica: dewarpaWriteMem (dewarp1.c)
    public byte[] WriteMem()
    {
        using MemoryStream fp = new MemoryStream();
        WriteStream(fp);
        return fp.ToArray();
    }
}

internal static class DewarpIo
{
    internal static Stream Seekable(Stream fp)
    {
        if (fp.CanSeek)
            return fp;
        MemoryStream ms = new MemoryStream();
        fp.CopyTo(ms);
        ms.Position = 0;
        return ms;
    }
}
