using System;
using Leptonica;

namespace Leptonica.Managed.Tests.IO;

/// <summary>Compares the PNG RGB/RGBA row conversion with the per-pixel ComposeRGBAPixel definition.</summary>
public class PngRgbConvertTests
{
    [Theory]
    [InlineData(false, 0u)]
    [InlineData(false, 255u)]
    [InlineData(true, 0u)]
    public void ConvertRgbRowMatchesScalarReference(bool hasAlpha, uint alpha)
    {
        var rng = new Random(5);
        int[] widths = [1, 2, 3, 7, 31, 32, 33, 63, 64, 65, 101, 576];
        foreach (int w in widths)
        {
            int bpp = hasAlpha ? 4 : 3;
            var src = new byte[w * bpp];
            rng.NextBytes(src);
            var expected = new uint[w];
            for (int j = 0, k = 0; j < w; j++)
            {
                int r = src[k++], g = src[k++], b = src[k++];
                int a = hasAlpha ? src[k++] : (int)alpha;
                Pix.ComposeRGBAPixel(r, g, b, a, out expected[j]);
            }
            var actual = new uint[w];
            Pix.ConvertRgbRow(src, actual, hasAlpha, alpha);
            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(33)]
    [InlineData(64)]
    public void PngRoundTripKeepsPixels(int w)
    {
        var rng = new Random(w);
        foreach (int spp in new[] { 3, 4 })
        {
            Pix pix = Pix.Create(w, 7, 32);
            for (int i = 0; i < pix.Data.Length; i++) pix.Data[i] = (uint)rng.NextInt64(0, 1L << 32);
            pix.Spp = spp;
            if (spp == 3) for (int i = 0; i < pix.Data.Length; i++) pix.Data[i] &= 0xffffff00u;
            Pix back = Pix.ReadMem(Pix.WriteMem(pix, L.IFF_PNG));
            Assert.Equal(pix.Data, back.Data);
        }
    }
}
