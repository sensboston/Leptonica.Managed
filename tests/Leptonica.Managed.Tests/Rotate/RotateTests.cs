using System;
using Leptonica;
using Leptonica.Managed.Tests.Rop;

namespace Leptonica.Managed.Tests.Rotate;

/// <summary>Tests for the functions of rotate.c that do not depend on rotateam.c / rotateshear.c.</summary>
public class RotateTests
{
    private static Pix RandomPix(Random rng, int w, int h, int d)
    {
        Pix pix = Pix.Create(w, h, d);
        uint mask = d == 32 ? 0xffffffffu : (1u << d) - 1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                NaiveRop.SetPixel(pix, x, y, (uint)rng.NextInt64(0, 1L << 32) & mask);
        return pix;
    }

    private static uint InColor(int d, int incolor)
    {
        if (incolor == L.L_BRING_IN_WHITE)
            return d == 1 ? 0u : (d == 32 ? 0xffffffffu : (1u << d) - 1);
        return d == 1 ? 1u : 0u;
    }

    private static void AssertPixelsEqual(Pix expected, Pix actual, string what)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Depth, actual.Depth);
        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++)
                if (NaiveRop.GetPixel(expected, x, y) != NaiveRop.GetPixel(actual, x, y))
                    Assert.Fail($"{what}: pixel mismatch at ({x}, {y})");
    }

    private static Pix NaiveSampling(Pix pixs, int xcen, int ycen, float angle, int incolor)
    {
        int w = pixs.Width, h = pixs.Height, d = pixs.Depth;
        Pix pixd = Pix.Create(w, h, d);
        float sina = (float)Math.Sin(angle);
        float cosa = (float)Math.Cos(angle);
        uint bg = InColor(d, incolor);
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w; j++) {
                int xdif = xcen - j;
                int ydif = ycen - i;
                int x = xcen + (int)(-xdif * cosa - ydif * sina);
                int y = ycen + (int)(-ydif * cosa + xdif * sina);
                uint v = (x < 0 || x > w - 1 || y < 0 || y > h - 1) ? bg : NaiveRop.GetPixel(pixs, x, y);
                NaiveRop.SetPixel(pixd, j, i, v);
            }
        }
        return pixd;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void RotateBySamplingMatchesNaive(int d)
    {
        var rng = new Random(73000 + d);
        float[] angles = { 0.3f, -0.5f, 1.2f, 0.05f };
        foreach (int w in new[] { 1, 7, 33, 64 }) {
            foreach (int h in new[] { 1, 5, 40 }) {
                Pix pixs = RandomPix(rng, w, h, d);
                foreach (float angle in angles) {
                    foreach (int incolor in new[] { L.L_BRING_IN_WHITE, L.L_BRING_IN_BLACK }) {
                        Pix expected = NaiveSampling(pixs, w / 3, h / 2, angle, incolor);
                        Pix actual = pixs.RotateBySampling(w / 3, h / 2, angle, incolor);
                        AssertPixelsEqual(expected, actual, $"d={d} {w}x{h} angle={angle} incolor={incolor}");
                    }
                }
            }
        }
    }

    [Fact]
    public void SmallAngleReturnsClone()
    {
        Pix pixs = Pix.Create(20, 10, 8);
        Assert.Same(pixs, pixs.RotateBySampling(10, 5, 0.0005f, L.L_BRING_IN_WHITE));
        Assert.Same(pixs, pixs.EmbedForRotation(0.0005f, L.L_BRING_IN_WHITE, 20, 10));
        Assert.Same(pixs, pixs.Rotate(-0.0005f, L.L_ROTATE_SAMPLING, L.L_BRING_IN_WHITE, 0, 0));
    }

    [Fact]
    public void EmbedForRotationGrowsAndCenters()
    {
        var rng = new Random(74000);
        foreach (int d in new[] { 1, 8, 32 }) {
            Pix pixs = RandomPix(rng, 100, 50, d);
            float angle = 0.5f;
            double cosa = Math.Cos(angle), sina = Math.Sin(angle);
            int w1 = (int)(Math.Abs(100 * cosa - 50 * sina) + 0.5);
            int w2 = (int)(Math.Abs(-100 * cosa - 50 * sina) + 0.5);
            int h1 = (int)(Math.Abs(100 * sina + 50 * cosa) + 0.5);
            int h2 = (int)(Math.Abs(-100 * sina + 50 * cosa) + 0.5);
            int wnew = Math.Max(100, Math.Max(w1, w2));
            int hnew = Math.Max(50, Math.Max(h1, h2));

            foreach (int incolor in new[] { L.L_BRING_IN_WHITE, L.L_BRING_IN_BLACK }) {
                Pix pixd = pixs.EmbedForRotation(angle, incolor, 100, 50);
                Assert.Equal(wnew, pixd.Width);
                Assert.Equal(hnew, pixd.Height);
                int xoff = (wnew - 100) / 2, yoff = (hnew - 50) / 2;
                uint bg = InColor(d, incolor);
                for (int y = 0; y < hnew; y++) {
                    for (int x = 0; x < wnew; x++) {
                        bool inside = x >= xoff && x < xoff + 100 && y >= yoff && y < yoff + 50;
                        uint expected = inside ? NaiveRop.GetPixel(pixs, x - xoff, y - yoff) : bg;
                        if (NaiveRop.GetPixel(pixd, x, y) != expected)
                            Assert.Fail($"d={d} incolor={incolor}: mismatch at ({x}, {y})");
                    }
                }
            }

            // already big enough for the stated original size: clone
            Assert.Same(pixs, pixs.EmbedForRotation(angle, L.L_BRING_IN_WHITE, 20, 10));
        }
    }

    [Fact]
    public void RotateBySamplingTypeUsesImageCenter()
    {
        var rng = new Random(75000);
        Pix pixs = RandomPix(rng, 41, 30, 8);
        // |angle| > LimitShearAngle: a shear request falls back to sampling
        Pix actual = pixs.Rotate(0.5f, L.L_ROTATE_SHEAR, L.L_BRING_IN_BLACK, 0, 0);
        AssertPixelsEqual(NaiveSampling(pixs, 41 / 2, 30 / 2, 0.5f, L.L_BRING_IN_BLACK), actual, "8 bpp");

        // 1 bpp with a large angle is rotated by sampling whatever the request
        Pix pix1 = RandomPix(rng, 50, 20, 1);
        actual = pix1.Rotate(0.2f, L.L_ROTATE_AREA_MAP, L.L_BRING_IN_WHITE, 0, 0);
        AssertPixelsEqual(NaiveSampling(pix1, 25, 10, 0.2f, L.L_BRING_IN_WHITE), actual, "1 bpp");
    }

    [Fact]
    public void InvalidArguments()
    {
        Pix pixs = Pix.Create(4, 4, 8);
        Assert.Throws<ArgumentException>(() => pixs.Rotate(0.5f, 0, L.L_BRING_IN_WHITE, 0, 0));
        Assert.Throws<ArgumentException>(() => pixs.Rotate(0.5f, L.L_ROTATE_SAMPLING, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => pixs.RotateBySampling(2, 2, 0.5f, 0));
        Assert.Throws<ArgumentException>(() => pixs.EmbedForRotation(0.5f, 0, 4, 4));
        Assert.Throws<ArgumentException>(() => pixs.RotateBinaryNice(0.1f, L.L_BRING_IN_WHITE));
        Assert.Throws<ArgumentException>(() => pixs.RotateWithAlpha(0.1f, null, 1.0f));
    }
}
