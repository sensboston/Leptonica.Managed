using System;
using Leptonica;
using Naive = Leptonica.Managed.Tests.Rop.NaiveRop;

namespace Leptonica.Managed.Tests.ScaleTests;

/// <summary>
/// Tests of scale2.c.  Tiny images have hand-computed expectations (the value tables are
/// val = 255 - (255 * sum) / n with integer division); the dispatchers are checked
/// against the composition of the functions they are documented to call.
/// </summary>
public class Scale2Tests
{
    private static uint Mask(int d) => d == 32 ? 0xffffffffu : (1u << d) - 1;

    private static Pix Make(int w, int h, int d, Func<int, int, uint> f)
    {
        Pix pix = Pix.Create(w, h, d);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                Naive.SetPixel(pix, x, y, f(x, y) & Mask(d));
        return pix;
    }

    private static Pix Gray(uint[][] rows) =>
        Make(rows[0].Length, rows.Length, 8, (x, y) => rows[y][x]);

    private static void AssertRow(Pix pix, int y, params uint[] expected)
    {
        Assert.Equal(expected.Length, pix.Width);
        for (int x = 0; x < expected.Length; x++)
            Assert.True(expected[x] == Naive.GetPixel(pix, x, y),
                $"row {y} pixel {x}: expected {expected[x]} got {Naive.GetPixel(pix, x, y)}");
    }

    private static void AssertSame(Pix expected, Pix actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Depth, actual.Depth);
        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++)
                Assert.True(Naive.GetPixel(expected, x, y) == Naive.GetPixel(actual, x, y),
                    $"pixel ({x},{y}): expected {Naive.GetPixel(expected, x, y)} got {Naive.GetPixel(actual, x, y)}");
    }

    private static Pix RandomBinary(int seed, int w, int h)
    {
        var rng = new Random(seed);
        return Make(w, h, 1, (x, y) => (uint)rng.Next(2));
    }

    /*------------------------------------------------------------------*
     *                    Scale-to-gray, integer factors                *
     *------------------------------------------------------------------*/
    [Fact]
    public void ScaleToGray2_HandChecked()
    {
        /* 8 x 2: blocks hold 4, 2, 0, 0 ON pixels */
        Pix pix = Make(8, 2, 1, (x, y) => (uint)((y == 0 && x < 4) || (y == 1 && x < 2) ? 1 : 0));
        Pix pixd = pix.ScaleToGray2();
        Assert.Equal(8, pixd.Depth);
        AssertRow(pixd, 0, 0, 128, 255, 255);   /* 255 - (2 * 255) / 4 = 128 */
    }

    [Fact]
    public void ScaleToGray2_WidthNotMultipleOfFour()
    {
        /* wd = 3: only the "extra" path is used */
        Pix pix = Make(6, 2, 1, (x, y) => (uint)((y == 0 && x < 4) || (y == 1 && x < 2) ? 1 : 0));
        Pix pixd = pix.ScaleToGray2();
        Assert.Equal(3, pixd.Width);
        Assert.Equal(1, pixd.Height);
        AssertRow(pixd, 0, 0, 128, 255);
    }

    [Fact]
    public void ScaleToGray2_FiveColumnsAndOddHeight()
    {
        /* 10 x 5 -> 5 x 2: one full group of four plus one extra column */
        Pix pix = Make(10, 5, 1, (x, y) => (uint)(x < 2 && y < 2 ? 1 : 0));
        Pix pixd = pix.ScaleToGray2();
        Assert.Equal(5, pixd.Width);
        Assert.Equal(2, pixd.Height);
        AssertRow(pixd, 0, 0, 255, 255, 255, 255);
        AssertRow(pixd, 1, 255, 255, 255, 255, 255);
    }

    [Fact]
    public void ScaleToGray2_SetsPadBitsAndResolution()
    {
        Pix pix = Make(12, 2, 1, (x, y) => 0);
        pix.XRes = 300;
        pix.YRes = 200;
        Pix pixd = pix.ScaleToGray2();
        Assert.Equal(6, pixd.Width);
        Assert.Equal(150, pixd.XRes);
        Assert.Equal(100, pixd.YRes);
        AssertRow(pixd, 0, 255, 255, 255, 255, 255, 255);
    }

    [Fact]
    public void ScaleToGray3_HandChecked()
    {
        /* 24 x 3: block 0 is full (9 ON), block 1 has one column (3 ON), the rest are empty */
        Pix pix = Make(24, 3, 1, (x, y) => (uint)(x < 3 || x == 3 ? 1 : 0));
        Pix pixd = pix.ScaleToGray3();
        Assert.Equal(8, pixd.Width);
        Assert.Equal(1, pixd.Height);
        AssertRow(pixd, 0, 0, 170, 255, 255, 255, 255, 255, 255);   /* 255 - (3 * 255) / 9 = 170 */
    }

    [Fact]
    public void ScaleToGray3_AllOnAndSizes()
    {
        Pix pix = Make(50, 7, 1, (x, y) => 1);
        Pix pixd = pix.ScaleToGray3();
        Assert.Equal(16, pixd.Width);    /* (50 / 3) & ~7 */
        Assert.Equal(2, pixd.Height);
        AssertRow(pixd, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    [Fact]
    public void ScaleToGray4_HandChecked()
    {
        /* 8 x 4: block 0 full (16 ON), block 1 has its first row (4 ON) */
        Pix pix = Make(8, 4, 1, (x, y) => (uint)(x < 4 || y == 0 ? 1 : 0));
        Pix pixd = pix.ScaleToGray4();
        Assert.Equal(2, pixd.Width);
        Assert.Equal(1, pixd.Height);
        AssertRow(pixd, 0, 0, 192);   /* 255 - (4 * 255) / 16 = 192 */
    }

    [Fact]
    public void ScaleToGray4_WidthTruncatedToEven()
    {
        Pix pix = Make(19, 9, 1, (x, y) => 0);
        Pix pixd = pix.ScaleToGray4();
        Assert.Equal(4, pixd.Width);     /* (19 / 4) & ~1 */
        Assert.Equal(2, pixd.Height);
    }

    [Fact]
    public void ScaleToGray4_PackedOutputMatchesFourByFourReference()
    {
        foreach (int width in new[] { 8, 16, 24, 32, 40 }) {
            Pix pix = RandomBinary(7429 + width, width, 12);
            int outputWidth = (width / 4) & ~1;
            int outputHeight = 12 / 4;
            Pix expected = Make(outputWidth, outputHeight, 8, (x, y) => {
                int on = 0;
                for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                        on += (int)Naive.GetPixel(pix, 4 * x + sx, 4 * y + sy);
                return (uint)(255 - (255 * on) / 16);
            });
            AssertSame(expected, pix.ScaleToGray4());
        }
    }

    [Fact]
    public void ScaleToGray6_HandChecked()
    {
        /* 48 x 6: ON counts 36, 18, 1 and 0 in the first four 6x6 blocks, then empty blocks */
        Pix pix = Make(48, 6, 1, (x, y) =>
            (uint)(x < 6 ? 1 : x < 12 ? (y < 3 ? 1 : 0) : x == 12 && y == 0 ? 1 : 0));
        Pix pixd = pix.ScaleToGray6();
        Assert.Equal(8, pixd.Width);
        Assert.Equal(1, pixd.Height);
        AssertRow(pixd, 0, 0, 128, 248, 255, 255, 255, 255, 255);   /* 255 - 127, 255 - 7 */
    }

    [Fact]
    public void ScaleToGray8_HandChecked()
    {
        /* 16 x 8: block 0 has two full rows (16 ON), block 1 is full */
        Pix pix = Make(16, 8, 1, (x, y) => (uint)(x >= 8 || y < 2 ? 1 : 0));
        Pix pixd = pix.ScaleToGray8();
        Assert.Equal(2, pixd.Width);
        Assert.Equal(1, pixd.Height);
        AssertRow(pixd, 0, 192, 0);   /* 255 - (16 * 255) / 64 = 192 */
    }

    [Fact]
    public void ScaleToGray16_HandChecked()
    {
        /* 32 x 16: block 0 is full (256 ON, clipped to 255), block 1 has 8 full rows (128 ON) */
        Pix pix = Make(32, 16, 1, (x, y) => (uint)(x < 16 || y < 8 ? 1 : 0));
        Pix pixd = pix.ScaleToGray16();
        Assert.Equal(2, pixd.Width);
        Assert.Equal(1, pixd.Height);
        AssertRow(pixd, 0, 0, 127);
    }

    [Fact]
    public void ScaleToGrayN_RequireOneBppAndSize()
    {
        Pix gray = Pix.Create(64, 64, 8);
        Assert.Throws<ArgumentException>(() => gray.ScaleToGray2());
        Assert.Throws<ArgumentException>(() => gray.ScaleToGray3());
        Assert.Throws<ArgumentException>(() => gray.ScaleToGray4());
        Assert.Throws<ArgumentException>(() => gray.ScaleToGray6());
        Assert.Throws<ArgumentException>(() => gray.ScaleToGray8());
        Assert.Throws<ArgumentException>(() => gray.ScaleToGray16());

        Pix tiny = Pix.Create(15, 15, 1);
        Assert.Throws<ArgumentException>(() => tiny.ScaleToGray16());
        Assert.Throws<ArgumentException>(() => Pix.Create(1, 9, 1).ScaleToGray2());
        Assert.Throws<ArgumentException>(() => Pix.Create(23, 9, 1).ScaleToGray3());
        Assert.Throws<ArgumentException>(() => Pix.Create(7, 9, 1).ScaleToGray4());
        Assert.Throws<ArgumentException>(() => Pix.Create(47, 9, 1).ScaleToGray6());
        Assert.Throws<ArgumentException>(() => Pix.Create(7, 9, 1).ScaleToGray8());
    }

    /*------------------------------------------------------------------*
     *                    Scale-to-gray, arbitrary factors              *
     *------------------------------------------------------------------*/
    [Theory]
    [InlineData(0.5f, 32, 32)]
    [InlineData(0.25f, 16, 16)]
    [InlineData(0.125f, 8, 8)]
    [InlineData(0.0625f, 4, 4)]
    public void ScaleToGray_ExactFactorsUseIntegerReduction(float factor, int wd, int hd)
    {
        Pix pix = RandomBinary(1, 64, 64);
        Pix pixd = pix.ScaleToGray(factor);
        Assert.Equal(8, pixd.Depth);
        Assert.Equal(wd, pixd.Width);
        Assert.Equal(hd, pixd.Height);
    }

    [Fact]
    public void ScaleToGray_ExactFactorsMatchIntegerFunctions()
    {
        Pix pix = RandomBinary(2, 96, 96);
        AssertSame(pix.ScaleToGray2(), pix.ScaleToGray(0.5f));
        AssertSame(pix.ScaleToGray4(), pix.ScaleToGray(0.25f));
        AssertSame(pix.ScaleToGray8(), pix.ScaleToGray(0.125f));
        AssertSame(pix.ScaleToGray16(), pix.ScaleToGray(0.0625f));
    }

    [Fact]
    public void ScaleToGray_BinaryPrescalingThenReduction()
    {
        Pix pix = RandomBinary(3, 100, 90);
        float f;

        f = 0.75f;
        AssertSame(pix.ScaleBinary(2.0f * f, 2.0f * f).ScaleToGray2(), pix.ScaleToGray(f));
        f = 0.4f;
        AssertSame(pix.ScaleBinary(3.0f * f, 3.0f * f).ScaleToGray3(), pix.ScaleToGray(f));
        f = 0.3f;
        AssertSame(pix.ScaleBinary(4.0f * f, 4.0f * f).ScaleToGray4(), pix.ScaleToGray(f));
        f = 0.2f;
        AssertSame(pix.ScaleBinary(6.0f * f, 6.0f * f).ScaleToGray6(), pix.ScaleToGray(f));
        f = 0.14f;
        AssertSame(pix.ScaleBinary(8.0f * f, 8.0f * f).ScaleToGray8(), pix.ScaleToGray(f));
        f = 0.09f;
        AssertSame(pix.ScaleBinary(8.0f * f, 8.0f * f).ScaleToGray8(), pix.ScaleToGray(f));
    }

    [Fact]
    public void ScaleToGray_BelowOneSixteenth()
    {
        Pix pix = RandomBinary(4, 160, 160);
        float f = 0.05f;   /* 16 * f = 0.8 >= 0.7: linear interpolation */
        AssertSame(pix.ScaleToGray16().ScaleGrayLI(16.0f * f, 16.0f * f), pix.ScaleToGray(f));
        f = 0.04f;         /* 16 * f = 0.64 < 0.7: smoothing */
        AssertSame(pix.ScaleToGray16().ScaleSmooth(16.0f * f, 16.0f * f), pix.ScaleToGray(f));
    }

    [Fact]
    public void ScaleToGray_Errors()
    {
        Pix pix = Pix.Create(64, 64, 1);
        Assert.Throws<ArgumentException>(() => pix.ScaleToGray(0.0f));
        Assert.Throws<ArgumentException>(() => pix.ScaleToGray(-0.5f));
        Assert.Throws<ArgumentException>(() => pix.ScaleToGray(1.0f));
        Assert.Throws<ArgumentException>(() => pix.ScaleToGray(0.02f));   /* mindest = 1 */
        Assert.Throws<ArgumentException>(() => Pix.Create(64, 64, 8).ScaleToGray(0.5f));
    }

    [Fact]
    public void ScaleToGrayFast_Behavior()
    {
        Pix pix = RandomBinary(5, 128, 128);
        AssertSame(pix.ScaleToGray2(), pix.ScaleToGrayFast(0.5f));
        AssertSame(pix.ScaleToGray3(), pix.ScaleToGrayFast(0.33333f));
        AssertSame(pix.ScaleToGray4(), pix.ScaleToGrayFast(0.25f));
        AssertSame(pix.ScaleToGray8(), pix.ScaleToGrayFast(0.125f));
        AssertSame(pix.ScaleToGray16(), pix.ScaleToGrayFast(0.0625f));

        float f = 0.3f;
        AssertSame(pix.ScaleBinary(2.0f * f, 2.0f * f).ScaleToGray2(), pix.ScaleToGrayFast(f));
        f = 0.03f;
        AssertSame(pix.ScaleToGray16().ScaleSmooth(16.0f * f, 16.0f * f), pix.ScaleToGrayFast(f));

        Assert.Throws<ArgumentException>(() => pix.ScaleToGrayFast(1.0f));
        Assert.Throws<ArgumentException>(() => pix.ScaleToGrayFast(0.0f));
        Assert.Throws<ArgumentException>(() => pix.ScaleToGrayFast(0.01f));
    }

    /*------------------------------------------------------------------*
     *                              Mipmap                              *
     *------------------------------------------------------------------*/
    [Fact]
    public void ScaleMipmap_HandChecked()
    {
        Pix pixs1 = Make(8, 8, 8, (x, y) => 100);
        Pix pixs2 = Make(4, 4, 8, (x, y) => 200);

        Pix pixd = pixs1.ScaleMipmap(pixs2, 1.0f);   /* w1 = 1: only the high-res image */
        Assert.Equal(8, pixd.Width);
        Assert.Equal(8, pixd.Height);
        AssertRow(pixd, 3, 100, 100, 100, 100, 100, 100, 100, 100);

        pixd = pixs1.ScaleMipmap(pixs2, 0.5f);       /* w1 = 0: only the low-res image */
        Assert.Equal(4, pixd.Width);
        Assert.Equal(4, pixd.Height);
        AssertRow(pixd, 0, 200, 200, 200, 200);

        pixd = pixs1.ScaleMipmap(pixs2, 0.75f);      /* halfway between the two */
        Assert.Equal(6, pixd.Width);
        Assert.Equal(6, pixd.Height);
        AssertRow(pixd, 5, 150, 150, 150, 150, 150, 150);
    }

    [Fact]
    public void ScaleMipmap_UsesCorrespondingSourcePixels()
    {
        /* scale = 0.5: ratio = 1, so dest (j, i) is low-res (j, i) */
        Pix pixs1 = Make(8, 8, 8, (x, y) => 0);
        Pix pixs2 = Gray(new[] {
            new uint[] { 1, 2, 3, 4 },
            new uint[] { 5, 6, 7, 8 },
            new uint[] { 9, 10, 11, 12 },
            new uint[] { 13, 14, 15, 16 } });
        Pix pixd = pixs1.ScaleMipmap(pixs2, 0.5f);
        AssertRow(pixd, 0, 1, 2, 3, 4);
        AssertRow(pixd, 3, 13, 14, 15, 16);
    }

    [Fact]
    public void ScaleMipmap_Errors()
    {
        Pix pixs1 = Pix.Create(8, 8, 8);
        Pix pixs2 = Pix.Create(4, 4, 8);
        Assert.Throws<ArgumentException>(() => pixs1.ScaleMipmap(pixs2, 1.1f));
        Assert.Throws<ArgumentException>(() => pixs1.ScaleMipmap(pixs2, 0.4f));
        Assert.Throws<ArgumentException>(() => Pix.Create(8, 8, 1).ScaleMipmap(pixs2, 0.75f));
        Assert.Throws<ArgumentException>(() => pixs1.ScaleMipmap(Pix.Create(4, 4, 1), 0.75f));
        Assert.Throws<ArgumentException>(() => pixs1.ScaleMipmap(Pix.Create(5, 4, 8), 0.75f));
        Assert.Throws<ArgumentException>(() => pixs1.ScaleMipmap(Pix.Create(4, 5, 8), 0.75f));
    }

    [Fact]
    public void ScaleToGrayMipmap_Behavior()
    {
        Pix pix = RandomBinary(6, 64, 64);
        AssertSame(pix.ScaleToGray2(), pix.ScaleToGrayMipmap(0.5f));
        AssertSame(pix.ScaleToGray4(), pix.ScaleToGrayMipmap(0.25f));
        AssertSame(pix.ScaleToGray8(), pix.ScaleToGrayMipmap(0.125f));
        AssertSame(pix.ScaleToGray16(), pix.ScaleToGrayMipmap(0.0625f));

        float f = 0.75f;
        Pix pixd = pix.ScaleToGrayMipmap(f);
        AssertSame(Pix.Convert1To8(null, pix, 255, 0).ScaleMipmap(pix.ScaleToGray2(), f), pixd);
        Assert.Equal(48, pixd.Width);

        f = 0.35f;
        AssertSame(pix.ScaleToGray2().ScaleMipmap(pix.ScaleToGray4(), 2.0f * f), pix.ScaleToGrayMipmap(f));
        f = 0.2f;
        AssertSame(pix.ScaleToGray4().ScaleMipmap(pix.ScaleToGray8(), 4.0f * f), pix.ScaleToGrayMipmap(f));
        f = 0.1f;
        AssertSame(pix.ScaleToGray8().ScaleMipmap(pix.ScaleToGray16(), 8.0f * f), pix.ScaleToGrayMipmap(f));

        Assert.Throws<ArgumentException>(() => pix.ScaleToGrayMipmap(1.0f));
        Assert.Throws<ArgumentException>(() => pix.ScaleToGrayMipmap(0.0f));
        Assert.Throws<ArgumentException>(() => pix.ScaleToGrayMipmap(0.02f));
        Assert.Throws<ArgumentException>(() => Pix.Create(64, 64, 8).ScaleToGrayMipmap(0.5f));
    }

    /*------------------------------------------------------------------*
     *                         Replicated expansion                     *
     *------------------------------------------------------------------*/
    [Theory]
    [InlineData(2, 3u)]
    [InlineData(4, 15u)]
    [InlineData(8, 200u)]
    [InlineData(16, 51234u)]
    [InlineData(32, 0xdeadbeefu)]
    public void ExpandReplicate_HandChecked(int d, uint v)
    {
        uint a = v, b = v - 1, c = v - 2, e = v - 3;
        Pix pix = Make(2, 2, d, (x, y) => y == 0 ? (x == 0 ? a : b) : (x == 0 ? c : e));
        Pix pixd = pix.ExpandReplicate(3);
        Assert.Equal(d, pixd.Depth);
        Assert.Equal(6, pixd.Width);
        Assert.Equal(6, pixd.Height);
        uint m = Mask(d);
        for (int y = 0; y < 3; y++) {
            AssertRow(pixd, y, a & m, a & m, a & m, b & m, b & m, b & m);
            AssertRow(pixd, y + 3, c & m, c & m, c & m, e & m, e & m, e & m);
        }
    }

    [Fact]
    public void ExpandReplicate_WidthNotWordAligned()
    {
        /* 5 x 2, 8 bpp, factor 4: 20 x 8 */
        Pix pix = Make(5, 2, 8, (x, y) => (uint)(x + 10 * y));
        Pix pixd = pix.ExpandReplicate(4);
        Assert.Equal(20, pixd.Width);
        Assert.Equal(8, pixd.Height);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 20; x++)
                Assert.Equal((uint)(x / 4 + 10 * (y / 4)), Naive.GetPixel(pixd, x, y));
    }

    [Fact]
    public void ExpandReplicate_FactorOneCopies()
    {
        Pix pix = Make(3, 3, 8, (x, y) => (uint)(x * 7 + y));
        Pix pixd = pix.ExpandReplicate(1);
        Assert.NotSame(pix, pixd);
        AssertSame(pix, pixd);
    }

    [Fact]
    public void ExpandReplicate_CopiesResolution()
    {
        Pix pix = Make(2, 2, 8, (x, y) => 1);
        pix.XRes = 100;
        pix.YRes = 50;
        Pix pixd = pix.ExpandReplicate(3);
        Assert.Equal(300, pixd.XRes);
        Assert.Equal(150, pixd.YRes);
    }

    [Fact]
    public void ExpandReplicate_Errors()
    {
        Pix pix = Make(2, 2, 8, (x, y) => 1);
        Assert.Throws<ArgumentException>(() => pix.ExpandReplicate(0));
        Assert.Throws<ArgumentException>(() => pix.ExpandReplicate(-2));
    }

    [Fact]
    public void ExpandReplicate_Rgba_ScalesAlpha()
    {
        Pix pix = Pix.Create(2, 2, 32);
        pix.Spp = 4;
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
                Naive.SetPixel(pix, x, y, 0x10203000u | 200u);
        Pix pixd = pix.ExpandReplicate(2);
        Assert.Equal(4, pixd.Width);
        for (int y = 0; y < 4; y++)
            AssertRow(pixd, y, 0x102030c8u, 0x102030c8u, 0x102030c8u, 0x102030c8u);
    }

    /*------------------------------------------------------------------*
     *                    Downscaling using min or max                  *
     *------------------------------------------------------------------*/
    private static Pix MinMaxSource() => Gray(new[] {
        new uint[] { 10, 20, 30, 40 },
        new uint[] { 50, 5, 60, 70 },
        new uint[] { 1, 2, 3, 4 },
        new uint[] { 9, 8, 7, 6 } });

    [Fact]
    public void ScaleGrayMinMax2_HandChecked()
    {
        Pix pix = MinMaxSource();
        Pix pixd = pix.ScaleGrayMinMax2(L.L_CHOOSE_MIN);
        AssertRow(pixd, 0, 5, 30);
        AssertRow(pixd, 1, 1, 3);
        pixd = pix.ScaleGrayMinMax2(L.L_CHOOSE_MAX);
        AssertRow(pixd, 0, 50, 70);
        AssertRow(pixd, 1, 9, 7);
        pixd = pix.ScaleGrayMinMax2(L.L_CHOOSE_MAXDIFF);
        AssertRow(pixd, 0, 45, 40);
        AssertRow(pixd, 1, 8, 4);
    }

    [Fact]
    public void ScaleGrayMinMax2_OddSizeDropsLastLineAndColumn()
    {
        Pix pix = Make(5, 3, 8, (x, y) => (uint)(x * 10 + y));
        Pix pixd = pix.ScaleGrayMinMax2(L.L_CHOOSE_MAX);
        Assert.Equal(2, pixd.Width);
        Assert.Equal(1, pixd.Height);
        AssertRow(pixd, 0, 11, 31);
    }

    [Fact]
    public void ScaleGrayMinMax2_Errors()
    {
        Pix pix = MinMaxSource();
        Assert.Throws<ArgumentException>(() => pix.ScaleGrayMinMax2(0));
        Assert.Throws<ArgumentException>(() => pix.ScaleGrayMinMax2(4));
        Assert.Throws<ArgumentException>(() => Pix.Create(1, 4, 8).ScaleGrayMinMax2(L.L_CHOOSE_MIN));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 1, 8).ScaleGrayMinMax2(L.L_CHOOSE_MIN));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 1).ScaleGrayMinMax2(L.L_CHOOSE_MIN));
    }

    [Fact]
    public void ScaleGrayMinMax_HandChecked()
    {
        Pix pix = MinMaxSource();

        /* 2 x 2 delegates to the special version */
        AssertSame(pix.ScaleGrayMinMax2(L.L_CHOOSE_MIN), pix.ScaleGrayMinMax(2, 2, L.L_CHOOSE_MIN));

        /* one 4 x 4 tile */
        Pix pixd = pix.ScaleGrayMinMax(4, 4, L.L_CHOOSE_MIN);
        Assert.Equal(1, pixd.Width);
        Assert.Equal(1, pixd.Height);
        AssertRow(pixd, 0, 1);
        AssertRow(pix.ScaleGrayMinMax(4, 4, L.L_CHOOSE_MAX), 0, 70);
        AssertRow(pix.ScaleGrayMinMax(4, 4, L.L_CHOOSE_MAXDIFF), 0, 69);

        /* 3 x 3 cells: one cell, the last column and row are dropped */
        AssertRow(pix.ScaleGrayMinMax(3, 3, L.L_CHOOSE_MIN), 0, 1);
        AssertRow(pix.ScaleGrayMinMax(3, 3, L.L_CHOOSE_MAX), 0, 60);
        AssertRow(pix.ScaleGrayMinMax(3, 3, L.L_CHOOSE_MAXDIFF), 0, 59);

        /* xfact larger than the width is limited to a single tile of width ws */
        pixd = pix.ScaleGrayMinMax(10, 1, L.L_CHOOSE_MIN);
        Assert.Equal(1, pixd.Width);
        Assert.Equal(4, pixd.Height);
        Assert.Equal(10u, Naive.GetPixel(pixd, 0, 0));
        Assert.Equal(5u, Naive.GetPixel(pixd, 0, 1));
        Assert.Equal(1u, Naive.GetPixel(pixd, 0, 2));
        Assert.Equal(6u, Naive.GetPixel(pixd, 0, 3));

        /* yfact larger than the height */
        pixd = pix.ScaleGrayMinMax(1, 10, L.L_CHOOSE_MAX);
        Assert.Equal(4, pixd.Width);
        Assert.Equal(1, pixd.Height);
        AssertRow(pixd, 0, 50, 20, 60, 70);

        /* rectangular cells: 4 x 2 */
        pixd = pix.ScaleGrayMinMax(4, 2, L.L_CHOOSE_MAXDIFF);
        Assert.Equal(1, pixd.Width);
        Assert.Equal(2, pixd.Height);
        Assert.Equal(65u, Naive.GetPixel(pixd, 0, 0));   /* 70 - 5 */
        Assert.Equal(8u, Naive.GetPixel(pixd, 0, 1));    /* 9 - 1 */
    }

    [Fact]
    public void ScaleGrayMinMax_Errors()
    {
        Pix pix = MinMaxSource();
        Assert.Throws<ArgumentException>(() => pix.ScaleGrayMinMax(0, 2, L.L_CHOOSE_MIN));
        Assert.Throws<ArgumentException>(() => pix.ScaleGrayMinMax(2, 0, L.L_CHOOSE_MIN));
        Assert.Throws<ArgumentException>(() => pix.ScaleGrayMinMax(2, 3, 7));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 32).ScaleGrayMinMax(2, 3, L.L_CHOOSE_MIN));
    }

    /*------------------------------------------------------------------*
     *                    Downscaling using rank value                  *
     *------------------------------------------------------------------*/
    private static Pix RankSource() => Gray(new[] {
        new uint[] { 10, 20, 9, 1, 5, 5, 7, 7 },
        new uint[] { 30, 40, 1, 9, 9, 1, 7, 7 } });

    [Fact]
    public void ScaleGrayRank2_HandChecked()
    {
        Pix pix = RankSource();
        AssertRow(pix.ScaleGrayRank2(1), 0, 10, 1, 1, 7);
        AssertRow(pix.ScaleGrayRank2(2), 0, 20, 1, 5, 7);
        AssertRow(pix.ScaleGrayRank2(3), 0, 30, 9, 5, 7);
        AssertRow(pix.ScaleGrayRank2(4), 0, 40, 9, 9, 7);
    }

    [Fact]
    public void ScaleGrayRank2_ExtremesMatchMinMax()
    {
        var rng = new Random(11);
        Pix pix = Make(12, 10, 8, (x, y) => (uint)rng.Next(256));
        AssertSame(pix.ScaleGrayMinMax2(L.L_CHOOSE_MIN), pix.ScaleGrayRank2(1));
        AssertSame(pix.ScaleGrayMinMax2(L.L_CHOOSE_MAX), pix.ScaleGrayRank2(4));
    }

    [Fact]
    public void ScaleGrayRank2_MatchesSortedBlocks()
    {
        var rng = new Random(12);
        Pix pix = Make(16, 12, 8, (x, y) => (uint)rng.Next(4) * 60);   /* many ties */
        Pix r2 = pix.ScaleGrayRank2(2);
        Pix r3 = pix.ScaleGrayRank2(3);
        for (int y = 0; y < 6; y++) {
            for (int x = 0; x < 8; x++) {
                uint[] v = {
                    Naive.GetPixel(pix, 2 * x, 2 * y), Naive.GetPixel(pix, 2 * x + 1, 2 * y),
                    Naive.GetPixel(pix, 2 * x, 2 * y + 1), Naive.GetPixel(pix, 2 * x + 1, 2 * y + 1) };
                Array.Sort(v);
                Assert.Equal(v[1], Naive.GetPixel(r2, x, y));
                Assert.Equal(v[2], Naive.GetPixel(r3, x, y));
            }
        }
    }

    [Fact]
    public void ScaleGrayRank2_Errors()
    {
        Pix pix = RankSource();
        Assert.Throws<ArgumentException>(() => pix.ScaleGrayRank2(0));
        Assert.Throws<ArgumentException>(() => pix.ScaleGrayRank2(5));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 1).ScaleGrayRank2(2));
    }

    [Fact]
    public void ScaleGrayRankCascade_Behavior()
    {
        var rng = new Random(13);
        Pix pix = Make(40, 36, 8, (x, y) => (uint)rng.Next(256));

        Pix pixd = pix.ScaleGrayRankCascade(0, 3, 3, 3);   /* level1 <= 0: copy */
        Assert.NotSame(pix, pixd);
        AssertSame(pix, pixd);

        AssertSame(pix.ScaleGrayRank2(2), pix.ScaleGrayRankCascade(2, 0, 3, 3));
        AssertSame(pix.ScaleGrayRank2(2).ScaleGrayRank2(3), pix.ScaleGrayRankCascade(2, 3, 0, 4));
        AssertSame(pix.ScaleGrayRank2(1).ScaleGrayRank2(2).ScaleGrayRank2(3),
                   pix.ScaleGrayRankCascade(1, 2, 3, 0));
        pixd = pix.ScaleGrayRankCascade(4, 3, 2, 1);
        Assert.Equal(2, pixd.Width);   /* 40 -> 20 -> 10 -> 5 -> 2 */
        Assert.Equal(2, pixd.Height);  /* 36 -> 18 -> 9 -> 4 -> 2 */
        AssertSame(pix.ScaleGrayRank2(4).ScaleGrayRank2(3).ScaleGrayRank2(2).ScaleGrayRank2(1), pixd);

        Assert.Throws<ArgumentException>(() => pix.ScaleGrayRankCascade(5, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => pix.ScaleGrayRankCascade(1, 1, 1, 5));
        Assert.Throws<ArgumentException>(() => Pix.Create(8, 8, 1).ScaleGrayRankCascade(1, 1, 0, 0));
    }

    /*------------------------------------------------------------------*
     *                          Alpha transfer                          *
     *------------------------------------------------------------------*/
    [Fact]
    public void ScaleAndTransferAlpha_UnitScaleCopiesAlpha()
    {
        Pix pixs = Pix.Create(3, 2, 32);
        pixs.Spp = 4;
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 3; x++)
                Naive.SetPixel(pixs, x, y, 0x01020300u | (uint)(10 * (x + 1) + y));
        Pix pixd = Make(3, 2, 32, (x, y) => 0xaabbcc00u);
        pixd.ScaleAndTransferAlpha(pixs, 1.0f, 1.0f);
        AssertRow(pixd, 0, 0xaabbcc0au, 0xaabbcc14u, 0xaabbcc1eu);
        AssertRow(pixd, 1, 0xaabbcc0bu, 0xaabbcc15u, 0xaabbcc1fu);
    }

    [Fact]
    public void ScaleAndTransferAlpha_Errors()
    {
        Pix rgba = Pix.Create(4, 4, 32);
        rgba.Spp = 4;
        Pix rgb = Pix.Create(4, 4, 32);
        Assert.Throws<ArgumentException>(() => rgba.ScaleAndTransferAlpha(rgb, 1.0f, 1.0f));
        Assert.Throws<ArgumentException>(() => rgba.ScaleAndTransferAlpha(Pix.Create(4, 4, 8), 1.0f, 1.0f));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).ScaleAndTransferAlpha(rgba, 1.0f, 1.0f));
    }

    [Fact]
    public void ScaleWithAlpha_FadingBorderAtUnitScale()
    {
        Pix pixs = Make(20, 20, 32, (x, y) => 0x40506000u);
        Pix pixd = pixs.ScaleWithAlpha(1.0f, 1.0f, null, 1.0f);
        Assert.Equal(32, pixd.Depth);
        Assert.Equal(20, pixd.Width);
        Assert.Equal(20, pixd.Height);
        Assert.Equal(4, pixd.Spp);
        Assert.Equal(3, pixs.Spp);   /* source is not modified */
        Assert.Equal(0x40506000u, Naive.GetPixel(pixd, 7, 7) & 0xffffff00u);
        Assert.Equal(0u, Naive.GetPixel(pixd, 0, 0) & 0xffu);       /* outer ring: transparent */
        Assert.Equal(0u, Naive.GetPixel(pixd, 10, 19) & 0xffu);
        Assert.Equal(127u, Naive.GetPixel(pixd, 1, 5) & 0xffu);     /* (int)(255 * 0.5) */
        Assert.Equal(127u, Naive.GetPixel(pixd, 18, 18) & 0xffu);
        Assert.Equal(255u, Naive.GetPixel(pixd, 10, 10) & 0xffu);   /* interior: opaque */
    }

    [Fact]
    public void ScaleWithAlpha_FractAndGrayMask()
    {
        Pix pixs = Make(20, 20, 32, (x, y) => 0x40506000u);
        Pix pixd = pixs.ScaleWithAlpha(1.0f, 1.0f, null, 0.5f);
        Assert.Equal(127u, Naive.GetPixel(pixd, 10, 10) & 0xffu);   /* (int)(255 * 0.5) */
        Assert.Equal(0u, Naive.GetPixel(pixd, 0, 0) & 0xffu);
        Assert.Equal(63u, Naive.GetPixel(pixd, 1, 1) & 0xffu);      /* (int)(255 * 0.5 * 0.5) */

        Pix pixg = Make(20, 20, 8, (x, y) => 80);   /* fract is ignored when pixg is given */
        pixd = pixs.ScaleWithAlpha(1.0f, 1.0f, pixg, 0.1f);
        Assert.Equal(80u, Naive.GetPixel(pixd, 10, 10) & 0xffu);
        Assert.Equal(0u, Naive.GetPixel(pixd, 0, 0) & 0xffu);
        Assert.Equal(12u, Naive.GetPixel(pixd, 1, 1) & 0xffu);      /* (int)(255 * 0.1 * 0.5) */
    }

    [Fact]
    public void ScaleWithAlpha_ScaledSizeAndErrors()
    {
        Pix pixs = Make(20, 20, 32, (x, y) => 0x40506000u);
        Pix pixd = pixs.ScaleWithAlpha(2.0f, 2.0f, null, 1.0f);
        Assert.Equal(40, pixd.Width);
        Assert.Equal(40, pixd.Height);
        Assert.Equal(4, pixd.Spp);
        Assert.Equal(255u, Naive.GetPixel(pixd, 20, 20) & 0xffu);

        Assert.Throws<ArgumentException>(() => pixs.ScaleWithAlpha(0.0f, 1.0f, null, 1.0f));
        Assert.Throws<ArgumentException>(() => pixs.ScaleWithAlpha(1.0f, -1.0f, null, 1.0f));
        Assert.Throws<ArgumentException>(() => Pix.Create(20, 20, 8).ScaleWithAlpha(1.0f, 1.0f, null, 1.0f));
    }
}
