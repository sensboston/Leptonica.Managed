using System;
using Leptonica;
using Naive = Leptonica.Managed.Tests.Rop.NaiveRop;

namespace Leptonica.Managed.Tests.PixCore;

/// <summary>
/// Tests of pix2.c.  Pixels are checked against the plain bit arithmetic of NaiveRop,
/// which shares no code with the word-oriented implementation.
/// </summary>
public class Pix2Tests
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

    private static void AssertPixEqual(Pix expected, Pix actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Depth, actual.Depth);
        Assert.Equal(expected.Data, actual.Data);
    }

    /*------------------------------------------------------------*
     *                        Pixel poking                        *
     *------------------------------------------------------------*/
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void GetPixelMatchesReference(int d)
    {
        Random rng = new(d);
        foreach (int w in new[] { 1, 7, 31, 32, 33, 70 }) {
            Pix pix = RandomPix(rng, w, 5, d);
            for (int y = 0; y < 5; y++) {
                for (int x = 0; x < w; x++) {
                    Assert.True(pix.GetPixel(x, y, out uint val));
                    Assert.Equal(Naive.GetPixel(pix, x, y), val);
                }
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void SetPixelMatchesReference(int d)
    {
        Random rng = new(100 + d);
        foreach (int w in new[] { 1, 7, 31, 32, 33, 70 }) {
            Pix pix = Pix.Create(w, 4, d);
            Pix expected = Pix.Create(w, 4, d);
            for (int k = 0; k < 200; k++) {
                int x = rng.Next(w), y = rng.Next(4);
                uint val = (uint)rng.NextInt64();
                Assert.True(pix.SetPixel(x, y, val));
                // Values are masked to the depth; for 1 bpp any nonzero value sets the bit.
                uint expectedVal = d == 1 ? (val != 0 ? 1u : 0u) : val & Mask(d);
                Naive.SetPixel(expected, x, y, expectedVal);
            }
            AssertPixEqual(expected, pix);
        }
    }

    [Fact]
    public void PixelAccessOutsideImageFailsSilently()
    {
        Pix pix = Pix.Create(5, 4, 8);
        Naive.SetPixel(pix, 0, 0, 77);
        foreach ((int x, int y) in new[] { (-1, 0), (0, -1), (5, 0), (0, 4), (100, 100) }) {
            Assert.False(pix.GetPixel(x, y, out uint val));
            Assert.Equal(0u, val);
            Assert.False(pix.SetPixel(x, y, 1));
            Assert.False(pix.ClearPixel(x, y));
            Assert.False(pix.FlipPixel(x, y));
        }
        Assert.Equal(77u, Naive.GetPixel(pix, 0, 0));
    }

    [Fact]
    public void PixelAccessRejectsDepth24()
    {
        Pix pix = Pix.Create(5, 4, 24);
        Assert.Throws<ArgumentException>(() => pix.GetPixel(1, 1, out _));
        Assert.Throws<ArgumentException>(() => pix.SetPixel(1, 1, 1));
        Assert.Throws<ArgumentException>(() => pix.ClearPixel(1, 1));
        Assert.Throws<ArgumentException>(() => pix.FlipPixel(1, 1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void ClearAndFlipPixel(int d)
    {
        Random rng = new(200 + d);
        Pix pix = RandomPix(rng, 37, 3, d);
        Pix before = Pix.Copy(null, pix);
        for (int y = 0; y < 3; y++) {
            for (int x = 0; x < 37; x += 2) {
                Assert.True(pix.FlipPixel(x, y));
                Assert.Equal(Naive.GetPixel(before, x, y) ^ Mask(d), Naive.GetPixel(pix, x, y));
                Assert.True(pix.FlipPixel(x, y));
                Assert.Equal(Naive.GetPixel(before, x, y), Naive.GetPixel(pix, x, y));
                Assert.True(pix.ClearPixel(x, y));
                Assert.Equal(0u, Naive.GetPixel(pix, x, y));
                // The neighbors are untouched.
                if (x + 1 < 37)
                    Assert.Equal(Naive.GetPixel(before, x + 1, y), Naive.GetPixel(pix, x + 1, y));
            }
        }
    }

    [Fact]
    public void RgbPixelRoundTrip()
    {
        Pix pix = Pix.Create(6, 3, 32);
        Assert.True(pix.SetRGBPixel(4, 2, 10, 20, 250));
        Assert.True(pix.GetRGBPixel(4, 2, out int r, out int g, out int b));
        Assert.Equal((10, 20, 250), (r, g, b));
        Assert.Equal(0x0a14fa00u, pix.Data[2 * pix.Wpl + 4]);
        Assert.False(pix.SetRGBPixel(6, 0, 1, 2, 3));
        Assert.False(pix.GetRGBPixel(0, 3, out r, out g, out b));
        Assert.Equal((0, 0, 0), (r, g, b));
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 3, 8).GetRGBPixel(0, 0, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 3, 8).SetRGBPixel(0, 0, 1, 1, 1));
    }

    [Fact]
    public void SetPixelLowSetsAllDepths()
    {
        foreach (int d in new[] { 1, 2, 4, 8, 16, 32 }) {
            Pix pix = Pix.Create(40, 2, d);
            Pix expected = Pix.Create(40, 2, d);
            for (int x = 0; x < 40; x++) {
                uint val = (uint)(x * 7 + 1) & Mask(d);
                Pix.SetPixelLow(pix.Data.AsSpan(pix.Wpl), x, d, val);
                Naive.SetPixel(expected, x, 1, val);
            }
            AssertPixEqual(expected, pix);
        }
    }

    [Fact]
    public void GetRandomPixelReturnsPixelWithinBounds()
    {
        Pix pix = Pix.Create(4, 3, 8);
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 4; x++)
                pix.SetPixel(x, y, (uint)(y * 10 + x + 1));

        pix.GetRandomPixel(out uint val, out int rx, out int ry);
        Assert.InRange(rx, 0, 3);
        Assert.InRange(ry, 0, 2);
        pix.GetPixel(rx, ry, out uint expected);
        Assert.Equal(expected, val);
    }

    /*------------------------------------------------------------*
     *               Black/white, full image set/clear            *
     *------------------------------------------------------------*/
    [Theory]
    [InlineData(1, 0u, 1u)]
    [InlineData(2, 3u, 0u)]
    [InlineData(4, 15u, 0u)]
    [InlineData(8, 255u, 0u)]
    [InlineData(16, 65535u, 0u)]
    [InlineData(32, 0xffffff00u, 0u)]
    public void BlackOrWhiteValue(int d, uint white, uint black)
    {
        Pix pix = Pix.Create(5, 5, d);
        Assert.Equal(white, pix.GetBlackOrWhiteVal(L.L_GET_WHITE_VAL));
        Assert.Equal(black, pix.GetBlackOrWhiteVal(L.L_GET_BLACK_VAL));
        Assert.Throws<ArgumentException>(() => pix.GetBlackOrWhiteVal(0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void SetAndClearAll(int d)
    {
        Pix pix = Pix.Create(33, 3, d);
        pix.SetAll();
        Assert.All(pix.Data, word => Assert.Equal(0xffffffffu, word));
        pix.ClearAll();
        Assert.All(pix.Data, word => Assert.Equal(0u, word));

        pix.SetBlackOrWhite(L.L_SET_WHITE);
        Assert.Equal(d == 1 ? 0u : 0xffffffffu, pix.Data[0]);
        pix.SetBlackOrWhite(L.L_SET_BLACK);
        Assert.Equal(d == 1 ? 0xffffffffu : 0u, pix.Data[0]);
        Assert.Throws<ArgumentException>(() => pix.SetBlackOrWhite(0));
    }

    [Fact]
    public void SetAllNeedsFullColormap()
    {
        Pix pix = Pix.CreateWithCmap(4, 4, 2, L.L_SET_WHITE);
        Assert.Throws<ArgumentException>(() => pix.SetAll());
        pix.Colormap!.AddColor(1, 2, 3);
        pix.Colormap.AddColor(4, 5, 6);
        pix.Colormap.AddColor(7, 8, 9);
        pix.SetAll();
        Assert.Equal(0xffffffffu, pix.Data[0]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void SetAllArbitraryFillsEveryPixel(int d)
    {
        Random rng = new(300 + d);
        foreach (int w in new[] { 1, 5, 31, 32, 33, 50 }) {
            Pix pix = RandomPix(rng, w, 3, d);
            uint val = (uint)rng.NextInt64() & Mask(d);
            pix.SetAllArbitrary(val);
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < w; x++)
                    Assert.Equal(val, Naive.GetPixel(pix, x, y));
        }
    }

    [Fact]
    public void SetAllArbitraryClampsToDepthAndColormap()
    {
        Pix pix = Pix.Create(5, 2, 4);
        pix.SetAllArbitrary(200);   // too large for depth: maxval
        Assert.Equal(15u, Naive.GetPixel(pix, 3, 1));

        Pix cpix = Pix.CreateWithCmap(5, 2, 4, L.L_SET_BLACK);
        cpix.Colormap!.AddColor(1, 1, 1);
        cpix.SetAllArbitrary(9);    // not in the colormap: the last color
        Assert.Equal(1u, Naive.GetPixel(cpix, 4, 0));
    }

    [Fact]
    public void SetBlackOrWhiteWithColormapAddsMissingColor()
    {
        Pix pix = Pix.CreateWithCmap(5, 2, 8, L.L_SET_BLACK);
        Assert.Equal(1, pix.Colormap!.Count);
        pix.SetBlackOrWhite(L.L_SET_WHITE);
        Assert.Equal(2, pix.Colormap.Count);
        Assert.Equal(1u, Naive.GetPixel(pix, 4, 1));
        Assert.Equal(1u, pix.GetBlackOrWhiteVal(L.L_GET_WHITE_VAL));
        Assert.Equal(0u, pix.GetBlackOrWhiteVal(L.L_GET_BLACK_VAL));
        Assert.Equal(2, pix.Colormap.Count);
    }

    [Fact]
    public void SetComponentArbitrary()
    {
        Pix pix = Pix.Create(7, 3, 32);
        pix.SetAllArbitrary(0x11223344);
        pix.SetComponentArbitrary(L.COLOR_GREEN, 0xab);
        Assert.Equal(0x11ab3344u, pix.Data[0]);
        pix.SetComponentArbitrary(L.L_ALPHA_CHANNEL, 255);
        Assert.Equal(0x11ab33ffu, pix.Data[pix.Data.Length - 1]);
        pix.SetComponentArbitrary(L.COLOR_RED, 0);
        pix.SetComponentArbitrary(L.COLOR_BLUE, 1);
        Assert.Equal(0x00ab01ffu, pix.Data[3]);

        Assert.Throws<ArgumentException>(() => pix.SetComponentArbitrary(4, 1));
        Assert.Throws<ArgumentException>(() => pix.SetComponentArbitrary(0, 256));
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 3, 8).SetComponentArbitrary(0, 1));
    }

    /*------------------------------------------------------------*
     *                    Rectangular regions                     *
     *------------------------------------------------------------*/
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void ClearAndSetInRect(int d)
    {
        Random rng = new(400 + d);
        foreach ((int x, int y, int bw, int bh) in new[] { (3, 2, 20, 4), (-4, -3, 10, 6), (30, 5, 40, 40), (0, 0, 45, 12) }) {
            Pix pix = RandomPix(rng, 45, 12, d);
            Pix cleared = Pix.Copy(null, pix);
            Pix set = Pix.Copy(null, pix);
            cleared.ClearInRect(Box.Create(x, y, bw, bh));
            set.SetInRect(Box.Create(x, y, bw, bh));
            for (int i = 0; i < 12; i++) {
                for (int j = 0; j < 45; j++) {
                    bool inside = j >= x && j < x + bw && i >= y && i < y + bh;
                    uint orig = Naive.GetPixel(pix, j, i);
                    Assert.Equal(inside ? 0u : orig, Naive.GetPixel(cleared, j, i));
                    Assert.Equal(inside ? Mask(d) : orig, Naive.GetPixel(set, j, i));
                }
            }
        }
    }

    [Fact]
    public void SetInRectNeedsFullColormap()
    {
        Pix pix = Pix.CreateWithCmap(4, 4, 2, L.L_SET_WHITE);
        Assert.Throws<ArgumentException>(() => pix.SetInRect(Box.Create(0, 0, 2, 2)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void SetInRectArbitrary(int d)
    {
        Random rng = new(500 + d);
        foreach ((int x, int y, int bw, int bh) in new[] { (3, 2, 20, 4), (-4, -3, 10, 6), (30, 5, 40, 40) }) {
            foreach (uint val in new[] { 0u, 1u, Mask(d) / 3 + 1, Mask(d) }) {
                Pix pix = RandomPix(rng, 45, 12, d);
                Pix result = Pix.Copy(null, pix);
                result.SetInRectArbitrary(Box.Create(x, y, bw, bh), val);
                uint expectedVal = d == 32 ? Math.Min(val, 0xffffff00u) : val;
                if (d == 32 && val == 0xffffffffu)
                    expectedVal = 0xffffff00u;
                for (int i = 0; i < 12; i++) {
                    for (int j = 0; j < 45; j++) {
                        bool inside = j >= x && j < x + bw && i >= y && i < y + bh;
                        uint orig = Naive.GetPixel(pix, j, i);
                        uint got = Naive.GetPixel(result, j, i);
                        if (inside && d == 32 && expectedVal == 0xffffff00u)
                            Assert.Equal(0xffffffffu, got);   // set to all ones: SetInRect
                        else
                            Assert.Equal(inside ? expectedVal : orig, got);
                    }
                }
            }
        }
    }

    [Fact]
    public void SetInRectArbitraryOutsideImageThrows()
    {
        Pix pix = Pix.Create(10, 10, 8);
        Assert.Throws<ArgumentException>(() => pix.SetInRectArbitrary(Box.Create(20, 20, 5, 5), 7));
        Assert.Throws<ArgumentException>(() => Pix.Create(10, 10, 24).SetInRectArbitrary(Box.Create(0, 0, 5, 5), 7));
    }

    /*------------------------------------------------------------*
     *                         Pad bits                           *
     *------------------------------------------------------------*/
    [Theory]
    [InlineData(1, 33)]
    [InlineData(2, 17)]
    [InlineData(4, 9)]
    [InlineData(8, 5)]
    [InlineData(16, 3)]
    [InlineData(1, 64)]
    [InlineData(32, 3)]
    public void SetPadBitsBandOnlyTouchesBand(int d, int w)
    {
        Pix pix = Pix.Create(w, 6, d);
        Assert.True(pix.SetPadBitsBand(2, 3, 1));
        int usedBits = (int)(((long)w * d) % 32);
        for (int i = 0; i < 6; i++) {
            uint last = pix.Data[i * pix.Wpl + pix.Wpl - 1];
            bool inBand = i >= 2 && i < 5;
            uint padmask = usedBits == 0 ? 0u : 0xffffffffu >> usedBits;
            Assert.Equal(inBand ? padmask : 0u, last);
        }
        // Pad bits are not pixels.
        Assert.True(pix.SetPadBitsBand(-5, 100, 0));
        Assert.All(pix.Data, word => Assert.Equal(0u, word));
    }

    [Fact]
    public void SetPadBitsBandErrors()
    {
        Pix pix = Pix.Create(5, 4, 8);
        Assert.Throws<ArgumentException>(() => pix.SetPadBitsBand(4, 1, 1));
        Assert.False(Pix.Create(5, 4, 24).SetPadBitsBand(0, 1, 1));
    }

    /*------------------------------------------------------------*
     *                       Border pixels                        *
     *------------------------------------------------------------*/
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(32)]
    public void SetOrClearBorder(int d)
    {
        Random rng = new(600 + d);
        Pix pix = RandomPix(rng, 30, 20, d);
        Pix set = Pix.Copy(null, pix);
        Pix clr = Pix.Copy(null, pix);
        set.SetOrClearBorder(3, 4, 2, 5, L.PIX_SET);
        clr.SetOrClearBorder(3, 4, 2, 5, L.PIX_CLR);
        for (int i = 0; i < 20; i++) {
            for (int j = 0; j < 30; j++) {
                bool border = j < 3 || j >= 30 - 4 || i < 2 || i >= 20 - 5;
                uint orig = Naive.GetPixel(pix, j, i);
                Assert.Equal(border ? Mask(d) : orig, Naive.GetPixel(set, j, i));
                Assert.Equal(border ? 0u : orig, Naive.GetPixel(clr, j, i));
            }
        }
        Assert.Throws<ArgumentException>(() => pix.SetOrClearBorder(1, 1, 1, 1, L.PIX_SRC));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void SetBorderVal(int d)
    {
        Random rng = new(700 + d);
        Pix pix = RandomPix(rng, 30, 20, d);
        Pix result = Pix.Copy(null, pix);
        uint val = 0x12345678;
        result.SetBorderVal(3, 4, 2, 5, val);
        for (int i = 0; i < 20; i++) {
            for (int j = 0; j < 30; j++) {
                bool border = j < 3 || j >= 30 - 4 || i < 2 || i >= 20 - 5;
                uint orig = Naive.GetPixel(pix, j, i);
                Assert.Equal(border ? val & Mask(d) : orig, Naive.GetPixel(result, j, i));
            }
        }
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 4).SetBorderVal(1, 1, 1, 1, 1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(32)]
    public void SetBorderRingVal(int d)
    {
        Pix pix = Pix.Create(11, 9, d);
        uint val = d == 32 ? 0xdeadbe00u : Mask(d);
        pix.SetBorderRingVal(2, val);
        for (int i = 0; i < 9; i++) {
            for (int j = 0; j < 11; j++) {
                bool onRing = (i == 1 && j >= 1 && j <= 9) || (i == 7 && j >= 1 && j <= 9) ||
                              (j == 1 && i >= 1 && i <= 7) || (j == 9 && i >= 1 && i <= 7);
                Assert.Equal(onRing ? val : 0u, Naive.GetPixel(pix, j, i));
            }
        }
    }

    [Fact]
    public void SetBorderRingValErrors()
    {
        Pix pix = Pix.Create(11, 9, 8);
        Assert.Throws<ArgumentException>(() => pix.SetBorderRingVal(0, 1));
        Assert.Throws<ArgumentException>(() => pix.SetBorderRingVal(5, 1));   // h < 2 * dist + 1
        Assert.Throws<ArgumentException>(() => pix.SetBorderRingVal(1, 256));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(32)]
    public void SetMirroredBorder(int d)
    {
        Random rng = new(800 + d);
        Pix pix = RandomPix(rng, 24, 17, d);
        Pix result = Pix.Copy(null, pix);
        result.SetMirroredBorder(4, 3, 5, 2);

        // Reference: left/right first on the interior rows, then top/bottom on full rows.
        Pix expected = Pix.Copy(null, pix);
        for (int i = 5; i < 17 - 2; i++) {
            for (int j = 0; j < 4; j++)
                Naive.SetPixel(expected, 4 - 1 - j, i, Naive.GetPixel(expected, 4 + j, i));
            for (int j = 0; j < 3; j++)
                Naive.SetPixel(expected, 24 - 3 + j, i, Naive.GetPixel(expected, 24 - 3 - 1 - j, i));
        }
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 24; j++)
                Naive.SetPixel(expected, j, 5 - 1 - i, Naive.GetPixel(expected, j, 5 + i));
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 24; j++)
                Naive.SetPixel(expected, j, 17 - 2 + i, Naive.GetPixel(expected, j, 17 - 2 - 1 - i));
        AssertPixEqual(expected, result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(32)]
    public void CopyBorder(int d)
    {
        Random rng = new(900 + d);
        Pix pixs = RandomPix(rng, 24, 17, d);
        Pix pixd = Pix.CopyBorder(null, pixs, 4, 3, 5, 2);
        for (int i = 0; i < 17; i++) {
            for (int j = 0; j < 24; j++) {
                bool border = j < 4 || j >= 24 - 3 || i < 5 || i >= 17 - 2;
                Assert.Equal(border ? Naive.GetPixel(pixs, j, i) : 0u, Naive.GetPixel(pixd, j, i));
            }
        }

        // Into an existing pix of the same size.
        Pix other = Pix.Create(24, 17, d);
        Assert.Same(other, Pix.CopyBorder(other, pixs, 4, 3, 5, 2));
        AssertPixEqual(pixd, other);
        Assert.Same(pixs, Pix.CopyBorder(pixs, pixs, 4, 3, 5, 2));
        Assert.Throws<ArgumentException>(() => Pix.CopyBorder(Pix.Create(5, 5, d), pixs, 1, 1, 1, 1));
    }

    [Fact]
    public void BlendInRect()
    {
        Pix pix = Pix.Create(6, 4, 32);
        pix.SetAllArbitrary(0x64c80000);   // r = 100, g = 200, b = 0
        pix.BlendInRect(Box.Create(-2, 1, 4, 2), 0xff000000, 0.5f);
        for (int i = 0; i < 4; i++) {
            for (int j = 0; j < 6; j++) {
                bool inside = j < 2 && i >= 1 && i < 3;
                // (int)((1. - 0.5) * 100 + 0.5 * 255) = 177, (int)(0.5 * 200) = 100
                Assert.Equal(inside ? 0xb1640000u : 0x64c80000u, pix.Data[i * pix.Wpl + j]);
            }
        }
        pix.BlendInRect(null, 0, 1.0f);
        Assert.All(pix.Data, word => Assert.Equal(0u, word));
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 3, 8).BlendInRect(null, 0, 0.5f));
    }

    [Fact]
    public void SetAllGray()
    {
        Pix p1 = Pix.Create(9, 2, 1);
        p1.SetAllGray(127);
        Assert.Equal(0xffffffffu, p1.Data[0]);
        p1.SetAllGray(128);
        Assert.Equal(0u, p1.Data[0]);

        Pix p4 = Pix.Create(9, 2, 4);
        p4.SetAllGray(0xa5);
        Assert.Equal(0xau, Naive.GetPixel(p4, 8, 1));

        Pix p8 = Pix.Create(9, 2, 8);
        p8.SetAllGray(300);   // clamped
        Assert.Equal(255u, Naive.GetPixel(p8, 3, 1));
        p8.SetAllGray(-4);
        Assert.Equal(0u, Naive.GetPixel(p8, 3, 1));

        Pix p16 = Pix.Create(9, 2, 16);
        p16.SetAllGray(0x12);
        Assert.Equal(0x1212u, Naive.GetPixel(p16, 8, 1));

        Pix p32 = Pix.Create(9, 2, 32);
        p32.SetAllGray(0x33);
        Assert.Equal(0x33333300u, Naive.GetPixel(p32, 8, 1));

        // 4 spp: the alpha component is preserved.
        Pix p4spp = Pix.Create(9, 2, 32);
        p4spp.SetAllArbitrary(0x000000ab);
        p4spp.Spp = 4;
        p4spp.SetAllGray(0x44);
        Assert.Equal(0x444444abu, Naive.GetPixel(p4spp, 5, 1));

        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 24).SetAllGray(1));
    }

    /*------------------------------------------------------------*
     *                    Add and remove border                   *
     *------------------------------------------------------------*/
    private static readonly int[] AllDepths = { 1, 2, 4, 8, 16, 32 };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void AddBorderGeneralPlacesContentAndBorderValue(int d)
    {
        Random rng = new(1000 + d);
        Pix pixs = RandomPix(rng, 13, 7, d);
        pixs.SetResolution(200, 100);
        foreach (uint val in new uint[] { 0, 1, Mask(d) / 3 + 1, Mask(d) }) {
            Pix pixd = pixs.AddBorderGeneral(3, 5, 2, 4, val);
            Assert.Equal((13 + 3 + 5, 7 + 2 + 4, d), (pixd.Width, pixd.Height, pixd.Depth));
            Assert.Equal((200, 100), (pixd.XRes, pixd.YRes));
            uint border = d == 32 && val == 0xffffffffu ? 0xffffffffu : val;
            for (int i = 0; i < pixd.Height; i++) {
                for (int j = 0; j < pixd.Width; j++) {
                    bool inside = j >= 3 && j < 3 + 13 && i >= 2 && i < 2 + 7;
                    uint got = Naive.GetPixel(pixd, j, i);
                    if (inside)
                        Assert.Equal(Naive.GetPixel(pixs, j - 3, i - 2), got);
                    else if (d == 32 && val >= 0xffffff00u)
                        Assert.Equal(0xffffffffu, got);   // val >= maxval: PIX_SET
                    else if (val >= Mask(d))
                        Assert.Equal(Mask(d), got);
                    else
                        Assert.Equal(val, got);
                }
            }
            _ = border;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void AddThenRemoveBorderRoundTrips(int d)
    {
        Random rng = new(1100 + d);
        foreach (int w in new[] { 1, 9, 32, 33 }) {
            Pix pixs = RandomPix(rng, w, 6, d);
            Pix added = pixs.AddBorder(5, 1);
            Assert.Equal((w + 10, 16), (added.Width, added.Height));
            Pix removed = added.RemoveBorder(5);
            AssertPixEqual(pixs, removed);

            Pix gen = pixs.AddBorderGeneral(1, 2, 3, 4, 0);
            AssertPixEqual(pixs, gen.RemoveBorderGeneral(1, 2, 3, 4));
        }
        Pix pix = Pix.Create(4, 4, d);
        Assert.Same(pix, pix.AddBorder(0, 1));
        Assert.Same(pix, pix.RemoveBorder(0));
    }

    [Fact]
    public void BorderErrors()
    {
        Pix pix = Pix.Create(10, 8, 8);
        Assert.Throws<ArgumentException>(() => pix.AddBorderGeneral(-1, 0, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => pix.RemoveBorderGeneral(-1, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => pix.RemoveBorderGeneral(5, 5, 0, 0));   // width 0
        Assert.Throws<ArgumentException>(() => pix.RemoveBorderGeneral(0, 0, 4, 5));   // height < 0
        Assert.Throws<ArgumentException>(() => pix.AddBlackOrWhiteBorder(1, 1, 1, 1, 0));
        Assert.Throws<ArgumentException>(() => pix.AddMirroredBorder(11, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => pix.AddRepeatedBorder(0, 0, 9, 0));
        Assert.Throws<ArgumentException>(() => pix.AddMixedBorder(0, 11, 0, 0));
    }

    [Fact]
    public void AddBorderKeepsColormap()
    {
        Pix pixs = Pix.CreateWithCmap(5, 4, 8, L.L_SET_WHITE);
        pixs.Colormap!.AddColor(10, 20, 30);
        pixs.SetPixel(2, 2, 1);
        Pix pixd = pixs.AddBorder(2, 1);
        Assert.NotNull(pixd.Colormap);
        Assert.Equal(2, pixd.Colormap!.Count);
        Assert.Equal(1u, Naive.GetPixel(pixd, 4, 4));
        Assert.Equal(1u, Naive.GetPixel(pixd, 0, 0));   // border value is a colormap index
        Assert.Equal(0u, Naive.GetPixel(pixd, 2, 2));
        Pix back = pixd.RemoveBorder(2);
        Assert.Equal(pixs.Data, back.Data);
        Assert.NotNull(back.Colormap);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(32)]
    public void AddBlackOrWhiteBorder(int d)
    {
        Random rng = new(1200 + d);
        Pix pixs = RandomPix(rng, 11, 6, d);
        Pix black = pixs.AddBlackOrWhiteBorder(2, 3, 1, 4, L.L_GET_BLACK_VAL);
        Pix white = pixs.AddBlackOrWhiteBorder(2, 3, 1, 4, L.L_GET_WHITE_VAL);
        uint blackval = d == 1 ? 1u : 0u;
        uint whiteval = d == 1 ? 0u : (d == 32 ? 0xffffffffu : Mask(d));
        for (int i = 0; i < 11; i++) {
            for (int j = 0; j < 16; j++) {
                bool inside = j >= 2 && j < 13 && i >= 1 && i < 7;
                if (inside) {
                    Assert.Equal(Naive.GetPixel(pixs, j - 2, i - 1), Naive.GetPixel(black, j, i));
                    Assert.Equal(Naive.GetPixel(pixs, j - 2, i - 1), Naive.GetPixel(white, j, i));
                } else {
                    Assert.Equal(blackval, Naive.GetPixel(black, j, i));
                    Assert.Equal(whiteval, Naive.GetPixel(white, j, i));
                }
            }
        }
    }

    [Fact]
    public void AddMultipleBlackWhiteBorders()
    {
        Pix pixs = Pix.Create(4, 3, 8);
        pixs.SetAll();   // white
        Pix pixd = pixs.AddMultipleBlackWhiteBorders(2, 0, 1, 3, 600, 1);   // 600 is skipped
        Assert.Equal((4 + 2 * (2 + 1 + 3 + 1), 3 + 2 * (2 + 1 + 3 + 1)), (pixd.Width, pixd.Height));
        // Rings from the outside: white(1), white(3)... the last border added is the outermost.
        // Order of addition: black 2, black 1, white 3, white 1 (the white 0 is skipped).
        Assert.Equal(255u, Naive.GetPixel(pixd, 0, 0));       // outermost: white
        Assert.Equal(255u, Naive.GetPixel(pixd, 1, 1));       // white 3 ring
        Assert.Equal(255u, Naive.GetPixel(pixd, 3, 3));
        Assert.Equal(0u, Naive.GetPixel(pixd, 4, 4));         // black 1 ring
        Assert.Equal(0u, Naive.GetPixel(pixd, 5, 5));         // black 2 ring
        Assert.Equal(0u, Naive.GetPixel(pixd, 6, 6));
        Assert.Equal(255u, Naive.GetPixel(pixd, 7, 7));       // content
        Assert.Same(pixs, pixs.AddMultipleBlackWhiteBorders(0, 0, 0, 0, 0, 0));
    }

    [Theory]
    [InlineData(100, 50, 40, 20)]
    [InlineData(100, 50, 41, 0)]
    [InlineData(100, 50, 0, 21)]
    [InlineData(100, 50, 100, 50)]
    [InlineData(100, 50, 200, 0)]
    [InlineData(100, 50, 0, 0)]
    [InlineData(100, 50, 99, 49)]
    public void RemoveBorderToSize(int w, int h, int wd, int hd)
    {
        Pix pix = Pix.Create(w, h, 8);
        Pix result = pix.RemoveBorderToSize(wd, hd);
        if ((wd <= 0 || wd >= w) && (hd <= 0 || hd >= h)) {
            Assert.Same(pix, result);
        } else if (wd > 0 && wd < w) {
            Assert.Equal(wd, result.Width);
            // Only the width is reduced when the first test passes (C: else-if).
            Assert.Equal(hd > 0 && hd < h ? hd : h, result.Height);
        } else {
            Assert.Equal(w, result.Width);
            Assert.Equal(hd, result.Height);
        }
    }

    [Fact]
    public void RemoveBorderToSizeSplitsOddAmountToRightAndBottom()
    {
        Pix pix = Pix.Create(10, 9, 8);
        for (int j = 0; j < 10; j++)
            Naive.SetPixel(pix, j, 0, (uint)j);
        for (int i = 0; i < 9; i++)
            Naive.SetPixel(pix, 0, i, (uint)(100 + i));
        Pix result = pix.RemoveBorderToSize(5, 4);   // remove 5 of width: 2 left, 3 right; 5 of height: 2 top, 3 bottom
        Assert.Equal((5, 4), (result.Width, result.Height));
        Assert.Equal(Naive.GetPixel(pix, 2, 2), Naive.GetPixel(result, 0, 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void MirroredRepeatedMixedContinuedBorders(int d)
    {
        Random rng = new(1300 + d);
        const int w = 9, h = 6, l = 3, r = 4, t = 2, b = 5;
        Pix pixs = RandomPix(rng, w, h, d);
        Pix mirrored = pixs.AddMirroredBorder(l, r, t, b);
        Pix repeated = pixs.AddRepeatedBorder(l, r, t, b);
        Pix mixed = pixs.AddMixedBorder(l, r, t, b);
        Pix continued = pixs.AddContinuedBorder(l, r, t, b);

        int wd = w + l + r, hd = h + t + b;
        foreach (Pix pix in new[] { mirrored, repeated, mixed, continued })
            Assert.Equal((wd, hd), (pix.Width, pix.Height));

        // Mirror: reflect across the image edge, with the edge pixel repeated (-1 -> 0).
        static int Mirror(int v, int n)
        {
            int m = v % (2 * n);
            if (m < 0) m += 2 * n;
            return m < n ? m : 2 * n - 1 - m;
        }
        static int Wrap(int v, int n) => ((v % n) + n) % n;
        static int Clamp(int v, int n) => Math.Min(Math.Max(v, 0), n - 1);

        for (int i = 0; i < hd; i++) {
            for (int j = 0; j < wd; j++) {
                int sx = j - l, sy = i - t;
                Assert.Equal(Naive.GetPixel(pixs, Mirror(sx, w), Mirror(sy, h)), Naive.GetPixel(mirrored, j, i));
                Assert.Equal(Naive.GetPixel(pixs, Wrap(sx, w), Wrap(sy, h)), Naive.GetPixel(repeated, j, i));
                Assert.Equal(Naive.GetPixel(pixs, Mirror(sx, w), Wrap(sy, h)), Naive.GetPixel(mixed, j, i));
                Assert.Equal(Naive.GetPixel(pixs, Clamp(sx, w), Clamp(sy, h)), Naive.GetPixel(continued, j, i));
            }
        }
    }

    /*------------------------------------------------------------*
     *                    Color components                        *
     *------------------------------------------------------------*/
    [Fact]
    public void GetAndSetRGBComponent()
    {
        Random rng = new(1400);
        Pix pix = RandomPix(rng, 11, 5, 32);
        pix.SetResolution(150, 150);
        int[] shifts = { 24, 16, 8, 0 };
        for (int comp = 0; comp < 4; comp++) {
            Pix c = pix.GetRGBComponent(comp);
            Assert.Equal((11, 5, 8), (c.Width, c.Height, c.Depth));
            Assert.Equal(150, c.XRes);
            for (int i = 0; i < 5; i++)
                for (int j = 0; j < 11; j++)
                    Assert.Equal((Naive.GetPixel(pix, j, i) >> shifts[comp]) & 0xff, Naive.GetPixel(c, j, i));
        }

        // Replace a component and check that only that byte changed.
        Pix src = RandomPix(rng, 11, 5, 8);
        Pix dst = Pix.Copy(null, pix);
        dst.SetRGBComponent(src, L.COLOR_GREEN);
        for (int i = 0; i < 5; i++) {
            for (int j = 0; j < 11; j++) {
                uint orig = Naive.GetPixel(pix, j, i);
                Assert.Equal((orig & 0xff00ffff) | (Naive.GetPixel(src, j, i) << 16), Naive.GetPixel(dst, j, i));
            }
        }
        Assert.Equal(3, dst.Spp);
        dst.SetRGBComponent(src, L.L_ALPHA_CHANNEL);
        Assert.Equal(4, dst.Spp);
        Assert.Equal(Naive.GetPixel(src, 4, 3), Naive.GetPixel(dst, 4, 3) & 0xff);
    }

    [Fact]
    public void SetRGBComponentUsesCommonCorner()
    {
        Pix dst = Pix.Create(6, 6, 32);
        Pix src = Pix.Create(3, 9, 8);
        src.SetAllArbitrary(7);
        dst.SetRGBComponent(src, L.COLOR_BLUE);
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 6; j++)
                Assert.Equal(j < 3 ? 0x700u : 0u, Naive.GetPixel(dst, j, i));
    }

    [Fact]
    public void ComponentErrors()
    {
        Pix rgb = Pix.Create(4, 4, 32);
        Pix gray = Pix.Create(4, 4, 8);
        Assert.Throws<ArgumentException>(() => gray.GetRGBComponent(0));
        Assert.Throws<ArgumentException>(() => rgb.GetRGBComponent(4));
        Assert.Throws<ArgumentException>(() => gray.SetRGBComponent(gray, 0));
        Assert.Throws<ArgumentException>(() => rgb.SetRGBComponent(rgb, 0));
        Assert.Throws<ArgumentException>(() => rgb.SetRGBComponent(gray, 4));
        Assert.Throws<ArgumentException>(() => rgb.CopyRGBComponent(rgb, 4));
    }

    [Fact]
    public void CreateRGBImage()
    {
        Random rng = new(1500);
        Pix r = RandomPix(rng, 7, 4, 8), g = RandomPix(rng, 7, 4, 8), b = RandomPix(rng, 7, 4, 8);
        r.SetResolution(72, 72);
        Pix rgb = Pix.CreateRGBImage(r, g, b);
        Assert.Equal((7, 4, 32), (rgb.Width, rgb.Height, rgb.Depth));
        Assert.Equal(72, rgb.XRes);
        for (int i = 0; i < 4; i++) {
            for (int j = 0; j < 7; j++) {
                uint expected = (Naive.GetPixel(r, j, i) << 24) | (Naive.GetPixel(g, j, i) << 16) |
                                (Naive.GetPixel(b, j, i) << 8);
                Assert.Equal(expected, Naive.GetPixel(rgb, j, i));
            }
        }
        AssertPixEqual(r, rgb.GetRGBComponent(L.COLOR_RED));
        AssertPixEqual(g, rgb.GetRGBComponent(L.COLOR_GREEN));
        AssertPixEqual(b, rgb.GetRGBComponent(L.COLOR_BLUE));

        Assert.Throws<ArgumentException>(() => Pix.CreateRGBImage(r, g, Pix.Create(7, 4, 16)));
        Assert.Throws<ArgumentException>(() => Pix.CreateRGBImage(r, g, Pix.Create(6, 4, 8)));
        Assert.Throws<ArgumentException>(() => Pix.CreateRGBImage(r, g, Pix.Create(7, 5, 8)));
    }

    [Fact]
    public void CopyRGBComponent()
    {
        Random rng = new(1600);
        Pix src = RandomPix(rng, 8, 5, 32);
        Pix dst = RandomPix(rng, 8, 5, 32);
        Pix copy = Pix.Copy(null, dst);
        copy.CopyRGBComponent(src, L.COLOR_RED);
        for (int i = 0; i < 5; i++) {
            for (int j = 0; j < 8; j++) {
                uint expected = (Naive.GetPixel(dst, j, i) & 0x00ffffff) | (Naive.GetPixel(src, j, i) & 0xff000000);
                Assert.Equal(expected, Naive.GetPixel(copy, j, i));
            }
        }
        copy.CopyRGBComponent(src, L.L_ALPHA_CHANNEL);
        Assert.Equal(4, copy.Spp);
        Assert.Equal(Naive.GetPixel(src, 7, 4) & 0xff, Naive.GetPixel(copy, 7, 4) & 0xff);
    }

    [Fact]
    public void ShiftAndTransferAlpha()
    {
        Random rng = new(1700);
        Pix src = RandomPix(rng, 10, 6, 32);
        src.Spp = 4;
        Pix dst = Pix.Create(10, 6, 32);
        dst.ShiftAndTransferAlpha(src, 0, 0);
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 10; j++)
                Assert.Equal(Naive.GetPixel(src, j, i) & 0xff, Naive.GetPixel(dst, j, i));
        Assert.Equal(4, dst.Spp);

        // Shifted by (2, 1): dst(x, y) takes the alpha of src(x - 2, y - 1) and 0 outside.
        Pix shifted = Pix.Create(10, 6, 32);
        shifted.ShiftAndTransferAlpha(src, 2, 1);
        for (int i = 0; i < 6; i++) {
            for (int j = 0; j < 10; j++) {
                uint expected = (j >= 2 && i >= 1) ? Naive.GetPixel(src, j - 2, i - 1) & 0xff : 0u;
                Assert.Equal(expected, Naive.GetPixel(shifted, j, i));
            }
        }
        Assert.Throws<ArgumentException>(() => dst.ShiftAndTransferAlpha(Pix.Create(10, 6, 32), 1, 1));   // spp 3
        Assert.Throws<ArgumentException>(() => Pix.Create(10, 6, 8).ShiftAndTransferAlpha(src, 1, 1));
    }

    [Fact]
    public void RemoveBorderGeneralKeepsAlpha()
    {
        Random rng = new(1800);
        Pix src = RandomPix(rng, 12, 9, 32);
        src.Spp = 4;
        Pix pixd = src.RemoveBorderGeneral(2, 3, 1, 4);
        Assert.Equal(4, pixd.Spp);
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 7; j++)
                Assert.Equal(Naive.GetPixel(src, j + 2, i + 1), Naive.GetPixel(pixd, j, i));
    }

    [Fact]
    public void ComposeAndExtractRGB()
    {
        Pix.ComposeRGBPixel(1, 2, 3, out uint pixel);
        Assert.Equal(0x01020300u, pixel);
        Pix.ComposeRGBAPixel(4, 5, 6, 7, out pixel);
        Assert.Equal(0x04050607u, pixel);
        Pix.ExtractRGBValues(0xa1b2c3d4, out int r, out int g, out int b);
        Assert.Equal((0xa1, 0xb2, 0xc3), (r, g, b));
        Pix.ExtractRGBAValues(0xa1b2c3d4, out r, out g, out b, out int a);
        Assert.Equal((0xa1, 0xb2, 0xc3, 0xd4), (r, g, b, a));

        Assert.Equal(0x20, Pix.ExtractMinMaxComponent(0x80203000, L.L_CHOOSE_MIN));
        Assert.Equal(0x80, Pix.ExtractMinMaxComponent(0x80203000, L.L_CHOOSE_MAX));
        Assert.Equal(0x7f, Pix.ExtractMinMaxComponent(0xff7fff00, L.L_CHOOSE_MIN));
    }

    [Fact]
    public void GetRGBLine()
    {
        Random rng = new(1900);
        Pix pix = RandomPix(rng, 9, 4, 32);
        byte[] r = new byte[9], g = new byte[9], b = new byte[9];
        pix.GetRGBLine(2, r, g, b);
        for (int j = 0; j < 9; j++) {
            uint px = Naive.GetPixel(pix, j, 2);
            Assert.Equal((px >> 24) & 0xff, r[j]);
            Assert.Equal((px >> 16) & 0xff, g[j]);
            Assert.Equal((px >> 8) & 0xff, b[j]);
        }
        Assert.Throws<ArgumentException>(() => pix.GetRGBLine(4, r, g, b));
        Assert.Throws<ArgumentException>(() => pix.GetRGBLine(-1, r, g, b));
        Assert.Throws<ArgumentException>(() => Pix.Create(9, 4, 8).GetRGBLine(0, r, g, b));
    }

    [Fact]
    public void SetLineDataVal()
    {
        foreach (int d in AllDepths) {
            Pix pix = Pix.Create(40, 2, d);
            Pix expected = Pix.Create(40, 2, d);
            for (int j = 0; j < 40; j++) {
                uint val = (uint)(j * 5 + 3) & Mask(d);
                Pix.SetLineDataVal(pix.Data.AsSpan(pix.Wpl), j, d, val);
                Naive.SetPixel(expected, j, 1, val);
            }
            AssertPixEqual(expected, pix);
        }
        Span<uint> line = new uint[2];
        Assert.Throws<ArgumentException>(() => Pix.SetLineDataVal(new uint[2], -1, 8, 1));
        Assert.Throws<ArgumentException>(() => Pix.SetLineDataVal(new uint[2], 0, 24, 1));
    }

    [Fact]
    public void GetRGBComponentCmapNeedsConversionForShallowPix()
    {
        Pix pix = Pix.CreateWithCmap(4, 4, 8, L.L_SET_BLACK);
        pix.Colormap!.AddColor(200, 100, 50);
        pix.SetPixel(1, 2, 1);
        Pix red = pix.GetRGBComponent(L.COLOR_RED);
        Pix green = pix.GetRGBComponent(L.COLOR_GREEN);
        Pix blue = pix.GetRGBComponent(L.COLOR_BLUE);
        Assert.Equal((200u, 100u, 50u), (Naive.GetPixel(red, 1, 2), Naive.GetPixel(green, 1, 2), Naive.GetPixel(blue, 1, 2)));
        Assert.Equal(0u, Naive.GetPixel(red, 0, 0));
        Assert.Throws<ArgumentException>(() => pix.GetRGBComponent(L.L_ALPHA_CHANNEL));

        // NOTE(upstream): a 2 or 4 bpp pix is first converted to a cmapped 8 bpp pix, but the validity test
        // is then made with the original (2 or 4 bpp) colormap against the 8 bpp pix, which fails.
        Pix pix4 = Pix.CreateWithCmap(4, 4, 4, L.L_SET_BLACK);
        Assert.Throws<ArgumentException>(() => pix4.GetRGBComponent(L.COLOR_RED));
    }

    /*------------------------------------------------------------*
     *                     Endian conversion                      *
     *------------------------------------------------------------*/
    private static uint Swap4(uint w) =>
        ((w & 0xff) << 24) | ((w & 0xff00) << 8) | ((w >> 8) & 0xff00) | (w >> 24);

    [Fact]
    public void EndianByteSwapIsAnInvolution()
    {
        Random rng = new(2000);
        Pix pix = RandomPix(rng, 37, 5, 8);
        Pix before = Pix.Copy(null, pix);
        Pix swappedNew = pix.EndianByteSwapNew();
        pix.EndianByteSwap();
        for (int k = 0; k < pix.Data.Length; k++) {
            Assert.Equal(Swap4(before.Data[k]), pix.Data[k]);
            Assert.Equal(pix.Data[k], swappedNew.Data[k]);
        }
        pix.EndianByteSwap();
        Assert.Equal(before.Data, pix.Data);
        // The source of the New variant is untouched.
        Assert.Equal(before.Data, Pix.Copy(null, before).Data);
    }

    [Fact]
    public void EndianByteSwapGivesRasterOrderBytes()
    {
        Pix pix = Pix.Create(6, 2, 8);
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 6; j++)
                Naive.SetPixel(pix, j, i, (uint)(10 * i + j));
        pix.EndianByteSwap();
        ReadOnlySpan<byte> bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(pix.Data.AsSpan());
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 6; j++)
                Assert.Equal(10 * i + j, bytes[i * pix.Wpl * 4 + j]);
    }

    [Fact]
    public void EndianTwoByteSwap()
    {
        Random rng = new(2100);
        Pix pix = RandomPix(rng, 21, 4, 16);
        Pix before = Pix.Copy(null, pix);
        Pix swappedNew = pix.EndianTwoByteSwapNew();
        pix.EndianTwoByteSwap();
        for (int k = 0; k < pix.Data.Length; k++) {
            Assert.Equal((before.Data[k] << 16) | (before.Data[k] >> 16), pix.Data[k]);
            Assert.Equal(pix.Data[k], swappedNew.Data[k]);
        }
        pix.EndianTwoByteSwap();
        Assert.Equal(before.Data, pix.Data);
        Assert.Equal(before.Data.Length, swappedNew.Data.Length);
    }

    [Fact]
    public void TwoByteSwapIsByteSwapThenShortByteSwaps()
    {
        Pix pix = Pix.Create(4, 1, 16);
        pix.Data[0] = 0x11223344;
        pix.Data[1] = 0xaabbccdd;
        Pix full = pix.EndianByteSwapNew();
        Assert.Equal(0x44332211u, full.Data[0]);
        // Two-byte swap = full byte swap followed by a byte swap inside each 16-bit half.
        Pix two = pix.EndianTwoByteSwapNew();
        Assert.Equal(0x33441122u, two.Data[0]);
    }

    [Fact]
    public void LineEndianByteSwap()
    {
        uint[] src = { 0x01020304, 0xaabbccdd, 0, 0xffffffff };
        uint[] dst = new uint[4];
        Pix.LineEndianByteSwap(dst, src, 3);
        Assert.Equal(new uint[] { 0x04030201, 0xddccbbaa, 0, 0 }, dst);
    }

    [Fact]
    public void ByteProcessingSetupAndCleanup()
    {
        Random rng = new(2200);
        Pix pix = RandomPix(rng, 13, 4, 8);
        Pix before = Pix.Copy(null, pix);
        int[] lineptrs = pix.SetupByteProcessing(out int w, out int h);
        Assert.Equal((13, 4), (w, h));
        Assert.Equal(4, lineptrs.Length);
        ReadOnlySpan<byte> bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(pix.Data.AsSpan());
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                Assert.Equal(Naive.GetPixel(before, j, i), bytes[lineptrs[i] + j]);
        pix.CleanupByteProcessing(lineptrs);
        Assert.Equal(before.Data, pix.Data);

        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 16).SetupByteProcessing(out _, out _));
        Assert.Throws<ArgumentException>(() => Pix.CreateWithCmap(4, 4, 8, L.L_SET_WHITE).SetupByteProcessing(out _, out _));
    }

    /*------------------------------------------------------------*
     *                     Raster data, misc                      *
     *------------------------------------------------------------*/
    [Theory]
    [InlineData(1, 19)]
    [InlineData(2, 9)]
    [InlineData(4, 7)]
    [InlineData(8, 5)]
    [InlineData(32, 5)]
    public void GetRasterDataIsPackedRasterOrder(int d, int w)
    {
        Random rng = new(2300 + d);
        Pix pix = RandomPix(rng, w, 3, d);
        // Dirty pad bits must not matter: they are cleared.
        pix.SetPadBits(1);
        byte[] data = pix.GetRasterData();
        int databpl = d == 32 ? 3 * w : (w * d + 7) / 8;
        Assert.Equal(databpl * 3, data.Length);
        for (int i = 0; i < 3; i++) {
            for (int j = 0; j < w; j++) {
                uint px = Naive.GetPixel(pix, j, i);
                if (d == 32) {
                    Assert.Equal((px >> 24) & 0xff, data[i * databpl + 3 * j]);
                    Assert.Equal((px >> 16) & 0xff, data[i * databpl + 3 * j + 1]);
                    Assert.Equal((px >> 8) & 0xff, data[i * databpl + 3 * j + 2]);
                } else {
                    int bitpos = j * d;
                    int shift = 8 - d - (bitpos & 7);
                    Assert.Equal(px, (uint)(data[i * databpl + (bitpos >> 3)] >> shift) & Mask(d));
                }
            }
        }
        // Pad bits are zero in the output.
        if (d == 1)
            Assert.Equal(0, data[2] & 0x1f);   // 19 pixels: the low 5 bits of byte 2 are pad
    }

    [Fact]
    public void GetRasterData16BppKeepsLowByteOnly()
    {
        Pix pix = Pix.Create(3, 1, 16);
        Naive.SetPixel(pix, 0, 0, 0x1234);
        Naive.SetPixel(pix, 1, 0, 0xabcd);
        Naive.SetPixel(pix, 2, 0, 0x0007);
        Assert.Equal(new byte[] { 0x34, 0, 0xcd, 0, 0x07, 0 }, pix.GetRasterData());
        Assert.Throws<ArgumentException>(() => Pix.Create(3, 1, 24).GetRasterData());
    }

    [Theory]
    [InlineData(3300, 2550, 11.0f, 300)]
    [InlineData(2550, 3300, 11.0f, 300)]
    [InlineData(100, 50, 100.0f, 1)]
    [InlineData(1, 1, 5.0f, 1)]
    [InlineData(1700, 1100, 8.5f, 200)]
    [InlineData(1000, 1000, 3.0f, 333)]
    public void InferResolution(int w, int h, float longside, int expected)
    {
        Pix pix = Pix.Create(w, h, 1);
        Assert.Equal(expected, pix.InferResolution(longside));
    }

    [Fact]
    public void InferResolutionErrors()
    {
        Pix pix = Pix.Create(10, 10, 1);
        Assert.Throws<ArgumentException>(() => pix.InferResolution(0f));
        Assert.Throws<ArgumentException>(() => pix.InferResolution(-1f));
    }

    [Fact]
    public void AlphaIsOpaque()
    {
        Pix pix = Pix.Create(5, 4, 32);
        pix.Spp = 4;
        pix.SetAllArbitrary(0x112233ff);
        Assert.True(pix.AlphaIsOpaque());
        Naive.SetPixel(pix, 4, 3, 0x112233fe);
        Assert.False(pix.AlphaIsOpaque());
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 4, 32).AlphaIsOpaque());   // spp 3
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 4, 8).AlphaIsOpaque());
    }

    [Fact]
    public void AlphaMaskBorder()
    {
        float[] saved = (float[])Pix.AlphaMaskBorderVals.Clone();
        try {
            Assert.Equal(new[] { 0.0f, 0.5f }, saved);
            Pix.SetAlphaMaskBorder(0.25f, 0.75f);
            Assert.Equal(new[] { 0.25f, 0.75f }, Pix.AlphaMaskBorderVals);
            Pix.SetAlphaMaskBorder(-3f, 7f);
            Assert.Equal(new[] { 0.0f, 1.0f }, Pix.AlphaMaskBorderVals);
        } finally {
            Pix.SetAlphaMaskBorder(saved[0], saved[1]);
        }
    }

    [Fact]
    public void DisplayLayersRGBAValidates()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).DisplayLayersRGBA(0xffffff00, 0));
    }
}
