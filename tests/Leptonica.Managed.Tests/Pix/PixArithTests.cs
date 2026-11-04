using System;
using Leptonica;
using Naive = Leptonica.Managed.Tests.Rop.NaiveRop;

namespace Leptonica.Managed.Tests.PixCore;

/// <summary>
/// Tests of pixarith.c.  Expected values are computed pixel by pixel with plain integer
/// arithmetic and compared with the line-oriented implementation.
/// </summary>
public class PixArithTests
{
    private static uint Mask(int d) => d == 32 ? 0xffffffffu : (1u << d) - 1;

    private static Pix RandomPix(Random rng, int w, int h, int d)
    {
        Pix pix = Pix.Create(w, h, d);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                Naive.SetPixel(pix, j, i, (uint)rng.NextInt64() & Mask(d));
        return pix;
    }

    /// <summary>Applies f to every pixel (x, y) of a copy of pix.</summary>
    private static Pix Map(Pix pix, Func<uint, uint> f)
    {
        Pix pixd = Pix.Copy(null, pix);
        for (int i = 0; i < pix.Height; i++)
            for (int j = 0; j < pix.Width; j++)
                Naive.SetPixel(pixd, j, i, f(Naive.GetPixel(pix, j, i)));
        return pixd;
    }

    /// <summary>Applies f to the overlapping region of pix1 and pix2, keeping the rest of pix1.</summary>
    private static Pix Map2(Pix pix1, Pix pix2, Func<uint, uint, uint> f)
    {
        Pix pixd = Pix.Copy(null, pix1);
        int w = Math.Min(pix1.Width, pix2.Width);
        int h = Math.Min(pix1.Height, pix2.Height);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                Naive.SetPixel(pixd, j, i, f(Naive.GetPixel(pix1, j, i), Naive.GetPixel(pix2, j, i)));
        return pixd;
    }

    private static void AssertPixEqual(Pix expected, Pix actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Depth, actual.Depth);
        Assert.Equal(expected.Data, actual.Data);
    }

    private static Pix Uniform(int w, int h, int d, uint val)
    {
        Pix pix = Pix.Create(w, h, d);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                Naive.SetPixel(pix, j, i, val);
        return pix;
    }

    private static uint Rgb(int r, int g, int b)
    {
        Pix.ComposeRGBPixel(r, g, b, out uint p);
        return p;
    }

    /*------------------------------------------------------------*
     *               One-image arithmetic operations              *
     *------------------------------------------------------------*/
    [Theory]
    [InlineData(8, 0)]
    [InlineData(8, 37)]
    [InlineData(8, 300)]
    [InlineData(8, -37)]
    [InlineData(8, -300)]
    [InlineData(16, 0)]
    [InlineData(16, 1234)]
    [InlineData(16, 70000)]
    [InlineData(16, -1234)]
    [InlineData(16, -70000)]
    [InlineData(32, 0)]
    [InlineData(32, 12345)]
    [InlineData(32, -12345)]
    public void AddConstantGray(int d, int val)
    {
        var rng = new Random(1000 + d + val);
        Pix pixs = RandomPix(rng, 37, 9, d);
        long max = Mask(d);
        Pix expected = Map(pixs, p => d == 32
            ? p + (uint)val
            : (uint)Math.Clamp((long)p + val, 0, max));
        Pix pix = Pix.Copy(null, pixs);
        pix.AddConstantGray(val);
        AssertPixEqual(expected, pix);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    public void AddConstantGrayClipsAtLimits(int d)
    {
        uint max = Mask(d);
        Pix pix = Uniform(5, 3, d, max);
        pix.AddConstantGray(1);
        Assert.Equal(max, Naive.GetPixel(pix, 4, 2));
        pix = Uniform(5, 3, d, 0);
        pix.AddConstantGray(-1);
        Assert.Equal(0u, Naive.GetPixel(pix, 0, 0));
        pix = Uniform(5, 3, d, max - 1);
        pix.AddConstantGray(1);
        Assert.Equal(max, Naive.GetPixel(pix, 2, 1));
        pix.AddConstantGray(-1);
        Assert.Equal(max - 1, Naive.GetPixel(pix, 2, 1));
    }

    [Fact]
    public void AddConstantGray32Wraps()
    {
        Pix pix = Uniform(3, 2, 32, 0xffffffffu);
        pix.AddConstantGray(1);
        Assert.Equal(0u, Naive.GetPixel(pix, 1, 1));
        pix.AddConstantGray(-1);
        Assert.Equal(0xffffffffu, Naive.GetPixel(pix, 1, 1));
    }

    [Fact]
    public void AddConstantGrayRejectsOtherDepths()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 1).AddConstantGray(1));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 4).AddConstantGray(1));
    }

    [Theory]
    [InlineData(8, 0.0f)]
    [InlineData(8, 0.5f)]
    [InlineData(8, 1.0f)]
    [InlineData(8, 2.75f)]
    [InlineData(8, 1000.0f)]
    [InlineData(16, 0.3f)]
    [InlineData(16, 1.5f)]
    [InlineData(16, 100.0f)]
    [InlineData(32, 0.25f)]
    [InlineData(32, 1.0f)]
    [InlineData(32, 3.0f)]
    public void MultConstantGray(int d, float val)
    {
        var rng = new Random(2000 + d);
        Pix pixs = RandomPix(rng, 29, 7, d);
        Pix expected = Map(pixs, p => {
            if (d == 32)
                return (uint)(val * p);
            int v = (int)(val * (int)p);
            return (uint)Math.Min(v, (int)Mask(d));
        });
        Pix pix = Pix.Copy(null, pixs);
        pix.MultConstantGray(val);
        AssertPixEqual(expected, pix);
    }

    [Fact]
    public void MultConstantGrayClipsAtMax()
    {
        Pix pix = Uniform(4, 4, 8, 200);
        pix.MultConstantGray(2.0f);
        Assert.Equal(255u, Naive.GetPixel(pix, 3, 3));
        pix = Uniform(4, 4, 16, 60000);
        pix.MultConstantGray(2.0f);
        Assert.Equal(65535u, Naive.GetPixel(pix, 3, 3));
        pix = Uniform(4, 4, 8, 255);
        pix.MultConstantGray(0.0f);
        Assert.Equal(0u, Naive.GetPixel(pix, 0, 0));
    }

    [Fact]
    public void MultConstantGrayErrors()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).MultConstantGray(-1.0f));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 1).MultConstantGray(1.0f));
    }

    /*------------------------------------------------------------*
     *               Two-image arithmetic operations              *
     *------------------------------------------------------------*/
    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void AddGrayThreeCases(int d)
    {
        var rng = new Random(3000 + d);
        Pix pixs1 = RandomPix(rng, 41, 11, d);
        Pix pixs2 = RandomPix(rng, 41, 11, d);
        uint max = Mask(d);
        Func<uint, uint, uint> add = (a, b) => d == 32
            ? a + b
            : (uint)Math.Min((long)a + b, max);
        Pix expected = Map2(pixs1, pixs2, add);

            /* pixd == null: new image, pixs1 untouched */
        Pix orig1 = Pix.Copy(null, pixs1);
        Pix pixd = Pix.AddGray(null, pixs1, pixs2);
        Assert.NotSame(pixs1, pixd);
        AssertPixEqual(expected, pixd);
        AssertPixEqual(orig1, pixs1);

            /* pixd == pixs1: in place */
        Pix inplace = Pix.Copy(null, pixs1);
        Pix ret = Pix.AddGray(inplace, inplace, pixs2);
        Assert.Same(inplace, ret);
        AssertPixEqual(expected, inplace);

            /* pixd != pixs1: existing destination */
        Pix dest = Pix.Create(41, 11, d);
        ret = Pix.AddGray(dest, pixs1, pixs2);
        Assert.Same(dest, ret);
        AssertPixEqual(expected, dest);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void SubtractGrayThreeCases(int d)
    {
        var rng = new Random(4000 + d);
        Pix pixs1 = RandomPix(rng, 41, 11, d);
        Pix pixs2 = RandomPix(rng, 41, 11, d);
        Func<uint, uint, uint> sub = (a, b) => d == 32
            ? a - b
            : (uint)Math.Max((long)a - b, 0);
        Pix expected = Map2(pixs1, pixs2, sub);

        Pix orig1 = Pix.Copy(null, pixs1);
        Pix pixd = Pix.SubtractGray(null, pixs1, pixs2);
        AssertPixEqual(expected, pixd);
        AssertPixEqual(orig1, pixs1);

        Pix inplace = Pix.Copy(null, pixs1);
        Assert.Same(inplace, Pix.SubtractGray(inplace, inplace, pixs2));
        AssertPixEqual(expected, inplace);

        Pix dest = Pix.Create(41, 11, d);
        Assert.Same(dest, Pix.SubtractGray(dest, pixs1, pixs2));
        AssertPixEqual(expected, dest);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    public void AddSubtractGrayClipAtLimits(int d)
    {
        uint max = Mask(d);
        Pix sum = Pix.AddGray(null, Uniform(3, 3, d, max), Uniform(3, 3, d, 1));
        Assert.Equal(max, Naive.GetPixel(sum, 2, 2));
        sum = Pix.AddGray(null, Uniform(3, 3, d, max / 2 + 1), Uniform(3, 3, d, max / 2 + 1));
        Assert.Equal(max, Naive.GetPixel(sum, 0, 0));
        Pix diff = Pix.SubtractGray(null, Uniform(3, 3, d, 0), Uniform(3, 3, d, 1));
        Assert.Equal(0u, Naive.GetPixel(diff, 1, 1));
        diff = Pix.SubtractGray(null, Uniform(3, 3, d, 5), Uniform(3, 3, d, 7));
        Assert.Equal(0u, Naive.GetPixel(diff, 1, 1));
        diff = Pix.SubtractGray(null, Uniform(3, 3, d, max), Uniform(3, 3, d, 1));
        Assert.Equal(max - 1, Naive.GetPixel(diff, 1, 1));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void AddSubtractGrayDifferentSizes(int d)
    {
        var rng = new Random(5000 + d);
        Pix pixs1 = RandomPix(rng, 30, 20, d);
        Pix pixs2 = RandomPix(rng, 17, 25, d);
        uint max = Mask(d);
        Pix expectedAdd = Map2(pixs1, pixs2, (a, b) => d == 32 ? a + b : (uint)Math.Min((long)a + b, max));
        Pix expectedSub = Map2(pixs1, pixs2, (a, b) => d == 32 ? a - b : (uint)Math.Max((long)a - b, 0));
        AssertPixEqual(expectedAdd, Pix.AddGray(null, pixs1, pixs2));
        AssertPixEqual(expectedSub, Pix.SubtractGray(null, pixs1, pixs2));
    }

    [Fact]
    public void AddSubtractGrayErrors()
    {
        Pix a = Pix.Create(4, 4, 8);
        Pix b = Pix.Create(4, 4, 8);
        Pix c = Pix.Create(4, 4, 16);
        Pix e = Pix.Create(4, 4, 1);
        Assert.Throws<ArgumentException>(() => Pix.AddGray(null, a, a));
        Assert.Throws<ArgumentException>(() => Pix.AddGray(b, a, b));
        Assert.Throws<ArgumentException>(() => Pix.AddGray(null, a, c));
        Assert.Throws<ArgumentException>(() => Pix.AddGray(c, a, b));
        Assert.Throws<ArgumentException>(() => Pix.AddGray(null, e, Pix.Create(4, 4, 1)));
        Assert.Throws<ArgumentException>(() => Pix.SubtractGray(null, a, a));
        Assert.Throws<ArgumentException>(() => Pix.SubtractGray(b, a, b));
        Assert.Throws<ArgumentException>(() => Pix.SubtractGray(null, a, c));
        Assert.Throws<ArgumentException>(() => Pix.SubtractGray(c, a, b));
    }

    [Fact]
    public void MultiplyGray8()
    {
        var rng = new Random(6000);
        Pix pixs = RandomPix(rng, 25, 10, 8);
        Pix pixg = RandomPix(rng, 25, 10, 8);
        foreach (float norm in new[] { 1.0f / 255.0f, 1.0f / 128.0f, 0.02f, 1.0f }) {
            Pix expected = Map2(pixs, pixg, (s, g) => (uint)Math.Min(255, (int)(s * g * norm + 0.5)));
            AssertPixEqual(expected, pixs.MultiplyGray(pixg, norm));
        }
    }

    [Fact]
    public void MultiplyGray32()
    {
        var rng = new Random(6001);
        Pix pixs = RandomPix(rng, 25, 10, 32);
        Pix pixg = RandomPix(rng, 25, 10, 8);
        float norm = 1.0f / 255.0f;
        Pix expected = Map2(pixs, pixg, (s, g) => {
            Pix.ExtractRGBValues(s, out int r, out int gr, out int b);
            int gv = (int)g;
            int r2 = Math.Min(255, (int)(r * gv * norm + 0.5));
            int g2 = Math.Min(255, (int)(gr * gv * norm + 0.5));
            int b2 = Math.Min(255, (int)(b * gv * norm + 0.5));
            return Rgb(r2, g2, b2);
        });
        AssertPixEqual(expected, pixs.MultiplyGray(pixg, norm));
    }

    [Fact]
    public void MultiplyGrayErrors()
    {
        Pix g = Pix.Create(4, 4, 8);
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 16).MultiplyGray(g, 1.0f));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).MultiplyGray(Pix.Create(4, 4, 32), 1.0f));
    }

    /*------------------------------------------------------------*
     *                      Threshold to value                    *
     *------------------------------------------------------------*/
    [Theory]
    [InlineData(8, 100, 255)]   // set pixels >= 100 to 255
    [InlineData(8, 100, 7)]     // set pixels <= 100 to 7
    [InlineData(8, 0, 255)]
    [InlineData(8, 255, 0)]
    [InlineData(16, 30000, 65535)]
    [InlineData(16, 30000, 5)]
    [InlineData(32, 0x7ffffffe, 0x7fffffff)]
    [InlineData(32, 1000, 2000000)]
    public void ThresholdToValue(int d, long threshval, long setval)
    {
        var rng = new Random(7000 + d);
        Pix pixs = RandomPix(rng, 33, 12, d);
        bool above = setval > threshval;
        Pix expected = Map(pixs, p => above
            ? (p >= threshval ? (uint)setval : p)
            : (p <= threshval ? (uint)setval : p));
        Pix orig = Pix.Copy(null, pixs);
        Pix pixd = Pix.ThresholdToValue(null, pixs, (int)threshval, (int)setval);
        AssertPixEqual(expected, pixd);
        AssertPixEqual(orig, pixs);

        Pix inplace = Pix.Copy(null, pixs);
        Assert.Same(inplace, Pix.ThresholdToValue(inplace, inplace, (int)threshval, (int)setval));
        AssertPixEqual(expected, inplace);
    }

    [Fact]
    public void ThresholdToValueNoOpAndErrors()
    {
        var rng = new Random(7100);
        Pix pixs = RandomPix(rng, 10, 10, 8);
        AssertPixEqual(pixs, Pix.ThresholdToValue(null, pixs, 50, 50));
        Assert.Throws<ArgumentException>(() => Pix.ThresholdToValue(Pix.Create(10, 10, 8), pixs, 1, 2));
        Assert.Throws<ArgumentException>(() => Pix.ThresholdToValue(null, pixs, -1, 2));
        Assert.Throws<ArgumentException>(() => Pix.ThresholdToValue(null, pixs, 1, 256));
        Assert.Throws<ArgumentException>(() => Pix.ThresholdToValue(null, Pix.Create(4, 4, 16), 1, 0x10000));
        Assert.Throws<ArgumentException>(() => Pix.ThresholdToValue(null, Pix.Create(4, 4, 1), 0, 1));
    }

    /*------------------------------------------------------------*
     *                       Accumulators                         *
     *------------------------------------------------------------*/
    [Fact]
    public void InitAccumulate()
    {
        Pix acc = Pix.InitAccumulate(7, 5, 12345);
        Assert.Equal(32, acc.Depth);
        Assert.Equal(7, acc.Width);
        Assert.Equal(5, acc.Height);
        Assert.Equal(12345u, Naive.GetPixel(acc, 6, 4));
        acc = Pix.InitAccumulate(3, 3, 0x7fffffff);
        Assert.Equal(0x40000000u, Naive.GetPixel(acc, 1, 1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void AccumulateAddAndSubtract(int d)
    {
        var rng = new Random(8000 + d);
        Pix pixs = RandomPix(rng, 23, 8, d);
        Pix acc = Pix.InitAccumulate(23, 8, 0x1000000);
        acc.Accumulate(pixs, L.L_ARITH_ADD);
        for (int i = 0; i < 8; i++)
            for (int j = 0; j < 23; j++)
                Assert.Equal(0x1000000u + Naive.GetPixel(pixs, j, i), Naive.GetPixel(acc, j, i));
        acc.Accumulate(pixs, L.L_ARITH_SUBTRACT);
        for (int i = 0; i < 8; i++)
            for (int j = 0; j < 23; j++)
                Assert.Equal(0x1000000u, Naive.GetPixel(acc, j, i));
        acc.Accumulate(pixs, L.L_ARITH_SUBTRACT);
        for (int i = 0; i < 8; i++)
            for (int j = 0; j < 23; j++)
                Assert.Equal(0x1000000u - Naive.GetPixel(pixs, j, i), Naive.GetPixel(acc, j, i));
    }

    [Fact]
    public void AccumulateClipsToMinimumSize()
    {
        Pix pixs = Uniform(10, 4, 8, 3);
        Pix acc = Pix.InitAccumulate(6, 7, 100);
        acc.Accumulate(pixs, L.L_ARITH_ADD);
        Assert.Equal(103u, Naive.GetPixel(acc, 5, 3));
        Assert.Equal(100u, Naive.GetPixel(acc, 5, 4));
        Assert.Equal(100u, Naive.GetPixel(acc, 5, 6));
    }

    [Fact]
    public void AccumulateErrors()
    {
        Pix pixs = Pix.Create(4, 4, 8);
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).Accumulate(pixs, L.L_ARITH_ADD));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 32).Accumulate(Pix.Create(4, 4, 4), L.L_ARITH_ADD));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 32).Accumulate(pixs, L.L_ARITH_MULTIPLY));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void FinalAccumulateClips(int d)
    {
        uint offset = 1000;
        Pix acc = Pix.Create(6, 2, 32);
        uint[] vals = { 0, 999, 1000, 1001, 1255, 1256 };
        for (int j = 0; j < 6; j++) {
            Naive.SetPixel(acc, j, 0, vals[j]);
            Naive.SetPixel(acc, j, 1, offset + 70000 + (uint)j);
        }
        Pix pixd = acc.FinalAccumulate(offset, d);
        Assert.Equal(d, pixd.Depth);
        for (int j = 0; j < 6; j++) {
            long v0 = (long)vals[j] - offset;
            long v1 = 70000 + j;
            if (d == 32) {
                Assert.Equal((uint)v0, Naive.GetPixel(pixd, j, 0));
            } else {
                long max = Mask(d);
                Assert.Equal((uint)Math.Clamp(v0, 0, max), Naive.GetPixel(pixd, j, 0));
                Assert.Equal((uint)Math.Clamp(v1, 0, max), Naive.GetPixel(pixd, j, 1));
            }
        }
    }

    [Fact]
    public void FinalAccumulateCopiesResolution()
    {
        Pix acc = Pix.InitAccumulate(4, 4, 0);
        acc.XRes = 300;
        acc.YRes = 200;
        Pix pixd = acc.FinalAccumulate(0, 8);
        Assert.Equal(300, pixd.XRes);
        Assert.Equal(200, pixd.YRes);
    }

    [Fact]
    public void FinalAccumulateErrors()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).FinalAccumulate(0, 8));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 32).FinalAccumulate(0, 4));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).FinalAccumulateThreshold(0, 1));
    }

    [Fact]
    public void FinalAccumulateThreshold()
    {
        uint offset = 500;
        Pix acc = Pix.Create(35, 3, 32);
        var rng = new Random(9000);
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 35; j++)
                Naive.SetPixel(acc, j, i, (uint)rng.Next(0, 1000));
        Pix pixd = acc.FinalAccumulateThreshold(offset, 100);
        Assert.Equal(1, pixd.Depth);
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 35; j++) {
                int val = (int)(Naive.GetPixel(acc, j, i) - offset);
                Assert.Equal((uint)((uint)val >= 100 ? 1 : 0), Naive.GetPixel(pixd, j, i));
            }
    }

    [Fact]
    public void MultConstAccumulate()
    {
        uint offset = 0x10000;
        var rng = new Random(9100);
        Pix acc = Pix.InitAccumulate(13, 6, offset);
        acc.Accumulate(RandomPix(rng, 13, 6, 8), L.L_ARITH_ADD);
        acc.Accumulate(RandomPix(rng, 13, 6, 8), L.L_ARITH_SUBTRACT);
        Pix before = Pix.Copy(null, acc);
        foreach (float factor in new[] { 0.0f, 0.5f, 1.0f, 3.0f }) {
            Pix pix = Pix.Copy(null, before);
            pix.MultConstAccumulate(factor, offset);
            for (int i = 0; i < 6; i++)
                for (int j = 0; j < 13; j++) {
                    int val = (int)(Naive.GetPixel(before, j, i) - offset);
                    val = (int)(val * factor);
                    val += (int)offset;
                    Assert.Equal((uint)val, Naive.GetPixel(pix, j, i));
                }
        }
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).MultConstAccumulate(1.0f, 0));
    }

    [Fact]
    public void AccumulateRoundTripWithClipping()
    {
            /* 3*I - A - B with clipping, the use case described in pixarith.c */
        var rng = new Random(9200);
        Pix pixi = RandomPix(rng, 31, 9, 8);
        Pix pixa = RandomPix(rng, 31, 9, 8);
        Pix pixb = RandomPix(rng, 31, 9, 8);
        const uint offset = 0x40000000;
        Pix acc = Pix.InitAccumulate(31, 9, offset);
        acc.Accumulate(pixi, L.L_ARITH_ADD);
        acc.MultConstAccumulate(3.0f, offset);
        acc.Accumulate(pixa, L.L_ARITH_SUBTRACT);
        acc.Accumulate(pixb, L.L_ARITH_SUBTRACT);
        Pix result = acc.FinalAccumulate(offset, 8);
        for (int i = 0; i < 9; i++)
            for (int j = 0; j < 31; j++) {
                long v = 3L * Naive.GetPixel(pixi, j, i) - Naive.GetPixel(pixa, j, i) - Naive.GetPixel(pixb, j, i);
                Assert.Equal((uint)Math.Clamp(v, 0, 255), Naive.GetPixel(result, j, i));
            }
    }

    /*------------------------------------------------------------*
     *                    Absolute difference                     *
     *------------------------------------------------------------*/
    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void AbsDifference(int d)
    {
        var rng = new Random(10000 + d);
        Pix pixs1 = RandomPix(rng, 27, 13, d);
        Pix pixs2 = RandomPix(rng, 27, 13, d);
        Pix pixd = pixs1.AbsDifference(pixs2);
        Assert.Equal(d, pixd.Depth);
        for (int i = 0; i < 13; i++)
            for (int j = 0; j < 27; j++) {
                uint a = Naive.GetPixel(pixs1, j, i);
                uint b = Naive.GetPixel(pixs2, j, i);
                uint exp;
                if (d == 32) {
                    Pix.ExtractRGBValues(a, out int r1, out int g1, out int b1);
                    Pix.ExtractRGBValues(b, out int r2, out int g2, out int b2);
                    exp = Rgb(Math.Abs(r1 - r2), Math.Abs(g1 - g2), Math.Abs(b1 - b2));
                } else {
                    exp = a > b ? a - b : b - a;
                }
                Assert.Equal(exp, Naive.GetPixel(pixd, j, i));
            }
        // Symmetric
        AssertPixEqual(pixd, pixs2.AbsDifference(pixs1));
    }

    [Fact]
    public void AbsDifferenceExtremesAndSizes()
    {
        Pix d8 = Uniform(5, 5, 8, 0).AbsDifference(Uniform(5, 5, 8, 255));
        Assert.Equal(255u, Naive.GetPixel(d8, 4, 4));
        Pix d16 = Uniform(5, 5, 16, 65535).AbsDifference(Uniform(5, 5, 16, 0));
        Assert.Equal(65535u, Naive.GetPixel(d16, 4, 4));
        Pix small = Uniform(3, 9, 8, 10).AbsDifference(Uniform(7, 4, 8, 30));
        Assert.Equal(3, small.Width);
        Assert.Equal(4, small.Height);
        Assert.Equal(20u, Naive.GetPixel(small, 2, 3));
        Assert.Throws<ArgumentException>(() => Uniform(3, 3, 8, 0).AbsDifference(Uniform(3, 3, 16, 0)));
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 3, 1).AbsDifference(Pix.Create(3, 3, 1)));
    }

    /*------------------------------------------------------------*
     *                         Sum of RGB                         *
     *------------------------------------------------------------*/
    [Fact]
    public void AddRGB()
    {
        var rng = new Random(11000);
        Pix pixs1 = RandomPix(rng, 20, 15, 32);
        Pix pixs2 = RandomPix(rng, 17, 18, 32);
        Pix pixd = pixs1.AddRGB(pixs2);
        Assert.Equal(17, pixd.Width);
        Assert.Equal(15, pixd.Height);
        for (int i = 0; i < 15; i++)
            for (int j = 0; j < 17; j++) {
                Pix.ExtractRGBValues(Naive.GetPixel(pixs1, j, i), out int r1, out int g1, out int b1);
                Pix.ExtractRGBValues(Naive.GetPixel(pixs2, j, i), out int r2, out int g2, out int b2);
                uint exp = Rgb(Math.Min(255, r1 + r2), Math.Min(255, g1 + g2), Math.Min(255, b1 + b2));
                Assert.Equal(exp, Naive.GetPixel(pixd, j, i));
            }
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).AddRGB(pixs1));
        Assert.Throws<ArgumentException>(() => pixs1.AddRGB(Pix.Create(4, 4, 8)));
    }

    /*------------------------------------------------------------*
     *                         Min and max                        *
     *------------------------------------------------------------*/
    [Theory]
    [InlineData(8, true)]
    [InlineData(8, false)]
    [InlineData(16, true)]
    [InlineData(16, false)]
    [InlineData(32, true)]
    [InlineData(32, false)]
    public void MinOrMax(int d, bool min)
    {
        var rng = new Random(12000 + d);
        Pix pixs1 = RandomPix(rng, 26, 14, d);
        Pix pixs2 = RandomPix(rng, 22, 16, d);
        int type = min ? L.L_CHOOSE_MIN : L.L_CHOOSE_MAX;
        Pix expected = Map2(pixs1, pixs2, (a, b) => {
            if (d != 32)
                return min ? Math.Min(a, b) : Math.Max(a, b);
            Pix.ExtractRGBValues(a, out int r1, out int g1, out int b1);
            Pix.ExtractRGBValues(b, out int r2, out int g2, out int b2);
            return min ? Rgb(Math.Min(r1, r2), Math.Min(g1, g2), Math.Min(b1, b2))
                       : Rgb(Math.Max(r1, r2), Math.Max(g1, g2), Math.Max(b1, b2));
        });
        if (d == 32) {
            // The alpha byte is not preserved in the clipped region: it is cleared by compose.
            int w = Math.Min(26, 22), h = Math.Min(14, 16);
            Pix r = Pix.MinOrMax(null, pixs1, pixs2, type);
            for (int i = 0; i < h; i++)
                for (int j = 0; j < w; j++)
                    Assert.Equal(Naive.GetPixel(expected, j, i), Naive.GetPixel(r, j, i));
            return;
        }

        Pix orig1 = Pix.Copy(null, pixs1);
        Pix pixd = Pix.MinOrMax(null, pixs1, pixs2, type);
        AssertPixEqual(expected, pixd);
        AssertPixEqual(orig1, pixs1);

        Pix inplace = Pix.Copy(null, pixs1);
        Assert.Same(inplace, Pix.MinOrMax(inplace, inplace, pixs2, type));
        AssertPixEqual(expected, inplace);

        Pix dest = Pix.Create(26, 14, d);
        Assert.Same(dest, Pix.MinOrMax(dest, pixs1, pixs2, type));
        AssertPixEqual(expected, dest);
    }

    [Fact]
    public void MinOrMaxErrors()
    {
        Pix a = Pix.Create(4, 4, 8);
        Pix b = Pix.Create(4, 4, 8);
        Assert.Throws<ArgumentException>(() => Pix.MinOrMax(null, a, a, L.L_CHOOSE_MIN));
        Assert.Throws<ArgumentException>(() => Pix.MinOrMax(null, a, b, L.L_CHOOSE_MAXDIFF));
        Assert.Throws<ArgumentException>(() => Pix.MinOrMax(null, a, Pix.Create(4, 4, 16), L.L_CHOOSE_MIN));
        Assert.Throws<ArgumentException>(() => Pix.MinOrMax(null, Pix.Create(4, 4, 1), Pix.Create(4, 4, 1), L.L_CHOOSE_MIN));
    }

    /*------------------------------------------------------------*
     *                    Maximum dynamic range                   *
     *------------------------------------------------------------*/
    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void MaxDynamicRangeLinear(int d)
    {
        var rng = new Random(13000 + d);
        Pix pixs = Pix.Create(21, 6, d);
        uint maxval = d == 8 ? 90u : d == 16 ? 3000u : 100000u;
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 21; j++)
                Naive.SetPixel(pixs, j, i, (uint)rng.NextInt64(0, maxval + 1));
        Naive.SetPixel(pixs, 3, 2, maxval);
        Pix pixd = pixs.MaxDynamicRange(L.L_LINEAR_SCALE);
        Assert.Equal(8, pixd.Depth);
        float factor = 255.0f / (float)maxval;
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 21; j++) {
                uint s = Naive.GetPixel(pixs, j, i);
                Assert.Equal((uint)(byte)(factor * (float)s + 0.5), Naive.GetPixel(pixd, j, i));
            }
        Assert.Equal(255u, Naive.GetPixel(pixd, 3, 2));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void MaxDynamicRangeLog(int d)
    {
        var rng = new Random(13100 + d);
        Pix pixs = Pix.Create(21, 6, d);
        uint maxval = d == 8 ? 200u : d == 16 ? 60000u : 3000000u;
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 21; j++)
                Naive.SetPixel(pixs, j, i, (uint)rng.NextInt64(0, maxval + 1));
        Naive.SetPixel(pixs, 5, 4, maxval);
        Pix pixd = pixs.MaxDynamicRange(L.L_LOG_SCALE);
        float[] tab = Pix.MakeLogBase2Tab();
        float factor = 255.0f / Pix.GetLogBase2((int)maxval, tab);
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 21; j++) {
                uint s = Naive.GetPixel(pixs, j, i);
                Assert.Equal((uint)(byte)(factor * Pix.GetLogBase2((int)s, tab) + 0.5), Naive.GetPixel(pixd, j, i));
            }
        Assert.Equal(255u, Naive.GetPixel(pixd, 5, 4));
    }

    [Fact]
    public void MaxDynamicRange4Log()
    {
        var rng = new Random(13200);
        Pix pixs = RandomPix(rng, 19, 5, 4);
        Naive.SetPixel(pixs, 0, 0, 15);
        Pix pixd = pixs.MaxDynamicRange(L.L_LOG_SCALE);
        Assert.Equal(8, pixd.Depth);
        float[] tab = Pix.MakeLogBase2Tab();
        float factor = 255.0f / Pix.GetLogBase2(15, tab);
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 19; j++) {
                uint s = Naive.GetPixel(pixs, j, i);
                Assert.Equal((uint)(byte)(factor * Pix.GetLogBase2((int)s, tab) + 0.5), Naive.GetPixel(pixd, j, i));
            }
    }

    [Fact]
    public void MaxDynamicRangeErrors()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 2).MaxDynamicRange(L.L_LINEAR_SCALE));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).MaxDynamicRange(0));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).MaxDynamicRangeRGB(L.L_LINEAR_SCALE));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 32).MaxDynamicRangeRGB(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaxDynamicRangeRGB(bool log)
    {
        var rng = new Random(13300);
        Pix pixs = Pix.Create(17, 7, 32);
        for (int i = 0; i < 7; i++)
            for (int j = 0; j < 17; j++)
                Naive.SetPixel(pixs, j, i, Rgb(rng.Next(0, 60), rng.Next(0, 80), rng.Next(0, 70)) | (uint)rng.Next(0, 256));
        Naive.SetPixel(pixs, 2, 3, Rgb(10, 80, 20) | 0x7f);
        Pix pixd = pixs.MaxDynamicRangeRGB(log ? L.L_LOG_SCALE : L.L_LINEAR_SCALE);

        float[] tab = Pix.MakeLogBase2Tab();
        float factor = log ? 255.0f / Pix.GetLogBase2(80, tab) : 255.0f / 80.0f;
        for (int i = 0; i < 7; i++)
            for (int j = 0; j < 17; j++) {
                uint s = Naive.GetPixel(pixs, j, i);
                uint exp = log ? Pix.LogScaleRGBVal(s, tab, factor) : Pix.LinearScaleRGBVal(s, factor);
                Assert.Equal(exp, Naive.GetPixel(pixd, j, i));
                // the alpha byte is passed through
                Assert.Equal(s & 0xff, Naive.GetPixel(pixd, j, i) & 0xff);
            }
        // The brightest component maps to 255
        Pix.ExtractRGBValues(Naive.GetPixel(pixd, 2, 3), out _, out int gmax, out _);
        Assert.Equal(255, gmax);
    }

    [Fact]
    public void MaxDynamicRangeRGBAllBlack()
    {
        Pix pixd = Pix.Create(5, 5, 32).MaxDynamicRangeRGB(L.L_LINEAR_SCALE);
        Assert.Equal(0u, Naive.GetPixel(pixd, 2, 2));
    }

    [Fact]
    public void LinearScaleRGBVal()
    {
        uint s = Rgb(10, 20, 30) | 0x5a;
        uint d = Pix.LinearScaleRGBVal(s, 2.0f);
        Assert.Equal(Rgb(20, 40, 60) | 0x5a, d);
        Assert.Equal(s, Pix.LinearScaleRGBVal(s, 1.0f));
        // rounding: 0.5 is added before truncation
        Assert.Equal(Rgb(1, 1, 2), Pix.LinearScaleRGBVal(Rgb(1, 1, 3), 0.5f));
    }

    /*------------------------------------------------------------*
     *                      Log base 2 lookup                     *
     *------------------------------------------------------------*/
    [Fact]
    public void LogBase2Table()
    {
        float[] tab = Pix.MakeLogBase2Tab();
        Assert.Equal(256, tab.Length);
        Assert.True(float.IsNegativeInfinity(tab[0]));
        Assert.Equal(0.0f, tab[1]);
        for (int p = 1; p <= 7; p++)
            Assert.Equal(p, tab[1 << p], 4);
        Assert.Equal(7.99435f, tab[255], 3);
        for (int i = 2; i < 256; i++)
            Assert.True(tab[i] > tab[i - 1]);
    }

    [Fact]
    public void GetLogBase2Ranges()
    {
        float[] tab = Pix.MakeLogBase2Tab();
        Assert.Equal(tab[255], Pix.GetLogBase2(255, tab));
        Assert.Equal(8.0f + tab[1], Pix.GetLogBase2(0x100, tab));
        Assert.Equal(8.0f + tab[255], Pix.GetLogBase2(0xffff, tab));
        Assert.Equal(16.0f + tab[1], Pix.GetLogBase2(0x10000, tab));
        Assert.Equal(16.0f + tab[200], Pix.GetLogBase2(200 << 16, tab));
        Assert.Equal(24.0f + tab[1], Pix.GetLogBase2(0x1000000, tab));
        Assert.Equal(24.0f + tab[127], Pix.GetLogBase2(int.MaxValue, tab));
    }
}
