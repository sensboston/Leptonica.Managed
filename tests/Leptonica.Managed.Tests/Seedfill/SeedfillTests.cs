using System;
using System.Collections.Generic;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Seedfill;

/// <summary>
/// Pixel-by-pixel references for seedfill.c: breadth-first flood fill for the binary fill,
/// fixed-point iteration for the grayscale reconstruction, and the exact L1 / Linf distance
/// for the distance function.  They share no code with the word-oriented implementation.
/// </summary>
internal static class NaiveSeedfill
{
    public static uint Get(Pix pix, int x, int y)
    {
        pix.GetPixel(x, y, out uint val);
        return val;
    }

    public static Pix RandomBinary(Random rng, int w, int h, double density)
    {
        Pix pix = Pix.Create(w, h, 1);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (rng.NextDouble() < density)
                    pix.SetPixel(x, y, 1);
        return pix;
    }

    public static Pix RandomGray(Random rng, int w, int h)
    {
        Pix pix = Pix.Create(w, h, 8);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pix.SetPixel(x, y, (uint)rng.Next(256));
        return pix;
    }

    private static readonly (int dx, int dy)[] Nbrs4 = { (0, -1), (-1, 0), (1, 0), (0, 1) };
    private static readonly (int dx, int dy)[] Nbrs8 =
        { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1) };

    public static (int dx, int dy)[] Neighbors(int connectivity) =>
        connectivity == 4 ? Nbrs4 : Nbrs8;

    /// <summary>
    /// Breadth-first flood fill: all pixels of the mask that are connected within the mask
    /// to a pixel that is ON in both the seed and the mask.  Seed pixels outside the mask
    /// are dropped, as in the C code.
    /// </summary>
    public static Pix FloodFill(Pix seed, Pix mask, int connectivity)
    {
        int w = seed.Width, h = seed.Height;
        Pix result = Pix.Create(w, h, 1);
        var queue = new Queue<(int x, int y)>();
        for (int y = 0; y < h; y++) {
            for (int x = 0; x < w; x++) {
                if (Get(seed, x, y) == 1 && Get(mask, x, y) == 1) {
                    result.SetPixel(x, y, 1);
                    queue.Enqueue((x, y));
                }
            }
        }
        while (queue.Count > 0) {
            var (x, y) = queue.Dequeue();
            foreach (var (dx, dy) in Neighbors(connectivity)) {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                    continue;
                if (Get(mask, nx, ny) == 1 && Get(result, nx, ny) == 0) {
                    result.SetPixel(nx, ny, 1);
                    queue.Enqueue((nx, ny));
                }
            }
        }
        return result;
    }

    public static Pix Not(Pix pix)
    {
        Pix result = Pix.Create(pix.Width, pix.Height, 1);
        for (int y = 0; y < pix.Height; y++)
            for (int x = 0; x < pix.Width; x++)
                result.SetPixel(x, y, 1 - Get(pix, x, y));
        return result;
    }

    /// <summary>The 1 pixel wide ring of ON pixels along the border of a w x h image.</summary>
    public static Pix BorderRing(int w, int h)
    {
        Pix ring = Pix.Create(w, h, 1);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (x == 0 || y == 0 || x == w - 1 || y == h - 1)
                    ring.SetPixel(x, y, 1);
        return ring;
    }

    /// <summary>
    /// Grayscale reconstruction by fixed-point iteration.  With inverse == false:
    /// J(p) = min(I(p), max(J(p), J(neighbors))) for I(p) &gt; 0 (needs seed &lt;= mask).
    /// With inverse == true: J(p) = max(J(p), J(neighbors)) if that is &gt; I(p), for I(p) &lt; 255.
    /// </summary>
    public static Pix GrayReconstruct(Pix seed, Pix mask, int connectivity, bool inverse)
    {
        int w = seed.Width, h = seed.Height;
        int[,] j = new int[w, h];
        int[,] m = new int[w, h];
        for (int y = 0; y < h; y++) {
            for (int x = 0; x < w; x++) {
                j[x, y] = (int)Get(seed, x, y);
                m[x, y] = (int)Get(mask, x, y);
            }
        }
        bool changed = true;
        while (changed) {
            changed = false;
            int[,] next = (int[,])j.Clone();
            for (int y = 0; y < h; y++) {
                for (int x = 0; x < w; x++) {
                    int maxval = j[x, y];
                    foreach (var (dx, dy) in Neighbors(connectivity)) {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && ny >= 0 && nx < w && ny < h)
                            maxval = Math.Max(maxval, j[nx, ny]);
                    }
                    int v = j[x, y];
                    if (!inverse && m[x, y] > 0)
                        v = Math.Min(m[x, y], maxval);
                    if (inverse && m[x, y] < 255 && maxval > m[x, y])
                        v = maxval;
                    if (v != j[x, y]) {
                        next[x, y] = v;
                        changed = true;
                    }
                }
            }
            j = next;
        }
        Pix result = Pix.Create(w, h, 8);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                result.SetPixel(x, y, (uint)j[x, y]);
        return result;
    }

    /// <summary>
    /// Exact distance to the nearest bg pixel (L1 for 4-connected, Linf for 8-connected).
    /// With bgOutside, the pixels outside the image are bg too (L_BOUNDARY_BG); otherwise
    /// only bg pixels inside the 1-pixel boundary count (L_BOUNDARY_FG, whose boundary pixels
    /// are not sources).
    /// </summary>
    public static int Distance(Pix pixs, int x, int y, int connectivity, bool bgOutside)
    {
        int w = pixs.Width, h = pixs.Height;
        int best = int.MaxValue;
        for (int sy = 0; sy < h; sy++) {
            for (int sx = 0; sx < w; sx++) {
                bool interior = sx > 0 && sy > 0 && sx < w - 1 && sy < h - 1;
                if (Get(pixs, sx, sy) != 0 || (!bgOutside && !interior))
                    continue;
                int dx = Math.Abs(x - sx), dy = Math.Abs(y - sy);
                best = Math.Min(best, connectivity == 4 ? dx + dy : Math.Max(dx, dy));
            }
        }
        if (bgOutside)
            best = Math.Min(best, Math.Min(Math.Min(x + 1, y + 1), Math.Min(w - x, h - y)));
        return best;
    }

    public static void AssertEqual(Pix expected, Pix actual, string what)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Depth, actual.Depth);
        for (int y = 0; y < expected.Height; y++) {
            for (int x = 0; x < expected.Width; x++) {
                uint e = Get(expected, x, y), a = Get(actual, x, y);
                if (e != a)
                    Assert.Fail($"{what}: pixel ({x},{y}) expected {e}, got {a} "
                                + $"(size {expected.Width}x{expected.Height})");
            }
        }
    }
}

public class SeedfillTests
{
    private static Pix Make(int w, int h, int d, params int[] values)
    {
        Pix pix = Pix.Create(w, h, d);
        Assert.Equal(w * h, values.Length);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pix.SetPixel(x, y, (uint)values[y * w + x]);
        return pix;
    }

    private static int[] Row(Pix pix, int y)
    {
        var row = new int[pix.Width];
        for (int x = 0; x < pix.Width; x++)
            row[x] = (int)NaiveSeedfill.Get(pix, x, y);
        return row;
    }

    /*----------------------------- Binary seedfill ---------------------------*/

    public static IEnumerable<object[]> BinaryCases()
    {
        var rng = new Random(20260101);
        for (int n = 0; n < 120; n++) {
            int w = n < 8 ? n + 1 : rng.Next(1, 100);
            int h = n < 8 ? 9 - n : rng.Next(1, 40);
            double maskDensity = 0.35 + 0.4 * rng.NextDouble();
            double seedDensity = 0.002 + 0.03 * rng.NextDouble();
            yield return new object[] { n, w, h, maskDensity, seedDensity };
        }
    }

    [Theory]
    [MemberData(nameof(BinaryCases))]
    public void SeedfillBinary_MatchesFloodFill(int n, int w, int h, double maskDensity, double seedDensity)
    {
        var rng = new Random(1000 + n);
        Pix mask = NaiveSeedfill.RandomBinary(rng, w, h, maskDensity);
        Pix seed = NaiveSeedfill.RandomBinary(rng, w, h, seedDensity);
        foreach (int conn in new[] { 4, 8 }) {
            Pix expected = NaiveSeedfill.FloodFill(seed, mask, conn);

            Pix pixs = Pix.Copy(null, seed);
            Pix result = Pix.SeedfillBinary(null, pixs, mask, conn);
            NaiveSeedfill.AssertEqual(expected, result, $"new pixd, conn {conn}");
            NaiveSeedfill.AssertEqual(seed, pixs, "pixs must not change");

            Pix inplace = Pix.Copy(null, seed);
            Pix result2 = Pix.SeedfillBinary(inplace, inplace, mask, conn);
            Assert.Same(inplace, result2);
            NaiveSeedfill.AssertEqual(expected, inplace, $"in place, conn {conn}");

            Pix pixd = Pix.Create(w, h, 1);
            Pix result3 = Pix.SeedfillBinary(pixd, seed, mask, conn);
            Assert.Same(pixd, result3);
            NaiveSeedfill.AssertEqual(expected, pixd, $"existing pixd, conn {conn}");
        }
    }

    [Fact]
    public void SeedfillBinary_HandChecked_4VersusOctoConnectivity()
    {
        /* Mask: two blobs touching only diagonally.  The seed is in the left blob. */
        Pix mask = Make(5, 3, 1,
                        1, 1, 0, 0, 0,
                        1, 1, 0, 1, 1,
                        0, 0, 1, 1, 1);
        Pix seed = Make(5, 3, 1,
                        1, 0, 0, 0, 0,
                        0, 0, 0, 0, 0,
                        0, 0, 0, 0, 0);
        Pix r4 = Pix.SeedfillBinary(null, seed, mask, 4);
        Assert.Equal(new[] { 1, 1, 0, 0, 0 }, Row(r4, 0));
        Assert.Equal(new[] { 1, 1, 0, 0, 0 }, Row(r4, 1));
        Assert.Equal(new[] { 0, 0, 0, 0, 0 }, Row(r4, 2));

        Pix r8 = Pix.SeedfillBinary(null, seed, mask, 8);
        Assert.Equal(new[] { 1, 1, 0, 0, 0 }, Row(r8, 0));
        Assert.Equal(new[] { 1, 1, 0, 1, 1 }, Row(r8, 1));
        Assert.Equal(new[] { 0, 0, 1, 1, 1 }, Row(r8, 2));
    }

    [Fact]
    public void SeedfillBinary_SeedOutsideMaskIsDropped()
    {
        Pix mask = Make(4, 1, 1, 1, 1, 0, 0);
        Pix seed = Make(4, 1, 1, 0, 0, 0, 1);
        Pix r = Pix.SeedfillBinary(null, seed, mask, 4);
        Assert.Equal(new[] { 0, 0, 0, 0 }, Row(r, 0));
    }

    [Fact]
    public void SeedfillBinary_Serpentine_ConvergesWithinIterationLimit()
    {
        /* A serpentine path needs about one raster/anti-raster cycle per turn */
        int w = 40, h = 9;
        Pix mask = Pix.Create(w, h, 1);
        for (int y = 0; y < h; y += 2)
            for (int x = 0; x < w; x++)
                mask.SetPixel(x, y, 1);
        for (int y = 1; y < h; y += 2)
            mask.SetPixel((y / 2) % 2 == 0 ? w - 1 : 0, y, 1);
        Pix seed = Pix.Create(w, h, 1);
        seed.SetPixel(0, 0, 1);
        Pix expected = NaiveSeedfill.FloodFill(seed, mask, 4);
        NaiveSeedfill.AssertEqual(expected, Pix.SeedfillBinary(null, seed, mask, 4), "serpentine");
    }

    [Fact]
    public void SeedfillBinary_RejectsBadArguments()
    {
        Pix p1 = Pix.Create(4, 4, 1), p8 = Pix.Create(4, 4, 8);
        Assert.Throws<ArgumentException>(() => Pix.SeedfillBinary(null, p8, p1, 4));
        Assert.Throws<ArgumentException>(() => Pix.SeedfillBinary(null, p1, p8, 4));
        Assert.Throws<ArgumentException>(() => Pix.SeedfillBinary(null, p1, p1, 6));
    }

    [Fact]
    public void SeedfillBinaryLow_ClipsToTheSmallerImage()
    {
        /* The seed is 2 rows taller than the mask: the extra rows are not touched */
        Pix seed = Make(3, 4, 1,
                        1, 0, 0,
                        0, 0, 0,
                        0, 0, 0,
                        0, 0, 1);
        Pix mask = Make(3, 2, 1,
                        1, 1, 1,
                        0, 0, 0);
        Pix r = Pix.SeedfillBinary(null, seed, mask, 4);
        Assert.Equal(new[] { 1, 1, 1 }, Row(r, 0));
        Assert.Equal(new[] { 0, 0, 0 }, Row(r, 1));
        Assert.Equal(new[] { 0, 0, 0 }, Row(r, 2));
        Assert.Equal(new[] { 0, 0, 1 }, Row(r, 3));
    }

    [Fact]
    public void SeedfillBinaryRestricted_LimitsTheFillDistance()
    {
        Pix mask = Pix.Create(20, 3, 1);
        for (int x = 0; x < 20; x++)
            mask.SetPixel(x, 1, 1);
        Pix seed = Pix.Create(20, 3, 1);
        seed.SetPixel(2, 1, 1);

        Pix unrestricted = Pix.SeedfillBinary(null, seed, mask, 4);
        Assert.Equal(20, unrestricted.CountPixels(null));

        /* xmax = 5: the seed pixel plus 5 pixels on each side */
        Pix r = Pix.SeedfillBinaryRestricted(null, seed, mask, 4, 5, 0);
        int[] row = Row(r, 1);
        for (int x = 0; x < 20; x++)
            Assert.Equal(x <= 7 ? 1 : 0, row[x]);

        /* No fill allowed: a clone of the seed */
        Pix none = Pix.SeedfillBinaryRestricted(null, seed, mask, 4, 0, 0);
        NaiveSeedfill.AssertEqual(seed, none, "xmax = ymax = 0");
    }

    /*------------------- Applications of the binary seedfill -----------------*/

    public static IEnumerable<object[]> ApplicationCases()
    {
        var rng = new Random(77);
        for (int n = 0; n < 60; n++)
            yield return new object[] { n, rng.Next(1, 70), rng.Next(1, 35), 0.25 + 0.5 * rng.NextDouble() };
    }

    [Theory]
    [MemberData(nameof(ApplicationCases))]
    public void BorderApplications_MatchNaive(int n, int w, int h, double density)
    {
        var rng = new Random(500 + n);
        Pix pixs = NaiveSeedfill.RandomBinary(rng, w, h, density);
        Pix saved = Pix.Copy(null, pixs);
        Pix inverse = NaiveSeedfill.Not(pixs);
        Pix ring = NaiveSeedfill.BorderRing(w, h);
        foreach (int conn in new[] { 4, 8 }) {
            /* components touching the border */
            Pix touching = NaiveSeedfill.FloodFill(ring, pixs, conn);
            NaiveSeedfill.AssertEqual(touching, pixs.ExtractBorderConnComps(conn), $"extract {conn}");

            Pix rest = Pix.Create(w, h, 1);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    rest.SetPixel(x, y, NaiveSeedfill.Get(pixs, x, y) & (1 - NaiveSeedfill.Get(touching, x, y)));
            NaiveSeedfill.AssertEqual(rest, pixs.RemoveBorderConnComps(conn), $"remove {conn}");

            /* bg components touching the border are filled to fg; pixs is restored */
            Pix bgTouching = NaiveSeedfill.FloodFill(ring, inverse, conn);
            Pix filled = Pix.Create(w, h, 1);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    filled.SetPixel(x, y, NaiveSeedfill.Get(pixs, x, y) | NaiveSeedfill.Get(bgTouching, x, y));
            NaiveSeedfill.AssertEqual(filled, pixs.FillBgFromBorder(conn), $"fillbg {conn}");
            NaiveSeedfill.AssertEqual(saved, pixs, "FillBgFromBorder must restore pixs");

            /* holes: the bg pixels not connected to the border through bg */
            Pix holes = Pix.Create(w, h, 1);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    holes.SetPixel(x, y, NaiveSeedfill.Get(inverse, x, y) & (1 - NaiveSeedfill.Get(bgTouching, x, y)));
            NaiveSeedfill.AssertEqual(holes, pixs.HolesByFilling(conn), $"holes {conn}");

            /* closed borders: seeds are the ring pixels that are bg in pixs */
            Pix ringSeed = Pix.Create(w, h, 1);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    ringSeed.SetPixel(x, y, NaiveSeedfill.Get(ring, x, y) & NaiveSeedfill.Get(inverse, x, y));
            Pix outside = NaiveSeedfill.FloodFill(ringSeed, inverse, conn);
            NaiveSeedfill.AssertEqual(NaiveSeedfill.Not(outside), pixs.FillClosedBorders(conn), $"closed {conn}");
        }
    }

    [Fact]
    public void HolesByFilling_HandChecked_4VersusOctoConnectivity()
    {
        /* A ring of fg with a bg pixel inside, plus a bg gap in the ring at a diagonal */
        Pix pix = Make(5, 5, 1,
                       0, 0, 0, 0, 0,
                       0, 1, 1, 1, 0,
                       0, 1, 0, 1, 0,
                       0, 1, 1, 0, 0,
                       0, 0, 0, 0, 0);
        /* The gap at (3,3) touches the outside only diagonally to (4,4): */
        Pix h4 = pix.HolesByFilling(4);   /* 4-connected filling: (3,3) is reached from the outside */
        Assert.Equal(new[] { 0, 0, 0, 0, 0 }, Row(h4, 0));
        Assert.Equal(new[] { 0, 0, 0, 0, 0 }, Row(h4, 1));
        Assert.Equal(new[] { 0, 0, 1, 0, 0 }, Row(h4, 2));
        Assert.Equal(new[] { 0, 0, 0, 0, 0 }, Row(h4, 3));

        /* Closing the gap makes (2,2) a hole for both connectivities */
        pix.SetPixel(3, 3, 1);
        Assert.Equal(1, (int)NaiveSeedfill.Get(pix.HolesByFilling(4), 2, 2));
        Assert.Equal(1, (int)NaiveSeedfill.Get(pix.HolesByFilling(8), 2, 2));
    }

    [Fact]
    public void RemoveSeededComponents_RemovesOnlySeededComponents()
    {
        Pix mask = Make(7, 3, 1,
                        1, 1, 0, 1, 0, 1, 1,
                        1, 0, 0, 1, 0, 0, 1,
                        1, 0, 0, 1, 0, 0, 0);
        Pix seed = Make(7, 3, 1,
                        0, 0, 0, 0, 0, 0, 0,
                        0, 0, 0, 1, 0, 0, 0,
                        0, 0, 0, 0, 0, 0, 0);
        Pix r = Pix.RemoveSeededComponents(null, seed, mask, 8, 0);
        Assert.Equal(new[] { 1, 1, 0, 0, 0, 1, 1 }, Row(r, 0));
        Assert.Equal(new[] { 1, 0, 0, 0, 0, 0, 1 }, Row(r, 1));
        Assert.Equal(new[] { 1, 0, 0, 0, 0, 0, 0 }, Row(r, 2));
        Assert.Equal(1, (int)NaiveSeedfill.Get(seed, 3, 1));   /* pixs is not altered */

        /* in place, with a cleared border of 1 */
        Pix inplace = Pix.Copy(null, mask);
        Pix r2 = Pix.RemoveSeededComponents(inplace, seed, inplace, 8, 1);
        Assert.Same(inplace, r2);
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0, 0 }, Row(inplace, 0));
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0, 0 }, Row(inplace, 1));
        Assert.Throws<ArgumentException>(() => Pix.RemoveSeededComponents(Pix.Create(7, 3, 1), seed, mask, 8, 0));
    }

    /*----------------------------- Gray seedfill -----------------------------*/

    public static IEnumerable<object[]> GrayCases()
    {
        var rng = new Random(4242);
        for (int n = 0; n < 80; n++)
            yield return new object[] { n, rng.Next(1, 45), rng.Next(1, 30) };
    }

    private static Pix GraySeedBelowMask(Random rng, Pix mask)
    {
        Pix seed = Pix.Create(mask.Width, mask.Height, 8);
        for (int y = 0; y < mask.Height; y++) {
            for (int x = 0; x < mask.Width; x++) {
                uint m = NaiveSeedfill.Get(mask, x, y);
                if (rng.Next(12) == 0)
                    seed.SetPixel(x, y, (uint)rng.Next((int)m + 1));
            }
        }
        return seed;
    }

    private static Pix SmoothMask(Random rng, int w, int h)
    {
        /* Mostly smooth surface with occasional zeros, so that the fill has structure */
        Pix mask = Pix.Create(w, h, 8);
        int[] phase = { rng.Next(20), rng.Next(20) };
        for (int y = 0; y < h; y++) {
            for (int x = 0; x < w; x++) {
                int v = 128 + (int)(100 * Math.Sin((x + phase[0]) / 3.0) * Math.Cos((y + phase[1]) / 4.0))
                        + rng.Next(-20, 21);
                if (rng.Next(25) == 0)
                    v = 0;
                mask.SetPixel(x, y, (uint)Math.Clamp(v, 0, 255));
            }
        }
        return mask;
    }

    [Theory]
    [MemberData(nameof(GrayCases))]
    public void SeedfillGray_AllVariantsMatchReconstruction(int n, int w, int h)
    {
        var rng = new Random(9000 + n);
        Pix mask = n % 2 == 0 ? NaiveSeedfill.RandomGray(rng, w, h) : SmoothMask(rng, w, h);
        Pix seed = GraySeedBelowMask(rng, mask);
        foreach (int conn in new[] { 4, 8 }) {
            Pix expected = NaiveSeedfill.GrayReconstruct(seed, mask, conn, inverse: false);

            Pix hybrid = Pix.Copy(null, seed);
            hybrid.SeedfillGray(mask, conn);
            NaiveSeedfill.AssertEqual(expected, hybrid, $"hybrid, conn {conn}");

            Pix simple = Pix.Copy(null, seed);
            simple.SeedfillGraySimple(mask, conn);
            NaiveSeedfill.AssertEqual(expected, simple, $"simple, conn {conn}");
        }
    }

    [Fact]
    public void SeedfillGray_LargeFillMatchesSimpleReference()
    {
        const int w = 113, h = 79;
        var rng = new Random(771903);
        Pix mask = SmoothMask(rng, w, h);
        Pix seed = GraySeedBelowMask(rng, mask);

        foreach (int conn in new[] { 4, 8 }) {
            Pix expected = Pix.Copy(null, seed);
            expected.SeedfillGraySimple(mask, conn);
            Pix actual = Pix.Copy(null, seed);
            actual.SeedfillGray(mask, conn);
            NaiveSeedfill.AssertEqual(expected, actual, $"large queue-growth reference, conn {conn}");
        }
    }

    [Theory]
    [MemberData(nameof(GrayCases))]
    public void SeedfillGrayInv_AllVariantsMatchReconstruction(int n, int w, int h)
    {
        var rng = new Random(19000 + n);
        Pix mask = n % 2 == 0 ? NaiveSeedfill.RandomGray(rng, w, h) : SmoothMask(rng, w, h);
        Pix seed = NaiveSeedfill.RandomGray(rng, w, h);
        /* mostly low seed values with a few high ones, so that basins get filled */
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (rng.Next(10) != 0)
                    seed.SetPixel(x, y, 0);
        foreach (int conn in new[] { 4, 8 }) {
            Pix expected = NaiveSeedfill.GrayReconstruct(seed, mask, conn, inverse: true);

            Pix hybrid = Pix.Copy(null, seed);
            hybrid.SeedfillGrayInv(mask, conn);
            NaiveSeedfill.AssertEqual(expected, hybrid, $"hybrid inv, conn {conn}");

            Pix simple = Pix.Copy(null, seed);
            simple.SeedfillGrayInvSimple(mask, conn);
            NaiveSeedfill.AssertEqual(expected, simple, $"simple inv, conn {conn}");
        }
    }

    [Fact]
    public void SeedfillGray_HandChecked_Sombrero()
    {
        /* Mask is a ridge; the seed has one point at height 150 below the max (200).
         * The fill is clipped by the mask and cannot get past the rim (30) at a higher level. */
        Pix mask = Make(7, 1, 8, 30, 90, 200, 90, 30, 90, 200);
        Pix seed = Make(7, 1, 8, 0, 0, 150, 0, 0, 0, 0);
        Pix pix = Pix.Copy(null, seed);
        pix.SeedfillGray(mask, 4);
        Assert.Equal(new[] { 30, 90, 150, 90, 30, 30, 30 }, Row(pix, 0));
    }

    [Fact]
    public void SeedfillGray_RejectsBadArguments()
    {
        Pix p8 = Pix.Create(4, 4, 8), p1 = Pix.Create(4, 4, 1), other = Pix.Create(5, 4, 8);
        Assert.Throws<ArgumentException>(() => p1.SeedfillGray(p8, 4));
        Assert.Throws<ArgumentException>(() => p8.SeedfillGray(p1, 4));
        Assert.Throws<ArgumentException>(() => p8.SeedfillGray(p8, 5));
        Assert.Throws<ArgumentException>(() => p8.SeedfillGray(other, 4));
        Assert.Throws<ArgumentException>(() => p8.SeedfillGrayInv(other, 4));
        Assert.Throws<ArgumentException>(() => p8.SeedfillGraySimple(other, 4));
        Assert.Throws<ArgumentException>(() => p8.SeedfillGrayInvSimple(other, 4));
    }

    [Fact]
    public void SeedfillGrayBasin_HandChecked()
    {
        /* Basin bottoms are raised by delta, up to the rim of the basin */
        Pix pixm = Make(5, 1, 8, 200, 100, 100, 200, 200);
        Pix pixb = Make(5, 1, 1, 0, 1, 0, 0, 0);
        Pix r = pixb.SeedfillGrayBasin(pixm, 10, 4);
        Assert.Equal(new[] { 200, 110, 110, 200, 200 }, Row(r, 0));

        Pix copy = pixb.SeedfillGrayBasin(pixm, 0, 4);   /* delta <= 0: a copy of pixm */
        NaiveSeedfill.AssertEqual(pixm, copy, "delta == 0");
        Assert.NotSame(pixm, copy);
    }

    /*---------------------------- Distance function --------------------------*/

    [Fact]
    public void DistanceFunction_HandChecked_AllForeground()
    {
        Pix pix = Pix.Create(5, 5, 1);
        pix.SetAll();
        Pix d = pix.DistanceFunction(4, 8, L.L_BOUNDARY_BG);
        Assert.Equal(new[] { 1, 1, 1, 1, 1 }, Row(d, 0));
        Assert.Equal(new[] { 1, 2, 2, 2, 1 }, Row(d, 1));
        Assert.Equal(new[] { 1, 2, 3, 2, 1 }, Row(d, 2));
        Assert.Equal(new[] { 1, 2, 2, 2, 1 }, Row(d, 3));
        Assert.Equal(new[] { 1, 1, 1, 1, 1 }, Row(d, 4));

        /* No bg anywhere: with free boundary conditions the distance floats up to the max */
        Pix d8 = pix.DistanceFunction(8, 8, L.L_BOUNDARY_FG);
        Pix d16 = pix.DistanceFunction(4, 16, L.L_BOUNDARY_FG);
        for (int y = 0; y < 5; y++) {
            Assert.Equal(new[] { 255, 255, 255, 255, 255 }, Row(d8, y));
            Assert.Equal(new[] { 65535, 65535, 65535, 65535, 65535 }, Row(d16, y));
        }
    }

    [Fact]
    public void DistanceFunction_HandChecked_4VersusOctoConnectivity()
    {
        /* One bg pixel in the middle of a 7x7 fg image, free boundaries */
        Pix pix = Pix.Create(7, 7, 1);
        pix.SetAll();
        pix.SetPixel(3, 3, 0);
        Pix d4 = pix.DistanceFunction(4, 8, L.L_BOUNDARY_FG);
        Pix d8 = pix.DistanceFunction(8, 8, L.L_BOUNDARY_FG);
        /* interior columns 1..5; L1 for 4-connected, Linf for 8-connected */
        Assert.Equal(new[] { 4, 3, 2, 3, 4 }, Row(d4, 1)[1..6]);
        Assert.Equal(new[] { 3, 2, 1, 2, 3 }, Row(d4, 2)[1..6]);
        Assert.Equal(new[] { 2, 1, 0, 1, 2 }, Row(d4, 3)[1..6]);
        Assert.Equal(new[] { 2, 2, 2, 2, 2 }, Row(d8, 1)[1..6]);
        Assert.Equal(new[] { 2, 1, 1, 1, 2 }, Row(d8, 2)[1..6]);
        Assert.Equal(new[] { 2, 1, 0, 1, 2 }, Row(d8, 3)[1..6]);
        /* the boundary pixels are set equal to the pixels next to them */
        Assert.Equal(Row(d4, 1)[1..6], Row(d4, 0)[1..6]);
        Assert.Equal(Row(d4, 5)[1..6], Row(d4, 6)[1..6]);
        Assert.Equal(4u, NaiveSeedfill.Get(d4, 0, 1) );
    }

    public static IEnumerable<object[]> DistanceCases()
    {
        var rng = new Random(31337);
        for (int n = 0; n < 60; n++)
            yield return new object[] { n, rng.Next(3, 40), rng.Next(3, 25), 0.05 + 0.9 * rng.NextDouble() };
    }

    [Theory]
    [MemberData(nameof(DistanceCases))]
    public void DistanceFunction_MatchesExactDistance(int n, int w, int h, double density)
    {
        var rng = new Random(6000 + n);
        Pix pixs = NaiveSeedfill.RandomBinary(rng, w, h, density);   /* ON = fg */
        foreach (int conn in new[] { 4, 8 }) {
            foreach (int depth in new[] { 8, 16 }) {
                /* bg outside the image: the whole image is compared */
                Pix d = pixs.DistanceFunction(conn, depth, L.L_BOUNDARY_BG);
                Assert.Equal(depth, d.Depth);
                for (int y = 0; y < h; y++) {
                    for (int x = 0; x < w; x++) {
                        int expected = NaiveSeedfill.Distance(pixs, x, y, conn, bgOutside: true);
                        if (NaiveSeedfill.Get(pixs, x, y) == 0)
                            expected = 0;
                        if (expected != (int)NaiveSeedfill.Get(d, x, y))
                            Assert.Fail($"BG conn {conn} depth {depth} ({x},{y}): expected {expected}, "
                                        + $"got {NaiveSeedfill.Get(d, x, y)} (size {w}x{h})");
                    }
                }

                /* free boundary: interior pixels have the exact distance to the interior bg,
                 * or the max value when there is none; the boundary copies the next pixel */
                Pix df = pixs.DistanceFunction(conn, depth, L.L_BOUNDARY_FG);
                uint max = depth == 8 ? 255u : 65535u;
                for (int y = 1; y < h - 1; y++) {
                    for (int x = 1; x < w - 1; x++) {
                        int e = NaiveSeedfill.Distance(pixs, x, y, conn, bgOutside: false);
                        uint expected = e == int.MaxValue ? max : (uint)Math.Min(e, (int)max);
                        if (NaiveSeedfill.Get(pixs, x, y) == 0)
                            expected = 0;
                        if (expected != NaiveSeedfill.Get(df, x, y))
                            Assert.Fail($"FG conn {conn} depth {depth} ({x},{y}): expected {expected}, "
                                        + $"got {NaiveSeedfill.Get(df, x, y)} (size {w}x{h})");
                    }
                }
                for (int x = 1; x < w - 1; x++) {
                    Assert.Equal(NaiveSeedfill.Get(df, x, 1), NaiveSeedfill.Get(df, x, 0));
                    Assert.Equal(NaiveSeedfill.Get(df, x, h - 2), NaiveSeedfill.Get(df, x, h - 1));
                }
                for (int y = 1; y < h - 1; y++) {
                    Assert.Equal(NaiveSeedfill.Get(df, 1, y), NaiveSeedfill.Get(df, 0, y));
                    Assert.Equal(NaiveSeedfill.Get(df, w - 2, y), NaiveSeedfill.Get(df, w - 1, y));
                }
            }
        }
    }

    [Fact]
    public void DistanceFunction_RejectsBadArguments()
    {
        Pix p1 = Pix.Create(5, 5, 1), p8 = Pix.Create(5, 5, 8);
        Assert.Throws<ArgumentException>(() => p8.DistanceFunction(4, 8, L.L_BOUNDARY_BG));
        Assert.Throws<ArgumentException>(() => p1.DistanceFunction(6, 8, L.L_BOUNDARY_BG));
        Assert.Throws<ArgumentException>(() => p1.DistanceFunction(4, 32, L.L_BOUNDARY_BG));
        Assert.Throws<ArgumentException>(() => p1.DistanceFunction(4, 8, 3));
    }

    /*------------------------------- Seed spread -----------------------------*/

    [Fact]
    public void Seedspread_SingleSeed8Connected_FillsEverything()
    {
        Pix pix = Pix.Create(9, 7, 8);
        pix.SetPixel(4, 3, 77);
        Pix r = pix.Seedspread(8);
        Assert.Equal(9, r.Width);
        Assert.Equal(7, r.Height);
        for (int y = 0; y < 7; y++)
            Assert.Equal(new[] { 77, 77, 77, 77, 77, 77, 77, 77, 77 }, Row(r, y));
    }

    [Fact]
    public void Seedspread_TwoSeeds_NearestSeedWins()
    {
        Pix pix = Pix.Create(21, 5, 8);
        pix.SetPixel(2, 2, 10);
        pix.SetPixel(18, 2, 20);
        foreach (int conn in new[] { 8 }) {
            Pix r = pix.Seedspread(conn);
            for (int y = 0; y < 5; y++) {
                for (int x = 0; x < 21; x++) {
                    if (x < 10)
                        Assert.Equal(10u, NaiveSeedfill.Get(r, x, y));
                    else if (x > 10)
                        Assert.Equal(20u, NaiveSeedfill.Get(r, x, y));
                }
            }
        }
    }

    [Fact]
    public void Seedspread_KeepsTheSeedsAndFillsEverything()
    {
        var rng = new Random(55);
        for (int n = 0; n < 30; n++) {
            int w = rng.Next(3, 30), h = rng.Next(3, 20);
            Pix pix = Pix.Create(w, h, 8);
            int nseeds = rng.Next(1, 6);
            for (int s = 0; s < nseeds; s++)
                pix.SetPixel(rng.Next(w), rng.Next(h), (uint)rng.Next(1, 256));
            foreach (int conn in new[] { 4, 8 }) {
                Pix r = pix.Seedspread(conn);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        if (NaiveSeedfill.Get(pix, x, y) != 0)
                            Assert.Equal(NaiveSeedfill.Get(pix, x, y), NaiveSeedfill.Get(r, x, y));
            }
        }
    }

    /*------------------------------ Local extrema ----------------------------*/

    [Fact]
    public void FindEqualValues_AlignedAtUpperLeftCorner()
    {
        Pix a = Make(4, 3, 8,
                     1, 2, 3, 4,
                     5, 6, 7, 8,
                     9, 9, 9, 9);
        Pix b = Make(3, 4, 8,
                     1, 0, 3,
                     5, 6, 0,
                     0, 9, 9,
                     7, 7, 7);
        Pix r = a.FindEqualValues(b);
        Assert.Equal(3, r.Width);
        Assert.Equal(3, r.Height);
        Assert.Equal(1, r.Depth);
        Assert.Equal(new[] { 1, 0, 1 }, Row(r, 0));
        Assert.Equal(new[] { 1, 1, 0 }, Row(r, 1));
        Assert.Equal(new[] { 0, 1, 1 }, Row(r, 2));
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 3, 1).FindEqualValues(b));
    }
}
