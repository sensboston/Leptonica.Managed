using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Leptonica;

/*
 *  rank.c: Rank filter (gray and rgb), median filter.
 *
 *      Rank filter (gray and rgb)
 *          Pix.RankFilter (pixRankFilter)
 *          Pix.RankFilterRGB (pixRankFilterRGB)
 *          Pix.RankFilterGray (pixRankFilterGray)
 *
 *      Median filter
 *          Pix.MedianFilter (pixMedianFilter)
 *
 *      Rank filter (accelerated with downscaling)
 *          Pix.RankFilterWithScaling (pixRankFilterWithScaling)
 */
public sealed partial class Pix
{
    /*----------------------------------------------------------------------*
     *                           Rank order filter                          *
     *----------------------------------------------------------------------*/
    // leptonica: pixRankFilter (rank.c)
    /// <summary>Rank order filter of an 8 or 32 bpp image.</summary>
    public Pix RankFilter(int wf, int hf, float rank)
    {
        const string func = "pixRankFilter";
        Pix pixs = this;
        if (pixs.Colormap != null)
            throw Lept.Arg(func, "pixs has colormap");
        int d = pixs.Depth;
        if (d != 8 && d != 32)
            throw Lept.Arg(func, "pixs not 8 or 32 bpp");
        if (wf < 1 || hf < 1)
            throw Lept.Arg(func, "wf < 1 || hf < 1");
        if (rank < 0.0 || rank > 1.0)
            throw Lept.Arg(func, "rank must be in [0.0, 1.0]");
        if (wf == 1 && hf == 1)   /* no-op */
            return Copy(null, pixs);

        if (d == 8)
            return pixs.RankFilterGray(wf, hf, rank);
        else  /* d == 32 */
            return pixs.RankFilterRGB(wf, hf, rank);
    }

    // leptonica: pixRankFilterRGB (rank.c)
    /// <summary>Rank order filter of a 32 bpp image, applied to each component.</summary>
    public Pix RankFilterRGB(int wf, int hf, float rank)
    {
        const string func = "pixRankFilterRGB";
        Pix pixs = this;
        if (pixs.Depth != 32)
            throw Lept.Arg(func, "pixs not 32 bpp");
        if (wf < 1 || hf < 1)
            throw Lept.Arg(func, "wf < 1 || hf < 1");
        if (rank < 0.0 || rank > 1.0)
            throw Lept.Arg(func, "rank must be in [0.0, 1.0]");
        if (wf == 1 && hf == 1)   /* no-op */
            return Copy(null, pixs);

        if (LeptGpu.TryGetActive((long)pixs.Width * pixs.Height, wf * hf * 6, out IGpuAccelerator? gpu) &&
            gpu.TryRankFilterRGB(pixs, wf, hf, rank, out Pix? gpuPix)) {
            return gpuPix;
        }

        pixs.SplitRGBFast(out Pix pixr, out Pix pixg, out Pix pixb);

        Pix pixrf = pixr.RankFilterGray(wf, hf, rank);
        Pix pixgf = pixg.RankFilterGray(wf, hf, rank);
        Pix pixbf = pixb.RankFilterGray(wf, hf, rank);

        return CombineRGBFast(pixrf, pixgf, pixbf);
    }

    // leptonica: pixRankFilterGray (rank.c)
    /// <summary>Rank order filter of an 8 bpp image.</summary>
    public Pix RankFilterGray(int wf, int hf, float rank)
    {
        const string func = "pixRankFilterGray";
        Pix pixs = this;
        if (pixs.Colormap != null)
            throw Lept.Arg(func, "pixs has colormap");
        pixs.GetDimensions(out int w, out int h, out int d);
        if (d != 8)
            throw Lept.Arg(func, "pixs not 8 bpp");
        if (wf < 1 || hf < 1)
            throw Lept.Arg(func, "wf < 1 || hf < 1");
        if (rank < 0.0 || rank > 1.0)
            throw Lept.Arg(func, "rank must be in [0.0, 1.0]");
        if (wf == 1 && hf == 1)   /* no-op */
            return Copy(null, pixs);

        if ((wf & 1) != 0 && (hf & 1) != 0) {
            if (rank == 0.0)
                return pixs.ErodeGray(wf, hf);
            else if (rank == 1.0)
                return pixs.DilateGray(wf, hf);
        }
        if (LeptGpu.TryGetActive((long)w * h, wf * hf * 2, out IGpuAccelerator? gpu) &&
            gpu.TryRankFilterGray(pixs, wf, hf, rank, out Pix? gpuPix)) {
            return gpuPix;
        }
        if (rank == 0.0) rank = 0.0001f;
        if (rank == 1.0) rank = 0.9999f;

        Pix pixt = pixs.AddMirroredBorder(wf / 2, wf / 2, hf / 2, hf / 2);
        int wt = pixt.Width;
        int ht = pixt.Height;
        uint[] datat = pixt.Data;
        int wplt = pixt.Wpl;

        /* Unpack mirrored-border source once into linear bytes for shift-free sliding updates. */
        using TempBuffer<byte> flatSrc = new TempBuffer<byte>(wt * ht);
        byte[] srcBytes = flatSrc.Array;
        {
            ref uint baseT = ref MemoryMarshal.GetArrayDataReference(datat);
            ref byte baseB = ref MemoryMarshal.GetArrayDataReference(srcBytes);
            for (int r = 0; r < ht; r++)
                UnpackMorphRow(ref Unsafe.Add(ref baseT, (nuint)(r * wplt)), wt, ref Unsafe.Add(ref baseB, (nuint)(r * wt)));
        }

        int rankloc = (int)(rank * wf * hf);
        Pix pixd = CreateTemplateNoInit(pixs);
        uint[] datad = pixd.Data;
        int wpld = pixd.Wpl;
        if ((w & 3) != 0) {
            int lastWord = w >> 2;
            for (int r = 0; r < h; r++)
                datad[r * wpld + lastWord] = 0;
        }

        if (hf > wf) {
            int wordCols = (w + 3) >> 2;
            LeptParallel.ForRows(wordCols, (long)h * (2 * wf + 24) * 4L, (jwStart, jwEnd) => {
                Span<int> histo = stackalloc int[256];
                Span<int> histo16 = stackalloc int[16];
                ref int hRef = ref MemoryMarshal.GetReference(histo);
                ref int h16Ref = ref MemoryMarshal.GetReference(histo16);
                ref byte srcBase = ref MemoryMarshal.GetArrayDataReference(srcBytes);
                ref uint dstBase = ref MemoryMarshal.GetArrayDataReference(datad);

                int jStart = jwStart << 2;
                int jEnd = Math.Min(w, jwEnd << 2);
                for (int j = jStart; j < jEnd; j++) {
                    histo.Clear();
                    histo16.Clear();

                    for (int k = 0; k < hf; k++) {
                        ref byte row = ref Unsafe.Add(ref srcBase, (nuint)(k * wt + j));
                        for (int m = 0; m < wf; m++) {
                            int val = Unsafe.Add(ref row, (nuint)m);
                            Unsafe.Add(ref hRef, (nuint)val)++;
                            Unsafe.Add(ref h16Ref, (nuint)(val >> 4))++;
                        }
                    }

                    ArrayAccess.SetDataByteUnchecked(ref dstBase, j, FindRankBin(ref hRef, ref h16Ref, rankloc, func));

                    for (int i = 1; i < h; i++) {
                        ref byte rowSub = ref Unsafe.Add(ref srcBase, (nuint)((i - 1) * wt + j));
                        ref byte rowAdd = ref Unsafe.Add(ref srcBase, (nuint)((i + hf - 1) * wt + j));
                        for (int m = 0; m < wf; m++) {
                            int vSub = Unsafe.Add(ref rowSub, (nuint)m);
                            Unsafe.Add(ref hRef, (nuint)vSub)--;
                            Unsafe.Add(ref h16Ref, (nuint)(vSub >> 4))--;
                            int vAdd = Unsafe.Add(ref rowAdd, (nuint)m);
                            Unsafe.Add(ref hRef, (nuint)vAdd)++;
                            Unsafe.Add(ref h16Ref, (nuint)(vAdd >> 4))++;
                        }
                        ArrayAccess.SetDataByteUnchecked(ref Unsafe.Add(ref dstBase, (nuint)(i * wpld)), j,
                            FindRankBin(ref hRef, ref h16Ref, rankloc, func));
                    }
                }
            });
        } else {  /* wf >= hf */
            LeptParallel.ForRows(h, (long)w * (2 * hf + 24), (yStart, yEnd) => {
                Span<int> histo = stackalloc int[256];
                Span<int> histo16 = stackalloc int[16];
                ref int hRef = ref MemoryMarshal.GetReference(histo);
                ref int h16Ref = ref MemoryMarshal.GetReference(histo16);
                ref byte srcBase = ref MemoryMarshal.GetArrayDataReference(srcBytes);
                ref uint dstBase = ref MemoryMarshal.GetArrayDataReference(datad);
                byte[] outRow = ArrayPool<byte>.Shared.Rent(w + 32);
                try {
                    ref byte outRef = ref MemoryMarshal.GetArrayDataReference(outRow);
                    for (int i = yStart; i < yEnd; i++) {
                        histo.Clear();
                        histo16.Clear();

                        for (int k = 0; k < hf; k++) {
                            ref byte row = ref Unsafe.Add(ref srcBase, (nuint)((i + k) * wt));
                            for (int m = 0; m < wf; m++) {
                                int val = Unsafe.Add(ref row, (nuint)m);
                                Unsafe.Add(ref hRef, (nuint)val)++;
                                Unsafe.Add(ref h16Ref, (nuint)(val >> 4))++;
                            }
                        }
                        outRef = (byte)FindRankBin(ref hRef, ref h16Ref, rankloc, func);

                        int rightOff = wf - 1;
                        for (int j = 1; j < w; j++) {
                            int leftIdx = j - 1;
                            int rightIdx = j + rightOff;
                            for (int k = 0; k < hf; k++) {
                                ref byte row = ref Unsafe.Add(ref srcBase, (nuint)((i + k) * wt));
                                int vSub = Unsafe.Add(ref row, (nuint)leftIdx);
                                Unsafe.Add(ref hRef, (nuint)vSub)--;
                                Unsafe.Add(ref h16Ref, (nuint)(vSub >> 4))--;
                                int vAdd = Unsafe.Add(ref row, (nuint)rightIdx);
                                Unsafe.Add(ref hRef, (nuint)vAdd)++;
                                Unsafe.Add(ref h16Ref, (nuint)(vAdd >> 4))++;
                            }
                            Unsafe.Add(ref outRef, (nuint)j) = (byte)FindRankBin(ref hRef, ref h16Ref, rankloc, func);
                        }

                        PackMorphRowSlice(ref outRef, ref Unsafe.Add(ref dstBase, (nuint)(i * wpld)), 0, w);
                    }
                } finally {
                    ArrayPool<byte>.Shared.Return(outRow);
                }
            });
        }

        return pixd;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindRankBin(ref int hRef, ref int h16Ref, int rankloc, string func)
    {
        int sum = 0;
        int n = 0;
        for (; n < 16; n++) {
            int c = Unsafe.Add(ref h16Ref, (nuint)n);
            if (sum + c > rankloc)
                break;
            sum += c;
        }
        if (n == 16) {
            Log.Warning(func, "n = 16; reducing");
            n = 15;
            sum -= Unsafe.Add(ref h16Ref, 15);
        }
        int k = n << 4;
        for (int m = 0; m < 16; m++, k++) {
            sum += Unsafe.Add(ref hRef, (nuint)k);
            if (sum > rankloc)
                return k;
        }
        return 0;
    }

    /*----------------------------------------------------------------------*
     *                             Median filter                            *
     *----------------------------------------------------------------------*/
    // leptonica: pixMedianFilter (rank.c)
    /// <summary>Median filter of an 8 or 32 bpp image.</summary>
    public Pix MedianFilter(int wf, int hf)
    {
        return RankFilter(wf, hf, 0.5f);
    }

    /*----------------------------------------------------------------------*
     *                Rank filter (accelerated with downscaling)            *
     *----------------------------------------------------------------------*/
    // leptonica: pixRankFilterWithScaling (rank.c)
    /// <summary>Rank order filter done on a downscaled image, then upscaled.</summary>
    public Pix RankFilterWithScaling(int wf, int hf, float rank, float scalefactor)
    {
        const string func = "pixRankFilterWithScaling";
        Pix pixs = this;
        if (pixs.Colormap != null)
            throw Lept.Arg(func, "pixs has colormap");
        int d = pixs.Depth;
        if (d != 8 && d != 32)
            throw Lept.Arg(func, "pixs not 8 or 32 bpp");
        if (wf < 1 || hf < 1)
            throw Lept.Arg(func, "wf < 1 || hf < 1");
        if (rank < 0.0 || rank > 1.0)
            throw Lept.Arg(func, "rank must be in [0.0, 1.0]");
        if (wf == 1 && hf == 1)   /* no-op */
            return Copy(null, pixs);
        if (scalefactor < 0.2 || scalefactor > 0.7) {
            Log.Error(func, "invalid scale factor; no scaling used");
            return pixs.RankFilter(wf, hf, rank);
        }

        Pix pix1 = pixs.ScaleAreaMap(scalefactor, scalefactor);
        int wfs = Math.Max(1, (int)(scalefactor * wf + 0.5));
        int hfs = Math.Max(1, (int)(scalefactor * hf + 0.5));
        Pix pix2 = pix1.RankFilter(wfs, hfs, rank);
        pixs.GetDimensions(out int w, out int h, out _);
        return pix2.ScaleToSize(w, h);
    }
}
