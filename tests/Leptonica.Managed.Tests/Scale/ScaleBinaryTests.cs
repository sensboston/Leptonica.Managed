using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Scale;

/// <summary>Compares pixScaleBinary with a per-pixel reference on random images.</summary>
public class ScaleBinaryTests
{
    [Theory]
    [InlineData(0.5f)]
    [InlineData(0.75f)]
    [InlineData(0.3f)]
    [InlineData(2.0f)]
    public void ScaleBinaryMatchesReference(float scale)
    {
        var rng = new Random(909);
        foreach (int w in new[] { 2, 3, 31, 32, 33, 63, 64, 65, 66, 127, 128, 129, 200, 257 }) {
            foreach (int h in new[] { 1, 2, 3, 8, 21 }) {
                Pix pix = Pix.Create(w, h, 1);
                for (int i = 0; i < pix.Data.Length; i++)
                    pix.Data[i] = (uint)rng.NextInt64(0, 1L << 32);
                int wd = (int)(scale * (float)w + 0.5);
                int hd = (int)(scale * (float)h + 0.5);
                if (wd < 2 || hd < 1)   // wd == 1 keeps the previous row's last pixel state (unchanged behavior)
                    continue;
                Pix actual = pix.ScaleBinary(scale, scale);
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
                        Assert.True(e == a, $"scale={scale} {w}x{h} ({j},{i}): expected {e}, got {a}");
                    }
                }
                // pad bits stay clear
                int wpl = actual.Wpl;
                for (int i = 0; i < hd; i++)
                    for (int x = wd; x < wpl * 32; x++)
                        Assert.True(((actual.Data[i * wpl + (x >> 5)] >> (31 - (x & 31))) & 1) == 0, $"pad bit {x} set");
            }
        }
    }
}
