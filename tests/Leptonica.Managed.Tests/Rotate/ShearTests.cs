using System;
using Leptonica;
using Leptonica.Managed.Tests.Rop;
using Xunit;

namespace Leptonica.Managed.Tests.Rotate;

/// <summary>
/// Compares the sampled shears against a naive per-pixel shear.  The naive version
/// finds the shift of each row (column) from the band boundaries, and then copies
/// pixel by pixel; it does not use rasterop.
/// </summary>
public class ShearTests
{
    private static readonly int[] Depths = { 1, 8, 32 };
    private static readonly (int, int)[] Sizes = { (1, 1), (7, 5), (33, 20), (64, 47), (70, 91) };
    private static readonly float[] Angles =
        { 0.005f, 0.02f, 0.05f, 0.1f, 0.3f, -0.005f, -0.02f, -0.1f, -0.3f };

    private static Pix RandomPix(Random rng, int w, int h, int d)
    {
        Pix pix = Pix.Create(w, h, d);
        uint mask = d == 32 ? 0xffffffffu : (1u << d) - 1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                NaiveRop.SetPixel(pix, x, y, (uint)rng.NextInt64(0, 1L << 32) & mask);
        return pix;
    }

    private static void AssertSame(Pix expected, Pix actual, string what)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Depth, actual.Depth);
        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++)
                Assert.True(NaiveRop.GetPixel(expected, x, y) == NaiveRop.GetPixel(actual, x, y),
                    $"{what}: mismatch at ({x},{y})");
    }

    private static uint FillValue(Pix pixs, int incolor)
    {
        Pix t = Pix.CreateTemplate(pixs);
        t.SetBlackOrWhite(incolor);
        return NaiveRop.GetPixel(t, 0, 0);
    }

    /// <summary>Shift of the band that contains offset d (from the shear line).</summary>
    private static int BandShift(int d, float angle)
    {
        float tanangle = (float)Math.Tan(angle);
        float inv = (float)Math.Abs(1.0 / tanangle);
        int init = (int)(inv / 2.0);
        if (d >= -init && d < init)
            return 0;
        int k;
        if (d >= init) {
            k = 1;
            while ((int)(inv * (k + 0.5) + 0.5) <= d) k++;
        } else {
            k = -1;
            while (d < (int)(inv * (k - 0.5) + 0.5)) k--;
        }
        return k;
    }

    private static Pix NaiveHShear(Pix pixs, int yloc, float angle, int incolor)
    {
        int sign = angle < 0 ? -1 : 1;
        uint fill = FillValue(pixs, incolor);
        Pix pixd = Pix.CreateTemplate(pixs);
        for (int y = 0; y < pixs.Height; y++) {
            int k = BandShift(y - yloc, angle);
            for (int x = 0; x < pixs.Width; x++) {
                int sx = x + sign * k;
                uint v = (sx >= 0 && sx < pixs.Width) ? NaiveRop.GetPixel(pixs, sx, y) : fill;
                NaiveRop.SetPixel(pixd, x, y, v);
            }
        }
        return pixd;
    }

    private static Pix NaiveVShear(Pix pixs, int xloc, float angle, int incolor)
    {
        int sign = angle < 0 ? -1 : 1;
        uint fill = FillValue(pixs, incolor);
        Pix pixd = Pix.CreateTemplate(pixs);
        for (int x = 0; x < pixs.Width; x++) {
            int k = BandShift(x - xloc, angle);
            for (int y = 0; y < pixs.Height; y++) {
                int sy = y - sign * k;
                uint v = (sy >= 0 && sy < pixs.Height) ? NaiveRop.GetPixel(pixs, x, sy) : fill;
                NaiveRop.SetPixel(pixd, x, y, v);
            }
        }
        return pixd;
    }

    [Fact]
    public void ZeroAngleIsIdentity()
    {
        var rng = new Random(1);
        foreach (int d in Depths) {
            Pix pixs = RandomPix(rng, 33, 20, d);
            AssertSame(pixs, Pix.HShear(null, pixs, 10, 0f, L.L_BRING_IN_WHITE), "HShear");
            AssertSame(pixs, Pix.VShear(null, pixs, 10, 0f, L.L_BRING_IN_BLACK), "VShear");
            AssertSame(pixs, Pix.HShearCenter(null, pixs, 0f, L.L_BRING_IN_WHITE), "HShearCenter");
            AssertSame(pixs, Pix.VShearCorner(null, pixs, 0f, L.L_BRING_IN_WHITE), "VShearCorner");
            AssertSame(pixs, pixs.RotateShearCenter(0f, L.L_BRING_IN_WHITE)!, "RotateShearCenter");
            AssertSame(pixs, pixs.Rotate2Shear(5, 5, 0f, L.L_BRING_IN_WHITE)!, "Rotate2Shear");
            AssertSame(pixs, pixs.Rotate3Shear(5, 5, 0f, L.L_BRING_IN_WHITE)!, "Rotate3Shear");

            Pix pixi = Pix.Copy(null, pixs);
            pixi.HShearIP(7, 0f, L.L_BRING_IN_WHITE);
            pixi.VShearIP(7, 0f, L.L_BRING_IN_WHITE);
            AssertSame(pixs, pixi, "IP");
            if (d >= 8) {
                AssertSame(pixs, pixs.HShearLI(5, 0f, L.L_BRING_IN_WHITE), "HShearLI");
                AssertSame(pixs, pixs.VShearLI(5, 0f, L.L_BRING_IN_WHITE), "VShearLI");
            }
        }
    }

    [Fact]
    public void HShearMatchesNaive()
    {
        var rng = new Random(2);
        foreach (int d in Depths)
        foreach ((int w, int h) in Sizes) {
            Pix pixs = RandomPix(rng, w, h, d);
            foreach (float angle in Angles)
            foreach (int incolor in new[] { L.L_BRING_IN_WHITE, L.L_BRING_IN_BLACK }) {
                foreach (int yloc in new[] { 0, h / 2, h - 1 }) {
                    Pix expected = NaiveHShear(pixs, yloc, angle, incolor);
                    string what = $"HShear d={d} {w}x{h} yloc={yloc} angle={angle}";
                    AssertSame(expected, Pix.HShear(null, pixs, yloc, angle, incolor), what);

                    Pix inplace = Pix.Copy(null, pixs);
                    Pix.HShear(inplace, inplace, yloc, angle, incolor);
                    AssertSame(expected, inplace, what + " (in place)");

                    Pix dest = Pix.Create(3, 3, d);
                    Pix.HShear(dest, pixs, yloc, angle, incolor);
                    AssertSame(expected, dest, what + " (pixd)");
                }
            }
        }
    }

    [Fact]
    public void VShearMatchesNaive()
    {
        var rng = new Random(3);
        foreach (int d in Depths)
        foreach ((int w, int h) in Sizes) {
            Pix pixs = RandomPix(rng, w, h, d);
            foreach (float angle in Angles)
            foreach (int incolor in new[] { L.L_BRING_IN_WHITE, L.L_BRING_IN_BLACK }) {
                foreach (int xloc in new[] { 0, w / 2, w - 1 }) {
                    Pix expected = NaiveVShear(pixs, xloc, angle, incolor);
                    string what = $"VShear d={d} {w}x{h} xloc={xloc} angle={angle}";
                    AssertSame(expected, Pix.VShear(null, pixs, xloc, angle, incolor), what);

                    Pix inplace = Pix.Copy(null, pixs);
                    Pix.VShear(inplace, inplace, xloc, angle, incolor);
                    AssertSame(expected, inplace, what + " (in place)");
                }
            }
        }
    }

    [Fact]
    public void RotationsMatchComposedNaiveShears()
    {
        var rng = new Random(4);
        foreach (int d in Depths) {
            Pix pixs = RandomPix(rng, 61, 43, d);
            int xcen = 30, ycen = 21;
            foreach (float angle in new[] { 0.02f, -0.04f, 0.1f, -0.25f }) {
                int incolor = L.L_BRING_IN_WHITE;
                Pix e2 = NaiveVShear(NaiveHShear(pixs, ycen, angle, incolor), xcen, angle, incolor);
                AssertSame(e2, pixs.Rotate2Shear(xcen, ycen, angle, incolor)!, $"Rotate2Shear d={d} {angle}");

                float hangle = (float)Math.Atan(Math.Sin(angle));
                Pix e3 = NaiveVShear(pixs, xcen, angle / 2.0f, incolor);
                e3 = NaiveHShear(e3, ycen, hangle, incolor);
                e3 = NaiveVShear(e3, xcen, angle / 2.0f, incolor);
                AssertSame(e3, pixs.Rotate3Shear(xcen, ycen, angle, incolor)!, $"Rotate3Shear d={d} {angle}");

                Pix? r = pixs.RotateShear(xcen, ycen, angle, incolor);
                AssertSame(Math.Abs(angle) <= 0.06f ? e2 : e3, r!, $"RotateShear d={d} {angle}");

                Pix ip = Pix.Copy(null, pixs);
                ip.RotateShearIP(xcen, ycen, angle, incolor);
                Pix eip = NaiveHShear(pixs, ycen, angle / 2.0f, incolor);
                eip = NaiveVShear(eip, xcen, hangle, incolor);
                eip = NaiveHShear(eip, ycen, angle / 2.0f, incolor);
                AssertSame(eip, ip, $"RotateShearIP d={d} {angle}");
            }
        }
    }

    [Fact]
    public void RotateShearTooLargeAngleReturnsNull()
    {
        Pix pixs = Pix.Create(10, 10, 8);
        Assert.Null(pixs.RotateShear(5, 5, 0.6f, L.L_BRING_IN_WHITE));
    }

    [Fact]
    public void LinearInterpolatedShearKeepsShearLine()
    {
        var rng = new Random(5);
        foreach (int d in new[] { 8, 32 }) {
                // composeRGBPixel() clears the alpha byte; the last row/column copies the whole word
            uint keep = d == 32 ? 0xffffff00u : 0xffu;
            Pix pixs = RandomPix(rng, 40, 30, d);
            Pix h = pixs.HShearLI(12, 0.1f, L.L_BRING_IN_WHITE);
            for (int x = 0; x < 40; x++)
                Assert.Equal(NaiveRop.GetPixel(pixs, x, 12) & keep, NaiveRop.GetPixel(h, x, 12) & keep);
            Pix v = pixs.VShearLI(17, -0.1f, L.L_BRING_IN_BLACK);
            for (int y = 0; y < 30; y++)
                Assert.Equal(NaiveRop.GetPixel(pixs, 17, y) & keep, NaiveRop.GetPixel(v, 17, y) & keep);
        }
    }
}
