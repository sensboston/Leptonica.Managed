using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Scale;

/// <summary>Compares pixScaleBySampling with a per-pixel reference on random images.</summary>
public class ScaleBySamplingTests
{
    [Theory]
    [InlineData(32, 0.71f)]
    [InlineData(32, 0.5f)]
    [InlineData(32, 0.33f)]
    [InlineData(32, 1.6f)]
    [InlineData(8, 0.71f)]
    [InlineData(16, 0.9f)]
    public void ScaleBySamplingMatchesReference(int d, float scale)
    {
        var rng = new Random(31 + d);
        foreach (int w in new[] { 1, 2, 3, 7, 16, 17, 31, 32, 33, 64, 65, 129 }) {
            foreach (int h in new[] { 1, 2, 5, 18 }) {
                Pix pix = Pix.Create(w, h, d);
                for (int i = 0; i < pix.Data.Length; i++)
                    pix.Data[i] = (uint)rng.NextInt64(0, 1L << 32);
                int wd = (int)(scale * (float)w + 0.5);
                int hd = (int)(scale * (float)h + 0.5);
                if (wd < 1 || hd < 1)
                    continue;
                Pix actual = pix.ScaleBySampling(scale, scale);
                Assert.Equal(wd, actual.Width);
                Assert.Equal(hd, actual.Height);
                float wratio = (float)w / (float)wd;
                float hratio = (float)h / (float)hd;
                for (int i = 0; i < hd; i++) {
                    int sy = Math.Min((int)(hratio * i + 0.5f), h - 1);
                    for (int j = 0; j < wd; j++) {
                        int sx = Math.Min((int)(wratio * j + 0.5f), w - 1);
                        pix.GetPixel(sx, sy, out uint e);
                        actual.GetPixel(j, i, out uint a);
                        Assert.True(e == a, $"d={d} scale={scale} {w}x{h} ({j},{i}): expected {e}, got {a}");
                    }
                }
            }
        }
    }
}
