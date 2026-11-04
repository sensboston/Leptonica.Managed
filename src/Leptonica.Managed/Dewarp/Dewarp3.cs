using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Leptonica;

/*
 *  dewarp3.c: applying and stripping the page disparity model.
 */

public sealed partial class Dewarpa
{
    /*----------------------------------------------------------------------*
     *                 Apply warping disparity array to pix                 *
     *----------------------------------------------------------------------*/
    /// <summary>Applies the disparity arrays of a page model to an image.</summary>
    /// <param name="pageno">The page number of the model to use, which may be a reference model.</param>
    /// <param name="pixs">The image to modify; 1, 8 or 32 bpp.</param>
    /// <param name="grayin">The gray value, from 0 to 255, for pixels brought in from outside the image; -1 uses the pixels on the boundary of <paramref name="pixs"/>.</param>
    /// <param name="x">The x origin for generation of the disparity arrays.</param>
    /// <param name="y">The y origin for generation of the disparity arrays.</param>
    /// <param name="ppixd">Receives the disparity-corrected image.</param>
    /// <param name="debugfile">The path of a file for debug output, or <c>null</c> to skip debug output.</param>
    /// <returns>0 if successful; 1 on error, including when no models or reference models are available.</returns>
    /// <exception cref="ArgumentException"><paramref name="pixs"/> is <c>null</c>.</exception>
    /// <remarks>
    /// <para>If the models and reference models have not been validated, this does so by calling <see cref="Dewarpa.InsertRefModels"/>.</para>
    /// <para>This works with both stripped and full resolution page models. If the full resolution arrays are missing, they are rebuilt.</para>
    /// <para>The caller must handle the error returned when there are no valid models for the page, typically by using the input image.</para>
    /// <para>If there is no model for <paramref name="pageno"/>, the model of its reference page is used and the result is stored in the model for <paramref name="pageno"/>.</para>
    /// <para>
    /// If <paramref name="x"/> or <paramref name="y"/> is positive, it is used with <paramref name="pixs"/> to determine the required
    /// slope-based extension of the full resolution arrays. With (0,0) all extension is to the right and down. Nonzero values are
    /// useful when <paramref name="pixs"/> is deliberately undercropped.
    /// </para>
    /// <para>
    /// When applying disparity to many images, call <see cref="Dewarp.Minimize"/> on the model for the page after saving each result.
    /// This releases the stored images and the full resolution arrays, which together hold about 16 bytes for each pixel of the source.
    /// </para>
    /// </remarks>
    // leptonica: dewarpaApplyDisparity (dewarp3.c)
    public int ApplyDisparity(int pageno, Pix pixs, int grayin, int x, int y,
                              out Pix ppixd, string? debugfile)
    {
        const string func = "dewarpaApplyDisparity";
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");
        ppixd = Pix.Clone(pixs);
        if (grayin > 255) {
            Log.Warning(func, $"invalid grayin = {grayin}; clipping at 255");
            grayin = 255;
        }

        if (ApplyInit(pageno, pixs, x, y, out Dewarp? dew, debugfile) != 0 || dew == null) {
            Log.Error(func, "no model available");
            return 1;
        }

        bool applyHoriz = useboth != 0 && dew.hsuccess != 0 && dew.skip_horiz == 0;
        if (debugfile == null && applyHoriz && dew.hvalid != 0 && dew.fullhdispar != null) {
            Pix? pixvh = Dewarp.ApplyVertAndHorizDisparity(dew, pixs, grayin);
            if (pixvh == null) {
                dew.Minimize();
                Log.Error(func, "pixd not made");
                return 1;
            }
            ppixd = pixvh;
            dew.Minimize();
            return 0;
        }

        Pix? pixv = Dewarp.ApplyVertDisparity(dew, pixs, grayin);
        if (pixv == null) {
            dew.Minimize();
            Log.Error(func, "pixv not made");
            return 1;
        }
        ppixd = pixv;
        if (debugfile != null) {
            pixv.DisplayWithTitle(300, 0, "pixv", 1);
            Utils.LeptRmdir("lept/dewapply");
            Utils.LeptMkdir("lept/dewapply");
            pixs.WriteDebug("/tmp/lept/dewapply/001.png", L.IFF_PNG);
            pixv.WriteDebug("/tmp/lept/dewapply/002.png", L.IFF_PNG);
        }

        if (applyHoriz) {
            if (dew.hvalid == 0) {
                Log.Info(func, $"invalid horiz model for page {pageno}");
            } else {
                Pix? pixh = Dewarp.ApplyHorizDisparity(dew, pixv, grayin);
                if (pixh != null) {
                    ppixd = pixh;
                    if (debugfile != null) {
                        pixh.DisplayWithTitle(600, 0, "pixh", 1);
                        pixh.WriteDebug("/tmp/lept/dewapply/003.png", L.IFF_PNG);
                    }
                } else {
                    Log.Error(func, $"horiz disparity failed on page {pageno}");
                }
            }
        }

        if (debugfile != null) {
            Dewarp? dew1 = GetDewarp(pageno);
            dew1?.Debug("lept/dewapply", 0);
            PdfIo.ConvertFilesToPdf("/tmp/lept/dewapply", null, 250, 1.0f, 0, 0,
                                    "Dewarp Apply Disparity", debugfile);
            Console.Error.WriteLine($"pdf file: {debugfile}");
        }

        dew.Minimize();
        return 0;
    }

    // leptonica: dewarpaApplyInit (dewarp3.c)
    private int ApplyInit(int pageno, Pix pixs, int x, int y,
                          out Dewarp? pdew, string? debugfile)
    {
        const string func = "dewarpaApplyInit";
        pdew = null;
        if (pageno < 0 || pageno > maxpage) {
            Log.Error(func, "invalid pageno");
            return 1;
        }
        if (pixs == null) {
            Log.Error(func, "pixs not defined");
            return 1;
        }
        if (x < 0) x = 0;
        if (y < 0) y = 0;
        int debug = debugfile != null ? 1 : 0;

        if (modelsready == 0)
            InsertRefModels(0, debug);

        Dewarp? dew1 = GetDewarp(pageno);
        if (dew1 == null) {
            Log.Info(func, $"no valid dew model for page {pageno}");
            return 1;
        }

        Dewarp? dew2 = dew1.hasref != 0 ? GetDewarp(dew1.refpage) : dew1;
        if (dew2 == null || dew2.vvalid == 0) {
            Log.Error(func, "no model; shouldn't happen");
            return 1;
        }
        pdew = dew2;

        if (useboth != 0 && check_columns != 0) {
            Pix pix1 = pixs.ConvertTo1(140);
            int ncols = pix1.CountTextColumns(0.3f, 0.5f, 0.1f, null);
            if (ncols > 1) {
                Log.Info(func, $"found {ncols} columns; not correcting horiz disparity");
                dew2.skip_horiz = 1;
            } else {
                dew2.skip_horiz = 0;
            }
        }

        dew2.PopulateFullRes(pixs, x, y);
        return 0;
    }

    /*----------------------------------------------------------------------*
     *                 Apply warping disparity array to boxa                *
     *----------------------------------------------------------------------*/
    /// <summary>Applies the disparity arrays of a page model to an array of boxes, in either mapping direction.</summary>
    /// <param name="pageno">The page number of the model to use, which may be a reference model.</param>
    /// <param name="pixs">The initial image, used for alignment and debugging.</param>
    /// <param name="boxas">The boxes to map.</param>
    /// <param name="mapdir">1 to map forward from the original to the dewarped image; 0 to map backward.</param>
    /// <param name="x">The x origin for generation of the disparity arrays with respect to the source region.</param>
    /// <param name="y">The y origin for generation of the disparity arrays with respect to the source region.</param>
    /// <param name="pboxad">Receives the mapped boxes; if an error occurs, a copy of the input boxes.</param>
    /// <param name="debugfile">The path of a file for debug output, or <c>null</c> to skip debug output.</param>
    /// <returns>0 if successful; 1 on error, including when no models or reference models are available.</returns>
    /// <exception cref="ArgumentException"><paramref name="boxas"/> is <c>null</c>.</exception>
    /// <remarks>
    /// <para>The backward direction can be used to locate a box in the original coordinates that would be dewarped to the specified image.</para>
    /// <para>If there is no model for <paramref name="pageno"/>, the model of its reference page is used. This works with both stripped and full resolution models.</para>
    /// </remarks>
    // leptonica: dewarpaApplyDisparityBoxa (dewarp3.c)
    public int ApplyDisparityBoxa(int pageno, Pix pixs, Boxa boxas, int mapdir,
                                  int x, int y, out Boxa pboxad, string? debugfile)
    {
        const string func = "dewarpaApplyDisparityBoxa";
        if (boxas == null)
            throw Lept.Arg(func, "boxas not defined");
        pboxad = boxas.Copy(L.L_CLONE);

        if (ApplyInit(pageno, pixs, x, y, out Dewarp? dew, debugfile) != 0 || dew == null) {
            Log.Error(func, "no model available");
            return 1;
        }

        Boxa? boxav = Dewarp.ApplyDisparityToBoxa(dew, boxas, L.L_VERT, mapdir);
        if (boxav == null) {
            dew.Minimize();
            Log.Error(func, "boxa1 not made");
            return 1;
        }
        pboxad = boxav;

        Pix? pixv = null;
        if (debugfile != null && mapdir != 1)
            Log.Info(func, "Reverse map direction; no debug output");
        bool debugOut = debugfile != null && mapdir == 1;
        if (debugOut) {
            Utils.LeptRmdir("lept/dewboxa");
            Utils.LeptMkdir("lept/dewboxa");
            Pix pix1 = pixs.ConvertTo32();
            pix1.RenderBoxaArb(boxas, 2, 255, 0, 0);
            pix1.WriteDebug("/tmp/lept/dewboxa/01.png", L.IFF_PNG);
            pixv = Dewarp.ApplyVertDisparity(dew, pixs, 255);
            pix1 = pixv!.ConvertTo32();
            pix1.RenderBoxaArb(boxav, 2, 0, 255, 0);
            pix1.WriteDebug("/tmp/lept/dewboxa/02.png", L.IFF_PNG);
        }

        if (useboth != 0 && dew.hsuccess != 0 && dew.skip_horiz == 0) {
            if (dew.hvalid == 0) {
                Log.Info(func, $"invalid horiz model for page {pageno}");
            } else {
                Boxa? boxah = Dewarp.ApplyDisparityToBoxa(dew, boxav, L.L_HORIZ, mapdir);
                if (boxah == null) {
                    Log.Error(func, $"horiz disparity fails on page {pageno}");
                } else {
                    pboxad = boxah;
                    if (debugOut) {
                        Pix? pixh = Dewarp.ApplyHorizDisparity(dew, pixv!, 255);
                        Pix pix1 = pixh!.ConvertTo32();
                        pix1.RenderBoxaArb(boxah, 2, 0, 0, 255);
                        pix1.WriteDebug("/tmp/lept/dewboxa/03.png", L.IFF_PNG);
                    }
                }
            }
        }

        if (debugOut) {
            Dewarp? dew1 = GetDewarp(pageno);
            dew1?.Debug("lept/dewapply", 0);
            PdfIo.ConvertFilesToPdf("/tmp/lept/dewboxa", null, 135, 1.0f, 0, 0,
                                    "Dewarp Apply Disparity Boxa", debugfile!);
            Console.Error.WriteLine($"Dewarp Apply Disparity Boxa pdf file: {debugfile}");
        }

        dew.Minimize();
        return 0;
    }
}

public sealed partial class Dewarp
{
    private const float DefaultSlopeFactor = 0.1f;

    // leptonica: pixApplyVertDisparity (dewarp3.c)
    internal static Pix? ApplyVertDisparity(Dewarp dew, Pix pixs, int grayin)
    {
        const string func = "pixApplyVertDisparity";
        if (dew == null || pixs == null)
            return null;
        pixs.GetDimensions(out int w, out int h, out int d);
        if (d != 1 && d != 8 && d != 32) {
            Log.Error(func, "pix not 1, 8 or 32 bpp");
            return null;
        }
        FPix? fpix = dew.fullvdispar;
        if (fpix == null) {
            Log.Error(func, "fullvdispar not defined");
            return null;
        }
        fpix.GetDimensions(out int fw, out int fh);
        if (fw < w || fh < h) {
            Console.Error.WriteLine($"fw = {fw}, w = {w}, fh = {fh}, h = {h}");
            Log.Error(func, "invalid fpix size");
            return null;
        }

        if (pixs.Colormap == null &&
            LeptGpu.TryGetActive((long)w * h, 12, out IGpuAccelerator? gpuVert) &&
            gpuVert.TryDewarpApplyDisparity(pixs, fpix, null, grayin, out Pix? gpuPixVert)) {
            return gpuPixVert;
        }

        Pix pixd = grayin < 0 && d != 1 ? Pix.CreateTemplateNoInit(pixs) : Pix.CreateTemplate(pixs);
        if (grayin >= 0)
            pixd.SetAllGray(grayin);
        uint[] datas = pixs.Data;
        uint[] datad = pixd.Data;
        float[] dataf = fpix.Data;
        int wpls = pixs.Wpl;
        int wpld = pixd.Wpl;
        int wplf = fpix.Wpl;

        if (d == 1) {
            LeptParallel.ForRows(h, w, (yStart, yEnd) => {
                for (int i = yStart; i < yEnd; i++) {
                    Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                    int fRow = i * wplf;
                    for (int j = 0; j < w; j++) {
                        int isrc = (int)(i - dataf[fRow + j] + 0.5);
                        if (grayin < 0)
                            isrc = Math.Clamp(isrc, 0, h - 1);
                        if ((uint)isrc < (uint)h) {
                            ReadOnlySpan<uint> lines = datas.AsSpan(isrc * wpls, wpls);
                            if (ArrayAccess.GetDataBit(lines, j) != 0)
                                ArrayAccess.SetDataBit(lined, j);
                        }
                    }
                }
            });
        } else if (d == 8) {
            LeptParallel.ForRows(h, w, (yStart, yEnd) => {
                ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
                ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
                ref float datafRef = ref MemoryMarshal.GetArrayDataReference(dataf);
                for (int i = yStart; i < yEnd; i++) {
                    ref uint dstLineRef = ref Unsafe.Add(ref datadRef, i * wpld);
                    int fRow = i * wplf;
                    if (grayin < 0 && (w & 3) != 0)
                        Unsafe.Add(ref dstLineRef, w >> 2) = 0;
                    for (int j = 0; j < w; j++) {
                        int isrc = (int)(i - Unsafe.Add(ref datafRef, fRow + j) + 0.5);
                        if (grayin < 0)
                            isrc = Math.Clamp(isrc, 0, h - 1);
                        if ((uint)isrc < (uint)h) {
                            int val8 = ArrayAccess.GetDataByteUnchecked(ref Unsafe.Add(ref datasRef, isrc * wpls), j);
                            ArrayAccess.SetDataByteUnchecked(ref dstLineRef, j, val8);
                        }
                    }
                }
            });
        } else {
            LeptParallel.ForRows(h, w, (yStart, yEnd) => {
                ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
                ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
                ref float datafRef = ref MemoryMarshal.GetArrayDataReference(dataf);
                for (int i = yStart; i < yEnd; i++) {
                    int dRow = i * wpld;
                    int fRow = i * wplf;
                    for (int j = 0; j < w; j++) {
                        int isrc = (int)(i - Unsafe.Add(ref datafRef, fRow + j) + 0.5);
                        if (grayin < 0)
                            isrc = Math.Clamp(isrc, 0, h - 1);
                        if ((uint)isrc < (uint)h)
                            Unsafe.Add(ref datadRef, dRow + j) = Unsafe.Add(ref datasRef, isrc * wpls + j);
                    }
                }
            });
        }

        return pixd;
    }

    internal static Pix? ApplyVertAndHorizDisparity(Dewarp dew, Pix pixs, int grayin)
    {
        pixs.GetDimensions(out int w, out int h, out int d);
        if (d != 1 && d != 8 && d != 32)
            return null;
        FPix? fpixv = dew.fullvdispar;
        FPix? fpixh = dew.fullhdispar;
        if (fpixv == null || fpixh == null)
            return null;
        fpixv.GetDimensions(out int vfw, out int vfh);
        fpixh.GetDimensions(out int hfw, out int hfh);
        if (vfw < w || vfh < h || hfw < w || hfh < h)
            return null;

        if (pixs.Colormap == null &&
            LeptGpu.TryGetActive((long)w * h, 20, out IGpuAccelerator? gpu) &&
            gpu.TryDewarpApplyDisparity(pixs, fpixv, fpixh, grayin, out Pix? gpuPix)) {
            return gpuPix;
        }

        Pix pixd = grayin < 0 && d != 1 ? Pix.CreateTemplateNoInit(pixs) : Pix.CreateTemplate(pixs);
        if (grayin >= 0)
            pixd.SetAllGray(grayin);
        uint[] datas = pixs.Data;
        uint[] datad = pixd.Data;
        float[] datav = fpixv.Data;
        float[] datah = fpixh.Data;
        int wpls = pixs.Wpl;
        int wpld = pixd.Wpl;
        int wplv = fpixv.Wpl;
        int wplh = fpixh.Wpl;

        if (d == 1) {
            bool whiteFill1 = grayin > 0 && grayin >= 128; // NOTE: SetAllGray on 1bpp sets 0 (white) when grayval >= 128, 1 (black) when < 128
            // Wait: if pixv had SetAllGray(grayin), an out-of-bounds isrc in pixv had bit=1 when 0 <= grayin < 128!
            // Preserve exact two-stage bit semantics for out-of-bounds isrc when 0 <= grayin < 128:
            int oobBit = (grayin >= 0 && grayin < 128) ? 1 : 0;
            _ = whiteFill1;
            LeptParallel.ForRows(h, w, (yStart, yEnd) => {
                for (int i = yStart; i < yEnd; i++) {
                    Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                    int vRow = i * wplv;
                    int hRow = i * wplh;
                    for (int j = 0; j < w; j++) {
                        int jsrc = (int)(j - datah[hRow + j] + 0.5);
                        if (grayin < 0)
                            jsrc = Math.Clamp(jsrc, 0, w - 1);
                        if ((uint)jsrc < (uint)w) {
                            int isrc = (int)(i - datav[vRow + jsrc] + 0.5);
                            if (grayin < 0)
                                isrc = Math.Clamp(isrc, 0, h - 1);
                            int bit = (uint)isrc < (uint)h
                                ? ArrayAccess.GetDataBit(datas.AsSpan(isrc * wpls, wpls), jsrc)
                                : oobBit;
                            if (oobBit == 0) {
                                if (bit != 0)
                                    ArrayAccess.SetDataBit(lined, j);
                            } else {
                                // Note: ApplyHorizDisparity only ever calls SetDataBit (ORing 1, never clearing 1!),
                                // and when 0 <= grayin < 128 pixd was already initialized to all 1s by SetAllGray!
                                if (bit != 0)
                                    ArrayAccess.SetDataBit(lined, j);
                            }
                        }
                    }
                }
            });
        } else if (d == 8) {
            int fill8 = Math.Clamp(grayin, 0, 255);
            LeptParallel.ForRows(h, w, (yStart, yEnd) => {
                ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
                ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
                ref float datavRef = ref MemoryMarshal.GetArrayDataReference(datav);
                ref float datahRef = ref MemoryMarshal.GetArrayDataReference(datah);
                for (int i = yStart; i < yEnd; i++) {
                    ref uint dstLineRef = ref Unsafe.Add(ref datadRef, i * wpld);
                    int vRow = i * wplv;
                    int hRow = i * wplh;
                    if (grayin < 0 && (w & 3) != 0)
                        Unsafe.Add(ref dstLineRef, w >> 2) = 0;
                    for (int j = 0; j < w; j++) {
                        int jsrc = (int)(j - Unsafe.Add(ref datahRef, hRow + j) + 0.5);
                        if (grayin < 0)
                            jsrc = Math.Clamp(jsrc, 0, w - 1);
                        if ((uint)jsrc < (uint)w) {
                            int isrc = (int)(i - Unsafe.Add(ref datavRef, vRow + jsrc) + 0.5);
                            if (grayin < 0)
                                isrc = Math.Clamp(isrc, 0, h - 1);
                            if ((uint)isrc < (uint)h) {
                                int val8 = ArrayAccess.GetDataByteUnchecked(ref Unsafe.Add(ref datasRef, isrc * wpls), jsrc);
                                ArrayAccess.SetDataByteUnchecked(ref dstLineRef, j, val8);
                            } else {
                                ArrayAccess.SetDataByteUnchecked(ref dstLineRef, j, fill8);
                            }
                        }
                    }
                }
            });
        } else {
            uint fill32 = grayin >= 0
                ? (uint)((Math.Clamp(grayin, 0, 255) * 0x01010100))
                : 0u;
            LeptParallel.ForRows(h, w, (yStart, yEnd) => {
                ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
                ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
                ref float datavRef = ref MemoryMarshal.GetArrayDataReference(datav);
                ref float datahRef = ref MemoryMarshal.GetArrayDataReference(datah);
                for (int i = yStart; i < yEnd; i++) {
                    int dRow = i * wpld;
                    int vRow = i * wplv;
                    int hRow = i * wplh;
                    for (int j = 0; j < w; j++) {
                        int jsrc = (int)(j - Unsafe.Add(ref datahRef, hRow + j) + 0.5);
                        if (grayin < 0)
                            jsrc = Math.Clamp(jsrc, 0, w - 1);
                        if ((uint)jsrc < (uint)w) {
                            int isrc = (int)(i - Unsafe.Add(ref datavRef, vRow + jsrc) + 0.5);
                            if (grayin < 0)
                                isrc = Math.Clamp(isrc, 0, h - 1);
                            Unsafe.Add(ref datadRef, dRow + j) = (uint)isrc < (uint)h
                                ? Unsafe.Add(ref datasRef, isrc * wpls + jsrc)
                                : fill32;
                        }
                    }
                }
            });
        }

        return pixd;
    }

    // leptonica: pixApplyHorizDisparity (dewarp3.c)
    internal static Pix? ApplyHorizDisparity(Dewarp dew, Pix pixs, int grayin)
    {
        const string func = "pixApplyHorizDisparity";
        if (dew == null)
            return pixs;
        if (pixs == null)
            return null;
        pixs.GetDimensions(out int w, out int h, out int d);
        if (d != 1 && d != 8 && d != 32) {
            Log.Error(func, "pix not 1, 8 or 32 bpp");
            return null;
        }
        FPix? fpix = dew.fullhdispar;
        if (fpix == null) {
            Log.Error(func, "fullhdispar not defined");
            return null;
        }
        fpix.GetDimensions(out int fw, out int fh);
        if (fw < w || fh < h) {
            Console.Error.WriteLine($"fw = {fw}, w = {w}, fh = {fh}, h = {h}");
            Log.Error(func, "invalid fpix size");
            return null;
        }

        Pix pixd = grayin < 0 && d != 1 ? Pix.CreateTemplateNoInit(pixs) : Pix.CreateTemplate(pixs);
        if (grayin >= 0)
            pixd.SetAllGray(grayin);
        uint[] datas = pixs.Data;
        uint[] datad = pixd.Data;
        float[] dataf = fpix.Data;
        int wpls = pixs.Wpl;
        int wpld = pixd.Wpl;
        int wplf = fpix.Wpl;

        if (d == 1) {
            LeptParallel.ForRows(h, w, (yStart, yEnd) => {
                for (int i = yStart; i < yEnd; i++) {
                    ReadOnlySpan<uint> lines = datas.AsSpan(i * wpls, wpls);
                    Span<uint> lined = datad.AsSpan(i * wpld, wpld);
                    int fRow = i * wplf;
                    for (int j = 0; j < w; j++) {
                        int jsrc = (int)(j - dataf[fRow + j] + 0.5);
                        if (grayin < 0)
                            jsrc = Math.Clamp(jsrc, 0, w - 1);
                        if ((uint)jsrc < (uint)w) {
                            if (ArrayAccess.GetDataBit(lines, jsrc) != 0)
                                ArrayAccess.SetDataBit(lined, j);
                        }
                    }
                }
            });
        } else if (d == 8) {
            LeptParallel.ForRows(h, w, (yStart, yEnd) => {
                ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
                ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
                ref float datafRef = ref MemoryMarshal.GetArrayDataReference(dataf);
                for (int i = yStart; i < yEnd; i++) {
                    ref uint srcLineRef = ref Unsafe.Add(ref datasRef, i * wpls);
                    ref uint dstLineRef = ref Unsafe.Add(ref datadRef, i * wpld);
                    int fRow = i * wplf;
                    if (grayin < 0 && (w & 3) != 0)
                        Unsafe.Add(ref dstLineRef, w >> 2) = 0;
                    for (int j = 0; j < w; j++) {
                        int jsrc = (int)(j - Unsafe.Add(ref datafRef, fRow + j) + 0.5);
                        if (grayin < 0)
                            jsrc = Math.Clamp(jsrc, 0, w - 1);
                        if ((uint)jsrc < (uint)w) {
                            int val8 = ArrayAccess.GetDataByteUnchecked(ref srcLineRef, jsrc);
                            ArrayAccess.SetDataByteUnchecked(ref dstLineRef, j, val8);
                        }
                    }
                }
            });
        } else {
            LeptParallel.ForRows(h, w, (yStart, yEnd) => {
                ref uint datasRef = ref MemoryMarshal.GetArrayDataReference(datas);
                ref uint datadRef = ref MemoryMarshal.GetArrayDataReference(datad);
                ref float datafRef = ref MemoryMarshal.GetArrayDataReference(dataf);
                for (int i = yStart; i < yEnd; i++) {
                    int sRow = i * wpls;
                    int dRow = i * wpld;
                    int fRow = i * wplf;
                    for (int j = 0; j < w; j++) {
                        int jsrc = (int)(j - Unsafe.Add(ref datafRef, fRow + j) + 0.5);
                        if (grayin < 0)
                            jsrc = Math.Clamp(jsrc, 0, w - 1);
                        if ((uint)jsrc < (uint)w)
                            Unsafe.Add(ref datadRef, dRow + j) = Unsafe.Add(ref datasRef, sRow + jsrc);
                    }
                }
            });
        }

        return pixd;
    }

    // leptonica: boxaApplyDisparity (dewarp3.c)
    internal static Boxa? ApplyDisparityToBoxa(Dewarp dew, Boxa boxa, int direction, int mapdir)
    {
        const string func = "boxaApplyDisparity";
        if (dew == null || boxa == null)
            return null;
        FPix? fpix;
        if (direction == L.L_VERT) {
            fpix = dew.fullvdispar;
        } else if (direction == L.L_HORIZ) {
            fpix = dew.fullhdispar;
        } else {
            Log.Error(func, "invalid direction");
            return null;
        }
        if (fpix == null) {
            Log.Error(func, "full disparity not defined");
            return null;
        }
        fpix.GetDimensions(out int fw, out int fh);

        float[] data = fpix.Data;
        int wpl = fpix.Wpl;
        int nbox = boxa.Count;
        Boxa boxad = Boxa.Create(nbox);
        for (int ib = 0; ib < nbox; ib++) {
            Box boxs = boxa.GetBox(ib, L.L_COPY);
            Pta ptas = boxs.ConvertToPta(4);
            Pta ptad = Pta.Create(4);
            for (int ip = 0; ip < 4; ip++) {
                ptas.GetIPt(ip, out int x, out int y);
                int cx = Math.Min(Math.Max(x, 0), fw - 1);
                int cy = Math.Min(Math.Max(y, 0), fh - 1);
                float disp = data[cy * wpl + cx];
                if (direction == L.L_VERT) {
                    float yn = mapdir == 0 ? y - disp : y + disp;
                    yn = Math.Max(0.0f, yn);
                    ptad.AddPt(x, yn);
                } else {
                    float xn = mapdir == 0 ? x - disp : x + disp;
                    xn = Math.Max(0.0f, xn);
                    ptad.AddPt(xn, y);
                }
            }
            Box boxd = ptad.ConvertToBox();
            boxad.AddBox(boxd, L.L_INSERT);
        }

        return boxad;
    }

    /*----------------------------------------------------------------------*
     *          Stripping out data and populating full res disparity        *
     *----------------------------------------------------------------------*/
    /// <summary>Releases all the data that is not needed for serialization.</summary>
    /// <returns>0 if successful; 1 on error.</returns>
    /// <remarks>The subsampled disparity arrays are kept, so the full resolution arrays can be reconstructed.</remarks>
    // leptonica: dewarpMinimize (dewarp3.c)
    public int Minimize()
    {
        const string func = "dewarpMinimize";
        Dewarp? dewt = hasref != 0 ? dewa?.GetDewarp(refpage) : this;
        if (dewt == null) {
            Log.Error(func, "dewt not found");
            return 1;
        }

        dewt.pixs = null;
        dewt.fullvdispar = null;
        dewt.fullhdispar = null;
        dewt.namidys = null;
        dewt.nacurves = null;
        return 0;
    }

    /// <summary>Builds the full resolution disparity arrays from the subsampled ones, if they do not exist.</summary>
    /// <param name="pix">An optional image that gives the size of the actual image; if <c>null</c>, the size of the original image is used and <paramref name="x"/> and <paramref name="y"/> are ignored.</param>
    /// <param name="x">The x origin of the arrays without extension, relative to <paramref name="pix"/>; this is the amount of slope extension on the left.</param>
    /// <param name="y">The y origin of the arrays without extension, relative to <paramref name="pix"/>; this is the amount of slope extension at the top.</param>
    /// <returns>0 if successful; 1 on error.</returns>
    /// <remarks>
    /// If <paramref name="pix"/> is given, the full resolution arrays must be large enough to accommodate it. The extension on the
    /// right and bottom is determined by the size of <paramref name="pix"/> and by <paramref name="x"/> and <paramref name="y"/>, and
    /// should never be negative. If the arrays exist but are too small, they are discarded and rebuilt.
    /// </remarks>
    // leptonica: dewarpPopulateFullRes (dewarp3.c)
    public int PopulateFullRes(Pix? pix, int x, int y)
    {
        const string func = "dewarpPopulateFullRes";
        if (sampvdispar == null) {
            Log.Error(func, "no sampled vert disparity");
            return 1;
        }
        if (x < 0) x = 0;
        if (y < 0) y = 0;

        int width, height;
        if (pix != null) {
            pix.GetDimensions(out width, out height, out _);
        } else {
            width = w;
            height = h;
        }

        if (fullvdispar != null) {
            fullvdispar.GetDimensions(out int fw, out _);
            // NOTE(upstream): compares height > fw instead of height > fh.
            if (width > fw || height > fw)
                fullvdispar = null;
        }
        if (fullhdispar != null) {
            fullhdispar.GetDimensions(out int fw, out _);
            // NOTE(upstream): compares height > fw instead of height > fh.
            if (width > fw || height > fw)
                fullhdispar = null;
        }

        int deltaw = width - sampling * (nx - 1) + 2;
        int deltah = height - sampling * (ny - 1) + 2;
        int redf = redfactor;
        deltaw = redf * Math.Max(0, deltaw);
        deltah = redf * Math.Max(0, deltah);

        if (fullvdispar == null) {
            FPix fpixt1 = sampvdispar.Copy();
            if (redf == 2)
                fpixt1.AddMultConstant(0.0f, (float)redf);
            FPix fpixt2 = fpixt1.ScaleByInteger(sampling * redf);
            if (deltah == 0 && deltaw == 0)
                fullvdispar = fpixt2;
            else
                fullvdispar = fpixt2.AddSlopeBorder(x, deltaw - x, y, deltah - y);
        }

        if (fullhdispar == null && samphdispar != null && skip_horiz == 0) {
            FPix fpixt1 = samphdispar.Copy();
            if (redf == 2)
                fpixt1.AddMultConstant(0.0f, (float)redf);
            FPix fpixt2 = fpixt1.ScaleByInteger(sampling * redf);
            if (deltah == 0 && deltaw == 0)
                fullhdispar = fpixt2;
            else
                fullhdispar = fpixt2.AddSlopeBorder(x, deltaw - x, y, deltah - y);
        }

        return 0;
    }

    // leptonica: fpixSampledDisparity (dewarp3.c)
    internal static FPix? SampledDisparity(FPix fpixs, int sampling)
    {
        if (fpixs == null || sampling < 1)
            return null;

        fpixs.GetDimensions(out int w, out int h);
        int wd = 1 + (w + sampling - 2) / sampling;
        int hd = 1 + (h + sampling - 2) / sampling;
        if (wd < 3 || hd < 3)
            return null;

        FPix fpixd = FPix.Create(wd, hd);
        for (int i = 0; i < hd; i++) {
            int isrc = sampling * i;
            if (isrc >= h) continue;
            for (int j = 0; j < wd; j++) {
                int jsrc = sampling * j;
                if (jsrc >= w) continue;
                fpixs.GetPixel(jsrc, isrc, out float val);
                fpixd.SetPixel(j, i, val);
            }
        }
        return fpixd;
    }

    // leptonica: fpixExtraHorizDisparity (dewarp3.c)
    internal static FPix? ExtraHorizDisparity(FPix fpixv, float factor, out int pxwid)
    {
        pxwid = 0;
        if (fpixv == null)
            return null;
        if (factor == 0.0f)
            factor = DefaultSlopeFactor;

        fpixv.GetDimensions(out int w, out int h);
        Numa nadiff = Numa.Create(w);
        float vdisp0 = 0.0f;
        for (int j = 0; j < w; j++) {
            fpixv.GetPixel(j, 0, out float val1);
            fpixv.GetPixel(j, h - 1, out float val2);
            float vdisp = factor * (val2 - val1) / (float)h;
            if (j == 0) vdisp0 = vdisp;
            vdisp = vdisp0 - vdisp;
            nadiff.AddNumber(vdisp);
        }
        nadiff.GetMax(out float maxval, out int maxloc);
        pxwid = (int)(maxval + 0.5);

        int fw = w + pxwid;
        FPix fpixh = FPix.Create(fw, h);
        float[] data = fpixh.Data;
        int wpl = fpixh.Wpl;
        float[] fadiff = nadiff.GetFArray(L.L_NOCOPY);
        for (int i = 0; i < h; i++) {
            int row = i * wpl;
            for (int j = 0; j < fw; j++)
                data[row + j] = j < maxloc ? fadiff[j] : maxval;
        }
        return fpixh;
    }
}
