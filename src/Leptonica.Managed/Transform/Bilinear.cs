using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  bilinear.c: Bilinear transforms.
 *
 *      Bilinear (4 pt) image transformation using a sampled
 *      (to nearest integer) transform on each dest point
 *           pixBilinearSampledPta()
 *           pixBilinearSampled()
 *
 *      Bilinear (4 pt) image transformation using interpolation
 *      (or area mapping) for anti-aliasing images that are
 *      2, 4, or 8 bpp gray, or colormapped, or 32 bpp RGB
 *           pixBilinearPta()
 *           pixBilinear()
 *           pixBilinearPtaColor()
 *           pixBilinearColor()
 *           pixBilinearPtaGray()
 *           pixBilinearGray()
 *
 *      Bilinear transform including alpha (blend) component
 *           pixBilinearPtaWithAlpha()
 *
 *      Bilinear coordinate transformation
 *           getBilinearXformCoeffs()
 *           bilinearXformSampledPt()
 *           bilinearXformPt()
 *
 *  We give both a bilinear coordinate transformation and
 *  a bilinear image transformation.
 *
 *  For the former, we ask for the coordinate value (x',y')
 *  in the transformed space for any point (x,y) in the original
 *  space.  The coefficients of the transformation are found by
 *  solving 8 simultaneous equations for the 8 coordinates of
 *  the 4 points in src and dest.  The transformation can then
 *  be used to compute the associated image transform, by
 *  computing, for each dest pixel, the relevant pixel(s) in
 *  the source.  This can be done either by taking the closest
 *  src pixel to each transformed dest pixel ("sampling") or
 *  by doing an interpolation and averaging over 4 source
 *  pixels with appropriate weightings ("interpolated").
 *
 *  A typical application would be to remove some of the
 *  keystoning due to a projective transform in the imaging system.
 *
 *  The bilinear transform is given by specifying two equations:
 *
 *      x' = ax + by + cxy + d
 *      y' = ex + fy + gxy + h
 *
 *  where the eight coefficients have been computed from four
 *  sets of these equations, each for two corresponding data pts.
 *  In practice, once the coefficients are known, we use the
 *  equations "backwards": for each point (x,y) in the dest image,
 *  these two equations are used to compute the corresponding point
 *  (x',y') in the src.  That computed point in the src is then used
 *  to determine the corresponding dest pixel value in one of two ways:
 *
 *   ~ sampling: simply take the value of the src pixel in which this
 *               point falls
 *   ~ interpolation: take appropriate linear combinations of the
 *                    four src pixels that this dest pixel would
 *                    overlap, with the coefficients proportional
 *                    to the amount of overlap
 *
 *  For small warp, like rotation, area mapping in the
 *  interpolation is equivalent to linear interpolation.
 *
 *  Typical relative timing of transforms (sampled = 1.0):
 *  8 bpp:   sampled        1.0
 *           interpolated   1.6
 *  32 bpp:  sampled        1.0
 *           interpolated   1.8
 *  Additionally, the computation time/pixel is nearly the same
 *  for 8 bpp and 32 bpp, for both sampled and interpolated.
 *
 *  Differences from C:
 *    - The coefficient vector is returned as float[8].
 *    - Functions that return NULL or an error code in C throw instead.
 *    - Like in Affine.cs, all functions are members of Pix.
 *
 *  Not ported: nothing.
 */
public sealed partial class Pix
{
    /*-------------------------------------------------------------*
     *             Sampled bilinear image transformation           *
     *-------------------------------------------------------------*/
    // leptonica: pixBilinearSampledPta (bilinear.c)
    /// <summary>Bilinear transformation, sampled to the nearest source pixel, from point sets.</summary>
    /// <param name="ptad">4 pts of final coordinate space</param>
    /// <param name="ptas">4 pts of initial coordinate space</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>pixd; all depths</returns>
    /// <remarks>
    /// (1) Brings in either black or white pixels from the boundary.
    /// (2) Retains colormap, which you can do for a sampled transform.
    /// (3) No 3 of the 4 points may be collinear.
    /// (4) For 8 and 32 bpp pix, better quality is obtained by the
    ///     somewhat slower pixBilinearPta().  See that
    ///     function for relative timings between sampled and interpolated.
    /// </remarks>
    public Pix BilinearSampledPta(Pta ptad, Pta ptas, int incolor)
    {
        const string func = "pixBilinearSampledPta";

        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid incolor");
        if (ptas.Count != 4)
            throw Lept.Arg(func, "ptas count not 4");
        if (ptad.Count != 4)
            throw Lept.Arg(func, "ptad count not 4");

        /* Get backwards transform from dest to src, and apply it */
        float[] vc = GetBilinearXformCoeffs(ptad, ptas);
        return BilinearSampled(vc, incolor);
    }

    // leptonica: pixBilinearSampled (bilinear.c)
    /// <summary>Bilinear transformation, sampled to the nearest source pixel.</summary>
    /// <param name="vc">vector of 8 coefficients for bilinear transformation</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>pixd; all depths</returns>
    /// <remarks>
    /// (1) Brings in either black or white pixels from the boundary.
    /// (2) Retains colormap, which you can do for a sampled transform.
    /// (3) For 8 or 32 bpp, much better quality is obtained by the
    ///     somewhat slower pixBilinear().  See that function
    ///     for relative timings between sampled and interpolated.
    /// </remarks>
    public Pix BilinearSampled(float[] vc, int incolor)
    {
        const string func = "pixBilinearSampled";

        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid incolor");
        GetDimensions(out int w, out int h, out int d);
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 32)
            throw Lept.Arg(func, "depth not 1, 2, 4, 8 or 16");

        float vc0 = vc[0], vc1 = vc[1], vc2 = vc[2], vc3 = vc[3];
        float vc4 = vc[4], vc5 = vc[5], vc6 = vc[6], vc7 = vc[7];

        uint[] datas = Data;
        int wpls = Wpl;
        PixColormap? cmap = Colormap;

        if (cmap == null && Spp != 4 && (d == 1 || d == 8 || d == 32) &&
            LeptGpu.TryGetActive((long)w * h, 10, out IGpuAccelerator? gpuSampled) &&
            gpuSampled.TryBilinear(this, vc, incolor, sampled: true, out Pix? gpuSampledPix)) {
            return gpuSampledPix;
        }

        if (d == 32 && cmap == null) {
            Pix pixd32 = CreateTemplateNoInit(this);
            uint fill32 = incolor == L.L_BRING_IN_BLACK ? 0u : 0xffffffffu;
            uint[] datad32 = pixd32.Data;
            int wpld32 = pixd32.Wpl;
            LeptParallel.ForRows(h, w, (yStart, yEnd) => {
                ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
                ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad32);
                for (int i = yStart; i < yEnd; i++) {
                    int lined = i * wpld32;
                    float ry1 = vc1 * i, ry5 = vc5 * i;
                    for (int j = 0; j < w; j++) {
                        int x = (int)(vc0 * j + ry1 + vc2 * j * i + vc3 + 0.5);
                        int y = (int)(vc4 * j + ry5 + vc6 * j * i + vc7 + 0.5);
                        Unsafe.Add(ref datadRef, lined + j) =
                            (uint)x < (uint)w && (uint)y < (uint)h
                                ? Unsafe.Add(ref datasRef, y * wpls + x)
                                : fill32;
                    }
                }
            });
            return pixd32;
        }

        if (d == 8) {
            Pix pixd8 = CreateTemplateNoInit(this);
            byte fill8;
            if (cmap != null) {
                int color = incolor == L.L_BRING_IN_WHITE ? 1 : 0;
                pixd8.Colormap!.AddBlackOrWhite(color, out int cmapindex);
                fill8 = (byte)cmapindex;
            } else {
                fill8 = incolor == L.L_BRING_IN_BLACK ? (byte)0 : (byte)255;
            }
            uint fillWord8 = fill8 * 0x01010101u;
            uint[] datad8 = pixd8.Data;
            int wpld8 = pixd8.Wpl;
            int w4 = w & ~3;
            LeptParallel.ForRows(h, w, (yStart, yEnd) => {
                ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
                ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad8);
                for (int i = yStart; i < yEnd; i++) {
                    int lined = i * wpld8;
                    float ry1 = vc1 * i, ry5 = vc5 * i;
                    int j = 0;
                    for (; j < w4; j += 4) {
                        int x0 = (int)(vc0 * j + ry1 + vc2 * j * i + vc3 + 0.5);
                        int y0 = (int)(vc4 * j + ry5 + vc6 * j * i + vc7 + 0.5);
                        uint b0 = (uint)x0 < (uint)w && (uint)y0 < (uint)h
                            ? (uint)GetDataByteUnchecked(ref Unsafe.Add(ref datasRef, y0 * wpls), x0) : fill8;

                        int j1 = j + 1;
                        int x1 = (int)(vc0 * j1 + ry1 + vc2 * j1 * i + vc3 + 0.5);
                        int y1 = (int)(vc4 * j1 + ry5 + vc6 * j1 * i + vc7 + 0.5);
                        uint b1 = (uint)x1 < (uint)w && (uint)y1 < (uint)h
                            ? (uint)GetDataByteUnchecked(ref Unsafe.Add(ref datasRef, y1 * wpls), x1) : fill8;

                        int j2 = j + 2;
                        int x2 = (int)(vc0 * j2 + ry1 + vc2 * j2 * i + vc3 + 0.5);
                        int y2 = (int)(vc4 * j2 + ry5 + vc6 * j2 * i + vc7 + 0.5);
                        uint b2 = (uint)x2 < (uint)w && (uint)y2 < (uint)h
                            ? (uint)GetDataByteUnchecked(ref Unsafe.Add(ref datasRef, y2 * wpls), x2) : fill8;

                        int j3 = j + 3;
                        int x3 = (int)(vc0 * j3 + ry1 + vc2 * j3 * i + vc3 + 0.5);
                        int y3 = (int)(vc4 * j3 + ry5 + vc6 * j3 * i + vc7 + 0.5);
                        uint b3 = (uint)x3 < (uint)w && (uint)y3 < (uint)h
                            ? (uint)GetDataByteUnchecked(ref Unsafe.Add(ref datasRef, y3 * wpls), x3) : fill8;

                        Unsafe.Add(ref datadRef, lined + (j >> 2)) = (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
                    }
                    if (j < w) {
                        ref uint dstLineRef = ref Unsafe.Add(ref datadRef, lined);
                        Unsafe.Add(ref dstLineRef, w >> 2) = fillWord8;
                        for (; j < w; j++) {
                            int x = (int)(vc0 * j + ry1 + vc2 * j * i + vc3 + 0.5);
                            int y = (int)(vc4 * j + ry5 + vc6 * j * i + vc7 + 0.5);
                            if ((uint)x < (uint)w && (uint)y < (uint)h)
                                SetDataByteUnchecked(ref dstLineRef, j, GetDataByteUnchecked(ref Unsafe.Add(ref datasRef, y * wpls), x));
                        }
                    }
                }
            });
            return pixd8;
        }

        /* Init all dest pixels to color to be brought in from outside */
        Pix pixd = CreateTemplate(this);
        if (cmap != null) {
            int color = incolor == L.L_BRING_IN_WHITE ? 1 : 0;
            pixd.Colormap!.AddBlackOrWhite(color, out int cmapindex);
            pixd.SetAllArbitrary((uint)cmapindex);
        } else {
            if ((d == 1 && incolor == L.L_BRING_IN_WHITE) ||
                (d > 1 && incolor == L.L_BRING_IN_BLACK)) {
                pixd.ClearAll();
            } else {
                pixd.SetAll();
            }
        }

        /* Scan over the dest pixels */
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        LeptParallel.ForRows(h, w, (yStart, yEnd) => {
            for (int i = yStart; i < yEnd; i++) {
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                float ry1 = vc1 * i, ry5 = vc5 * i;
                if (d == 1) {
                    for (int j = 0; j < w; j++) {
                        int x = (int)(vc0 * j + ry1 + vc2 * j * i + vc3 + 0.5);
                        int y = (int)(vc4 * j + ry5 + vc6 * j * i + vc7 + 0.5);
                        if ((uint)x >= (uint)w || (uint)y >= (uint)h) continue;
                        int val = GetDataBit(datas.AsSpan(y * wpls, wpls), x);
                        SetDataBitVal(lined, j, val);
                    }
                } else if (d == 2) {
                    for (int j = 0; j < w; j++) {
                        int x = (int)(vc0 * j + ry1 + vc2 * j * i + vc3 + 0.5);
                        int y = (int)(vc4 * j + ry5 + vc6 * j * i + vc7 + 0.5);
                        if ((uint)x >= (uint)w || (uint)y >= (uint)h) continue;
                        int val = GetDataDibit(datas.AsSpan(y * wpls, wpls), x);
                        SetDataDibit(lined, j, val);
                    }
                } else {
                    for (int j = 0; j < w; j++) {
                        int x = (int)(vc0 * j + ry1 + vc2 * j * i + vc3 + 0.5);
                        int y = (int)(vc4 * j + ry5 + vc6 * j * i + vc7 + 0.5);
                        if ((uint)x >= (uint)w || (uint)y >= (uint)h) continue;
                        int val = GetDataQbit(datas.AsSpan(y * wpls, wpls), x);
                        SetDataQbit(lined, j, val);
                    }
                }
            }
        });

        return pixd;
    }

    /*---------------------------------------------------------------------*
     *            Interpolated bilinear image transformation             *
     *---------------------------------------------------------------------*/
    // leptonica: pixBilinearPta (bilinear.c)
    /// <summary>Interpolated bilinear transformation from point sets.</summary>
    /// <param name="ptad">4 pts of final coordinate space</param>
    /// <param name="ptas">4 pts of initial coordinate space</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>pixd; all depths; colormap ok</returns>
    /// <remarks>
    /// (1) Brings in either black or white pixels from the boundary
    /// (2) Removes any existing colormap, if necessary, before transforming
    /// </remarks>
    public Pix BilinearPta(Pta ptad, Pta ptas, int incolor)
    {
        const string func = "pixBilinearPta";

        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid incolor");
        if (ptas.Count != 4)
            throw Lept.Arg(func, "ptas count not 4");
        if (ptad.Count != 4)
            throw Lept.Arg(func, "ptad count not 4");

        if (Depth == 1)
            return BilinearSampledPta(ptad, ptas, incolor);

        /* Remove cmap if it exists, and unpack to 8 bpp if necessary */
        Pix pixt1 = RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        int d = pixt1.Depth;
        Pix pixt2;
        if (d < 8)
            pixt2 = pixt1.ConvertTo8(false);
        else
            pixt2 = Clone(pixt1);
        d = pixt2.Depth;

        /* Compute actual color to bring in from edges */
        uint colorval = 0;
        if (incolor == L.L_BRING_IN_WHITE) {
            if (d == 8)
                colorval = 255;
            else  /* d == 32 */
                colorval = 0xffffff00;
        }

        Pix pixd;
        if (d == 8)
            pixd = pixt2.BilinearPtaGray(ptad, ptas, (byte)colorval);
        else  /* d == 32 */
            pixd = pixt2.BilinearPtaColor(ptad, ptas, colorval);
        return pixd;
    }

    // leptonica: pixBilinear (bilinear.c)
    /// <summary>Interpolated bilinear transformation with a coefficient vector.</summary>
    /// <param name="vc">vector of 8 coefficients for bilinear transformation</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>pixd; all depths; colormap ok</returns>
    /// <remarks>
    /// (1) Brings in either black or white pixels from the boundary
    /// (2) Removes any existing colormap, if necessary, before transforming
    /// </remarks>
    public Pix Bilinear(float[] vc, int incolor)
    {
        if (Depth == 1)
            return BilinearSampled(vc, incolor);

        /* Remove cmap if it exists, and unpack to 8 bpp if necessary */
        Pix pixt1 = RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);
        int d = pixt1.Depth;
        Pix pixt2;
        if (d < 8)
            pixt2 = pixt1.ConvertTo8(false);
        else
            pixt2 = Clone(pixt1);
        d = pixt2.Depth;

        if (pixt2.Spp != 4 &&
            LeptGpu.TryGetActive((long)pixt2.Width * pixt2.Height, d == 32 ? 24 : 14, out IGpuAccelerator? gpu) &&
            gpu.TryBilinear(pixt2, vc, incolor, sampled: false, out Pix? gpuPix)) {
            return gpuPix;
        }

        /* Compute actual color to bring in from edges */
        uint colorval = 0;
        if (incolor == L.L_BRING_IN_WHITE) {
            if (d == 8)
                colorval = 255;
            else  /* d == 32 */
                colorval = 0xffffff00;
        }

        Pix pixd;
        if (d == 8)
            pixd = pixt2.BilinearGray(vc, (byte)colorval);
        else  /* d == 32 */
            pixd = pixt2.BilinearColor(vc, colorval);
        return pixd;
    }

    // leptonica: pixBilinearPtaColor (bilinear.c)
    /// <summary>Interpolated bilinear transformation of a 32 bpp image from point sets.</summary>
    /// <param name="ptad">4 pts of final coordinate space</param>
    /// <param name="ptas">4 pts of initial coordinate space</param>
    /// <param name="colorval">e.g., 0 to bring in BLACK, 0xffffff00 for WHITE</param>
    /// <returns>pixd; 32 bpp</returns>
    public Pix BilinearPtaColor(Pta ptad, Pta ptas, uint colorval)
    {
        const string func = "pixBilinearPtaColor";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs must be 32 bpp");
        if (ptas.Count != 4)
            throw Lept.Arg(func, "ptas count not 4");
        if (ptad.Count != 4)
            throw Lept.Arg(func, "ptad count not 4");

        /* Get backwards transform from dest to src, and apply it */
        float[] vc = GetBilinearXformCoeffs(ptad, ptas);
        return BilinearColor(vc, colorval);
    }

    // leptonica: pixBilinearColor (bilinear.c)
    /// <summary>Interpolated bilinear transformation of a 32 bpp image.</summary>
    /// <param name="vc">vector of 8 coefficients for bilinear transformation</param>
    /// <param name="colorval">e.g., 0 to bring in BLACK, 0xffffff00 for WHITE</param>
    /// <returns>pixd; 32 bpp</returns>
    public Pix BilinearColor(float[] vc, uint colorval)
    {
        GetDimensions(out int w, out int h, out int d);
        if (d != 32)
            throw Lept.Arg("pixBilinearColor", "pixs must be 32 bpp");

        float vc0 = vc[0], vc1 = vc[1], vc2 = vc[2], vc3 = vc[3];
        float vc4 = vc[4], vc5 = vc[5], vc6 = vc[6], vc7 = vc[7];

        uint[] datas = Data;
        int wpls = Wpl;
        Pix pixd = CreateTemplateNoInit(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

        /* Iterate over destination pixels */
        LeptParallel.ForRows(h, w, (yStart, yEnd) => {
            ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
            for (int i = yStart; i < yEnd; i++) {
                int lined = i * wpld;
                float ry1 = vc1 * i, ry5 = vc5 * i;
                for (int j = 0; j < w; j++) {
                    float x = vc0 * j + ry1 + vc2 * j * i + vc3;
                    float y = vc4 * j + ry5 + vc6 * j * i + vc7;
                    Unsafe.Add(ref datadRef, lined + j) =
                        LinearInterpolatePixelColorCore(ref datasRef, wpls, w, h, x, y, colorval);
                }
            }
        });

        /* If rgba, transform the pixs alpha channel and insert in pixd */
        if (Spp == 4) {
            Pix pix1 = GetRGBComponent(L.L_ALPHA_CHANNEL);
            Pix pix2 = pix1.BilinearGray(vc, 255);  /* bring in opaque */
            pixd.SetRGBComponent(pix2, L.L_ALPHA_CHANNEL);
        }

        return pixd;
    }

    // leptonica: pixBilinearPtaGray (bilinear.c)
    /// <summary>Interpolated bilinear transformation of an 8 bpp image from point sets.</summary>
    /// <param name="ptad">4 pts of final coordinate space</param>
    /// <param name="ptas">4 pts of initial coordinate space</param>
    /// <param name="grayval">0 to bring in BLACK, 255 for WHITE</param>
    /// <returns>pixd; 8 bpp</returns>
    public Pix BilinearPtaGray(Pta ptad, Pta ptas, byte grayval)
    {
        const string func = "pixBilinearPtaGray";

        if (Depth != 8)
            throw Lept.Arg(func, "pixs must be 8 bpp");
        if (ptas.Count != 4)
            throw Lept.Arg(func, "ptas count not 4");
        if (ptad.Count != 4)
            throw Lept.Arg(func, "ptad count not 4");

        /* Get backwards transform from dest to src, and apply it */
        float[] vc = GetBilinearXformCoeffs(ptad, ptas);
        return BilinearGray(vc, grayval);
    }

    // leptonica: pixBilinearGray (bilinear.c)
    /// <summary>Interpolated bilinear transformation of an 8 bpp image.</summary>
    /// <param name="vc">vector of 8 coefficients for bilinear transformation</param>
    /// <param name="grayval">0 to bring in BLACK, 255 for WHITE</param>
    /// <returns>pixd; 8 bpp</returns>
    public Pix BilinearGray(float[] vc, byte grayval)
    {
        GetDimensions(out int w, out int h, out _);
        if (Depth != 8)
            throw Lept.Arg("pixBilinearGray", "pixs must be 8 bpp");

        float vc0 = vc[0], vc1 = vc[1], vc2 = vc[2], vc3 = vc[3];
        float vc4 = vc[4], vc5 = vc[5], vc6 = vc[6], vc7 = vc[7];

        uint[] datas = Data;
        int wpls = Wpl;
        Pix pixd = CreateTemplateNoInit(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        int w4 = w & ~3;
        uint fillWord = grayval * 0x01010101u;

        /* Iterate over destination pixels */
        LeptParallel.ForRows(h, w, (yStart, yEnd) => {
            ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
            ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
            for (int i = yStart; i < yEnd; i++) {
                int lined = i * wpld;
                float ry1 = vc1 * i, ry5 = vc5 * i;
                int j = 0;
                for (; j < w4; j += 4) {
                    uint b0 = (uint)LinearInterpolatePixelGrayCore(ref datasRef, wpls, w, h, vc0 * j + ry1 + vc2 * j * i + vc3, vc4 * j + ry5 + vc6 * j * i + vc7, grayval);
                    int j1 = j + 1;
                    uint b1 = (uint)LinearInterpolatePixelGrayCore(ref datasRef, wpls, w, h, vc0 * j1 + ry1 + vc2 * j1 * i + vc3, vc4 * j1 + ry5 + vc6 * j1 * i + vc7, grayval);
                    int j2 = j + 2;
                    uint b2 = (uint)LinearInterpolatePixelGrayCore(ref datasRef, wpls, w, h, vc0 * j2 + ry1 + vc2 * j2 * i + vc3, vc4 * j2 + ry5 + vc6 * j2 * i + vc7, grayval);
                    int j3 = j + 3;
                    uint b3 = (uint)LinearInterpolatePixelGrayCore(ref datasRef, wpls, w, h, vc0 * j3 + ry1 + vc2 * j3 * i + vc3, vc4 * j3 + ry5 + vc6 * j3 * i + vc7, grayval);
                    Unsafe.Add(ref datadRef, lined + (j >> 2)) = (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
                }
                if (j < w) {
                    ref uint dstLineRef = ref Unsafe.Add(ref datadRef, lined);
                    Unsafe.Add(ref dstLineRef, w >> 2) = fillWord;
                    for (; j < w; j++) {
                        int val = LinearInterpolatePixelGrayCore(ref datasRef, wpls, w, h, vc0 * j + ry1 + vc2 * j * i + vc3, vc4 * j + ry5 + vc6 * j * i + vc7, grayval);
                        SetDataByteUnchecked(ref dstLineRef, j, val);
                    }
                }
            }
        });

        return pixd;
    }

    /*-------------------------------------------------------------------------*
     *           Bilinear transform including alpha (blend) component          *
     *-------------------------------------------------------------------------*/
    // leptonica: pixBilinearPtaWithAlpha (bilinear.c)
    /// <summary>Bilinear transformation of a 32 bpp image with an added alpha channel.</summary>
    /// <param name="ptad">4 pts of final coordinate space</param>
    /// <param name="ptas">4 pts of initial coordinate space</param>
    /// <param name="pixg">[optional] 8 bpp, for alpha channel, can be null</param>
    /// <param name="fract">between 0.0 and 1.0, with 0.0 fully transparent
    /// and 1.0 fully opaque</param>
    /// <param name="border">of pixels added to capture transformed source pixels</param>
    /// <returns>pixd; 32 bpp rgba</returns>
    /// <remarks>
    /// (1) The alpha channel is transformed separately from pixs,
    ///     and aligns with it, being fully transparent outside the
    ///     boundary of the transformed pixs.  For pixels that are fully
    ///     transparent, a blending function like pixBlendWithGrayMask()
    ///     will give zero weight to corresponding pixels in pixs.
    /// (2) If %pixg is NULL, it is generated as an alpha layer that is
    ///     partially opaque, using %fract.  Otherwise, it is cropped
    ///     to %pixs if required and %fract is ignored.  The alpha channel
    ///     in %pixs is never used.
    /// (3) Colormaps are removed.
    /// (4) When pixs is transformed, it doesn't matter what color is brought
    ///     in because the alpha channel will be transparent (0) there.
    /// (5) To avoid losing source pixels in the destination, it may be
    ///     necessary to add a border to the source pix before doing
    ///     the bilinear transformation.  This can be any non-negative number.
    /// (6) The input %ptad and %ptas are in a coordinate space before
    ///     the border is added.  Internally, we compensate for this
    ///     before doing the bilinear transform on the image after
    ///     the border is added.
    /// (7) The default setting for the border values in the alpha channel
    ///     is 0 (transparent) for the outermost ring of pixels and
    ///     (0.5 * fract * 255) for the second ring.  When blended over
    ///     a second image, this
    ///     (a) shrinks the visible image to make a clean overlap edge
    ///         with an image below, and
    ///     (b) softens the edges by weakening the aliasing there.
    ///     Use l_setAlphaMaskBorder() to change these values.
    /// </remarks>
    public Pix BilinearPtaWithAlpha(Pta ptad, Pta ptas, Pix? pixg, float fract, int border)
    {
        const string func = "pixBilinearPtaWithAlpha";

        GetDimensions(out int ws, out int hs, out int d);
        if (d != 32 && Colormap == null)
            throw Lept.Arg(func, "pixs not cmapped or 32 bpp");
        if (pixg != null && pixg.Depth != 8) {
            Log.Warning(func, "pixg not 8 bpp; using 'fract' transparent alpha");
            pixg = null;
        }
        if (pixg == null && (fract < 0.0 || fract > 1.0)) {
            Log.Warning(func, "invalid fract; using 1.0 (fully transparent)");
            fract = 1.0f;
        }
        if (pixg == null && fract == 0.0)
            Log.Warning(func, "fully opaque alpha; image cannot be blended");

        /* Add border; the color doesn't matter */
        Pix pixb1 = AddBorder(border, 0);

        /* Transform the ptr arrays to work on the bordered image */
        Pta ptad2 = ptad.Transform(border, border, 1.0f, 1.0f);
        Pta ptas2 = ptas.Transform(border, border, 1.0f, 1.0f);

        /* Do separate bilinear transform of rgb channels of pixs and of pixg */
        Pix pixd = pixb1.BilinearPtaColor(ptad2, ptas2, 0);
        Pix pixg2;
        if (pixg == null) {
            pixg2 = Create(ws, hs, 8);
            if (fract == 1.0)
                pixg2.SetAll();
            else
                pixg2.SetAllArbitrary((uint)(int)(255.0 * fract));
        } else {
            pixg2 = pixg.ResizeToMatch(null, ws, hs);
        }
        if (ws > 10 && hs > 10) {  /* see note 7 */
            pixg2.SetBorderRingVal(1,
                (uint)(int)(255.0 * fract * AlphaMaskBorderVals[0]));
            pixg2.SetBorderRingVal(2,
                (uint)(int)(255.0 * fract * AlphaMaskBorderVals[1]));
        }
        Pix pixb2 = pixg2.AddBorder(border, 0);  /* must be black border */
        Pix pixga = pixb2.BilinearPtaGray(ptad2, ptas2, 0);
        pixd.SetRGBComponent(pixga, L.L_ALPHA_CHANNEL);
        pixd.Spp = 4;

        return pixd;
    }

    /*-------------------------------------------------------------*
     *                Bilinear coordinate transformation           *
     *-------------------------------------------------------------*/
    // leptonica: getBilinearXformCoeffs (bilinear.c)
    /// <summary>Finds the 8 coefficients of the bilinear transform that takes ptas into ptad.</summary>
    /// <param name="ptas">source 4 points; unprimed</param>
    /// <param name="ptad">transformed 4 points; primed</param>
    /// <returns>vector of coefficients of transform</returns>
    /// <remarks>
    /// We have a set of 8 equations, describing the bilinear
    /// transformation that takes 4 points ptas into 4 other
    /// points ptad.  These equations are:
    ///
    ///          x1' = c[0]*x1 + c[1]*y1 + c[2]*x1*y1 + c[3]
    ///          y1' = c[4]*x1 + c[5]*y1 + c[6]*x1*y1 + c[7]
    ///          x2' = c[0]*x2 + c[1]*y2 + c[2]*x2*y2 + c[3]
    ///          y2' = c[4]*x2 + c[5]*y2 + c[6]*x2*y2 + c[7]
    ///          x3' = c[0]*x3 + c[1]*y3 + c[2]*x3*y3 + c[3]
    ///          y3' = c[4]*x3 + c[5]*y3 + c[6]*x3*y3 + c[7]
    ///          x4' = c[0]*x4 + c[1]*y4 + c[2]*x4*y4 + c[3]
    ///          y4' = c[4]*x4 + c[5]*y4 + c[6]*x4*y4 + c[7]
    ///
    /// This can be represented as
    ///
    ///           AC = B
    ///
    /// where B and C are column vectors
    ///
    ///         B = [ x1' y1' x2' y2' x3' y3' x4' y4' ]
    ///         C = [ c[0] c[1] c[2] c[3] c[4] c[5] c[6] c[7] ]
    ///
    /// and A is the 8x8 matrix
    ///
    ///             x1   y1   x1*y1   1   0    0      0     0
    ///              0    0     0     0   x1   y1   x1*y1   1
    ///             x2   y2   x2*y2   1   0    0      0     0
    ///              0    0     0     0   x2   y2   x2*y2   1
    ///             x3   y3   x3*y3   1   0    0      0     0
    ///              0    0     0     0   x3   y3   x3*y3   1
    ///             x4   y4   x4*y4   1   0    0      0     0
    ///              0    0     0     0   x4   y4   x4*y4   1
    ///
    /// These eight equations are solved here for the coefficients C.
    ///
    /// These eight coefficients can then be used to find the mapping
    /// (x,y) --> (x',y'):
    ///
    ///           x' = c[0]x + c[1]y + c[2]xy + c[3]
    ///           y' = c[4]x + c[5]y + c[6]xy + c[7]
    ///
    /// that are implemented in bilinearXformSampledPt and
    /// bilinearXFormPt.
    /// </remarks>
    public static float[] GetBilinearXformCoeffs(Pta ptas, Pta ptad)
    {
        float[] b = new float[8];   /* rhs vector of primed coords X'; coeffs returned */
        float[][] a = new float[8][];  /* 8x8 matrix A  */

        ptas.GetPt(0, out float x1, out float y1);
        ptas.GetPt(1, out float x2, out float y2);
        ptas.GetPt(2, out float x3, out float y3);
        ptas.GetPt(3, out float x4, out float y4);
        ptad.GetPt(0, out b[0], out b[1]);
        ptad.GetPt(1, out b[2], out b[3]);
        ptad.GetPt(2, out b[4], out b[5]);
        ptad.GetPt(3, out b[6], out b[7]);

        for (int i = 0; i < 8; i++)
            a[i] = new float[8];
        a[0][0] = x1;
        a[0][1] = y1;
        a[0][2] = x1 * y1;
        a[0][3] = 1.0f;
        a[1][4] = x1;
        a[1][5] = y1;
        a[1][6] = x1 * y1;
        a[1][7] = 1.0f;
        a[2][0] = x2;
        a[2][1] = y2;
        a[2][2] = x2 * y2;
        a[2][3] = 1.0f;
        a[3][4] = x2;
        a[3][5] = y2;
        a[3][6] = x2 * y2;
        a[3][7] = 1.0f;
        a[4][0] = x3;
        a[4][1] = y3;
        a[4][2] = x3 * y3;
        a[4][3] = 1.0f;
        a[5][4] = x3;
        a[5][5] = y3;
        a[5][6] = x3 * y3;
        a[5][7] = 1.0f;
        a[6][0] = x4;
        a[6][1] = y4;
        a[6][2] = x4 * y4;
        a[6][3] = 1.0f;
        a[7][4] = x4;
        a[7][5] = y4;
        a[7][6] = x4 * y4;
        a[7][7] = 1.0f;

        GaussJordan(a, b, 8);

        return b;
    }

    // leptonica: bilinearXformSampledPt (bilinear.c)
    /// <summary>Finds the nearest pixel coordinates of the transformed point.</summary>
    /// <param name="vc">vector of 8 coefficients</param>
    /// <param name="x">initial point</param>
    /// <param name="y">initial point</param>
    /// <param name="pxp">transformed point</param>
    /// <param name="pyp">transformed point</param>
    public static void BilinearXformSampledPt(float[] vc, int x, int y, out int pxp, out int pyp)
    {
        pxp = (int)(vc[0] * x + vc[1] * y + vc[2] * x * y + vc[3] + 0.5);
        pyp = (int)(vc[4] * x + vc[5] * y + vc[6] * x * y + vc[7] + 0.5);
    }

    // leptonica: bilinearXformPt (bilinear.c)
    /// <summary>Computes the floating point location of the transformed point.</summary>
    /// <param name="vc">vector of 8 coefficients</param>
    /// <param name="x">initial point</param>
    /// <param name="y">initial point</param>
    /// <param name="pxp">transformed point</param>
    /// <param name="pyp">transformed point</param>
    public static void BilinearXformPt(float[] vc, int x, int y, out float pxp, out float pyp)
    {
        pxp = vc[0] * x + vc[1] * y + vc[2] * x * y + vc[3];
        pyp = vc[4] * x + vc[5] * y + vc[6] * x * y + vc[7];
    }
}
