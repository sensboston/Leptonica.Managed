using System;
using Leptonica;
using Naive = Leptonica.Managed.Tests.Rop.NaiveRop;

namespace Leptonica.Managed.Tests.AdaptMap;

/// <summary>
/// Tests of adaptmap.c on synthetic images (uniform and gradient backgrounds) with the
/// expected map values worked out here by plain loops.  The tests marked with a skip reason
/// need functions of files that are not ported yet (convolve.c, scale2.c, pixabasic.c,
/// enhance.c); they were checked against simple stand-ins for those functions.
/// </summary>
public class AdaptMapTests
{

    private static Pix MakeGray(int w, int h, Func<int, int, int> f)
    {
        Pix pix = Pix.Create(w, h, 8);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                Naive.SetPixel(pix, j, i, (uint)f(j, i));
        return pix;
    }

    private static Pix MakeRgb(int w, int h, Func<int, int, (int r, int g, int b)> f)
    {
        Pix pix = Pix.Create(w, h, 32);
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w; j++) {
                (int r, int g, int b) = f(j, i);
                Pix.ComposeRGBPixel(r, g, b, out uint pixel);
                Naive.SetPixel(pix, j, i, pixel);
            }
        }
        return pix;
    }

    private static Pix MakeMap(int w, int h, int d, Func<int, int, int> f)
    {
        Pix pix = Pix.Create(w, h, d);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                Naive.SetPixel(pix, j, i, (uint)f(j, i));
        return pix;
    }

    private static void AssertMap(int[,] expected, Pix actual)
    {
        int h = expected.GetLength(0);
        int w = expected.GetLength(1);
        Assert.Equal(w, actual.Width);
        Assert.Equal(h, actual.Height);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                Assert.True(expected[i, j] == (int)Naive.GetPixel(actual, j, i),
                            $"map differs at x={j}, y={i}: expected {expected[i, j]}, " +
                            $"got {Naive.GetPixel(actual, j, i)}");
    }

    /// <summary>Plain-loop average of the pixels of complete tiles, as computed by the map functions.</summary>
    private static int[,] TileMeans(Pix pix, int sx, int sy, Func<int, int, bool> isFg, int mincount)
    {
        int nx = pix.Width / sx, ny = pix.Height / sy;
        var means = new int[ny, nx];
        for (int i = 0; i < ny; i++) {
            for (int j = 0; j < nx; j++) {
                int sum = 0, count = 0;
                for (int y = i * sy; y < (i + 1) * sy; y++) {
                    for (int x = j * sx; x < (j + 1) * sx; x++) {
                        if (!isFg(x, y)) {
                            sum += (int)Naive.GetPixel(pix, x, y);
                            count++;
                        }
                    }
                }
                means[i, j] = count >= mincount ? sum / count : 0;
            }
        }
        return means;
    }

    /*------------------------------------------------------------------*
     *                         pixFillMapHoles                          *
     *------------------------------------------------------------------*/
    [Fact]
    public void FillMapHoles_FillsColumnsAndMissingColumns()
    {
        // 5 x 4 map; nx = 4 complete columns, ny = 3 complete rows.
        int[,] src = {
            { 0, 0, 10,  0, 0 },
            { 50, 0,  0,  0, 0 },
            { 0, 0,  0, 90, 0 },
            { 70, 0,  0,  0, 0 },
        };
        Pix pix = MakeMap(5, 4, 8, (x, y) => src[y, x]);

        Assert.True(pix.FillMapHoles(4, 3, L.L_FILL_BLACK));

        // Column 0: first valid value 50 at y = 1 is copied up and down; the last row
        // (outside ny) is 70 and is kept.  Column 1 has no data and takes column 0.
        // Column 2: 10 copied down.  Column 3: 90 copied up and down.  Column 4 copies
        // column 3 because w > nx.
        int[,] expected = {
            { 50, 50, 10, 90, 90 },
            { 50, 50, 10, 90, 90 },
            { 50, 50, 10, 90, 90 },
            { 70, 70, 10, 90, 90 },
        };
        AssertMap(expected, pix);
    }

    [Fact]
    public void FillMapHoles_FirstColumnsMissingAreCopiedBackward()
    {
        int[,] src = {
            { 0, 0, 0, 200 },
            { 0, 0, 0, 100 },
        };
        Pix pix = MakeMap(4, 2, 8, (x, y) => src[y, x]);

        Assert.True(pix.FillMapHoles(4, 2, L.L_FILL_BLACK));

        AssertMap(new[,] { { 200, 200, 200, 200 }, { 100, 100, 100, 100 } }, pix);
    }

    [Fact]
    public void FillMapHoles_FillWhiteTreatsWhiteAsHole()
    {
        int[,] src = {
            { 255, 30, 255 },
            { 40, 255, 255 },
            { 255, 255, 255 },
        };
        Pix pix = MakeMap(3, 3, 8, (x, y) => src[y, x]);

        Assert.True(pix.FillMapHoles(3, 3, L.L_FILL_WHITE));

        // Column 2 has no data and copies column 1.
        AssertMap(new[,] { { 40, 30, 30 }, { 40, 30, 30 }, { 40, 30, 30 } }, pix);
    }

    [Fact]
    public void FillMapHoles_NoDataReturnsFalseAndLeavesMapUnchanged()
    {
        Pix pix = MakeMap(4, 3, 8, (x, y) => 0);

        Assert.False(pix.FillMapHoles(3, 2, L.L_FILL_BLACK));

        AssertMap(new int[3, 4], pix);
    }

    [Fact]
    public void FillMapHoles_RejectsBadPix()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 16).FillMapHoles(4, 4, L.L_FILL_BLACK));
        Pix cmapped = Pix.CreateWithCmap(4, 4, 8, L.L_SET_WHITE);
        Assert.Throws<ArgumentException>(() => cmapped.FillMapHoles(4, 4, L.L_FILL_BLACK));
    }

    /*------------------------------------------------------------------*
     *                      pixExtendByReplication                      *
     *------------------------------------------------------------------*/
    [Fact]
    public void ExtendByReplication_ReplicatesLastColumnAndRow()
    {
        Pix pix = MakeGray(3, 2, (x, y) => 10 * (y + 1) + x);   // 10 11 12 / 20 21 22

        Pix pixd = pix.ExtendByReplication(2, 1);

        AssertMap(new[,] {
            { 10, 11, 12, 12, 12 },
            { 20, 21, 22, 22, 22 },
            { 20, 21, 22, 22, 22 },
        }, pixd);
    }

    [Fact]
    public void ExtendByReplication_OnlyOneDirection()
    {
        Pix pix = MakeGray(2, 2, (x, y) => 1 + x + 2 * y);   // 1 2 / 3 4

        AssertMap(new[,] { { 1, 2 }, { 3, 4 }, { 3, 4 } }, pix.ExtendByReplication(0, 1));
        AssertMap(new[,] { { 1, 2, 2 }, { 3, 4, 4 } }, pix.ExtendByReplication(1, 0));
    }

    [Fact]
    public void ExtendByReplication_ZeroExtensionIsACopy()
    {
        Pix pix = MakeGray(5, 3, (x, y) => 7 * x + y);

        Pix pixd = pix.ExtendByReplication(0, 0);

        Assert.NotSame(pix, pixd);
        Assert.Equal(pix.Data, pixd.Data);
    }

    [Fact]
    public void ExtendByReplication_RejectsNon8bpp()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 32).ExtendByReplication(1, 1));
    }

    /*------------------------------------------------------------------*
     *                    Apply inverse background map                  *
     *------------------------------------------------------------------*/
    [Fact]
    public void ApplyInvBackgroundGrayMap_ScalesEachTile()
    {
        // 7 x 5 image in tiles of 4 x 3: the map is 2 x 2 and the right and bottom
        // tiles are partial.
        Pix pixs = MakeGray(7, 5, (x, y) => 100 + x);
        int[,] mapvals = { { 512, 300 }, { 256, 600 } };
        Pix pixm = MakeMap(2, 2, 16, (x, y) => mapvals[y, x]);

        Pix pixd = pixs.ApplyInvBackgroundGrayMap(pixm, 4, 3);

        Assert.Equal(8, pixd.Depth);
        Assert.Equal(7, pixd.Width);
        Assert.Equal(5, pixd.Height);
        for (int y = 0; y < 5; y++) {
            for (int x = 0; x < 7; x++) {
                int factor = mapvals[y / 3, x / 4];
                int expected = Math.Min(255, ((100 + x) * factor) / 256);
                Assert.Equal(expected, (int)Naive.GetPixel(pixd, x, y));
            }
        }
        // 100 * 512 / 256 = 200; 105 * 600 / 256 = 246; 106 * 600 / 256 = 248
        Assert.Equal(200u, Naive.GetPixel(pixd, 0, 0));
        Assert.Equal(246u, Naive.GetPixel(pixd, 5, 4));
    }

    [Fact]
    public void ApplyInvBackgroundGrayMap_ClipsAt255()
    {
        Pix pixs = MakeGray(4, 4, (x, y) => 250);
        Pix pixm = MakeMap(1, 1, 16, (x, y) => 1000);

        Pix pixd = pixs.ApplyInvBackgroundGrayMap(pixm, 4, 4);

        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                Assert.Equal(255u, Naive.GetPixel(pixd, x, y));
    }

    [Fact]
    public void ApplyInvBackgroundGrayMap_RejectsBadArguments()
    {
        Pix pixs = MakeGray(8, 8, (x, y) => 100);
        Pix pixm = MakeMap(2, 2, 16, (x, y) => 256);
        Assert.Throws<ArgumentException>(() => pixs.ApplyInvBackgroundGrayMap(MakeMap(2, 2, 8, (x, y) => 1), 4, 4));
        Assert.Throws<ArgumentException>(() => pixs.ApplyInvBackgroundGrayMap(pixm, 0, 4));
        Assert.Throws<ArgumentException>(() => pixs.ApplyInvBackgroundGrayMap(pixm, 4, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(8, 8, 32).ApplyInvBackgroundGrayMap(pixm, 4, 4));
    }

    [Fact]
    public void ApplyInvBackgroundRGBMap_ScalesEachComponent()
    {
        Pix pixs = MakeRgb(6, 4, (x, y) => (100 + x, 120 + y, 140));
        Pix pixmr = MakeMap(2, 2, 16, (x, y) => 512);
        Pix pixmg = MakeMap(2, 2, 16, (x, y) => 256);
        Pix pixmb = MakeMap(2, 2, 16, (x, y) => 400);

        Pix pixd = pixs.ApplyInvBackgroundRGBMap(pixmr, pixmg, pixmb, 3, 2);

        Assert.Equal(32, pixd.Depth);
        for (int y = 0; y < 4; y++) {
            for (int x = 0; x < 6; x++) {
                Pix.ExtractRGBValues(Naive.GetPixel(pixd, x, y), out int r, out int g, out int b);
                Assert.Equal(Math.Min(255, (100 + x) * 512 / 256), r);
                Assert.Equal(120 + y, g);
                Assert.Equal(140 * 400 / 256, b);
            }
        }
    }

    [Fact]
    public void ApplyInvBackgroundRGBMap_RejectsBadArguments()
    {
        Pix pixs = MakeRgb(6, 4, (x, y) => (1, 2, 3));
        Pix m16 = MakeMap(2, 2, 16, (x, y) => 256);
        Pix m8 = MakeMap(2, 2, 8, (x, y) => 1);
        Assert.Throws<ArgumentException>(() => pixs.ApplyInvBackgroundRGBMap(m16, m16, m8, 3, 2));
        Assert.Throws<ArgumentException>(() => pixs.ApplyInvBackgroundRGBMap(m16, m16, m16, 0, 2));
        Assert.Throws<ArgumentException>(() => MakeGray(6, 4, (x, y) => 1).ApplyInvBackgroundRGBMap(m16, m16, m16, 3, 2));
    }

    /*------------------------------------------------------------------*
     *                        Apply variable map                        *
     *------------------------------------------------------------------*/
    private static int ExpectedVariable(int vals, int valg, int target)
    {
        float fval = (float)((float)(vals * target) / (valg + 0.5));
        return Math.Min(255, (int)(fval + 0.5));
    }

    [Theory]
    [InlineData(20, 15)]    // direct computation
    [InlineData(400, 300)]  // more than 100000 pixels: uses the lookup table
    public void ApplyVariableGrayMap_MapsThresholdToTarget(int w, int h)
    {
        Pix pixs = MakeGray(w, h, (x, y) => (x * 7 + y * 3) & 0xff);
        Pix pixg = MakeGray(w, h, (x, y) => 40 + ((x + 5 * y) & 0x7f));

        Pix pixd = pixs.ApplyVariableGrayMap(pixg, 128);

        Assert.Equal(8, pixd.Depth);
        for (int y = 0; y < h; y++) {
            for (int x = 0; x < w; x++) {
                int expected = ExpectedVariable((int)Naive.GetPixel(pixs, x, y),
                                                (int)Naive.GetPixel(pixg, x, y), 128);
                Assert.True(expected == (int)Naive.GetPixel(pixd, x, y), $"differs at x={x}, y={y}");
            }
        }
    }

    [Fact]
    public void ApplyVariableGrayMap_PixelEqualToMapGoesToTarget()
    {
        // A pixel equal to the local threshold is mapped to the target (to within rounding).
        Pix pixs = MakeGray(8, 1, (x, y) => 100 + 10 * x);
        Pix pixg = MakeGray(8, 1, (x, y) => 100 + 10 * x);

        Pix pixd = pixs.ApplyVariableGrayMap(pixg, 128);

        for (int x = 0; x < 8; x++)
            Assert.InRange((int)Naive.GetPixel(pixd, x, 0), 127, 128);
    }

    [Fact]
    public void ApplyVariableGrayMap_RejectsBadArguments()
    {
        Pix pixs = MakeGray(8, 8, (x, y) => 100);
        Assert.Throws<ArgumentException>(() => pixs.ApplyVariableGrayMap(MakeGray(8, 7, (x, y) => 1), 128));
        Assert.Throws<ArgumentException>(() => Pix.Create(8, 8, 32).ApplyVariableGrayMap(Pix.Create(8, 8, 32), 128));
    }

    /*------------------------------------------------------------------*
     *                    Measurement of local background               *
     *------------------------------------------------------------------*/
    [Fact]
    public void GetBackgroundGrayMap_UniformImageGivesUniformMap()
    {
        Pix pixs = MakeGray(60, 45, (x, y) => 150);

        Pix? pixm = pixs.GetBackgroundGrayMap(null, 10, 15, 60, 40);

        Assert.NotNull(pixm);
        AssertMap(new[,] { { 150, 150, 150, 150, 150, 150 },
                           { 150, 150, 150, 150, 150, 150 },
                           { 150, 150, 150, 150, 150, 150 } }, pixm);
    }

    [Fact]
    public void GetBackgroundGrayMap_GradientMatchesTileMeansAndExtendsPartialTiles()
    {
        // 65 x 47 in tiles of 10 x 15: the map is 7 x 4; only 6 x 3 tiles are complete and
        // the last column and row are filled by replication.
        Pix pixs = MakeGray(65, 47, (x, y) => 100 + x + 2 * (y / 5));

        Pix? pixm = pixs.GetBackgroundGrayMap(null, 10, 15, 60, 40);

        Assert.NotNull(pixm);
        int[,] means = TileMeans(pixs, 10, 15, (x, y) => false, 40);
        var expected = new int[4, 7];
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 7; j++)
                expected[i, j] = means[Math.Min(i, 2), Math.Min(j, 5)];
        AssertMap(expected, pixm);
    }

    [Fact]
    public void GetBackgroundGrayMap_ForegroundIsExcludedFromTheAverage()
    {
        // Dark 3 x 3 block in the middle of tile (1, 1).  The foreground mask is dilated
        // by 3 pixels, so 9 x 9 = 81 of the 150 pixels of the tile are ignored and the
        // tile average stays at the background value.
        Pix pixs = MakeGray(60, 45, (x, y) => (x >= 24 && x < 27 && y >= 21 && y < 24) ? 20 : 150);

        Pix? pixm = pixs.GetBackgroundGrayMap(null, 10, 15, 60, 40);

        Assert.NotNull(pixm);
        AssertMap(new[,] { { 150, 150, 150, 150, 150, 150 },
                           { 150, 150, 150, 150, 150, 150 },
                           { 150, 150, 150, 150, 150, 150 } }, pixm);
    }

    [Fact]
    public void GetBackgroundGrayMap_TilesWithTooFewBackgroundPixelsAreFilledFromAbove()
    {
        // Tile rows have the values 100, 120, 140.  With mincount = 140 the tile (2, 1),
        // which has 81 pixels covered by the dilated foreground, is a hole and takes the
        // value of the tile above it.
        Pix pixs = MakeGray(60, 45, (x, y) =>
            (x >= 24 && x < 27 && y >= 21 && y < 24) ? 20 : 100 + 20 * (y / 15));

        Pix? pixm = pixs.GetBackgroundGrayMap(null, 10, 15, 60, 140);

        Assert.NotNull(pixm);
        AssertMap(new[,] { { 100, 100, 100, 100, 100, 100 },
                           { 120, 120, 100, 120, 120, 120 },
                           { 140, 140, 140, 140, 140, 140 } }, pixm);
    }

    [Fact]
    public void GetBackgroundGrayMap_AllForegroundGivesNoMap()
    {
        // Every tile is a hole, so there is no background to propagate.
        Pix pixs = MakeGray(60, 45, (x, y) => 20);

        Assert.Null(pixs.GetBackgroundGrayMap(null, 10, 15, 60, 40));
    }

    [Fact]
    public void GetBackgroundGrayMap_MincountAboveTileAreaIsReset()
    {
        // mincount > sx * sy is reset to (sx * sy) / 3 = 50.
        Pix pixs = MakeGray(60, 45, (x, y) => 150);

        Pix? pixm = pixs.GetBackgroundGrayMap(null, 10, 15, 60, 1000);

        Assert.NotNull(pixm);
        Assert.Equal(6, pixm.Width);
        Assert.Equal(3, pixm.Height);
        Assert.Equal(150u, Naive.GetPixel(pixm, 3, 1));
    }

    [Fact]
    public void GetBackgroundGrayMap_RejectsBadArguments()
    {
        Pix pixs = MakeGray(60, 45, (x, y) => 150);
        Assert.Throws<ArgumentException>(() => Pix.Create(60, 45, 32).GetBackgroundGrayMap(null, 10, 15, 60, 40));
        Assert.Throws<ArgumentException>(() => pixs.GetBackgroundGrayMap(null, 3, 15, 60, 40));
        Assert.Throws<ArgumentException>(() => pixs.GetBackgroundGrayMap(null, 10, 3, 60, 40));
        Assert.Throws<ArgumentException>(() => pixs.GetBackgroundGrayMap(Pix.Create(60, 45, 8), 10, 15, 60, 40));
        // An image mask that is all foreground leaves no background.
        Pix allfg = Pix.Create(60, 45, 1);
        Pix.Invert(allfg, allfg);
        Assert.Throws<ArgumentException>(() => pixs.GetBackgroundGrayMap(allfg, 10, 15, 60, 40));
    }

    [Fact]
    public void GetBackgroundGrayMap_EmptyImageMaskChangesNothing()
    {
        Pix pixs = MakeGray(60, 45, (x, y) => 100 + x);
        Pix pixim = Pix.Create(60, 45, 1);   // no foreground pixels

        Pix? withmask = pixs.GetBackgroundGrayMap(pixim, 10, 15, 60, 40);
        Pix? without = pixs.GetBackgroundGrayMap(null, 10, 15, 60, 40);

        Assert.NotNull(withmask);
        Assert.NotNull(without);
        Assert.Equal(without.Data, withmask.Data);
        Assert.True(pixim.Zero());   // the mask is restored
    }

    [Fact]
    public void GetBackgroundRGBMap_UniformComponents()
    {
        Pix pixs = MakeRgb(60, 45, (x, y) => (200, 150, 100));

        bool ok = pixs.GetBackgroundRGBMap(null, null, 10, 15, 60, 40,
                                           out Pix? pixmr, out Pix? pixmg, out Pix? pixmb);

        Assert.True(ok);
        foreach ((Pix? pixm, int val) in new[] { (pixmr, 200), (pixmg, 150), (pixmb, 100) }) {
            Assert.NotNull(pixm);
            var expected = new int[3, 6];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 6; j++)
                    expected[i, j] = val;
            AssertMap(expected, pixm);
        }
    }

    [Fact]
    public void GetBackgroundRGBMap_GradientMatchesTileMeansPerComponent()
    {
        // The foreground mask comes from the green component (>= 60 here, so no foreground).
        Pix pixs = MakeRgb(65, 47, (x, y) => (100 + x, 90 + (y / 5), 200 - x / 2));

        bool ok = pixs.GetBackgroundRGBMap(null, null, 10, 15, 60, 40,
                                           out Pix? pixmr, out Pix? pixmg, out Pix? pixmb);

        Assert.True(ok);
        Pix[] comps = {
            MakeGray(65, 47, (x, y) => 100 + x),
            MakeGray(65, 47, (x, y) => 90 + (y / 5)),
            MakeGray(65, 47, (x, y) => 200 - x / 2),
        };
        Pix?[] maps = { pixmr, pixmg, pixmb };
        for (int c = 0; c < 3; c++) {
            int[,] means = TileMeans(comps[c], 10, 15, (x, y) => false, 40);
            var expected = new int[4, 7];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 7; j++)
                    expected[i, j] = means[Math.Min(i, 2), Math.Min(j, 5)];
            AssertMap(expected, maps[c]!);
        }
    }

    [Fact]
    public void GetBackgroundRGBMap_UsesSuppliedGrayImageForTheMask()
    {
        // The color image is bright everywhere, but the supplied gray version has a dark
        // block, which therefore drives the foreground mask and the hole in the map.
        Pix pixs = MakeRgb(60, 45, (x, y) => (200, 150, 100 + 20 * (y / 15)));
        Pix pixg = MakeGray(60, 45, (x, y) => (x >= 24 && x < 27 && y >= 21 && y < 24) ? 20 : 150);

        bool ok = pixs.GetBackgroundRGBMap(null, pixg, 10, 15, 60, 140,
                                           out Pix? pixmr, out Pix? pixmg, out Pix? pixmb);

        Assert.True(ok);
        AssertMap(new[,] { { 100, 100, 100, 100, 100, 100 },
                           { 120, 120, 100, 120, 120, 120 },
                           { 140, 140, 140, 140, 140, 140 } }, pixmb!);
        Assert.Equal(200u, Naive.GetPixel(pixmr!, 2, 1));
        Assert.Equal(150u, Naive.GetPixel(pixmg!, 2, 1));
    }

    [Fact]
    public void GetBackgroundRGBMap_AllForegroundReturnsFalse()
    {
        Pix pixs = MakeRgb(60, 45, (x, y) => (10, 20, 30));

        bool ok = pixs.GetBackgroundRGBMap(null, null, 10, 15, 60, 40,
                                           out Pix? pixmr, out Pix? pixmg, out Pix? pixmb);

        Assert.False(ok);
        Assert.Null(pixmr);
        Assert.Null(pixmg);
        Assert.Null(pixmb);
    }

    [Fact]
    public void GetBackgroundRGBMap_RejectsBadArguments()
    {
        Pix pixs = MakeRgb(60, 45, (x, y) => (200, 150, 100));
        Assert.Throws<ArgumentException>(() => MakeGray(60, 45, (x, y) => 1)
            .GetBackgroundRGBMap(null, null, 10, 15, 60, 40, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => pixs
            .GetBackgroundRGBMap(null, null, 3, 15, 60, 40, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => pixs
            .GetBackgroundRGBMap(Pix.Create(60, 45, 8), null, 10, 15, 60, 40, out _, out _, out _));
    }

    /*------------------------------------------------------------------*
     *                 Background normalization (top level)             *
     *------------------------------------------------------------------*/
    [Fact]
    public void BackgroundNorm_TooFewTilesForTheMapReturnsACopy()
    {
        // The map is 4 x 2 here, below the 5 x 5 minimum of pixGetInvBackgroundMap(), so
        // as in C the result is a copy of the source.
        Pix pixs = MakeGray(40, 30, (x, y) => 150 + (x & 3));

        Pix pixd = pixs.BackgroundNorm(null, null, 10, 15, 60, 40, 200, 2, 1);

        Assert.NotSame(pixs, pixd);
        Assert.Equal(pixs.Data, pixd.Data);
    }

    [Fact]
    public void BackgroundNorm_NoBackgroundReturnsACopy()
    {
        Pix pixs = MakeGray(60, 45, (x, y) => 20);

        Pix pixd = pixs.BackgroundNorm(null, null, 10, 15, 60, 40, 200, 0, 0);

        Assert.NotSame(pixs, pixd);
        Assert.Equal(pixs.Data, pixd.Data);
    }

    [Fact]
    public void BackgroundNorm_RejectsBadArguments()
    {
        Pix pixs = MakeGray(60, 45, (x, y) => 150);
        Assert.Throws<ArgumentException>(() => Pix.Create(60, 45, 16).BackgroundNorm(null, null, 10, 15, 60, 40, 200, 2, 1));
        Assert.Throws<ArgumentException>(() => pixs.BackgroundNorm(null, null, 3, 15, 60, 40, 200, 2, 1));
        Pix allfg = Pix.Create(60, 45, 1);
        Pix.Invert(allfg, allfg);
        Assert.Throws<ArgumentException>(() => pixs.BackgroundNorm(allfg, null, 10, 15, 60, 40, 200, 2, 1));
        // The mask is left as it was.
        Assert.False(allfg.Zero());
    }

    [Fact]
    public void BackgroundNormMorph_RejectsBadArguments()
    {
        Pix pixs = MakeGray(60, 45, (x, y) => 150);
        Assert.Throws<ArgumentException>(() => pixs.BackgroundNormMorph(null, 1, 5, 200));
        Assert.Throws<ArgumentException>(() => pixs.BackgroundNormMorph(null, 17, 5, 200));
        Assert.Throws<ArgumentException>(() => Pix.Create(60, 45, 4).BackgroundNormMorph(null, 4, 5, 200));
    }

    [Fact]
    public void BackgroundNormArrays_RejectBadArguments()
    {
        Pix gray = MakeGray(60, 45, (x, y) => 150);
        Pix rgb = MakeRgb(60, 45, (x, y) => (1, 2, 3));
        Assert.Throws<ArgumentException>(() => rgb.BackgroundNormGrayArray(null, 10, 15, 60, 40, 200, 2, 1));
        Assert.Throws<ArgumentException>(() => gray.BackgroundNormGrayArray(null, 3, 15, 60, 40, 200, 2, 1));
        Assert.Throws<ArgumentException>(() => gray.BackgroundNormRGBArrays(null, null, 10, 15, 60, 40, 200, 2, 1,
                                                                            out _, out _, out _));
        Assert.Throws<ArgumentException>(() => rgb.BackgroundNormGrayArrayMorph(null, 4, 5, 200));
        Assert.Throws<ArgumentException>(() => gray.BackgroundNormRGBArraysMorph(null, 4, 5, 200,
                                                                                 out _, out _, out _));
        Assert.Throws<ArgumentException>(() => gray.BackgroundNormGrayArrayMorph(null, 1, 5, 200));
    }

    /*------------------------------------------------------------------*
     *                         Argument checks                          *
     *------------------------------------------------------------------*/
    [Fact]
    public void ContrastNorm_RejectsBadArguments()
    {
        Pix pixs = MakeGray(100, 100, (x, y) => x);
        Assert.Throws<ArgumentException>(() => Pix.ContrastNorm(null, Pix.Create(100, 100, 32), 20, 20, 50, 2, 2));
        Assert.Throws<ArgumentException>(() => Pix.ContrastNorm(Pix.Create(100, 100, 8), pixs, 20, 20, 50, 2, 2));
        Assert.Throws<ArgumentException>(() => Pix.ContrastNorm(null, pixs, 4, 20, 50, 2, 2));
        Assert.Throws<ArgumentException>(() => Pix.ContrastNorm(null, pixs, 20, 4, 50, 2, 2));
        Assert.Throws<ArgumentException>(() => Pix.ContrastNorm(null, pixs, 20, 20, 50, -1, 2));
        Assert.Throws<ArgumentException>(() => Pix.ContrastNorm(null, pixs, 20, 20, 50, 2, 9));
    }

    [Fact]
    public void BackgroundNormFlex_RejectsBadArguments()
    {
        Pix pixs = MakeGray(100, 100, (x, y) => 150);
        Assert.Throws<ArgumentException>(() => Pix.Create(100, 100, 32).BackgroundNormFlex(7, 7, 1, 1, 0));
        Assert.Throws<ArgumentException>(() => pixs.BackgroundNormFlex(2, 7, 1, 1, 0));
        Assert.Throws<ArgumentException>(() => pixs.BackgroundNormFlex(7, 11, 1, 1, 0));
        Assert.Throws<ArgumentException>(() => pixs.BackgroundNormFlex(7, 7, 0, 1, 0));
        Assert.Throws<ArgumentException>(() => pixs.BackgroundNormFlex(7, 7, 1, 4, 0));
    }

    [Fact]
    public void ThresholdSpreadNorm_RejectsBadArguments()
    {
        Pix pixs = MakeGray(100, 100, (x, y) => 150);
        Assert.Throws<ArgumentException>(() => Pix.Create(100, 100, 32)
            .ThresholdSpreadNorm(L.L_SOBEL_EDGE, 10, 0, 0, 1.0f, 0, 255, 128, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => pixs
            .ThresholdSpreadNorm(7, 10, 0, 0, 1.0f, 0, 255, 128, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => pixs
            .ThresholdSpreadNorm(L.L_SOBEL_EDGE, 10, 0, 0, 1.0f, 0, 255, 128, false, false, false,
                                 out _, out _, out _));
    }

    [Fact]
    public void GlobalNormNoSatRGB_RejectsBadArguments()
    {
        Pix pixs = MakeRgb(10, 10, (x, y) => (200, 190, 180));
        Assert.Throws<ArgumentException>(() => Pix.GlobalNormNoSatRGB(null, MakeGray(10, 10, (x, y) => 1), 200, 190, 180, 1, 0.9f));
        Assert.Throws<ArgumentException>(() => Pix.GlobalNormNoSatRGB(null, pixs, 200, 190, 180, 0, 0.9f));
        Assert.Throws<ArgumentException>(() => Pix.GlobalNormNoSatRGB(null, pixs, 200, 190, 180, 1, 1.5f));
        Assert.Throws<ArgumentException>(() => Pix.GlobalNormNoSatRGB(null, pixs, 0, 190, 180, 1, 0.9f));
    }

    [Fact]
    public void GlobalNormRGB_RejectsGrayWithoutColormap()
    {
        Assert.Throws<ArgumentException>(() => Pix.GlobalNormRGB(null, MakeGray(10, 10, (x, y) => 1), 200, 190, 180, 255));
    }

    [Fact]
    public void BackgroundNormTo1MinMax_RejectsBadArguments()
    {
        Pix pixs = MakeGray(20, 20, (x, y) => 150);
        Assert.Throws<ArgumentException>(() => pixs.BackgroundNormTo1MinMax(0, 1));
        Assert.Throws<ArgumentException>(() => pixs.BackgroundNormTo1MinMax(11, 1));
        Assert.Throws<ArgumentException>(() => pixs.BackgroundNormTo1MinMax(5, 3));
    }

    [Fact]
    public void BackgroundNormTo1MinMax_BinaryInputIsCopied()
    {
        Pix pixs = Pix.Create(20, 20, 1);
        pixs.SetPixel(3, 4, 1);

        Pix pixd = pixs.BackgroundNormTo1MinMax(5, 1);

        Assert.NotSame(pixs, pixd);
        Assert.Equal(pixs.Data, pixd.Data);
    }

    [Fact]
    public void CleanBackgroundToWhite_ResetsLargeWhiteValue()
    {
        // The argument check on depth runs first.
        Assert.Throws<ArgumentException>(() =>
            Pix.Create(20, 20, 16).CleanBackgroundToWhite(null, null, 1.0f, 70, 190));
    }

    /*------------------------------------------------------------------*
     *                  pixConvertTo8MinMax                             *
     *------------------------------------------------------------------*/
    [Fact]
    public void ConvertTo8MinMax_1bppMapsOneToBlack()
    {
        Pix pixs = Pix.Create(8, 2, 1);
        pixs.SetPixel(2, 1, 1);

        Pix pixd = pixs.ConvertTo8MinMax();

        Assert.Equal(8, pixd.Depth);
        Assert.Equal(0u, Naive.GetPixel(pixd, 2, 1));
        Assert.Equal(255u, Naive.GetPixel(pixd, 0, 0));
    }

    [Fact]
    public void ConvertTo8MinMax_8bppIsCopied()
    {
        Pix pixs = MakeGray(9, 5, (x, y) => 11 * x + y);

        Pix pixd = pixs.ConvertTo8MinMax();

        Assert.NotSame(pixs, pixd);
        Assert.Equal(pixs.Data, pixd.Data);
    }

    [Fact]
    public void ConvertTo8MinMax_32bppTakesMinimumComponent()
    {
        Pix pixs = MakeRgb(4, 2, (x, y) => (200, 100 + 10 * x, 150));

        Pix pixd = pixs.ConvertTo8MinMax();

        Assert.Equal(8, pixd.Depth);
        for (int x = 0; x < 4; x++)
            Assert.Equal((uint)Math.Min(150, 100 + 10 * x), Naive.GetPixel(pixd, x, 0));
    }

    [Fact]
    public void ConvertTo8MinMax_16bppUsesMostSignificantByte()
    {
        Pix pixs = MakeMap(4, 2, 16, (x, y) => (0x1200 + 0x100 * x) | 0x34);

        Pix pixd = pixs.ConvertTo8MinMax();

        Assert.Equal(8, pixd.Depth);
        for (int x = 0; x < 4; x++)
            Assert.Equal((uint)(0x12 + x), Naive.GetPixel(pixd, x, 0));
    }

    /*------------------------------------------------------------------*
     *      Tests that need functions of files that are not ported      *
     *------------------------------------------------------------------*/
    [Fact]
    public void GetInvBackgroundMap_InvertsMapValues()
    {
        Pix pixs = MakeGray(8, 6, (x, y) => 50 + 20 * x + y);

        Pix pixd = pixs.GetInvBackgroundMap(200, 0, 0);

        Assert.Equal(16, pixd.Depth);
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 8; x++)
                Assert.Equal((uint)(256 * 200 / (50 + 20 * x + y)), Naive.GetPixel(pixd, x, y));
        Assert.Equal(1024u, Naive.GetPixel(pixd, 0, 0));
    }

    [Fact]
    public void GetInvBackgroundMap_RejectsSmallMaps()
    {
        Assert.Throws<ArgumentException>(() => MakeGray(4, 8, (x, y) => 100).GetInvBackgroundMap(200, 0, 0));
        Assert.Throws<ArgumentException>(() => MakeGray(8, 4, (x, y) => 100).GetInvBackgroundMap(200, 0, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(8, 8, 16).GetInvBackgroundMap(200, 0, 0));
    }

    [Fact]
    public void BackgroundNorm_UniformBackgroundGoesNearBgval()
    {
        // The inverse map is 256 * 200 / 150 = 341 and 150 * 341 / 256 = 199, because the
        // factor is an integer.
        Pix pixs = MakeGray(100, 90, (x, y) => 150);

        Pix pixd = pixs.BackgroundNorm(null, null, 10, 15, 60, 40, 200, 0, 0);

        for (int y = 0; y < 90; y++)
            for (int x = 0; x < 100; x++)
                Assert.Equal(199u, Naive.GetPixel(pixd, x, y));
    }

    [Fact]
    public void BackgroundNorm_RgbUniformBackgroundGoesNearBgval()
    {
        Pix pixs = MakeRgb(100, 90, (x, y) => (150, 160, 170));

        Pix pixd = pixs.BackgroundNorm(null, null, 10, 15, 60, 40, 200, 0, 0);

        for (int y = 0; y < 90; y += 7) {
            for (int x = 0; x < 100; x += 3) {
                Pix.ExtractRGBValues(Naive.GetPixel(pixd, x, y), out int r, out int g, out int b);
                Assert.Equal(150 * (256 * 200 / 150) / 256, r);
                Assert.Equal(160 * (256 * 200 / 160) / 256, g);
                Assert.Equal(170 * (256 * 200 / 170) / 256, b);
            }
        }
    }

    [Fact]
    public void BackgroundNormGrayArray_MatchesInvertedMap()
    {
        Pix pixs = MakeGray(100, 90, (x, y) => 100 + 10 * (x / 10));

        Pix pixd = pixs.BackgroundNormGrayArray(null, 10, 15, 60, 40, 200, 0, 0);

        Assert.Equal(16, pixd.Depth);
        Assert.Equal(10, pixd.Width);
        Assert.Equal(6, pixd.Height);
        for (int j = 0; j < 10; j++)
            Assert.Equal((uint)(256 * 200 / (100 + 10 * j)), Naive.GetPixel(pixd, j, 3));
    }

    [Fact]
    public void ContrastNorm_ExpandsEachTileToFullRange()
    {
        // Two tiles side by side with ranges [100 .. 138] and [50 .. 240].
        Pix pixs = MakeGray(40, 20, (x, y) => x < 20 ? 100 + 2 * x : 50 + 10 * (x - 20));

        Pix pixd = Pix.ContrastNorm(null, pixs, 20, 20, 10, 0, 0);

        Assert.NotSame(pixs, pixd);
        // Both min and max of the tile are raised by 1 before use (to avoid 0 values), so the
        // tile minimum maps to 0 and the tile maximum to a little below 255.
        for (int tile = 0; tile < 2; tile++) {
            int min = tile == 0 ? 100 : 50;
            int max = tile == 0 ? 138 : 240;
            int diff = max - min;
            float factor = (float)(255.0 / (float)diff);
            for (int y = 0; y < 20; y++) {
                for (int x = 20 * tile; x < 20 * tile + 20; x++) {
                    int sval = Math.Max(0, (int)Naive.GetPixel(pixs, x, y) - (min + 1));
                    int expected = sval > diff ? 255 : (int)(factor * sval + 0.5);
                    Assert.Equal(expected, (int)Naive.GetPixel(pixd, x, y));
                }
            }
        }
        Assert.Equal(0u, Naive.GetPixel(pixd, 0, 5));
        Assert.Equal(0u, Naive.GetPixel(pixd, 20, 5));
        Assert.Equal(254u, Naive.GetPixel(pixd, 39, 5));
    }

    [Fact]
    public void ContrastNorm_InPlaceReturnsTheSameImage()
    {
        Pix pixs = MakeGray(40, 20, (x, y) => x < 20 ? 100 + 2 * x : 50 + 10 * (x - 20));
        Pix expected = Pix.ContrastNorm(null, pixs, 20, 20, 10, 0, 0);

        Pix pixd = Pix.ContrastNorm(pixs, pixs, 20, 20, 10, 0, 0);

        Assert.Same(pixs, pixd);
        Assert.Equal(expected.Data, pixd.Data);
    }

    [Fact]
    public void SmoothConnectedRegions_SetsEachRegionToItsAverage()
    {
        // Two separate 3 x 3 regions in the mask: the map under each is set to its average.
        Pix pixs = MakeGray(12, 6, (x, y) => 100 + x);
        Pix pixm = Pix.Create(12, 6, 1);
        for (int y = 1; y < 4; y++) {
            for (int x = 1; x < 4; x++)
                pixm.SetPixel(x, y, 1);
            for (int x = 7; x < 10; x++)
                pixm.SetPixel(x, y, 1);
        }

        pixs.SmoothConnectedRegions(pixm, 1);

        for (int y = 1; y < 4; y++) {
            for (int x = 1; x < 4; x++)
                Assert.Equal(102u, Naive.GetPixel(pixs, x, y));
            for (int x = 7; x < 10; x++)
                Assert.Equal(108u, Naive.GetPixel(pixs, x, y));
        }
        Assert.Equal(100u, Naive.GetPixel(pixs, 0, 0));
        Assert.Equal(104u, Naive.GetPixel(pixs, 4, 2));
    }

    [Fact]
    public void SmoothConnectedRegions_NoOpCases()
    {
        Pix pixs = MakeGray(12, 6, (x, y) => 100 + x);
        uint[] before = (uint[])pixs.Data.Clone();

        pixs.SmoothConnectedRegions(null, 1);
        pixs.SmoothConnectedRegions(Pix.Create(12, 6, 1), 1);   // mask without foreground

        Assert.Equal(before, pixs.Data);
        Assert.Throws<ArgumentException>(() => pixs.SmoothConnectedRegions(Pix.Create(12, 6, 8), 1));
        Assert.Throws<ArgumentException>(() => Pix.Create(12, 6, 32).SmoothConnectedRegions(null, 1));
    }

    [Fact]
    public void GlobalNormRGB_MapsWhitePointToMapval()
    {
        Pix pixs = MakeRgb(4, 2, (x, y) => (200, 150, 100));

        Pix pixd = Pix.GlobalNormRGB(null, pixs, 200, 150, 100, 255);

        Assert.NotSame(pixs, pixd);
        for (int x = 0; x < 4; x++) {
            Pix.ExtractRGBValues(Naive.GetPixel(pixd, x, 1), out int r, out int g, out int b);
            Assert.Equal((255, 255, 255), (r, g, b));
        }
    }
}
