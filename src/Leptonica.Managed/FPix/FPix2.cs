using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Leptonica;

/*
 *  fpix2.c: Interconversions between Pix, FPix and DPix, and arithmetic
 *           and geometric operations on FPix and DPix.
 *
 *      Interconversions between Pix, FPix and DPix
 *            Pix.ConvertToFPix, Pix.ConvertToDPix
 *            FPix.ConvertToPix, FPix.DisplayMaxDynamicRange
 *            FPix.ConvertToDPix
 *            DPix.ConvertToPix, DPix.ConvertToFPix
 *
 *      Min/max value
 *            FPix.GetMin, FPix.GetMax, DPix.GetMin, DPix.GetMax
 *
 *      Special integer scaling
 *            FPix.ScaleByInteger, DPix.ScaleByInteger
 *
 *      Arithmetic operations
 *            FPix.LinearCombination, FPix.AddMultConstant
 *            DPix.LinearCombination, DPix.AddMultConstant
 *
 *      Set all
 *            FPix.SetAllArbitrary, DPix.SetAllArbitrary
 *
 *      Border functions
 *            FPix.AddBorder, RemoveBorder, AddMirroredBorder,
 *            AddContinuedBorder, AddSlopeBorder
 *
 *      Simple rasterop
 *            FPix.Rasterop
 *
 *      Rotation by multiples of 90 degrees
 *            FPix.RotateOrth, Rotate180, Rotate90, FlipLR, FlipTB
 *
 *      Affine and projective interpolated transforms
 *            FPix.AffinePta, Affine, ProjectivePta, Projective
 *            FPix.LinearInterpolatePixelFloat
 *
 *      Thresholding to 1 bpp Pix
 *            FPix.ThresholdToPix
 *
 *      Generate function from components
 *            Pix.ComponentFunction
 *
 *  Notes on the port:
 *    - The data of FPix and DPix is a float[] / double[] with the C layout
 *      (wpl elements per row), so the pointer walks of the C code become
 *      index arithmetic.
 */

/// <summary>Constants from pix.h used by fpix2.c.</summary>
public static partial class L
{
    /// <summary>Takes the absolute value of negative values.</summary>
    public const int L_TAKE_ABSVAL = 2;
}

public sealed partial class Pix
{
    /*--------------------------------------------------------------------*
     *                     FPix  <-->  Pix conversions                    *
     *--------------------------------------------------------------------*/
    // leptonica: pixConvertToFPix (fpix2.c)
    /// <summary>Converts a 1, 2, 4, 8, 16 or 32 bpp image to a float image.</summary>
    /// <param name="ncomps">number of components: 3 for RGB, 1 otherwise</param>
    /// <returns>fpix</returns>
    /// <remarks>
    /// (1) If colormapped, remove to grayscale.
    /// (2) If 32 bpp and %ncomps == 3, this is RGB; convert to luminance.
    ///     In all other cases the src image is treated as having a single
    ///     component of pixel values.
    /// </remarks>
    public FPix ConvertToFPix(int ncomps)
    {
        const string func = "pixConvertToFPix";
        int w, h, d, i, j, wplt, wpld;
        uint uval;
        Pix pixt;
        FPix fpixd;

               /* Convert to a single component */
        if (Colormap != null)
            pixt = RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        else if (Depth == 32 && ncomps == 3)
            pixt = ConvertRGBToLuminance();
        else
            pixt = Clone(this);
        pixt.GetDimensions(out w, out h, out d);
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 16 && d != 32)
            throw Lept.Arg(func, "invalid depth");

        fpixd = FPix.CreateNoInit(w, h);
        uint[] datat = pixt.Data;
        wplt = pixt.Wpl;
        float[] datad = fpixd.Data;
        wpld = fpixd.Wpl;
        for (i = 0; i < h; i++) {
            ref uint rt = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datat), (nuint)(i * wplt));
            ref float rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(i * wpld));
            if (d == 8) {
                j = 0;
                if (Vector256.IsHardwareAccelerated && BitConverter.IsLittleEndian) {
                    ref byte bt = ref Unsafe.As<uint, byte>(ref rt);
                    for (; j + 16 <= w; j += 16) {
                        Vector128<byte> vb = SimdEndian.SwapBytesInWords(Vector128.LoadUnsafe(ref bt, (nuint)j));
                        (Vector128<ushort> uLo, Vector128<ushort> uHi) = Vector128.Widen(vb);
                        Vector256<ushort> vu16 = Vector256.Create(uLo, uHi);
                        (Vector256<uint> lo32, Vector256<uint> hi32) = Vector256.Widen(vu16);
                        Vector256.ConvertToSingle(lo32.AsInt32()).StoreUnsafe(ref rd, (nuint)j);
                        Vector256.ConvertToSingle(hi32.AsInt32()).StoreUnsafe(ref rd, (nuint)(j + 8));
                    }
                }
                for (; j < w; j++)
                    Unsafe.Add(ref rd, (nuint)j) = ArrayAccess.GetDataByteUnchecked(ref rt, j);
            } else if (d == 1) {
                for (j = 0; j < w; j++)
                    Unsafe.Add(ref rd, (nuint)j) = ArrayAccess.GetDataBitUnchecked(ref rt, j);
            } else if (d == 2) {
                for (j = 0; j < w; j++)
                    Unsafe.Add(ref rd, (nuint)j) = ArrayAccess.GetDataDibitUnchecked(ref rt, j);
            } else if (d == 4) {
                for (j = 0; j < w; j++)
                    Unsafe.Add(ref rd, (nuint)j) = ArrayAccess.GetDataQbitUnchecked(ref rt, j);
            } else if (d == 16) {
                for (j = 0; j < w; j++)
                    Unsafe.Add(ref rd, (nuint)j) = ArrayAccess.GetDataTwoBytesUnchecked(ref rt, j);
            } else {  /* d == 32 */
                for (j = 0; j < w; j++) {
                    uval = Unsafe.Add(ref rt, (nuint)j);
                    Unsafe.Add(ref rd, (nuint)j) = (float)uval;
                }
            }
        }

        return fpixd;
    }

    // leptonica: pixConvertToDPix (fpix2.c)
    /// <summary>Converts a 1, 2, 4, 8, 16 or 32 bpp image to a double image.</summary>
    /// <param name="ncomps">number of components: 3 for RGB, 1 otherwise</param>
    /// <returns>dpix</returns>
    /// <remarks>
    /// (1) If colormapped, remove to grayscale.
    /// (2) If 32 bpp and %ncomps == 3, this is RGB; convert to luminance.
    ///     In all other cases the src image is treated as having a single
    ///     component of pixel values.
    /// </remarks>
    public DPix ConvertToDPix(int ncomps)
    {
        const string func = "pixConvertToDPix";
        int w, h, d, i, j, wplt, wpld;
        uint uval;
        Pix pixt;
        DPix dpixd;

               /* Convert to a single component */
        if (Colormap != null)
            pixt = RemoveColormap(L.REMOVE_CMAP_TO_GRAYSCALE);
        else if (Depth == 32 && ncomps == 3)
            pixt = ConvertRGBToLuminance();
        else
            pixt = Clone(this);
        pixt.GetDimensions(out w, out h, out d);
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 16 && d != 32)
            throw Lept.Arg(func, "invalid depth");

        dpixd = DPix.CreateNoInit(w, h);
        uint[] datat = pixt.Data;
        wplt = pixt.Wpl;
        double[] datad = dpixd.Data;
        wpld = dpixd.Wpl;
        for (i = 0; i < h; i++) {
            ref uint rt = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datat), (nuint)(i * wplt));
            ref double rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(i * wpld));
            if (d == 8) {
                for (j = 0; j < w; j++)
                    Unsafe.Add(ref rd, (nuint)j) = ArrayAccess.GetDataByteUnchecked(ref rt, j);
            } else if (d == 1) {
                for (j = 0; j < w; j++)
                    Unsafe.Add(ref rd, (nuint)j) = ArrayAccess.GetDataBitUnchecked(ref rt, j);
            } else if (d == 2) {
                for (j = 0; j < w; j++)
                    Unsafe.Add(ref rd, (nuint)j) = ArrayAccess.GetDataDibitUnchecked(ref rt, j);
            } else if (d == 4) {
                for (j = 0; j < w; j++)
                    Unsafe.Add(ref rd, (nuint)j) = ArrayAccess.GetDataQbitUnchecked(ref rt, j);
            } else if (d == 16) {
                for (j = 0; j < w; j++)
                    Unsafe.Add(ref rd, (nuint)j) = ArrayAccess.GetDataTwoBytesUnchecked(ref rt, j);
            } else {  /* d == 32 */
                for (j = 0; j < w; j++) {
                    uval = Unsafe.Add(ref rt, (nuint)j);
                    Unsafe.Add(ref rd, (nuint)j) = (double)uval;
                }
            }
        }

        return dpixd;
    }

    /*--------------------------------------------------------------------*
     *                Generate function from components                   *
     *--------------------------------------------------------------------*/
    // leptonica: pixComponentFunction (fpix2.c)
    /// <summary>Stores a ratio of linear combinations of the rgb components of each pixel in an FPix.</summary>
    /// <param name="rnum">coefficient for numerator</param>
    /// <param name="gnum">coefficient for numerator</param>
    /// <param name="bnum">coefficient for numerator</param>
    /// <param name="rdenom">coefficient for denominator</param>
    /// <param name="gdenom">coefficient for denominator</param>
    /// <param name="bdenom">coefficient for denominator</param>
    /// <returns>fpixd</returns>
    /// <remarks>
    /// (1) This stores a function of the component values of each
    ///     input pixel in %fpixd.
    /// (2) The function is a ratio of linear combinations of component values.
    ///     There are two special cases for denominator coefficients:
    ///     (a) The denominator is 1.0: input 0 for all denominator coefficients
    ///     (b) Only one component is used in the denominator: input 1.0
    ///         for that denominator component and 0.0 for the other two.
    /// (3) If the denominator is 0, multiply by an arbitrary number that
    ///     is much larger than 1.  Choose 256 "arbitrarily".
    /// </remarks>
    public FPix ComponentFunction(float rnum, float gnum, float bnum,
                                  float rdenom, float gdenom, float bdenom)
    {
        const string func = "pixComponentFunction";
        int i, j, w, h, wpls, wpld, rval, gval, bval, zerodenom, onedenom;
        float fnum, fdenom;
        float[]? recip;
        FPix fpixd;

        if (Depth != 32)
            throw Lept.Arg(func, "pix undefined or not 32 bpp");

        GetDimensions(out w, out h, out _);
        uint[] datas = Data;
        wpls = Wpl;
        fpixd = FPix.Create(w, h);
        float[] datad = fpixd.Data;
        wpld = fpixd.Wpl;
        zerodenom = (rdenom == 0.0 && gdenom == 0.0 && bdenom == 0.0) ? 1 : 0;
        onedenom = ((rdenom == 1.0 && gdenom == 0.0 && bdenom == 0.0) ||
                    (rdenom == 0.0 && gdenom == 1.0 && bdenom == 0.0) ||
                    (rdenom == 0.0 && gdenom == 0.0 && bdenom == 1.0)) ? 1 : 0;
        recip = null;
        if (onedenom != 0) {
            recip = new float[256];
            recip[0] = 256;  /* arbitrary large number */
            for (i = 1; i < 256; i++)
                recip[i] = (float)(1.0 / (float)i);
        }
        for (i = 0; i < h; i++) {
            int lines = i * wpls;
            int lined = i * wpld;
            if (zerodenom != 0) {
                for (j = 0; j < w; j++) {
                    ExtractRGBValues(datas[lines + j], out rval, out gval, out bval);
                    datad[lined + j] = rnum * rval + gnum * gval + bnum * bval;
                }
            } else if (onedenom != 0 && rdenom == 1.0) {
                for (j = 0; j < w; j++) {
                    ExtractRGBValues(datas[lines + j], out rval, out gval, out bval);
                    datad[lined + j]
                        = recip![rval] * (rnum * rval + gnum * gval + bnum * bval);
                }
            } else if (onedenom != 0 && gdenom == 1.0) {
                for (j = 0; j < w; j++) {
                    ExtractRGBValues(datas[lines + j], out rval, out gval, out bval);
                    datad[lined + j]
                        = recip![gval] * (rnum * rval + gnum * gval + bnum * bval);
                }
            } else if (onedenom != 0 && bdenom == 1.0) {
                for (j = 0; j < w; j++) {
                    ExtractRGBValues(datas[lines + j], out rval, out gval, out bval);
                    datad[lined + j]
                        = recip![bval] * (rnum * rval + gnum * gval + bnum * bval);
                }
            } else {  /* general case */
                for (j = 0; j < w; j++) {
                    ExtractRGBValues(datas[lines + j], out rval, out gval, out bval);
                    fnum = rnum * rval + gnum * gval + bnum * bval;
                    fdenom = rdenom * rval + gdenom * gval + bdenom * bval;
                    datad[lined + j] = (float)((fdenom == 0) ? 256.0 * fnum : fnum / fdenom);
                }
            }
        }

        return fpixd;
    }
}

public sealed partial class FPix
{
    // leptonica: fpixConvertToPix (fpix2.c)
    /// <summary>Converts to a 8, 16 or 32 bpp pix.</summary>
    /// <param name="outdepth">0 (use default), 8, 16 or 32 bpp</param>
    /// <param name="negvals">L_CLIP_TO_ZERO, L_TAKE_ABSVAL</param>
    /// <param name="errorflag">1 to output error stats; 0 otherwise</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) Use %outdepth = 0 to programmatically determine the
    ///     output depth.  If no values are greater than 255,
    ///     it will set outdepth = 8; otherwise to 16 or 32.
    /// (2) Because we are converting a float to an unsigned int
    ///     with a specified dynamic range (8, 16 or 32 bits), errors
    ///     can occur.  If errorflag == TRUE, output the number
    ///     of values out of range, both negative and positive.
    /// (3) If a pixel value is positive and out of range, clip to
    ///     the maximum value represented at the outdepth of 8, 16
    ///     or 32 bits.
    /// </remarks>
    public Pix ConvertToPix(int outdepth, int negvals, int errorflag)
    {
        const string func = "fpixConvertToPix";
        int w, h, i, j, wpls, wpld;
        uint vald, maxval;
        float val;
        Pix pixd;

        if (negvals != L.L_CLIP_TO_ZERO && negvals != L.L_TAKE_ABSVAL)
            throw Lept.Arg(func, "invalid negvals");
        if (outdepth != 0 && outdepth != 8 && outdepth != 16 && outdepth != 32)
            throw Lept.Arg(func, "outdepth not in {0,8,16,32}");

        GetDimensions(out w, out h);
        float[] datas = Data;
        wpls = Wpl;

            /* Adaptive determination of output depth */
        if (outdepth == 0) {
            outdepth = 8;
            for (i = 0; i < h && outdepth < 32; i++) {
                int lines = i * wpls;
                for (j = 0; j < w && outdepth < 32; j++) {
                    if (datas[lines + j] > 65535.5)
                        outdepth = 32;
                    else if (datas[lines + j] > 255.5)
                        outdepth = 16;
                }
            }
        }
        if (outdepth == 8)
            maxval = 0xff;
        else if (outdepth == 16)
            maxval = 0xffff;
        else  /* outdepth == 32 */
            maxval = 0xffffffff;

            /* Gather statistics if %errorflag = TRUE */
        if (errorflag != 0) {
            int negs = 0;
            int overvals = 0;
            for (i = 0; i < h; i++) {
                int lines = i * wpls;
                for (j = 0; j < w; j++) {
                    val = datas[lines + j];
                    if (val < 0.0)
                        negs++;
                    else if (val > (float)maxval)
                        overvals++;
                }
            }
            if (negs > 0)
                Log.Error(func, $"Number of negative values: {negs}");
            if (overvals > 0)
                Log.Error(func, $"Number of too-large values: {overvals}");
        }

            /* Make the pix and convert the data */
        pixd = Pix.Create(w, h, outdepth);
        uint[] datad = pixd.Data;
        wpld = pixd.Wpl;
        for (i = 0; i < h; i++) {
            int lines = i * wpls;
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < w; j++) {
                val = datas[lines + j];
                if (val >= 0.0)
                    vald = (uint)(val + 0.5);
                else if (negvals == L.L_CLIP_TO_ZERO)  /* and val < 0.0 */
                    vald = 0;
                else
                    vald = (uint)(-val + 0.5);
                if (vald > maxval)
                    vald = maxval;

                if (outdepth == 8)
                    ArrayAccess.SetDataByte(lined, j, (int)vald);
                else if (outdepth == 16)
                    ArrayAccess.SetDataTwoBytes(lined, j, (int)vald);
                else  /* outdepth == 32 */
                    ArrayAccess.SetDataFourBytes(lined, j, vald);
            }
        }

        return pixd;
    }

    // leptonica: fpixDisplayMaxDynamicRange (fpix2.c)
    /// <summary>Converts to an 8 bpp pix using the maximum dynamic range.</summary>
    /// <returns>pixd 8 bpp</returns>
    public Pix DisplayMaxDynamicRange()
    {
        byte dval;
        int i, j, w, h, wpls, wpld;
        float factor, sval, maxval;
        Pix pixd;

        GetDimensions(out w, out h);
        float[] datas = Data;
        wpls = Wpl;

        maxval = 0.0f;
        for (i = 0; i < h; i++) {
            int lines = i * wpls;
            for (j = 0; j < w; j++) {
                sval = datas[lines + j];
                if (sval > maxval)
                    maxval = sval;
            }
        }

        pixd = Pix.Create(w, h, 8);
        if (maxval == 0.0)
            return pixd;  /* all pixels are 0 */

        uint[] datad = pixd.Data;
        wpld = pixd.Wpl;
        factor = (float)(255.0 / maxval);
        for (i = 0; i < h; i++) {
            int lines = i * wpls;
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < w; j++) {
                sval = datas[lines + j];
                if (sval < 0.0) sval = 0.0f;
                dval = (byte)(factor * sval + 0.5);
                ArrayAccess.SetDataByte(lined, j, dval);
            }
        }

        return pixd;
    }

    // leptonica: fpixConvertToDPix (fpix2.c)
    /// <summary>Converts to a DPix.</summary>
    /// <returns>dpix</returns>
    public DPix ConvertToDPix()
    {
        int w, h, i, j, wpls, wpld;
        float val;
        DPix dpix;

        GetDimensions(out w, out h);
        dpix = DPix.Create(w, h);

        float[] datas = Data;
        double[] datad = dpix.Data;
        wpls = Wpl;
        wpld = dpix.Wpl;  /* 8 byte words */
        for (i = 0; i < h; i++) {
            int lines = i * wpls;
            int lined = i * wpld;
            for (j = 0; j < w; j++) {
                val = datas[lines + j];
                datad[lined + j] = val;
            }
        }

        return dpix;
    }

    /*--------------------------------------------------------------------*
     *                           Min/max value                            *
     *--------------------------------------------------------------------*/
    // leptonica: fpixGetMin (fpix2.c)
    /// <summary>Minimum value and its location.</summary>
    /// <param name="pminval">min value</param>
    /// <param name="pxminloc">x location of min</param>
    /// <param name="pyminloc">y location of min</param>
    public void GetMin(out float pminval, out int pxminloc, out int pyminloc)
    {
        int i, j, w, h, wpl, xminloc, yminloc;
        float minval;

        minval = +1.0e20f;
        xminloc = 0;
        yminloc = 0;
        GetDimensions(out w, out h);
        float[] data = Data;
        wpl = Wpl;
        for (i = 0; i < h; i++) {
            int line = i * wpl;
            for (j = 0; j < w; j++) {
                if (data[line + j] < minval) {
                    minval = data[line + j];
                    xminloc = j;
                    yminloc = i;
                }
            }
        }

        pminval = minval;
        pxminloc = xminloc;
        pyminloc = yminloc;
    }

    // leptonica: fpixGetMax (fpix2.c)
    /// <summary>Maximum value and its location.</summary>
    /// <param name="pmaxval">max value</param>
    /// <param name="pxmaxloc">x location of max</param>
    /// <param name="pymaxloc">y location of max</param>
    public void GetMax(out float pmaxval, out int pxmaxloc, out int pymaxloc)
    {
        int i, j, w, h, wpl, xmaxloc, ymaxloc;
        float maxval;

        maxval = -1.0e20f;
        xmaxloc = 0;
        ymaxloc = 0;
        GetDimensions(out w, out h);
        float[] data = Data;
        wpl = Wpl;
        for (i = 0; i < h; i++) {
            int line = i * wpl;
            for (j = 0; j < w; j++) {
                if (data[line + j] > maxval) {
                    maxval = data[line + j];
                    xmaxloc = j;
                    ymaxloc = i;
                }
            }
        }

        pmaxval = maxval;
        pxmaxloc = xmaxloc;
        pymaxloc = ymaxloc;
    }

    /*--------------------------------------------------------------------*
     *                       Special integer scaling                      *
     *--------------------------------------------------------------------*/
    // leptonica: fpixScaleByInteger (fpix2.c)
    /// <summary>Scales by an integer factor, using bilinear interpolation.</summary>
    /// <param name="factor">integer scaling factor</param>
    /// <returns>fpixd interpolated result</returns>
    /// <remarks>
    /// (1) The width wd of fpixd is related to ws of fpixs by:
    ///         wd = factor * (ws - 1) + 1   (and ditto for the height)
    ///     We avoid special-casing boundary pixels in the interpolation
    ///     by constructing fpixd by inserting (factor - 1) interpolated
    ///     pixels between each pixel in fpixs.  Then
    ///          wd = ws + (ws - 1) * (factor - 1)    (same as above)
    ///     This also has the advantage that if we subsample by %factor,
    ///     throwing out all the interpolated pixels, we regain the
    ///     original low resolution fpix.
    /// </remarks>
    public FPix ScaleByInteger(int factor)
    {
        int i, j, k, m, ws, hs, wd, hd, wpls, wpld;
        float val0, val1, val2, val3;
        float[] fract;
        FPix fpixd;

        GetDimensions(out ws, out hs);
        wd = factor * (ws - 1) + 1;
        hd = factor * (hs - 1) + 1;
        fpixd = Create(wd, hd);
        float[] datas = Data;
        float[] datad = fpixd.Data;
        wpls = Wpl;
        wpld = fpixd.Wpl;
        fract = new float[factor];
        for (i = 0; i < factor; i++)
            fract[i] = i / (float)factor;
        for (i = 0; i < hs - 1; i++) {
            int lines = i * wpls;
            for (j = 0; j < ws - 1; j++) {
                val0 = datas[lines + j];
                val1 = datas[lines + j + 1];
                val2 = datas[lines + wpls + j];
                val3 = datas[lines + wpls + j + 1];
                for (k = 0; k < factor; k++) {  /* rows of sub-block */
                    int lined = (i * factor + k) * wpld;
                    for (m = 0; m < factor; m++) {  /* cols of sub-block */
                         datad[lined + j * factor + m] = (float)(
                                val0 * (1.0 - fract[m]) * (1.0 - fract[k]) +
                                val1 * fract[m] * (1.0 - fract[k]) +
                                val2 * (1.0 - fract[m]) * fract[k] +
                                val3 * fract[m] * fract[k]);
                    }
                }
            }
        }

            /* Do the right-most column of fpixd, skipping LR corner */
        for (i = 0; i < hs - 1; i++) {
            int lines = i * wpls;
            val0 = datas[lines + ws - 1];
            val1 = datas[lines + wpls + ws - 1];
            for (k = 0; k < factor; k++) {
                int lined = (i * factor + k) * wpld;
                datad[lined + wd - 1] = (float)(val0 * (1.0 - fract[k]) + val1 * fract[k]);
            }
        }

            /* Do the bottom-most row of fpixd */
        {
            int lines = (hs - 1) * wpls;
            int lined = (hd - 1) * wpld;
            for (j = 0; j < ws - 1; j++) {
                val0 = datas[lines + j];
                val1 = datas[lines + j + 1];
                for (m = 0; m < factor; m++)
                    datad[lined + j * factor + m] =
                        (float)(val0 * (1.0 - fract[m]) + val1 * fract[m]);
                datad[lined + wd - 1] = datas[lines + ws - 1];  /* LR corner */
            }
        }

        return fpixd;
    }

    /*--------------------------------------------------------------------*
     *                        Arithmetic operations                       *
     *--------------------------------------------------------------------*/
    // leptonica: fpixLinearCombination (fpix2.c)
    /// <summary>Computes the pixelwise linear combination a * src1 + b * src2.</summary>
    /// <param name="fpixd">[optional] this can be null, or equal to fpixs1</param>
    /// <param name="fpixs1">can be equal to fpixd</param>
    /// <param name="fpixs2"></param>
    /// <param name="a">multiplication factor on fpixs1</param>
    /// <param name="b">multiplication factor on fpixs2</param>
    /// <returns>fpixd always</returns>
    /// <remarks>
    /// (1) Computes pixelwise linear combination: a * src1 + b * src2
    /// (2) Alignment is to UL corner; src1 and src2 do not have to be
    ///     the same size.
    /// (3) There are 2 cases.  The result can go to a new dest, or
    ///     in-place to fpixs1:
    ///     * fpixd == null:   (src1 + src2) --> new fpixd
    ///     * fpixd == fpixs1:  (src1 + src2) --> src1  (in-place)
    /// </remarks>
    public static FPix LinearCombination(FPix? fpixd, FPix fpixs1, FPix fpixs2, float a, float b)
    {
        const string func = "fpixLinearCombination";
        int i, j, ws, hs, w, h, wpls, wpld;

        if (fpixd != null && fpixd != fpixs1)
            throw Lept.Arg(func, "invalid inplace operation");

        if (fpixd == null)
            fpixd = fpixs1.Copy();
        float[] datas = fpixs2.Data;
        float[] datad = fpixd.Data;
        wpls = fpixs2.Wpl;
        wpld = fpixd.Wpl;
        fpixs2.GetDimensions(out ws, out hs);
        fpixd.GetDimensions(out w, out h);
        w = Math.Min(ws, w);
        h = Math.Min(hs, h);
        Vector256<float> va = Vector256.Create(a);
        Vector256<float> vb = Vector256.Create(b);
        for (i = 0; i < h; i++) {
            ref float rs = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas), (nuint)(i * wpls));
            ref float rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(i * wpld));
            j = 0;
            if (Vector256.IsHardwareAccelerated) {
                for (; j + 8 <= w; j += 8) {
                    Vector256<float> vd = Vector256.LoadUnsafe(ref rd, (nuint)j);
                    Vector256<float> vs = Vector256.LoadUnsafe(ref rs, (nuint)j);
                    (va * vd + vb * vs).StoreUnsafe(ref rd, (nuint)j);
                }
            }
            for (; j < w; j++)
                Unsafe.Add(ref rd, (nuint)j) = a * Unsafe.Add(ref rd, (nuint)j) + b * Unsafe.Add(ref rs, (nuint)j);
        }

        return fpixd;
    }

    // leptonica: fpixAddMultConstant (fpix2.c)
    /// <summary>Adds and multiplies each pixel by constants, in place.</summary>
    /// <param name="addc">use 0.0 to skip the operation</param>
    /// <param name="multc">use 1.0 to skip the operation</param>
    /// <remarks>
    /// (1) This is an in-place operation.
    /// (2) It can be used to multiply each pixel by a constant,
    ///     and also to add a constant to each pixel.  Multiplication
    ///     is done first.
    /// </remarks>
    public void AddMultConstant(float addc, float multc)
    {
        int i, j, w, h, wpl;

        if (addc == 0.0 && multc == 1.0)
            return;

        GetDimensions(out w, out h);
        float[] data = Data;
        wpl = Wpl;
        Vector256<float> vAdd = Vector256.Create(addc);
        Vector256<float> vMult = Vector256.Create(multc);
        for (i = 0; i < h; i++) {
            ref float rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(data), (nuint)(i * wpl));
            j = 0;
            if (addc == 0.0) {
                if (Vector256.IsHardwareAccelerated) {
                    for (; j + 8 <= w; j += 8)
                        (Vector256.LoadUnsafe(ref rd, (nuint)j) * vMult).StoreUnsafe(ref rd, (nuint)j);
                }
                for (; j < w; j++)
                    Unsafe.Add(ref rd, (nuint)j) *= multc;
            } else if (multc == 1.0) {
                if (Vector256.IsHardwareAccelerated) {
                    for (; j + 8 <= w; j += 8)
                        (Vector256.LoadUnsafe(ref rd, (nuint)j) + vAdd).StoreUnsafe(ref rd, (nuint)j);
                }
                for (; j < w; j++)
                    Unsafe.Add(ref rd, (nuint)j) += addc;
            } else {
                if (Vector256.IsHardwareAccelerated) {
                    for (; j + 8 <= w; j += 8)
                        (vMult * Vector256.LoadUnsafe(ref rd, (nuint)j) + vAdd).StoreUnsafe(ref rd, (nuint)j);
                }
                for (; j < w; j++)
                    Unsafe.Add(ref rd, (nuint)j) = multc * Unsafe.Add(ref rd, (nuint)j) + addc;
            }
        }
    }

    /*--------------------------------------------------------------------*
     *                              Set all                               *
     *--------------------------------------------------------------------*/
    // leptonica: fpixSetAllArbitrary (fpix2.c)
    /// <summary>Sets all pixels to the same value.</summary>
    /// <param name="inval">to set at each pixel</param>
    public void SetAllArbitrary(float inval)
    {
        GetDimensions(out int w, out int h);
        Data.AsSpan(0, w * h).Fill(inval);
    }

    /*--------------------------------------------------------------------*
     *                          Border functions                          *
     *--------------------------------------------------------------------*/
    // leptonica: fpixAddBorder (fpix2.c)
    /// <summary>Adds a border of '0' 32-bit pixels.</summary>
    /// <param name="left">pixels to be added on the left</param>
    /// <param name="right">pixels to be added on the right</param>
    /// <param name="top">pixels to be added on the top</param>
    /// <param name="bot">pixels to be added on the bottom</param>
    /// <returns>fpixd</returns>
    public FPix AddBorder(int left, int right, int top, int bot)
    {
        int ws, hs, wd, hd;
        FPix fpixd;

        if (left <= 0 && right <= 0 && top <= 0 && bot <= 0)
            return Copy();
        GetDimensions(out ws, out hs);
        wd = ws + left + right;
        hd = hs + top + bot;
        fpixd = Create(wd, hd);

        fpixd.CopyResolution(this);
        fpixd.Rasterop(left, top, ws, hs, this, 0, 0);
        return fpixd;
    }

    // leptonica: fpixRemoveBorder (fpix2.c)
    /// <summary>Removes a border.</summary>
    /// <param name="left">pixels to be removed on the left</param>
    /// <param name="right">pixels to be removed on the right</param>
    /// <param name="top">pixels to be removed on the top</param>
    /// <param name="bot">pixels to be removed on the bottom</param>
    /// <returns>fpixd</returns>
    public FPix RemoveBorder(int left, int right, int top, int bot)
    {
        const string func = "fpixRemoveBorder";
        int ws, hs, wd, hd;
        FPix fpixd;

        if (left <= 0 && right <= 0 && top <= 0 && bot <= 0)
            return Copy();
        GetDimensions(out ws, out hs);
        wd = ws - left - right;
        hd = hs - top - bot;
        if (wd <= 0 || hd <= 0)
            throw Lept.Arg(func, "width & height not both > 0");
        fpixd = Create(wd, hd);

        fpixd.CopyResolution(this);
        fpixd.Rasterop(0, 0, wd, hd, this, left, top);
        return fpixd;
    }

    // leptonica: fpixAddMirroredBorder (fpix2.c)
    /// <summary>Adds a border whose pixels mirror the image at its edges.</summary>
    /// <param name="left">pixels to be added on the left</param>
    /// <param name="right">pixels to be added on the right</param>
    /// <param name="top">pixels to be added on the top</param>
    /// <param name="bot">pixels to be added on the bottom</param>
    /// <returns>fpixd</returns>
    /// <remarks>
    /// (1) See pixAddMirroredBorder() for situations of usage.
    /// </remarks>
    public FPix AddMirroredBorder(int left, int right, int top, int bot)
    {
        int i, j, w, h;
        FPix fpixd;

        fpixd = AddBorder(left, right, top, bot);
        GetDimensions(out w, out h);
        for (j = 0; j < left; j++)
            fpixd.Rasterop(left - 1 - j, top, 1, h,
                           fpixd, left + j, top);
        for (j = 0; j < right; j++)
            fpixd.Rasterop(left + w + j, top, 1, h,
                           fpixd, left + w - 1 - j, top);
        for (i = 0; i < top; i++)
            fpixd.Rasterop(0, top - 1 - i, left + w + right, 1,
                           fpixd, 0, top + i);
        for (i = 0; i < bot; i++)
            fpixd.Rasterop(0, top + h + i, left + w + right, 1,
                           fpixd, 0, top + h - 1 - i);

        return fpixd;
    }

    // leptonica: fpixAddContinuedBorder (fpix2.c)
    /// <summary>Adds a border whose pixels equal the closest boundary pixel.</summary>
    /// <param name="left">pixels to be added on the left</param>
    /// <param name="right">pixels to be added on the right</param>
    /// <param name="top">pixels to be added on the top</param>
    /// <param name="bot">pixels to be added on the bottom</param>
    /// <returns>fpixd</returns>
    /// <remarks>
    /// (1) This adds pixels on each side whose values are equal to
    ///     the value on the closest boundary pixel.
    /// </remarks>
    public FPix AddContinuedBorder(int left, int right, int top, int bot)
    {
        int i, j, w, h;
        FPix fpixd;

        fpixd = AddBorder(left, right, top, bot);
        GetDimensions(out w, out h);
        for (j = 0; j < left; j++)
            fpixd.Rasterop(j, top, 1, h, fpixd, left, top);
        for (j = 0; j < right; j++)
            fpixd.Rasterop(left + w + j, top, 1, h, fpixd, left + w - 1, top);
        for (i = 0; i < top; i++)
            fpixd.Rasterop(0, i, left + w + right, 1, fpixd, 0, top);
        for (i = 0; i < bot; i++)
            fpixd.Rasterop(0, top + h + i, left + w + right, 1,
                           fpixd, 0, top + h - 1);

        return fpixd;
    }

    // leptonica: fpixAddSlopeBorder (fpix2.c)
    /// <summary>Adds a border whose pixels continue the normal derivative at the edges.</summary>
    /// <param name="left">pixels to be added on the left</param>
    /// <param name="right">pixels to be added on the right</param>
    /// <param name="top">pixels to be added on the top</param>
    /// <param name="bot">pixels to be added on the bottom</param>
    /// <returns>fpixd</returns>
    /// <remarks>
    /// (1) This adds pixels on each side whose values have a normal
    ///     derivative equal to the normal derivative at the boundary
    ///     of fpixs.
    /// </remarks>
    public FPix AddSlopeBorder(int left, int right, int top, int bot)
    {
        int i, j, w, h, fullw, fullh;
        float val1, val2, del;
        FPix fpixd;

        fpixd = AddBorder(left, right, top, bot);
        GetDimensions(out w, out h);

            /* Left */
        for (i = top; i < top + h; i++) {
            fpixd.GetPixel(left, i, out val1);
            fpixd.GetPixel(left + 1, i, out val2);
            del = val1 - val2;
            for (j = 0; j < left; j++)
                fpixd.SetPixel(j, i, val1 + del * (left - j));
        }

            /* Right */
        fullw = left + w + right;
        for (i = top; i < top + h; i++) {
            fpixd.GetPixel(left + w - 1, i, out val1);
            fpixd.GetPixel(left + w - 2, i, out val2);
            del = val1 - val2;
            for (j = left + w; j < fullw; j++)
                fpixd.SetPixel(j, i, val1 + del * (j - left - w + 1));
        }

            /* Top */
        for (j = 0; j < fullw; j++) {
            fpixd.GetPixel(j, top, out val1);
            fpixd.GetPixel(j, top + 1, out val2);
            del = val1 - val2;
            for (i = 0; i < top; i++)
                fpixd.SetPixel(j, i, val1 + del * (top - i));
        }

            /* Bottom */
        fullh = top + h + bot;
        for (j = 0; j < fullw; j++) {
            fpixd.GetPixel(j, top + h - 1, out val1);
            fpixd.GetPixel(j, top + h - 2, out val2);
            del = val1 - val2;
            for (i = top + h; i < fullh; i++)
                fpixd.SetPixel(j, i, val1 + del * (i - top - h + 1));
        }

        return fpixd;
    }

    /*--------------------------------------------------------------------*
     *                          Simple rasterop                           *
     *--------------------------------------------------------------------*/
    // leptonica: fpixRasterop (fpix2.c)
    /// <summary>Copies a rectangle of fpixs into this fpix, with clipping.</summary>
    /// <param name="dx">x val of UL corner of dest rectangle</param>
    /// <param name="dy">y val of UL corner of dest rectangle</param>
    /// <param name="dw">width of dest rectangle</param>
    /// <param name="dh">height of dest rectangle</param>
    /// <param name="fpixs">src fpix</param>
    /// <param name="sx">x val of UL corner of src rectangle</param>
    /// <param name="sy">y val of UL corner of src rectangle</param>
    /// <remarks>
    /// (1) This is similar in structure to pixRasterop(), except
    ///     it only allows copying from the source into the destination.
    ///     For that reason, no op code is necessary.  Additionally,
    ///     all pixels are 32 bit words (float values), which makes
    ///     the copy very simple.
    /// (2) Clipping of both src and dest fpix are done automatically.
    /// (3) This allows in-place copying, without checking to see if
    ///     the result is valid:  use for in-place with caution!
    /// </remarks>
    public void Rasterop(int dx, int dy, int dw, int dh, FPix fpixs, int sx, int sy)
    {
        int fsw, fsh, fdw, fdh, dhangw, shangw, dhangh, shangh;
        int i, j, wpls, wpld;

        /* -------------------------------------------------------- *
         *      Clip to maximum rectangle with both src and dest    *
         * -------------------------------------------------------- */
        fpixs.GetDimensions(out fsw, out fsh);
        GetDimensions(out fdw, out fdh);

            /* First clip horizontally (sx, dx, dw) */
        if (dx < 0) {
            sx -= dx;  /* increase sx */
            dw += dx;  /* reduce dw */
            dx = 0;
        }
        if (sx < 0) {
            dx -= sx;  /* increase dx */
            dw += sx;  /* reduce dw */
            sx = 0;
        }
        dhangw = dx + dw - fdw;  /* rect overhang of dest to right */
        if (dhangw > 0)
            dw -= dhangw;  /* reduce dw */
        shangw = sx + dw - fsw;   /* rect overhang of src to right */
        if (shangw > 0)
            dw -= shangw;  /* reduce dw */

            /* Then clip vertically (sy, dy, dh) */
        if (dy < 0) {
            sy -= dy;  /* increase sy */
            dh += dy;  /* reduce dh */
            dy = 0;
        }
        if (sy < 0) {
            dy -= sy;  /* increase dy */
            dh += sy;  /* reduce dh */
            sy = 0;
        }
        dhangh = dy + dh - fdh;  /* rect overhang of dest below */
        if (dhangh > 0)
            dh -= dhangh;  /* reduce dh */
        shangh = sy + dh - fsh;  /* rect overhang of src below */
        if (shangh > 0)
            dh -= shangh;  /* reduce dh */

            /* if clipped entirely, quit */
        if ((dw <= 0) || (dh <= 0))
            return;

        /* -------------------------------------------------------- *
         *                    Copy block of data                    *
         * -------------------------------------------------------- */
        float[] datas = fpixs.Data;
        float[] datad = Data;
        wpls = fpixs.Wpl;
        wpld = Wpl;
        int bases = sy * wpls + sx;  /* at UL corner of block */
        int based = dy * wpld + dx;  /* at UL corner of block */
        for (i = 0; i < dh; i++) {
            int lines = bases + i * wpls;
            int lined = based + i * wpld;
            for (j = 0; j < dw; j++) {
                datad[lined] = datas[lines];
                lines++;
                lined++;
            }
        }
    }

    /*--------------------------------------------------------------------*
     *                   Rotation by multiples of 90 degrees              *
     *--------------------------------------------------------------------*/
    // leptonica: fpixRotateOrth (fpix2.c)
    /// <summary>Rotates by a multiple of 90 degrees.</summary>
    /// <param name="quads">0-3; number of 90 degree cw rotations</param>
    /// <returns>fpixd</returns>
    public FPix RotateOrth(int quads)
    {
        const string func = "fpixRotateOrth";
        if (quads < 0 || quads > 3)
            throw Lept.Arg(func, "quads not in {0,1,2,3}");

        if (quads == 0)
            return Copy();
        else if (quads == 1)
            return Rotate90(1);
        else if (quads == 2)
            return Rotate180(null, this);
        else /* quads == 3 */
            return Rotate90(-1);
    }

    // leptonica: fpixRotate180 (fpix2.c)
    /// <summary>Rotates by 180 degrees.</summary>
    /// <param name="fpixd">[optional] can be null, or equal to fpixs</param>
    /// <param name="fpixs"></param>
    /// <returns>fpixd</returns>
    /// <remarks>
    /// (1) This does a 180 rotation of the image about the center,
    ///     which is equivalent to a left-right flip about a vertical
    ///     line through the image center, followed by a top-bottom
    ///     flip about a horizontal line through the image center.
    /// (2) There are 2 cases for input:
    ///     (a) fpixd == null (creates a new fpixd)
    ///     (b) fpixd == fpixs (in-place operation)
    /// (3) For clarity, use these two patterns:
    ///     (a) fpixd = fpixRotate180(NULL, fpixs);
    ///     (b) fpixRotate180(fpixs, fpixs);
    /// </remarks>
    public static FPix Rotate180(FPix? fpixd, FPix fpixs)
    {
            /* Prepare pixd for in-place operation */
        if (fpixd == null)
            fpixd = fpixs.Copy();

        FlipLR(fpixd, fpixd);
        FlipTB(fpixd, fpixd);
        return fpixd;
    }

    // leptonica: fpixRotate90 (fpix2.c)
    /// <summary>Rotates by 90 degrees, cw or ccw.</summary>
    /// <param name="direction">1 = clockwise; -1 = counter-clockwise</param>
    /// <returns>fpixd</returns>
    /// <remarks>
    /// (1) This does a 90 degree rotation of the image about the center,
    ///     either cw or ccw, returning a new pix.
    /// (2) The direction must be either 1 (cw) or -1 (ccw).
    /// </remarks>
    public FPix Rotate90(int direction)
    {
        const string func = "fpixRotate90";
        int i, j, wd, hd, wpls, wpld;
        FPix fpixd;

        if (direction != 1 && direction != -1)
            throw Lept.Arg(func, "invalid direction");

        GetDimensions(out hd, out wd);
        fpixd = Create(wd, hd);
        fpixd.CopyResolution(this);

        float[] datas = Data;
        wpls = Wpl;
        float[] datad = fpixd.Data;
        wpld = fpixd.Wpl;
        if (direction == 1) {  /* clockwise */
            for (i = 0; i < hd; i++) {
                int lined = i * wpld;
                int lines = (wd - 1) * wpls;
                for (j = 0; j < wd; j++) {
                    datad[lined + j] = datas[lines + i];
                    lines -= wpls;
                }
            }
        } else {  /* ccw */
            for (i = 0; i < hd; i++) {
                int lined = i * wpld;
                int lines = 0;
                for (j = 0; j < wd; j++) {
                    datad[lined + j] = datas[lines + hd - 1 - i];
                    lines += wpls;
                }
            }
        }

        return fpixd;
    }

    // leptonica: fpixFlipLR (fpix2.c)
    /// <summary>Left-right flip.</summary>
    /// <param name="fpixd">[optional] can be null, or equal to fpixs</param>
    /// <param name="fpixs"></param>
    /// <returns>fpixd</returns>
    /// <remarks>
    /// (1) This does a left-right flip of the image, which is
    ///     equivalent to a rotation out of the plane about a
    ///     vertical line through the image center.
    /// (2) There are 2 cases for input:
    ///     (a) fpixd == null (creates a new fpixd)
    ///     (b) fpixd == fpixs (in-place operation)
    /// (3) For clarity, use these two patterns:
    ///     (a) fpixd = fpixFlipLR(NULL, fpixs);
    ///     (b) fpixFlipLR(fpixs, fpixs);
    /// </remarks>
    public static FPix FlipLR(FPix? fpixd, FPix fpixs)
    {
        int i, j, w, h, wpl;

            /* Prepare fpixd for in-place operation */
        if (fpixd == null)
            fpixd = fpixs.Copy();

        fpixd.GetDimensions(out w, out h);
        float[] data = fpixd.Data;
        wpl = fpixd.Wpl;  /* 4-byte words */
        float[] buffer = new float[wpl];
        for (i = 0; i < h; i++) {
            int line = i * wpl;
            Array.Copy(data, line, buffer, 0, wpl);
            for (j = 0; j < w; j++)
                data[line + j] = buffer[w - 1 - j];
        }
        return fpixd;
    }

    // leptonica: fpixFlipTB (fpix2.c)
    /// <summary>Top-bottom flip.</summary>
    /// <param name="fpixd">[optional] can be null, or equal to fpixs</param>
    /// <param name="fpixs"></param>
    /// <returns>fpixd</returns>
    /// <remarks>
    /// (1) This does a top-bottom flip of the image, which is
    ///     equivalent to a rotation out of the plane about a
    ///     horizontal line through the image center.
    /// (2) There are 2 cases for input:
    ///     (a) fpixd == null (creates a new fpixd)
    ///     (b) fpixd == fpixs (in-place operation)
    /// (3) For clarity, use these two patterns:
    ///     (a) fpixd = fpixFlipTB(NULL, fpixs);
    ///     (b) fpixFlipTB(fpixs, fpixs);
    /// </remarks>
    public static FPix FlipTB(FPix? fpixd, FPix fpixs)
    {
        int i, k, h, h2, wpl;

            /* Prepare fpixd for in-place operation */
        if (fpixd == null)
            fpixd = fpixs.Copy();

        float[] data = fpixd.Data;
        wpl = fpixd.Wpl;
        fpixd.GetDimensions(out _, out h);
        float[] buffer = new float[wpl];
        h2 = h / 2;
        for (i = 0, k = h - 1; i < h2; i++, k--) {
            int linet = i * wpl;
            int lineb = k * wpl;
            Array.Copy(data, linet, buffer, 0, wpl);
            Array.Copy(data, lineb, data, linet, wpl);
            Array.Copy(buffer, 0, data, lineb, wpl);
        }
        return fpixd;
    }

    /*--------------------------------------------------------------------*
     *            Affine and projective interpolated transforms           *
     *--------------------------------------------------------------------*/
    // leptonica: fpixAffinePta (fpix2.c)
    /// <summary>Affine transform specified by three corresponding points, with interpolation.</summary>
    /// <param name="ptad">4 pts of final coordinate space</param>
    /// <param name="ptas">4 pts of initial coordinate space</param>
    /// <param name="border">size of extension with constant normal derivative</param>
    /// <param name="inval">value brought in; typ. 0</param>
    /// <returns>fpixd</returns>
    /// <remarks>
    /// (1) If %border > 0, all four sides are extended by that distance,
    ///     and removed after the transformation is finished.  Pixels
    ///     that would be brought in to the trimmed result from outside
    ///     the extended region are assigned %inval.  The purpose of
    ///     extending the image is to avoid such assignments.
    /// (2) On the other hand, you may want to give all pixels that
    ///     are brought in from outside fpixs a specific value.  In that
    ///     case, set %border == 0.
    /// </remarks>
    public FPix AffinePta(Pta ptad, Pta ptas, int border, float inval)
    {
        float[] vc;
        Pta ptas2, ptad2;
        FPix fpixs2, fpixd, fpixd2;

            /* If a border is to be added, also translate the ptas */
        if (border > 0) {
            ptas2 = ptas.Transform(border, border, 1.0f, 1.0f);
            ptad2 = ptad.Transform(border, border, 1.0f, 1.0f);
            fpixs2 = AddSlopeBorder(border, border, border, border);
        } else {
            ptas2 = ptas;
            ptad2 = ptad;
            fpixs2 = Clone();
        }

            /* Get backwards transform from dest to src, and apply it */
        vc = Pix.GetAffineXformCoeffs(ptad2, ptas2);
        fpixd2 = fpixs2.Affine(vc, inval);

        if (border == 0)
            return fpixd2;

            /* Remove the added border */
        fpixd = fpixd2.RemoveBorder(border, border, border, border);
        return fpixd;
    }

    // leptonica: fpixAffine (fpix2.c)
    /// <summary>Affine transform with interpolation.</summary>
    /// <param name="vc">vector of 6 coefficients for affine transformation</param>
    /// <param name="inval">value brought in; typ. 0</param>
    /// <returns>fpixd</returns>
    public FPix Affine(float[] vc, float inval)
    {
        int i, j, w, h, wpld;
        float val;
        float x, y;
        FPix fpixd;

        GetDimensions(out w, out h);

        float[] datas = Data;
        fpixd = CreateTemplate();
        fpixd.SetAllArbitrary(inval);
        float[] datad = fpixd.Data;
        wpld = fpixd.Wpl;

            /* Iterate over destination pixels */
        for (i = 0; i < h; i++) {
            int lined = i * wpld;
            for (j = 0; j < w; j++) {
                    /* Compute float src pixel location corresponding to (i,j) */
                Pix.AffineXformPt(vc, j, i, out x, out y);
                val = LinearInterpolatePixelFloat(datas, w, h, x, y, inval);
                datad[lined + j] = val;
            }
        }

        return fpixd;
    }

    // leptonica: fpixProjectivePta (fpix2.c)
    /// <summary>Projective transform specified by four corresponding points, with interpolation.</summary>
    /// <param name="ptad">4 pts of final coordinate space</param>
    /// <param name="ptas">4 pts of initial coordinate space</param>
    /// <param name="border">size of extension with constant normal derivative</param>
    /// <param name="inval">value brought in; typ. 0</param>
    /// <returns>fpixd</returns>
    /// <remarks>
    /// (1) If %border > 0, all four sides are extended by that distance,
    ///     and removed after the transformation is finished.  Pixels
    ///     that would be brought in to the trimmed result from outside
    ///     the extended region are assigned %inval.  The purpose of
    ///     extending the image is to avoid such assignments.
    /// (2) On the other hand, you may want to give all pixels that
    ///     are brought in from outside fpixs a specific value.  In that
    ///     case, set %border == 0.
    /// </remarks>
    public FPix ProjectivePta(Pta ptad, Pta ptas, int border, float inval)
    {
        float[] vc;
        Pta ptas2, ptad2;
        FPix fpixs2, fpixd, fpixd2;

            /* If a border is to be added, also translate the ptas */
        if (border > 0) {
            ptas2 = ptas.Transform(border, border, 1.0f, 1.0f);
            ptad2 = ptad.Transform(border, border, 1.0f, 1.0f);
            fpixs2 = AddSlopeBorder(border, border, border, border);
        } else {
            ptas2 = ptas;
            ptad2 = ptad;
            fpixs2 = Clone();
        }

            /* Get backwards transform from dest to src, and apply it */
        vc = Pix.GetProjectiveXformCoeffs(ptad2, ptas2);
        fpixd2 = fpixs2.Projective(vc, inval);

        if (border == 0)
            return fpixd2;

            /* Remove the added border */
        fpixd = fpixd2.RemoveBorder(border, border, border, border);
        return fpixd;
    }

    // leptonica: fpixProjective (fpix2.c)
    /// <summary>Projective transform with interpolation.</summary>
    /// <param name="vc">vector of 8 coefficients for projective transform</param>
    /// <param name="inval">value brought in; typ. 0</param>
    /// <returns>fpixd</returns>
    public FPix Projective(float[] vc, float inval)
    {
        int i, j, w, h, wpld;
        float val;
        float x, y;
        FPix fpixd;

        GetDimensions(out w, out h);

        float[] datas = Data;
        fpixd = CreateTemplate();
        fpixd.SetAllArbitrary(inval);
        float[] datad = fpixd.Data;
        wpld = fpixd.Wpl;

            /* Iterate over destination pixels */
        for (i = 0; i < h; i++) {
            int lined = i * wpld;
            for (j = 0; j < w; j++) {
                    /* Compute float src pixel location corresponding to (i,j) */
                Pix.ProjectiveXformPt(vc, j, i, out x, out y);
                val = LinearInterpolatePixelFloat(datas, w, h, x, y, inval);
                datad[lined + j] = val;
            }
        }

        return fpixd;
    }

    // leptonica: linearInterpolatePixelFloat (fpix2.c)
    /// <summary>Linear interpolation of a float image at a floating point location.</summary>
    /// <param name="datas">beginning of float image data</param>
    /// <param name="w">dimensions of image</param>
    /// <param name="h">dimensions of image</param>
    /// <param name="x">floating pt location for evaluation</param>
    /// <param name="y">floating pt location for evaluation</param>
    /// <param name="inval">float value brought in from the outside when the input x,y location is outside the image</param>
    /// <returns>interpolated float value</returns>
    /// <remarks>
    /// (1) This is a standard linear interpolation function.  It is
    ///     equivalent to area weighting on each component, and
    ///     avoids "jaggies" when rendering sharp edges.
    /// </remarks>
    public static float LinearInterpolatePixelFloat(float[] datas, int w, int h,
                                                    float x, float y, float inval)
    {
        int xpm, ypm, xp, yp, xf, yf;
        float v00, v01, v10, v11;

            /* Skip if off the edge */
        if (x < 0.0 || y < 0.0 || x > w - 2.0 || y > h - 2.0)
            return inval;

        xpm = (int)(16.0 * x + 0.5);
        ypm = (int)(16.0 * y + 0.5);
        xp = xpm >> 4;
        yp = ypm >> 4;
        xf = xpm & 0x0f;
        yf = ypm & 0x0f;

            /* Interpolate by area weighting. */
        int lines = yp * w;
        v00 = (float)((16.0 - xf) * (16.0 - yf) * datas[lines + xp]);
        v10 = (float)(xf * (16.0 - yf) * datas[lines + xp + 1]);
        v01 = (float)((16.0 - xf) * yf * datas[lines + w + xp]);
        v11 = (float)(xf) * yf * datas[lines + w + xp + 1];
        return (float)((v00 + v01 + v10 + v11) / 256.0);
    }

    /*--------------------------------------------------------------------*
     *                      Thresholding to 1 bpp Pix                     *
     *--------------------------------------------------------------------*/
    // leptonica: fpixThresholdToPix (fpix2.c)
    /// <summary>Sets the pixel in a 1 bpp pix for all values &lt;= thresh.</summary>
    /// <param name="thresh"></param>
    /// <returns>pixd 1 bpp</returns>
    /// <remarks>
    /// (1) For all values of fpix that are &lt;= thresh, sets the pixel
    ///     in pixd to 1.
    /// </remarks>
    public Pix ThresholdToPix(float thresh)
    {
        int i, j, w, h, wpls, wpld;
        Pix pixd;

        GetDimensions(out w, out h);
        float[] datas = Data;
        wpls = Wpl;
        pixd = Pix.Create(w, h, 1);
        uint[] datad = pixd.Data;
        wpld = pixd.Wpl;
        for (i = 0; i < h; i++) {
            int lines = i * wpls;
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < w; j++) {
                if (datas[lines + j] <= thresh)
                    ArrayAccess.SetDataBit(lined, j);
            }
        }

        return pixd;
    }
}

public sealed partial class DPix
{
    // leptonica: dpixConvertToPix (fpix2.c)
    /// <summary>Converts to a 8, 16 or 32 bpp pix.</summary>
    /// <param name="outdepth">0, 8, 16 or 32 bpp</param>
    /// <param name="negvals">L_CLIP_TO_ZERO, L_TAKE_ABSVAL</param>
    /// <param name="errorflag">1 to output error stats; 0 otherwise</param>
    /// <returns>pixd</returns>
    /// <remarks>
    /// (1) Use %outdepth = 0 to programmatically determine the
    ///     output depth.  If no values are greater than 255,
    ///     it will set outdepth = 8; otherwise to 16 or 32.
    /// (2) Because we are converting a float to an unsigned int
    ///     with a specified dynamic range (8, 16 or 32 bits), errors
    ///     can occur.  If errorflag == TRUE, output the number
    ///     of values out of range, both negative and positive.
    /// (3) If a pixel value is positive and out of range, clip to
    ///     the maximum value represented at the outdepth of 8, 16
    ///     or 32 bits.
    /// </remarks>
    public Pix ConvertToPix(int outdepth, int negvals, int errorflag)
    {
        const string func = "dpixConvertToPix";
        int w, h, i, j, wpls, wpld, maxval;
        uint vald;
        double val;
        Pix pixd;

        if (negvals != L.L_CLIP_TO_ZERO && negvals != L.L_TAKE_ABSVAL)
            throw Lept.Arg(func, "invalid negvals");
        if (outdepth != 0 && outdepth != 8 && outdepth != 16 && outdepth != 32)
            throw Lept.Arg(func, "outdepth not in {0,8,16,32}");

        GetDimensions(out w, out h);
        double[] datas = Data;
        wpls = Wpl;

            /* Adaptive determination of output depth */
        if (outdepth == 0) {
            outdepth = 8;
            for (i = 0; i < h && outdepth < 32; i++) {
                int lines = i * wpls;
                for (j = 0; j < w && outdepth < 32; j++) {
                    if (datas[lines + j] > 65535.5)
                        outdepth = 32;
                    else if (datas[lines + j] > 255.5)
                        outdepth = 16;
                }
            }
        }
        /* NOTE(upstream): the else binds to the outdepth == 16 test, so maxval is
         * 0xffffffff (-1 as l_int32) for 8 bpp too, and nothing is clipped at 255. */
        maxval = 0xff;
        if (outdepth == 16)
            maxval = 0xffff;
        else  /* outdepth == 32 */
            maxval = unchecked((int)0xffffffff);

            /* Gather statistics if %errorflag = TRUE */
        if (errorflag != 0) {
            int negs = 0;
            int overvals = 0;
            for (i = 0; i < h; i++) {
                int lines = i * wpls;
                for (j = 0; j < w; j++) {
                    val = datas[lines + j];
                    if (val < 0.0)
                        negs++;
                    else if (val > maxval)
                        overvals++;
                }
            }
            if (negs > 0)
                Log.Error(func, $"Number of negative values: {negs}");
            if (overvals > 0)
                Log.Error(func, $"Number of too-large values: {overvals}");
        }

            /* Make the pix and convert the data */
        pixd = Pix.Create(w, h, outdepth);
        uint[] datad = pixd.Data;
        wpld = pixd.Wpl;
        for (i = 0; i < h; i++) {
            int lines = i * wpls;
            Span<uint> lined = datad.AsSpan(i * wpld, wpld);
            for (j = 0; j < w; j++) {
                val = datas[lines + j];
                if (val >= 0.0) {
                    vald = (uint)(val + 0.5);
                } else {  /* val < 0.0 */
                    if (negvals == L.L_CLIP_TO_ZERO)
                        vald = 0;
                    else
                        vald = (uint)(-val + 0.5);
                }
                if (vald > unchecked((uint)maxval))
                    vald = unchecked((uint)maxval);
                if (outdepth == 8)
                    ArrayAccess.SetDataByte(lined, j, (int)vald);
                else if (outdepth == 16)
                    ArrayAccess.SetDataTwoBytes(lined, j, (int)vald);
                else  /* outdepth == 32 */
                    ArrayAccess.SetDataFourBytes(lined, j, vald);
            }
        }

        return pixd;
    }

    // leptonica: dpixConvertToFPix (fpix2.c)
    /// <summary>Converts to an FPix.</summary>
    /// <returns>fpix</returns>
    public FPix ConvertToFPix()
    {
        int w, h, i, j, wpls, wpld;
        double val;
        FPix fpix;

        GetDimensions(out w, out h);
        fpix = FPix.Create(w, h);

        double[] datas = Data;
        float[] datad = fpix.Data;
        wpls = Wpl;  /* 8 byte words */
        wpld = fpix.Wpl;
        for (i = 0; i < h; i++) {
            int lines = i * wpls;
            int lined = i * wpld;
            for (j = 0; j < w; j++) {
                val = datas[lines + j];
                datad[lined + j] = (float)val;
            }
        }

        return fpix;
    }

    // leptonica: dpixGetMin (fpix2.c)
    /// <summary>Minimum value and its location.</summary>
    /// <param name="pminval">min value</param>
    /// <param name="pxminloc">x location of min</param>
    /// <param name="pyminloc">y location of min</param>
    public void GetMin(out double pminval, out int pxminloc, out int pyminloc)
    {
        int i, j, w, h, wpl, xminloc, yminloc;
        double minval;

        minval = +1.0e300;
        xminloc = 0;
        yminloc = 0;
        GetDimensions(out w, out h);
        double[] data = Data;
        wpl = Wpl;
        for (i = 0; i < h; i++) {
            int line = i * wpl;
            for (j = 0; j < w; j++) {
                if (data[line + j] < minval) {
                    minval = data[line + j];
                    xminloc = j;
                    yminloc = i;
                }
            }
        }

        pminval = minval;
        pxminloc = xminloc;
        pyminloc = yminloc;
    }

    // leptonica: dpixGetMax (fpix2.c)
    /// <summary>Maximum value and its location.</summary>
    /// <param name="pmaxval">max value</param>
    /// <param name="pxmaxloc">x location of max</param>
    /// <param name="pymaxloc">y location of max</param>
    public void GetMax(out double pmaxval, out int pxmaxloc, out int pymaxloc)
    {
        int i, j, w, h, wpl, xmaxloc, ymaxloc;
        double maxval;

        maxval = -1.0e20;
        xmaxloc = 0;
        ymaxloc = 0;
        GetDimensions(out w, out h);
        double[] data = Data;
        wpl = Wpl;
        for (i = 0; i < h; i++) {
            int line = i * wpl;
            for (j = 0; j < w; j++) {
                if (data[line + j] > maxval) {
                    maxval = data[line + j];
                    xmaxloc = j;
                    ymaxloc = i;
                }
            }
        }

        pmaxval = maxval;
        pxmaxloc = xmaxloc;
        pymaxloc = ymaxloc;
    }

    // leptonica: dpixScaleByInteger (fpix2.c)
    /// <summary>Scales by an integer factor, using bilinear interpolation.</summary>
    /// <param name="factor">integer scaling factor</param>
    /// <returns>dpixd interpolated result</returns>
    /// <remarks>
    /// (1) The width wd of dpixd is related to ws of dpixs by:
    ///         wd = factor * (ws - 1) + 1   (and ditto for the height)
    ///     We avoid special-casing boundary pixels in the interpolation
    ///     by constructing fpixd by inserting (factor - 1) interpolated
    ///     pixels between each pixel in fpixs.  Then
    ///          wd = ws + (ws - 1) * (factor - 1)    (same as above)
    ///     This also has the advantage that if we subsample by %factor,
    ///     throwing out all the interpolated pixels, we regain the
    ///     original low resolution dpix.
    /// </remarks>
    public DPix ScaleByInteger(int factor)
    {
        int i, j, k, m, ws, hs, wd, hd, wpls, wpld;
        double val0, val1, val2, val3;
        double[] fract;
        DPix dpixd;

        GetDimensions(out ws, out hs);
        wd = factor * (ws - 1) + 1;
        hd = factor * (hs - 1) + 1;
        dpixd = Create(wd, hd);
        double[] datas = Data;
        double[] datad = dpixd.Data;
        wpls = Wpl;
        wpld = dpixd.Wpl;
        fract = new double[factor];
        for (i = 0; i < factor; i++)
            fract[i] = i / (double)factor;
        for (i = 0; i < hs - 1; i++) {
            int lines = i * wpls;
            for (j = 0; j < ws - 1; j++) {
                val0 = datas[lines + j];
                val1 = datas[lines + j + 1];
                val2 = datas[lines + wpls + j];
                val3 = datas[lines + wpls + j + 1];
                for (k = 0; k < factor; k++) {  /* rows of sub-block */
                    int lined = (i * factor + k) * wpld;
                    for (m = 0; m < factor; m++) {  /* cols of sub-block */
                         datad[lined + j * factor + m] =
                                val0 * (1.0 - fract[m]) * (1.0 - fract[k]) +
                                val1 * fract[m] * (1.0 - fract[k]) +
                                val2 * (1.0 - fract[m]) * fract[k] +
                                val3 * fract[m] * fract[k];
                    }
                }
            }
        }

            /* Do the right-most column of dpixd, skipping LR corner */
        for (i = 0; i < hs - 1; i++) {
            int lines = i * wpls;
            val0 = datas[lines + ws - 1];
            val1 = datas[lines + wpls + ws - 1];
            for (k = 0; k < factor; k++) {
                int lined = (i * factor + k) * wpld;
                datad[lined + wd - 1] = val0 * (1.0 - fract[k]) + val1 * fract[k];
            }
        }

            /* Do the bottom-most row of dpixd */
        {
            int lines = (hs - 1) * wpls;
            int lined = (hd - 1) * wpld;
            for (j = 0; j < ws - 1; j++) {
                val0 = datas[lines + j];
                val1 = datas[lines + j + 1];
                for (m = 0; m < factor; m++)
                    datad[lined + j * factor + m] = val0 * (1.0 - fract[m]) + val1 * fract[m];
                datad[lined + wd - 1] = datas[lines + ws - 1];  /* LR corner */
            }
        }

        return dpixd;
    }

    // leptonica: dpixLinearCombination (fpix2.c)
    /// <summary>Computes the pixelwise linear combination a * src1 + b * src2.</summary>
    /// <param name="dpixd">[optional] this can be null, or equal to dpixs1</param>
    /// <param name="dpixs1">can be equal to dpixd</param>
    /// <param name="dpixs2"></param>
    /// <param name="a">multiplication factor on dpixs1</param>
    /// <param name="b">multiplication factor on dpixs2</param>
    /// <returns>dpixd always</returns>
    /// <remarks>
    /// (1) Computes pixelwise linear combination: a * src1 + b * src2
    /// (2) Alignment is to UL corner; src1 and src2 do not have to be
    ///     the same size.
    /// (3) There are 2 cases.  The result can go to a new dest, or
    ///     in-place to dpixs1:
    ///     * dpixd == null:   (src1 + src2) --> new dpixd
    ///     * dpixd == dpixs1:  (src1 + src2) --> src1  (in-place)
    /// </remarks>
    public static DPix LinearCombination(DPix? dpixd, DPix dpixs1, DPix dpixs2, float a, float b)
    {
        const string func = "dpixLinearCombination";
        int i, j, ws, hs, w, h, wpls, wpld;

        if (dpixd != null && dpixd != dpixs1)
            throw Lept.Arg(func, "invalid inplace operation");

        if (dpixd == null)
            dpixd = dpixs1.Copy();
        double[] datas = dpixs2.Data;
        double[] datad = dpixd.Data;
        wpls = dpixs2.Wpl;
        wpld = dpixd.Wpl;
        dpixs2.GetDimensions(out ws, out hs);
        dpixd.GetDimensions(out w, out h);
        w = Math.Min(ws, w);
        h = Math.Min(hs, h);
        double da = a, db = b;
        Vector256<double> va = Vector256.Create(da);
        Vector256<double> vb = Vector256.Create(db);
        for (i = 0; i < h; i++) {
            ref double rs = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datas), (nuint)(i * wpls));
            ref double rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(datad), (nuint)(i * wpld));
            j = 0;
            if (Vector256.IsHardwareAccelerated) {
                for (; j + 4 <= w; j += 4)
                    (va * Vector256.LoadUnsafe(ref rd, (nuint)j) + vb * Vector256.LoadUnsafe(ref rs, (nuint)j)).StoreUnsafe(ref rd, (nuint)j);
            }
            for (; j < w; j++)
                Unsafe.Add(ref rd, (nuint)j) = da * Unsafe.Add(ref rd, (nuint)j) + db * Unsafe.Add(ref rs, (nuint)j);
        }

        return dpixd;
    }

    // leptonica: dpixAddMultConstant (fpix2.c)
    /// <summary>Adds and multiplies each pixel by constants, in place.</summary>
    /// <param name="addc">use 0.0 to skip the operation</param>
    /// <param name="multc">use 1.0 to skip the operation</param>
    /// <remarks>
    /// (1) This is an in-place operation.
    /// (2) It can be used to multiply each pixel by a constant,
    ///     and also to add a constant to each pixel.  Multiplication
    ///     is done first.
    /// </remarks>
    public void AddMultConstant(double addc, double multc)
    {
        int i, j, w, h, wpl;

        if (addc == 0.0 && multc == 1.0)
            return;

        GetDimensions(out w, out h);
        double[] data = Data;
        wpl = Wpl;
        Vector256<double> vAdd = Vector256.Create(addc);
        Vector256<double> vMult = Vector256.Create(multc);
        for (i = 0; i < h; i++) {
            ref double rd = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(data), (nuint)(i * wpl));
            j = 0;
            if (addc == 0.0) {
                if (Vector256.IsHardwareAccelerated) {
                    for (; j + 4 <= w; j += 4)
                        (Vector256.LoadUnsafe(ref rd, (nuint)j) * vMult).StoreUnsafe(ref rd, (nuint)j);
                }
                for (; j < w; j++)
                    Unsafe.Add(ref rd, (nuint)j) *= multc;
            } else if (multc == 1.0) {
                if (Vector256.IsHardwareAccelerated) {
                    for (; j + 4 <= w; j += 4)
                        (Vector256.LoadUnsafe(ref rd, (nuint)j) + vAdd).StoreUnsafe(ref rd, (nuint)j);
                }
                for (; j < w; j++)
                    Unsafe.Add(ref rd, (nuint)j) += addc;
            } else {
                if (Vector256.IsHardwareAccelerated) {
                    for (; j + 4 <= w; j += 4)
                        (vMult * Vector256.LoadUnsafe(ref rd, (nuint)j) + vAdd).StoreUnsafe(ref rd, (nuint)j);
                }
                for (; j < w; j++)
                    Unsafe.Add(ref rd, (nuint)j) = multc * Unsafe.Add(ref rd, (nuint)j) + addc;
            }
        }
    }

    // leptonica: dpixSetAllArbitrary (fpix2.c)
    /// <summary>Sets all pixels to the same value.</summary>
    /// <param name="inval">to set at each pixel</param>
    public void SetAllArbitrary(double inval)
    {
        GetDimensions(out int w, out int h);
        Data.AsSpan(0, w * h).Fill(inval);
    }
}
