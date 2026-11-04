using System;
using System.IO;

namespace Leptonica;

/*
 *  pix1.c: Pix creation, copying, accessors.
 *
 *  Differences from C:
 *    - No reference counting and no pixDestroy(): the GC owns Pix.
 *      pixClone() returns the same reference.
 *    - Get/set accessor pairs are properties.
 *    - setPixMemoryManager() is not ported (managed arrays only).
 *    - pixTransferAllData() and pixExtractData() always behave as with
 *      refcount == 1: the data is moved out of the source Pix.
 *    - pixGetLinePtrs() returns word offsets of the lines in Data.
 */

/// <summary>
/// Image with 1, 2, 4, 8, 16, 24 or 32 bits per pixel (struct Pix).
/// </summary>
/// <remarks>
/// The raster is <see cref="Data"/>: <see cref="Height"/> lines of
/// <see cref="Wpl"/> 32-bit words each, with pixels stored MSB-first
/// in each word. See <see cref="ArrayAccess"/>.
/// </remarks>
public sealed partial class Pix
{
    private int w;             /* width in pixels                   */
    private int h;             /* height in pixels                  */
    private int d;             /* depth in bits (bpp)               */
    private int spp;           /* number of samples per pixel       */
    private int wpl;           /* 32-bit words/line                 */
    private int xres;          /* image res (ppi) in x direction    */
    private int yres;          /* image res (ppi) in y direction    */
    private int informat;      /* input file format, IFF_*          */
    private int special;       /* special instructions for I/O, etc */
    private string? text;      /* text string associated with pix   */
    private PixColormap? colormap;  /* colormap (may be null)       */
    private uint[] data = Array.Empty<uint>();  /* the image data   */

    private Pix() { }

    /*--------------------------------------------------------------------*
     *                             Pix Creation                           *
     *--------------------------------------------------------------------*/
    // leptonica: pixCreate (pix1.c)
    /// <summary>Creates a pix with data allocated and initialized to 0.</summary>
    public static Pix Create(int width, int height, int depth)
    {
        Pix pixd = CreateHeader(width, height, depth);
        pixd.SetData(new uint[(int)((long)pixd.Wpl * height)]);
        return pixd;
    }

    // leptonica: pixCreateNoInit (pix1.c)
    /// <summary>Creates a pix with data allocated without clearing interior pixels; pad bits are zeroed.</summary>
    /// <remarks>
    /// (1) Pad bits are set to avoid reading uninitialized data, because
    ///     some optimized routines read from pad bits.
    /// </remarks>
    public static Pix CreateNoInit(int width, int height, int depth)
    {
        Pix pixd = CreateHeader(width, height, depth);
        pixd.SetData(GC.AllocateUninitializedArray<uint>((int)((long)pixd.Wpl * height)));
        if (depth < 32 && ((width * depth) & 31) != 0)
            pixd.SetPadBits(0);
        return pixd;
    }

    /// <summary>
    /// Internal fast path that allocates an uninitialized raster without clearing pad bits,
    /// for callers that overwrite every word of every scanline (e.g. Copy, full-word bitwise ops).
    /// </summary>
    internal static Pix CreateRawNoInit(int width, int height, int depth)
    {
        Pix pixd = CreateHeader(width, height, depth);
        pixd.SetData(GC.AllocateUninitializedArray<uint>((int)((long)pixd.Wpl * height)));
        return pixd;
    }

    // leptonica: pixCreateTemplate (pix1.c)
    /// <summary>
    /// Makes a Pix of the same size as the input Pix, with the
    /// data array allocated and initialized to 0.
    /// </summary>
    /// <remarks>(2) Copies the other fields, including colormap if it exists.</remarks>
    public static Pix CreateTemplate(Pix pixs)
    {
        pixs.GetDimensions(out int w, out int h, out int d);
        Pix pixd = Create(w, h, d);
        pixd.CopySpp(pixs);
        pixd.CopyResolution(pixs);
        pixd.CopyColormap(pixs);
        pixd.CopyText(pixs);
        pixd.CopyInputFormat(pixs);
        return pixd;
    }

    // leptonica: pixCreateTemplateNoInit (pix1.c)
    /// <summary>
    /// Makes a Pix of the same size as the input Pix, with
    /// the data array allocated but not initialized to 0 (pad bits zeroed).
    /// </summary>
    /// <remarks>
    /// (2) Copies the other fields, including colormap if it exists.
    /// (3) Pad bits are set to avoid reading uninitialized data, because
    ///     some optimized routines read from pad bits.
    /// </remarks>
    public static Pix CreateTemplateNoInit(Pix pixs)
    {
        pixs.GetDimensions(out int w, out int h, out int d);
        Pix pixd = CreateNoInit(w, h, d);
        pixd.CopySpp(pixs);
        pixd.CopyResolution(pixs);
        pixd.CopyColormap(pixs);
        pixd.CopyText(pixs);
        pixd.CopyInputFormat(pixs);
        return pixd;
    }

    internal static Pix CreateTemplateRawNoInit(Pix pixs)
    {
        pixs.GetDimensions(out int w, out int h, out int d);
        Pix pixd = CreateRawNoInit(w, h, d);
        pixd.CopySpp(pixs);
        pixd.CopyResolution(pixs);
        pixd.CopyColormap(pixs);
        pixd.CopyText(pixs);
        pixd.CopyInputFormat(pixs);
        return pixd;
    }

    // leptonica: pixCreateWithCmap (pix1.c)
    /// <summary>
    /// Creates an image with a colormap, initialized to value 0, and assigns the
    /// initialization color to colormap index 0.
    /// </summary>
    /// <param name="width">Width of the image in pixels.</param>
    /// <param name="height">Height of the image in pixels.</param>
    /// <param name="depth">Depth of the image: 2, 4 or 8 bpp.</param>
    /// <param name="initcolor">Initialization color: <see cref="L.L_SET_BLACK"/> or <see cref="L.L_SET_WHITE"/>.</param>
    /// <returns>The new image with its colormap.</returns>
    /// <exception cref="ArgumentException"><paramref name="depth"/> is not 2, 4 or 8.</exception>
    public static Pix CreateWithCmap(int width, int height, int depth, int initcolor)
    {
        if (depth != 2 && depth != 4 && depth != 8)
            throw Lept.Arg("pixCreateWithCmap", "depth not 2, 4 or 8 bpp");

        Pix pix = Create(width, height, depth);
        PixColormap cmap = PixColormap.Create(depth);
        pix.SetColormap(cmap);
        if (initcolor == L.L_SET_BLACK)
            cmap.AddColor(0, 0, 0);
        else  /* L_SET_WHITE */
            cmap.AddColor(255, 255, 255);
        return pix;
    }

    // leptonica: pixCreateHeader (pix1.c)
    /// <summary>Creates a pix with no data allocated.</summary>
    /// <remarks>
    /// (1) It is assumed that all 32 bit pix have 3 spp.  If there is
    ///     a valid alpha channel, this will be set to 4 spp later.
    /// (2) All pixCreate*() functions call pixCreateHeader().
    ///     The requested allocation of image data is limited in a typesafe way.
    /// </remarks>
    public static Pix CreateHeader(int width, int height, int depth)
    {
        if ((depth != 1) && (depth != 2) && (depth != 4) && (depth != 8)
             && (depth != 16) && (depth != 24) && (depth != 32))
            throw Lept.Arg("pixCreateHeader", "depth must be {1, 2, 4, 8, 16, 24, 32}");
        if (width <= 0)
            throw Lept.Arg("pixCreateHeader", "width must be > 0");
        if (height <= 0)
            throw Lept.Arg("pixCreateHeader", "height must be > 0");

            /* Avoid overflow in malloc, malicious or otherwise */
        ulong wpl64 = ((ulong)width * (ulong)depth + 31) / 32;
        if (wpl64 > ((1UL << 24) - 1)) {
            Log.Error("pixCreateHeader", $"requested w = {width}, h = {height}, d = {depth}");
            throw Lept.Arg("pixCreateHeader", "wpl >= 2^24");
        }
        int wpl = (int)wpl64;
        ulong bignum = 4UL * (ulong)wpl * (ulong)height;   /* number of bytes to be requested */
        if (bignum > ((1UL << 31) - 1)) {
            Log.Error("pixCreateHeader", $"requested w = {width}, h = {height}, d = {depth}");
            throw Lept.Arg("pixCreateHeader", "requested bytes >= 2^31");
        }

        Pix pixd = new Pix();
        pixd.Width = width;
        pixd.Height = height;
        pixd.Depth = depth;
        pixd.Wpl = wpl;
        if (depth == 24 || depth == 32)
            pixd.Spp = 3;
        else
            pixd.Spp = 1;
        pixd.informat = L.IFF_UNKNOWN;
        return pixd;
    }

    // leptonica: pixClone (pix1.c)
    /// <summary>Returns the same pix; there is no reference count.</summary>
    public static Pix Clone(Pix pixs)
    {
        return pixs;
    }

    /*-------------------------------------------------------------------------*
     *                                 Pix Copy                                *
     *-------------------------------------------------------------------------*/
    // leptonica: pixCopy (pix1.c)
    /// <summary>Copies an image, into an existing destination image if one is given.</summary>
    /// <param name="pixd">Optional destination; can be <c>null</c>, the same object as <paramref name="pixs"/>, or a different image.</param>
    /// <param name="pixs">The source image.</param>
    /// <returns>The destination image: a new image, or <paramref name="pixd"/> after the copy.</returns>
    /// <remarks>
    /// <para>There are three cases: if <paramref name="pixd"/> is <c>null</c> a new image is made;
    /// if it is the same object as <paramref name="pixs"/> nothing is done; otherwise the data are copied into it.</para>
    /// <para>The general pattern of use is <c>pixd = Pix.Copy(pixd, pixs);</c>, which works for all three cases.</para>
    /// <para>In the data-copy case, if the two images have the same size and depth the data are copied directly.
    /// Otherwise the data are reallocated to the correct size and the copy proceeds.</para>
    /// </remarks>
    public static Pix Copy(Pix? pixd, Pix pixs)
    {
        if (pixs == pixd)
            return pixs;

            /* Total words in image data */
        int words = pixs.Wpl * pixs.Height;

            /* If we're making a new pix ... */
        if (pixd == null) {
            pixd = CreateTemplateRawNoInit(pixs);
            pixs.data.AsSpan(0, words).CopyTo(pixd.data);
            return pixd;
        }

            /* Reallocate image data if sizes are different. */
        pixd.ResizeImageDataNoInit(pixs);

            /* Copy non-image data fields */
        pixd.CopyColormap(pixs);
        pixd.CopySpp(pixs);
        pixd.CopyResolution(pixs);
        pixd.CopyInputFormat(pixs);
        pixd.CopyText(pixs);

            /* Copy image data */
        pixs.data.AsSpan(0, words).CopyTo(pixd.data);
        return pixd;
    }

    // leptonica: pixResizeImageData (pix1.c)
    /// <summary>
    /// If the sizes of data in pixs and this pix are unequal, this
    /// allocates a new buffer that will hold the image data of pixs.
    /// The image data from pixs is not copied into the new buffer.
    /// </summary>
    public void ResizeImageData(Pix pixs)
    {
        if (SizesEqual(pixs, this))  /* nothing to do */
            return;

            /* Make sure we can copy the data */
        pixs.GetDimensions(out int w, out int h, out int d);
        int wpl = pixs.Wpl;
        uint[] data = new uint[(int)((long)wpl * h)];

            /* OK, do it */
        Width = w;
        Height = h;
        Depth = d;
        Wpl = wpl;
        FreeAndSetData(data);  /* free old data and assign new data */
        CopyResolution(pixs);
    }

    internal void ResizeImageDataNoInit(Pix pixs)
    {
        if (SizesEqual(pixs, this))
            return;

        pixs.GetDimensions(out int w, out int h, out int d);
        int wpl = pixs.Wpl;
        uint[] data = GC.AllocateUninitializedArray<uint>((int)((long)wpl * h));

        Width = w;
        Height = h;
        Depth = d;
        Wpl = wpl;
        FreeAndSetData(data);
        CopyResolution(pixs);
    }

    // leptonica: pixCopyColormap (pix1.c)
    /// <summary>Replaces the colormap of this pix by a copy of the colormap of pixs.</summary>
    /// <remarks>(1) This destroys the colormap in pixd, unless the operation is a no-op</remarks>
    public void CopyColormap(Pix pixs)
    {
        if (pixs == this)
            return;   /* no-op */
        if (pixs.Depth != Depth)
            throw Lept.Arg("pixCopyColormap", "depths of pixs and pixd differ");

        DestroyColormap();
        PixColormap? cmaps = pixs.colormap;
        if (cmaps == null)  /* not an error */
            return;
        if (!cmaps.IsValid(null))
            throw Lept.Fail("pixCopyColormap", "cmap not valid");

        PixColormap cmapd = cmaps.Copy();
        SetColormap(cmapd);
    }

    // leptonica: pixTransferAllData (pix1.c)
    /// <summary>
    /// Does a complete data transfer from pixs to this pix; pixs is nulled.
    /// </summary>
    /// <param name="pixs">source; set to null on return</param>
    /// <param name="copytext">true to copy the text field</param>
    /// <param name="copyformat">true to copy the informat field</param>
    /// <remarks>
    /// The data, colormap and (optionally) text are moved, as in C with
    /// refcount == 1.  Other references to pixs see an empty raster afterwards.
    /// </remarks>
    public void TransferAllData(ref Pix? pixs, bool copytext, bool copyformat)
    {
        if (pixs == null)
            throw Lept.Arg("pixTransferAllData", "pixs not defined");
        if (pixs == this)  /* no-op */
            throw Lept.Arg("pixTransferAllData", "pixd == pixs");

        FreeData();  /* dealloc any existing data */
        SetData(pixs.Data);  /* transfer new data from pixs */
        pixs.data = Array.Empty<uint>();  /* pixs no longer owns data */
        DestroyColormap();  /* free the old one, if it exists */
        colormap = pixs.Colormap;  /* transfer to pixd */
        pixs.colormap = null;  /* pixs no longer owns colormap */
        if (copytext) {
            Text = pixs.Text;
            pixs.Text = null;
        }

        CopySpp(pixs);
        CopyResolution(pixs);
        CopyDimensions(pixs);
        if (copyformat)
            CopyInputFormat(pixs);

        pixs = null;
    }

    // leptonica: pixSwapAndDestroy (pix1.c)
    /// <summary>
    /// After this operation, pixd refers to what was pixs, and pixs is null.
    /// </summary>
    public static void SwapAndDestroy(ref Pix? pixd, ref Pix? pixs)
    {
        if (pixs == null)
            throw Lept.Arg("pixSwapAndDestroy", "pixs not defined");

        pixd = pixs;
        pixs = null;
    }

    /*--------------------------------------------------------------------*
     *                              Pix Accessors                         *
     *--------------------------------------------------------------------*/
    // leptonica: pixGetWidth, pixSetWidth (pix1.c)
    /// <summary>Gets or sets the image width in pixels.</summary>
    /// <exception cref="ArgumentException">The value being set is negative.</exception>
    public int Width
    {
        get => w;
        set {
            if (value < 0) {
                w = 0;
                throw Lept.Arg("pixSetWidth", "width must be >= 0");
            }
            w = value;
        }
    }

    // leptonica: pixGetHeight, pixSetHeight (pix1.c)
    /// <summary>Gets or sets the image height in pixels.</summary>
    /// <exception cref="ArgumentException">The value being set is negative.</exception>
    public int Height
    {
        get => h;
        set {
            if (value < 0) {
                h = 0;
                throw Lept.Arg("pixSetHeight", "h must be >= 0");
            }
            h = value;
        }
    }

    // leptonica: pixGetDepth, pixSetDepth (pix1.c)
    /// <summary>Gets or sets the image depth in bits per pixel.</summary>
    /// <exception cref="ArgumentException">The value being set is less than 1.</exception>
    public int Depth
    {
        get => d;
        set {
            if (value < 1)
                throw Lept.Arg("pixSetDepth", "d must be >= 1");
            d = value;
        }
    }

    // leptonica: pixGetDimensions (pix1.c)
    /// <summary>Gets the width, height and depth of the image.</summary>
    /// <param name="pw">Receives the width in pixels.</param>
    /// <param name="ph">Receives the height in pixels.</param>
    /// <param name="pd">Receives the depth in bits per pixel.</param>
    public void GetDimensions(out int pw, out int ph, out int pd)
    {
        pw = w;
        ph = h;
        pd = d;
    }

    // leptonica: pixSetDimensions (pix1.c)
    /// <summary>Sets the width, height and depth of the image.</summary>
    /// <param name="w">The new width; 0 skips setting this value.</param>
    /// <param name="h">The new height; 0 skips setting this value.</param>
    /// <param name="d">The new depth; 0 skips setting this value.</param>
    public void SetDimensions(int w, int h, int d)
    {
        if (w > 0) Width = w;
        if (h > 0) Height = h;
        if (d > 0) Depth = d;
    }

    // leptonica: pixCopyDimensions (pix1.c)
    /// <summary>Copies the width, height, depth and words-per-line from another image.</summary>
    /// <param name="pixs">The image to copy the dimensions from.</param>
    public void CopyDimensions(Pix pixs)
    {
        if (pixs == this)
            return;   /* no-op */

        Width = pixs.Width;
        Height = pixs.Height;
        Depth = pixs.Depth;
        Wpl = pixs.Wpl;
    }

    // leptonica: pixGetSpp, pixSetSpp (pix1.c)
    /// <summary>Gets or sets the number of samples per pixel: 1, 3 or 4.</summary>
    /// <exception cref="ArgumentException">The value being set is less than 1.</exception>
    /// <remarks>
    /// For a 32 bpp image, this can be used to ignore the alpha sample (value 3) or to use it (value 4).
    /// </remarks>
    public int Spp
    {
        get => spp;
        set {
            if (value < 1)
                throw Lept.Arg("pixSetSpp", "spp must be >= 1");
            spp = value;
        }
    }

    // leptonica: pixCopySpp (pix1.c)
    /// <summary>Copies the number of samples per pixel from another image.</summary>
    /// <param name="pixs">The image to copy the value from.</param>
    public void CopySpp(Pix pixs)
    {
        if (pixs == this)
            return;   /* no-op */

        Spp = pixs.Spp;
    }

    // leptonica: pixGetWpl, pixSetWpl (pix1.c)
    /// <summary>Gets or sets the number of 32-bit words per raster line.</summary>
    public int Wpl
    {
        get => wpl;
        set => wpl = value;
    }

    // leptonica: pixGetXRes, pixSetXRes (pix1.c)
    /// <summary>Gets or sets the horizontal resolution in pixels per inch.</summary>
    public int XRes
    {
        get => xres;
        set => xres = value;
    }

    // leptonica: pixGetYRes, pixSetYRes (pix1.c)
    /// <summary>Gets or sets the vertical resolution in pixels per inch.</summary>
    public int YRes
    {
        get => yres;
        set => yres = value;
    }

    // leptonica: pixGetResolution (pix1.c)
    /// <summary>Gets the horizontal and vertical resolution.</summary>
    /// <param name="pxres">Receives the horizontal resolution in pixels per inch.</param>
    /// <param name="pyres">Receives the vertical resolution in pixels per inch.</param>
    public void GetResolution(out int pxres, out int pyres)
    {
        pxres = xres;
        pyres = yres;
    }

    // leptonica: pixSetResolution (pix1.c)
    /// <summary>Sets the horizontal and vertical resolution.</summary>
    /// <param name="xres">The horizontal resolution in pixels per inch; 0 skips setting this value.</param>
    /// <param name="yres">The vertical resolution in pixels per inch; 0 skips setting this value.</param>
    public void SetResolution(int xres, int yres)
    {
        if (xres > 0) this.xres = xres;
        if (yres > 0) this.yres = yres;
    }

    // leptonica: pixCopyResolution (pix1.c)
    /// <summary>Copies the horizontal and vertical resolution from another image.</summary>
    /// <param name="pixs">The image to copy the resolution from.</param>
    public void CopyResolution(Pix pixs)
    {
        if (pixs == this)
            return;   /* no-op */

        XRes = pixs.XRes;
        YRes = pixs.YRes;
    }

    // leptonica: pixScaleResolution (pix1.c)
    /// <summary>Multiplies the horizontal and vertical resolution by scale factors.</summary>
    /// <param name="xscale">Scale factor for the horizontal resolution; must be positive.</param>
    /// <param name="yscale">Scale factor for the vertical resolution; must be positive.</param>
    /// <exception cref="ArgumentException">A scale factor is not positive.</exception>
    public void ScaleResolution(float xscale, float yscale)
    {
        double maxres = 100000000.0;

        if (xscale <= 0 || yscale <= 0)
            throw Lept.Arg("pixScaleResolution", "invalid scaling ratio");

        double xres = (double)xscale * (float)(this.xres) + 0.5;
        double yres = (double)yscale * (float)(this.yres) + 0.5;
        this.xres = (int)(uint)Math.Min(xres, maxres);
        this.yres = (int)(uint)Math.Min(yres, maxres);
    }

    // leptonica: pixGetInputFormat, pixSetInputFormat (pix1.c)
    /// <summary>Gets or sets the format of the file the image was read from, one of the <c>L.IFF_*</c> values.</summary>
    public int InputFormat
    {
        get => informat;
        set => informat = value;
    }

    // leptonica: pixCopyInputFormat (pix1.c)
    /// <summary>Copies the input file format from another image.</summary>
    /// <param name="pixs">The image to copy the format from.</param>
    public void CopyInputFormat(Pix pixs)
    {
        if (pixs == this)
            return;   /* no-op */

        InputFormat = pixs.InputFormat;
    }

    // leptonica: pixSetSpecial (pix1.c)
    /// <summary>Gets or sets a value holding special instructions for I/O and similar operations.</summary>
    public int Special
    {
        get => special;
        set => special = value;
    }

    // leptonica: pixGetText, pixSetText (pix1.c)
    /// <summary>Gets or sets the text string associated with the image; can be <c>null</c>.</summary>
    public string? Text
    {
        get => text;
        set => text = value;
    }

    // leptonica: pixAddText (pix1.c)
    /// <summary>Appends a string to any existing text of the image.</summary>
    /// <param name="textstring">The string to append; can be <c>null</c>.</param>
    public void AddText(string? textstring)
    {
        text = (text ?? "") + (textstring ?? "");
    }

    // leptonica: pixCopyText (pix1.c)
    /// <summary>Copies the text string from another image.</summary>
    /// <param name="pixs">The image to copy the text from.</param>
    public void CopyText(Pix pixs)
    {
        if (pixs == this)
            return;   /* no-op */

        Text = pixs.text;
    }

    // leptonica: pixGetTextCompNew (pix1.c)
    /// <summary>
    /// Retrieves the text string and performs ascii85 decoding
    /// followed by decompression on it.
    /// </summary>
    /// <returns>The decoded and decompressed data.</returns>
    /// <exception cref="ArgumentException">The image has no text string.</exception>
    public byte[] GetTextCompNew()
    {
        string str = text ?? throw Lept.Arg("pixGetTextCompNew", "text not defined");
        return LeptEncoding.DecodeAscii85WithComp(str, str.Length);
    }

    // leptonica: pixSetTextCompNew (pix1.c)
    /// <summary>
    /// Performs compression and ascii85 encoding on data, and stores the
    /// result as the text string.
    /// </summary>
    /// <param name="data">The data to compress, encode and store.</param>
    public void SetTextCompNew(ReadOnlySpan<byte> data)
    {
        text = LeptEncoding.EncodeAscii85WithComp(data);
    }

    // leptonica: pixGetColormap (pix1.c)
    /// <summary>Gets the colormap of the image, or <c>null</c> if it has none.</summary>
    public PixColormap? Colormap => colormap;

    // leptonica: pixSetColormap (pix1.c)
    /// <summary>Replaces any existing colormap.</summary>
    /// <param name="colormap">The new colormap; if <c>null</c>, nothing is done.</param>
    /// <exception cref="LeptonicaException">The colormap is not valid for the image.</exception>
    /// <remarks>
    /// <para>If the colormap is not valid, this throws after assigning it. The caller
    /// should check whether the depths of the image and the colormap can differ.</para>
    /// <para>The new colormap must not belong to any other image.</para>
    /// </remarks>
    public void SetColormap(PixColormap? colormap)
    {
        if (colormap == null) return;

            /* Make sure the colormap doesn't get lost */
        DestroyColormap();
        this.colormap = colormap;

        if (!colormap.IsValid(null))
            throw Lept.Fail("pixSetColormap", "colormap is not valid");
    }

    // leptonica: pixDestroyColormap (pix1.c)
    /// <summary>Removes the colormap from the image.</summary>
    public void DestroyColormap()
    {
        colormap = null;
    }

    // leptonica: pixGetData (pix1.c)
    /// <summary>Gets the raster data of the image: <see cref="Height"/> lines of <see cref="Wpl"/> words each.</summary>
    public uint[] Data => data;

    // leptonica: pixFreeAndSetData (pix1.c)
    /// <summary>Discards the current raster data and uses a new array in its place.</summary>
    /// <param name="data">The new raster data.</param>
    public void FreeAndSetData(uint[] data)
    {
        FreeData();
        this.data = data;
    }

    // leptonica: pixSetData (pix1.c)
    /// <summary>Replaces the raster data of the image with a new array.</summary>
    /// <param name="data">The new raster data.</param>
    public void SetData(uint[] data)
    {
        this.data = data;
    }

    // leptonica: pixFreeData (pix1.c)
    /// <summary>Discards the raster data of the image, leaving it empty.</summary>
    public void FreeData()
    {
        data = Array.Empty<uint>();
    }

    // leptonica: pixExtractData (pix1.c)
    /// <summary>Extracts the raster data; the image is left with an empty raster.</summary>
    /// <returns>The raster data that belonged to the image.</returns>
    public uint[] ExtractData()
    {
        uint[] data = this.data;
        this.data = Array.Empty<uint>();
        return data;
    }

    /*--------------------------------------------------------------------*
     *                          Pix line ptrs                             *
     *--------------------------------------------------------------------*/
    // leptonica: pixGetLinePtrs (pix1.c)
    /// <summary>Returns the offset in Data of the first word of each line.</summary>
    public int[] GetLinePtrs()
    {
        int h = Height;
        int[] lines = new int[h];
        int wpl = Wpl;
        for (int i = 0; i < h; i++)
            lines[i] = i * wpl;

        return lines;
    }

    /*--------------------------------------------------------------------*
     *                         Pix Size Comparisons                       *
     *--------------------------------------------------------------------*/
    // leptonica: pixSizesEqual (pix1.c)
    /// <summary>True if the two pix have the same {h, w, d}.</summary>
    public static bool SizesEqual(Pix pix1, Pix pix2)
    {
        if (pix1 == pix2)
            return true;

        if ((pix1.Width != pix2.Width) ||
            (pix1.Height != pix2.Height) ||
            (pix1.Depth != pix2.Depth))
            return false;
        else
            return true;
    }

    // leptonica: pixMaxAspectRatio (pix1.c)
    /// <summary>Max aspect ratio, >= 1.0.</summary>
    public float MaxAspectRatio()
    {
        GetDimensions(out int w, out int h, out _);
        if (w == 0 || h == 0)
            throw Lept.Fail("pixMaxAspectRatio", $"invalid size: w = {w}, h = {h}");

        return Math.Max((float)h / (float)w, (float)w / (float)h);
    }

    /*--------------------------------------------------------------------*
     *                    Print output for debugging                      *
     *--------------------------------------------------------------------*/
    // leptonica: pixPrintStreamInfo (pix1.c)
    /// <summary>Writes a one-line description of the image to a writer, for debugging.</summary>
    /// <param name="fp">The writer to write to.</param>
    /// <param name="text">Optional identifying string; can be <c>null</c>.</param>
    public void PrintStreamInfo(TextWriter fp, string? text)
    {
        if (text != null)
            fp.Write($"  Pix Info for {text}:\n");
        fp.Write($"    width = {Width}, height = {Height}, depth = {Depth}, spp = {Spp}\n");
        fp.Write($"    wpl = {Wpl}, data = {data.Length} words, refcount = 1\n");
        fp.Write($"    xres = {XRes}, yres = {YRes}\n");
        PixColormap? cmap;
        if ((cmap = colormap) != null)
            cmap.WriteStream(fp);
        else
            fp.Write("    no colormap\n");
        int informat = InputFormat;
        fp.Write($"    input format: {informat} ({L.ImageFileFormatExtensions[informat]})\n");
        if (this.text != null)
            fp.Write($"    text: {this.text}\n");
    }
}
