using System;
using Leptonica;
using Naive = Leptonica.Managed.Tests.Rop.NaiveRop;

namespace Leptonica.Managed.Tests.ScaleTests;

/// <summary>
/// Tests of scale1.c.  Small images have hand-computed expectations; the size tests check the
/// (int)(scale * w + 0.5) rule for many factors and depths.  Functions that need callees from
/// files that are not ported yet (pixConvertTo8Or32, pixUnsharpMasking, the dithering and
/// thresholding line functions) are covered only on the paths that do not reach them.
/// </summary>
public class Scale1Tests
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

    private static Pix Random(Random rng, int w, int h, int d) =>
        Make(w, h, d, (x, y) => (uint)rng.NextInt64(0, 1L << 32));

    private static uint Rgb(int r, int g, int b)
    {
        Pix.ComposeRGBPixel(r, g, b, out uint p);
        return p;
    }

    private static int Dim(float scale, int n) => (int)(scale * (float)n + 0.5);

    /*------------------------------------------------------------------*
     *                        Linear interpolation                      *
     *------------------------------------------------------------------*/
    [Fact]
    public void GrayLI_2x_HandChecked()
    {
        Pix pixs = Gray(new[] { new uint[] { 0, 100 }, new uint[] { 200, 40 } });
        Pix pixd = pixs.ScaleGrayLI(2.0f, 2.0f);
        Assert.Equal(4, pixd.Width);
        Assert.Equal(4, pixd.Height);
        AssertRow(pixd, 0, 0, 50, 100, 100);
        AssertRow(pixd, 1, 100, 85, 70, 70);
        AssertRow(pixd, 2, 200, 120, 40, 40);
        AssertRow(pixd, 3, 200, 120, 40, 40);
    }

    [Fact]
    public void GrayLI_2x_WideImageUsesUnrolledWords()
    {
        // 11 pixels wide: the unrolled word loop runs for j = 0..3, 4..7 and the
        // byte loop finishes the rest.  Compare with the per-pixel formula.
        Pix pixs = Make(11, 3, 8, (x, y) => (uint)((x * 37 + y * 91 + 5) & 0xff));
        Pix pixd = pixs.ScaleGray2xLI();
        Assert.Equal(22, pixd.Width);
        Assert.Equal(6, pixd.Height);
        for (int i = 0; i < 3; i++) {
            bool last = i == 2;
            for (int j = 0; j < 11; j++) {
                uint s1 = Naive.GetPixel(pixs, j, i);
                uint s2 = Naive.GetPixel(pixs, Math.Min(j + 1, 10), i);
                uint s3 = Naive.GetPixel(pixs, j, last ? i : i + 1);
                uint s4 = Naive.GetPixel(pixs, Math.Min(j + 1, 10), last ? i : i + 1);
                bool lastx = j == 10;
                Assert.Equal(s1, Naive.GetPixel(pixd, 2 * j, 2 * i));
                Assert.Equal(lastx ? s1 : (s1 + s2) / 2, Naive.GetPixel(pixd, 2 * j + 1, 2 * i));
                Assert.Equal((s1 + s3) / 2, Naive.GetPixel(pixd, 2 * j, 2 * i + 1));
                Assert.Equal(lastx ? (s1 + s3) / 2 : (s1 + s2 + s3 + s4) / 4,
                             Naive.GetPixel(pixd, 2 * j + 1, 2 * i + 1));
            }
        }
    }

    [Fact]
    public void GrayLI_4x_HandChecked()
    {
        Pix pixs = Gray(new[] { new uint[] { 0, 100 }, new uint[] { 200, 40 } });
        Pix pixd = pixs.ScaleGrayLI(4.0f, 4.0f);
        Assert.Equal(8, pixd.Width);
        Assert.Equal(8, pixd.Height);
        AssertRow(pixd, 0, 0, 25, 50, 75, 100, 100, 100, 100);
        AssertRow(pixd, 1, 50, 58, 67, 76, 85, 85, 85, 85);
        AssertRow(pixd, 2, 100, 92, 85, 77, 70, 70, 70, 70);
        AssertRow(pixd, 3, 150, 126, 102, 78, 55, 55, 55, 55);
        for (int y = 4; y < 8; y++)
            AssertRow(pixd, y, 200, 160, 120, 80, 40, 40, 40, 40);
    }

    [Fact]
    public void GrayLI_General_HandChecked()
    {
        // scale 1.5 on a 2x1 image: scx = 16 * 2 / 3 = 10.67 (float); dest x = 0,1,2 map to
        // xpm = 0, 10, 21 -> (xp, xf) = (0, 0), (0, 10), (1, 5).  One line: bottom case.
        Pix pixs = Gray(new[] { new uint[] { 0, 160 } });
        Pix pixd = pixs.ScaleGrayLI(1.5f, 1.0f);
        Assert.Equal(3, pixd.Width);
        Assert.Equal(1, pixd.Height);
        // x = 1: v00 = 6*16*0, v10 = 10*16*160 = 25600 -> (25600 + 128) / 256 = 100
        AssertRow(pixd, 0, 0, 100, 160);
    }

    [Fact]
    public void ColorLI_2x_HandChecked()
    {
        Pix pixs = Make(2, 2, 32, (x, y) => {
            int[,] r = { { 0, 100 }, { 200, 40 } };
            int[,] g = { { 255, 155 }, { 55, 215 } };
            return Rgb(r[y, x], g[y, x], 7);
        });
        Pix pixd = pixs.ScaleColorLI(2.0f, 2.0f);
        Assert.Equal(4, pixd.Width);
        Assert.Equal(4, pixd.Height);
        AssertRow(pixd, 0, Rgb(0, 255, 7), Rgb(50, 205, 7), Rgb(100, 155, 7), Rgb(100, 155, 7));
        AssertRow(pixd, 1, Rgb(100, 155, 7), Rgb(85, 170, 7), Rgb(70, 185, 7), Rgb(70, 185, 7));
        AssertRow(pixd, 2, Rgb(200, 55, 7), Rgb(120, 135, 7), Rgb(40, 215, 7), Rgb(40, 215, 7));
        AssertRow(pixd, 3, Rgb(200, 55, 7), Rgb(120, 135, 7), Rgb(40, 215, 7), Rgb(40, 215, 7));
    }

    [Fact]
    public void ColorLI_4x_MatchesGray4xPerComponent()
    {
        Random rng = new(4);
        Pix pixs = Random(rng, 5, 4, 32);
        Pix pixd = pixs.ScaleColorLI(4.0f, 4.0f);
        Pix expected = Pix.CreateRGBImage(
            pixs.GetRGBComponent(L.COLOR_RED).ScaleGray4xLI(),
            pixs.GetRGBComponent(L.COLOR_GREEN).ScaleGray4xLI(),
            pixs.GetRGBComponent(L.COLOR_BLUE).ScaleGray4xLI());
        AssertSame(expected, pixd);
    }

    [Fact]
    public void ColorLI_General_FlatImageStaysFlat()
    {
        Pix pixs = Make(9, 7, 32, (x, y) => Rgb(10, 130, 250));
        foreach (float s in new[] { 0.75f, 1.13f, 1.7f, 3.3f }) {
            Pix pixd = pixs.ScaleColorLI(s, s);
            for (int y = 0; y < pixd.Height; y++)
                for (int x = 0; x < pixd.Width; x++)
                    Assert.Equal(Rgb(10, 130, 250), Naive.GetPixel(pixd, x, y));
        }
    }

    [Fact]
    public void LI_IdentityAtScaleOne()
    {
        Random rng = new(1);
        Pix gray = Random(rng, 13, 5, 8);
        Pix rgb = Random(rng, 7, 6, 32);
        Pix g = gray.ScaleGrayLI(1.0f, 1.0f);
        Pix c = rgb.ScaleColorLI(1.0f, 1.0f);
        Assert.NotSame(gray, g);
        Assert.NotSame(rgb, c);
        AssertSame(gray, g);
        AssertSame(rgb, c);
    }

    [Fact]
    public void LI_SizesForManyFactors()
    {
        Pix gray = Pix.Create(13, 9, 8);
        Pix rgb = Pix.Create(13, 9, 32);
        foreach (float sx in new[] { 0.75f, 0.85f, 1.0f, 1.3f, 1.5f, 2.0f, 2.5f, 3.0f, 4.0f, 5.5f }) {
            foreach (float sy in new[] { 0.75f, 1.0f, 1.9f, 2.0f, 4.0f }) {
                Pix g = gray.ScaleGrayLI(sx, sy);
                Pix c = rgb.ScaleColorLI(sx, sy);
                int wd = (sx == 2.0f && sy == 2.0f) ? 26 : (sx == 4.0f && sy == 4.0f) ? 52 : Dim(sx, 13);
                int hd = (sx == 2.0f && sy == 2.0f) ? 18 : (sx == 4.0f && sy == 4.0f) ? 36 : Dim(sy, 9);
                Assert.Equal((wd, hd), (g.Width, g.Height));
                Assert.Equal((wd, hd), (c.Width, c.Height));
                Assert.Equal(8, g.Depth);
                Assert.Equal(32, c.Depth);
            }
        }
    }

    [Fact]
    public void LI_RejectsWrongDepth()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 32).ScaleGrayLI(2.0f, 2.0f));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).ScaleColorLI(2.0f, 2.0f));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 1).ScaleLI(2.0f, 2.0f));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).ScaleGray2xLI().ScaleColor2xLI());
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).ScaleColor4xLI());
    }

    [Fact]
    public void LI_Resolution()
    {
        Pix pixs = Pix.Create(10, 10, 8);
        pixs.XRes = 300;
        pixs.YRes = 200;
        Pix pixd = pixs.ScaleGrayLI(2.0f, 2.0f);
        Assert.Equal(600, pixd.XRes);
        Assert.Equal(400, pixd.YRes);
        pixd = pixs.ScaleGrayLI(1.5f, 1.5f);
        Assert.Equal(450, pixd.XRes);
        Assert.Equal(300, pixd.YRes);
    }

    /*------------------------------------------------------------------*
     *                          Closest pixel sampling                  *
     *------------------------------------------------------------------*/
    [Fact]
    public void Sampling_2x_HandChecked()
    {
        Pix pixs = Gray(new[] { new uint[] { 1, 2 }, new uint[] { 3, 4 } });
        Pix pixd = pixs.ScaleBySampling(2.0f, 2.0f);
        AssertRow(pixd, 0, 1, 2, 2, 2);
        AssertRow(pixd, 1, 3, 4, 4, 4);
        AssertRow(pixd, 2, 3, 4, 4, 4);
        AssertRow(pixd, 3, 3, 4, 4, 4);

        // no shift: sampled columns are (int)(0.5 * j) = 0, 0, 1, 1
        pixd = pixs.ScaleBySamplingWithShift(2.0f, 2.0f, 0.0f, 0.0f);
        AssertRow(pixd, 0, 1, 1, 2, 2);
        AssertRow(pixd, 1, 1, 1, 2, 2);
        AssertRow(pixd, 2, 3, 3, 4, 4);
        AssertRow(pixd, 3, 3, 3, 4, 4);
    }

    [Fact]
    public void Sampling_Half_HandChecked()
    {
        Pix pixs = Make(4, 4, 8, (x, y) => (uint)(y * 4 + x));
        Pix pixd = pixs.ScaleBySampling(0.5f, 0.5f);
        AssertRow(pixd, 0, 0, 2);
        AssertRow(pixd, 1, 8, 10);
        pixd = pixs.ScaleByIntSampling(2);
        AssertRow(pixd, 0, 0, 2);
        AssertRow(pixd, 1, 8, 10);
        pixd = pixs.ScaleBySamplingWithShift(0.5f, 0.5f, 0.0f, 0.0f);
        AssertRow(pixd, 0, 0, 2);
        AssertRow(pixd, 1, 8, 10);
    }

    [Fact]
    public void Sampling_AllDepthsMatchFormula()
    {
        Random rng = new(5);
        foreach (int d in new[] { 2, 4, 8, 16, 32 }) {
            Pix pixs = Random(rng, 11, 7, d);
            foreach (float s in new[] { 0.3f, 0.5f, 0.77f, 1.0f, 1.4f, 2.0f, 3.7f }) {
                foreach (float shift in new[] { 0.0f, 0.5f }) {
                    Pix pixd = pixs.ScaleBySamplingWithShift(s, s * 0.9f, shift, shift);
                    int wd = Dim(s, 11), hd = Dim(s * 0.9f, 7);
                    if (s == 1.0f && s * 0.9f == 1.0f) continue;
                    Assert.Equal((wd, hd, d), (pixd.Width, pixd.Height, d));
                    float wr = 11f / wd, hr = 7f / hd;
                    for (int y = 0; y < hd; y++)
                        for (int x = 0; x < wd; x++) {
                            int sx = Math.Min((int)(wr * x + shift), 10);
                            int sy = Math.Min((int)(hr * y + shift), 6);
                            Assert.True(Naive.GetPixel(pixs, sx, sy) == Naive.GetPixel(pixd, x, y),
                                $"d={d} s={s} shift={shift} ({x},{y})");
                        }
                }
            }
        }
    }

    [Fact]
    public void Sampling_IdentityAndErrors()
    {
        Random rng = new(6);
        foreach (int d in new[] { 1, 2, 4, 8, 16, 32 }) {
            Pix pixs = Random(rng, 9, 5, d);
            Pix pixd = pixs.ScaleBySampling(1.0f, 1.0f);
            Assert.NotSame(pixs, pixd);
            AssertSame(pixs, pixd);
            AssertSame(pixs, pixs.ScaleByIntSampling(1));
        }
        Pix pix = Pix.Create(4, 4, 8);
        Assert.Throws<ArgumentException>(() => pix.ScaleBySampling(0.0f, 1.0f));
        Assert.Throws<ArgumentException>(() => pix.ScaleBySamplingWithShift(2.0f, 2.0f, 0.25f, 0.0f));
        Assert.Throws<ArgumentException>(() => pix.ScaleBySamplingWithShift(2.0f, 2.0f, 0.0f, 1.0f));
        Assert.Throws<ArgumentException>(() => pix.ScaleBySamplingToSize(0, 0));
    }

    [Fact]
    public void Sampling_KeepsColormapAndResolution()
    {
        Pix pixs = Pix.CreateWithCmap(4, 4, 4, L.L_SET_WHITE);
        pixs.XRes = 300;
        pixs.YRes = 300;
        Pix pixd = pixs.ScaleBySampling(0.5f, 0.5f);
        Assert.NotNull(pixd.Colormap);
        Assert.Equal(150, pixd.XRes);
    }

    [Fact]
    public void Sampling_ToSize()
    {
        Pix pixs = Pix.Create(4, 2, 8);
        Pix pixd = pixs.ScaleBySamplingToSize(8, 0);
        Assert.Equal((8, 4), (pixd.Width, pixd.Height));
        pixd = pixs.ScaleBySamplingToSize(0, 6);
        Assert.Equal((12, 6), (pixd.Width, pixd.Height));
        pixd = pixs.ScaleBySamplingToSize(3, 5);
        Assert.Equal((3, 5), (pixd.Width, pixd.Height));
    }

    /*------------------------------------------------------------------*
     *                             Binary scaling                       *
     *------------------------------------------------------------------*/
    [Fact]
    public void Binary_2x_HandChecked()
    {
        Pix pixs = Make(2, 2, 1, (x, y) => (uint)(x == y ? 1 : 0));
        Pix pixd = pixs.ScaleBinary(2.0f, 2.0f);
        AssertRow(pixd, 0, 1, 0, 0, 0);
        AssertRow(pixd, 1, 0, 1, 1, 1);
        AssertRow(pixd, 2, 0, 1, 1, 1);
        AssertRow(pixd, 3, 0, 1, 1, 1);

        pixd = pixs.ScaleBinaryWithShift(2.0f, 2.0f, 0.0f, 0.0f);
        AssertRow(pixd, 0, 1, 1, 0, 0);
        AssertRow(pixd, 1, 1, 1, 0, 0);
        AssertRow(pixd, 2, 0, 0, 1, 1);
        AssertRow(pixd, 3, 0, 0, 1, 1);
    }

    [Fact]
    public void Binary_MatchesFormulaForManyFactors()
    {
        Random rng = new(7);
        Pix pixs = Random(rng, 37, 21, 1);
        foreach (float sx in new[] { 0.1f, 0.33f, 0.5f, 0.9f, 1.7f, 2.0f, 3.0f, 4.4f }) {
            foreach (float sy in new[] { 0.25f, 0.6f, 1.5f, 5.0f }) {
                Pix pixd = pixs.ScaleBinary(sx, sy);
                int wd = Dim(sx, 37), hd = Dim(sy, 21);
                Assert.Equal((wd, hd), (pixd.Width, pixd.Height));
                float wr = 37f / wd, hr = 21f / hd;
                for (int y = 0; y < hd; y++)
                    for (int x = 0; x < wd; x++) {
                        int px = Math.Min((int)(wr * x + 0.5f), 36);
                        int py = Math.Min((int)(hr * y + 0.5f), 20);
                        Assert.True(Naive.GetPixel(pixs, px, py) == Naive.GetPixel(pixd, x, y),
                            $"sx={sx} sy={sy} ({x},{y})");
                    }
            }
        }
    }

    [Fact]
    public void Binary_IdentityAndErrors()
    {
        Random rng = new(8);
        Pix pixs = Random(rng, 33, 4, 1);
        AssertSame(pixs, pixs.ScaleBinary(1.0f, 1.0f));
        Assert.Throws<ArgumentException>(() => pixs.ScaleBinary(0.0f, 1.0f));
        Assert.Throws<ArgumentException>(() => pixs.ScaleBinaryWithShift(2.0f, 2.0f, 0.3f, 0.0f));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).ScaleBinary(2.0f, 2.0f));
    }

    [Fact]
    public void Scale_OneBppDispatchesToBinary()
    {
        Random rng = new(9);
        Pix pixs = Random(rng, 20, 20, 1);
        AssertSame(pixs.ScaleBinary(2.5f, 0.4f), pixs.Scale(2.5f, 0.4f));
        AssertSame(pixs.ScaleBinary(0.5f, 0.5f), pixs.ScaleGeneral(0.5f, 0.5f, 0.2f, 1));
        AssertSame(pixs, pixs.Scale(1.0f, 1.0f));
        Assert.Throws<ArgumentException>(() => pixs.ScaleGeneral(-1.0f, 1.0f, 0.0f, 0));
    }

    [Fact]
    public void ScaleToSize_OneBpp()
    {
        Pix pixs = Pix.Create(20, 10, 1);
        Assert.Equal((40, 20), Size(pixs.ScaleToSize(40, 0)));
        Assert.Equal((60, 30), Size(pixs.ScaleToSize(0, 30)));
        Assert.Equal((7, 9), Size(pixs.ScaleToSize(7, 9)));
        Assert.Equal((25, 12), Size(pixs.ScaleToSizeRel(5, 2)));
        AssertSame(pixs, pixs.ScaleToSizeRel(0, 0));
        Assert.Throws<ArgumentException>(() => pixs.ScaleToSize(0, 0));
        Assert.Throws<ArgumentException>(() => pixs.ScaleToSizeRel(-20, 0));

        pixs.XRes = 150;
        Pix pixd = pixs.ScaleToResolution(300.0f, 0.0f, out float fact);
        Assert.Equal(2.0f, fact);
        Assert.Equal((40, 20), Size(pixd));
        Pix pix2 = Pix.Create(20, 10, 1);
        pixd = pix2.ScaleToResolution(300.0f, 150.0f, out fact);
        Assert.Equal(2.0f, fact);
        Assert.Equal((40, 20), Size(pixd));
        pixd = pix2.ScaleToResolution(300.0f, 0.0f, out fact);
        Assert.Equal(1.0f, fact);
        Assert.Equal((20, 10), Size(pixd));
    }

    private static (int, int) Size(Pix pix) => (pix.Width, pix.Height);

    /*------------------------------------------------------------------*
     *                        Fast integer subsampling                  *
     *------------------------------------------------------------------*/
    [Fact]
    public void RGBToGrayFast_HandChecked()
    {
        Pix pixs = Make(5, 4, 32, (x, y) => Rgb(x * 10 + y, 100 + x, 200 + y));
        Pix pixd = pixs.ScaleRGBToGrayFast(2, L.COLOR_RED);
        Assert.Equal((2, 2), Size(pixd));
        AssertRow(pixd, 0, 0, 20);
        AssertRow(pixd, 1, 2, 22);
        pixd = pixs.ScaleRGBToGrayFast(2, L.COLOR_GREEN);
        AssertRow(pixd, 0, 100, 102);
        pixd = pixs.ScaleRGBToGrayFast(2, L.COLOR_BLUE);
        AssertRow(pixd, 0, 200, 200);
        AssertRow(pixd, 1, 202, 202);
        Assert.Equal((5, 4), Size(pixs.ScaleRGBToGrayFast(1, L.COLOR_RED)));
        Assert.Throws<ArgumentException>(() => pixs.ScaleRGBToGrayFast(0, L.COLOR_RED));
        Assert.Throws<ArgumentException>(() => pixs.ScaleRGBToGrayFast(2, L.L_ALPHA_CHANNEL));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).ScaleRGBToGrayFast(2, L.COLOR_RED));
    }

    [Fact]
    public void RGBToBinaryFast_HandChecked()
    {
        Pix pixs = Make(4, 2, 32, (x, y) => Rgb(0, x * 50, 0));
        Pix pixd = pixs.ScaleRGBToBinaryFast(2, 60);
        Assert.Equal((2, 1), Size(pixd));
        AssertRow(pixd, 0, 1, 0);    // green 0 < 60 -> ON; green 100 -> OFF
        pixd = pixs.ScaleRGBToBinaryFast(1, 100);
        AssertRow(pixd, 0, 1, 1, 0, 0);
    }

    [Fact]
    public void GrayToBinaryFast_HandChecked()
    {
        Pix pixs = Make(6, 4, 8, (x, y) => (uint)(x * 30 + y));
        Pix pixd = pixs.ScaleGrayToBinaryFast(2, 70);
        Assert.Equal((3, 2), Size(pixd));
        AssertRow(pixd, 0, 1, 1, 0);   // 0, 60, 120 vs 70
        AssertRow(pixd, 1, 1, 1, 0);   // 2, 62, 122
        Assert.Throws<ArgumentException>(() => pixs.ScaleGrayToBinaryFast(0, 70));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 32).ScaleGrayToBinaryFast(2, 70));
    }

    /*------------------------------------------------------------------*
     *                            Area mapping                          *
     *------------------------------------------------------------------*/
    [Fact]
    public void AreaMap2_HandChecked()
    {
        Pix pixs = Gray(new[] { new uint[] { 10, 20, 30, 40, 7 }, new uint[] { 30, 40, 50, 60, 9 } });
        Pix pixd = pixs.ScaleAreaMap2();
        Assert.Equal((2, 1), Size(pixd));
        AssertRow(pixd, 0, 25, 45);

        Pix rgb = Make(2, 2, 32, (x, y) => Rgb(10 + x + y, 100 * x + y, 255));
        Pix pixc = rgb.ScaleAreaMap2();
        // r: (10 + 11 + 11 + 12) >> 2 = 11; g: (0 + 100 + 1 + 101) >> 2 = 50; b = 255
        AssertRow(pixc, 0, Rgb(11, 50, 255));
    }

    [Fact]
    public void AreaMap_PowersOfTwoUseCascade()
    {
        Random rng = new(10);
        Pix pixs = Random(rng, 33, 32, 8);
        AssertSame(pixs.ScaleAreaMap2(), pixs.ScaleAreaMap(0.5f, 0.5f));
        AssertSame(pixs.ScaleAreaMap2().ScaleAreaMap2(), pixs.ScaleAreaMap(0.25f, 0.25f));
        AssertSame(pixs.ScaleAreaMap2().ScaleAreaMap2().ScaleAreaMap2(), pixs.ScaleAreaMap(0.125f, 0.125f));
        Pix rgb = Random(rng, 40, 34, 32);
        AssertSame(rgb.ScaleAreaMap2().ScaleAreaMap2().ScaleAreaMap2().ScaleAreaMap2(),
                   rgb.ScaleAreaMap(0.0625f, 0.0625f));
    }

    [Fact]
    public void AreaMap_FlatImageStaysFlat()
    {
        Pix gray = Make(40, 30, 8, (x, y) => 77);
        Pix rgb = Make(40, 30, 32, (x, y) => Rgb(3, 99, 201));
        foreach (float s in new[] { 0.05f, 0.1f, 0.33f, 0.45f, 0.55f, 0.6f }) {
            Pix g = gray.ScaleAreaMap(s, s * 1.1f);
            Pix c = rgb.ScaleAreaMap(s * 1.1f, s);
            for (int y = 0; y < g.Height; y++)
                for (int x = 0; x < g.Width; x++)
                    Assert.Equal(77u, Naive.GetPixel(g, x, y));
            for (int y = 0; y < c.Height; y++)
                for (int x = 0; x < c.Width; x++)
                    Assert.Equal(Rgb(3, 99, 201), Naive.GetPixel(c, x, y));
        }
    }

    [Fact]
    public void AreaMap_NearEdgeCopiesSourcePixel()
    {
        // 6x3 image scaled to 2x1: scy = 16 * 3 / 1 = 48, so the lower edge of the
        // dest pixel (yl = 48, ylp = 3) is beyond hm2 = 1 and every dest pixel copies
        // the src pixel at its UL corner: (0, 0) and (xu >> 4 = 3, 0).
        Pix pixs = Make(6, 3, 8, (x, y) => (uint)(x < 3 ? 30 + 3 * y : 90));
        Pix pixd = pixs.ScaleAreaMap(2.0f / 6.0f, 1.0f / 3.0f);
        Assert.Equal((2, 1), Size(pixd));
        AssertRow(pixd, 0, 30, 90);
    }

    [Fact]
    public void AreaMap_SizesForManyFactors()
    {
        Pix gray = Pix.Create(97, 61, 8);
        Pix rgb = Pix.Create(97, 61, 32);
        foreach (float sx in new[] { 0.03f, 0.05f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.69f }) {
            foreach (float sy in new[] { 0.03f, 0.15f, 0.5f, 0.66f }) {
                Pix g = gray.ScaleAreaMap(sx, sy);
                Pix c = rgb.ScaleAreaMap(sx, sy);
                int wd = Dim(sx, 97), hd = Dim(sy, 61);
                if (sx == 0.5f && sy == 0.5f) { wd = 48; hd = 30; }
                Assert.Equal((wd, hd), Size(g));
                Assert.Equal((wd, hd), Size(c));
            }
        }
    }

    [Fact]
    public void AreaMap_ToSize()
    {
        Pix pixs = Pix.Create(100, 50, 8);
        Assert.Equal((30, 15), Size(pixs.ScaleAreaMapToSize(30, 0)));
        Assert.Equal((40, 20), Size(pixs.ScaleAreaMapToSize(0, 20)));
        Assert.Equal((20, 30), Size(pixs.ScaleAreaMapToSize(20, 30)));
        Assert.Throws<ArgumentException>(() => pixs.ScaleAreaMapToSize(0, 0));
    }

    /*------------------------------------------------------------------*
     *                              Smoothing                           *
     *------------------------------------------------------------------*/
    [Fact]
    public void RGBToGray2_HandChecked()
    {
        Pix pixs = Make(5, 3, 32, (x, y) => Rgb(100, 200, 60));
        Pix pixd = pixs.ScaleRGBToGray2(0.25f, 0.5f, 0.25f);
        Assert.Equal((2, 1), Size(pixd));
        AssertRow(pixd, 0, 140, 140);
        Assert.Throws<ArgumentException>(() => pixs.ScaleRGBToGray2(0.5f, 0.5f, 0.5f));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).ScaleRGBToGray2(0.25f, 0.5f, 0.25f));
    }
}
