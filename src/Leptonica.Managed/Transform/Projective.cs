using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Leptonica.ArrayAccess;

namespace Leptonica;

/*
 *  projective.c: Projective transforms.
 *
 *      Projective (4 pt) image transformation using a sampled
 *      (to nearest integer) transform on each dest point
 *           pixProjectiveSampledPta()
 *           pixProjectiveSampled()
 *
 *      Projective (4 pt) image transformation using interpolation
 *      (or area mapping) for anti-aliasing images that are
 *      2, 4, or 8 bpp gray, or colormapped, or 32 bpp RGB
 *           pixProjectivePta()
 *           pixProjective()
 *           pixProjectivePtaColor()
 *           pixProjectiveColor()
 *           pixProjectivePtaGray()
 *           pixProjectiveGray()
 *
 *      Projective transform including alpha (blend) component
 *           pixProjectivePtaWithAlpha()
 *
 *      Projective coordinate transformation
 *           getProjectiveXformCoeffs()
 *           projectiveXformSampledPt()
 *           projectiveXformPt()
 *
 *  A projective transform can be specified as a specific functional
 *  mapping between 4 points in the source and 4 points in the dest.
 *  It preserves straight lines, but is less stable than a bilinear
 *  transform, because it contains a division by a quantity that
 *  can get arbitrarily small.)
 *
 *  We give both a projective coordinate transformation and
 *  two projective image transformations.
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
 *  A typical application would be to remove keystoning
 *  due to a projective transform in the imaging system.
 *
 *  The projective transform is given by specifying two equations:
 *
 *      x' = (ax + by + c) / (gx + hy + 1)
 *      y' = (dx + ey + f) / (gx + hy + 1)
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
 *  For small warp where there is little scale change, (e.g.,
 *  for rotation) area mapping is nearly equivalent to interpolation.
 *
 *  Typical relative timing of pointwise transforms (sampled = 1.0):
 *  8 bpp:   sampled        1.0
 *           interpolated   1.5
 *  32 bpp:  sampled        1.0
 *           interpolated   1.6
 *  Additionally, the computation time/pixel is nearly the same
 *  for 8 bpp and 32 bpp, for both sampled and interpolated.
 *
 *  Differences from C:
 *    - The coefficient vector is returned as float[8].
 *    - Functions that return NULL or an error code in C throw instead.
 *      In particular projectiveXformSampledPt() and projectiveXformPt()
 *      throw when the denominator is 0.0.
 *    - Like in Affine.cs, all functions are members of Pix.
 *
 *  Not ported: nothing.
 */
public sealed partial class Pix
{
    /*-------------------------------------------------------------*
     *             Sampled projective image transformation           *
     *-------------------------------------------------------------*/
    // leptonica: pixProjectiveSampledPta (projective.c)
    /// <summary>Projective transformation, sampled to the nearest source pixel, from point sets.</summary>
    /// <param name="ptad">4 pts of final coordinate space</param>
    /// <param name="ptas">4 pts of initial coordinate space</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>pixd; all depths</returns>
    /// <remarks>
    /// (1) Brings in either black or white pixels from the boundary.
    /// (2) Retains colormap, which you can do for a sampled transform.
    /// (3) No 3 of the 4 points may be collinear.
    /// (4) For 8 and 32 bpp pix, better quality is obtained by the
    ///     somewhat slower pixProjectivePta().  See that
    ///     function for relative timings between sampled and interpolated.
    /// </remarks>
    public Pix ProjectiveSampledPta(Pta ptad, Pta ptas, int incolor)
    {
        const string func = "pixProjectiveSampledPta";

        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid incolor");
        if (ptas.Count != 4)
            throw Lept.Arg(func, "ptas count not 4");
        if (ptad.Count != 4)
            throw Lept.Arg(func, "ptad count not 4");

        /* Get backwards transform from dest to src, and apply it */
        float[] vc = GetProjectiveXformCoeffs(ptad, ptas);
        return ProjectiveSampled(vc, incolor);
    }

    // leptonica: pixProjectiveSampled (projective.c)
    /// <summary>Projective transformation, sampled to the nearest source pixel.</summary>
    /// <param name="vc">vector of 8 coefficients for projective transformation</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>pixd; all depths</returns>
    /// <remarks>
    /// (1) Brings in either black or white pixels from the boundary.
    /// (2) Retains colormap, which you can do for a sampled transform.
    /// (3) For 8 or 32 bpp, much better quality is obtained by the
    ///     somewhat slower pixProjective().  See that function
    ///     for relative timings between sampled and interpolated.
    /// </remarks>
    public Pix ProjectiveSampled(float[] vc, int incolor)
    {
        const string func = "pixProjectiveSampled";

        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid incolor");
        GetDimensions(out int w, out int h, out int d);
        if (d != 1 && d != 2 && d != 4 && d != 8 && d != 32)
            throw Lept.Arg(func, "depth not 1, 2, 4, 8 or 16");

        float vc0 = vc[0], vc1 = vc[1], vc2 = vc[2];
        float vc3 = vc[3], vc4 = vc[4], vc5 = vc[5];
        float vc6 = vc[6], vc7 = vc[7];
        bool canParallel = ProjectiveDenomNonZeroOnRect(vc6, vc7, w, h);

        uint[] datas = Data;
        int wpls = Wpl;
        PixColormap? cmap = Colormap;

        if (cmap == null && Spp != 4 && (d == 1 || d == 8 || d == 32) &&
            LeptGpu.TryGetActive((long)w * h, 14, out IGpuAccelerator? gpuSampled) &&
            gpuSampled.TryProjective(this, vc, incolor, sampled: true, out Pix? gpuSampledPix)) {
            return gpuSampledPix;
        }

        if (d == 32 && cmap == null && canParallel) {
            Pix pixd32 = CreateTemplateNoInit(this);
            uint fill32 = incolor == L.L_BRING_IN_BLACK ? 0u : 0xffffffffu;
            uint[] datad32 = pixd32.Data;
            int wpld32 = pixd32.Wpl;
            LeptParallel.ForRows(h, w, (yStart, yEnd) => {
                ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
                ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad32);
                for (int i = yStart; i < yEnd; i++) {
                    int lined = i * wpld32;
                    float ry1 = vc1 * i, ry4 = vc4 * i, ry7 = vc7 * i;
                    for (int j = 0; j < w; j++) {
                        double denom = vc6 * j + ry7 + 1.0f;
                        float factor = (float)(1.0f / denom);
                        int x = (int)(factor * (vc0 * j + ry1 + vc2) + 0.5f);
                        int y = (int)(factor * (vc3 * j + ry4 + vc5) + 0.5f);
                        Unsafe.Add(ref datadRef, lined + j) =
                            (uint)x < (uint)w && (uint)y < (uint)h
                                ? Unsafe.Add(ref datasRef, y * wpls + x)
                                : fill32;
                    }
                }
            });
            return pixd32;
        }

        if (d == 8 && canParallel) {
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
                    float ry1 = vc1 * i, ry4 = vc4 * i, ry7 = vc7 * i;
                    int j = 0;
                    for (; j < w4; j += 4) {
                        float f0 = (float)(1.0f / (double)(vc6 * j + ry7 + 1.0f));
                        int x0 = (int)(f0 * (vc0 * j + ry1 + vc2) + 0.5f);
                        int y0 = (int)(f0 * (vc3 * j + ry4 + vc5) + 0.5f);
                        uint b0 = (uint)x0 < (uint)w && (uint)y0 < (uint)h
                            ? (uint)GetDataByteUnchecked(ref Unsafe.Add(ref datasRef, y0 * wpls), x0) : fill8;

                        int j1 = j + 1;
                        float f1 = (float)(1.0f / (double)(vc6 * j1 + ry7 + 1.0f));
                        int x1 = (int)(f1 * (vc0 * j1 + ry1 + vc2) + 0.5f);
                        int y1 = (int)(f1 * (vc3 * j1 + ry4 + vc5) + 0.5f);
                        uint b1 = (uint)x1 < (uint)w && (uint)y1 < (uint)h
                            ? (uint)GetDataByteUnchecked(ref Unsafe.Add(ref datasRef, y1 * wpls), x1) : fill8;

                        int j2 = j + 2;
                        float f2 = (float)(1.0f / (double)(vc6 * j2 + ry7 + 1.0f));
                        int x2 = (int)(f2 * (vc0 * j2 + ry1 + vc2) + 0.5f);
                        int y2 = (int)(f2 * (vc3 * j2 + ry4 + vc5) + 0.5f);
                        uint b2 = (uint)x2 < (uint)w && (uint)y2 < (uint)h
                            ? (uint)GetDataByteUnchecked(ref Unsafe.Add(ref datasRef, y2 * wpls), x2) : fill8;

                        int j3 = j + 3;
                        float f3 = (float)(1.0f / (double)(vc6 * j3 + ry7 + 1.0f));
                        int x3 = (int)(f3 * (vc0 * j3 + ry1 + vc2) + 0.5f);
                        int y3 = (int)(f3 * (vc3 * j3 + ry4 + vc5) + 0.5f);
                        uint b3 = (uint)x3 < (uint)w && (uint)y3 < (uint)h
                            ? (uint)GetDataByteUnchecked(ref Unsafe.Add(ref datasRef, y3 * wpls), x3) : fill8;

                        Unsafe.Add(ref datadRef, lined + (j >> 2)) = (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
                    }
                    if (j < w) {
                        ref uint dstLineRef = ref Unsafe.Add(ref datadRef, lined);
                        Unsafe.Add(ref dstLineRef, w >> 2) = fillWord8;
                        for (; j < w; j++) {
                            float f = (float)(1.0f / (double)(vc6 * j + ry7 + 1.0f));
                            int x = (int)(f * (vc0 * j + ry1 + vc2) + 0.5f);
                            int y = (int)(f * (vc3 * j + ry4 + vc5) + 0.5f);
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
        Action<int, int> body = (yStart, yEnd) => {
            for (int i = yStart; i < yEnd; i++) {
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (int j = 0; j < w; j++) {
                    ProjectiveXformSampledPt(vc, j, i, out int x, out int y);
                    if ((uint)x >= (uint)w || (uint)y >= (uint)h)
                        continue;
                    ReadOnlySpan<uint> lines = datas.AsSpan(y * wpls, wpls);
                    if (d == 1) {
                        int val = GetDataBit(lines, x);
                        SetDataBitVal(lined, j, val);
                    } else if (d == 8) {
                        int val = GetDataByte(lines, x);
                        SetDataByte(lined, j, val);
                    } else if (d == 32) {
                        lined[j] = lines[x];
                    } else if (d == 2) {
                        int val = GetDataDibit(lines, x);
                        SetDataDibit(lined, j, val);
                    } else if (d == 4) {
                        int val = GetDataQbit(lines, x);
                        SetDataQbit(lined, j, val);
                    }
                }
            }
        };
        if (canParallel)
            LeptParallel.ForRows(h, w, body);
        else
            body(0, h);

        return pixd;
    }

    /*---------------------------------------------------------------------*
     *            Interpolated projective image transformation             *
     *---------------------------------------------------------------------*/
    // leptonica: pixProjectivePta (projective.c)
    /// <summary>Interpolated projective transformation from point sets.</summary>
    /// <param name="ptad">4 pts of final coordinate space</param>
    /// <param name="ptas">4 pts of initial coordinate space</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>pixd; all depths; colormap ok</returns>
    /// <remarks>
    /// (1) Brings in either black or white pixels from the boundary
    /// (2) Removes any existing colormap, if necessary, before transforming
    /// </remarks>
    public Pix ProjectivePta(Pta ptad, Pta ptas, int incolor)
    {
        const string func = "pixProjectivePta";

        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid incolor");
        if (ptas.Count != 4)
            throw Lept.Arg(func, "ptas count not 4");
        if (ptad.Count != 4)
            throw Lept.Arg(func, "ptad count not 4");

        if (Depth == 1)
            return ProjectiveSampledPta(ptad, ptas, incolor);

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
            pixd = pixt2.ProjectivePtaGray(ptad, ptas, (byte)colorval);
        else  /* d == 32 */
            pixd = pixt2.ProjectivePtaColor(ptad, ptas, colorval);
        return pixd;
    }

    // leptonica: pixProjective (projective.c)
    /// <summary>Interpolated projective transformation with a coefficient vector.</summary>
    /// <param name="vc">vector of 8 coefficients for projective transformation</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>pixd; all depths; colormap ok</returns>
    /// <remarks>
    /// (1) Brings in either black or white pixels from the boundary
    /// (2) Removes any existing colormap, if necessary, before transforming
    /// </remarks>
    public Pix Projective(float[] vc, int incolor)
    {
        if (Depth == 1)
            return ProjectiveSampled(vc, incolor);

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
            LeptGpu.TryGetActive((long)pixt2.Width * pixt2.Height, d == 32 ? 26 : 16, out IGpuAccelerator? gpu) &&
            gpu.TryProjective(pixt2, vc, incolor, sampled: false, out Pix? gpuPix)) {
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
            pixd = pixt2.ProjectiveGray(vc, (byte)colorval);
        else  /* d == 32 */
            pixd = pixt2.ProjectiveColor(vc, colorval);
        return pixd;
    }

    // leptonica: pixProjectivePtaColor (projective.c)
    /// <summary>Interpolated projective transformation of a 32 bpp image from point sets.</summary>
    /// <param name="ptad">4 pts of final coordinate space</param>
    /// <param name="ptas">4 pts of initial coordinate space</param>
    /// <param name="colorval">e.g., 0 to bring in BLACK, 0xffffff00 for WHITE</param>
    /// <returns>pixd; 32 bpp</returns>
    public Pix ProjectivePtaColor(Pta ptad, Pta ptas, uint colorval)
    {
        const string func = "pixProjectivePtaColor";

        if (Depth != 32)
            throw Lept.Arg(func, "pixs must be 32 bpp");
        if (ptas.Count != 4)
            throw Lept.Arg(func, "ptas count not 4");
        if (ptad.Count != 4)
            throw Lept.Arg(func, "ptad count not 4");

        /* Get backwards transform from dest to src, and apply it */
        float[] vc = GetProjectiveXformCoeffs(ptad, ptas);
        return ProjectiveColor(vc, colorval);
    }

    private static bool ProjectiveDenomNonZeroOnRect(float vc6, float vc7, int w, int h)
    {
        if (w <= 0 || h <= 0) return true;
        float xm = w - 1;
        float ym = h - 1;
        float c00 = 1.0f;
        float c10 = vc6 * xm + 1.0f;
        float c01 = vc7 * ym + 1.0f;
        float c11 = vc6 * xm + vc7 * ym + 1.0f;
        const float eps = 1e-5f;
        return (c00 > eps && c10 > eps && c01 > eps && c11 > eps) ||
               (c00 < -eps && c10 < -eps && c01 < -eps && c11 < -eps);
    }

    // leptonica: pixProjectiveColor (projective.c)
    /// <summary>Interpolated projective transformation of a 32 bpp image.</summary>
    /// <param name="vc">vector of 8 coefficients for projective transformation</param>
    /// <param name="colorval">e.g., 0 to bring in BLACK, 0xffffff00 for WHITE</param>
    /// <returns>pixd; 32 bpp</returns>
    public Pix ProjectiveColor(float[] vc, uint colorval)
    {
        GetDimensions(out int w, out int h, out int d);
        if (d != 32)
            throw Lept.Arg("pixProjectiveColor", "pixs must be 32 bpp");

        float vc0 = vc[0], vc1 = vc[1], vc2 = vc[2];
        float vc3 = vc[3], vc4 = vc[4], vc5 = vc[5];
        float vc6 = vc[6], vc7 = vc[7];

        uint[] datas = Data;
        int wpls = Wpl;
        Pix pixd = CreateTemplateNoInit(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;

        if (ProjectiveDenomNonZeroOnRect(vc6, vc7, w, h)) {
            LeptParallel.ForRows(h, w, (yStart, yEnd) => {
                ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
                ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
                for (int i = yStart; i < yEnd; i++) {
                    int lined = i * wpld;
                    float ry1 = vc1 * i, ry4 = vc4 * i, ry7 = vc7 * i;
                    for (int j = 0; j < w; j++) {
                        float factor = (float)(1.0f / (double)(vc6 * j + ry7 + 1.0f));
                        float x = factor * (vc0 * j + ry1 + vc2);
                        float y = factor * (vc3 * j + ry4 + vc5);
                        Unsafe.Add(ref datadRef, lined + j) =
                            LinearInterpolatePixelColorCore(ref datasRef, wpls, w, h, x, y, colorval);
                    }
                }
            });
        } else {
            for (int i = 0; i < h; i++) {
                int lined = i * wpld;
                for (int j = 0; j < w; j++) {
                    ProjectiveXformPt(vc, j, i, out float x, out float y);
                    datad[lined + j] = LinearInterpolatePixelColor(datas, wpls, w, h, x, y, colorval);
                }
            }
        }

        /* If rgba, transform the pixs alpha channel and insert in pixd */
        if (Spp == 4) {
            Pix pix1 = GetRGBComponent(L.L_ALPHA_CHANNEL);
            Pix pix2 = pix1.ProjectiveGray(vc, 255);  /* bring in opaque */
            pixd.SetRGBComponent(pix2, L.L_ALPHA_CHANNEL);
        }

        return pixd;
    }

    // leptonica: pixProjectivePtaGray (projective.c)
    /// <summary>Interpolated projective transformation of an 8 bpp image from point sets.</summary>
    /// <param name="ptad">4 pts of final coordinate space</param>
    /// <param name="ptas">4 pts of initial coordinate space</param>
    /// <param name="grayval">0 to bring in BLACK, 255 for WHITE</param>
    /// <returns>pixd; 8 bpp</returns>
    public Pix ProjectivePtaGray(Pta ptad, Pta ptas, byte grayval)
    {
        const string func = "pixProjectivePtaGray";

        if (Depth != 8)
            throw Lept.Arg(func, "pixs must be 8 bpp");
        if (ptas.Count != 4)
            throw Lept.Arg(func, "ptas count not 4");
        if (ptad.Count != 4)
            throw Lept.Arg(func, "ptad count not 4");

        /* Get backwards transform from dest to src, and apply it */
        float[] vc = GetProjectiveXformCoeffs(ptad, ptas);
        return ProjectiveGray(vc, grayval);
    }

    // leptonica: pixProjectiveGray (projective.c)
    /// <summary>Interpolated projective transformation of an 8 bpp image.</summary>
    /// <param name="vc">vector of 8 coefficients for projective transformation</param>
    /// <param name="grayval">0 to bring in BLACK, 255 for WHITE</param>
    /// <returns>pixd; 8 bpp</returns>
    public Pix ProjectiveGray(float[] vc, byte grayval)
    {
        GetDimensions(out int w, out int h, out _);
        if (Depth != 8)
            throw Lept.Arg("pixProjectiveGray", "pixs must be 8 bpp");

        float vc0 = vc[0], vc1 = vc[1], vc2 = vc[2];
        float vc3 = vc[3], vc4 = vc[4], vc5 = vc[5];
        float vc6 = vc[6], vc7 = vc[7];

        uint[] datas = Data;
        int wpls = Wpl;
        Pix pixd = CreateTemplateNoInit(this);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        int w4 = w & ~3;
        uint fillWord = grayval * 0x01010101u;

        if (ProjectiveDenomNonZeroOnRect(vc6, vc7, w, h)) {
            LeptParallel.ForRows(h, w, (yStart, yEnd) => {
                ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
                ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
                for (int i = yStart; i < yEnd; i++) {
                    int lined = i * wpld;
                    float ry1 = vc1 * i, ry4 = vc4 * i, ry7 = vc7 * i;
                    int j = 0;
                    for (; j < w4; j += 4) {
                        float f0 = (float)(1.0f / (double)(vc6 * j + ry7 + 1.0f));
                        uint b0 = (uint)LinearInterpolatePixelGrayCore(ref datasRef, wpls, w, h, f0 * (vc0 * j + ry1 + vc2), f0 * (vc3 * j + ry4 + vc5), grayval);
                        int j1 = j + 1;
                        float f1 = (float)(1.0f / (double)(vc6 * j1 + ry7 + 1.0f));
                        uint b1 = (uint)LinearInterpolatePixelGrayCore(ref datasRef, wpls, w, h, f1 * (vc0 * j1 + ry1 + vc2), f1 * (vc3 * j1 + ry4 + vc5), grayval);
                        int j2 = j + 2;
                        float f2 = (float)(1.0f / (double)(vc6 * j2 + ry7 + 1.0f));
                        uint b2 = (uint)LinearInterpolatePixelGrayCore(ref datasRef, wpls, w, h, f2 * (vc0 * j2 + ry1 + vc2), f2 * (vc3 * j2 + ry4 + vc5), grayval);
                        int j3 = j + 3;
                        float f3 = (float)(1.0f / (double)(vc6 * j3 + ry7 + 1.0f));
                        uint b3 = (uint)LinearInterpolatePixelGrayCore(ref datasRef, wpls, w, h, f3 * (vc0 * j3 + ry1 + vc2), f3 * (vc3 * j3 + ry4 + vc5), grayval);
                        Unsafe.Add(ref datadRef, lined + (j >> 2)) = (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
                    }
                    if (j < w) {
                        ref uint dstLineRef = ref Unsafe.Add(ref datadRef, lined);
                        Unsafe.Add(ref dstLineRef, w >> 2) = fillWord;
                        for (; j < w; j++) {
                            float f = (float)(1.0f / (double)(vc6 * j + ry7 + 1.0f));
                            int val = LinearInterpolatePixelGrayCore(ref datasRef, wpls, w, h, f * (vc0 * j + ry1 + vc2), f * (vc3 * j + ry4 + vc5), grayval);
                            SetDataByteUnchecked(ref dstLineRef, j, val);
                        }
                    }
                }
            });
        } else {
            pixd.SetAllArbitrary(grayval);
            for (int i = 0; i < h; i++) {
                Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                for (int j = 0; j < w; j++) {
                    ProjectiveXformPt(vc, j, i, out float x, out float y);
                    int val = LinearInterpolatePixelGray(datas, wpls, w, h, x, y, grayval);
                    SetDataByte(lined, j, val);
                }
            }
        }

        return pixd;
    }

    /*-------------------------------------------------------------------------*
     *           Projective transform including alpha (blend) component          *
     *-------------------------------------------------------------------------*/
    // leptonica: pixProjectivePtaWithAlpha (projective.c)
    /// <summary>Projective transformation of a 32 bpp image with an added alpha channel.</summary>
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
    ///     the projective transformation.  This can be any non-negative number.
    /// (6) The input %ptad and %ptas are in a coordinate space before
    ///     the border is added.  Internally, we compensate for this
    ///     before doing the projective transform on the image after
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
    public Pix ProjectivePtaWithAlpha(Pta ptad, Pta ptas, Pix? pixg, float fract, int border)
    {
        const string func = "pixProjectivePtaWithAlpha";

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
            Log.Warning(func, "fully opaque alpha; image will not be blended");

        /* Add border; the color doesn't matter */
        Pix pixb1 = AddBorder(border, 0);

        /* Transform the ptr arrays to work on the bordered image */
        Pta ptad2 = ptad.Transform(border, border, 1.0f, 1.0f);
        Pta ptas2 = ptas.Transform(border, border, 1.0f, 1.0f);

        /* Do separate projective transform of rgb channels of pixs and of pixg */
        Pix pixd = pixb1.ProjectivePtaColor(ptad2, ptas2, 0);
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
        Pix pixga = pixb2.ProjectivePtaGray(ptad2, ptas2, 0);
        pixd.SetRGBComponent(pixga, L.L_ALPHA_CHANNEL);
        pixd.Spp = 4;

        return pixd;
    }

    /*-------------------------------------------------------------*
     *                Projective coordinate transformation         *
     *-------------------------------------------------------------*/
    // leptonica: getProjectiveXformCoeffs (projective.c)
    /// <summary>Finds the 8 coefficients of the projective transform that takes ptas into ptad.</summary>
    /// <param name="ptas">source 4 points; unprimed</param>
    /// <param name="ptad">transformed 4 points; primed</param>
    /// <returns>vector of coefficients of transform</returns>
    /// <remarks>
    /// We have a set of 8 equations, describing the projective
    /// transformation that takes 4 points ptas into 4 other
    /// points ptad.  These equations are:
    ///
    ///          x1' = (c[0]*x1 + c[1]*y1 + c[2]) / (c[6]*x1 + c[7]*y1 + 1)
    ///          y1' = (c[3]*x1 + c[4]*y1 + c[5]) / (c[6]*x1 + c[7]*y1 + 1)
    ///          x2' = (c[0]*x2 + c[1]*y2 + c[2]) / (c[6]*x2 + c[7]*y2 + 1)
    ///          y2' = (c[3]*x2 + c[4]*y2 + c[5]) / (c[6]*x2 + c[7]*y2 + 1)
    ///          x3' = (c[0]*x3 + c[1]*y3 + c[2]) / (c[6]*x3 + c[7]*y3 + 1)
    ///          y3' = (c[3]*x3 + c[4]*y3 + c[5]) / (c[6]*x3 + c[7]*y3 + 1)
    ///          x4' = (c[0]*x4 + c[1]*y4 + c[2]) / (c[6]*x4 + c[7]*y4 + 1)
    ///          y4' = (c[3]*x4 + c[4]*y4 + c[5]) / (c[6]*x4 + c[7]*y4 + 1)
    ///
    /// Multiplying both sides of each eqn by the denominator, we get
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
    ///             x1   y1     1     0   0    0   -x1*x1'  -y1*x1'
    ///              0    0     0    x1   y1   1   -x1*y1'  -y1*y1'
    ///             x2   y2     1     0   0    0   -x2*x2'  -y2*x2'
    ///              0    0     0    x2   y2   1   -x2*y2'  -y2*y2'
    ///             x3   y3     1     0   0    0   -x3*x3'  -y3*x3'
    ///              0    0     0    x3   y3   1   -x3*y3'  -y3*y3'
    ///             x4   y4     1     0   0    0   -x4*x4'  -y4*x4'
    ///              0    0     0    x4   y4   1   -x4*y4'  -y4*y4'
    ///
    /// These eight equations are solved here for the coefficients C.
    ///
    /// These eight coefficients can then be used to find the mapping
    /// (x,y) --> (x',y'):
    ///
    ///           x' = (c[0]x + c[1]y + c[2]) / (c[6]x + c[7]y + 1)
    ///           y' = (c[3]x + c[4]y + c[5]) / (c[6]x + c[7]y + 1)
    ///
    /// that is implemented in projectiveXformSampled and
    /// projectiveXFormInterpolated.
    /// </remarks>
    public static float[] GetProjectiveXformCoeffs(Pta ptas, Pta ptad)
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
        a[0][2] = 1.0f;
        a[0][6] = -x1 * b[0];
        a[0][7] = -y1 * b[0];
        a[1][3] = x1;
        a[1][4] = y1;
        a[1][5] = 1;
        a[1][6] = -x1 * b[1];
        a[1][7] = -y1 * b[1];

        a[2][0] = x2;
        a[2][1] = y2;
        a[2][2] = 1.0f;
        a[2][6] = -x2 * b[2];
        a[2][7] = -y2 * b[2];
        a[3][3] = x2;
        a[3][4] = y2;
        a[3][5] = 1;
        a[3][6] = -x2 * b[3];
        a[3][7] = -y2 * b[3];

        a[4][0] = x3;
        a[4][1] = y3;
        a[4][2] = 1.0f;
        a[4][6] = -x3 * b[4];
        a[4][7] = -y3 * b[4];
        a[5][3] = x3;
        a[5][4] = y3;
        a[5][5] = 1;
        a[5][6] = -x3 * b[5];
        a[5][7] = -y3 * b[5];

        a[6][0] = x4;
        a[6][1] = y4;
        a[6][2] = 1.0f;
        a[6][6] = -x4 * b[6];
        a[6][7] = -y4 * b[6];
        a[7][3] = x4;
        a[7][4] = y4;
        a[7][5] = 1;
        a[7][6] = -x4 * b[7];
        a[7][7] = -y4 * b[7];

        GaussJordan(a, b, 8);

        return b;
    }

    // leptonica: projectiveXformSampledPt (projective.c)
    /// <summary>Finds the nearest pixel coordinates of the transformed point.</summary>
    /// <param name="vc">vector of 8 coefficients</param>
    /// <param name="x">initial point</param>
    /// <param name="y">initial point</param>
    /// <param name="pxp">transformed point</param>
    /// <param name="pyp">transformed point</param>
    public static void ProjectiveXformSampledPt(float[] vc, int x, int y, out int pxp, out int pyp)
    {
        double denom;
        if ((denom = vc[6] * x + vc[7] * y + 1.0f) == 0.0f)
            throw Lept.Fail("projectiveXformSampledPt", "denom = 0.0");
        float factor = (float)(1.0f / denom);
        pxp = (int)(factor * (vc[0] * x + vc[1] * y + vc[2]) + 0.5f);
        pyp = (int)(factor * (vc[3] * x + vc[4] * y + vc[5]) + 0.5f);
    }

    // leptonica: projectiveXformPt (projective.c)
    /// <summary>Computes the floating point location of the transformed point.</summary>
    /// <param name="vc">vector of 8 coefficients</param>
    /// <param name="x">initial point</param>
    /// <param name="y">initial point</param>
    /// <param name="pxp">transformed point</param>
    /// <param name="pyp">transformed point</param>
    public static void ProjectiveXformPt(float[] vc, int x, int y, out float pxp, out float pyp)
    {
        double denom;
        if ((denom = vc[6] * x + vc[7] * y + 1.0f) == 0.0f)
            throw Lept.Fail("projectiveXformPt", "denom = 0.0");
        float factor = (float)(1.0f / denom);
        pxp = factor * (vc[0] * x + vc[1] * y + vc[2]);
        pyp = factor * (vc[3] * x + vc[4] * y + vc[5]);
    }
}
