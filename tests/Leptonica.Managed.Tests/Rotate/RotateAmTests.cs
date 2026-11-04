using System;
using Leptonica;
using Leptonica.Managed.Tests.Rop;
using Xunit;

namespace Leptonica.Managed.Tests.Rotate;

/// <summary>
/// Tests for the area-mapping rotations (rotateam.c): identity for small angles,
/// hand-computed pixels on 4x4 images, fill colors and all supported depths.
/// </summary>
public class RotateAmTests
{
    private const float Angle = 0.1f;   // cos*16 = 15.92, sin*16 = 1.597

    private static Pix Gray4x4()
    {
        Pix pix = Pix.Create(4, 4, 8);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                NaiveRop.SetPixel(pix, x, y, (uint)(10 + 17 * x + 31 * y));
        return pix;
    }

    private static uint Rgb(int r, int g, int b) => ((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8);

    private static void AssertSamePixels(Pix a, Pix b)
    {
        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Height, b.Height);
        Assert.Equal(a.Depth, b.Depth);
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
                Assert.Equal(NaiveRop.GetPixel(a, x, y), NaiveRop.GetPixel(b, x, y));
    }

    [Fact]
    public void NarrowAreaRotationsMatchSignedScalarBounds()
    {
        var rng = new Random(907213);
        foreach (int w in new[] { 1, 2, 5 }) {
            foreach (int h in new[] { 1, 2, 5 }) {
                foreach (float angle in new[] { 0.1f, -0.35f }) {
                    Pix gray = Pix.Create(w, h, 8);
                    Pix color = Pix.Create(w, h, 32);
                    for (int y = 0; y < h; y++) {
                        for (int x = 0; x < w; x++) {
                            NaiveRop.SetPixel(gray, x, y, (uint)rng.Next(256));
                            NaiveRop.SetPixel(color, x, y, (uint)rng.NextInt64(0, 1L << 24) << 8);
                        }
                    }
                    foreach (int mode in new[] { 0, 1, 2, 3, 4 }) {
                        bool isGray = mode == 0 || mode == 2;
                        bool corner = mode == 2 || mode == 3;
                        bool fast = mode == 4;
                        Pix src = isGray ? gray : color;
                        Pix actual = mode switch {
                            0 => gray.RotateAMGray(angle, 173),
                            1 => color.RotateAMColor(angle, 0x12345600),
                            2 => gray.RotateAMGrayCorner(angle, 173),
                            3 => color.RotateAMColorCorner(angle, 0x12345600),
                            _ => color.RotateAMColorFast(angle, 0x12345600)
                        };
                        int unit = fast ? 4 : 16;
                        float sina = (float)(unit * Math.Sin(angle));
                        float cosa = (float)(unit * Math.Cos(angle));
                        int xc = w / 2, yc = h / 2;
                        Pix expectedPix = Pix.Create(w, h, isGray ? 8 : 32);
                        for (int y = 0; y < h; y++) {
                            for (int x = 0; x < w; x++) {
                                int xpm = corner ? (int)(x * cosa + y * sina) :
                                    (int)(-(xc - x) * cosa - (yc - y) * sina);
                                int ypm = corner ? (int)(y * cosa - x * sina) :
                                    (int)(-(yc - y) * cosa + (xc - x) * sina);
                                int shift = fast ? 2 : 4;
                                int xp = (corner ? 0 : xc) + (xpm >> shift);
                                int yp = (corner ? 0 : yc) + (ypm >> shift);
                                uint expected;
                                if (xp < 0 || yp < 0 || xp > w - 2 || yp > h - 2) {
                                    expected = isGray ? 173u : 0x12345600u;
                                } else {
                                    int xf = xpm & (unit - 1), yf = ypm & (unit - 1);
                                    uint p00 = NaiveRop.GetPixel(src, xp, yp);
                                    uint p10 = NaiveRop.GetPixel(src, xp + 1, yp);
                                    uint p01 = NaiveRop.GetPixel(src, xp, yp + 1);
                                    uint p11 = NaiveRop.GetPixel(src, xp + 1, yp + 1);
                                    expected = 0;
                                    for (int channel = isGray ? 0 : 8; channel <= (isGray ? 0 : 24); channel += 8) {
                                        int sum = (unit - xf) * (unit - yf) * (int)((p00 >> channel) & 255) +
                                                  xf * (unit - yf) * (int)((p10 >> channel) & 255) +
                                                  (unit - xf) * yf * (int)((p01 >> channel) & 255) +
                                                  xf * yf * (int)((p11 >> channel) & 255);
                                        expected |= (uint)((sum + (fast ? 0 : 128)) / (unit * unit)) << channel;
                                    }
                                }
                                NaiveRop.SetPixel(expectedPix, x, y, expected);
                                Assert.Equal(expected, NaiveRop.GetPixel(actual, x, y));
                            }
                        }
                        Assert.Equal(expectedPix.Data, actual.Data);
                    }
                }
            }
        }
    }

    [Fact]
    public void ZeroAngleIsIdentity()
    {
        Pix gray = Gray4x4();
        Pix color = Pix.Create(4, 4, 32);
        Assert.Same(gray, gray.RotateAMGray(0f, 0));
        Assert.Same(gray, gray.RotateAMGrayCorner(0.0005f, 255));
        Assert.Same(color, color.RotateAMColor(0f, 0));
        Assert.Same(color, color.RotateAMColorCorner(-0.0005f, 0));
        Assert.Same(color, color.RotateAMColorFast(0f, 0));
        foreach (int d in new[] { 2, 4, 8, 32 }) {
            Pix pix = Pix.Create(5, 5, d);
            Assert.Same(pix, pix.RotateAM(0f, L.L_BRING_IN_WHITE));
            Assert.Same(pix, pix.RotateAMCorner(0f, L.L_BRING_IN_BLACK));
        }
    }

    [Fact]
    public void InvalidDepthsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 1).RotateAM(Angle, L.L_BRING_IN_WHITE));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).RotateAMColor(Angle, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 32).RotateAMGray(Angle, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).RotateAMColorCorner(Angle, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 32).RotateAMGrayCorner(Angle, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 8).RotateAMColorFast(Angle, 0));
    }

    [Fact]
    public void GrayCenterPixelsAreHandChecked()
    {
        Pix pix = Gray4x4();
        NaiveRop.SetPixel(pix, 1, 2, 100);
        NaiveRop.SetPixel(pix, 2, 2, 200);
        NaiveRop.SetPixel(pix, 1, 3, 40);
        NaiveRop.SetPixel(pix, 2, 3, 80);

        Pix pixd = pix.RotateAMGray(Angle, 0);

        // dest (2,2): xpm = ypm = 0, so the source pixel is copied
        Assert.Equal(200u, NaiveRop.GetPixel(pixd, 2, 2));
        // dest (1,2): xpm = -15, ypm = 1 -> xp = 1, yp = 2, xf = 1, yf = 1
        // (225 * 100 + 15 * 200 + 15 * 40 + 1 * 80 + 128) / 256 = 102
        Assert.Equal(102u, NaiveRop.GetPixel(pixd, 1, 2));
        // dest (0,0) maps outside the source
        Assert.Equal(0u, NaiveRop.GetPixel(pixd, 0, 0));
        Assert.Equal(255u, NaiveRop.GetPixel(pix.RotateAMGray(Angle, 255), 0, 0));
    }

    [Fact]
    public void ColorCenterPixelsAreHandChecked()
    {
        Pix pix = Pix.Create(4, 4, 32);
        NaiveRop.SetPixel(pix, 1, 2, Rgb(100, 110, 120));
        NaiveRop.SetPixel(pix, 2, 2, Rgb(200, 210, 220));
        NaiveRop.SetPixel(pix, 1, 3, Rgb(40, 50, 60));
        NaiveRop.SetPixel(pix, 2, 3, Rgb(80, 90, 100));

        Pix pixd = pix.RotateAMColor(Angle, 0xffffff00);

        Assert.Equal(Rgb(200, 210, 220), NaiveRop.GetPixel(pixd, 2, 2));
        // same weights as the gray case; the weights add up to 256, so the
        // other components are offset by exactly 10 and 20
        Assert.Equal(Rgb(102, 112, 122), NaiveRop.GetPixel(pixd, 1, 2));
        Assert.Equal(0xffffff00u, NaiveRop.GetPixel(pixd, 0, 0));
        Assert.Equal(0u, NaiveRop.GetPixel(pix.RotateAMColor(Angle, 0), 0, 0));
    }

    [Fact]
    public void GrayCornerPixelsAreHandChecked()
    {
        Pix pix = Pix.Create(4, 4, 8);
        NaiveRop.SetPixel(pix, 0, 0, 10);
        NaiveRop.SetPixel(pix, 1, 0, 20);
        NaiveRop.SetPixel(pix, 0, 1, 30);
        NaiveRop.SetPixel(pix, 1, 1, 40);

        Pix pixd = pix.RotateAMGrayCorner(Angle, 255);

        // dest (0,0) is the rotation center
        Assert.Equal(10u, NaiveRop.GetPixel(pixd, 0, 0));
        // dest (1,0): ypm = -1 -> off the top edge
        Assert.Equal(255u, NaiveRop.GetPixel(pixd, 1, 0));
        // dest (0,1): xpm = 1, ypm = 15 -> xp = 0, yp = 0, xf = 1, yf = 15
        // (15 * 10 + 1 * 20 + 225 * 30 + 15 * 40 + 128) / 256 = 29
        Assert.Equal(29u, NaiveRop.GetPixel(pixd, 0, 1));
    }

    [Fact]
    public void ColorCornerPixelsAreHandChecked()
    {
        Pix pix = Pix.Create(4, 4, 32);
        NaiveRop.SetPixel(pix, 0, 0, Rgb(10, 11, 12));
        NaiveRop.SetPixel(pix, 1, 0, Rgb(20, 21, 22));
        NaiveRop.SetPixel(pix, 0, 1, Rgb(30, 31, 32));
        NaiveRop.SetPixel(pix, 1, 1, Rgb(40, 41, 42));

        Pix pixd = pix.RotateAMColorCorner(Angle, 0);

        Assert.Equal(Rgb(10, 11, 12), NaiveRop.GetPixel(pixd, 0, 0));
        Assert.Equal(0u, NaiveRop.GetPixel(pixd, 1, 0));
        Assert.Equal(Rgb(29, 30, 31), NaiveRop.GetPixel(pixd, 0, 1));
    }

    [Fact]
    public void ColorFastPixelsAreHandChecked()
    {
        Pix pix = Pix.Create(4, 4, 32);
        NaiveRop.SetPixel(pix, 1, 2, Rgb(100, 110, 120));
        NaiveRop.SetPixel(pix, 2, 2, Rgb(200, 210, 220));

        Pix pixd = pix.RotateAMColorFast(Angle, 0xffffff00);

        Assert.Equal(Rgb(200, 210, 220), NaiveRop.GetPixel(pixd, 2, 2));
        // dest (1,2): xpm = -3, ypm = 0 -> xp = 1, xf = 1, yf = 0 (case 1):
        // (3 * sp1 + sp2) / 4 per component
        Assert.Equal(Rgb(125, 135, 145), NaiveRop.GetPixel(pixd, 1, 2));
        // dest (0,3): ypm = 4 -> yp = 3 > h - 2, off the edge
        Assert.Equal(0xffffff00u, NaiveRop.GetPixel(pixd, 0, 3));
    }

    [Fact]
    public void ConstantImageStaysConstantInsideAndFillsOutside()
    {
        Pix gray = Pix.Create(20, 17, 8);
        Pix color = Pix.Create(20, 17, 32);
        for (int y = 0; y < 17; y++) {
            for (int x = 0; x < 20; x++) {
                NaiveRop.SetPixel(gray, x, y, 77);
                NaiveRop.SetPixel(color, x, y, Rgb(77, 78, 79));
            }
        }
        foreach (float angle in new[] { 0.3f, -0.7f, 1.2f }) {
            Pix g = gray.RotateAMGray(angle, 255);
            Pix gc = gray.RotateAMGrayCorner(angle, 255);
            Pix c = color.RotateAMColor(angle, 0xffffff00);
            Pix cc = color.RotateAMColorCorner(angle, 0xffffff00);
            for (int y = 0; y < 17; y++) {
                for (int x = 0; x < 20; x++) {
                    Assert.True(NaiveRop.GetPixel(g, x, y) is 77 or 255);
                    Assert.True(NaiveRop.GetPixel(gc, x, y) is 77 or 255);
                    Assert.True(NaiveRop.GetPixel(c, x, y) == Rgb(77, 78, 79) || NaiveRop.GetPixel(c, x, y) == 0xffffff00u);
                    Assert.True(NaiveRop.GetPixel(cc, x, y) == Rgb(77, 78, 79) || NaiveRop.GetPixel(cc, x, y) == 0xffffff00u);
                }
            }
            // the center (corner) pixel is always inside
            Assert.Equal(77u, NaiveRop.GetPixel(g, 10, 8));
            Assert.Equal(77u, NaiveRop.GetPixel(gc, 0, 0));
        }
    }

    [Fact]
    public void RotateAMHandlesAllDepths()
    {
        foreach (int d in new[] { 2, 4, 8, 32 }) {
            Pix pix = Pix.Create(9, 7, d);
            uint mask = d == 32 ? 0xffffff00u : (1u << d) - 1;
            for (int y = 0; y < 7; y++)
                for (int x = 0; x < 9; x++)
                    NaiveRop.SetPixel(pix, x, y, (uint)(x * 29 + y * 53 + 3) * 2654435761u & mask);

            foreach (int incolor in new[] { L.L_BRING_IN_WHITE, L.L_BRING_IN_BLACK }) {
                Pix expected, expectedCorner;
                uint white = incolor == L.L_BRING_IN_WHITE ? (d == 32 ? 0xffffff00u : 255u) : 0u;
                if (d == 32) {
                    expected = pix.RotateAMColor(0.25f, white);
                    expectedCorner = pix.RotateAMColorCorner(0.25f, white);
                } else {
                    Pix pix8 = d == 8 ? pix : pix.ConvertTo8(false);
                    expected = pix8.RotateAMGray(0.25f, (byte)white);
                    expectedCorner = pix8.RotateAMGrayCorner(0.25f, (byte)white);
                }
                Pix actual = pix.RotateAM(0.25f, incolor);
                Pix actualCorner = pix.RotateAMCorner(0.25f, incolor);
                Assert.Equal(d == 32 ? 32 : 8, actual.Depth);
                AssertSamePixels(expected, actual);
                AssertSamePixels(expectedCorner, actualCorner);
            }
        }
    }

    [Fact]
    public void AlphaChannelBringsInOpaque()
    {
        Pix pix = Pix.Create(4, 4, 32);
        pix.Spp = 4;
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                NaiveRop.SetPixel(pix, x, y, Rgb(50, 60, 70) | 100);

        Pix pixd = pix.RotateAMColor(Angle, 0);
        Assert.Equal(4, pixd.Spp);
        Assert.Equal(Rgb(50, 60, 70) | 100, NaiveRop.GetPixel(pixd, 2, 2));
        Assert.Equal(255u, NaiveRop.GetPixel(pixd, 0, 0));   // fill color 0, opaque alpha

        Pix pixc = pix.RotateAMColorCorner(Angle, 0);
        Assert.Equal(Rgb(50, 60, 70) | 100, NaiveRop.GetPixel(pixc, 0, 0));
        Assert.Equal(255u, NaiveRop.GetPixel(pixc, 1, 0));
    }
}
