using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Leptonica;

/*
 *  pix2.c: Pixel poking, pad bits, border pixels, border add/remove,
 *  color components, endian byte swaps, raster data.
 *
 *  Differences from C:
 *    - Functions that return 2 for a point outside the image (pixGetPixel,
 *      pixSetPixel, pixGetRGBPixel, pixSetRGBPixel, pixSetCmapPixel,
 *      pixClearPixel, pixFlipPixel) return bool: false means outside
 *      (or, for pixSetCmapPixel, a full colormap).  Outside is silent.
 *    - The endian swaps assume a little-endian machine, as do the accessors
 *      in ArrayAccess.
 *
 *  Stubs for callees in other files: pixcmapAddNewColor,
 *  pixcmapAddNearestColor, pixcmapAddBlackOrWhite (colormap.c),
 *  pixRemoveColormap, pixConvertTo8, pixConvertTo32 (pixconv.c),
 *  pixaCreate (pixabasic.c),
 *  pixaDisplayTiledInRows (pixafunc2.c).
 */

/// <summary>Constants from pix.h, environ.h and morph.h used by pix2.c.</summary>
public static partial class L
{
    /* Black and white values (pix.h) */
    /// <summary>Get the white pixel value.</summary>
    public const int L_GET_WHITE_VAL = 1;
    /// <summary>Get the black pixel value.</summary>
    public const int L_GET_BLACK_VAL = 2;

    /* Choice of min or max component (morph.h) */
    /// <summary>Choose the minimum; useful in a downscaling "erosion".</summary>
    public const int L_CHOOSE_MIN = 1;
    /// <summary>Choose the maximum; useful in a downscaling "dilation".</summary>
    public const int L_CHOOSE_MAX = 2;
    /// <summary>Choose the maximum difference; useful in a downscaling contrast.</summary>
    public const int L_CHOOSE_MAXDIFF = 3;
    /// <summary>Choose a modification of the minimum value.</summary>
    public const int L_CHOOSE_MIN_BOOST = 4;
    /// <summary>Choose a modification of the maximum value.</summary>
    public const int L_CHOOSE_MAX_BOOST = 5;

    /* Undefined value (environ.h) */
    /// <summary>Value used to indicate an undefined quantity.</summary>
    public const int UNDEF = -1;
}

public sealed partial class Pix
{
    private static readonly uint[] rmask32 = {0x0,
        0x00000001, 0x00000003, 0x00000007, 0x0000000f,
        0x0000001f, 0x0000003f, 0x0000007f, 0x000000ff,
        0x000001ff, 0x000003ff, 0x000007ff, 0x00000fff,
        0x00001fff, 0x00003fff, 0x00007fff, 0x0000ffff,
        0x0001ffff, 0x0003ffff, 0x0007ffff, 0x000fffff,
        0x001fffff, 0x003fffff, 0x007fffff, 0x00ffffff,
        0x01ffffff, 0x03ffffff, 0x07ffffff, 0x0fffffff,
        0x1fffffff, 0x3fffffff, 0x7fffffff, 0xffffffff};

    /*-------------------------------------------------------------*
     *                         Pixel poking                        *
     *-------------------------------------------------------------*/
    // leptonica: pixGetPixel (pix2.c)
    /// <summary>Gets the pixel value at (x, y).</summary>
    /// <param name="x">pixel coords</param>
    /// <param name="y">pixel coords</param>
    /// <param name="pval">pixel value; 0 if the point is outside the image</param>
    /// <returns>false if the point is outside the image; true otherwise</returns>
    /// <remarks>
    /// (1) This returns the value in the data array.  If the pix is
    ///     colormapped, it returns the colormap index, not the rgb value.
    /// (2) Because of the function overhead and the parameter checking,
    ///     this is much slower than using the GET_DATA_*() macros directly.
    ///     Speed on a 1 Mpixel RGB image, using a 3 GHz machine:
    ///       * pixGet/pixSet: ~25 Mpix/sec
    ///       * GET_DATA/SET_DATA: ~350 MPix/sec
    ///     If speed is important and you're doing random access into
    ///     the pix, use pixGetLinePtrs() and the array access macros.
    /// (3) If the point is outside the image, this returns false (C: error 2),
    ///     with 0 in %pval.  To avoid spamming output, it fails silently.
    /// </remarks>
    public bool GetPixel(int x, int y, out uint pval)
    {
        pval = 0;
        GetDimensions(out int w, out int h, out int d);
        if (x < 0 || x >= w || y < 0 || y >= h)
            return false;

        int wpl = Wpl;
        uint[] data = Data;
        ReadOnlySpan<uint> line = data.AsSpan(y * wpl);
        uint val;
        switch (d)
        {
        case 1:
            val = (uint)ArrayAccess.GetDataBit(line, x);
            break;
        case 2:
            val = (uint)ArrayAccess.GetDataDibit(line, x);
            break;
        case 4:
            val = (uint)ArrayAccess.GetDataQbit(line, x);
            break;
        case 8:
            val = (uint)ArrayAccess.GetDataByte(line, x);
            break;
        case 16:
            val = (uint)ArrayAccess.GetDataTwoBytes(line, x);
            break;
        case 32:
            val = line[x];
            break;
        default:
            throw Lept.Arg("pixGetPixel", "depth must be in {1,2,4,8,16,32} bpp");
        }

        pval = val;
        return true;
    }

    // leptonica: pixSetPixel (pix2.c)
    /// <summary>Sets the pixel value at (x, y).</summary>
    /// <param name="x">pixel coords</param>
    /// <param name="y">pixel coords</param>
    /// <param name="val">value to be inserted</param>
    /// <returns>false if the point is outside the image; true otherwise</returns>
    /// <remarks>
    /// (1) Warning: the input value is not checked for overflow with respect
    ///     the the depth of %pix, and the sign bit (if any) is ignored.
    ///     * For d == 1, %val > 0 sets the bit on.
    ///     * For d == 2, 4, 8 and 16, %val is masked to the maximum allowable
    ///       pixel value, and any (invalid) higher order bits are discarded.
    /// (2) See pixGetPixel() for information on performance.
    /// (3) If the point is outside the image, this returns false (C: error 2).
    ///     To avoid spamming output, it fails silently.
    /// </remarks>
    public bool SetPixel(int x, int y, uint val)
    {
        GetDimensions(out int w, out int h, out int d);
        if (x < 0 || x >= w || y < 0 || y >= h)
            return false;

        uint[] data = Data;
        int wpl = Wpl;
        Span<uint> line = data.AsSpan(y * wpl);
        switch (d)
        {
        case 1:
            if (val != 0)
                ArrayAccess.SetDataBit(line, x);
            else
                ArrayAccess.ClearDataBit(line, x);
            break;
        case 2:
            ArrayAccess.SetDataDibit(line, x, (int)val);
            break;
        case 4:
            ArrayAccess.SetDataQbit(line, x, (int)val);
            break;
        case 8:
            ArrayAccess.SetDataByte(line, x, (int)val);
            break;
        case 16:
            ArrayAccess.SetDataTwoBytes(line, x, (int)val);
            break;
        case 32:
            line[x] = val;
            break;
        default:
            throw Lept.Arg("pixSetPixel", "depth must be in {1,2,4,8,16,32} bpp");
        }

        return true;
    }

    // leptonica: pixGetRGBPixel (pix2.c)
    /// <summary>Gets the rgb components of the 32 bpp pixel at (x, y).</summary>
    /// <param name="x">pixel coords</param>
    /// <param name="y">pixel coords</param>
    /// <param name="rval">red component; 0 if outside</param>
    /// <param name="gval">green component; 0 if outside</param>
    /// <param name="bval">blue component; 0 if outside</param>
    /// <returns>false if the point is outside the image; true otherwise</returns>
    /// <remarks>
    /// (1) pix must be 32 bpp rgb, not colormapped.
    /// (2) If the point is outside the image, this returns false (C: error 2),
    ///     with 0 in the outputs.  To avoid spamming output, it fails silently.
    /// </remarks>
    public bool GetRGBPixel(int x, int y, out int rval, out int gval, out int bval)
    {
        rval = gval = bval = 0;
        GetDimensions(out int w, out int h, out int d);
        if (d != 32)
            throw Lept.Arg("pixGetRGBPixel", "pix not 32 bpp");
        if (x < 0 || x >= w || y < 0 || y >= h)
            return false;

        int wpl = Wpl;
        uint[] data = Data;
        int ppixel = y * wpl + x;
        rval = ArrayAccess.GetDataByte(data[ppixel], L.COLOR_RED);
        gval = ArrayAccess.GetDataByte(data[ppixel], L.COLOR_GREEN);
        bval = ArrayAccess.GetDataByte(data[ppixel], L.COLOR_BLUE);
        return true;
    }

    // leptonica: pixSetRGBPixel (pix2.c)
    /// <summary>Sets the rgb components of the 32 bpp pixel at (x, y).</summary>
    /// <param name="x">pixel coords</param>
    /// <param name="y">pixel coords</param>
    /// <param name="rval">red component</param>
    /// <param name="gval">green component</param>
    /// <param name="bval">blue component</param>
    /// <returns>false if the point is outside the image; true otherwise</returns>
    /// <remarks>
    /// (1) pix must be 32 bpp.
    /// (2) If the point is outside the image, this returns false (C: error 2),
    ///     and to avoid spamming output, it fails silently.
    /// </remarks>
    public bool SetRGBPixel(int x, int y, int rval, int gval, int bval)
    {
        GetDimensions(out int w, out int h, out int d);
        if (d != 32)
            throw Lept.Arg("pixSetRGBPixel", "pix not 32 bpp");
        if (x < 0 || x >= w || y < 0 || y >= h)
            return false;

        int wpl = Wpl;
        uint[] data = Data;
        int line = y * wpl;
        ComposeRGBPixel(rval, gval, bval, out uint pixel);
        data[line + x] = pixel;
        return true;
    }

    // leptonica: pixSetCmapPixel (pix2.c)
    /// <summary>Sets the pixel at (x, y) to the index of the given color in the colormap.</summary>
    /// <param name="x">pixel coords</param>
    /// <param name="y">pixel coords</param>
    /// <param name="rval">red component</param>
    /// <param name="gval">green component</param>
    /// <param name="bval">blue component</param>
    /// <returns>
    /// false if the point is outside the image, or if the colormap is full
    /// (d &lt; 8); true otherwise
    /// </returns>
    /// <remarks>
    /// (1) pix must be 2, 4 or 8 bpp, colormapped.
    /// (2) If the point is outside the image, this returns false (C: error 2),
    ///     and to avoid spamming output, it fails silently.
    /// (3) - If the color already exists, use it.
    ///     - If the color does not exist in the colormap, it is added
    ///       if possible.
    ///     - If there is not room in the colormap for the new color:
    ///       * if d &lt; 8, return false with an error message.
    ///       * if d == 8, find and use the nearest color.
    /// (4) Note that this operation scales with the number of colors
    ///     in the colormap, and therefore can be very expensive if an
    ///     attempt is made to set many pixels.  (In that case, it should
    ///     be implemented with a map:rgb--&gt;index for efficiency.)
    ///     This is best used with very small images.
    /// </remarks>
    public bool SetCmapPixel(int x, int y, int rval, int gval, int bval)
    {
        PixColormap? cmap = Colormap;
        if (cmap == null)
            throw Lept.Arg("pixSetCmapPixel", "pix is not colormapped");
        GetDimensions(out int w, out int h, out int d);
        if (d != 2 && d != 4 && d != 8)
            throw Lept.Arg("pixSetCmapPixel", "pix depth not 2, 4 or 8");
        if (x < 0 || x >= w || y < 0 || y >= h)
            return false;

        int index;
        if (d == 8) {  /* always add */
            cmap.AddNearestColor(rval, gval, bval, out index);
        } else {  /* d < 8 */
            if (cmap.AddNewColor(rval, gval, bval, out index) == 2) {
                Log.Error("pixSetCmapPixel", "colormap is full");
                return false;
            }
        }
        SetPixel(x, y, (uint)index);
        return true;
    }

    // leptonica: pixGetRandomPixel (pix2.c)
    /// <summary>Gets the value of a randomly chosen pixel.</summary>
    /// <param name="pval">pixel value; the rgb value if colormapped</param>
    /// <param name="px">x coordinate chosen</param>
    /// <param name="py">y coordinate chosen</param>
    /// <remarks>
    /// (1) If the pix is colormapped, it returns the rgb value.
    /// (2) This needs the C rand(); it throws until CRand is available.
    /// </remarks>
    public void GetRandomPixel(out uint pval, out int px, out int py)
    {
        pval = 0;
        px = 0;
        py = 0;

        GetDimensions(out int w, out int h, out _);
        int x = Rand() % w;
        int y = Rand() % h;
        px = x;
        py = y;
        GetPixel(x, y, out uint val);
        PixColormap? cmap = Colormap;
        if (cmap != null) {
            cmap.GetColor((int)val, out int rval, out int gval, out int bval);
            ComposeRGBPixel(rval, gval, bval, out pval);
        } else {
            pval = val;
        }
    }

    private static int Rand() => CRand.Next();

    // leptonica: pixClearPixel (pix2.c)
    /// <summary>Clears the pixel at (x, y).</summary>
    /// <param name="x">pixel coords</param>
    /// <param name="y">pixel coords</param>
    /// <returns>false if the point is outside the image; true otherwise</returns>
    /// <remarks>
    /// (1) Any depth; there is a warning if colormapped.
    /// (2) If the point is outside the image, this returns false (C: error 2).
    ///     To avoid spamming output, it fails silently.
    /// </remarks>
    public bool ClearPixel(int x, int y)
    {
        if (Colormap != null)
            Log.Warning("pixClearPixel", "cmapped: setting to 0 may not be intended");
        GetDimensions(out int w, out int h, out int d);
        if (x < 0 || x >= w || y < 0 || y >= h)
            return false;

        int wpl = Wpl;
        uint[] data = Data;
        Span<uint> line = data.AsSpan(y * wpl);
        switch (d)
        {
        case 1:
            ArrayAccess.ClearDataBit(line, x);
            break;
        case 2:
            ArrayAccess.ClearDataDibit(line, x);
            break;
        case 4:
            ArrayAccess.ClearDataQbit(line, x);
            break;
        case 8:
            ArrayAccess.SetDataByte(line, x, 0);
            break;
        case 16:
            ArrayAccess.SetDataTwoBytes(line, x, 0);
            break;
        case 32:
            line[x] = 0;
            break;
        default:
            throw Lept.Arg("pixClearPixel", "depth must be in {1,2,4,8,16,32} bpp");
        }

        return true;
    }

    // leptonica: pixFlipPixel (pix2.c)
    /// <summary>Flips (inverts) the pixel at (x, y).</summary>
    /// <param name="x">pixel coords</param>
    /// <param name="y">pixel coords</param>
    /// <returns>false if the point is outside the image; true otherwise</returns>
    /// <remarks>
    /// (1) Any depth; there is a warning if colormapped.
    /// (2) If the point is outside the image, this returns false (C: error 2).
    ///     To avoid spamming output, it fails silently.
    /// </remarks>
    public bool FlipPixel(int x, int y)
    {
        if (Colormap != null)
            Log.Warning("pixFlipPixel", "cmapped: setting to 0 may not be intended");
        GetDimensions(out int w, out int h, out int d);
        if (x < 0 || x >= w || y < 0 || y >= h)
            return false;

        uint[] data = Data;
        int wpl = Wpl;
        Span<uint> line = data.AsSpan(y * wpl);
        uint val;
        switch (d)
        {
        case 1:
            val = (uint)ArrayAccess.GetDataBit(line, x);
            if (val != 0)
                ArrayAccess.ClearDataBit(line, x);
            else
                ArrayAccess.SetDataBit(line, x);
            break;
        case 2:
            val = (uint)ArrayAccess.GetDataDibit(line, x);
            val ^= 0x3;
            ArrayAccess.SetDataDibit(line, x, (int)val);
            break;
        case 4:
            val = (uint)ArrayAccess.GetDataQbit(line, x);
            val ^= 0xf;
            ArrayAccess.SetDataQbit(line, x, (int)val);
            break;
        case 8:
            val = (uint)ArrayAccess.GetDataByte(line, x);
            val ^= 0xff;
            ArrayAccess.SetDataByte(line, x, (int)val);
            break;
        case 16:
            val = (uint)ArrayAccess.GetDataTwoBytes(line, x);
            val ^= 0xffff;
            ArrayAccess.SetDataTwoBytes(line, x, (int)val);
            break;
        case 32:
            val = line[x] ^ 0xffffffff;
            line[x] = val;
            break;
        default:
            throw Lept.Arg("pixFlipPixel", "depth must be in {1,2,4,8,16,32} bpp");
        }

        return true;
    }

    // leptonica: setPixelLow (pix2.c)
    /// <summary>Sets the pixel at x in a raster line.</summary>
    /// <param name="line">the raster line, starting at its first word</param>
    /// <param name="x">pixel location in line</param>
    /// <param name="depth">bpp</param>
    /// <param name="val">to be inserted</param>
    /// <remarks>(1) Caution: input variables are not checked!</remarks>
    public static void SetPixelLow(Span<uint> line, int x, int depth, uint val)
    {
        switch (depth)
        {
        case 1:
            if (val != 0)
                ArrayAccess.SetDataBit(line, x);
            else
                ArrayAccess.ClearDataBit(line, x);
            break;
        case 2:
            ArrayAccess.SetDataDibit(line, x, (int)val);
            break;
        case 4:
            ArrayAccess.SetDataQbit(line, x, (int)val);
            break;
        case 8:
            ArrayAccess.SetDataByte(line, x, (int)val);
            break;
        case 16:
            ArrayAccess.SetDataTwoBytes(line, x, (int)val);
            break;
        case 32:
            line[x] = val;
            break;
        default:
            CStdio.Stderr($"illegal depth in setPixelLow()\n");
            break;
        }
    }

    /*-------------------------------------------------------------*
     *                     Find black or white value               *
     *-------------------------------------------------------------*/
    // leptonica: pixGetBlackOrWhiteVal (pix2.c)
    /// <summary>Gets the pixel value of black or white.</summary>
    /// <param name="op">L_GET_BLACK_VAL, L_GET_WHITE_VAL</param>
    /// <returns>the pixel value</returns>
    /// <remarks>
    /// (1) Side effect.  For a colormapped image, if the requested
    ///     color is not present and there is room to add it in the cmap,
    ///     it is added and the new index is returned.  If there is no room,
    ///     the index of the closest color in intensity is returned.
    /// </remarks>
    public uint GetBlackOrWhiteVal(int op)
    {
        if (op != L.L_GET_BLACK_VAL && op != L.L_GET_WHITE_VAL)
            throw Lept.Arg("pixGetBlackOrWhiteVal", "invalid op");

        uint val;
        PixColormap? cmap = Colormap;
        int d = Depth;
        if (cmap == null) {
            if ((d == 1 && op == L.L_GET_WHITE_VAL) ||
                (d > 1 && op == L.L_GET_BLACK_VAL)) {  /* min val */
                val = 0;
            } else {  /* max val */
                val = (d == 32) ? 0xffffff00 : (uint)((1 << d) - 1);
            }
        } else {  /* handle colormap */
            int index;
            if (op == L.L_GET_BLACK_VAL)
                cmap.AddBlackOrWhite(0, out index);
            else  /* L_GET_WHITE_VAL */
                cmap.AddBlackOrWhite(1, out index);
            val = (uint)index;
        }

        return val;
    }

    /*-------------------------------------------------------------*
     *     Full image clear/set/set-to-arbitrary-value/invert      *
     *-------------------------------------------------------------*/
    // leptonica: pixClearAll (pix2.c)
    /// <summary>Clears all data to 0.</summary>
    /// <remarks>
    /// (1) Clears all data to 0.  For 1 bpp, this is white; for grayscale
    ///     or color, this is black.
    /// (2) Caution: for colormapped pix, this sets the color to the first
    ///     one in the colormap.  Be sure that this is the intended color!
    /// </remarks>
    public void ClearAll()
    {
        Array.Clear(Data, 0, Wpl * Height);
    }

    // leptonica: pixSetAll (pix2.c)
    /// <summary>Sets all data to 1.</summary>
    /// <remarks>
    /// (1) Sets all data to 1.  For 1 bpp, this is black; for grayscale
    ///     or color, this is white.
    /// (2) Caution: for colormapped pix, this sets the pixel value to the
    ///     maximum value supported by the colormap: 2^d - 1.  However, this
    ///     color may not be defined, because the colormap may not be full.
    /// </remarks>
    public void SetAll()
    {
        PixColormap? cmap = Colormap;
        if (cmap != null) {
            if (cmap.FreeCount > 0)  /* cmap is not full */
                throw Lept.Arg("pixSetAll", "cmap entry does not exist");
        }

        Array.Fill(Data, 0xffffffffu, 0, Wpl * Height);
    }

    // leptonica: pixSetAllGray (pix2.c)
    /// <summary>Sets all pixels to the given gray level.</summary>
    /// <param name="grayval">in range 0 ... 255</param>
    /// <remarks>
    /// (1) All depths, cmap ok.  N.B.  For all images, %grayval == 0 represents
    ///     black and %grayval == 255 represents white.
    /// (2) For depth &lt; 8, we do our best to approximate the gray level.
    ///     For 1 bpp images, any %grayval &lt; 128 is black; >= 128 is white.
    ///     For 32 bpp images, each r,g,b component is set to %grayval,
    ///     and the alpha component is preserved.
    /// (3) If pix is colormapped, it adds the gray value, replicated in
    ///     all components, to the colormap if it's not there and there
    ///     is room.  If the colormap is full, it finds the closest color in
    ///     L2 distance of components.  This index is written to all pixels.
    /// </remarks>
    public void SetAllGray(int grayval)
    {
        if (grayval < 0) {
            Log.Warning("pixSetAllGray", "grayval < 0; setting to 0");
            grayval = 0;
        } else if (grayval > 255) {
            Log.Warning("pixSetAllGray", "grayval > 255; setting to 255");
            grayval = 255;
        }

            /* Handle the colormap case */
        PixColormap? cmap = Colormap;
        if (cmap != null) {
            cmap.AddNearestColor(grayval, grayval, grayval, out int index);
            SetAllArbitrary((uint)index);
            return;
        }

            /* Non-cmapped */
        int d = Depth;
        int spp = Spp;
        uint val32;
        if (d == 1) {
            if (grayval < 128)  /* black */
                SetAll();
            else
                ClearAll();  /* white */
        } else if (d < 8) {
            grayval >>= 8 - d;
            SetAllArbitrary((uint)grayval);
        } else if (d == 8) {
            SetAllArbitrary((uint)grayval);
        } else if (d == 16) {
            grayval |= (grayval << 8);
            SetAllArbitrary((uint)grayval);
        } else if (d == 32 && spp == 3) {
            ComposeRGBPixel(grayval, grayval, grayval, out val32);
            SetAllArbitrary(val32);
        } else if (d == 32 && spp == 4) {
            Pix alpha = GetRGBComponent(L.L_ALPHA_CHANNEL);
            ComposeRGBPixel(grayval, grayval, grayval, out val32);
            SetAllArbitrary(val32);
            SetRGBComponent(alpha, L.L_ALPHA_CHANNEL);
        } else {
            Log.Error("pixSetAllGray", $"invalid depth: {d}");
            throw Lept.Arg("pixSetAllGray", "invalid depth");
        }
    }

    // leptonica: pixSetAllArbitrary (pix2.c)
    /// <summary>Sets all pixels to val.</summary>
    /// <param name="val">value to set all pixels</param>
    /// <remarks>
    /// (1) Caution 1!  For colormapped pix, %val is used as an index
    ///     into a colormap.  Be sure that index refers to the intended color.
    ///     If the color is not in the colormap, you should first add it
    ///     and then call this function.
    /// (2) Caution 2!  For 32 bpp pix, the interpretation of the LSB
    ///     of %val depends on whether spp == 3 (RGB) or spp == 4 (RGBA).
    ///     For RGB, the LSB is ignored in image transformations.
    ///     For RGBA, the LSB is interpreted as the alpha (transparency)
    ///     component; full transparency has alpha == 0x0, whereas
    ///     full opacity has alpha = 0xff.  An RGBA image with full
    ///     opacity behaves like an RGB image.
    /// (3) As an example of (2), suppose you want to initialize a 32 bpp
    ///     pix with partial opacity, say 0xee337788.  If the pix is 3 spp,
    ///     the 0x88 alpha component will be ignored and may be changed
    ///     in subsequent processing.  However, if the pix is 4 spp, the
    ///     alpha component will be retained and used. The function
    ///     pixCreate(w, h, 32) makes an RGB image by default, and
    ///     pixSetSpp(pix, 4) can be used to promote an RGB image to RGBA.
    /// </remarks>
    public void SetAllArbitrary(uint val)
    {
            /* If colormapped, make sure that val is less than the size
             * of the cmap array. */
        PixColormap? cmap = Colormap;
        if (cmap != null) {
            int n = cmap.Count;
            if (val >= (uint)n) {
                Log.Warning("pixSetAllArbitrary", "index not in colormap; using last color");
                val = (uint)(n - 1);
            }
        }

            /* Make sure val isn't too large for the pixel depth.
             * If it is too large, set the pixel color to white.  */
        GetDimensions(out int w, out int h, out int d);
        if (d < 32) {
            uint maxval = (uint)((1 << d) - 1);
            if (val > maxval) {
                Log.Warning("pixSetAllArbitrary",
                            $"val = {val} too large for depth; using maxval = {maxval}");
                val = maxval;
            }
        }

            /* Set up word to tile with */
        uint wordval = 0;
        int npix = 32 / d;    /* number of pixels per 32 bit word */
        for (int j = 0; j < npix; j++)
            wordval |= (val << (j * d));
        int wpl = Wpl;
        uint[] data = Data;
        for (int i = 0; i < h; i++) {
            int line = i * wpl;
            for (int j = 0; j < wpl; j++) {
                data[line + j] = wordval;
            }
        }
    }

    // leptonica: pixSetBlackOrWhite (pix2.c)
    /// <summary>Sets all pixels to either black or white.</summary>
    /// <param name="op">L_SET_BLACK, L_SET_WHITE</param>
    /// <remarks>
    /// (1) Function for setting all pixels in an image to either black
    ///     or white.
    /// (2) If pixs is colormapped, it adds black or white to the
    ///     colormap if it's not there and there is room.  If the colormap
    ///     is full, it finds the closest color in intensity.
    ///     This index is written to all pixels.
    /// </remarks>
    public void SetBlackOrWhite(int op)
    {
        if (op != L.L_SET_BLACK && op != L.L_SET_WHITE)
            throw Lept.Arg("pixSetBlackOrWhite", "invalid op");

        PixColormap? cmap = Colormap;
        int d = Depth;
        if (cmap == null) {
            if ((d == 1 && op == L.L_SET_BLACK) || (d > 1 && op == L.L_SET_WHITE))
                SetAll();
            else
                ClearAll();
        } else {  /* handle colormap */
            int index;
            if (op == L.L_SET_BLACK)
                cmap.AddBlackOrWhite(0, out index);
            else  /* L_SET_WHITE */
                cmap.AddBlackOrWhite(1, out index);
            SetAllArbitrary((uint)index);
        }
    }

    // leptonica: pixSetComponentArbitrary (pix2.c)
    /// <summary>Sets one color component of all pixels to val.</summary>
    /// <param name="comp">COLOR_RED, COLOR_GREEN, COLOR_BLUE, L_ALPHA_CHANNEL</param>
    /// <param name="val">value to set this component</param>
    /// <remarks>
    /// (1) pix must be 32 bpp.  For example, this can be used to set the
    ///     alpha component to opaque:
    ///         pixSetComponentArbitrary(pix, L_ALPHA_CHANNEL, 255)
    /// </remarks>
    public void SetComponentArbitrary(int comp, int val)
    {
        if (Depth != 32)
            throw Lept.Arg("pixSetComponentArbitrary", "pix not defined or not 32 bpp");
        if (comp != L.COLOR_RED && comp != L.COLOR_GREEN && comp != L.COLOR_BLUE &&
            comp != L.L_ALPHA_CHANNEL)
            throw Lept.Arg("pixSetComponentArbitrary", "invalid component");
        if (val < 0 || val > 255)
            throw Lept.Arg("pixSetComponentArbitrary", "val not in [0 ... 255]");

        uint mask1 = ~(uint)(255 << (8 * (3 - comp)));
        uint mask2 = (uint)(val << (8 * (3 - comp)));
        int nwords = Height * Wpl;
        uint[] data = Data;
        for (int i = 0; i < nwords; i++) {
            data[i] &= mask1;  /* clear out the component */
            data[i] |= mask2;  /* insert the new component value */
        }
    }

    /*-------------------------------------------------------------*
     *     Rectangular region clear/set/set-to-arbitrary-value     *
     *-------------------------------------------------------------*/
    // leptonica: pixClearInRect (pix2.c)
    /// <summary>Clears all data in the rectangle to 0.</summary>
    /// <param name="box">in which all pixels will be cleared</param>
    /// <remarks>
    /// (1) Clears all data in rect to 0.  For 1 bpp, this is white;
    ///     for grayscale or color, this is black.
    /// (2) Caution: for colormapped pix, this sets the color to the first
    ///     one in the colormap.  Be sure that this is the intended color!
    /// </remarks>
    public void ClearInRect(Box box)
    {
        box.GetGeometry(out int x, out int y, out int w, out int h);
        Rasterop(x, y, w, h, L.PIX_CLR, null, 0, 0);
    }

    // leptonica: pixSetInRect (pix2.c)
    /// <summary>Sets all data in the rectangle to 1.</summary>
    /// <param name="box">in which all pixels will be set</param>
    /// <remarks>
    /// (1) Sets all data in rect to 1.  For 1 bpp, this is black;
    ///     for grayscale or color, this is white.
    /// (2) Caution: for colormapped pix, this sets the pixel value to the
    ///     maximum value supported by the colormap: 2^d - 1.  However, this
    ///     color may not be defined, because the colormap may not be full.
    /// </remarks>
    public void SetInRect(Box box)
    {
        PixColormap? cmap = Colormap;
        if (cmap != null) {
            if (cmap.FreeCount > 0)  /* cmap is not full */
                throw Lept.Arg("pixSetInRect", "cmap entry does not exist");
        }

        box.GetGeometry(out int x, out int y, out int w, out int h);
        Rasterop(x, y, w, h, L.PIX_SET, null, 0, 0);
    }

    // leptonica: pixSetInRectArbitrary (pix2.c)
    /// <summary>Sets all pixels in the rectangle to val.</summary>
    /// <param name="box">in which all pixels will be set to val</param>
    /// <param name="val">value to set all pixels</param>
    /// <remarks>
    /// (1) For colormapped pix, be sure the value is the intended
    ///     one in the colormap.
    /// (2) Caution: for colormapped pix, this sets each pixel in the
    ///     rect to the color at the index equal to val.  Be sure that
    ///     this index exists in the colormap and that it is the intended one!
    /// </remarks>
    public void SetInRectArbitrary(Box box, uint val)
    {
        GetDimensions(out int w, out int h, out int d);
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 16 && d != 32)
            throw Lept.Arg("pixSetInRectArbitrary", "depth must be in {1,2,4,8,16,32} bpp");
        PixColormap? cmap = Colormap;
        if (cmap != null) {
            int n = cmap.Count;
            if (val >= (uint)n) {
                Log.Warning("pixSetInRectArbitrary", "index not in colormap; using last color");
                val = (uint)(n - 1);
            }
        }

        uint maxval = (d == 32) ? 0xffffff00 : (uint)((1 << d) - 1);
        if (val > maxval) val = maxval;

            /* Handle the simple cases: the min and max values */
        if (val == 0) {
            ClearInRect(box);
            return;
        }
        if (d == 1 ||
            (d == 2 && val == 3) ||
            (d == 4 && val == 0xf) ||
            (d == 8 && val == 0xff) ||
            (d == 16 && val == 0xffff) ||
            (d == 32 && ((val ^ 0xffffff00) >> 8 == 0))) {
            SetInRect(box);
            return;
        }

            /* Find the overlap of box with the input pix */
        Box boxc = box.ClipToRectangle(w, h);   /* throws if there is no overlap */
        boxc.GetGeometry(out int xstart, out int ystart, out int bw, out int bh);
        int xend = xstart + bw - 1;
        int yend = ystart + bh - 1;

        int wpl = Wpl;
        uint[] data = Data;
        for (int y = ystart; y <= yend; y++) {
            Span<uint> line = data.AsSpan(y * wpl);
            for (int x = xstart; x <= xend; x++) {
                switch (d)
                {
                case 2:
                    ArrayAccess.SetDataDibit(line, x, (int)val);
                    break;
                case 4:
                    ArrayAccess.SetDataQbit(line, x, (int)val);
                    break;
                case 8:
                    ArrayAccess.SetDataByte(line, x, (int)val);
                    break;
                case 16:
                    ArrayAccess.SetDataTwoBytes(line, x, (int)val);
                    break;
                case 32:
                    line[x] = val;
                    break;
                default:
                    throw Lept.Arg("pixSetInRectArbitrary", "depth not 2|4|8|16|32 bpp");
                }
            }
        }
    }

    // leptonica: pixBlendInRect (pix2.c)
    /// <summary>Blends a color with the pixels in a rectangle (in-place).</summary>
    /// <param name="box">[optional] in which all pixels will be blended</param>
    /// <param name="val">blend value; 0xrrggbb00</param>
    /// <param name="fract">fraction of color to be blended with each pixel in pixs</param>
    /// <remarks>
    /// (1) pixs must be 32 bpp rgb.  This is an in-place function.  It blends
    ///     the input color %val with the pixels in pixs in the specified
    ///     rectangle.  If no rectangle is specified, it blends over the entire image.
    /// </remarks>
    public void BlendInRect(Box? box, uint val, float fract)
    {
        if (Depth != 32)
            throw Lept.Arg("pixBlendInRect", "pixs not defined or not 32 bpp");

        ExtractRGBValues(val, out int rval, out int gval, out int bval);
        GetDimensions(out int w, out int h, out _);
        uint[] datas = Data;
        int wpls = Wpl;
        int prval, pgval, pbval;
        uint val32;
        if (box == null) {
            for (int i = 0; i < h; i++) {   /* scan over box */
                int lines = i * wpls;
                for (int j = 0; j < w; j++) {
                    val32 = datas[lines + j];
                    ExtractRGBValues(val32, out prval, out pgval, out pbval);
                    prval = (int)((1.0 - fract) * prval + fract * rval);
                    pgval = (int)((1.0 - fract) * pgval + fract * gval);
                    pbval = (int)((1.0 - fract) * pbval + fract * bval);
                    ComposeRGBPixel(prval, pgval, pbval, out val32);
                    datas[lines + j] = val32;
                }
            }
            return;
        }

        box.GetGeometry(out int bx, out int by, out int bw, out int bh);
        for (int i = 0; i < bh; i++) {   /* scan over box */
            if (by + i < 0 || by + i >= h) continue;
            int lines = (by + i) * wpls;
            for (int j = 0; j < bw; j++) {
                if (bx + j < 0 || bx + j >= w) continue;
                val32 = datas[lines + bx + j];
                ExtractRGBValues(val32, out prval, out pgval, out pbval);
                prval = (int)((1.0 - fract) * prval + fract * rval);
                pgval = (int)((1.0 - fract) * pgval + fract * gval);
                pbval = (int)((1.0 - fract) * pbval + fract * bval);
                ComposeRGBPixel(prval, pgval, pbval, out val32);
                datas[lines + bx + j] = val32;
            }
        }
    }

    /*-------------------------------------------------------------*
     *                         Set pad bits                        *
     *-------------------------------------------------------------*/
    // leptonica: pixSetPadBits (pix2.c)
    /// <summary>Sets the pad bits at the end of each line to val.</summary>
    /// <param name="val">0 or 1</param>
    /// <returns>false for 24 bpp, where pixels are not aligned with 32-bit words; true otherwise</returns>
    /// <remarks>
    /// (1) The pad bits are the bits that expand each scanline to a
    ///     multiple of 32 bits.  They are usually not used in
    ///     image processing operations.  When boundary conditions
    ///     are important, as in seedfill, they must be set properly.
    /// (2) This sets the value of the pad bits (if any) in the last
    ///     32-bit word in each scanline.
    /// (3) For 32 bpp pix, there are no pad bits, so this is a no-op.
    /// (4) For 24 bpp pix (which are not generally supported in leptonica),
    ///     this operation would affect image components because the pixels
    ///     are not aligned with 32-bit word boundaries.
    /// (5) When writing formatted output, such as tiff, png or jpeg,
    ///     the pad bits have no effect on the raster image that is
    ///     generated by reading back from the file.  However, in some
    ///     cases, the compressed file itself will depend on the pad
    ///     bits.  This is seen, for example, in Windows with 2 and 4 bpp
    ///     tiff-compressed images that have pad bits on each scanline.
    ///     It is sometimes convenient to use a golden file with a
    ///     byte-by-byte check to verify invariance.  Consequently,
    ///     and because setting the pad bits is cheap, the pad bits are
    ///     set to 0 before writing these compressed files.
    /// </remarks>
    public bool SetPadBits(int val)
    {
        GetDimensions(out int w, out int h, out int d);
        if (d == 32)  /* no padding exists for 32 bpp */
            return true;
        if (d == 24) {  /* pixels not aligned with 32-bit words */
            Log.Info("pixSetPadBits", "pix is 24 bpp");
            return false;
        }

        uint[] data = Data;
        int wpl = Wpl;
        int endbits = (int)(32 - (((long)w * d) % 32));
        if (endbits == 32)  /* no partial word */
            return true;
        int fullwords = (int)((1L * w * d) / 32);
        uint mask = rmask32[endbits];
        if (val == 0)
            mask = ~mask;

        for (int i = 0; i < h; i++) {
            int pword = i * wpl + fullwords;
            if (val == 0) /* clear */
                data[pword] = data[pword] & mask;
            else  /* set */
                data[pword] = data[pword] | mask;
        }
        return true;
    }

    // leptonica: pixSetPadBitsBand (pix2.c)
    /// <summary>Sets the pad bits at the end of each line in a band of lines to val.</summary>
    /// <param name="by">starting y value of band</param>
    /// <param name="bh">height of band</param>
    /// <param name="val">0 or 1</param>
    /// <returns>false for 24 bpp, where pixels are not aligned with 32-bit words; true otherwise</returns>
    /// <remarks>
    /// (1) The pad bits are the bits that expand each scanline to a
    ///     multiple of 32 bits.  They are usually not used in
    ///     image processing operations.  When boundary conditions
    ///     are important, as in seedfill, they must be set properly.
    /// (2) This sets the value of the pad bits (if any) in the last
    ///     32-bit word in each scanline, within the specified
    ///     band of raster lines.
    /// (3) For 32 bpp pix, there are no pad bits, so this is a no-op.
    ///     For 24 bpp pix, this function would change image components.
    /// </remarks>
    public bool SetPadBitsBand(int by, int bh, int val)
    {
        GetDimensions(out int w, out int h, out int d);
        if (d == 32)  /* no padding exists for 32 bpp */
            return true;
        if (d == 24) {  /* pixels not aligned with 32-bit words */
            Log.Info("pixSetPadBitsBand", "pix is 24 bpp");
            return false;
        }

        if (by < 0)
            by = 0;
        if (by >= h)
            throw Lept.Arg("pixSetPadBitsBand", "start y not in image");
        if (by + bh > h)
            bh = h - by;

        uint[] data = Data;
        int wpl = Wpl;
        int endbits = (int)(32 - (((long)w * d) % 32));
        if (endbits == 32)  /* no partial word */
            return true;
        int fullwords = (int)((long)w * d / 32);

        uint mask = rmask32[endbits];
        if (val == 0)
            mask = ~mask;

        for (int i = by; i < by + bh; i++) {
            int pword = i * wpl + fullwords;
            if (val == 0) /* clear */
                data[pword] = data[pword] & mask;
            else  /* set */
                data[pword] = data[pword] | mask;
        }

        return true;
    }

    /*-------------------------------------------------------------*
     *                       Set border pixels                     *
     *-------------------------------------------------------------*/
    // leptonica: pixSetOrClearBorder (pix2.c)
    /// <summary>Sets or clears all pixels in the border region.</summary>
    /// <param name="left">border region amount to set or clear: these distances are from outside</param>
    /// <param name="right">border region amount to set or clear</param>
    /// <param name="top">border region amount to set or clear</param>
    /// <param name="bot">border region amount to set or clear</param>
    /// <param name="op">operation PIX_SET or PIX_CLR</param>
    /// <remarks>
    /// (1) The border region is defined to be the region in the
    ///     image within a specific distance of each edge.  Here, we
    ///     allow the pixels within a specified distance of each
    ///     edge to be set independently.  This either sets or
    ///     clears all pixels in the border region.
    /// (2) For binary images, use PIX_SET for black and PIX_CLR for white.
    /// (3) For grayscale or color images, use PIX_SET for white
    ///     and PIX_CLR for black.
    /// </remarks>
    public void SetOrClearBorder(int left, int right, int top, int bot, int op)
    {
        if (op != L.PIX_SET && op != L.PIX_CLR)
            throw Lept.Arg("pixSetOrClearBorder", "op must be PIX_SET or PIX_CLR");

        GetDimensions(out int w, out int h, out _);
        Rasterop(0, 0, left, h, op, null, 0, 0);
        Rasterop(w - right, 0, right, h, op, null, 0, 0);
        Rasterop(0, 0, w, top, op, null, 0, 0);
        Rasterop(0, h - bot, w, bot, op, null, 0, 0);
    }

    // leptonica: pixSetBorderVal (pix2.c)
    /// <summary>Sets all pixels in the border region to val.</summary>
    /// <param name="left">border region amount to set: these distances are from outside</param>
    /// <param name="right">border region amount to set</param>
    /// <param name="top">border region amount to set</param>
    /// <param name="bot">border region amount to set</param>
    /// <param name="val">value to set at each border pixel</param>
    /// <remarks>
    /// (1) pix must be 8, 16 or 32 bpp.
    /// (2) The border region is defined to be the region in the
    ///     image within a specific distance of each edge.  Here, we
    ///     allow the pixels within a specified distance of each
    ///     edge to be set independently.  This sets the pixels
    ///     in the border region to the given input value.
    /// (3) For efficiency, use pixSetOrClearBorder() if
    ///     you're setting the border to either black or white.
    /// (4) If d != 32, the input value should be masked off
    ///     to the appropriate number of least significant bits.
    /// (5) The code is easily generalized for 2 or 4 bpp.
    /// </remarks>
    public void SetBorderVal(int left, int right, int top, int bot, uint val)
    {
        GetDimensions(out int w, out int h, out int d);
        if (d != 8 && d != 16 && d != 32)
            throw Lept.Arg("pixSetBorderVal", "depth must be 8, 16 or 32 bpp");

        uint[] datas = Data;
        int wpls = Wpl;
        int i, j, bstart, rstart;
        Span<uint> lines;
        if (d == 8) {
            val &= 0xff;
            for (i = 0; i < top; i++) {
                lines = datas.AsSpan(i * wpls);
                for (j = 0; j < w; j++)
                    ArrayAccess.SetDataByte(lines, j, (int)val);
            }
            rstart = w - right;
            bstart = h - bot;
            for (i = top; i < bstart; i++) {
                lines = datas.AsSpan(i * wpls);
                for (j = 0; j < left; j++)
                    ArrayAccess.SetDataByte(lines, j, (int)val);
                for (j = rstart; j < w; j++)
                    ArrayAccess.SetDataByte(lines, j, (int)val);
            }
            for (i = bstart; i < h; i++) {
                lines = datas.AsSpan(i * wpls);
                for (j = 0; j < w; j++)
                    ArrayAccess.SetDataByte(lines, j, (int)val);
            }
        } else if (d == 16) {
            val &= 0xffff;
            for (i = 0; i < top; i++) {
                lines = datas.AsSpan(i * wpls);
                for (j = 0; j < w; j++)
                    ArrayAccess.SetDataTwoBytes(lines, j, (int)val);
            }
            rstart = w - right;
            bstart = h - bot;
            for (i = top; i < bstart; i++) {
                lines = datas.AsSpan(i * wpls);
                for (j = 0; j < left; j++)
                    ArrayAccess.SetDataTwoBytes(lines, j, (int)val);
                for (j = rstart; j < w; j++)
                    ArrayAccess.SetDataTwoBytes(lines, j, (int)val);
            }
            for (i = bstart; i < h; i++) {
                lines = datas.AsSpan(i * wpls);
                for (j = 0; j < w; j++)
                    ArrayAccess.SetDataTwoBytes(lines, j, (int)val);
            }
        } else {   /* d == 32 */
            for (i = 0; i < top; i++) {
                lines = datas.AsSpan(i * wpls);
                for (j = 0; j < w; j++)
                    lines[j] = val;
            }
            rstart = w - right;
            bstart = h - bot;
            for (i = top; i < bstart; i++) {
                lines = datas.AsSpan(i * wpls);
                for (j = 0; j < left; j++)
                    lines[j] = val;
                for (j = rstart; j < w; j++)
                    lines[j] = val;
            }
            for (i = bstart; i < h; i++) {
                lines = datas.AsSpan(i * wpls);
                for (j = 0; j < w; j++)
                    lines[j] = val;
            }
        }
    }

    // leptonica: pixSetBorderRingVal (pix2.c)
    /// <summary>Sets all pixels in a one-pixel-wide ring at a given distance from the edge to val.</summary>
    /// <param name="dist">distance from outside; must be &gt; 0; first ring is 1</param>
    /// <param name="val">value to set at each border pixel</param>
    /// <remarks>
    /// (1) Any depth; cmap OK.  The rings are single-pixel-wide rectangular
    ///     sets of pixels at a given distance from the edge of the pix.
    ///     This sets all pixels in a given ring to a value.
    /// </remarks>
    public void SetBorderRingVal(int dist, uint val)
    {
        if (dist < 1)
            throw Lept.Arg("pixSetBorderRingVal", "dist must be > 0");
        GetDimensions(out int w, out int h, out int d);
        if (w < 2 * dist + 1 || h < 2 * dist + 1)
            throw Lept.Arg("pixSetBorderRingVal", "ring doesn't exist");
        if (d < 32 && (val >= (uint)(1 << d)))
            throw Lept.Arg("pixSetBorderRingVal", "invalid pixel value");

        int xend = w - dist;
        int yend = h - dist;
        for (int j = dist - 1; j <= xend; j++)
            SetPixel(j, dist - 1, val);
        for (int j = dist - 1; j <= xend; j++)
            SetPixel(j, yend, val);
        for (int i = dist - 1; i <= yend; i++)
            SetPixel(dist - 1, i, val);
        for (int i = dist - 1; i <= yend; i++)
            SetPixel(xend, i, val);
    }

    // leptonica: pixSetMirroredBorder (pix2.c)
    /// <summary>Sets the border pixels by mirroring the pixels near the border (in-place).</summary>
    /// <param name="left">number of pixels to set</param>
    /// <param name="right">number of pixels to set</param>
    /// <param name="top">number of pixels to set</param>
    /// <param name="bot">number of pixels to set</param>
    /// <remarks>
    /// (1) All depths; colormap ok.  This applies what is effectively mirror
    ///     boundary conditions to a border region in the image.  It is in-place.
    /// (2) This is useful for setting pixels near the border to a
    ///     value representative of the near pixels to the interior.
    /// (3) The general pixRasterop() is used for an in-place operation here
    ///     because there is no overlap between the src and dest rectangles.
    /// </remarks>
    public void SetMirroredBorder(int left, int right, int top, int bot)
    {
        GetDimensions(out int w, out int h, out _);
        for (int j = 0; j < left; j++)
            Rasterop(left - 1 - j, top, 1, h - top - bot, L.PIX_SRC,
                     this, left + j, top);
        for (int j = 0; j < right; j++)
            Rasterop(w - right + j, top, 1, h - top - bot, L.PIX_SRC,
                     this, w - right - 1 - j, top);
        for (int i = 0; i < top; i++)
            Rasterop(0, top - 1 - i, w, 1, L.PIX_SRC,
                     this, 0, top + i);
        for (int i = 0; i < bot; i++)
            Rasterop(0, h - bot + i, w, 1, L.PIX_SRC,
                     this, 0, h - bot - 1 - i);
    }

    // leptonica: pixCopyBorder (pix2.c)
    /// <summary>Copies the border pixels of pixs to pixd.</summary>
    /// <param name="pixd">all depths; colormap ok; can be null</param>
    /// <param name="pixs">same depth and size as pixd</param>
    /// <param name="left">number of pixels to copy</param>
    /// <param name="right">number of pixels to copy</param>
    /// <param name="top">number of pixels to copy</param>
    /// <param name="bot">number of pixels to copy</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) pixd can be null, but otherwise it must be the same size
    ///     and depth as pixs.  Always returns pixd.
    /// (2) This is useful in situations where by setting a few border
    ///     pixels we can avoid having to copy all pixels in pixs into
    ///     pixd as an initialization step for some operation.
    ///     Nevertheless, for safety, if making a new pixd, all the
    ///     non-border pixels are initialized to 0.
    /// </remarks>
    public static Pix CopyBorder(Pix? pixd, Pix pixs, int left, int right, int top, int bot)
    {
        if (pixd != null) {
            if (pixd == pixs) {
                Log.Warning("pixCopyBorder", "same: nothing to do");
                return pixd;
            } else if (!SizesEqual(pixs, pixd)) {
                throw Lept.Arg("pixCopyBorder", "pixs and pixd sizes differ");
            }
        } else {
            pixd = CreateTemplate(pixs);
        }

        pixs.GetDimensions(out int w, out int h, out _);
        pixd.Rasterop(0, 0, left, h, L.PIX_SRC, pixs, 0, 0);
        pixd.Rasterop(w - right, 0, right, h, L.PIX_SRC, pixs, w - right, 0);
        pixd.Rasterop(0, 0, w, top, L.PIX_SRC, pixs, 0, 0);
        pixd.Rasterop(0, h - bot, w, bot, L.PIX_SRC, pixs, 0, h - bot);
        return pixd;
    }

    /*-------------------------------------------------------------*
     *                     Add and remove border                   *
     *-------------------------------------------------------------*/
    // leptonica: pixAddBorder (pix2.c)
    /// <summary>Adds a border of npix pixels with value val on each side.</summary>
    /// <param name="npix">number of pixels to be added to each side</param>
    /// <param name="val">value of added border pixels</param>
    /// <returns>pixd with the added exterior pixels (the same pix if npix == 0)</returns>
    /// <remarks>
    /// (1) All depths; colormap ok.
    /// (2) See pixGetBlackOrWhiteVal() for values of black and white pixels.
    /// </remarks>
    public Pix AddBorder(int npix, uint val)
    {
        if (npix == 0)
            return Clone(this);
        return AddBorderGeneral(npix, npix, npix, npix, val);
    }

    // leptonica: pixAddBlackOrWhiteBorder (pix2.c)
    /// <summary>Adds a black or white border.</summary>
    /// <param name="left">number of pixels added</param>
    /// <param name="right">number of pixels added</param>
    /// <param name="top">number of pixels added</param>
    /// <param name="bot">number of pixels added</param>
    /// <param name="op">L_GET_BLACK_VAL, L_GET_WHITE_VAL</param>
    /// <returns>pixd with the added exterior pixels</returns>
    /// <remarks>
    /// (1) All depths; colormap ok.  See pixGetBlackOrWhiteVal() for possible
    ///     side effect (adding a color to a colormap).
    /// (2) The only complication is that pixs may have a colormap.
    ///     There are two ways to add the black or white border:
    ///     (a) As done here (simplest, most efficient)
    ///     (b) l_int32 ws, hs, d;
    ///         pixGetDimensions(pixs, &amp;ws, &amp;hs, &amp;d);
    ///         Pix *pixd = pixCreate(ws + left + right, hs + top + bot, d);
    ///         PixColormap *cmap = pixGetColormap(pixs);
    ///         if (cmap != NULL)
    ///             pixSetColormap(pixd, pixcmapCopy(cmap));
    ///         pixSetBlackOrWhite(pixd, L_SET_WHITE);  // uses cmap
    ///         pixRasterop(pixd, left, top, ws, hs, PIX_SET, pixs, 0, 0);
    /// </remarks>
    public Pix AddBlackOrWhiteBorder(int left, int right, int top, int bot, int op)
    {
        if (op != L.L_GET_BLACK_VAL && op != L.L_GET_WHITE_VAL)
            throw Lept.Arg("pixAddBlackOrWhiteBorder", "invalid op");

        uint val = GetBlackOrWhiteVal(op);
        return AddBorderGeneral(left, right, top, bot, val);
    }

    // leptonica: pixAddBorderGeneral (pix2.c)
    /// <summary>Adds a border with value val, with independent widths on each side.</summary>
    /// <param name="left">number of pixels added</param>
    /// <param name="right">number of pixels added</param>
    /// <param name="top">number of pixels added</param>
    /// <param name="bot">number of pixels added</param>
    /// <param name="val">value of added border pixels</param>
    /// <returns>pixd with the added exterior pixels</returns>
    /// <remarks>
    /// (1) All depths; colormap ok.  For binary images:
    ///        white:  val = 0
    ///        black:  val = 1
    ///     For grayscale images:
    ///        white:  val = 2 ** d - 1
    ///        black:  val = 0
    ///     For rgb color images:
    ///        white:  val = 0xffffff00
    ///        black:  val = 0
    ///     For colormapped images, set val to the appropriate colormap index.
    /// (2) If the added border is either black or white, you can use
    ///        pixAddBlackOrWhiteBorder()
    ///     The black and white values for all images can be found with
    ///        pixGetBlackOrWhiteVal()
    ///     which, if pixs is cmapped, may add an entry to the colormap.
    ///     Alternatively, if pixs has a colormap, you can find the index
    ///     of the pixel whose intensity is closest to white or black:
    ///        white: pixcmapGetRankIntensity(cmap, 1.0, &amp;index);
    ///        black: pixcmapGetRankIntensity(cmap, 0.0, &amp;index);
    ///     and use that for val.
    /// </remarks>
    public Pix AddBorderGeneral(int left, int right, int top, int bot, uint val)
    {
        if (left < 0 || right < 0 || top < 0 || bot < 0)
            throw Lept.Arg("pixAddBorderGeneral", "negative border added!");

        GetDimensions(out int ws, out int hs, out int d);
        int wd = ws + left + right;
        int hd = hs + top + bot;
        Pix pixd = Create(wd, hd, d);
        pixd.CopyResolution(this);
        pixd.CopyColormap(this);

            /* Set the new border pixels */
        uint maxval = (d == 32) ? 0xffffff00 : (uint)((1 << d) - 1);
        int op = L.UNDEF;
        if (val == 0)
            op = L.PIX_CLR;
        else if (val >= maxval)
            op = L.PIX_SET;
        if (op == L.UNDEF) {
            pixd.SetAllArbitrary(val);
        } else {  /* just set or clear the border pixels */
            pixd.Rasterop(0, 0, left, hd, op, null, 0, 0);
            pixd.Rasterop(wd - right, 0, right, hd, op, null, 0, 0);
            pixd.Rasterop(0, 0, wd, top, op, null, 0, 0);
            pixd.Rasterop(0, hd - bot, wd, bot, op, null, 0, 0);
        }

            /* Copy pixs into the interior */
        pixd.Rasterop(left, top, ws, hs, L.PIX_SRC, this, 0, 0);
        return pixd;
    }

    // leptonica: pixAddMultipleBlackWhiteBorders (pix2.c)
    /// <summary>Adds up to 3 black and 3 white borders, alternating.</summary>
    /// <param name="nblack1">width of first black border</param>
    /// <param name="nwhite1">width of first white border</param>
    /// <param name="nblack2">width of second black border</param>
    /// <param name="nwhite2">width of second white border</param>
    /// <param name="nblack3">width of third black border</param>
    /// <param name="nwhite3">width of third white border</param>
    /// <returns>pixd with the added borders</returns>
    /// <remarks>
    /// (1) All depths; colormap ok.  This is a convenience function for adding
    ///     up to 3 black and 3 white borders, alternating black and white.
    /// (2) Each of the 6 args gives the width of the next border, starting
    ///     with a black border.  Any of the args can be 0, skipping
    ///     the addition of that border.
    /// (3) Maximum allowed border width is 500 for any border.
    /// </remarks>
    public Pix AddMultipleBlackWhiteBorders(int nblack1, int nwhite1, int nblack2,
                                            int nwhite2, int nblack3, int nwhite3)
    {
        int[] w = new int[6];
        w[0] = nblack1;
        w[1] = nwhite1;
        w[2] = nblack2;
        w[3] = nwhite2;
        w[4] = nblack3;
        w[5] = nwhite3;
        Pix pixd = Clone(this);
        for (int i = 0; i < 6; i++) {
            if (w[i] > 500)
                Log.Warning("pixAddMultipleBlackWhiteBorders", $"w = {w[i]} > 500; skipping");
            if (w[i] > 0 && w[i] <= 500) {
                int color = (i % 2 == 0) ? L.L_GET_BLACK_VAL : L.L_GET_WHITE_VAL;
                Pix pix1 = pixd.AddBlackOrWhiteBorder(w[i], w[i], w[i], w[i], color);
                pixd = pix1;
            }
        }

        return pixd;
    }

    // leptonica: pixRemoveBorder (pix2.c)
    /// <summary>Removes npix pixels from each of the 4 sides.</summary>
    /// <param name="npix">number to be removed from each of the 4 sides</param>
    /// <returns>pixd with pixels removed around border (the same pix if npix == 0)</returns>
    /// <remarks>All depths; colormap ok.</remarks>
    public Pix RemoveBorder(int npix)
    {
        if (npix == 0)
            return Clone(this);
        return RemoveBorderGeneral(npix, npix, npix, npix);
    }

    // leptonica: pixRemoveBorderGeneral (pix2.c)
    /// <summary>Removes the given number of pixels from each side.</summary>
    /// <param name="left">number of pixels removed</param>
    /// <param name="right">number of pixels removed</param>
    /// <param name="top">number of pixels removed</param>
    /// <param name="bot">number of pixels removed</param>
    /// <returns>pixd with pixels removed around border</returns>
    /// <remarks>All depths; colormap ok.</remarks>
    public Pix RemoveBorderGeneral(int left, int right, int top, int bot)
    {
        if (left < 0 || right < 0 || top < 0 || bot < 0)
            throw Lept.Arg("pixRemoveBorderGeneral", "negative border removed!");

        GetDimensions(out int ws, out int hs, out int d);
        int wd = ws - left - right;
        int hd = hs - top - bot;
        if (wd <= 0)
            throw Lept.Arg("pixRemoveBorderGeneral", "width must be > 0");
        if (hd <= 0)
            throw Lept.Arg("pixRemoveBorderGeneral", "height must be > 0");
        Pix pixd = Create(wd, hd, d);
        pixd.CopyResolution(this);
        pixd.CopySpp(this);
        pixd.CopyColormap(this);

        pixd.Rasterop(0, 0, wd, hd, L.PIX_SRC, this, left, top);
        if (Depth == 32 && Spp == 4)
            pixd.ShiftAndTransferAlpha(this, -left, -top);
        return pixd;
    }

    // leptonica: pixRemoveBorderToSize (pix2.c)
    /// <summary>Removes pixels as evenly as possible from the sides, leaving the central part.</summary>
    /// <param name="wd">target width; use 0 if only removing from height</param>
    /// <param name="hd">target height; use 0 if only removing from width</param>
    /// <returns>pixd with pixels removed around border</returns>
    /// <remarks>
    /// (1) All depths; colormap ok.  Removes pixels as evenly as possible from
    ///     the sides of the image, leaving the central part.
    /// (2) Returns clone if no pixels requested removed, or the target
    ///     sizes are larger than the image.
    /// </remarks>
    public Pix RemoveBorderToSize(int wd, int hd)
    {
        GetDimensions(out int w, out int h, out _);
        if ((wd <= 0 || wd >= w) && (hd <= 0 || hd >= h))
            return Clone(this);

        int left = (w - wd) / 2;
        int right = left;
        int delta = w - 2 * left - wd;
        right += delta;
        int top = (h - hd) / 2;
        int bot = top;
        delta = h - hd - 2 * top;
        bot += delta;
        if (wd <= 0 || wd > w)
            left = right = 0;
        else if (hd <= 0 || hd > h)
            top = bot = 0;

        return RemoveBorderGeneral(left, right, top, bot);
    }

    // leptonica: pixAddMirroredBorder (pix2.c)
    /// <summary>Adds a border whose pixels are mirror copies of the pixels near the edges.</summary>
    /// <param name="left">number of pixels added</param>
    /// <param name="right">number of pixels added</param>
    /// <param name="top">number of pixels added</param>
    /// <param name="bot">number of pixels added</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) All depths; colormap ok.  This applies what is effectively mirror
    ///     boundary conditions.  For the added border pixels in pixd, the
    ///     pixels in pixs near the border are mirror-copied into the border region.
    /// (2) This is useful for avoiding special operations near
    ///     boundaries when doing image processing operations
    ///     such as rank filters and convolution.  In use, one first
    ///     adds mirrored pixels to each side of the image.  The number
    ///     of pixels added on each side is half the filter dimension.
    ///     Then the image processing operations proceed over a
    ///     region equal to the size of the original image, and
    ///     write directly into a dest pix of the same size as pixs.
    /// (3) The general pixRasterop() is used for an in-place operation here
    ///     because there is no overlap between the src and dest rectangles.
    /// </remarks>
    public Pix AddMirroredBorder(int left, int right, int top, int bot)
    {
        GetDimensions(out int w, out int h, out _);
        if (left > w || right > w || top > h || bot > h)
            throw Lept.Arg("pixAddMirroredBorder", "border too large");

            /* Set pixels on left, right, top and bottom, in that order */
        Pix pixd = AddBorderGeneral(left, right, top, bot, 0);
        for (int j = 0; j < left; j++)
            pixd.Rasterop(left - 1 - j, top, 1, h, L.PIX_SRC,
                          pixd, left + j, top);
        for (int j = 0; j < right; j++)
            pixd.Rasterop(left + w + j, top, 1, h, L.PIX_SRC,
                          pixd, left + w - 1 - j, top);
        for (int i = 0; i < top; i++)
            pixd.Rasterop(0, top - 1 - i, left + w + right, 1, L.PIX_SRC,
                          pixd, 0, top + i);
        for (int i = 0; i < bot; i++)
            pixd.Rasterop(0, top + h + i, left + w + right, 1, L.PIX_SRC,
                          pixd, 0, top + h - 1 - i);

        return pixd;
    }

    // leptonica: pixAddRepeatedBorder (pix2.c)
    /// <summary>Adds a border as if the image were tiled over the plane.</summary>
    /// <param name="left">number of pixels added</param>
    /// <param name="right">number of pixels added</param>
    /// <param name="top">number of pixels added</param>
    /// <param name="bot">number of pixels added</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) All depths; colormap ok.  This applies a repeated border, as if
    ///     the central part of the image is tiled over the plane.  So, for
    ///     example, the pixels in the left border come from the right side
    ///     of the image.
    /// (2) The general pixRasterop() is used for an in-place operation here
    ///     because there is no overlap between the src and dest rectangles.
    /// </remarks>
    public Pix AddRepeatedBorder(int left, int right, int top, int bot)
    {
        GetDimensions(out int w, out int h, out _);
        if (left > w || right > w || top > h || bot > h)
            throw Lept.Arg("pixAddRepeatedBorder", "border too large");

        Pix pixd = AddBorderGeneral(left, right, top, bot, 0);

            /* Set pixels on left, right, top and bottom, in that order */
        pixd.Rasterop(0, top, left, h, L.PIX_SRC, pixd, w, top);
        pixd.Rasterop(left + w, top, right, h, L.PIX_SRC, pixd, left, top);
        pixd.Rasterop(0, 0, left + w + right, top, L.PIX_SRC, pixd, 0, h);
        pixd.Rasterop(0, top + h, left + w + right, bot, L.PIX_SRC, pixd, 0, top);

        return pixd;
    }

    // leptonica: pixAddMixedBorder (pix2.c)
    /// <summary>Adds a border with mirrored pixels horizontally and repeated pixels vertically.</summary>
    /// <param name="left">number of pixels added</param>
    /// <param name="right">number of pixels added</param>
    /// <param name="top">number of pixels added</param>
    /// <param name="bot">number of pixels added</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) All depths; colormap ok.  This applies mirrored boundary
    ///     conditions (b.c.) horizontally and repeated b.c. vertically.
    /// (2) It is specifically used for avoiding special operations
    ///     near boundaries when convolving a hue-saturation histogram
    ///     with a given window size.  The repeated b.c. are used
    ///     vertically for hue, and the mirrored b.c. are used
    ///     horizontally for saturation.  The number of pixels added
    ///     on each side is approximately (but not quite) half the
    ///     filter dimension.  The image processing operations can
    ///     then proceed over a region equal to the size of the original
    ///     image, and write directly into a dest pix of the same
    ///     size as pixs.
    /// (3) The general pixRasterop() can be used for an in-place
    ///     operation here because there is no overlap between the
    ///     src and dest rectangles.
    /// </remarks>
    public Pix AddMixedBorder(int left, int right, int top, int bot)
    {
        GetDimensions(out int w, out int h, out _);
        if (left > w || right > w || top > h || bot > h)
            throw Lept.Arg("pixAddMixedBorder", "border too large");

            /* Set mirrored pixels on left and right;
             * then set repeated pixels on top and bottom. */
        Pix pixd = AddBorderGeneral(left, right, top, bot, 0);
        for (int j = 0; j < left; j++)
            pixd.Rasterop(left - 1 - j, top, 1, h, L.PIX_SRC,
                          pixd, left + j, top);
        for (int j = 0; j < right; j++)
            pixd.Rasterop(left + w + j, top, 1, h, L.PIX_SRC,
                          pixd, left + w - 1 - j, top);
        pixd.Rasterop(0, 0, left + w + right, top, L.PIX_SRC, pixd, 0, h);
        pixd.Rasterop(0, top + h, left + w + right, bot, L.PIX_SRC, pixd, 0, top);

        return pixd;
    }

    // leptonica: pixAddContinuedBorder (pix2.c)
    /// <summary>Adds a border whose pixels equal the closest boundary pixel.</summary>
    /// <param name="left">pixels on each side to be added</param>
    /// <param name="right">pixels on each side to be added</param>
    /// <param name="top">pixels on each side to be added</param>
    /// <param name="bot">pixels on each side to be added</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) All depths; colormap ok.  This adds pixels on each side whose
    ///     values are equal to the value on the closest boundary pixel.
    /// </remarks>
    public Pix AddContinuedBorder(int left, int right, int top, int bot)
    {
        Pix pixd = AddBorderGeneral(left, right, top, bot, 0);
        GetDimensions(out int w, out int h, out _);
        for (int j = 0; j < left; j++)
            pixd.Rasterop(j, top, 1, h, L.PIX_SRC, pixd, left, top);
        for (int j = 0; j < right; j++)
            pixd.Rasterop(left + w + j, top, 1, h,
                          L.PIX_SRC, pixd, left + w - 1, top);
        for (int i = 0; i < top; i++)
            pixd.Rasterop(0, i, left + w + right, 1, L.PIX_SRC, pixd, 0, top);
        for (int i = 0; i < bot; i++)
            pixd.Rasterop(0, top + h + i, left + w + right, 1,
                          L.PIX_SRC, pixd, 0, top + h - 1);

        return pixd;
    }

    /*-------------------------------------------------------------------*
     *                     Helper functions using alpha                  *
     *-------------------------------------------------------------------*/
    // leptonica: pixShiftAndTransferAlpha (pix2.c)
    /// <summary>Transfers the alpha channel of pixs to this pix, shifted.</summary>
    /// <param name="pixs">32 bpp, spp == 4</param>
    /// <param name="shiftx">shift of the alpha channel in x</param>
    /// <param name="shifty">shift of the alpha channel in y</param>
    /// <remarks>This pix (pixd) must be 32 bpp.</remarks>
    public void ShiftAndTransferAlpha(Pix pixs, float shiftx, float shifty)
    {
        if (pixs.Depth != 32 || pixs.Spp != 4)
            throw Lept.Arg("pixShiftAndTransferAlpha", "pixs not 32 bpp and 4 spp");
        if (Depth != 32)
            throw Lept.Arg("pixShiftAndTransferAlpha", "pixd not 32 bpp");

        if (shiftx == 0 && shifty == 0) {
            CopyRGBComponent(pixs, L.L_ALPHA_CHANNEL);
            return;
        }

        Pix pix1 = pixs.GetRGBComponent(L.L_ALPHA_CHANNEL);
        GetDimensions(out int w, out int h, out _);
        Pix pix2 = Create(w, h, 8);
        pix2.Rasterop(0, 0, w, h, L.PIX_SRC, pix1, (int)(-shiftx), (int)(-shifty));
        SetRGBComponent(pix2, L.L_ALPHA_CHANNEL);
    }

    // leptonica: pixDisplayLayersRGBA (pix2.c)
    /// <summary>Shows the rgb values, the alpha layer and the image over a background color.</summary>
    /// <param name="val">32 bit unsigned color to use as background</param>
    /// <param name="maxw">max output image width; 0 for no scaling</param>
    /// <returns>pixd showing various image views</returns>
    /// <remarks>
    /// (1) pixs is cmap or 32 bpp rgba.  Use %val == 0xffffff00 for white background.
    /// (2) Three views are given:
    ///      ~ the image with a fully opaque alpha
    ///      ~ the alpha layer
    ///      ~ the image as it would appear with a white background.
    /// </remarks>
    public Pix DisplayLayersRGBA(uint val, int maxw)
    {
        PixColormap? cmap = Colormap;
        if (cmap == null && !(Depth == 32 && Spp == 4))
            throw Lept.Arg("pixDisplayLayersRGBA", "pixs not cmap and not 32 bpp rgba");
        int w = Width;
        if (w == 0)
            throw Lept.Arg("pixDisplayLayersRGBA", "pixs width 0 !!");

        Pix pix1;
        if (cmap != null)
            pix1 = RemoveColormap(L.REMOVE_CMAP_WITH_ALPHA);
        else
            pix1 = Copy(null, this);

            /* Scale if necessary so the output width is not larger than maxw */
        float scalefact = (maxw == 0) ? 1.0f : Math.Min(1.0f, (float)(maxw) / w);
        int width = (int)(scalefact * w);

        Pixa pixa = Pixa.Create(3);
        pix1.Spp = 3;
        pixa.AddPix(pix1, L.L_INSERT);  /* show the rgb values */
        pix1 = GetRGBComponent(L.L_ALPHA_CHANNEL);
        Pix pix2 = pix1.ConvertTo32();
        pixa.AddPix(pix2, L.L_INSERT);  /* show the alpha channel */
        pix1 = AlphaBlendUniform(val & 0xffffff00);
        pixa.AddPix(pix1, L.L_INSERT);  /* with %val color bg showing */
        Pix pixd = pixa.DisplayTiledInRows(32, width, scalefact, 0, 25, 2);
        return pixd;
    }

    /*-------------------------------------------------------------*
     *                Color sample setting and extraction          *
     *-------------------------------------------------------------*/
    // leptonica: pixCreateRGBImage (pix2.c)
    /// <summary>Makes a 32 bpp image from three 8 bpp component images.</summary>
    /// <param name="pixr">8 bpp red pix</param>
    /// <param name="pixg">8 bpp green pix</param>
    /// <param name="pixb">8 bpp blue pix</param>
    /// <returns>32 bpp pix, interleaved with 4 samples/pixel</returns>
    /// <remarks>
    /// (1) the 4th byte, sometimes called the "alpha channel",
    ///     and which is often used for blending between different
    ///     images, is left with 0 value (fully opaque).
    /// (2) see Note (4) in pix.h for details on storage of
    ///     8-bit samples within each 32-bit word.
    /// (3) This implementation, setting the r, g and b components
    ///     sequentially, is much faster than setting them in parallel
    ///     by constructing an RGB dest pixel and writing it to dest.
    ///     The reason is there are many more cache misses when reading
    ///     from 3 input images simultaneously.
    /// </remarks>
    public static Pix CreateRGBImage(Pix pixr, Pix pixg, Pix pixb)
    {
        pixr.GetDimensions(out int wr, out int hr, out int dr);
        pixg.GetDimensions(out int wg, out int hg, out int dg);
        pixb.GetDimensions(out int wb, out int hb, out int db);
        if (dr != 8 || dg != 8 || db != 8)
            throw Lept.Arg("pixCreateRGBImage", "input pix not all 8 bpp");
        if (wr != wg || wr != wb)
            throw Lept.Arg("pixCreateRGBImage", "widths not the same");
        if (hr != hg || hr != hb)
            throw Lept.Arg("pixCreateRGBImage", "heights not the same");

        Pix pixd = Create(wr, hr, 32);
        pixd.CopyResolution(pixr);
        pixd.SetRGBComponent(pixr, L.COLOR_RED);
        pixd.SetRGBComponent(pixg, L.COLOR_GREEN);
        pixd.SetRGBComponent(pixb, L.COLOR_BLUE);

        return pixd;
    }

    // leptonica: pixGetRGBComponent (pix2.c)
    /// <summary>Extracts one component of a 32 bpp (or colormapped) image as an 8 bpp image.</summary>
    /// <param name="comp">one of {COLOR_RED, COLOR_GREEN, COLOR_BLUE, L_ALPHA_CHANNEL}</param>
    /// <returns>the selected 8 bpp component image of the input 32 bpp image</returns>
    /// <remarks>
    /// (1) Three calls to this function generate the r, g and b 8 bpp
    ///     component images.  This is much faster than generating the
    ///     three images in parallel, by extracting a src pixel and setting
    ///     the pixels of each component image from it.  The reason is
    ///     there are many more cache misses when writing to three
    ///     output images simultaneously.
    /// </remarks>
    public Pix GetRGBComponent(int comp)
    {
        if (Colormap != null)
            return GetRGBComponentCmap(comp);
        if (Depth != 32)
            throw Lept.Arg("pixGetRGBComponent", "pixs not 32 bpp");
        if (comp != L.COLOR_RED && comp != L.COLOR_GREEN &&
            comp != L.COLOR_BLUE && comp != L.L_ALPHA_CHANNEL)
            throw Lept.Arg("pixGetRGBComponent", "invalid comp");

        GetDimensions(out int w, out int h, out _);
        Pix pixd = Create(w, h, 8);
        pixd.CopyResolution(this);
        int wpls = Wpl;
        int wpld = pixd.Wpl;
        uint[] datas = Data;
        uint[] datad = pixd.Data;
        bool simd = Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian && w >= 16;
        int shift = 8 * (3 - comp);
        for (int i = 0; i < h; i++) {
            int lines = i * wpls;
            Span<uint> lined = datad.AsSpan(i * wpld);
            int js = 0;
            if (simd) {
                    /* 16 pixels per step: shift down, narrow to bytes, then put
                     * the bytes in little-endian order inside each dest word. */
                ref uint ps = ref datas[lines];
                ref byte pd = ref MemoryMarshal.GetReference(MemoryMarshal.AsBytes(lined));
                var rev = Vector128.Create((byte)3, 2, 1, 0, 7, 6, 5, 4, 11, 10, 9, 8, 15, 14, 13, 12);
                var m = Vector128.Create(0xffu);
                for (; js + 16 <= w; js += 16) {
                    var a = Vector128.ShiftRightLogical(Vector128.LoadUnsafe(ref ps, (nuint)js), shift) & m;
                    var b = Vector128.ShiftRightLogical(Vector128.LoadUnsafe(ref ps, (nuint)(js + 4)), shift) & m;
                    var c = Vector128.ShiftRightLogical(Vector128.LoadUnsafe(ref ps, (nuint)(js + 8)), shift) & m;
                    var d = Vector128.ShiftRightLogical(Vector128.LoadUnsafe(ref ps, (nuint)(js + 12)), shift) & m;
                    var bytes = Vector128.Narrow(Vector128.Narrow(a, b), Vector128.Narrow(c, d));
                    Vector128.StoreUnsafe(Vector128.Shuffle(bytes, rev), ref pd, (nuint)js);
                }
            }
            for (int j = js; j < w; j++) {
                int val = ArrayAccess.GetDataByte(datas[lines + j], comp);
                ArrayAccess.SetDataByte(lined, j, val);
            }
        }

        return pixd;
    }

    // leptonica: pixSetRGBComponent (pix2.c)
    /// <summary>Places the 8 bpp pixs into the specified component of this 32 bpp pix.</summary>
    /// <param name="pixs">8 bpp</param>
    /// <param name="comp">one of the set: {COLOR_RED, COLOR_GREEN, COLOR_BLUE, L_ALPHA_CHANNEL}</param>
    /// <remarks>
    /// (1) This (pixd) must be 32 bpp.  This places the 8 bpp pixel in pixs
    ///     into the specified component (properly interleaved) in pixd,
    /// (2) The two images are registered to the UL corner; the sizes
    ///     need not be the same, but a warning is issued if they differ.
    /// </remarks>
    public void SetRGBComponent(Pix pixs, int comp)
    {
        if (Depth != 32)
            throw Lept.Arg("pixSetRGBComponent", "pixd not 32 bpp");
        if (pixs.Depth != 8)
            throw Lept.Arg("pixSetRGBComponent", "pixs not 8 bpp");
        if (comp != L.COLOR_RED && comp != L.COLOR_GREEN &&
            comp != L.COLOR_BLUE && comp != L.L_ALPHA_CHANNEL)
            throw Lept.Arg("pixSetRGBComponent", "invalid comp");
        pixs.GetDimensions(out int ws, out int hs, out _);
        GetDimensions(out int wd, out int hd, out _);
        if (ws != wd || hs != hd)
            Log.Warning("pixSetRGBComponent", "images sizes not equal");
        int w = Math.Min(ws, wd);
        int h = Math.Min(hs, hd);
        if (comp == L.L_ALPHA_CHANNEL)
            Spp = 4;
        uint[] datas = pixs.Data;
        uint[] datad = Data;
        int wpls = pixs.Wpl;
        int wpld = Wpl;
        bool simd = Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian && w >= 16;
        int shift = 8 * (3 - comp);
        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls);
            int lined = i * wpld;
            int js = 0;
            if (simd) {
                    /* 16 pixels per step: restore pixel order, widen the bytes
                     * to words and merge them into the selected component. */
                ref byte ps = ref MemoryMarshal.GetReference(MemoryMarshal.AsBytes(lines));
                ref uint pd = ref datad[lined];
                var rev = Vector128.Create((byte)3, 2, 1, 0, 7, 6, 5, 4, 11, 10, 9, 8, 15, 14, 13, 12);
                var keep = Vector128.Create(~(0xffu << shift));
                for (; js + 16 <= w; js += 16) {
                    var bytes = Vector128.Shuffle(Vector128.LoadUnsafe(ref ps, (nuint)js), rev);
                    var (w0, w1) = Vector128.Widen(bytes);
                    var (d0, d1) = Vector128.Widen(w0);
                    var (d2, d3) = Vector128.Widen(w1);
                    Vector128.StoreUnsafe((Vector128.LoadUnsafe(ref pd, (nuint)js) & keep)
                        | Vector128.ShiftLeft(d0, shift), ref pd, (nuint)js);
                    Vector128.StoreUnsafe((Vector128.LoadUnsafe(ref pd, (nuint)(js + 4)) & keep)
                        | Vector128.ShiftLeft(d1, shift), ref pd, (nuint)(js + 4));
                    Vector128.StoreUnsafe((Vector128.LoadUnsafe(ref pd, (nuint)(js + 8)) & keep)
                        | Vector128.ShiftLeft(d2, shift), ref pd, (nuint)(js + 8));
                    Vector128.StoreUnsafe((Vector128.LoadUnsafe(ref pd, (nuint)(js + 12)) & keep)
                        | Vector128.ShiftLeft(d3, shift), ref pd, (nuint)(js + 12));
                }
            }
            for (int j = js; j < w; j++) {
                byte srcbyte = (byte)ArrayAccess.GetDataByte(lines, j);
                ArrayAccess.SetDataByte(ref datad[lined + j], comp, srcbyte);
            }
        }
    }

    // leptonica: pixGetRGBComponentCmap (pix2.c)
    /// <summary>Extracts one component of a colormapped image as an 8 bpp image.</summary>
    /// <param name="comp">one of the set: {COLOR_RED, COLOR_GREEN, COLOR_BLUE}</param>
    /// <returns>the selected 8 bpp component image of the input cmapped image</returns>
    /// <remarks>(1) pixs is colormapped.  In leptonica, we do not support alpha in colormaps.</remarks>
    public Pix GetRGBComponentCmap(int comp)
    {
        PixColormap? cmap = Colormap;
        if (cmap == null)
            throw Lept.Arg("pixGetRGBComponentCmap", "pixs not cmapped");
        if (comp == L.L_ALPHA_CHANNEL)
            throw Lept.Arg("pixGetRGBComponentCmap", "alpha in cmaps not supported");
        if (comp != L.COLOR_RED && comp != L.COLOR_GREEN && comp != L.COLOR_BLUE)
            throw Lept.Arg("pixGetRGBComponentCmap", "invalid comp");

            /* If not 8 bpp, make a cmapped 8 bpp pix */
        Pix pixc;
        if (Depth == 8)
            pixc = Clone(this);
        else
            pixc = ConvertTo8(true);
        bool valid = cmap.IsValid(pixc);
        if (!valid)
            throw Lept.Arg("pixGetRGBComponentCmap", "invalid colormap");

        GetDimensions(out int w, out int h, out _);
        Pix pixd = Create(w, h, 8);
        pixd.CopyResolution(this);
        int wplc = pixc.Wpl;
        int wpld = pixd.Wpl;
        uint[] datac = pixc.Data;
        uint[] datad = pixd.Data;
        cmap.ToArrays(out int[] rmap, out int[] gmap, out int[] bmap, out _);

        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> linec = datac.AsSpan(i * wplc);
            Span<uint> lined = datad.AsSpan(i * wpld);
            if (comp == L.COLOR_RED) {
                for (int j = 0; j < w; j++) {
                    int index = ArrayAccess.GetDataByte(linec, j);
                    int val = rmap[index];
                    ArrayAccess.SetDataByte(lined, j, val);
                }
            } else if (comp == L.COLOR_GREEN) {
                for (int j = 0; j < w; j++) {
                    int index = ArrayAccess.GetDataByte(linec, j);
                    int val = gmap[index];
                    ArrayAccess.SetDataByte(lined, j, val);
                }
            } else if (comp == L.COLOR_BLUE) {
                for (int j = 0; j < w; j++) {
                    int index = ArrayAccess.GetDataByte(linec, j);
                    int val = bmap[index];
                    ArrayAccess.SetDataByte(lined, j, val);
                }
            }
        }

        return pixd;
    }

    // leptonica: pixCopyRGBComponent (pix2.c)
    /// <summary>Copies one component of pixs into this pix.</summary>
    /// <param name="pixs">32 bpp</param>
    /// <param name="comp">one of the set: {COLOR_RED, COLOR_GREEN, COLOR_BLUE, L_ALPHA_CHANNEL}</param>
    /// <remarks>
    /// (1) This (pixd) and pixs must be 32 bpp.
    /// (2) The two images are registered to the UL corner.  The sizes
    ///     are usually the same, and a warning is issued if they differ.
    /// </remarks>
    public void CopyRGBComponent(Pix pixs, int comp)
    {
        // NOTE(upstream): the C test is "!pixd && pixGetDepth(pixd) != 32" (and the same
        // for pixs), which never rejects a pix that is not 32 bpp; it is not checked here either.
        if (comp != L.COLOR_RED && comp != L.COLOR_GREEN &&
            comp != L.COLOR_BLUE && comp != L.L_ALPHA_CHANNEL)
            throw Lept.Arg("pixCopyRGBComponent", "invalid component");
        pixs.GetDimensions(out int ws, out int hs, out _);
        GetDimensions(out int wd, out int hd, out _);
        if (ws != wd || hs != hd)
            Log.Warning("pixCopyRGBComponent", "images sizes not equal");
        int w = Math.Min(ws, wd);
        int h = Math.Min(hs, hd);
        if (comp == L.L_ALPHA_CHANNEL)
            Spp = 4;
        int wpls = pixs.Wpl;
        int wpld = Wpl;
        uint[] datas = pixs.Data;
        uint[] datad = Data;
        for (int i = 0; i < h; i++) {
            int lines = i * wpls;
            int lined = i * wpld;
            for (int j = 0; j < w; j++) {
                int val = ArrayAccess.GetDataByte(datas[lines + j], comp);
                ArrayAccess.SetDataByte(ref datad[lined + j], comp, val);
            }
        }
    }

    // leptonica: composeRGBPixel (pix2.c)
    /// <summary>Composes a 32 bpp pixel from red, green and blue components; alpha is 0.</summary>
    /// <param name="rval">The red component, from 0 to 255.</param>
    /// <param name="gval">The green component, from 0 to 255.</param>
    /// <param name="bval">The blue component, from 0 to 255.</param>
    /// <param name="ppixel">Receives the 32-bit pixel.</param>
    /// <remarks>
    /// All channels are 8 bits: the input values must be between 0 and 255.
    /// For speed, this is not enforced by masking with 0xff before shifting.
    /// </remarks>
    public static void ComposeRGBPixel(int rval, int gval, int bval, out uint ppixel)
    {
        ppixel = ((uint)rval << L.L_RED_SHIFT) |
                 ((uint)gval << L.L_GREEN_SHIFT) |
                 ((uint)bval << L.L_BLUE_SHIFT);
    }

    // leptonica: composeRGBAPixel (pix2.c)
    /// <summary>Composes a 32 bpp pixel from red, green, blue and alpha components.</summary>
    /// <param name="rval">The red component, from 0 to 255.</param>
    /// <param name="gval">The green component, from 0 to 255.</param>
    /// <param name="bval">The blue component, from 0 to 255.</param>
    /// <param name="aval">The alpha component, from 0 to 255.</param>
    /// <param name="ppixel">Receives the 32-bit pixel.</param>
    /// <remarks>
    /// All channels are 8 bits: the input values must be between 0 and 255.
    /// For speed, this is not enforced by masking with 0xff before shifting.
    /// </remarks>
    public static void ComposeRGBAPixel(int rval, int gval, int bval, int aval, out uint ppixel)
    {
        ppixel = ((uint)rval << L.L_RED_SHIFT) |
                 ((uint)gval << L.L_GREEN_SHIFT) |
                 ((uint)bval << L.L_BLUE_SHIFT) |
                 (uint)aval;
    }

    // leptonica: extractRGBValues (pix2.c)
    /// <summary>Extracts the r, g and b components of a 32 bit pixel.</summary>
    /// <remarks>
    /// A slower implementation uses macros:
    ///    *prval = GET_DATA_BYTE(&amp;pixel, COLOR_RED);
    ///    *pgval = GET_DATA_BYTE(&amp;pixel, COLOR_GREEN);
    ///    *pbval = GET_DATA_BYTE(&amp;pixel, COLOR_BLUE);
    /// </remarks>
    public static void ExtractRGBValues(uint pixel, out int prval, out int pgval, out int pbval)
    {
        prval = (int)((pixel >> L.L_RED_SHIFT) & 0xff);
        pgval = (int)((pixel >> L.L_GREEN_SHIFT) & 0xff);
        pbval = (int)((pixel >> L.L_BLUE_SHIFT) & 0xff);
    }

    // leptonica: extractRGBAValues (pix2.c)
    /// <summary>Extracts the r, g, b and a components of a 32 bit pixel.</summary>
    public static void ExtractRGBAValues(uint pixel, out int prval, out int pgval,
                                         out int pbval, out int paval)
    {
        prval = (int)((pixel >> L.L_RED_SHIFT) & 0xff);
        pgval = (int)((pixel >> L.L_GREEN_SHIFT) & 0xff);
        pbval = (int)((pixel >> L.L_BLUE_SHIFT) & 0xff);
        paval = (int)((pixel >> L.L_ALPHA_SHIFT) & 0xff);
    }

    // leptonica: extractMinMaxComponent (pix2.c)
    /// <summary>Finds the minimum or maximum of the r, g and b components of a pixel.</summary>
    /// <param name="pixel">32 bpp RGB</param>
    /// <param name="type">L_CHOOSE_MIN or L_CHOOSE_MAX</param>
    /// <returns>component in range [0 ... 255]</returns>
    public static int ExtractMinMaxComponent(uint pixel, int type)
    {
        int val;
        ExtractRGBValues(pixel, out int rval, out int gval, out int bval);
        if (type == L.L_CHOOSE_MIN) {
            val = Math.Min(rval, gval);
            val = Math.Min(val, bval);
        } else {  /* type == L_CHOOSE_MAX */
            val = Math.Max(rval, gval);
            val = Math.Max(val, bval);
        }
        return val;
    }

    // leptonica: pixGetRGBLine (pix2.c)
    /// <summary>Puts the rgb components of a line into the given buffers.</summary>
    /// <param name="row">the line</param>
    /// <param name="bufr">array of red samples; size w bytes</param>
    /// <param name="bufg">array of green samples; size w bytes</param>
    /// <param name="bufb">array of blue samples; size w bytes</param>
    /// <remarks>
    /// (1) This pix must be 32 bpp.  This puts rgb components from the input
    ///     line in pixs into the given buffers.
    /// </remarks>
    public void GetRGBLine(int row, Span<byte> bufr, Span<byte> bufg, Span<byte> bufb)
    {
        if (Depth != 32)
            throw Lept.Arg("pixGetRGBLine", "pixs not 32 bpp");

        GetDimensions(out int w, out int h, out _);
        if (row < 0 || row >= h)
            throw Lept.Arg("pixGetRGBLine", "row out of bounds");
        int wpls = Wpl;
        int lines = row * wpls;
        uint[] data = Data;

        for (int j = 0; j < w; j++) {
            bufr[j] = (byte)ArrayAccess.GetDataByte(data[lines + j], L.COLOR_RED);
            bufg[j] = (byte)ArrayAccess.GetDataByte(data[lines + j], L.COLOR_GREEN);
            bufb[j] = (byte)ArrayAccess.GetDataByte(data[lines + j], L.COLOR_BLUE);
        }
    }

    /*-------------------------------------------------------------*
     *                   Raster line pixel setter                  *
     *-------------------------------------------------------------*/
    // leptonica: setLineDataVal (pix2.c)
    /// <summary>Sets a pixel value in a raster line.</summary>
    /// <param name="line">first word in raster line data</param>
    /// <param name="j">index of pixels into the raster line</param>
    /// <param name="d">depth of the pixel</param>
    /// <param name="val">pixel value to be set</param>
    /// <remarks>
    /// (1) This is a convenience function to set a pixel value in a
    ///     raster line where the depth of the image can have different
    ///     values (1, 2, 4, 8, 16 or 32).
    /// </remarks>
    public static void SetLineDataVal(Span<uint> line, int j, int d, uint val)
    {
        if (j < 0)
            throw Lept.Arg("setLineDataVal", "j must be >= 0");
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 16 && d != 32)
            throw Lept.Arg("setLineDataVal", "invalid d");

        if (d == 1)
            ArrayAccess.SetDataBitVal(line, j, (int)val);
        else if (d == 2)
            ArrayAccess.SetDataDibit(line, j, (int)val);
        else if (d == 4)
            ArrayAccess.SetDataQbit(line, j, (int)val);
        else if (d == 8)
            ArrayAccess.SetDataByte(line, j, (int)val);
        else if (d == 16)
            ArrayAccess.SetDataTwoBytes(line, j, (int)val);
        else  /* d == 32 */
            line[j] = val;
    }

    /*-------------------------------------------------------------*
     *                    Pixel endian conversion                  *
     *-------------------------------------------------------------*/
    // leptonica: pixEndianByteSwap (pix2.c)
    /// <summary>In-place byte swap of each 32-bit word (little-endian machines).</summary>
    /// <remarks>
    /// This is used to convert the data to the big-endian byte order
    /// of image files, and back.
    /// </remarks>
    public void EndianByteSwap()
    {
        uint[] data = Data;
        int wpl = Wpl;
        int h = Height;
        int k = 0;
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < wpl; j++, k++) {
                uint word = data[k];
                data[k] = (word >> 24) |
                          ((word >> 8) & 0x0000ff00) |
                          ((word << 8) & 0x00ff0000) |
                          (word << 24);
            }
        }
    }

    // leptonica: pixEndianByteSwapNew (pix2.c)
    /// <summary>Returns a byte-swapped copy (little-endian machines).</summary>
    public Pix EndianByteSwapNew()
    {
        uint[] datas = Data;
        int wpl = Wpl;
        int h = Height;
        Pix pixd = CreateTemplate(this);
        uint[] datad = pixd.Data;
        int k = 0;
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < wpl; j++, k++) {
                uint word = datas[k];
                datad[k] = (word >> 24) |
                           ((word >> 8) & 0x0000ff00) |
                           ((word << 8) & 0x00ff0000) |
                           (word << 24);
            }
        }
        return pixd;
    }

    // leptonica: lineEndianByteSwap (pix2.c)
    /// <summary>Swaps the bytes within each word of a line of image data (little-endian machines).</summary>
    /// <param name="datad">dest byte array data, reordered on little-endians</param>
    /// <param name="datas">a src line of pix data</param>
    /// <param name="wpl">number of 32 bit words in the line</param>
    /// <remarks>
    /// (1) This is used on little-endian platforms to swap
    ///     the bytes within each word in the line of image data.
    ///     Bytes 0 &lt;==&gt; 3 and 1 &lt;==&gt; 2 are swapped in the dest
    ///     byte array data8d, relative to the pix data in datas.
    /// (2) The bytes represent 8 bit pixel values.  They are swapped
    ///     for little endians so that when the dest array datad
    ///     is addressed by bytes, the pixels are chosen sequentially
    ///     from left to right in the image.
    /// </remarks>
    public static void LineEndianByteSwap(Span<uint> datad, ReadOnlySpan<uint> datas, int wpl)
    {
        for (int j = 0; j < wpl; j++) {
            uint word = datas[j];
            datad[j] = (word >> 24) |
                       ((word >> 8) & 0x0000ff00) |
                       ((word << 8) & 0x00ff0000) |
                       (word << 24);
        }
    }

    // leptonica: pixEndianTwoByteSwapNew (pix2.c)
    /// <summary>Returns a copy with the 2-byte entities within each 32-bit word swapped (little-endian machines).</summary>
    /// <remarks>
    /// (1) This is used on little-endian platforms to swap the
    ///     2-byte entities within a 32-bit word.
    /// (2) This is equivalent to a full byte swap, as performed
    ///     by pixEndianByteSwap(), followed by byte swaps in
    ///     each of the 16-bit entities separately.
    /// (3) Unlike pixEndianTwoByteSwap(), which swaps the shorts in-place,
    ///     this returns a new pix.  We provide this
    ///     to avoid having to swap twice in situations where the input
    ///     pix must be restored to canonical little-endian order.
    /// </remarks>
    public Pix EndianTwoByteSwapNew()
    {
        uint[] datas = Data;
        int wpl = Wpl;
        int h = Height;
        Pix pixd = CreateTemplate(this);
        uint[] datad = pixd.Data;
        int k = 0;
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < wpl; j++, k++) {
                uint word = datas[k];
                datad[k] = (word << 16) | (word >> 16);
            }
        }

        return pixd;
    }

    // leptonica: pixEndianTwoByteSwap (pix2.c)
    /// <summary>In-place swap of the 2-byte entities within each 32-bit word (little-endian machines).</summary>
    /// <remarks>
    /// (1) This is used on little-endian platforms to swap the
    ///     2-byte entities within a 32-bit word.
    /// (2) This is equivalent to a full byte swap, as performed
    ///     by pixEndianByteSwap(), followed by byte swaps in
    ///     each of the 16-bit entities separately.
    /// </remarks>
    public void EndianTwoByteSwap()
    {
        uint[] data = Data;
        int wpl = Wpl;
        int h = Height;
        int k = 0;
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < wpl; j++, k++) {
                uint word = data[k];
                data[k] = (word << 16) | (word >> 16);
            }
        }
    }

    /*-------------------------------------------------------------*
     *             Extract raster data as binary string            *
     *-------------------------------------------------------------*/
    // leptonica: pixGetRasterData (pix2.c)
    /// <summary>Returns the raster data as a byte string, padded to the byte.</summary>
    /// <returns>the raster data; its length is the number of bytes (C: nbytes)</returns>
    /// <remarks>
    /// (1) pixs is 1, 2, 4, 8, 16 or 32 bpp.  This returns the raster data as
    ///     a byte string, padded to the byte.  For 1 bpp, the first pixel is
    ///     the MSbit in the first byte.  For rgb, the bytes are in (rgb) order.
    ///     This is the format required for flate encoding of pixels in a
    ///     PostScript file.
    /// (2) The pad bits of this pix are set to 0, as in C.
    /// </remarks>
    public byte[] GetRasterData()
    {
        GetDimensions(out int w, out int h, out int d);
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 16 && d != 32)
            throw Lept.Arg("pixGetRasterData", "depth not in {1,2,4,8,16,32}");

        SetPadBits(0);
        uint[] rdata = Data;
        int wpl = Wpl;
        int databpl;  /* bytes for each raster line in returned data */
        if (d == 1)
            databpl = (w + 7) / 8;
        else if (d == 2)
            databpl = (w + 3) / 4;
        else if (d == 4)
            databpl = (w + 1) / 2;
        else if (d == 8 || d == 16)
            databpl = w * (d / 8);
        else  /* d == 32 bpp rgb */
            databpl = 3 * w;
        byte[] data = new byte[(long)databpl * h];  /* packed data in returned array */

        for (int i = 0; i < h; i++) {
            ReadOnlySpan<uint> rline = rdata.AsSpan(i * wpl);
            int line = i * databpl;
            if (d <= 8) {
                for (int j = 0; j < databpl; j++)
                    data[line + j] = (byte)ArrayAccess.GetDataByte(rline, j);
            } else if (d == 16) {
                // NOTE(upstream): only the low byte of each 16-bit pixel is stored, at an even
                // position; the odd bytes stay 0.
                for (int j = 0; j < w; j++)
                    data[line + 2 * j] = (byte)ArrayAccess.GetDataTwoBytes(rline, j);
            } else {  /* d == 32 bpp rgb */
                for (int j = 0; j < w; j++) {
                    ExtractRGBValues(rline[j], out int rval, out int gval, out int bval);
                    data[line + 3 * j] = (byte)rval;
                    data[line + 3 * j + 1] = (byte)gval;
                    data[line + 3 * j + 2] = (byte)bval;
                }
            }
        }

        return data;
    }

    /*-------------------------------------------------------------*
     *                Infer resolution from image size             *
     *-------------------------------------------------------------*/
    // leptonica: pixInferResolution (pix2.c)
    /// <summary>Infers the resolution, assuming the longest side of the image has the given length.</summary>
    /// <param name="longside">assumed max dimension, in inches</param>
    /// <returns>resolution (ppi)</returns>
    /// <remarks>
    /// (1) This finds the resolution, assuming that the longest side
    ///     of the image is %longside.  (C: on error, returns 300 ppi.)
    /// (2) This is useful for computing resolution for generating pdfs,
    ///     when the images are scanned from pages of known size.
    ///     There, %longside is typically about 11.0.
    /// </remarks>
    public int InferResolution(float longside)
    {
        if (longside <= 0.0)
            throw Lept.Arg("pixInferResolution", "longside not > 0");

        GetDimensions(out int w, out int h, out _);
        int maxdim = Math.Max(w, h);
        int res = (int)(maxdim / longside + 0.5);
        res = Math.Max(res, 1);  /* don't let it be 0 */
        if (res < 10)
            Log.Warning("pixInferResolution", $"low inferred resolution: {res} ppi");
        if (res > 10000)
            Log.Warning("pixInferResolution", $"high inferred resolution: {res} ppi");
        return res;
    }

    /*-------------------------------------------------------------*
     *                 Test alpha component opaqueness             *
     *-------------------------------------------------------------*/
    // leptonica: pixAlphaIsOpaque (pix2.c)
    /// <summary>Tests whether all alpha values are 255.</summary>
    /// <returns>true if spp == 4 and all alpha component values are 255 (opaque); false otherwise</returns>
    /// <remarks>(1) The pix must be 32 bpp, spp == 4.</remarks>
    public bool AlphaIsOpaque()
    {
        if (Depth != 32)
            throw Lept.Arg("pixAlphaIsOpaque", "&pix not 32 bpp");
        if (Spp != 4)
            throw Lept.Arg("pixAlphaIsOpaque", "&pix not 4 spp");

        uint[] data = Data;
        int wpl = Wpl;
        GetDimensions(out int w, out int h, out _);
        for (int i = 0; i < h; i++) {
            int line = i * wpl;
            for (int j = 0; j < w; j++) {
                int alpha = ArrayAccess.GetDataByte(data[line + j], L.L_ALPHA_CHANNEL);
                if ((alpha ^ 0xff) != 0)  /* not opaque */
                    return false;
            }
        }

        return true;
    }

    /*-------------------------------------------------------------*
     *             Setup helpers for 8 bpp byte processing         *
     *-------------------------------------------------------------*/
    // leptonica: pixSetupByteProcessing (pix2.c)
    /// <summary>Swaps the byte order within each word and returns the line offsets for direct byte access.</summary>
    /// <param name="pw">width</param>
    /// <param name="ph">height</param>
    /// <returns>
    /// the offset in bytes of the start of each raster line in
    /// MemoryMarshal.AsBytes(pix.Data) (C: the line ptr array)
    /// </returns>
    /// <remarks>
    /// (1) The pix must be 8 bpp, no colormap.  This is a simple helper for
    ///     processing 8 bpp images with direct byte access.  It can swap
    ///     byte order within each word.
    /// (2) After processing, you must call pixCleanupByteProcessing(),
    ///     which restores byte order.
    /// (3) Usage:
    ///         int[] lineptrs = pix.SetupByteProcessing(out w, out h);
    ///         Span&lt;byte&gt; bytes = MemoryMarshal.AsBytes(pix.Data.AsSpan());
    ///         for (i = 0; i &lt; h; i++) {
    ///             int line = lineptrs[i];
    ///             for (j = 0; j &lt; w; j++) {
    ///                 val = bytes[line + j];
    ///                 ...
    ///             }
    ///         }
    ///         pix.CleanupByteProcessing(lineptrs);
    /// </remarks>
    public int[] SetupByteProcessing(out int pw, out int ph)
    {
        pw = 0;
        ph = 0;
        if (Depth != 8)
            throw Lept.Arg("pixSetupByteProcessing", "pix not defined or not 8 bpp");
        GetDimensions(out int w, out int h, out _);
        pw = w;
        ph = h;
        if (Colormap != null)
            throw Lept.Arg("pixSetupByteProcessing", "pix has colormap");

        EndianByteSwap();
        int[] lineptrs = GetLinePtrs();
        for (int i = 0; i < lineptrs.Length; i++)
            lineptrs[i] *= 4;   /* word offsets to byte offsets */
        return lineptrs;
    }

    // leptonica: pixCleanupByteProcessing (pix2.c)
    /// <summary>Restores the byte order after processing started by pixSetupByteProcessing().</summary>
    /// <param name="lineptrs">the line offsets from pixSetupByteProcessing()</param>
    /// <remarks>
    /// (1) The pix is 8 bpp, no colormap.  This must be called after
    ///     processing that was initiated by pixSetupByteProcessing() has finished.
    /// </remarks>
    public void CleanupByteProcessing(int[] lineptrs)
    {
        EndianByteSwap();
    }

    /*------------------------------------------------------------------------*
     *      Setting parameters for antialias masking with alpha transforms    *
     *------------------------------------------------------------------------*/
    /// <summary>
    /// The default 8 bpp alpha mask values for rings at distance 1 and 2 from the
    /// border (C global AlphaMaskBorderVals).  To change the values, use
    /// <see cref="SetAlphaMaskBorder"/>.
    /// </summary>
    public static readonly float[] AlphaMaskBorderVals = { 0.0f, 0.5f };

    // leptonica: l_setAlphaMaskBorder (pix2.c)
    /// <summary>Sets the opacity values used for the two outer boundary rings of an alpha mask.</summary>
    /// <param name="val1">in [0.0 ... 1.0]</param>
    /// <param name="val2">in [0.0 ... 1.0]</param>
    /// <remarks>
    /// (1) This sets the opacity values used to generate the two outer
    ///     boundary rings in the alpha mask associated with geometric
    ///     transforms such as pixRotateWithAlpha().
    /// (2) The default values are val1 = 0.0 (completely transparent
    ///     in the outermost ring) and val2 = 0.5 (half transparent
    ///     in the second ring).  When the image is blended, this
    ///     completely removes the outer ring (shrinking the image by
    ///     2 in each direction), and alpha-blends with 0.5 the second ring.
    ///     Using val1 = 0.25 and val2 = 0.75 gives a slightly more
    ///     blurred border, with no perceptual difference at screen resolution.
    /// (3) The actual mask values are found by multiplying these
    ///     normalized opacity values by 255.
    /// </remarks>
    public static void SetAlphaMaskBorder(float val1, float val2)
    {
        val1 = Math.Max(0.0f, Math.Min(1.0f, val1));
        val2 = Math.Max(0.0f, Math.Min(1.0f, val2));
        AlphaMaskBorderVals[0] = val1;
        AlphaMaskBorderVals[1] = val2;
    }

}
