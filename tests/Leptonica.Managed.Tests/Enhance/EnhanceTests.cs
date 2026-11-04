using System;
using Leptonica;
using Naive = Leptonica.Managed.Tests.Rop.NaiveRop;

namespace Leptonica.Managed.Tests.Enhance;

/// <summary>
/// Tests of enhance.c: the TRC tables are compared with a direct computation, and the pixel
/// operations with plain per-pixel loops on small random images.  The functions that need
/// colorspace.c (hue, saturation, brightness) or pixacc.c (unsharp masking with half-width
/// above 2) are not covered here, because those files are not ported yet.
/// </summary>
public class EnhanceTests
{
    private static Pix MakeGray(Random rng, int w, int h)
    {
        Pix pix = Pix.Create(w, h, 8);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                Naive.SetPixel(pix, j, i, (uint)rng.Next(256));
        return pix;
    }

    private static Pix MakeRgb(Random rng, int w, int h)
    {
        Pix pix = Pix.Create(w, h, 32);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                Naive.SetPixel(pix, j, i, (uint)rng.NextInt64(0, 0x1_0000_0000L) & 0xffffff00u);
        return pix;
    }

    private static void AssertSame(Pix expected, Pix actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Depth, actual.Depth);
        for (int i = 0; i < expected.Height; i++)
            for (int j = 0; j < expected.Width; j++)
                Assert.True(Naive.GetPixel(expected, j, i) == Naive.GetPixel(actual, j, i),
                            $"differ at ({j},{i}): {Naive.GetPixel(expected, j, i)} vs {Naive.GetPixel(actual, j, i)}");
    }

    private static int[] Table(Numa na)
    {
        Assert.Equal(256, na.Count);
        int[] tab = new int[256];
        for (int i = 0; i < 256; i++)
            tab[i] = na.GetIValue(i);
        return tab;
    }

    /*------------------------------ TRC tables -----------------------------*/

    [Theory]
    [InlineData(0.5f, 0, 255)]
    [InlineData(1.0f, 0, 255)]
    [InlineData(2.2f, 0, 255)]
    [InlineData(1.8f, 50, 200)]
    [InlineData(0.7f, -100, 255)]
    [InlineData(1.3f, 0, 400)]
    [InlineData(1.0f, 30, 140)]
    public void GammaTRC_MatchesDirectComputation(float gamma, int minval, int maxval)
    {
        int[] tab = Table(Numa.GammaTRC(gamma, minval, maxval));
        float invgamma = (float)(1.0 / gamma);
        for (int i = 0; i < 256; i++) {
            int expected;
            if (i < minval)
                expected = 0;
            else if (i > maxval)
                expected = 255;
            else {
                float x = (float)(i - minval) / (float)(maxval - minval);
                expected = (int)(255.0 * MathF.Pow(x, invgamma) + 0.5);
                expected = Math.Clamp(expected, 0, 255);
            }
            Assert.True(expected == tab[i], $"i = {i}: {expected} vs {tab[i]}");
        }
    }

    [Fact]
    public void GammaTRC_IdentityIsLinear()
    {
        int[] tab = Table(Numa.GammaTRC(1.0f, 0, 255));
        for (int i = 0; i < 256; i++)
            Assert.Equal(i, tab[i]);
    }

    [Fact]
    public void GammaTRC_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Numa.GammaTRC(1.0f, 100, 100));
        Assert.Throws<ArgumentException>(() => Numa.GammaTRC(1.0f, 200, 100));
        /* gamma <= 0 is replaced by 1.0 */
        int[] tab = Table(Numa.GammaTRC(-1.0f, 0, 255));
        for (int i = 0; i < 256; i++)
            Assert.Equal(i, tab[i]);
    }

    [Theory]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    [InlineData(3.0f)]
    public void ContrastTRC_MatchesDirectComputation(float factor)
    {
        int[] tab = Table(Numa.ContrastTRC(factor));
        double scale = 5.0;
        double ymax = Math.Atan(1.0 * factor * scale);
        double ymin = Math.Atan(-127.0 * factor * scale / 128.0);
        double dely = ymax - ymin;
        for (int i = 0; i < 256; i++) {
            int expected = (int)((255.0 / dely) *
                (-ymin + Math.Atan(factor * scale * (i - 127.0) / 128.0)) + 0.5);
            Assert.True(expected == tab[i], $"i = {i}: {expected} vs {tab[i]}");
        }
        Assert.Equal(0, tab[0]);
        Assert.Equal(255, tab[255]);
        for (int i = 1; i < 256; i++)
            Assert.True(tab[i] >= tab[i - 1]);
    }

    [Fact]
    public void ContrastTRC_ZeroFactorIsLinear()
    {
        int[] tab = Table(Numa.ContrastTRC(0.0f));
        for (int i = 0; i < 256; i++)
            Assert.Equal(i, tab[i]);
        tab = Table(Numa.ContrastTRC(-2.0f));
        for (int i = 0; i < 256; i++)
            Assert.Equal(i, tab[i]);
    }

    [Fact]
    public void EqualizeTRC_FlatHistogramIsIdentityAndFractZeroIsLinear()
    {
        /* every gray level occurs equally often */
        Pix pix = Pix.Create(256, 4, 8);
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 256; j++)
                Naive.SetPixel(pix, j, i, (uint)j);

        int[] tab = Table(Numa.EqualizeTRC(pix, 1.0f, 1));
        for (int i = 0; i < 256; i++) {   /* partial sums include the bin itself, so the map is shifted by one bin */
            int itarg = (int)(255.0 * (4 * (i + 1)) / 1024 + 0.5);
            Assert.Equal(Math.Min(i + (itarg - i), 255), tab[i]);
        }

        /* two-valued image: dark pixels go to 128, light pixels to 255 (full equalization) */
        Pix two = Pix.Create(10, 2, 8);
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 10; j++)
                Naive.SetPixel(two, j, i, j < 5 ? 10u : 200u);
        tab = Table(Numa.EqualizeTRC(two, 1.0f, 1));
        Assert.Equal(0, tab[0]);
        Assert.Equal(128, tab[10]);   /* itarg = (int)(255 * 0.5 + 0.5) = 128; iout = 10 + (int)(1.0 * 118) */
        Assert.Equal(255, tab[200]);

        /* fract 0: iout = iin */
        tab = Table(Numa.EqualizeTRC(two, 0.0f, 1));
        for (int i = 0; i < 256; i++)
            Assert.Equal(i, tab[i]);
    }

    /*------------------------------- TRCMap --------------------------------*/

    [Fact]
    public void TRCMap_Gray_AppliesTable()
    {
        var rng = new Random(1);
        Pix pix = MakeGray(rng, 19, 7);
        Numa na = Numa.GammaTRC(1.7f, 20, 230);
        int[] tab = Table(na);
        Pix copy = Pix.Copy(null, pix);
        pix.TRCMap(null, na);
        for (int i = 0; i < 7; i++)
            for (int j = 0; j < 19; j++)
                Assert.Equal((uint)tab[Naive.GetPixel(copy, j, i)], Naive.GetPixel(pix, j, i));
    }

    [Fact]
    public void TRCMap_Rgb_AppliesTableToEachComponentAndClearsAlpha()
    {
        var rng = new Random(2);
        Pix pix = MakeRgb(rng, 11, 9);
        Naive.SetPixel(pix, 0, 0, 0x102030ffu);
        Numa na = Numa.ContrastTRC(0.5f);
        int[] tab = Table(na);
        Pix copy = Pix.Copy(null, pix);
        pix.TRCMap(null, na);
        for (int i = 0; i < 9; i++)
            for (int j = 0; j < 11; j++) {
                uint s = Naive.GetPixel(copy, j, i);
                uint expected = ((uint)tab[(s >> 24) & 0xff] << 24) |
                                ((uint)tab[(s >> 16) & 0xff] << 16) |
                                ((uint)tab[(s >> 8) & 0xff] << 8);
                Assert.Equal(expected, Naive.GetPixel(pix, j, i));
            }
    }

    [Fact]
    public void TRCMap_Masked_ChangesOnlyPixelsUnderMask()
    {
        var rng = new Random(3);
        Pix pix = MakeGray(rng, 21, 10);
        Pix mask = Pix.Create(15, 8, 1);   /* smaller than pix */
        for (int i = 0; i < 8; i++)
            for (int j = 0; j < 15; j++)
                Naive.SetPixel(mask, j, i, (uint)((i * 3 + j) % 2));
        Numa na = Numa.GammaTRC(0.5f, 0, 255);
        int[] tab = Table(na);
        Pix copy = Pix.Copy(null, pix);
        pix.TRCMap(mask, na);
        for (int i = 0; i < 10; i++)
            for (int j = 0; j < 21; j++) {
                uint s = Naive.GetPixel(copy, j, i);
                bool on = i < 8 && j < 15 && Naive.GetPixel(mask, j, i) == 1;
                Assert.Equal(on ? (uint)tab[s] : s, Naive.GetPixel(pix, j, i));
            }
    }

    [Fact]
    public void TRCMapGeneral_UsesSeparateTables()
    {
        var rng = new Random(4);
        Pix pix = MakeRgb(rng, 8, 8);
        Numa nar = Numa.GammaTRC(0.5f, 0, 255);
        Numa nag = Numa.GammaTRC(1.0f, 0, 255);
        Numa nab = Numa.GammaTRC(2.0f, 10, 240);
        int[] tr = Table(nar), tg = Table(nag), tb = Table(nab);
        Pix copy = Pix.Copy(null, pix);
        pix.TRCMapGeneral(null, nar, nag, nab);
        for (int i = 0; i < 8; i++)
            for (int j = 0; j < 8; j++) {
                uint s = Naive.GetPixel(copy, j, i);
                uint expected = ((uint)tr[(s >> 24) & 0xff] << 24) |
                                ((uint)tg[(s >> 16) & 0xff] << 16) |
                                ((uint)tb[(s >> 8) & 0xff] << 8);
                Assert.Equal(expected, Naive.GetPixel(pix, j, i));
            }
    }

    [Fact]
    public void TRCMap_InvalidArguments()
    {
        Pix pix1 = Pix.Create(4, 4, 1);
        Pix pix8 = Pix.Create(4, 4, 8);
        Assert.Throws<ArgumentException>(() => pix1.TRCMap(null, Numa.GammaTRC(1.0f, 0, 255)));
        Assert.Throws<ArgumentException>(() => pix8.TRCMap(null, Numa.MakeSequence(0, 1, 100)));
        Assert.Throws<ArgumentException>(() => pix8.TRCMap(pix8, Numa.GammaTRC(1.0f, 0, 255)));
    }

    /*------------------------------ Pix wrappers ---------------------------*/

    [Fact]
    public void GammaTRC_IdentityReturnsCopy()
    {
        var rng = new Random(5);
        Pix pix = MakeGray(rng, 13, 6);
        Pix pixd = Pix.GammaTRC(null, pix, 1.0f, 0, 255);
        Assert.NotSame(pix, pixd);
        AssertSame(pix, pixd);
        Assert.Same(pix, Pix.GammaTRC(pix, pix, 1.0f, 0, 255));
    }

    [Fact]
    public void GammaTRC_NewAndInPlaceAgree()
    {
        var rng = new Random(6);
        Pix pix = MakeRgb(rng, 12, 5);
        Pix copy = Pix.Copy(null, pix);
        Pix pixd = Pix.GammaTRC(null, pix, 1.4f, 20, 220);
        AssertSame(copy, pix);   /* source untouched */
        Pix inplace = Pix.GammaTRC(pix, pix, 1.4f, 20, 220);
        Assert.Same(pix, inplace);
        AssertSame(pixd, inplace);
    }

    [Fact]
    public void GammaTRC_Colormapped_TransformsColormap()
    {
        Pix pix = Pix.Create(4, 4, 8);
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(10, 100, 200);
        cmap.AddColor(0, 255, 128);
        pix.SetColormap(cmap);
        Numa na = Numa.GammaTRC(0.6f, 0, 255);
        int[] tab = Table(na);
        Pix pixd = Pix.GammaTRC(null, pix, 0.6f, 0, 255);
        pixd.Colormap!.GetColor(0, out int r, out int g, out int b);
        Assert.Equal((tab[10], tab[100], tab[200]), (r, g, b));
        pix.Colormap!.GetColor(0, out r, out g, out b);
        Assert.Equal((10, 100, 200), (r, g, b));
    }

    [Fact]
    public void GammaTRC_Pix_InvalidArguments()
    {
        Pix pix8 = Pix.Create(4, 4, 8);
        Pix pix16 = Pix.Create(4, 4, 16);
        Assert.Throws<ArgumentException>(() => Pix.GammaTRC(null, pix16, 0.5f, 0, 255));
        Assert.Throws<ArgumentException>(() => Pix.GammaTRC(null, pix8, 0.5f, 100, 100));
        Assert.Throws<ArgumentException>(() => Pix.GammaTRC(Pix.Create(4, 4, 8), pix8, 0.5f, 0, 255));
    }

    [Fact]
    public void GammaTRCMasked_AndWithAlpha()
    {
        var rng = new Random(7);
        Pix pix = MakeGray(rng, 9, 9);
        Pix mask = Pix.Create(9, 9, 1);
        for (int i = 0; i < 9; i++)
            for (int j = 0; j < 9; j++)
                Naive.SetPixel(mask, j, i, (uint)((i + j) & 1));
        int[] tab = Table(Numa.GammaTRC(2.0f, 0, 255));
        Pix pixd = Pix.GammaTRCMasked(null, pix, mask, 2.0f, 0, 255);
        for (int i = 0; i < 9; i++)
            for (int j = 0; j < 9; j++) {
                uint s = Naive.GetPixel(pix, j, i);
                Assert.Equal(((i + j) & 1) == 1 ? (uint)tab[s] : s, Naive.GetPixel(pixd, j, i));
            }

        /* alpha channel is preserved */
        Pix rgba = MakeRgb(rng, 6, 6);
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 6; j++)
                Naive.SetPixel(rgba, j, i, (Naive.GetPixel(rgba, j, i) & 0xffffff00u) | (uint)(i * 6 + j + 1));
        rgba.Spp = 4;
        Pix outp = Pix.GammaTRCWithAlpha(null, rgba, 2.0f, 0, 255);
        Assert.Equal(4, outp.Spp);
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 6; j++) {
                uint s = Naive.GetPixel(rgba, j, i);
                uint expected = ((uint)tab[(s >> 24) & 0xff] << 24) |
                                ((uint)tab[(s >> 16) & 0xff] << 16) |
                                ((uint)tab[(s >> 8) & 0xff] << 8) | (s & 0xff);
                Assert.Equal(expected, Naive.GetPixel(outp, j, i));
            }
    }

    [Fact]
    public void ContrastTRC_AppliesTableAndZeroFactorCopies()
    {
        var rng = new Random(8);
        Pix pix = MakeGray(rng, 14, 7);
        int[] tab = Table(Numa.ContrastTRC(0.8f));
        Pix pixd = Pix.ContrastTRC(null, pix, 0.8f);
        for (int i = 0; i < 7; i++)
            for (int j = 0; j < 14; j++)
                Assert.Equal((uint)tab[Naive.GetPixel(pix, j, i)], Naive.GetPixel(pixd, j, i));
        Pix same = Pix.ContrastTRC(null, pix, 0.0f);
        Assert.NotSame(pix, same);
        AssertSame(pix, same);
    }

    [Fact]
    public void EqualizeTRC_Pix_MatchesTableAndHandlesRgb()
    {
        var rng = new Random(9);
        Pix pix = MakeGray(rng, 30, 20);
        Numa na = Numa.EqualizeTRC(pix, 0.7f, 1);
        int[] tab = Table(na);
        Pix pixd = Pix.EqualizeTRC(null, pix, 0.7f, 1);
        for (int i = 0; i < 20; i++)
            for (int j = 0; j < 30; j++)
                Assert.Equal((uint)tab[Naive.GetPixel(pix, j, i)], Naive.GetPixel(pixd, j, i));

        Pix rgb = MakeRgb(rng, 16, 16);
        Pix rgbd = Pix.EqualizeTRC(null, rgb, 1.0f, 1);
        int[][] tabs = new int[3][];
        for (int c = 0; c < 3; c++)
            tabs[c] = Table(Numa.EqualizeTRC(rgb.GetRGBComponent(c), 1.0f, 1));
        for (int i = 0; i < 16; i++)
            for (int j = 0; j < 16; j++) {
                uint s = Naive.GetPixel(rgb, j, i);
                uint expected = ((uint)tabs[0][(s >> 24) & 0xff] << 24) |
                                ((uint)tabs[1][(s >> 16) & 0xff] << 16) |
                                ((uint)tabs[2][(s >> 8) & 0xff] << 8);
                Assert.Equal(expected, Naive.GetPixel(rgbd, j, i));
            }
        Assert.Throws<ArgumentException>(() => Pix.EqualizeTRC(null, pix, 1.5f, 1));
        Assert.Throws<ArgumentException>(() => Pix.EqualizeTRC(null, pix, 0.5f, 0));
    }

    /*----------------------------- unsharp masking -------------------------*/

    [Theory]
    [InlineData(1, L.L_HORIZ)]
    [InlineData(2, L.L_HORIZ)]
    [InlineData(1, L.L_VERT)]
    [InlineData(2, L.L_VERT)]
    public void UnsharpMaskingGray1D_MatchesDirectComputation(int halfwidth, int direction)
    {
        var rng = new Random(20 + halfwidth + direction);
        int w = 17, h = 13;
        float fract = 0.45f;
        Pix pix = MakeGray(rng, w, h);
        Pix pixd = pix.UnsharpMaskingGray1D(halfwidth, fract, direction);

        float[] a = new float[2 * halfwidth + 1];
        for (int k = 0; k < a.Length; k++)
            a[k] = (float)(-fract / (2 * halfwidth + 1.0));
        a[halfwidth] = (float)(1.0 + fract * (2.0 * halfwidth) / (2 * halfwidth + 1.0));
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++) {
                bool inside = direction == L.L_HORIZ
                    ? (j >= halfwidth && j < w - halfwidth)
                    : (i >= halfwidth && i < h - halfwidth);
                uint expected = Naive.GetPixel(pix, j, i);
                if (inside) {
                    float val = 0;
                    for (int k = -halfwidth; k <= halfwidth; k++) {
                        uint p = direction == L.L_HORIZ ? Naive.GetPixel(pix, j + k, i)
                                                       : Naive.GetPixel(pix, j, i + k);
                        if (k == -halfwidth)
                            val = a[0] * p;
                        else
                            val += a[k + halfwidth] * p;
                    }
                    expected = (uint)Math.Clamp((int)val, 0, 255);
                }
                Assert.True(expected == Naive.GetPixel(pixd, j, i), $"({j},{i})");
            }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void UnsharpMaskingGray2D_MatchesDirectComputation(int halfwidth)
    {
        var rng = new Random(30 + halfwidth);
        int w = 15, h = 12;
        float fract = 0.6f;
        Pix pix = MakeGray(rng, w, h);
        Pix pixd = pix.UnsharpMaskingGray2D(halfwidth, fract);
        int n = 2 * halfwidth + 1;
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++) {
                uint sval = Naive.GetPixel(pix, j, i);
                uint expected = sval;
                if (i >= halfwidth && i < h - halfwidth && j >= halfwidth && j < w - halfwidth) {
                    /* horizontal sums are exact floats; the vertical sum is added in order */
                    float sum = 0;
                    for (int k = -halfwidth; k <= halfwidth; k++) {
                        float rowsum = 0;
                        for (int m = -halfwidth; m <= halfwidth; m++)
                            rowsum += Naive.GetPixel(pix, j + m, i + k);
                        sum += rowsum;
                    }
                    float norm = 1.0f / (n * n);
                    float val = norm * sum;
                    int ival = (int)(sval + fract * (sval - val) + 0.5);
                    expected = (uint)Math.Clamp(ival, 0, 255);
                }
                Assert.True(expected == Naive.GetPixel(pixd, j, i), $"({j},{i})");
            }
    }

    [Fact]
    public void UnsharpMasking_ConstantImageUnchangedAndNoSharpeningReturnsClone()
    {
        Pix pix = Pix.Create(20, 20, 8);
        pix.SetAllArbitrary(97);
        AssertSame(pix, pix.UnsharpMasking(1, 0.5f));
        AssertSame(pix, pix.UnsharpMasking(2, 0.5f));
        Assert.Same(pix, pix.UnsharpMasking(0, 0.5f));
        Assert.Same(pix, pix.UnsharpMasking(1, 0.0f));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 1).UnsharpMasking(1, 0.5f));
        Assert.Throws<ArgumentException>(() => pix.UnsharpMaskingFast(3, 0.5f, L.L_BOTH_DIRECTIONS));
    }

    [Fact]
    public void UnsharpMasking_Rgb_SharpensEachComponent()
    {
        var rng = new Random(40);
        Pix pix = MakeRgb(rng, 14, 14);
        Pix pixd = pix.UnsharpMasking(1, 0.5f);
        for (int c = 0; c < 3; c++) {
            Pix expected = pix.GetRGBComponent(c).UnsharpMaskingGrayFast(1, 0.5f, L.L_BOTH_DIRECTIONS);
            AssertSame(expected, pixd.GetRGBComponent(c));
        }
    }

    /*------------------------------- color ops -----------------------------*/

    [Fact]
    public void ColorShiftRGB_MatchesDirectComputation()
    {
        var rng = new Random(50);
        Pix pix = MakeRgb(rng, 10, 10);
        float rf = 0.3f, gf = -0.5f, bf = 0.0f;
        Pix pixd = Pix.ColorShiftRGB(pix, rf, gf, bf);
        for (int i = 0; i < 10; i++)
            for (int j = 0; j < 10; j++) {
                uint s = Naive.GetPixel(pix, j, i);
                int r = (int)(s >> 24), g = (int)((s >> 16) & 0xff), b = (int)((s >> 8) & 0xff);
                float fr = r, fg = g, fb = b;
                int nr = (int)(fr + (255.0 - fr) * rf);
                int ng = (int)(fg * (1.0 + gf));
                int nb = (int)(fb + (255.0 - fb) * bf);
                Assert.Equal(((uint)nr << 24) | ((uint)ng << 16) | ((uint)nb << 8), Naive.GetPixel(pixd, j, i));
            }
        AssertSame(pix, Pix.ColorShiftRGB(pix, 0.0f, 0.0f, 0.0f));
        Assert.Throws<ArgumentException>(() => Pix.ColorShiftRGB(pix, 1.5f, 0.0f, 0.0f));
    }

    [Fact]
    public void MultConstantColor_Rgb_AndColormap()
    {
        var rng = new Random(51);
        Pix pix = MakeRgb(rng, 10, 10);
        Pix pixd = pix.MultConstantColor(1.5f, 0.5f, -1.0f);
        for (int i = 0; i < 10; i++)
            for (int j = 0; j < 10; j++) {
                uint s = Naive.GetPixel(pix, j, i);
                int r = (int)(s >> 24), g = (int)((s >> 16) & 0xff);
                uint expected = ((uint)Math.Min(255, (int)(1.5f * r)) << 24) |
                                ((uint)Math.Min(255, (int)(0.5f * g)) << 16);
                Assert.Equal(expected, Naive.GetPixel(pixd, j, i));
            }

        Pix pixc = Pix.Create(3, 3, 8);
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(200, 100, 50);
        pixc.SetColormap(cmap);
        Pix pixcd = pixc.MultConstantColor(2.0f, 1.0f, 0.5f);
        pixcd.Colormap!.GetColor(0, out int rv, out int gv, out int bv);
        Assert.Equal((255, 100, 25), (rv, gv, bv));
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 3, 8).MultConstantColor(1f, 1f, 1f));
    }

    [Fact]
    public void MultMatrixColor_IdentityAndSwap()
    {
        var rng = new Random(52);
        Pix pix = MakeRgb(rng, 9, 9);
        L_Kernel ident = L_Kernel.Create(3, 3);
        ident.SetElement(0, 0, 1.0f);
        ident.SetElement(1, 1, 1.0f);
        ident.SetElement(2, 2, 1.0f);
        AssertSame(pix, pix.MultMatrixColor(ident));

        /* swap r and b, double g and clip */
        L_Kernel kel = L_Kernel.Create(3, 3);
        kel.SetElement(0, 2, 1.0f);
        kel.SetElement(1, 1, 2.0f);
        kel.SetElement(2, 0, 1.0f);
        Pix pixd = pix.MultMatrixColor(kel);
        for (int i = 0; i < 9; i++)
            for (int j = 0; j < 9; j++) {
                uint s = Naive.GetPixel(pix, j, i);
                int r = (int)(s >> 24), g = (int)((s >> 16) & 0xff), b = (int)((s >> 8) & 0xff);
                uint expected = ((uint)b << 24) | ((uint)Math.Min(255, 2 * g) << 16) | ((uint)r << 8);
                Assert.Equal(expected, Naive.GetPixel(pixd, j, i));
            }
        Assert.Throws<ArgumentException>(() => pix.MultMatrixColor(L_Kernel.Create(3, 2)));
    }

    [Fact]
    public void DarkenGray_DarkensOnlyUnsaturatedDarkPixels()
    {
        Pix pix = Pix.Create(4, 1, 32);
        Naive.SetPixel(pix, 0, 0, (100u << 24) | (100u << 16) | (100u << 8));   /* gray, dark: sat 0 */
        Naive.SetPixel(pix, 1, 0, (100u << 24) | (110u << 16) | (120u << 8));   /* sat 20 of 40 */
        Naive.SetPixel(pix, 2, 0, (200u << 24) | (200u << 16) | (200u << 8));   /* above thresh */
        Naive.SetPixel(pix, 3, 0, (10u << 24) | (100u << 16) | (200u << 8));    /* saturated */
        Pix pixd = Pix.DarkenGray(null, pix, 150, 40);
        Assert.Equal(0u, Naive.GetPixel(pixd, 0, 0));
        float ratio = 20.0f / 40.0f;
        Assert.Equal(((uint)(int)(ratio * 100) << 24) | ((uint)(int)(ratio * 110) << 16) |
                     ((uint)(int)(ratio * 120) << 8), Naive.GetPixel(pixd, 1, 0));
        Assert.Equal(Naive.GetPixel(pix, 2, 0), Naive.GetPixel(pixd, 2, 0));
        Assert.Equal(Naive.GetPixel(pix, 3, 0), Naive.GetPixel(pixd, 3, 0));
        Assert.Throws<ArgumentException>(() => Pix.DarkenGray(null, pix, 150, 0));
    }

    [Fact]
    public void HalfEdgeByBandpass_IsDifferenceOfBlurs()
    {
        var rng = new Random(60);
        Pix pix = MakeGray(rng, 24, 20);
        Pix pixd = pix.HalfEdgeByBandpass(1, 1, 3, 3);
        Pix c1 = pix.BlockconvGray(null, 1, 1);
        Pix c2 = pix.BlockconvGray(null, 3, 3);
        for (int i = 0; i < 20; i++)
            for (int j = 0; j < 24; j++) {
                int v = (int)Naive.GetPixel(c1, j, i) - (int)Naive.GetPixel(c2, j, i);
                Assert.Equal((uint)Math.Max(0, v), Naive.GetPixel(pixd, j, i));
            }
        Assert.Throws<ArgumentException>(() => pix.HalfEdgeByBandpass(1, 1, 1, 1));
    }
}
