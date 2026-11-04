using System;
using System.IO;
using System.Text;

namespace Leptonica;

/// <summary>Colormap entry (struct RGBA_Quad).</summary>
public struct RgbaQuad
{
    /// <summary>The blue component, in the range 0 to 255.</summary>
    public byte Blue;

    /// <summary>The green component, in the range 0 to 255.</summary>
    public byte Green;

    /// <summary>The red component, in the range 0 to 255.</summary>
    public byte Red;

    /// <summary>The alpha (opacity) component, in the range 0 to 255.</summary>
    public byte Alpha;
}

/// <summary>Constants from pix.h used by colormap.c.</summary>
public static partial class L
{
    /* Color component selection flags (pix.h) */

    /// <summary>Uses the red component.</summary>
    public const int L_SELECT_RED = 1;

    /// <summary>Uses the green component.</summary>
    public const int L_SELECT_GREEN = 2;

    /// <summary>Uses the blue component.</summary>
    public const int L_SELECT_BLUE = 3;

    /// <summary>Uses the average of the color components.</summary>
    public const int L_SELECT_AVERAGE = 6;
}

/*
 *  colormap.c: Colormap creation, addition, access, conversion, I/O,
 *  serialization and transforms.
 *
 *  Differences from C:
 *    - No pixcmapDestroy(): the GC owns the objects (not ported).
 *    - FILE* becomes Stream.  Text is formatted with CFormat (the printf of the
 *      C runtime) and read with the fscanf formats of the C code, so that the
 *      serialization is the same as the C output byte for byte.  The *Mem
 *      variants take a ReadOnlySpan&lt;byte&gt; and return a byte[] (without the
 *      terminating NUL that C appends to its buffer).
 *    - pixcmapGetColor(), pixcmapGetRGBA() and pixcmapResetColor() return false
 *      and log an error for an invalid index, as the C callers ignore that error.
 *    - Functions that return an error code or NULL in C throw instead,
 *      except where the C code logs and carries on (noted at the place).
 *
 *  Stubs for callees in other files: numaGammaTRC, numaContrastTRC (enhance.c),
 *  pixelShiftByComponent (coloring.c).
 */

/// <summary>
/// Colormap for 1, 2, 4 or 8 bpp images (struct PixColormap).
/// </summary>
public sealed partial class PixColormap
{
    private RgbaQuad[] array;  /* colormap table (array of RGBA_QUAD)     */
    private int depth;         /* of pix (1, 2, 4 or 8 bpp)               */
    private int nalloc;        /* number of color entries allocated       */
    private int n;             /* number of color entries used            */

    private PixColormap(RgbaQuad[] array, int depth, int nalloc, int n)
    {
        this.array = array;
        this.depth = depth;
        this.nalloc = nalloc;
        this.n = n;
    }

    /*-------------------------------------------------------------*
     *                Colormap creation and addition               *
     *-------------------------------------------------------------*/
    // leptonica: pixcmapCreate (colormap.c)
    /// <param name="depth">of pix, in bpp</param>
    public static PixColormap Create(int depth)
    {
        if (depth != 1 && depth != 2 && depth != 4 && depth != 8)
            throw Lept.Arg("pixcmapCreate", "depth not in {1,2,4,8}");

        int nalloc = 1 << depth;
        return new PixColormap(new RgbaQuad[nalloc], depth, nalloc, 0);
    }

    // leptonica: pixcmapCreateRandom (colormap.c)
    /// <summary>Creates a colormap with random colors.</summary>
    /// <param name="depth">of pix, in bpp: 2, 4 or 8</param>
    /// <param name="hasblack">true if the first color is black; false if no black</param>
    /// <param name="haswhite">true if the last color is white; false if no white</param>
    /// <remarks>
    /// (1) This sets up a colormap with random colors,
    ///     where the first color is optionally black, the last color
    ///     is optionally white, and the remaining colors are
    ///     chosen randomly.
    /// (2) The number of randomly chosen colors is:
    ///          2^(depth) - haswhite - hasblack
    /// (3) Because rand() is seeded, it might disrupt otherwise
    ///     deterministic results if also used elsewhere in a program.
    /// (4) rand() is not threadsafe, and will generate garbage if run
    ///     on multiple threads at once -- though garbage is generally
    ///     what you want from a random number generator!
    /// (5) Modern rand()s have equal randomness in low and high order
    ///     bits, but older ones don't.  Here, we're just using rand()
    ///     to choose colors for output.
    /// </remarks>
    public static PixColormap CreateRandom(int depth, bool hasblack, bool haswhite)
    {
        if (depth != 2 && depth != 4 && depth != 8)
            throw Lept.Arg("pixcmapCreateRandom", "depth not in {2, 4, 8}");
        int hb = hasblack ? 1 : 0;
        int hw = haswhite ? 1 : 0;

        PixColormap cmap = Create(depth);
        int ncolors = 1 << depth;
        if (hb != 0)  /* first color is optionally black */
            cmap.AddColor(0, 0, 0);
        for (int i = hb; i < ncolors - hw; i++) {
            int red = (int)((uint)CRand.Next() & 0xff);
            int green = (int)((uint)CRand.Next() & 0xff);
            int blue = (int)((uint)CRand.Next() & 0xff);
            cmap.AddColor(red, green, blue);
        }
        if (hw != 0)  /* last color is optionally white */
            cmap.AddColor(255, 255, 255);

        return cmap;
    }

    // leptonica: pixcmapCreateLinear (colormap.c)
    /// <summary>Creates a colormap with equally spaced gray color values.</summary>
    /// <param name="d">depth of pix for this colormap; 1, 2, 4 or 8</param>
    /// <param name="nlevels">valid in range [2, 2^d]</param>
    /// <remarks>
    /// (1) Colormap has equally spaced gray color values
    ///     from black (0, 0, 0) to white (255, 255, 255).
    /// </remarks>
    public static PixColormap CreateLinear(int d, int nlevels)
    {
        const string func = "pixcmapCreateLinear";
        if (d != 1 && d != 2 && d != 4 && d != 8)
            throw Lept.Arg(func, "d not in {1, 2, 4, 8}");
        int maxlevels = 1 << d;
        if (nlevels < 2 || nlevels > maxlevels)
            throw Lept.Arg(func, "invalid nlevels");

        PixColormap cmap = Create(d);
        for (int i = 0; i < nlevels; i++) {
            int val = (255 * i) / (nlevels - 1);
            cmap.AddColor(val, val, val);
        }
        return cmap;
    }

    /// <summary>Creates a copy of the colormap.</summary>
    /// <returns>A new colormap with the same depth and colors.</returns>
    /// <exception cref="LeptonicaException">The colormap is not valid.</exception>
    // leptonica: pixcmapCopy (colormap.c)
    public PixColormap Copy()
    {
        if (!IsValid(null))
            throw Lept.Fail("pixcmapCopy", "invalid cmap");

        RgbaQuad[] arrayd = new RgbaQuad[nalloc];
        Array.Copy(array, arrayd, n);
        return new PixColormap(arrayd, depth, nalloc, n);
    }

    // leptonica: pixcmapIsValid (colormap.c)
    /// <summary>True if the colormap is valid, and also valid for pix if given.</summary>
    /// <param name="pix">optional; can be null</param>
    /// <remarks>
    /// (1) If pix is input, this will verify that pixel values cannot
    ///     overflow the colormap.  This is a relatively expensive operation
    ///     that may need to check all the pixel values.
    /// (2) If pix is input, there must be at least one color in the
    ///     colormap if it is to be valid with any pix, even if the
    ///     pixels are all 0.
    /// </remarks>
    public bool IsValid(Pix? pix)
    {
        const string func = "pixcmapIsValid";
        int d = depth;
        if (d != 1 && d != 2 && d != 4 && d != 8) {
            Log.Error(func, $"invalid cmap depth: {d}");
            return false;
        }
        if (nalloc != (1 << d)) {
            Log.Error(func, $"invalid cmap nalloc = {nalloc}; d = {d}");
            return false;
        }
        if (n < 0 || n > nalloc) {
            Log.Error(func, $"invalid cmap n: {n}; nalloc = {nalloc}");
            return false;
        }

            /* If a pix is given, it must have a depth no larger than 8 */
        int pixdepth = 0, maxcolors = 0;
        if (pix != null) {
            pixdepth = pix.Depth;
            if (pixdepth > 8) {
                Log.Error(func, $"pix depth {pixdepth} > 8");
                return false;
            }
            maxcolors = 1 << pixdepth;
        }

            /* To prevent indexing overflow into the cmap, the pix depth
             * must not exceed the cmap depth.  Do not require depth equality,
             * because some functions such as median cut quantizers allow
             * the cmap depth to be bigger than the pix depth. */
        if (pix != null && (pixdepth > d)) {
            Log.Error(func, $"(pix depth = {pixdepth}) > (cmap depth = {d})");
            return false;
        }
        if (pix != null && n < 1) {
            Log.Error(func, "cmap array is empty; invalid with any pix");
            return false;
        }

            /* Do not let the colormap have more colors than the pixels
             * can address.  The png encoder considers this to be an
             * "invalid palette length".  For example, for 1 bpp, the
             * colormap may have a depth > 1, but it must not have more
             * than 2 colors. */
        if (pix != null && (n > maxcolors)) {
            Log.Error(func, $"cmap entries = {n} > max colors for pix = {maxcolors}");
            return false;
        }

            /* Where the colormap or the pix may have been corrupted, and
             * in particular when reading or writing image files, it should
             * be verified that the largest colormap index value in the image
             * is less than the number of entries in the colormap array.  */
        if (pix != null) {
            int maxindex = pix.GetMaxColorIndex();
            if (maxindex >= n) {
                Log.Error(func, $"(max index in image = {maxindex}) >= " +
                                $"(number entries in colormap = {n})");
                return false;
            }
        }

        return true;
    }

    // leptonica: pixcmapAddColor (colormap.c)
    /// <summary>Adds a color if there is room; the alpha component is 255 (opaque).</summary>
    public void AddColor(int rval, int gval, int bval)
    {
        if (n >= nalloc)
            throw Lept.Fail("pixcmapAddColor", "no free color entries");

        array[n].Red = (byte)rval;
        array[n].Green = (byte)gval;
        array[n].Blue = (byte)bval;
        array[n].Alpha = 255;
        n++;
    }

    // leptonica: pixcmapAddRGBA (colormap.c)
    /// <summary>Adds a color if there is room.</summary>
    public void AddRGBA(int rval, int gval, int bval, int aval)
    {
        if (n >= nalloc)
            throw Lept.Fail("pixcmapAddRGBA", "no free color entries");

        array[n].Red = (byte)rval;
        array[n].Green = (byte)gval;
        array[n].Blue = (byte)bval;
        array[n].Alpha = (byte)aval;
        n++;
    }

    // leptonica: pixcmapAddNewColor (colormap.c)
    /// <summary>Adds the color if it is not in the colormap yet and there is room.</summary>
    /// <param name="rval">The red component, in the range 0 to 255.</param>
    /// <param name="gval">The green component, in the range 0 to 255.</param>
    /// <param name="bval">The blue component, in the range 0 to 255.</param>
    /// <param name="index">Receives the index of the new or existing color.</param>
    /// <returns>0 if successful; 2 if the color could not be added.</returns>
    /// <remarks>
    /// (1) This only adds color if not already there.
    /// (2) The alpha component is 255 (opaque)
    /// (3) This returns the index of the new (or existing) color.
    /// (4) Returns 2 with a warning if unable to add this color;
    ///     the caller should check the return value.
    /// </remarks>
    public int AddNewColor(int rval, int gval, int bval, out int index)
    {
        index = 0;

            /* Check if the color is already present. */
        if (GetIndex(rval, gval, bval, out index))  /* found */
            return 0;

            /* We need to add the color.  Is there room? */
        if (n >= nalloc) {
            Log.Warning("pixcmapAddNewColor", "no free color entries");
            return 2;
        }

            /* There's room.  Add it. */
        AddColor(rval, gval, bval);
        index = Count - 1;
        return 0;
    }

    // leptonica: pixcmapAddNearestColor (colormap.c)
    /// <summary>Adds the color if there is room, otherwise finds the nearest one.</summary>
    /// <param name="rval">The red component, in the range 0 to 255.</param>
    /// <param name="gval">The green component, in the range 0 to 255.</param>
    /// <param name="bval">The blue component, in the range 0 to 255.</param>
    /// <param name="index">Receives the index of the new or existing color, or of the nearest color if there is no room.</param>
    /// <remarks>
    /// (1) This only adds color if not already there.
    /// (2) The alpha component is 255 (opaque)
    /// (3) If it's not in the colormap and there is no room to add
    ///     another color, this returns the index of the nearest color.
    /// </remarks>
    public void AddNearestColor(int rval, int gval, int bval, out int index)
    {
        index = 0;

            /* Check if the color is already present. */
        if (GetIndex(rval, gval, bval, out index))  /* found */
            return;

            /* We need to add the color.  Is there room? */
        if (n < nalloc) {
            AddColor(rval, gval, bval);
            index = Count - 1;
            return;
        }

            /* There's no room.  Return the index of the nearest color */
        GetNearestIndex(rval, gval, bval, out index);
    }

    // leptonica: pixcmapUsableColor (colormap.c)
    /// <summary>Checks if the color already exists or if there is room to add it.</summary>
    /// <returns>true if usable; false if not</returns>
    /// <remarks>
    /// (1) This checks if the color already exists or if there is
    ///     room to add it.  It makes no change in the colormap.
    /// </remarks>
    public bool UsableColor(int rval, int gval, int bval)
    {
            /* Is there room to add it? */
        if (n < nalloc)
            return true;

            /* No room; check if the color is already present. */
        return GetIndex(rval, gval, bval, out _);   /* found */
    }

    // leptonica: pixcmapAddBlackOrWhite (colormap.c)
    /// <summary>Adds black (color == 0) or white (color == 1) if it is missing and there is room.</summary>
    /// <param name="color">0 for black, 1 for white</param>
    /// <param name="index">index of color</param>
    /// <remarks>
    /// (1) This only adds color if not already there.
    /// (2) The alpha component is 255 (opaque)
    /// (3) This sets index to the requested color.
    /// (4) If there is no room in the colormap, returns the index
    ///     of the closest color.
    /// </remarks>
    public void AddBlackOrWhite(int color, out int index)
    {
        index = 0;

        if (color == 0) {  /* black */
            if (FreeCount > 0)
                AddNewColor(0, 0, 0, out index);
            else
                index = GetRankIntensity(0.0f);
        } else {  /* white */
            if (FreeCount > 0)
                AddNewColor(255, 255, 255, out index);
            else
                index = GetRankIntensity(1.0f);
        }
    }

    // leptonica: pixcmapSetBlackAndWhite (colormap.c)
    /// <param name="setblack">false for no operation; true to set darkest color to black</param>
    /// <param name="setwhite">false for no operation; true to set lightest color to white</param>
    public void SetBlackAndWhite(bool setblack, bool setwhite)
    {
        if (setblack) {
            int index = GetRankIntensity(0.0f);
            ResetColor(index, 0, 0, 0);
        }
        if (setwhite) {
            int index = GetRankIntensity(1.0f);
            ResetColor(index, 255, 255, 255);
        }
    }

    /// <summary>Gets the number of colors in the colormap.</summary>
    // leptonica: pixcmapGetCount (colormap.c)
    public int Count => n;

    /// <summary>Gets the depth of the colormap in bits per pixel: 1, 2, 4 or 8.</summary>
    // leptonica: pixcmapGetDepth (colormap.c)
    public int Depth => depth;

    // leptonica: pixcmapGetMinDepth (colormap.c)
    /// <summary>Minimum depth to support the colormap: 2, 4 or 8.</summary>
    public int MinDepth
    {
        get
        {
            int ncolors = Count;
            if (ncolors <= 4)
                return 2;
            else if (ncolors <= 16)
                return 4;
            else  /* ncolors > 16 */
                return 8;
        }
    }

    /// <summary>Gets the number of unused color entries that remain available in the colormap.</summary>
    // leptonica: pixcmapGetFreeCount (colormap.c)
    public int FreeCount => nalloc - n;

    // leptonica: pixcmapClear (colormap.c)
    /// <remarks>
    /// (1) This removes the colors by setting the count to 0.
    /// </remarks>
    public void Clear()
    {
        n = 0;
    }

    /*-------------------------------------------------------------*
     *                      Colormap random access                 *
     *-------------------------------------------------------------*/
    // leptonica: pixcmapGetColor (colormap.c)
    /// <returns>true if OK; false (with an error logged) if the index is not accessible;
    /// the caller should check</returns>
    public bool GetColor(int index, out int prval, out int pgval, out int pbval)
    {
        prval = pgval = pbval = 0;
        if (index < 0 || index >= n) {
            Log.Error("pixcmapGetColor", "index out of bounds");
            return false;
        }

        prval = array[index].Red;
        pgval = array[index].Green;
        pbval = array[index].Blue;
        return true;
    }

    // leptonica: pixcmapGetColor32 (colormap.c)
    /// <returns>32-bit rgb color value; the alpha channel value is 255</returns>
    public uint GetColor32(int index)
    {
        if (!GetColor(index, out int rval, out int gval, out int bval))
            throw Lept.Fail("pixcmapGetColor32", "rgb values not found");
        Pix.ComposeRGBAPixel(rval, gval, bval, 255, out uint val32);
        return val32;
    }

    // leptonica: pixcmapGetRGBA (colormap.c)
    /// <returns>true if OK; false (with an error logged) if the index is not accessible;
    /// the caller should check</returns>
    public bool GetRGBA(int index, out int prval, out int pgval, out int pbval, out int paval)
    {
        prval = pgval = pbval = paval = 0;
        if (index < 0 || index >= n) {
            Log.Error("pixcmapGetRGBA", "index out of bounds");
            return false;
        }

        prval = array[index].Red;
        pgval = array[index].Green;
        pbval = array[index].Blue;
        paval = array[index].Alpha;
        return true;
    }

    // leptonica: pixcmapGetRGBA32 (colormap.c)
    /// <returns>32-bit rgba color value</returns>
    public uint GetRGBA32(int index)
    {
        if (!GetRGBA(index, out int rval, out int gval, out int bval, out int aval))
            throw Lept.Fail("pixcmapGetRGBA32", "rgba values not found");
        Pix.ComposeRGBAPixel(rval, gval, bval, aval, out uint val32);
        return val32;
    }

    // leptonica: pixcmapResetColor (colormap.c)
    /// <summary>Resets the color of an existing colormap entry.</summary>
    /// <param name="index">The index of the entry to reset.</param>
    /// <param name="rval">The new red component, in the range 0 to 255.</param>
    /// <param name="gval">The new green component, in the range 0 to 255.</param>
    /// <param name="bval">The new blue component, in the range 0 to 255.</param>
    /// <remarks>
    /// (1) This resets sets the color of an entry that has already
    ///     been set and included in the count of colors.
    /// (2) The alpha component is 255 (opaque)
    /// </remarks>
    /// <returns>true if OK; false (with an error logged) if the index is not accessible;
    /// the caller should check</returns>
    public bool ResetColor(int index, int rval, int gval, int bval)
    {
        if (index < 0 || index >= n) {
            Log.Error("pixcmapResetColor", "index out of bounds");
            return false;
        }

        array[index].Red = (byte)rval;
        array[index].Green = (byte)gval;
        array[index].Blue = (byte)bval;
        array[index].Alpha = 255;
        return true;
    }

    // leptonica: pixcmapSetAlpha (colormap.c)
    /// <summary>Modifies the transparency of one entry in a colormap.</summary>
    /// <param name="index">The index of the entry to modify.</param>
    /// <param name="aval">The new alpha component, in the range 0 to 255.</param>
    /// <exception cref="ArgumentException"><paramref name="index"/> is out of bounds.</exception>
    public void SetAlpha(int index, int aval)
    {
        if (index < 0 || index >= n)
            throw Lept.Arg("pixcmapSetAlpha", "index out of bounds");

        array[index].Alpha = (byte)aval;
    }

    // leptonica: pixcmapGetIndex (colormap.c)
    /// <summary>Finds the index of a color in the colormap.</summary>
    /// <param name="rval">The red component to look for.</param>
    /// <param name="gval">The green component to look for.</param>
    /// <param name="bval">The blue component to look for.</param>
    /// <param name="index">Receives the index of the color if found; otherwise 0.</param>
    /// <returns><c>true</c> if the color is in the colormap; otherwise <c>false</c>.</returns>
    public bool GetIndex(int rval, int gval, int bval, out int index)
    {
        index = 0;
        int n = Count;

        for (int i = 0; i < n; i++) {
            if (rval == array[i].Red &&
                gval == array[i].Green &&
                bval == array[i].Blue) {
                index = i;
                return true;
            }
        }
        return false;
    }

    // leptonica: pixcmapHasColor (colormap.c)
    /// <returns>true if cmap has color; false otherwise</returns>
    public bool HasColor()
    {
        ToArrays(out int[] rmap, out int[] gmap, out int[] bmap, out _);
        int n = Count;
        for (int i = 0; i < n; i++) {
            if ((rmap[i] != gmap[i]) || (rmap[i] != bmap[i]))
                return true;
        }
        return false;
    }

    // leptonica: pixcmapIsOpaque (colormap.c)
    /// <summary>True if fully opaque: all entries are 255.</summary>
    public bool IsOpaque()
    {
        int n = Count;
        for (int i = 0; i < n; i++) {
            if (array[i].Alpha != 255)
                return false;
        }
        return true;
    }

    // leptonica: pixcmapNonOpaqueColorsInfo (colormap.c)
    /// <param name="pntrans">number of transparent alpha entries; &lt;= 256</param>
    /// <param name="pmax_trans">max index of transparent alpha</param>
    /// <param name="pmin_opaque">min index of opaque &lt; 256</param>
    /// <remarks>
    /// (1) This is used, for clarity, when writing the png tRNS palette.
    ///     According to the spec, http://www.w3.org/TR/PNG/#11tRNS,
    ///     if the tRNS palette is of size ntrans, the palette uses the first
    ///     ntrans alpha entries in the cmap, and the remaining alpha values
    ///     are assumed to be 255 (opaque), regardless of cmap alpha value.
    /// (2) Ordinarily, the non-opaque colors come first in the cmap, so
    ///          min_opaque > max_trans
    ///     and
    ///          ntrans = max_trans + 1 = min_opaque.
    ///     But this does not happen in general.  In trans-2bpp-palette.png,
    ///     for example, only the third of four entries is not opaque, so
    ///          ntrans = 1
    ///          max_trans = 2 (index is 0-based)
    ///          min_opaque = 0
    ///     The tRNS palette must extend to the third entry to cover the
    ///     color with transparency: use 3 as the fourth arg to png_set_tRNS().
    /// (3) If all entries are opaque, max_trans = -1.
    ///     If all entries are transparent, min_opaque = size of cmap.
    /// </remarks>
    public void NonOpaqueColorsInfo(out int pntrans, out int pmax_trans, out int pmin_opaque)
    {
        int n = Count;
        int ntrans = 0;
        int max_trans = -1;
        int min_opaque = n;
        bool opaque_found = false;
        for (int i = 0; i < n; i++) {
            if (array[i].Alpha != 255) {
                ntrans++;
                max_trans = i;
            } else if (opaque_found == false) {
                opaque_found = true;
                min_opaque = i;
            }
        }
        pntrans = ntrans;
        pmax_trans = max_trans;
        pmin_opaque = min_opaque;
    }

    // leptonica: pixcmapIsBlackAndWhite (colormap.c)
    /// <returns>true if the cmap has only two colors: black (0,0,0) and white (255,255,255)</returns>
    public bool IsBlackAndWhite()
    {
        if (Count != 2)
            return false;

        if (HasColor())
            return false;

        int val0 = array[0].Red;
        int val1 = array[1].Red;
        return (val0 == 0 && val1 == 255) || (val0 == 255 && val1 == 0);
    }

    // leptonica: pixcmapCountGrayColors (colormap.c)
    /// <summary>Counts the unique gray colors, including black and white.</summary>
    /// <returns>number of gray colors</returns>
    public int CountGrayColors()
    {
        int[] gray = new int[256];
        int n = Count;
        int count = 0;
        for (int i = 0; i < n; i++) {
            GetColor(i, out int rval, out int gval, out int bval);
            if ((rval == gval) && (rval == bval) && (gray[rval] == 0)) {
                gray[rval] = 1;
                count++;
            }
        }
        return count;
    }

    // leptonica: pixcmapGetRankIntensity (colormap.c)
    /// <summary>Returns the index of the color with the given intensity rank.</summary>
    /// <param name="rankval">0.0 for darkest, 1.0 for lightest color</param>
    /// <returns>the index into the colormap that corresponds to the rank intensity color</returns>
    public int GetRankIntensity(float rankval)
    {
        if (rankval < 0.0 || rankval > 1.0)
            throw Lept.Arg("pixcmapGetRankIntensity", "rankval not in [0.0 ... 1.0]");

        int n = Count;
        Numa na = Numa.Create(n);
        for (int i = 0; i < n; i++) {
            GetColor(i, out int rval, out int gval, out int bval);
            na.AddNumber(rval + gval + bval);
        }
        Numa nasort = na.GetSortIndex(L.L_SORT_INCREASING);
        int rankindex = (int)(rankval * (n - 1) + 0.5);

            /* C: numaGetIValue() fails with an error and leaves the index 0 */
        if (rankindex < 0 || rankindex >= nasort.Count) {
            Log.Error("numaGetIValue", "index not valid");
            return 0;
        }
        return nasort.GetIValue(rankindex);
    }

    // leptonica: pixcmapGetNearestIndex (colormap.c)
    /// <summary>Finds the index of the nearest color.</summary>
    /// <param name="rval">The red component of the target color.</param>
    /// <param name="gval">The green component of the target color.</param>
    /// <param name="bval">The blue component of the target color.</param>
    /// <param name="index">Receives the index of the nearest color, or <see cref="L.UNDEF"/> if the colormap is empty.</param>
    /// <remarks>
    /// (1) Returns the index of the exact color if possible, otherwise the
    ///     index of the color closest to the target color.
    /// (2) Nearest color is that which is the least sum-of-squares distance
    ///     from the target color.
    /// </remarks>
    public void GetNearestIndex(int rval, int gval, int bval, out int index)
    {
        index = L.UNDEF;
        int n = Count;

        int mindist = 3 * 255 * 255 + 1;
        for (int i = 0; i < n; i++) {
            int delta = array[i].Red - rval;
            int dist = delta * delta;
            delta = array[i].Green - gval;
            dist += delta * delta;
            delta = array[i].Blue - bval;
            dist += delta * delta;
            if (dist < mindist) {
                index = i;
                if (dist == 0)
                    break;
                mindist = dist;
            }
        }
    }

    // leptonica: pixcmapGetNearestGrayIndex (colormap.c)
    /// <summary>Finds the index of the nearest gray color.</summary>
    /// <param name="val">gray value to search for; in range [0, ... 255]</param>
    /// <param name="index">the index of the nearest color</param>
    /// <remarks>
    /// (1) This should be used on gray colormaps.  It uses only the
    ///     green value of the colormap.
    /// (2) Returns the index of the exact color if possible, otherwise the
    ///     index of the color closest to the target color.
    /// </remarks>
    public void GetNearestGrayIndex(int val, out int index)
    {
        index = 0;
        if (val < 0 || val > 255)
            throw Lept.Arg("pixcmapGetNearestGrayIndex", "val not in [0 ... 255]");

        int n = Count;
        int mindist = 256;
        for (int i = 0; i < n; i++) {
            int dist = array[i].Green - val;
            dist = Math.Abs(dist);   /* L_ABS */
            if (dist < mindist) {
                index = i;
                if (dist == 0)
                    break;
                mindist = dist;
            }
        }
    }

    // leptonica: pixcmapGetDistanceToColor (colormap.c)
    /// <summary>Returns the L2 distance (squared) between the color at index and the target color.</summary>
    /// <returns>the distance from the cmap entry to target</returns>
    public int GetDistanceToColor(int index, int rval, int gval, int bval)
    {
        int n = Count;
        if (index >= n)
            throw Lept.Arg("pixcmapGetDistanceToColor", "invalid index");

        int delta = array[index].Red - rval;
        int dist = delta * delta;
        delta = array[index].Green - gval;
        dist += delta * delta;
        delta = array[index].Blue - bval;
        dist += delta * delta;
        return dist;
    }

    // leptonica: pixcmapGetRangeValues (colormap.c)
    /// <summary>Returns the extreme values of a component (or the average) and their indices.</summary>
    /// <param name="select">L_SELECT_RED, L_SELECT_GREEN, L_SELECT_BLUE or L_SELECT_AVERAGE</param>
    /// <param name="minval">minimum value of component</param>
    /// <param name="maxval">maximum value of component</param>
    /// <param name="minindex">index of minimum value</param>
    /// <param name="maxindex">index of maximum value</param>
    /// <remarks>
    /// (1) Returns, for selected components (or the average), the
    ///     the extreme values (min and/or max) and their indices
    ///     that are found in the cmap.
    /// </remarks>
    public void GetRangeValues(int select, out int minval, out int maxval,
                               out int minindex, out int maxindex)
    {
        minval = L.UNDEF;
        maxval = L.UNDEF;
        minindex = L.UNDEF;
        maxindex = L.UNDEF;

        int imin = L.UNDEF;
        int imax = L.UNDEF;
        int minv = 100000;
        int maxv = -1;
        int n = Count;
        for (int i = 0; i < n; i++) {
            GetColor(i, out int rval, out int gval, out int bval);
            if (select == L.L_SELECT_RED) {
                if (rval < minv) {
                    minv = rval;
                    imin = i;
                }
                if (rval > maxv) {
                    maxv = rval;
                    imax = i;
                }
            } else if (select == L.L_SELECT_GREEN) {
                if (gval < minv) {
                    minv = gval;
                    imin = i;
                }
                if (gval > maxv) {
                    maxv = gval;
                    imax = i;
                }
            } else if (select == L.L_SELECT_BLUE) {
                if (bval < minv) {
                    minv = bval;
                    imin = i;
                }
                if (bval > maxv) {
                    maxv = bval;
                    imax = i;
                }
            } else if (select == L.L_SELECT_AVERAGE) {
                int aveval = (rval + gval + bval) / 3;
                if (aveval < minv) {
                    minv = aveval;
                    imin = i;
                }
                if (aveval > maxv) {
                    maxv = aveval;
                    imax = i;
                }
            } else {
                throw Lept.Arg("pixcmapGetRangeValues", "invalid selection");
            }
        }

        minval = minv;
        maxval = maxv;
        minindex = imin;
        maxindex = imax;
    }

    /*-------------------------------------------------------------*
     *                       Colormap conversion                   *
     *-------------------------------------------------------------*/
    // leptonica: pixcmapGrayToFalseColor (colormap.c)
    /// <summary>Creates a colormap that maps from gray to false colors.</summary>
    /// <param name="gamma">(factor) 0.0 or 1.0 for default; &gt; 1.0 for brighter; 2.0 is quite nice</param>
    /// <remarks>
    /// (1) This creates a colormap that maps from gray to false colors.
    ///     The colormap is modeled after the Matlap "jet" configuration.
    /// </remarks>
    public static PixColormap GrayToFalseColor(float gamma)
    {
        if (gamma <= 0.0) gamma = 1.0f;

            /* Generate curve for transition part of color map */
        int[] curve = new int[64];
        float invgamma = (float)(1.0 / gamma);
        for (int i = 0; i < 64; i++) {
            float x = (float)((float)i / 64.0);
            curve[i] = (int)(255.0 * MathF.Pow(x, invgamma) + 0.5);
        }

        PixColormap cmap = Create(8);
        for (int i = 0; i < 256; i++) {
            int rval, gval, bval;
            if (i < 32) {
                rval = 0;
                gval = 0;
                bval = curve[i + 32];
            } else if (i < 96) {   /* 32 - 95 */
                rval = 0;
                gval = curve[i - 32];
                bval = 255;
            } else if (i < 160) {  /* 96 - 159 */
                rval = curve[i - 96];
                gval = 255;
                bval = curve[159 - i];
            } else if (i < 224) {  /* 160 - 223 */
                rval = 255;
                gval = curve[223 - i];
                bval = 0;
            } else {  /* 224 - 255 */
                rval = curve[287 - i];
                gval = 0;
                bval = 0;
            }
            cmap.AddColor(rval, gval, bval);
        }

        return cmap;
    }

    // leptonica: pixcmapGrayToColor (colormap.c)
    /// <summary>Creates a colormap that maps from gray to a specific color.</summary>
    /// <remarks>
    /// (1) This creates a colormap that maps from gray to
    ///     a specific color.  In the mapping, each component
    ///     is faded to white, depending on the gray value.
    /// (2) In use, this is simply attached to a grayscale pix
    ///     to give it the input color.
    /// </remarks>
    public static PixColormap GrayToColor(uint color)
    {
        Pix.ExtractRGBValues(color, out int rval, out int gval, out int bval);
        PixColormap cmap = Create(8);
        for (int i = 0; i < 256; i++) {
            cmap.AddColor(rval + (i * (255 - rval)) / 255,
                          gval + (i * (255 - gval)) / 255,
                          bval + (i * (255 - bval)) / 255);
        }

        return cmap;
    }

    // leptonica: pixcmapColorToGray (colormap.c)
    /// <summary>Creates a gray colormap from an arbitrary colormap.</summary>
    /// <param name="rwt">non-negative; the weights should add to 1.0</param>
    /// <param name="gwt">non-negative; the weights should add to 1.0</param>
    /// <param name="bwt">non-negative; the weights should add to 1.0</param>
    /// <remarks>
    /// (1) This creates a gray colormap from an arbitrary colormap.
    /// (2) In use, attach the output gray colormap to the pix
    ///     (or a copy of it) that provided the input colormap.
    /// </remarks>
    public PixColormap ColorToGray(float rwt, float gwt, float bwt)
    {
        const string func = "pixcmapColorToGray";
        if (rwt < 0.0 || gwt < 0.0 || bwt < 0.0)
            throw Lept.Arg(func, "weights not all >= 0.0");

            /* Make sure the sum of weights is 1.0; otherwise, you can get
             * overflow in the gray value. */
        float sum = rwt + gwt + bwt;
        if (sum == 0.0) {
            Log.Warning(func, "all weights zero; setting equal to 1/3");
            rwt = gwt = bwt = 0.33333f;
            sum = 1.0f;
        }
        if (Math.Abs(sum - 1.0) > 0.0001) {  /* maintain ratios with sum == 1.0 */
            Log.Warning(func, "weights don't sum to 1; maintaining ratios");
            rwt = rwt / sum;
            gwt = gwt / sum;
            bwt = bwt / sum;
        }

        PixColormap cmapd = Copy();
        int n = cmapd.Count;
        for (int i = 0; i < n; i++) {
            cmapd.GetColor(i, out int rval, out int gval, out int bval);
            int val = (int)(rwt * rval + gwt * gval + bwt * bval + 0.5);
            cmapd.ResetColor(i, val, val, val);
        }

        return cmapd;
    }

    // leptonica: pixcmapConvertTo4 (colormap.c)
    /// <summary>Converts a 2 bpp colormap to 4 bpp.</summary>
    /// <returns>cmapd (4 bpp)</returns>
    /// <remarks>
    /// (1) This converts a 2 bpp colormap to 4 bpp.  The colors
    ///     are the same; the output colormap entry array has size 16.
    /// </remarks>
    public PixColormap ConvertTo4()
    {
        if (Depth != 2)
            throw Lept.Arg("pixcmapConvertTo4", "cmaps not for 2 bpp pix");

        PixColormap cmapd = Create(4);
        int n = Count;
        for (int i = 0; i < n; i++) {
            GetColor(i, out int rval, out int gval, out int bval);
            cmapd.AddColor(rval, gval, bval);
        }
        return cmapd;
    }

    // leptonica: pixcmapConvertTo8 (colormap.c)
    /// <summary>Converts a 2 bpp or 4 bpp colormap to 8 bpp.</summary>
    /// <returns>cmapd (8 bpp)</returns>
    /// <remarks>
    /// (1) This converts a 2 bpp or 4 bpp colormap to 8 bpp.  The colors
    ///     are the same; the output colormap entry array has size 256.
    /// </remarks>
    public PixColormap ConvertTo8()
    {
        int depth = Depth;
        if (depth == 8) return Copy();
        if (depth != 2 && depth != 4)
            throw Lept.Arg("pixcmapConvertTo8", "cmaps not 2 or 4 bpp");

        PixColormap cmapd = Create(8);
        int n = Count;
        for (int i = 0; i < n; i++) {
            GetColor(i, out int rval, out int gval, out int bval);
            cmapd.AddColor(rval, gval, bval);
        }
        return cmapd;
    }

    /*-------------------------------------------------------------*
     *                         Colormap I/O                        *
     *-------------------------------------------------------------*/
    /// <summary>Reads a serialized colormap from a file.</summary>
    /// <param name="filename">The path of the file to read.</param>
    /// <returns>The colormap read from the file.</returns>
    /// <exception cref="ArgumentException">The file cannot be opened or does not contain a valid colormap.</exception>
    // leptonica: pixcmapRead (colormap.c)
    public static PixColormap Read(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenReadStream(filename);
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("pixcmapRead", $"stream not opened: {filename}");
        }
        using (fp) {
            try {
                return ReadStream(fp);
            } catch (ArgumentException e) {
                throw new ArgumentException($"Error in pixcmapRead: cmap not read: {filename}", e);
            }
        }
    }

    // leptonica: pixcmapReadStream (colormap.c)
    /// <param name="fp">input stream</param>
    /// <remarks>
    /// (1) The text is read ahead from the stream.  If the stream is seekable,
    ///     it is left after the serialized data (and the whitespace that
    ///     follows it); otherwise the rest of the stream is consumed.
    /// </remarks>
    public static PixColormap ReadStream(Stream fp)
    {
        const string func = "pixcmapReadStream";
        CFloatScanner sc = new CFloatScanner(fp);
        try {
            int ret = sc.Scan("\nPixcmap: depth = %d bpp; %d colors\n", out object?[] v);
            if (ret != 2)
                throw Lept.Arg(func, "invalid cmap size");
            int depth = (int)v[0]!;
            int ncolors = (int)v[1]!;
            if ((depth != 1 && depth != 2 && depth != 4 && depth != 8) ||
                (ncolors < 2 || ncolors > 256))
                throw Lept.Arg(func, "invalid cmap size");
            sc.Scan("Color    R-val    G-val    B-val   Alpha\n", out _);
            sc.Scan("----------------------------------------\n", out _);

            PixColormap cmap = Create(depth);
            for (int i = 0; i < ncolors; i++) {
                if (sc.Scan("%3d       %3d      %3d      %3d      %3d\n", out v) != 5)
                    throw Lept.Arg(func, "invalid entry");
                cmap.AddRGBA((int)v[1]!, (int)v[2]!, (int)v[3]!, (int)v[4]!);
            }
            return cmap;
        } finally {
            sc.Release();
        }
    }

    // leptonica: pixcmapReadMem (colormap.c)
    /// <param name="data">serialization of pixcmap; in ascii</param>
    public static PixColormap ReadMem(ReadOnlySpan<byte> data)
    {
        using MemoryStream fp = new MemoryStream(data.ToArray(), false);
        try {
            return ReadStream(fp);
        } catch (ArgumentException) {
            Log.Error("pixcmapReadMem", "cmap not read");
            throw;
        }
    }

    /// <summary>Writes the colormap to a file in serialized form.</summary>
    /// <param name="filename">The path of the file to write.</param>
    /// <exception cref="ArgumentException">The file cannot be opened for writing.</exception>
    // leptonica: pixcmapWrite (colormap.c)
    public void Write(string filename)
    {
        FileStream fp;
        try {
            fp = Utils.FopenWriteStream(filename, "wb");
        } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) {
            throw Lept.Arg("pixcmapWrite", $"stream not opened: {filename}");
        }
        using (fp) {
            WriteStream(fp);
        }
    }

    // leptonica: pixcmapWriteStream (colormap.c)
    /// <param name="fp">output stream</param>
    public void WriteStream(Stream fp)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Serialize());
        fp.Write(bytes, 0, bytes.Length);
    }

    /// <summary>pixcmapWriteStream() to a text writer, with the same text.</summary>
    public void WriteStream(TextWriter fp)
    {
        fp.Write(Serialize());
    }

    private string Serialize()
    {
        ToArrays(out int[] rmap, out int[] gmap, out int[] bmap, out int[] amap);

        StringBuilder sb = new StringBuilder();
        sb.Append(CFormat.Sprintf("\nPixcmap: depth = %d bpp; %d colors\n", depth, n));
        sb.Append("Color    R-val    G-val    B-val   Alpha\n");
        sb.Append("----------------------------------------\n");
        for (int i = 0; i < n; i++)
            sb.Append(CFormat.Sprintf("%3d       %3d      %3d      %3d      %3d\n",
                                      i, rmap[i], gmap[i], bmap[i], amap[i]));
        sb.Append('\n');
        return sb.ToString();
    }

    // leptonica: pixcmapWriteMem (colormap.c)
    /// <summary>Serializes a pixcmap in memory and returns the ascii bytes.</summary>
    public byte[] WriteMem()
    {
        using MemoryStream fp = new MemoryStream();
        WriteStream(fp);
        return fp.ToArray();
    }

    /*----------------------------------------------------------------------*
     *               Extract colormap arrays and serialization              *
     *----------------------------------------------------------------------*/
    /// <summary>Extracts the colormap entries into separate component arrays.</summary>
    /// <param name="prmap">Receives the red components.</param>
    /// <param name="pgmap">Receives the green components.</param>
    /// <param name="pbmap">Receives the blue components.</param>
    /// <param name="pamap">Receives the alpha components.</param>
    // leptonica: pixcmapToArrays (colormap.c)
    public void ToArrays(out int[] prmap, out int[] pgmap, out int[] pbmap, out int[] pamap)
    {
        int ncolors = Count;
        int[] rmap = new int[ncolors];
        int[] gmap = new int[ncolors];
        int[] bmap = new int[ncolors];
        int[] amap = new int[ncolors];
        for (int i = 0; i < ncolors; i++) {
            rmap[i] = array[i].Red;
            gmap[i] = array[i].Green;
            bmap[i] = array[i].Blue;
            amap[i] = array[i].Alpha;
        }
        prmap = rmap;
        pgmap = gmap;
        pbmap = bmap;
        pamap = amap;
    }

    // leptonica: pixcmapToRGBTable (colormap.c)
    /// <param name="ncolors">size of table</param>
    /// <returns>table of rgba values for the colormap</returns>
    public uint[] ToRGBTable(out int ncolors)
    {
        ncolors = Count;
        uint[] tab = new uint[ncolors];

        for (int i = 0; i < ncolors; i++) {
            GetRGBA(i, out int rval, out int gval, out int bval, out int aval);
            Pix.ComposeRGBAPixel(rval, gval, bval, aval, out tab[i]);
        }
        return tab;
    }

    // leptonica: pixcmapSerializeToMemory (colormap.c)
    /// <param name="cpc">components/color: 3 for rgb, 4 for rgba</param>
    /// <param name="ncolors">number of colors in table</param>
    /// <returns>binary string, cpc bytes per color</returns>
    /// <remarks>
    /// (1) When serializing to store in a pdf, use cpc = 3.
    /// </remarks>
    public byte[] SerializeToMemory(int cpc, out int ncolors)
    {
        ncolors = 0;
        if (cpc != 3 && cpc != 4)
            throw Lept.Arg("pixcmapSerializeToMemory", "cpc not 3 or 4");

        ncolors = Count;
        byte[] data = new byte[(long)cpc * ncolors];

        for (int i = 0; i < ncolors; i++) {
            GetRGBA(i, out int rval, out int gval, out int bval, out int aval);
            data[cpc * i] = (byte)rval;
            data[cpc * i + 1] = (byte)gval;
            data[cpc * i + 2] = (byte)bval;
            if (cpc == 4)
                data[cpc * i + 3] = (byte)aval;
        }
        return data;
    }

    // leptonica: pixcmapDeserializeFromMemory (colormap.c)
    /// <param name="data">binary string, 3 or 4 bytes per color</param>
    /// <param name="cpc">components/color: 3 for rgb, 4 for rgba</param>
    /// <param name="ncolors">&gt; 0</param>
    public static PixColormap DeserializeFromMemory(ReadOnlySpan<byte> data, int cpc, int ncolors)
    {
        const string func = "pixcmapDeserializeFromMemory";
        if (cpc != 3 && cpc != 4)
            throw Lept.Arg(func, "cpc not 3 or 4");
        if (ncolors <= 0)
            throw Lept.Arg(func, "no entries");
        if (ncolors > 256)
            throw Lept.Arg(func, "ncolors > 256");

        int d;
        if (ncolors > 16)
            d = 8;
        else if (ncolors > 4)
            d = 4;
        else if (ncolors > 2)
            d = 2;
        else
            d = 1;
        PixColormap cmap = Create(d);
        for (int i = 0; i < ncolors; i++) {
            int rval = data[cpc * i];
            int gval = data[cpc * i + 1];
            int bval = data[cpc * i + 2];
            int aval;
            if (cpc == 4)
                aval = data[cpc * i + 3];
            else
                aval = 255;  /* opaque */
            cmap.AddRGBA(rval, gval, bval, aval);
        }

        return cmap;
    }

    // leptonica: pixcmapConvertToHex (colormap.c)
    /// <param name="data">binary serialized data</param>
    /// <param name="ncolors">in colormap</param>
    /// <returns>hexdata bracketed, space-separated ascii hex string</returns>
    /// <remarks>
    /// (1) The number of bytes in data is 3 * ncolors.
    /// (2) Output is in form:
    ///        &lt; r0g0b0 r1g1b1 ... rngnbn &gt;
    ///     where r0, g0, b0 ... are each 2 bytes of hex ascii
    /// (3) This is used in pdf files to express the colormap as an
    ///     array in ascii (human-readable) format.
    /// </remarks>
    public static string ConvertToHex(ReadOnlySpan<byte> data, int ncolors)
    {
        if (ncolors < 1)
            throw Lept.Arg("pixcmapConvertToHex", "no colors");

        StringBuilder hexdata = new StringBuilder(2 + (2 * 3 + 1) * ncolors + 1);
        hexdata.Append("< ");
        for (int i = 0; i < ncolors; i++) {
            hexdata.Append(CFormat.Sprintf("%02x", data[3 * i]));
            hexdata.Append(CFormat.Sprintf("%02x", data[3 * i + 1]));
            hexdata.Append(CFormat.Sprintf("%02x", data[3 * i + 2]));
            hexdata.Append(' ');
        }
        hexdata.Append('>');
        return hexdata.ToString();
    }

    /*-------------------------------------------------------------*
     *                     Colormap transforms                     *
     *-------------------------------------------------------------*/
    // leptonica: pixcmapGammaTRC (colormap.c)
    /// <summary>In-place gamma transform of the colormap.</summary>
    /// <param name="gamma">gamma correction; must be &gt; 0.0</param>
    /// <param name="minval">input value that gives 0 for output; can be &lt; 0</param>
    /// <param name="maxval">input value that gives 255 for output; can be &gt; 255</param>
    /// <remarks>
    /// (1) This is an in-place transform
    /// (2) See pixGammaTRC() and numaGammaTRC() in enhance.c
    ///     for description and use of transform
    /// </remarks>
    public void GammaTRC(float gamma, int minval, int maxval)
    {
        const string func = "pixcmapGammaTRC";
        if (gamma <= 0.0) {
            Log.Warning(func, "gamma must be > 0.0; setting to 1.0");
            gamma = 1.0f;
        }
        if (minval >= maxval)
            throw Lept.Arg(func, "minval not < maxval");

        if (gamma == 1.0 && minval == 0 && maxval == 255)  /* no-op */
            return;

        Numa nag = Numa.GammaTRC(gamma, minval, maxval);

        int ncolors = Count;
        for (int i = 0; i < ncolors; i++) {
            GetColor(i, out int rval, out int gval, out int bval);
            int trval = nag.GetIValue(rval);
            int tgval = nag.GetIValue(gval);
            int tbval = nag.GetIValue(bval);
            ResetColor(i, trval, tgval, tbval);
        }
    }

    // leptonica: pixcmapContrastTRC (colormap.c)
    /// <summary>In-place contrast transform of the colormap.</summary>
    /// <param name="factor">generally between 0.0 [no enhancement] and 1.0, but can be larger than 1.0</param>
    /// <remarks>
    /// (1) This is an in-place transform
    /// (2) See pixContrastTRC() and numaContrastTRC() in enhance.c
    ///     for description and use of transform
    /// </remarks>
    public void ContrastTRC(float factor)
    {
        if (factor < 0.0) {
            Log.Warning("pixcmapContrastTRC", "factor must be >= 0.0; setting to 0.0");
            factor = 0.0f;
        }

        Numa nac = Numa.ContrastTRC(factor);

        int ncolors = Count;
        for (int i = 0; i < ncolors; i++) {
            GetColor(i, out int rval, out int gval, out int bval);
            int trval = nac.GetIValue(rval);
            int tgval = nac.GetIValue(gval);
            int tbval = nac.GetIValue(bval);
            ResetColor(i, trval, tgval, tbval);
        }
    }

    // leptonica: pixcmapShiftIntensity (colormap.c)
    /// <summary>In-place proportional shift of the intensity of each color.</summary>
    /// <param name="fraction">between -1.0 and +1.0</param>
    /// <remarks>
    /// (1) This is an in-place transform
    /// (2) It does a proportional shift of the intensity for each color.
    /// (3) If fraction &lt; 0.0, it moves all colors towards (0,0,0).
    ///     This darkens the image.
    ///     If fraction &gt; 0.0, it moves all colors towards (255,255,255)
    ///     This fades the image.
    /// (4) The equivalent transform can be accomplished with pixcmapGammaTRC(),
    ///     but it is considerably more difficult (see numaGammaTRC()).
    /// </remarks>
    public void ShiftIntensity(float fraction)
    {
        if (fraction < -1.0 || fraction > 1.0)
            throw Lept.Arg("pixcmapShiftIntensity", "fraction not in [-1.0, 1.0]");

        int ncolors = Count;
        for (int i = 0; i < ncolors; i++) {
            GetColor(i, out int rval, out int gval, out int bval);
            if (fraction < 0.0)
                ResetColor(i,
                           (int)((1.0 + fraction) * rval),
                           (int)((1.0 + fraction) * gval),
                           (int)((1.0 + fraction) * bval));
            else
                ResetColor(i,
                           rval + (int)(fraction * (255 - rval)),
                           gval + (int)(fraction * (255 - gval)),
                           bval + (int)(fraction * (255 - bval)));
        }
    }

    // leptonica: pixcmapShiftByComponent (colormap.c)
    /// <summary>In-place implementation of pixelShiftByComponent() for each color.</summary>
    /// <param name="srcval">source color: 0xrrggbb00</param>
    /// <param name="dstval">target color: 0xrrggbb00</param>
    /// <remarks>
    /// (1) This is an in-place transform
    /// (2) It implements pixelShiftByComponent() for each color.
    ///     The mapping is specified by srcval and dstval.
    /// (3) If a component decreases, the component in the colormap
    ///     decreases by the same ratio.  Likewise for increasing, except
    ///     all ratios are taken with respect to the distance from 255.
    /// </remarks>
    public void ShiftByComponent(uint srcval, uint dstval)
    {
        int ncolors = Count;
        for (int i = 0; i < ncolors; i++) {
            GetColor(i, out int rval, out int gval, out int bval);
            uint newval = Pix.PixelShiftByComponent(rval, gval, bval, srcval, dstval);
            Pix.ExtractRGBValues(newval, out rval, out gval, out bval);
            ResetColor(i, rval, gval, bval);
        }
    }

}
