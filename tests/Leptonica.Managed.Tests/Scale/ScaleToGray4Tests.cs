using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Scale;

/// <summary>
/// Compares pixScaleToGray4 (vectorized low level) with a plain per-pixel
/// reference on random images (fixed seeds).
/// </summary>
public class ScaleToGray4Tests
{
    private static Pix RandomBinary(Random rng, int w, int h, int density)
    {
        Pix pix = Pix.Create(w, h, 1);
        uint[] data = pix.Data;
        for (int i = 0; i < data.Length; i++) {
            uint v = 0;
            switch (density) {
            case 0: v = 0; break;
            case 1: v = 0xffffffffu; break;
            case 2: v = (uint)rng.NextInt64(0, 1L << 32); break;
            default: v = (uint)rng.NextInt64(0, 1L << 32) & (uint)rng.NextInt64(0, 1L << 32)
                         & (uint)rng.NextInt64(0, 1L << 32); break;
            }
            data[i] = v;
        }
        return pix;
    }

    private static Pix ReferenceScaleToGray4(Pix pixs)
    {
        pixs.GetDimensions(out int ws, out int hs, out _);
        int wd = (ws / 4) & unchecked((int)0xfffffffe);
        int hd = hs / 4;
        Pix pixd = Pix.Create(wd, hd, 8);
        for (int y = 0; y < hd; y++) {
            for (int x = 0; x < wd; x++) {
                int sum = 0;
                for (int dy = 0; dy < 4; dy++) {
                    for (int dx = 0; dx < 4; dx++) {
                        pixs.GetPixel(4 * x + dx, 4 * y + dy, out uint v);
                        sum += (int)(v & 1);
                    }
                }
                pixd.SetPixel(x, y, (uint)(0xff - (sum * 255) / 16));
            }
        }
        return pixd;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ScaleToGray4MatchesReference(int density)
    {
        int[] widths = { 8, 9, 31, 32, 33, 63, 64, 65, 100, 127, 128, 129, 130, 131, 255, 256,
                         257, 263, 300, 517, 1001 };
        int[] heights = { 4, 5, 7, 8, 9, 13, 21 };
        var rng = new Random(4400 + density);
        foreach (int w in widths) {
            foreach (int h in heights) {
                Pix pix = RandomBinary(rng, w, h, density);
                Pix expected = ReferenceScaleToGray4(pix);
                Pix actual = pix.ScaleToGray4();
                Assert.Equal(expected.Width, actual.Width);
                Assert.Equal(expected.Height, actual.Height);
                for (int y = 0; y < expected.Height; y++) {
                    for (int x = 0; x < expected.Width; x++) {
                        expected.GetPixel(x, y, out uint e);
                        actual.GetPixel(x, y, out uint a);
                        Assert.True(e == a, $"w={w} h={h} density={density} at ({x},{y}): expected {e}, got {a}");
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void ScaleToGray4TooSmallThrows(int w)
    {
        Pix pix = Pix.Create(w, 16, 1);
        Assert.ThrowsAny<Exception>(() => pix.ScaleToGray4());
    }
}
