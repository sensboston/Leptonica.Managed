using System;
using System.Collections.Generic;
using Leptonica;
using Leptonica.Managed.Tests.Rop;

namespace Leptonica.Managed.Tests.Morph;

/// <summary>
/// Pixel-by-pixel reference for binary morphology.  It shares no code with the rasterop
/// based implementation: every output pixel is computed independently from the definition
/// (dilation = union of shifted copies, erosion = intersection of shifted copies), with the
/// pixels outside the image treated as OFF, or as ON for symmetric-boundary erosion.
/// </summary>
internal static class NaiveMorph
{
    public static bool InRange(Pix p, int x, int y) =>
        x >= 0 && y >= 0 && x < p.Width && y < p.Height;

    public static uint Get(Pix p, int x, int y) => NaiveRop.GetPixel(p, x, y);

    public static Pix RandomPix(Random rng, int w, int h, int percentOn)
    {
        Pix pix = Pix.Create(w, h, 1);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (rng.Next(100) < percentOn)
                    NaiveRop.SetPixel(pix, x, y, 1);
        return pix;
    }

    /// <summary>Random sel; hit / miss probabilities are in percent, the rest is don't care.</summary>
    public static Sel RandomSel(Random rng, int maxDim, int percentHit, int percentMiss)
    {
        int h = rng.Next(1, maxDim + 1);
        int w = rng.Next(1, maxDim + 1);
        Sel sel = Sel.Create(h, w);
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w; j++) {
                int r = rng.Next(100);
                int type = r < percentHit ? L.SEL_HIT
                         : r < percentHit + percentMiss ? L.SEL_MISS : L.SEL_DONT_CARE;
                sel.SetElement(i, j, type);
            }
        }
        sel.SetOrigin(rng.Next(0, h), rng.Next(0, w));
        return sel;
    }

    /// <summary>Brick sel of all hits with the origin at (vsize/2, hsize/2).</summary>
    public static Sel Box(int hsize, int vsize)
    {
        Sel sel = Sel.Create(vsize, hsize);
        for (int i = 0; i < vsize; i++)
            for (int j = 0; j < hsize; j++)
                sel.SetElement(i, j, L.SEL_HIT);
        sel.SetOrigin(vsize / 2, hsize / 2);
        return sel;
    }

    public static bool HasHitOrMiss(Sel sel)
    {
        foreach (int[] row in sel.Data)
            foreach (int v in row)
                if (v == L.SEL_HIT || v == L.SEL_MISS)
                    return true;
        return false;
    }

    /// <summary>Dilation: dest(x,y) = OR over hits (i,j) of src(x - (j-cx), y - (i-cy)).</summary>
    public static Pix Dilate(Pix src, Sel sel)
    {
        sel.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Pix dst = Pix.Create(src.Width, src.Height, 1);
        for (int y = 0; y < src.Height; y++) {
            for (int x = 0; x < src.Width; x++) {
                uint v = 0;
                for (int i = 0; i < sy && v == 0; i++) {
                    for (int j = 0; j < sx; j++) {
                        if (sel.Data[i][j] != L.SEL_HIT)
                            continue;
                        int xs = x - (j - cx), ys = y - (i - cy);
                        if (InRange(src, xs, ys) && Get(src, xs, ys) == 1) {
                            v = 1;
                            break;
                        }
                    }
                }
                NaiveRop.SetPixel(dst, x, y, v);
            }
        }
        return dst;
    }

    /// <summary>
    /// Erosion: dest(x,y) = AND over hits (i,j) of src(x + (j-cx), y + (i-cy)); pixels
    /// outside the image are ON for the symmetric and OFF for the asymmetric convention.
    /// </summary>
    public static Pix Erode(Pix src, Sel sel, bool symmetric)
    {
        sel.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Pix dst = Pix.Create(src.Width, src.Height, 1);
        for (int y = 0; y < src.Height; y++) {
            for (int x = 0; x < src.Width; x++) {
                uint v = 1;
                for (int i = 0; i < sy && v == 1; i++) {
                    for (int j = 0; j < sx; j++) {
                        if (sel.Data[i][j] != L.SEL_HIT)
                            continue;
                        int xs = x + (j - cx), ys = y + (i - cy);
                        bool on = InRange(src, xs, ys) ? Get(src, xs, ys) == 1 : symmetric;
                        if (!on) {
                            v = 0;
                            break;
                        }
                    }
                }
                NaiveRop.SetPixel(dst, x, y, v);
            }
        }
        return dst;
    }

    /// <summary>
    /// Hit-miss transform: every hit needs an ON pixel inside the image, every miss needs
    /// an OFF pixel or a position outside the image (this is independent of the boundary
    /// convention).  The sel must have at least one hit or miss.
    /// </summary>
    public static Pix Hmt(Pix src, Sel sel)
    {
        sel.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Pix dst = Pix.Create(src.Width, src.Height, 1);
        for (int y = 0; y < src.Height; y++) {
            for (int x = 0; x < src.Width; x++) {
                uint v = 1;
                for (int i = 0; i < sy && v == 1; i++) {
                    for (int j = 0; j < sx; j++) {
                        int t = sel.Data[i][j];
                        if (t != L.SEL_HIT && t != L.SEL_MISS)
                            continue;
                        int xs = x + (j - cx), ys = y + (i - cy);
                        bool on = InRange(src, xs, ys) && Get(src, xs, ys) == 1;
                        if ((t == L.SEL_HIT && !on) || (t == L.SEL_MISS && on)) {
                            v = 0;
                            break;
                        }
                    }
                }
                NaiveRop.SetPixel(dst, x, y, v);
            }
        }
        return dst;
    }

    public static Pix Open(Pix src, Sel sel, bool symmetric) =>
        Dilate(Erode(src, sel, symmetric), sel);

    public static Pix Close(Pix src, Sel sel, bool symmetric) =>
        Erode(Dilate(src, sel), sel, symmetric);

    public static Pix OpenGeneralized(Pix src, Sel sel) => Dilate(Hmt(src, sel), sel);

    public static Pix CloseGeneralized(Pix src, Sel sel) => Hmt(Dilate(src, sel), sel);

    /// <summary>Adds a border of OFF pixels on all four sides.</summary>
    public static Pix Pad(Pix src, int n)
    {
        Pix dst = Pix.Create(src.Width + 2 * n, src.Height + 2 * n, 1);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                NaiveRop.SetPixel(dst, x + n, y + n, Get(src, x, y));
        return dst;
    }

    public static Pix Crop(Pix src, int n)
    {
        Pix dst = Pix.Create(src.Width - 2 * n, src.Height - 2 * n, 1);
        for (int y = 0; y < dst.Height; y++)
            for (int x = 0; x < dst.Width; x++)
                NaiveRop.SetPixel(dst, x, y, Get(src, x + n, y + n));
        return dst;
    }

    /// <summary>A step of a sequence: 'D' = dilate, 'E' = erode.</summary>
    public readonly record struct Step(char Kind, Sel Sel);

    /// <summary>Applies the steps in order on src padded by <paramref name="pad"/> OFF pixels.</summary>
    public static Pix Sequence(Pix src, IEnumerable<Step> steps, bool symmetric, int pad)
    {
        Pix cur = pad > 0 ? Pad(src, pad) : src;
        foreach (Step s in steps)
            cur = s.Kind == 'D' ? Dilate(cur, s.Sel) : Erode(cur, s.Sel, symmetric);
        return pad > 0 ? Crop(cur, pad) : cur;
    }

    public static void AssertSame(Pix expected, Pix actual, string ctx)
    {
        Assert.True(expected.Width == actual.Width && expected.Height == actual.Height,
            $"{ctx}: size {actual.Width}x{actual.Height}, expected {expected.Width}x{expected.Height}");
        Assert.Equal(1, actual.Depth);
        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++)
                Assert.True(Get(expected, x, y) == Get(actual, x, y),
                    $"{ctx}: pixel ({x},{y}) is {Get(actual, x, y)}, expected {Get(expected, x, y)}");
    }

    /// <summary>True if every ON pixel of a is also ON in b.</summary>
    public static bool IsSubset(Pix a, Pix b)
    {
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
                if (Get(a, x, y) == 1 && Get(b, x, y) != 1)
                    return false;
        return true;
    }
}
