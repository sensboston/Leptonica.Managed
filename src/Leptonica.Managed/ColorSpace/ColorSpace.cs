using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using static Leptonica.ArrayAccess;
using static Leptonica.L;

namespace Leptonica;

/*
 *  colorspace.c: Color space conversions.
 *
 *      Colorspace conversion between RGB and HSV
 *      Selection and display of range of colors in HSV space
 *      Colorspace conversion between RGB and YUV
 *      Colorspace conversion between RGB and XYZ
 *      Colorspace conversion between XYZ and LAB
 *      Colorspace conversion between RGB and LAB
 *      Gamut display of RGB colors
 *
 *  Differences from C:
 *    - convertRGBToHSV(), convertHSVToRGB() and the other value converters
 *      return void and have all outputs as out parameters; an invalid hue
 *      in convertHSVToRGB() throws.
 *    - pixFindHistoPeaksHSV() takes the debug Pixa from the caller (an empty
 *      Pixa) instead of the optional PIXA ** output; null skips the debug
 *      images.
 *    - lab_forward() and lab_reverse() are private.
 *    - The DEBUG_HISTO and SLOW_CUBE_ROOT options are not ported.
 *
 *  Not ported: nothing.
 */

/// <summary>Constants from pix.h used by colorspace.c.</summary>
public static partial class L
{
    /* Types of HSV histograms */
    /// <summary>Uses the hue-saturation histogram.</summary>
    public const int L_HS_HISTO = 1;

    /// <summary>Uses the hue-value histogram.</summary>
    public const int L_HV_HISTO = 2;

    /// <summary>Uses the saturation-value histogram.</summary>
    public const int L_SV_HISTO = 3;

    /* Region flags for pixMakeRangeMaskHS() etc. */

    /// <summary>Uses the pixels inside the specified HSV region.</summary>
    public const int L_INCLUDE_REGION = 1;

    /// <summary>Excludes the pixels inside the specified HSV region.</summary>
    public const int L_EXCLUDE_REGION = 2;
}

public sealed partial class Pix
{
    /*---------------------------------------------------------------------------*
     *                Colorspace conversion between RGB and HSV                  *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvertRGBToHSV (colorspace.c)
    /// <summary>Converts RGB to HSV.</summary>
    /// <param name="pixd">[optional] either null or equal to pixs (in-place)</param>
    /// <param name="pixs">32 bpp RGB, or colormapped</param>
    /// <returns>pixd always</returns>
    /// <remarks>
    /// (1) For pixs 32 bpp RGB, pixd is HSV in the same byte order:
    ///     hue in the red byte, saturation in the green byte, value in the blue byte.
    /// (2) The HSV image is not a real image; it is only an intermediate
    ///     data structure. The hue ranges over [0 ... 239], saturation and
    ///     value over [0 ... 255].
    /// (3) If pixs has a colormap, only the colormap is converted.
    /// </remarks>
    public static Pix ConvertRGBToHSV(Pix? pixd, Pix pixs)
    {
        const string func = "pixConvertRGBToHSV";

        if (pixd != null && pixd != pixs)
            throw Lept.Arg(func, "pixd defined and not inplace");
        int d = pixs.Depth;
        PixColormap? cmap = pixs.Colormap;
        if (cmap == null && d != 32)
            throw Lept.Arg(func, "not cmapped or rgb");

        if (cmap != null) {  /* just convert the colormap */
            if (pixd == null)
                pixd = Copy(null, pixs);
            pixd.Colormap!.ConvertRGBToHSV();
            return pixd;
        }

        if (pixd == null)
            pixd = CreateTemplateNoInit(pixs);

        /* Convert RGB image */
        pixd.GetDimensions(out int w, out int h, out _);
        int wpls = pixs.Wpl;
        int wpld = pixd.Wpl;
        uint[] datas = pixs.Data;
        uint[] datad = pixd.Data;
        LeptParallel.ForRows(h, w, (yStart, yEnd) => {
            for (int i = yStart; i < yEnd; i++) {
                int lines = i * wpls;
                int lined = i * wpld;
                for (int j = 0; j < w; j++) {
                    ExtractRGBValues(datas[lines + j], out int rval, out int gval, out int bval);
                    ConvertRGBToHSV(rval, gval, bval, out int hval, out int sval, out int vval);
                    datad[lined + j] = (uint)((hval << 24) | (sval << 16) | (vval << 8));
                }
            }
        });

        return pixd;
    }

    // leptonica: pixConvertHSVToRGB (colorspace.c)
    /// <summary>Converts HSV to RGB.</summary>
    /// <param name="pixd">[optional] either null or equal to pixs (in-place)</param>
    /// <param name="pixs">32 bpp HSV, or colormapped</param>
    /// <returns>pixd always</returns>
    /// <remarks>
    /// (1) For pixs 32 bpp HSV, pixd is RGB. See pixConvertRGBToHSV().
    /// (2) If pixs has a colormap, only the colormap is converted.
    /// </remarks>
    public static Pix ConvertHSVToRGB(Pix? pixd, Pix pixs)
    {
        const string func = "pixConvertHSVToRGB";

        if (pixd != null && pixd != pixs)
            throw Lept.Arg(func, "pixd defined and not inplace");
        int d = pixs.Depth;
        PixColormap? cmap = pixs.Colormap;
        if (cmap == null && d != 32)
            throw Lept.Arg(func, "not cmapped or hsv");

        if (cmap != null) {  /* just convert the colormap */
            if (pixd == null)
                pixd = Copy(null, pixs);
            pixd.Colormap!.ConvertHSVToRGB();
            return pixd;
        }

        if (pixd == null)
            pixd = CreateTemplateNoInit(pixs);

        /* Convert HSV image */
        pixd.GetDimensions(out int w, out int h, out _);
        int wpls = pixs.Wpl;
        int wpld = pixd.Wpl;
        uint[] datas = pixs.Data;
        uint[] datad = pixd.Data;
        LeptParallel.ForRows(h, w, (yStart, yEnd) => {
            for (int i = yStart; i < yEnd; i++) {
                int lines = i * wpls;
                int lined = i * wpld;
                for (int j = 0; j < w; j++) {
                    uint pixel = datas[lines + j];
                    int hval = (int)(pixel >> 24);
                    int sval = (int)((pixel >> 16) & 0xff);
                    int vval = (int)((pixel >> 8) & 0xff);
                    ConvertHSVToRGB(hval, sval, vval, out int rval, out int gval, out int bval);
                    ComposeRGBPixel(rval, gval, bval, out datad[lined + j]);
                }
            }
        });

        return pixd;
    }

    // leptonica: convertRGBToHSV (colorspace.c)
    /// <summary>Converts RGB to HSV.</summary>
    /// <param name="rval">RGB input</param>
    /// <param name="gval">RGB input</param>
    /// <param name="bval">RGB input</param>
    /// <param name="phval">hue [0 ... 239]</param>
    /// <param name="psval">saturation [0 ... 255]</param>
    /// <param name="pvval">value [0 ... 255]</param>
    /// <remarks>
    /// (1) The range of returned values is:
    ///       h [0 ... 239]
    ///       s [0 ... 255]
    ///       v [0 ... 255]
    /// (2) If r = g = b, the pixel is gray (s = 0, h = 0).
    /// (3) Hue is the angle around the color wheel, scaled so that the
    ///     full circle is 240 units: red 0, yellow 40, green 80, cyan 120,
    ///     blue 160, magenta 200.
    /// </remarks>
    public static void ConvertRGBToHSV(int rval, int gval, int bval, out int phval, out int psval, out int pvval)
    {
        int minrg = Math.Min(rval, gval);
        int min = Math.Min(minrg, bval);
        int maxrg = Math.Max(rval, gval);
        int max = Math.Max(maxrg, bval);
        int delta = max - min;
        pvval = max;
        if (delta == 0) {  /* gray; no chroma */
            phval = 0;
            psval = 0;
        } else {
            float h;
            psval = (int)(255.0 * (float)delta / (float)max + 0.5);
            if (rval == max)  /* between magenta and yellow */
                h = (float)(gval - bval) / (float)delta;
            else if (gval == max)  /* between yellow and cyan */
                h = (float)(2.0 + (float)(bval - rval) / (float)delta);
            else  /* between cyan and magenta */
                h = (float)(4.0 + (float)(rval - gval) / (float)delta);
            h = (float)(h * 40.0);
            if (h < 0.0)
                h = (float)(h + 240.0);
            if (h >= 239.5)
                h = 0.0f;
            phval = (int)(h + 0.5);
        }
    }

    // leptonica: convertHSVToRGB (colorspace.c)
    /// <summary>Converts HSV to RGB.</summary>
    /// <param name="hval">HSV input</param>
    /// <param name="sval">HSV input</param>
    /// <param name="vval">HSV input</param>
    /// <param name="prval">RGB output</param>
    /// <param name="pgval">RGB output</param>
    /// <param name="pbval">RGB output</param>
    /// <remarks>
    /// (1) See convertRGBToHSV() for valid input range of HSV values and
    ///     their interpretation in color space.
    /// </remarks>
    public static void ConvertHSVToRGB(int hval, int sval, int vval, out int prval, out int pgval, out int pbval)
    {
        prval = 0;
        pgval = 0;
        pbval = 0;
        if (sval == 0) {  /* gray */
            prval = vval;
            pgval = vval;
            pbval = vval;
        } else {
            if (hval < 0 || hval > 240)
                throw Lept.Arg("convertHSVToRGB", "invalid hval");
            if (hval == 240)
                hval = 0;
            float h = (float)((float)hval / 40.0);
            int i = (int)h;
            float f = h - i;
            float s = (float)((float)sval / 255.0);
            int x = (int)(vval * (1.0 - s) + 0.5);
            int y = (int)(vval * (1.0 - s * f) + 0.5);
            int z = (int)(vval * (1.0 - s * (1.0 - f)) + 0.5);
            switch (i) {
                case 0:
                    prval = vval;
                    pgval = z;
                    pbval = x;
                    break;
                case 1:
                    prval = y;
                    pgval = vval;
                    pbval = x;
                    break;
                case 2:
                    prval = x;
                    pgval = vval;
                    pbval = z;
                    break;
                case 3:
                    prval = x;
                    pgval = y;
                    pbval = vval;
                    break;
                case 4:
                    prval = z;
                    pgval = x;
                    pbval = vval;
                    break;
                case 5:
                    prval = vval;
                    pgval = x;
                    pbval = y;
                    break;
                default:  /* none possible */
                    break;
            }
        }
    }

    // leptonica: pixConvertRGBToHue (colorspace.c)
    /// <summary>Returns the hue of each pixel as an 8 bpp image. The source must be 32 bpp RGB or colormapped.</summary>
    /// <returns>An 8 bpp image of hue values.</returns>
    /// <remarks>
    /// (1) The hue is the same as that used in pixConvertRGBToHSV().
    /// (2) The result for gray pixels (no chroma) is 0.
    /// </remarks>
    public Pix ConvertRGBToHue()
    {
        const string func = "pixConvertRGBToHue";

        GetDimensions(out int w, out int h, out int d);
        if (d != 32 && Colormap == null)
            throw Lept.Arg(func, "not cmapped or rgb");

        Pix pixt = RemoveColormap(REMOVE_CMAP_TO_FULL_COLOR);

        /* Convert RGB image */
        Pix pixd = CreateNoInit(w, h, 8);
        pixd.CopyResolution(this);
        int wplt = pixt.Wpl;
        uint[] datat = pixt.Data;
        int wpld = pixd.Wpl;
        uint[] datad = pixd.Data;
        LeptParallel.ForRows(h, w, (yStart, yEnd) => {
            ref uint tRef = ref MemoryMarshal.GetArrayDataReference(datat);
            ref uint dRef = ref MemoryMarshal.GetArrayDataReference(datad);
            for (int i = yStart; i < yEnd; i++) {
                ref uint linetRef = ref Unsafe.Add(ref tRef, i * wplt);
                ref uint linedRef = ref Unsafe.Add(ref dRef, i * wpld);
                if ((w & 3) != 0)
                    Unsafe.Add(ref linedRef, w >> 2) = 0;
                for (int j = 0; j < w; j++) {
                    uint pixel = Unsafe.Add(ref linetRef, j);
                    int rval = (int)(pixel >> 24);
                    int gval = (int)((pixel >> 16) & 0xffu);
                    int bval = (int)((pixel >> 8) & 0xffu);
                    int min = Math.Min(Math.Min(rval, gval), bval);
                    int max = Math.Max(Math.Max(rval, gval), bval);
                    int delta = max - min;
                    int hval;
                    if (delta == 0) {  /* gray; no chroma */
                        hval = 0;
                    } else {
                        float fh;
                        if (rval == max)  /* between magenta and yellow */
                            fh = (float)(gval - bval) / (float)delta;
                        else if (gval == max)  /* between yellow and cyan */
                            fh = (float)(2.0 + (float)(bval - rval) / (float)delta);
                        else  /* between cyan and magenta */
                            fh = (float)(4.0 + (float)(rval - gval) / (float)delta);
                        fh = (float)(fh * 40.0);
                        if (fh < 0.0)
                            fh = (float)(fh + 240.0);
                        hval = (int)(fh + 0.5);
                    }
                    SetDataByteUnchecked(ref linedRef, j, hval);
                }
            }
        });

        return pixd;
    }

    // leptonica: pixConvertRGBToSaturation (colorspace.c)
    /// <summary>Returns the saturation of each pixel as an 8 bpp image. The source must be 32 bpp RGB or colormapped.</summary>
    /// <returns>An 8 bpp image of saturation values.</returns>
    /// <remarks>
    /// (1) The saturation is the same as that used in pixConvertRGBToHSV().
    /// </remarks>
    public Pix ConvertRGBToSaturation()
    {
        const string func = "pixConvertRGBToSaturation";

        GetDimensions(out int w, out int h, out int d);
        if (d != 32 && Colormap == null)
            throw Lept.Arg(func, "not cmapped or rgb");

        Pix pixt = RemoveColormap(REMOVE_CMAP_TO_FULL_COLOR);

        /* Convert RGB image */
        Pix pixd = CreateNoInit(w, h, 8);
        pixd.CopyResolution(this);
        int wplt = pixt.Wpl;
        uint[] datat = pixt.Data;
        int wpld = pixd.Wpl;
        uint[] datad = pixd.Data;
        LeptParallel.ForRows(h, w, (yStart, yEnd) => {
            ref uint tRef = ref MemoryMarshal.GetArrayDataReference(datat);
            ref uint dRef = ref MemoryMarshal.GetArrayDataReference(datad);
            for (int i = yStart; i < yEnd; i++) {
                ref uint linetRef = ref Unsafe.Add(ref tRef, i * wplt);
                ref uint linedRef = ref Unsafe.Add(ref dRef, i * wpld);
                if ((w & 3) != 0)
                    Unsafe.Add(ref linedRef, w >> 2) = 0;
                for (int j = 0; j < w; j++) {
                    uint pixel = Unsafe.Add(ref linetRef, j);
                    int rval = (int)(pixel >> 24);
                    int gval = (int)((pixel >> 16) & 0xffu);
                    int bval = (int)((pixel >> 8) & 0xffu);
                    int min = Math.Min(Math.Min(rval, gval), bval);
                    int max = Math.Max(Math.Max(rval, gval), bval);
                    int delta = max - min;
                    int sval = delta == 0 ? 0 : (int)(255.0 * (float)delta / (float)max + 0.5);
                    SetDataByteUnchecked(ref linedRef, j, sval);
                }
            }
        });

        return pixd;
    }

    // leptonica: pixConvertRGBToValue (colorspace.c)
    /// <summary>Returns the value (maximum component) of each pixel as an 8 bpp image. The source must be 32 bpp RGB or colormapped.</summary>
    /// <returns>An 8 bpp image of the value channel.</returns>
    /// <remarks>
    /// (1) The value is the same as that used in pixConvertRGBToHSV():
    ///     the maximum of the three components.
    /// </remarks>
    public Pix ConvertRGBToValue()
    {
        const string func = "pixConvertRGBToValue";

        GetDimensions(out int w, out int h, out int d);
        if (d != 32 && Colormap == null)
            throw Lept.Arg(func, "not cmapped or rgb");

        Pix pixt = RemoveColormap(REMOVE_CMAP_TO_FULL_COLOR);

        /* Convert RGB image */
        Pix pixd = CreateNoInit(w, h, 8);
        pixd.CopyResolution(this);
        int wplt = pixt.Wpl;
        uint[] datat = pixt.Data;
        int wpld = pixd.Wpl;
        uint[] datad = pixd.Data;
        int w4 = w & ~3;
        LeptParallel.ForRows(h, w, (yStart, yEnd) => {
            ref uint tRef = ref MemoryMarshal.GetArrayDataReference(datat);
            ref uint dRef = ref MemoryMarshal.GetArrayDataReference(datad);
            Vector128<byte> shufR = Vector128.Create((byte)15, 11, 7, 3, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255);
            Vector128<byte> shufG = Vector128.Create((byte)14, 10, 6, 2, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255);
            Vector128<byte> shufB = Vector128.Create((byte)13, 9, 5, 1, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255);
            for (int i = yStart; i < yEnd; i++) {
                ref uint linetRef = ref Unsafe.Add(ref tRef, i * wplt);
                ref uint linedRef = ref Unsafe.Add(ref dRef, i * wpld);
                int j = 0;
                if (Vector128.IsHardwareAccelerated) {
                    for (; j < w4; j += 4) {
                        Vector128<byte> v = Vector128.LoadUnsafe(ref Unsafe.Add(ref linetRef, j)).AsByte();
                        Vector128<byte> vr = Vector128.Shuffle(v, shufR);
                        Vector128<byte> vg = Vector128.Shuffle(v, shufG);
                        Vector128<byte> vb = Vector128.Shuffle(v, shufB);
                        Unsafe.Add(ref linedRef, j >> 2) = Vector128.Max(vr, Vector128.Max(vg, vb)).AsUInt32().GetElement(0);
                    }
                }
                if (j < w) {
                    if ((w & 3) != 0)
                        Unsafe.Add(ref linedRef, w >> 2) = 0;
                    for (; j < w; j++) {
                        uint pixel = Unsafe.Add(ref linetRef, j);
                        int max = Math.Max((int)(pixel >> 24), Math.Max((int)((pixel >> 16) & 0xffu), (int)((pixel >> 8) & 0xffu)));
                        SetDataByteUnchecked(ref linedRef, j, max);
                    }
                }
            }
        });

        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *              Selection and display of range of colors in HSV              *
     *---------------------------------------------------------------------------*/
    // leptonica: pixMakeRangeMaskHS (colorspace.c)
    /// <summary>Makes a 1 bpp mask over the pixels in a hue-saturation region. The source must be 32 bpp RGB.</summary>
    /// <param name="huecenter">center value of hue range</param>
    /// <param name="huehw">half-width of hue range</param>
    /// <param name="satcenter">center value of saturation range</param>
    /// <param name="sathw">half-width of saturation range</param>
    /// <param name="regionflag">L_INCLUDE_REGION, L_EXCLUDE_REGION</param>
    /// <returns>1 bpp mask over pixels with the specified HS colors</returns>
    /// <remarks>
    /// (1) The pixels are converted to HSV, and the mask is made over pixels
    ///     whose hue and saturation are in the specified ranges. Hue wraps
    ///     around at 240.
    /// (2) With L_INCLUDE_REGION the mask is 1 for pixels in the range;
    ///     with L_EXCLUDE_REGION it is 0 for pixels in the range.
    /// </remarks>
    public Pix MakeRangeMaskHS(int huecenter, int huehw, int satcenter, int sathw, int regionflag)
    {
        const string func = "pixMakeRangeMaskHS";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not 32 bpp");
        if (regionflag != L_INCLUDE_REGION && regionflag != L_EXCLUDE_REGION)
            throw Lept.Arg(func, "invalid regionflag");

        /* Set up LUTs for hue and saturation.  These have the value 1
         * within the specified intervals of hue and saturation. */
        int[] hlut = new int[240];
        int[] slut = new int[256];
        int sstart = Math.Max(0, satcenter - sathw);
        int send = Math.Min(255, satcenter + sathw);
        for (int i = sstart; i <= send; i++)
            slut[i] = 1;
        int hstart = (huecenter - huehw + 240) % 240;
        int hend = (huecenter + huehw + 240) % 240;
        if (hstart < hend) {
            for (int i = hstart; i <= hend; i++)
                hlut[i] = 1;
        } else {  /* wrap */
            for (int i = hstart; i < 240; i++)
                hlut[i] = 1;
            for (int i = 0; i <= hend; i++)
                hlut[i] = 1;
        }

        /* Generate the mask */
        Pix pixt = ConvertRGBToHSV(null, this);
        GetDimensions(out int w, out int h, out _);
        Pix pixd = Create(w, h, 1);
        if (regionflag == L_INCLUDE_REGION)
            pixd.ClearAll();
        else  /* L_EXCLUDE_REGION */
            pixd.SetAll();
        uint[] datat = pixt.Data;
        uint[] datad = pixd.Data;
        int wplt = pixt.Wpl;
        int wpld = pixd.Wpl;
        for (int i = 0; i < h; i++) {
            int linet = i * wplt;
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                uint pixel = datat[linet + j];
                int hval = (int)((pixel >> L_RED_SHIFT) & 0xff);
                int sval = (int)((pixel >> L_GREEN_SHIFT) & 0xff);
                if (hlut[hval] == 1 && slut[sval] == 1) {
                    if (regionflag == L_INCLUDE_REGION)
                        SetDataBit(lined, j);
                    else  /* L_EXCLUDE_REGION */
                        ClearDataBit(lined, j);
                }
            }
        }

        return pixd;
    }

    // leptonica: pixMakeRangeMaskHV (colorspace.c)
    /// <summary>Makes a 1 bpp mask over the pixels in a hue-value region. The source must be 32 bpp RGB.</summary>
    /// <param name="huecenter">center value of hue range</param>
    /// <param name="huehw">half-width of hue range</param>
    /// <param name="valcenter">center value of max intensity range</param>
    /// <param name="valhw">half-width of max intensity range</param>
    /// <param name="regionflag">L_INCLUDE_REGION, L_EXCLUDE_REGION</param>
    /// <returns>1 bpp mask over pixels with the specified HV colors</returns>
    /// <remarks>
    /// (1) See pixMakeRangeMaskHS().
    /// </remarks>
    public Pix MakeRangeMaskHV(int huecenter, int huehw, int valcenter, int valhw, int regionflag)
    {
        const string func = "pixMakeRangeMaskHV";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not 32 bpp");
        if (regionflag != L_INCLUDE_REGION && regionflag != L_EXCLUDE_REGION)
            throw Lept.Arg(func, "invalid regionflag");

        /* Set up LUTs for hue and maximum intensity (val).  These have the
         * value 1 within the specified intervals of hue and value. */
        int[] hlut = new int[240];
        int[] vlut = new int[256];
        int vstart = Math.Max(0, valcenter - valhw);
        int vend = Math.Min(255, valcenter + valhw);
        for (int i = vstart; i <= vend; i++)
            vlut[i] = 1;
        int hstart = (huecenter - huehw + 240) % 240;
        int hend = (huecenter + huehw + 240) % 240;
        if (hstart < hend) {
            for (int i = hstart; i <= hend; i++)
                hlut[i] = 1;
        } else {
            for (int i = hstart; i < 240; i++)
                hlut[i] = 1;
            for (int i = 0; i <= hend; i++)
                hlut[i] = 1;
        }

        /* Generate the mask */
        Pix pixt = ConvertRGBToHSV(null, this);
        GetDimensions(out int w, out int h, out _);
        Pix pixd = Create(w, h, 1);
        if (regionflag == L_INCLUDE_REGION)
            pixd.ClearAll();
        else  /* L_EXCLUDE_REGION */
            pixd.SetAll();
        uint[] datat = pixt.Data;
        uint[] datad = pixd.Data;
        int wplt = pixt.Wpl;
        int wpld = pixd.Wpl;
        for (int i = 0; i < h; i++) {
            int linet = i * wplt;
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                uint pixel = datat[linet + j];
                int hval = (int)((pixel >> L_RED_SHIFT) & 0xff);
                int vval = (int)((pixel >> L_BLUE_SHIFT) & 0xff);
                if (hlut[hval] == 1 && vlut[vval] == 1) {
                    if (regionflag == L_INCLUDE_REGION)
                        SetDataBit(lined, j);
                    else  /* L_EXCLUDE_REGION */
                        ClearDataBit(lined, j);
                }
            }
        }

        return pixd;
    }

    // leptonica: pixMakeRangeMaskSV (colorspace.c)
    /// <summary>Makes a 1 bpp mask over the pixels in a saturation-value region. The source must be 32 bpp RGB.</summary>
    /// <param name="satcenter">center value of saturation range</param>
    /// <param name="sathw">half-width of saturation range</param>
    /// <param name="valcenter">center value of max intensity range</param>
    /// <param name="valhw">half-width of max intensity range</param>
    /// <param name="regionflag">L_INCLUDE_REGION, L_EXCLUDE_REGION</param>
    /// <returns>1 bpp mask over pixels with the specified SV colors</returns>
    /// <remarks>
    /// (1) See pixMakeRangeMaskHS().
    /// </remarks>
    public Pix MakeRangeMaskSV(int satcenter, int sathw, int valcenter, int valhw, int regionflag)
    {
        const string func = "pixMakeRangeMaskSV";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not 32 bpp");
        if (regionflag != L_INCLUDE_REGION && regionflag != L_EXCLUDE_REGION)
            throw Lept.Arg(func, "invalid regionflag");

        /* Set up LUTs for saturation and max intensity (val).  These have
         * the value 1 within the specified intervals of saturation and value. */
        int[] slut = new int[256];
        int[] vlut = new int[256];
        int sstart = Math.Max(0, satcenter - sathw);
        int send = Math.Min(255, satcenter + sathw);
        int vstart = Math.Max(0, valcenter - valhw);
        int vend = Math.Min(255, valcenter + valhw);
        for (int i = sstart; i <= send; i++)
            slut[i] = 1;
        for (int i = vstart; i <= vend; i++)
            vlut[i] = 1;

        /* Generate the mask */
        Pix pixt = ConvertRGBToHSV(null, this);
        GetDimensions(out int w, out int h, out _);
        Pix pixd = Create(w, h, 1);
        if (regionflag == L_INCLUDE_REGION)
            pixd.ClearAll();
        else  /* L_EXCLUDE_REGION */
            pixd.SetAll();
        uint[] datat = pixt.Data;
        uint[] datad = pixd.Data;
        int wplt = pixt.Wpl;
        int wpld = pixd.Wpl;
        for (int i = 0; i < h; i++) {
            int linet = i * wplt;
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (int j = 0; j < w; j++) {
                uint pixel = datat[linet + j];
                int sval = (int)((pixel >> L_GREEN_SHIFT) & 0xff);
                int vval = (int)((pixel >> L_BLUE_SHIFT) & 0xff);
                if (slut[sval] == 1 && vlut[vval] == 1) {
                    if (regionflag == L_INCLUDE_REGION)
                        SetDataBit(lined, j);
                    else  /* L_EXCLUDE_REGION */
                        ClearDataBit(lined, j);
                }
            }
        }

        return pixd;
    }

    // leptonica: pixMakeHistoHS (colorspace.c)
    /// <summary>Makes a 2D histogram of hue and saturation. The source must be a 32 bpp image in the HSV color space.</summary>
    /// <param name="factor">subsampling factor; integer</param>
    /// <param name="nahue">[optional] hue histogram</param>
    /// <param name="nasat">[optional] saturation histogram</param>
    /// <returns>32 bpp histogram in hue and saturation (240 rows, 256 columns), or null on error</returns>
    /// <remarks>
    /// (1) pixs is a 32 bpp image in HSV colorspace; hue is in the red
    ///     channel, saturation in the green channel.
    /// (2) In pixd, hue is the vertical axis (240 rows) and saturation the
    ///     horizontal (256 columns).
    /// (3) The histograms of hue and saturation are returned if requested
    ///     (nahue, nasat are optional out parameters in C; here they are always returned).
    /// </remarks>
    public Pix MakeHistoHS(int factor, out Numa nahue, out Numa nasat)
    {
        const string func = "pixMakeHistoHS";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not 32 bpp");
        nahue = Numa.Create(240);
        nahue.Count = 240;
        nasat = Numa.Create(256);
        nasat.Count = 256;

        Pix pixt;
        if (factor <= 1)
            pixt = Clone(this);
        else
            pixt = ScaleBySampling(1.0f / (float)factor, 1.0f / (float)factor);

        /* Create the hue-saturation histogram */
        Pix pixd = Create(256, 240, 32);
        uint[] lined32 = pixd.Data;
        int wpld = pixd.Wpl;
        pixt.GetDimensions(out int w, out int h, out _);
        uint[] datat = pixt.Data;
        int wplt = pixt.Wpl;
        for (int i = 0; i < h; i++) {
            int linet = i * wplt;
            for (int j = 0; j < w; j++) {
                uint pixel = datat[linet + j];
                int hval = (int)((pixel >> L_RED_SHIFT) & 0xff);
                int sval = (int)((pixel >> L_GREEN_SHIFT) & 0xff);
                nahue.ShiftValue(hval, 1.0f);
                nasat.ShiftValue(sval, 1.0f);
                uint nd = lined32[hval * wpld + sval];
                lined32[hval * wpld + sval] = nd + 1;
            }
        }

        return pixd;
    }

    // leptonica: pixMakeHistoHV (colorspace.c)
    /// <summary>Makes a 2D histogram of hue and value. The source must be a 32 bpp image in the HSV color space.</summary>
    /// <param name="factor">subsampling factor; integer</param>
    /// <param name="nahue">[optional] hue histogram</param>
    /// <param name="naval">[optional] value histogram</param>
    /// <returns>32 bpp histogram in hue and value (240 rows, 256 columns), or null on error</returns>
    /// <remarks>
    /// (1) See pixMakeHistoHS(). Value is in the blue channel.
    /// </remarks>
    public Pix MakeHistoHV(int factor, out Numa nahue, out Numa naval)
    {
        const string func = "pixMakeHistoHV";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not 32 bpp");
        nahue = Numa.Create(240);
        nahue.Count = 240;
        naval = Numa.Create(256);
        naval.Count = 256;

        Pix pixt;
        if (factor <= 1)
            pixt = Clone(this);
        else
            pixt = ScaleBySampling(1.0f / (float)factor, 1.0f / (float)factor);

        /* Create the hue-value histogram */
        Pix pixd = Create(256, 240, 32);
        uint[] lined32 = pixd.Data;
        int wpld = pixd.Wpl;
        pixt.GetDimensions(out int w, out int h, out _);
        uint[] datat = pixt.Data;
        int wplt = pixt.Wpl;
        for (int i = 0; i < h; i++) {
            int linet = i * wplt;
            for (int j = 0; j < w; j++) {
                uint pixel = datat[linet + j];
                int hval = (int)((pixel >> L_RED_SHIFT) & 0xff);
                int vval = (int)((pixel >> L_BLUE_SHIFT) & 0xff);
                nahue.ShiftValue(hval, 1.0f);
                naval.ShiftValue(vval, 1.0f);
                uint nd = lined32[hval * wpld + vval];
                lined32[hval * wpld + vval] = nd + 1;
            }
        }

        return pixd;
    }

    // leptonica: pixMakeHistoSV (colorspace.c)
    /// <summary>Makes a 2D histogram of saturation and value. The source must be a 32 bpp image in the HSV color space.</summary>
    /// <param name="factor">subsampling factor; integer</param>
    /// <param name="nasat">[optional] saturation histogram</param>
    /// <param name="naval">[optional] value histogram</param>
    /// <returns>32 bpp histogram in saturation and value (256 x 256), or null on error</returns>
    /// <remarks>
    /// (1) See pixMakeHistoHS(). Saturation is in the green channel,
    ///     value in the blue channel.
    /// </remarks>
    public Pix MakeHistoSV(int factor, out Numa nasat, out Numa naval)
    {
        const string func = "pixMakeHistoSV";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not 32 bpp");
        nasat = Numa.Create(256);
        nasat.Count = 256;
        naval = Numa.Create(256);
        naval.Count = 256;

        Pix pixt;
        if (factor <= 1)
            pixt = Clone(this);
        else
            pixt = ScaleBySampling(1.0f / (float)factor, 1.0f / (float)factor);

        /* Create the saturation-value histogram */
        Pix pixd = Create(256, 256, 32);
        uint[] lined32 = pixd.Data;
        int wpld = pixd.Wpl;
        pixt.GetDimensions(out int w, out int h, out _);
        uint[] datat = pixt.Data;
        int wplt = pixt.Wpl;
        for (int i = 0; i < h; i++) {
            int linet = i * wplt;
            for (int j = 0; j < w; j++) {
                uint pixel = datat[linet + j];
                int sval = (int)((pixel >> L_GREEN_SHIFT) & 0xff);
                int vval = (int)((pixel >> L_BLUE_SHIFT) & 0xff);
                nasat.ShiftValue(sval, 1.0f);
                naval.ShiftValue(vval, 1.0f);
                uint nd = lined32[sval * wpld + vval];
                lined32[sval * wpld + vval] = nd + 1;
            }
        }

        return pixd;
    }

    // leptonica: pixFindHistoPeaksHSV (colorspace.c)
    /// <summary>Finds the peaks in a 2D HSV histogram. The source must be a 32 bpp hue-saturation, hue-value or saturation-value histogram.</summary>
    /// <param name="type">L_HS_HISTO, L_HV_HISTO or L_SV_HISTO</param>
    /// <param name="width">half width of sliding window</param>
    /// <param name="height">half height of sliding window</param>
    /// <param name="npeaks">number of peaks to look for</param>
    /// <param name="erasefactor">ratio of erase window size to sliding window size</param>
    /// <param name="ppta">locations of peaks</param>
    /// <param name="pnatot">integrated peak areas</param>
    /// <param name="pixa">[optional] empty Pixa for debugging; null to skip</param>
    /// <remarks>
    /// (1) pixs is a 2D histogram of HS, HV or SV values, made by
    ///     pixMakeHistoHS() etc. A sliding window of size (2 * width + 1)
    ///     by (2 * height + 1) is used to find the peaks.
    /// (2) The input histogram is wrapped around in hue, for HS and HV.
    /// (3) Data for a maximum of npeaks is returned in pta and natot.
    /// </remarks>
    public void FindHistoPeaksHSV(int type, int width, int height, int npeaks, float erasefactor,
                                  out Pta ppta, out Numa pnatot, Pixa? pixa)
    {
        const string func = "pixFindHistoPeaksHSV";

        ppta = null!;
        pnatot = null!;
        if (Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not 32 bpp");
        if (type != L_HS_HISTO && type != L_HV_HISTO && type != L_SV_HISTO)
            throw Lept.Arg(func, "invalid HSV histo type");

        Pta pta = Pta.Create(npeaks);
        Numa natot = Numa.Create(npeaks);
        pnatot = natot;
        ppta = pta;

        Pix pixh;
        if (type == L_SV_HISTO)
            pixh = AddMirroredBorder(width + 1, width + 1, height + 1, height + 1);
        else  /* type == L_HS_HISTO or type == L_HV_HISTO */
            pixh = AddMixedBorder(width + 1, width + 1, height + 1, height + 1);

        /* Get the total count in the sliding window.  If the window
         * fully covers the peak, this will be the integrated
         * volume under the peak. */
        Pix pixw = pixh.WindowedMean(width, height, 1, 0);

        /* Sequentially identify and erase peaks in the histogram.
         * If requested for debugging, save a pixa of the sequence of
         * false color histograms. */
        for (int i = 0; i < npeaks; i++) {
            pixw.GetMaxValueInRect(null, out uint maxval, out int xmax, out int ymax);
            if (maxval == 0)
                break;
            natot.AddNumber((float)maxval);
            pta.AddPt(xmax, ymax);
            int ewidth = (int)(width * erasefactor);
            int eheight = (int)(height * erasefactor);
            Box box = Box.Create(xmax - ewidth, ymax - eheight, 2 * ewidth + 1, 2 * eheight + 1);
            if (pixa != null) {
                Pix pix1 = pixw.MaxDynamicRange(L_LINEAR_SCALE);
                pixa.AddPix(pix1, L_INSERT);
                Pix pix2 = pix1.ConvertGrayToFalseColor(1.0f);
                pixa.AddPix(pix2, L_INSERT);
                pix1 = pixw.MaxDynamicRange(L_LOG_SCALE);
                pix2 = pix1.ConvertGrayToFalseColor(1.0f);
                pixa.AddPix(pix2, L_INSERT);
                Pix pix3 = pix1.ConvertTo32();
                pix3.RenderHashBoxArb(box, 6, 2, L_NEG_SLOPE_LINE, 1, 255, 100, 100);
                pixa.AddPix(pix3, L_INSERT);
            }
            pixw.ClearInRect(box);

            if (type == L_HS_HISTO || type == L_HV_HISTO) {
                /* clear wraps at bottom and top */
                Box? box2;
                if (ymax - eheight < 0) {  /* overlap to bottom */
                    box2 = Box.Create(xmax - ewidth, 240 + ymax - eheight,
                                      2 * ewidth + 1, eheight - ymax);
                } else if (ymax + eheight > 239) {  /* overlap to top */
                    box2 = Box.Create(xmax - ewidth, 0, 2 * ewidth + 1,
                                      ymax + eheight - 239);
                } else {
                    box2 = null;
                }
                if (box2 != null)
                    pixw.ClearInRect(box2);
            }
        }
    }

    // leptonica: displayHSVColorRange (colorspace.c)
    /// <summary>Displays a sampled range of HSV colors.</summary>
    /// <param name="hval">hue center value; in [0 ... 240]</param>
    /// <param name="sval">saturation center value</param>
    /// <param name="vval">max intensity value</param>
    /// <param name="huehw">half-width of the hue range</param>
    /// <param name="sathw">half-width of the saturation range</param>
    /// <param name="nsamp">number of samples on each side of the center</param>
    /// <param name="factor">linear replication factor</param>
    /// <returns>32 bpp pix of the color range</returns>
    /// <remarks>
    /// (1) The total size of the color display is (2 * nsamp + 1) times
    ///     factor, in each direction.
    /// (2) Hue varies along the vertical, saturation along the horizontal.
    /// </remarks>
    public static Pix DisplayHSVColorRange(int hval, int sval, int vval, int huehw, int sathw, int nsamp, int factor)
    {
        const string func = "displayHSVColorRange";

        if (hval < 0 || hval > 240)
            throw Lept.Arg(func, "invalid hval");
        if (huehw < 5 || huehw > 120)
            throw Lept.Arg(func, "invalid huehw");
        if (sval - sathw < 0 || sval + sathw > 255)
            throw Lept.Arg(func, "invalid sval/sathw");
        if (nsamp < 1 || factor < 3)
            throw Lept.Arg(func, "invalid nsamp or rep. factor");
        if (vval < 0 || vval > 255)
            throw Lept.Arg(func, "invalid vval");

        int w = (2 * nsamp + 1);
        int huedelta = (int)((float)huehw / (float)nsamp);
        int satdelta = (int)((float)sathw / (float)nsamp);
        Pix pixt = Create(w, w, 32);
        for (int i = 0; i < w; i++) {
            int hue = hval + huedelta * (i - nsamp);
            if (hue < 0) hue += 240;
            if (hue >= 240) hue -= 240;
            for (int j = 0; j < w; j++) {
                int sat = sval + satdelta * (j - nsamp);
                ConvertHSVToRGB(hue, sat, vval, out int rval, out int gval, out int bval);
                pixt.SetRGBPixel(j, i, rval, gval, bval);
            }
        }

        Pix pixd = pixt.ExpandReplicate(factor);
        return pixd;
    }

    /*---------------------------------------------------------------------------*
     *                Colorspace conversion between RGB and YUV                  *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvertRGBToYUV (colorspace.c)
    /// <summary>Converts RGB to YUV.</summary>
    /// <param name="pixd">[optional] either null or equal to pixs (in-place)</param>
    /// <param name="pixs">32 bpp RGB, or colormapped</param>
    /// <returns>pixd always</returns>
    /// <remarks>
    /// (1) For pixs 32 bpp RGB, pixd is YUV in the same byte order: Y in the
    ///     red byte, U in the green byte, V in the blue byte.
    /// (2) The YUV image is not a real image; it is an intermediate data structure.
    /// (3) If pixs has a colormap, only the colormap is converted.
    /// </remarks>
    public static Pix ConvertRGBToYUV(Pix? pixd, Pix pixs)
    {
        const string func = "pixConvertRGBToYUV";

        if (pixd != null && pixd != pixs)
            throw Lept.Arg(func, "pixd defined and not inplace");
        int d = pixs.Depth;
        PixColormap? cmap = pixs.Colormap;
        if (cmap == null && d != 32)
            throw Lept.Arg(func, "not cmapped or rgb");

        if (cmap != null) {  /* just convert the colormap */
            if (pixd == null)
                pixd = Copy(null, pixs);
            pixd.Colormap!.ConvertRGBToYUV();
            return pixd;
        }

        if (pixd == null)
            pixd = CreateTemplateNoInit(pixs);

        /* Convert RGB image */
        pixd.GetDimensions(out int w, out int h, out _);
        int wpls = pixs.Wpl;
        int wpld = pixd.Wpl;
        uint[] datas = pixs.Data;
        uint[] datad = pixd.Data;
        LeptParallel.ForRows(h, w, (yStart, yEnd) => {
            for (int i = yStart; i < yEnd; i++) {
                int lines = i * wpls;
                int lined = i * wpld;
                for (int j = 0; j < w; j++) {
                    ExtractRGBValues(datas[lines + j], out int rval, out int gval, out int bval);
                    ConvertRGBToYUV(rval, gval, bval, out int yval, out int uval, out int vval);
                    datad[lined + j] = (uint)((yval << 24) | (uval << 16) | (vval << 8));
                }
            }
        });

        return pixd;
    }

    // leptonica: pixConvertYUVToRGB (colorspace.c)
    /// <summary>Converts YUV to RGB.</summary>
    /// <param name="pixd">[optional] either null or equal to pixs (in-place)</param>
    /// <param name="pixs">32 bpp YUV, or colormapped</param>
    /// <returns>pixd always</returns>
    /// <remarks>
    /// (1) For pixs 32 bpp YUV, pixd is RGB. See pixConvertRGBToYUV().
    /// (2) If pixs has a colormap, only the colormap is converted.
    /// </remarks>
    public static Pix ConvertYUVToRGB(Pix? pixd, Pix pixs)
    {
        const string func = "pixConvertYUVToRGB";

        if (pixd != null && pixd != pixs)
            throw Lept.Arg(func, "pixd defined and not inplace");
        int d = pixs.Depth;
        PixColormap? cmap = pixs.Colormap;
        if (cmap == null && d != 32)
            throw Lept.Arg(func, "not cmapped or hsv");

        if (cmap != null) {  /* just convert the colormap */
            if (pixd == null)
                pixd = Copy(null, pixs);
            pixd.Colormap!.ConvertYUVToRGB();
            return pixd;
        }

        if (pixd == null)
            pixd = CreateTemplateNoInit(pixs);

        /* Convert YUV image */
        pixd.GetDimensions(out int w, out int h, out _);
        int wpls = pixs.Wpl;
        int wpld = pixd.Wpl;
        uint[] datas = pixs.Data;
        uint[] datad = pixd.Data;
        LeptParallel.ForRows(h, w, (yStart, yEnd) => {
            for (int i = yStart; i < yEnd; i++) {
                int lines = i * wpls;
                int lined = i * wpld;
                for (int j = 0; j < w; j++) {
                    uint pixel = datas[lines + j];
                    int yval = (int)(pixel >> 24);
                    int uval = (int)((pixel >> 16) & 0xff);
                    int vval = (int)((pixel >> 8) & 0xff);
                    ConvertYUVToRGB(yval, uval, vval, out int rval, out int gval, out int bval);
                    ComposeRGBPixel(rval, gval, bval, out datad[lined + j]);
                }
            }
        });

        return pixd;
    }

    // leptonica: convertRGBToYUV (colorspace.c)
    /// <summary>Converts RGB to YUV.</summary>
    /// <param name="rval">RGB input</param>
    /// <param name="gval">RGB input</param>
    /// <param name="bval">RGB input</param>
    /// <param name="pyval">Y output</param>
    /// <param name="puval">U output</param>
    /// <param name="pvval">V output</param>
    /// <remarks>
    /// (1) The range of returned values is:
    ///       Y [16 ... 235]
    ///       U [16 ... 240]
    ///       V [16 ... 240]
    /// </remarks>
    public static void ConvertRGBToYUV(int rval, int gval, int bval, out int pyval, out int puval, out int pvval)
    {
        float norm = (float)(1.0 / 256.0);
        pyval = (int)(16.0 +
                    norm * (65.738 * rval + 129.057 * gval + 25.064 * bval) + 0.5);
        puval = (int)(128.0 +
                    norm * (-37.945 * rval - 74.494 * gval + 112.439 * bval) + 0.5);
        pvval = (int)(128.0 +
                    norm * (112.439 * rval - 94.154 * gval - 18.285 * bval) + 0.5);
    }

    // leptonica: convertYUVToRGB (colorspace.c)
    /// <summary>Converts YUV to RGB.</summary>
    /// <param name="yval">YUV input</param>
    /// <param name="uval">YUV input</param>
    /// <param name="vval">YUV input</param>
    /// <param name="prval">RGB output</param>
    /// <param name="pgval">RGB output</param>
    /// <param name="pbval">RGB output</param>
    /// <remarks>
    /// (1) The range of valid input values is:
    ///       Y [16 ... 235]
    ///       U [16 ... 240]
    ///       V [16 ... 240]
    /// (2) Conversions for values outside these ranges are clipped to [0 ... 255].
    /// </remarks>
    public static void ConvertYUVToRGB(int yval, int uval, int vval, out int prval, out int pgval, out int pbval)
    {
        float norm = (float)(1.0 / 256.0);
        float ym = (float)(yval - 16.0);
        float um = (float)(uval - 128.0);
        float vm = (float)(vval - 128.0);
        int rval = (int)(norm * (298.082 * ym + 408.583 * vm) + 0.5);
        int gval = (int)(norm * (298.082 * ym - 100.291 * um - 208.120 * vm) +
                         0.5);
        int bval = (int)(norm * (298.082 * ym + 516.411 * um) + 0.5);
        prval = Math.Min(255, Math.Max(0, rval));
        pgval = Math.Min(255, Math.Max(0, gval));
        pbval = Math.Min(255, Math.Max(0, bval));
    }

    /*---------------------------------------------------------------------------*
     *                Colorspace conversion between RGB and XYZ                  *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvertRGBToXYZ (colorspace.c)
    /// <summary>Converts RGB to XYZ. The source must be 32 bpp RGB.</summary>
    /// <returns>An array of three float images holding the X, Y and Z components.</returns>
    /// <remarks>
    /// (1) The [x,y,z] values are stored as float values, each in its own fpix.
    /// (2) The conversion is a linear transformation of the rgb values; see
    ///     convertRGBToXYZ().
    /// </remarks>
    public FPixa ConvertRGBToXYZ()
    {
        const string func = "pixConvertRGBToXYZ";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not rgb");

        /* Convert RGB image */
        GetDimensions(out int w, out int h, out _);
        FPixa fpixa = FPixa.Create(3);
        FPix fpix = null!;
        for (int i = 0; i < 3; i++) {
            fpix = FPix.Create(w, h);
            fpixa.AddFPix(fpix, L_INSERT);
        }
        int wpls = Wpl;
        int wpld = fpix.Wpl;
        uint[] datas = Data;
        float[] datax = fpixa.GetData(0);
        float[] datay = fpixa.GetData(1);
        float[] dataz = fpixa.GetData(2);
        for (int i = 0; i < h; i++) {
            int lines = i * wpls;
            int linex = i * wpld;
            int liney = i * wpld;
            int linez = i * wpld;
            for (int j = 0; j < w; j++) {
                ExtractRGBValues(datas[lines + j], out int rval, out int gval, out int bval);
                ConvertRGBToXYZ(rval, gval, bval, out float fxval, out float fyval, out float fzval);
                datax[linex + j] = fxval;
                datay[liney + j] = fyval;
                dataz[linez + j] = fzval;
            }
        }

        return fpixa;
    }

    // leptonica: fpixaConvertXYZToRGB (colorspace.c)
    /// <summary>Converts XYZ to RGB.</summary>
    /// <param name="fpixa">xyz</param>
    /// <returns>32 bpp rgb</returns>
    /// <remarks>
    /// (1) The xyz image is stored in three fpix. Out of gamut values are
    ///     clipped to the nearest rgb color.
    /// </remarks>
    public static Pix FpixaConvertXYZToRGB(FPixa fpixa)
    {
        const string func = "fpixaConvertXYZToRGB";

        if (fpixa.Count != 3)
            throw Lept.Arg(func, "fpixa undefined or invalid");

        /* Convert XYZ image */
        fpixa.GetFPixDimensions(0, out int w, out int h);
        Pix pixd = Create(w, h, 32);
        int wpld = pixd.Wpl;
        uint[] datad = pixd.Data;
        float[] datax = fpixa.GetData(0);
        float[] datay = fpixa.GetData(1);
        float[] dataz = fpixa.GetData(2);
        FPix fpix = fpixa.GetFPix(0, L_CLONE);
        int wpls = fpix.Wpl;
        for (int i = 0; i < h; i++) {
            int linex = i * wpls;
            int liney = i * wpls;
            int linez = i * wpls;
            int lined = i * wpld;
            for (int j = 0; j < w; j++) {
                float fxval = datax[linex + j];
                float fyval = datay[liney + j];
                float fzval = dataz[linez + j];
                ConvertXYZToRGB(fxval, fyval, fzval, 0, out int rval, out int gval, out int bval);
                ComposeRGBPixel(rval, gval, bval, out datad[lined + j]);
            }
        }

        return pixd;
    }

    // leptonica: convertRGBToXYZ (colorspace.c)
    /// <summary>Converts RGB to XYZ.</summary>
    /// <param name="rval">rgb input</param>
    /// <param name="gval">rgb input</param>
    /// <param name="bval">rgb input</param>
    /// <param name="pfxval">xyz output</param>
    /// <param name="pfyval">xyz output</param>
    /// <param name="pfzval">xyz output</param>
    /// <remarks>
    /// (1) The 3x3 matrix is applied to the rgb values, which are not
    ///     gamma-corrected; the white point is [242.37, 255.0, 277.69].
    /// </remarks>
    public static void ConvertRGBToXYZ(int rval, int gval, int bval, out float pfxval, out float pfyval, out float pfzval)
    {
        pfxval = (float)(0.4125 * rval + 0.3576 * gval + 0.1804 * bval);
        pfyval = (float)(0.2127 * rval + 0.7152 * gval + 0.0722 * bval);
        pfzval = (float)(0.0193 * rval + 0.1192 * gval + 0.9502 * bval);
    }

    // leptonica: convertXYZToRGB (colorspace.c)
    /// <summary>Converts XYZ to RGB.</summary>
    /// <param name="fxval">xyz input</param>
    /// <param name="fyval">xyz input</param>
    /// <param name="fzval">xyz input</param>
    /// <param name="blackout">0 to output nearest color if out of gamut; 1 to output black</param>
    /// <param name="prval">rgb output</param>
    /// <param name="pgval">rgb output</param>
    /// <param name="pbval">rgb output</param>
    /// <remarks>
    /// (1) For values of xyz that are out of gamut (not in the rgb cube),
    ///     either clip to the nearest rgb or output black.
    /// </remarks>
    public static void ConvertXYZToRGB(float fxval, float fyval, float fzval, int blackout,
                                       out int prval, out int pgval, out int pbval)
    {
        prval = pgval = pbval = 0;

        int rval = (int)(3.2405 * fxval - 1.5372 * fyval - 0.4985 * fzval + 0.5);
        int gval = (int)(-0.9693 * fxval + 1.8760 * fyval + 0.0416 * fzval + 0.5);
        int bval = (int)(0.0556 * fxval - 0.2040 * fyval + 1.0573 * fzval + 0.5);
        if (blackout == 0) {  /* the usual situation; use nearest rgb color */
            prval = Math.Max(0, Math.Min(rval, 255));
            pgval = Math.Max(0, Math.Min(gval, 255));
            pbval = Math.Max(0, Math.Min(bval, 255));
        } else {  /* use black for out of gamut */
            if (rval >= 0 && rval < 256 && gval >= 0 && gval < 256 &&
                bval >= 0 && bval < 256) {  /* in gamut */
                prval = rval;
                pgval = gval;
                pbval = bval;
            }
        }
    }

    /*---------------------------------------------------------------------------*
     *                Colorspace conversion between XYZ and LAB                  *
     *---------------------------------------------------------------------------*/
    // leptonica: fpixaConvertXYZToLAB (colorspace.c)
    /// <summary>Converts XYZ to LAB.</summary>
    /// <param name="fpixas">xyz</param>
    /// <returns>fpixa lab</returns>
    /// <remarks>
    /// (1) The input [x,y,z] and output [l,a,b] values are stored as
    ///     float values, each set in three fpix.
    /// (2) The CIE LAB color space was invented in 1976, as an
    ///     absolute reference for color measurement.
    /// </remarks>
    public static FPixa FpixaConvertXYZToLAB(FPixa fpixas)
    {
        const string func = "fpixaConvertXYZToLAB";

        if (fpixas.Count != 3)
            throw Lept.Arg(func, "fpixas undefined/invalid");

        /* Convert XYZ image */
        fpixas.GetFPixDimensions(0, out int w, out int h);
        FPixa fpixad = FPixa.Create(3);
        FPix fpix = null!;
        for (int i = 0; i < 3; i++) {
            fpix = FPix.Create(w, h);
            fpixad.AddFPix(fpix, L_INSERT);
        }
        int wpl = fpix.Wpl;
        float[] datax = fpixas.GetData(0);
        float[] datay = fpixas.GetData(1);
        float[] dataz = fpixas.GetData(2);
        float[] datal = fpixad.GetData(0);
        float[] dataa = fpixad.GetData(1);
        float[] datab = fpixad.GetData(2);

        /* Convert XYZ image */
        for (int i = 0; i < h; i++) {
            int linex = i * wpl;
            int liney = i * wpl;
            int linez = i * wpl;
            int linel = i * wpl;
            int linea = i * wpl;
            int lineb = i * wpl;
            for (int j = 0; j < w; j++) {
                float fxval = datax[linex + j];
                float fyval = datay[liney + j];
                float fzval = dataz[linez + j];
                ConvertXYZToLAB(fxval, fyval, fzval, out float flval, out float faval, out float fbval);
                datal[linel + j] = flval;
                dataa[linea + j] = faval;
                datab[lineb + j] = fbval;
            }
        }

        return fpixad;
    }

    // leptonica: fpixaConvertLABToXYZ (colorspace.c)
    /// <summary>Converts LAB to XYZ.</summary>
    /// <param name="fpixas">lab</param>
    /// <returns>fpixa xyz</returns>
    /// <remarks>
    /// (1) See fpixaConvertXYZToLAB() for the representation.
    /// </remarks>
    public static FPixa FpixaConvertLABToXYZ(FPixa fpixas)
    {
        const string func = "fpixaConvertLABToXYZ";

        if (fpixas.Count != 3)
            throw Lept.Arg(func, "fpixas undefined/invalid");

        /* Convert LAB image */
        fpixas.GetFPixDimensions(0, out int w, out int h);
        FPixa fpixad = FPixa.Create(3);
        FPix fpix = null!;
        for (int i = 0; i < 3; i++) {
            fpix = FPix.Create(w, h);
            fpixad.AddFPix(fpix, L_INSERT);
        }
        int wpl = fpix.Wpl;
        float[] datal = fpixas.GetData(0);
        float[] dataa = fpixas.GetData(1);
        float[] datab = fpixas.GetData(2);
        float[] datax = fpixad.GetData(0);
        float[] datay = fpixad.GetData(1);
        float[] dataz = fpixad.GetData(2);

        /* Convert XYZ image */
        for (int i = 0; i < h; i++) {
            int linel = i * wpl;
            int linea = i * wpl;
            int lineb = i * wpl;
            int linex = i * wpl;
            int liney = i * wpl;
            int linez = i * wpl;
            for (int j = 0; j < w; j++) {
                float flval = datal[linel + j];
                float faval = dataa[linea + j];
                float fbval = datab[lineb + j];
                ConvertLABToXYZ(flval, faval, fbval, out float fxval, out float fyval, out float fzval);
                datax[linex + j] = fxval;
                datay[liney + j] = fyval;
                dataz[linez + j] = fzval;
            }
        }

        return fpixad;
    }

    // leptonica: convertXYZToLAB (colorspace.c)
    /// <summary>Converts XYZ to LAB.</summary>
    /// <param name="xval">xyz input</param>
    /// <param name="yval">xyz input</param>
    /// <param name="zval">xyz input</param>
    /// <param name="plval">lab output</param>
    /// <param name="paval">lab output</param>
    /// <param name="pbval">lab output</param>
    /// <remarks>
    /// (1) The xyz values are first normalized by the white point
    ///     corresponding to rgb = (255, 255, 255).
    /// (2) Uses a rational approximation of the cube root; see lab_forward().
    /// </remarks>
    public static void ConvertXYZToLAB(float xval, float yval, float zval, out float plval, out float paval, out float pbval)
    {
        /* First normalize to the corresponding white values */
        float xn = (float)(0.0041259 * xval);
        float yn = (float)(0.0039216 * yval);
        float zn = (float)(0.0036012 * zval);

        /* Then apply the lab_forward function */
        float fx = lab_forward(xn);
        float fy = lab_forward(yn);
        float fz = lab_forward(zn);

        plval = (float)(116.0 * fy - 16.0);
        paval = (float)(500.0 * (fx - fy));
        pbval = (float)(200.0 * (fy - fz));
    }

    // leptonica: convertLABToXYZ (colorspace.c)
    /// <summary>Converts LAB to XYZ.</summary>
    /// <param name="lval">lab input</param>
    /// <param name="aval">lab input</param>
    /// <param name="bval">lab input</param>
    /// <param name="pxval">xyz output</param>
    /// <param name="pyval">xyz output</param>
    /// <param name="pzval">xyz output</param>
    /// <remarks>
    /// (1) The inverse of convertXYZToLAB(), with the white point scale
    ///     of the rgb white.
    /// </remarks>
    public static void ConvertLABToXYZ(float lval, float aval, float bval, out float pxval, out float pyval, out float pzval)
    {
        float xw = 242.37f;  /* x component corresponding to rgb white */
        float yw = 255.0f;   /* y component corresponding to rgb white */
        float zw = 277.69f;  /* z component corresponding to rgb white */

        float fy = (float)(0.0086207 * (16.0 + lval));
        float fx = (float)(fy + 0.002 * aval);
        float fz = (float)(fy - 0.005 * bval);
        pxval = xw * lab_reverse(fx);
        pyval = yw * lab_reverse(fy);
        pzval = zw * lab_reverse(fz);
    }

    /*
     * See http://en.wikipedia.org/wiki/Lab_color_space for formulas.
     * The cube root is replaced by a rational polynomial approximation.
     */
    // leptonica: lab_forward (colorspace.c)
    private static float lab_forward(float v)
    {
        const float f_thresh = 0.008856f;  /* (6/29)^3  */
        const float f_factor = 7.787f;     /* (1/3) * (29/6)^2)  */
        const float f_offset = 0.13793f;   /* 4/29 */

        if (v > f_thresh) {
            float num = (float)(4.37089e-04 + v * (9.52695e-02 + v * (1.25201 + v * 1.30273)));
            float den = (float)(3.91236e-03 + v * (2.95408e-01 + v * (1.71714 + v * 6.34341e-01)));
            return num / den;
        } else {
            return f_factor * v + f_offset;
        }
    }

    // leptonica: lab_reverse (colorspace.c)
    private static float lab_reverse(float v)
    {
        const float r_thresh = 0.20690f;  /* 6/29  */
        const float r_factor = 0.12842f;  /* 3 * (6/29)^2   */
        const float r_offset = 0.13793f;  /* 4/29 */

        if (v > r_thresh) {
            return v * v * v;
        } else {
            return r_factor * (v - r_offset);
        }
    }

    /*---------------------------------------------------------------------------*
     *                Colorspace conversion between RGB and LAB                  *
     *---------------------------------------------------------------------------*/
    // leptonica: pixConvertRGBToLAB (colorspace.c)
    /// <summary>Converts RGB to LAB. The source must be 32 bpp RGB.</summary>
    /// <returns>An array of three float images holding the L, a and b components.</returns>
    /// <remarks>
    /// (1) The [l,a,b] values are stored as float values, each in its own fpix.
    /// </remarks>
    public FPixa ConvertRGBToLAB()
    {
        const string func = "pixConvertRGBToLAB";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs undefined or not rgb");

        /* Convert RGB image */
        GetDimensions(out int w, out int h, out _);
        FPixa fpixa = FPixa.Create(3);
        FPix fpix = null!;
        for (int i = 0; i < 3; i++) {
            fpix = FPix.Create(w, h);
            fpixa.AddFPix(fpix, L_INSERT);
        }
        int wpls = Wpl;
        int wpld = fpix.Wpl;
        uint[] datas = Data;
        float[] datal = fpixa.GetData(0);
        float[] dataa = fpixa.GetData(1);
        float[] datab = fpixa.GetData(2);
        for (int i = 0; i < h; i++) {
            int lines = i * wpls;
            int linel = i * wpld;
            int linea = i * wpld;
            int lineb = i * wpld;
            for (int j = 0; j < w; j++) {
                ExtractRGBValues(datas[lines + j], out int rval, out int gval, out int bval);
                ConvertRGBToLAB(rval, gval, bval, out float flval, out float faval, out float fbval);
                datal[linel + j] = flval;
                dataa[linea + j] = faval;
                datab[lineb + j] = fbval;
            }
        }

        return fpixa;
    }

    // leptonica: fpixaConvertLABToRGB (colorspace.c)
    /// <summary>Converts LAB to RGB.</summary>
    /// <param name="fpixa">lab</param>
    /// <returns>32 bpp rgb</returns>
    /// <remarks>
    /// (1) Out of gamut values are clipped to the nearest rgb color.
    /// </remarks>
    public static Pix FpixaConvertLABToRGB(FPixa fpixa)
    {
        const string func = "fpixaConvertLABToRGB";

        if (fpixa.Count != 3)
            throw Lept.Arg(func, "fpixa undefined or invalid");

        /* Convert LAB image */
        fpixa.GetFPixDimensions(0, out int w, out int h);
        Pix pixd = Create(w, h, 32);
        int wpld = pixd.Wpl;
        uint[] datad = pixd.Data;
        float[] datal = fpixa.GetData(0);
        float[] dataa = fpixa.GetData(1);
        float[] datab = fpixa.GetData(2);
        FPix fpix = fpixa.GetFPix(0, L_CLONE);
        int wpls = fpix.Wpl;
        for (int i = 0; i < h; i++) {
            int linel = i * wpls;
            int linea = i * wpls;
            int lineb = i * wpls;
            int lined = i * wpld;
            for (int j = 0; j < w; j++) {
                float flval = datal[linel + j];
                float faval = dataa[linea + j];
                float fbval = datab[lineb + j];
                ConvertLABToRGB(flval, faval, fbval, out int rval, out int gval, out int bval);
                ComposeRGBPixel(rval, gval, bval, out datad[lined + j]);
            }
        }

        return pixd;
    }

    // leptonica: convertRGBToLAB (colorspace.c)
    /// <summary>Converts RGB to LAB, through XYZ.</summary>
    /// <param name="rval">rgb input</param>
    /// <param name="gval">rgb input</param>
    /// <param name="bval">rgb input</param>
    /// <param name="pflval">lab output</param>
    /// <param name="pfaval">lab output</param>
    /// <param name="pfbval">lab output</param>
    public static void ConvertRGBToLAB(int rval, int gval, int bval, out float pflval, out float pfaval, out float pfbval)
    {
        ConvertRGBToXYZ(rval, gval, bval, out float fxval, out float fyval, out float fzval);
        ConvertXYZToLAB(fxval, fyval, fzval, out pflval, out pfaval, out pfbval);
    }

    // leptonica: convertLABToRGB (colorspace.c)
    /// <summary>Converts LAB to RGB, through XYZ.</summary>
    /// <param name="flval">lab input</param>
    /// <param name="faval">lab input</param>
    /// <param name="fbval">lab input</param>
    /// <param name="prval">rgb output</param>
    /// <param name="pgval">rgb output</param>
    /// <param name="pbval">rgb output</param>
    public static void ConvertLABToRGB(float flval, float faval, float fbval, out int prval, out int pgval, out int pbval)
    {
        ConvertLABToXYZ(flval, faval, fbval, out float fxval, out float fyval, out float fzval);
        ConvertXYZToRGB(fxval, fyval, fzval, 0, out prval, out pgval, out pbval);
    }

    /*---------------------------------------------------------------------------*
     *                       Gamut display of RGB colors                         *
     *---------------------------------------------------------------------------*/
    // leptonica: pixMakeGamutRGB (colorspace.c)
    /// <summary>Makes a pix showing the rgb gamut: 32 tiles, one per blue value in steps of 8.</summary>
    /// <param name="scale">scale factor applied to the tiles; if &lt;= 0, uses 8</param>
    /// <returns>32 bpp image tiled in 8 columns</returns>
    /// <remarks>
    /// (1) Each tile is 32 x 32; red varies along the horizontal and
    ///     green along the vertical, in steps of 8.
    /// </remarks>
    public static Pix MakeGamutRGB(int scale)
    {
        if (scale <= 0) scale = 8;  /* default */
        Pixa pixa = Pixa.Create(32);
        for (int k = 0; k < 32; k++) {
            Pix pix1 = Create(32, 32, 32);
            for (int i = 0; i < 32; i++) {
                for (int j = 0; j < 32; j++) {
                    ComposeRGBPixel(8 * j, 8 * i, 8 * k, out uint val32);
                    pix1.SetPixel(j, i, val32);
                }
            }
            pixa.AddPix(pix1, L_INSERT);
        }
        Pix pix2 = pixa.DisplayTiledInColumns(8, scale, 5, 0);
        return pix2;
    }
}

public sealed partial class PixColormap
{
    // leptonica: pixcmapConvertRGBToHSV (colorspace.c)
    /// <summary>Converts the colormap in place from RGB to HSV.</summary>
    /// <remarks>
    /// (1) See Pix.ConvertRGBToHSV() for the interpretation of the values.
    /// </remarks>
    public void ConvertRGBToHSV()
    {
        int ncolors = Count;
        for (int i = 0; i < ncolors; i++) {
            GetColor(i, out int rval, out int gval, out int bval);
            Pix.ConvertRGBToHSV(rval, gval, bval, out int hval, out int sval, out int vval);
            ResetColor(i, hval, sval, vval);
        }
    }

    // leptonica: pixcmapConvertHSVToRGB (colorspace.c)
    /// <summary>Converts the colormap in place from HSV to RGB.</summary>
    public void ConvertHSVToRGB()
    {
        int ncolors = Count;
        for (int i = 0; i < ncolors; i++) {
            GetColor(i, out int hval, out int sval, out int vval);
            Pix.ConvertHSVToRGB(hval, sval, vval, out int rval, out int gval, out int bval);
            ResetColor(i, rval, gval, bval);
        }
    }

    // leptonica: pixcmapConvertRGBToYUV (colorspace.c)
    /// <summary>Converts the colormap in place from RGB to YUV.</summary>
    public void ConvertRGBToYUV()
    {
        int ncolors = Count;
        for (int i = 0; i < ncolors; i++) {
            GetColor(i, out int rval, out int gval, out int bval);
            Pix.ConvertRGBToYUV(rval, gval, bval, out int yval, out int uval, out int vval);
            ResetColor(i, yval, uval, vval);
        }
    }

    // leptonica: pixcmapConvertYUVToRGB (colorspace.c)
    /// <summary>Converts the colormap in place from YUV to RGB.</summary>
    public void ConvertYUVToRGB()
    {
        int ncolors = Count;
        for (int i = 0; i < ncolors; i++) {
            GetColor(i, out int yval, out int uval, out int vval);
            Pix.ConvertYUVToRGB(yval, uval, vval, out int rval, out int gval, out int bval);
            ResetColor(i, rval, gval, bval);
        }
    }
}
