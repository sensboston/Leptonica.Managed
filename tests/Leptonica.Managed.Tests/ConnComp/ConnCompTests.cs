using System;
using System.Collections.Generic;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests;

/// <summary>
/// Tests for conncomp.c against a naive flood-fill labeling in this file.
/// The components must come out in raster order of their first (top-left) pixel.
/// </summary>
public class ConnCompTests
{
    private static Pix RandomPix(Random rng, int w, int h, double density)
    {
        Pix pix = Pix.Create(w, h, 1);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (rng.NextDouble() < density)
                    pix.SetPixel(x, y, 1);
        return pix;
    }

    private static bool[,] ToBits(Pix pix)
    {
        pix.GetDimensions(out int w, out int h, out _);
        var bits = new bool[w, h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) {
                pix.GetPixel(x, y, out uint v);
                bits[x, y] = v != 0;
            }
        return bits;
    }

    /// <summary>Naive labeling: scan in raster order, flood fill with an explicit queue.</summary>
    private static List<(int x, int y, int w, int h)> NaiveBoxes(bool[,] bits, int connectivity)
    {
        int w = bits.GetLength(0), h = bits.GetLength(1);
        var seen = new bool[w, h];
        var result = new List<(int, int, int, int)>();
        for (int y0 = 0; y0 < h; y0++) {
            for (int x0 = 0; x0 < w; x0++) {
                if (!bits[x0, y0] || seen[x0, y0])
                    continue;
                int minx = x0, maxx = x0, miny = y0, maxy = y0;
                var queue = new Queue<(int, int)>();
                queue.Enqueue((x0, y0));
                seen[x0, y0] = true;
                while (queue.Count > 0) {
                    var (cx, cy) = queue.Dequeue();
                    minx = Math.Min(minx, cx);
                    maxx = Math.Max(maxx, cx);
                    miny = Math.Min(miny, cy);
                    maxy = Math.Max(maxy, cy);
                    for (int dy = -1; dy <= 1; dy++) {
                        for (int dx = -1; dx <= 1; dx++) {
                            if (dx == 0 && dy == 0)
                                continue;
                            if (connectivity == 4 && dx != 0 && dy != 0)
                                continue;
                            int nx = cx + dx, ny = cy + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                                continue;
                            if (!bits[nx, ny] || seen[nx, ny])
                                continue;
                            seen[nx, ny] = true;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }
                result.Add((minx, miny, maxx - minx + 1, maxy - miny + 1));
            }
        }
        return result;
    }

    private static List<(int x, int y, int w, int h)> Geometry(Boxa boxa)
    {
        var list = new List<(int, int, int, int)>();
        for (int i = 0; i < boxa.Count; i++) {
            boxa.GetBoxGeometry(i, out int x, out int y, out int w, out int h);
            list.Add((x, y, w, h));
        }
        return list;
    }

    public static IEnumerable<object[]> Cases()
    {
        int[] sizes = { 1, 2, 5, 31, 32, 33, 64, 70 };
        double[] densities = { 0.05, 0.3, 0.5, 0.7, 0.95 };
        foreach (int conn in new[] { 4, 8 })
            foreach (int w in sizes)
                foreach (double d in densities)
                    yield return new object[] { conn, w, d };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ConnCompBB_MatchesNaiveLabeling(int connectivity, int w, double density)
    {
        var rng = new Random(1000 * connectivity + w);
        for (int trial = 0; trial < 4; trial++) {
            int h = 1 + rng.Next(80);
            Pix pix = RandomPix(rng, w, h, density);
            var bits = ToBits(pix);
            var expected = NaiveBoxes(bits, connectivity);

            Boxa boxa = pix.ConnCompBB(connectivity);
            Assert.Equal(expected, Geometry(boxa));
            Assert.Equal(expected.Count, pix.CountConnComp(connectivity));
            Assert.Equal(expected, Geometry(pix.ConnComp(connectivity)));

                /* The input is not modified */
            Assert.Equal(bits, ToBits(pix));
        }
    }

    [Fact]
    public void EmptyImage_GivesNoComponents()
    {
        Pix pix = Pix.Create(40, 20, 1);
        Assert.Equal(0, pix.ConnCompBB(4).Count);
        Assert.Equal(0, pix.ConnCompBB(8).Count);
        Assert.Equal(0, pix.CountConnComp(8));
    }

    [Fact]
    public void Connectivity_DiagonalPixelsJoinOnlyWith8()
    {
        Pix pix = Pix.Create(5, 5, 1);
        pix.SetPixel(1, 1, 1);
        pix.SetPixel(2, 2, 1);
        pix.SetPixel(3, 3, 1);

        Assert.Equal(3, pix.CountConnComp(4));
        Assert.Equal(1, pix.CountConnComp(8));
        Assert.Equal(new[] { (1, 1, 1, 1), (2, 2, 1, 1), (3, 3, 1, 1) }, Geometry(pix.ConnCompBB(4)));
        Assert.Equal(new[] { (1, 1, 3, 3) }, Geometry(pix.ConnCompBB(8)));
    }

    [Fact]
    public void Components_AreInRasterOrderOfFirstPixel()
    {
            /* A 'U' shape found first at its upper left arm, then a pixel to its right in row 0 */
        Pix pix = Pix.Create(10, 6, 1);
        for (int y = 0; y < 4; y++) {
            pix.SetPixel(1, y, 1);
            pix.SetPixel(3, y, 1);
        }
        pix.SetPixel(2, 3, 1);
        pix.SetPixel(8, 0, 1);

        Boxa boxa = pix.ConnCompBB(4);
        Assert.Equal(new[] { (1, 0, 3, 4), (8, 0, 1, 1) }, Geometry(boxa));
    }

    [Fact]
    public void InvalidArguments_Throw()
    {
        Pix pix1 = Pix.Create(8, 8, 1);
        Pix pix8 = Pix.Create(8, 8, 8);
        Assert.Throws<ArgumentException>(() => pix8.ConnCompBB(4));
        Assert.Throws<ArgumentException>(() => pix8.CountConnComp(4));
        Assert.Throws<ArgumentException>(() => pix1.ConnCompBB(6));
        Assert.Throws<ArgumentException>(() => pix1.CountConnComp(5));
        Assert.Throws<ArgumentException>(() => pix1.ConnComp(0));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void SeedfillBB_ErasesOnlyTheComponent(int connectivity)
    {
        var rng = new Random(77 + connectivity);
        Pix pix = RandomPix(rng, 45, 37, 0.45);
        Pix work = Pix.Copy(null, pix);
        L_Stack stack = L_Stack.Create(37);
        stack.AuxStack = L_Stack.Create(0);

        Assert.True(work.NextOnPixelInRaster(0, 0, out int x, out int y));
        Box? box = work.SeedfillBB(stack, x, y, connectivity);
        Assert.NotNull(box);
        var expected = NaiveBoxes(ToBits(pix), connectivity)[0];
        Assert.Equal(expected, (box!.X, box.Y, box.Width, box.Height));

            /* The component is gone, and the stack is empty again */
        Assert.Equal(0, stack.Count);
        Assert.Equal(NaiveBoxes(ToBits(pix), connectivity).Count - 1,
                     NaiveBoxes(ToBits(work), connectivity).Count);
        Assert.Equal(0, ArrayAccess.GetDataBit(work.Data.AsSpan(y * work.Wpl), x));

            /* A seed on an OFF pixel or outside the image gives null and changes nothing */
        Assert.Null(work.SeedfillBB(stack, x, y, connectivity));
        Assert.Null(work.SeedfillBB(stack, -1, 0, connectivity));
        Assert.Null(work.SeedfillBB(stack, 0, 1000, connectivity));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void Seedfill_MatchesSeedfillBB(int connectivity)
    {
        var rng = new Random(5 + connectivity);
        Pix pix = RandomPix(rng, 70, 50, 0.5);
        Pix a = Pix.Copy(null, pix);
        Pix b = Pix.Copy(null, pix);
        L_Stack stack = L_Stack.Create(0);

        Assert.True(a.NextOnPixelInRaster(0, 0, out int x, out int y));
        a.Seedfill(stack, x, y, connectivity);
        b.SeedfillBB(stack, x, y, connectivity);
        Assert.Equal(ToBits(b), ToBits(a));
    }

    [Fact]
    public void NextOnPixelInRaster_FindsPixelsInRasterOrder()
    {
        Pix pix = Pix.Create(100, 5, 1);
        pix.SetPixel(33, 1, 1);
        pix.SetPixel(99, 1, 1);
        pix.SetPixel(0, 3, 1);

        Assert.True(pix.NextOnPixelInRaster(0, 0, out int x, out int y));
        Assert.Equal((33, 1), (x, y));
        Assert.True(pix.NextOnPixelInRaster(34, 1, out x, out y));
        Assert.Equal((99, 1), (x, y));
        Assert.True(pix.NextOnPixelInRaster(99, 1, out x, out y));
        Assert.Equal((99, 1), (x, y));
        Assert.True(pix.NextOnPixelInRaster(0, 2, out x, out y));
        Assert.Equal((0, 3), (x, y));
        Assert.False(pix.NextOnPixelInRaster(1, 3, out _, out _));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(100)]
    public void NextOnPixelInRaster_MatchesNaiveSearchAcrossWordBoundaries(int width)
    {
        const int height = 7;
        var rng = new Random(0xC0FFEE + width);
        Pix pix = Pix.Create(width, height, 1);
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                if (rng.NextDouble() < 0.23)
                    pix.SetPixel(x, y, 1);
            }
        }

        // Padding bits must never be returned as image pixels.
        int tailBits = width & 31;
        if (tailBits != 0) {
            uint paddingMask = (1u << (32 - tailBits)) - 1u;
            for (int y = 0; y < height; y++)
                pix.Data[y * pix.Wpl + pix.Wpl - 1] |= paddingMask;
        }

        for (int ystart = 0; ystart < height; ystart++) {
            for (int xstart = 0; xstart < width; xstart++) {
                bool expected = false;
                int expectedX = 0;
                int expectedY = 0;
                for (int y = ystart; y < height && !expected; y++) {
                    int xbegin = y == ystart ? xstart : 0;
                    for (int x = xbegin; x < width; x++) {
                        if (ArrayAccess.GetDataBit(pix.Data.AsSpan(y * pix.Wpl), x) != 0) {
                            expected = true;
                            expectedX = x;
                            expectedY = y;
                            break;
                        }
                    }
                }

                bool found = pix.NextOnPixelInRaster(xstart, ystart, out int actualX, out int actualY);
                Assert.Equal(expected, found);
                if (expected)
                    Assert.Equal((expectedX, expectedY), (actualX, actualY));
            }
        }
    }

    [Fact]
    public void Stack_IsLifoAndGrows()
    {
        L_Stack stack = L_Stack.Create(0);
        Assert.Null(stack.Remove());
        for (int i = 0; i < 1000; i++)
            stack.Add(i);
        Assert.Equal(1000, stack.Count);
        for (int i = 999; i >= 0; i--)
            Assert.Equal(i, stack.Remove());
        Assert.Equal(0, stack.Count);
    }
}
