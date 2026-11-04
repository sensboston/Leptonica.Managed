using System;
using Leptonica;
using Naive = Leptonica.Managed.Tests.Rop.NaiveRop;

namespace Leptonica.Managed.Tests.ScaleTests;

/// <summary>Tests of binexpand.c and binreduce.c against pixel-by-pixel references.</summary>
public class BinScaleTests
{
    private static Pix RandomBinary(int w, int h, int seed)
    {
        var rnd = new Random(seed);
        Pix pix = Pix.Create(w, h, 1);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                Naive.SetPixel(pix, x, y, rnd.Next(2) == 0 ? 0u : 1u);
        return pix;
    }

    [Theory]
    [InlineData(1, 1, 1, 1)]
    [InlineData(7, 5, 2, 2)]
    [InlineData(33, 4, 2, 2)]
    [InlineData(33, 4, 4, 4)]
    [InlineData(37, 3, 8, 8)]
    [InlineData(70, 3, 16, 16)]
    [InlineData(13, 6, 3, 5)]
    [InlineData(40, 2, 1, 4)]
    public void ExpandBinaryReplicate_ReplicatesPixels(int w, int h, int xf, int yf)
    {
        Pix pixs = RandomBinary(w, h, 11 + w);
        pixs.XRes = 100;
        pixs.YRes = 200;
        Pix pixd = pixs.ExpandBinaryReplicate(xf, yf);
        Assert.Equal(w * xf, pixd.Width);
        Assert.Equal(h * yf, pixd.Height);
        Assert.Equal(1, pixd.Depth);
        Assert.Equal(100 * xf, pixd.XRes);
        Assert.Equal(200 * yf, pixd.YRes);
        for (int y = 0; y < pixd.Height; y++)
            for (int x = 0; x < pixd.Width; x++)
                Assert.True(Naive.GetPixel(pixs, x / xf, y / yf) == Naive.GetPixel(pixd, x, y),
                    $"pixel ({x},{y})");
    }

    [Fact]
    public void ExpandBinaryReplicate_InvalidArguments()
    {
        Pix pixs = RandomBinary(8, 8, 1);
        Assert.Throws<ArgumentException>(() => pixs.ExpandBinaryReplicate(0, 2));
        Assert.Throws<ArgumentException>(() => pixs.ExpandBinaryReplicate(2, -1));
        Assert.Throws<ArgumentException>(() => Pix.Create(8, 8, 8).ExpandBinaryReplicate(2, 2));
        Assert.Throws<ArgumentException>(() => pixs.ExpandBinaryPower2(3));
    }

    [Fact]
    public void ExpandBinaryPower2_FactorOneCopies()
    {
        Pix pixs = RandomBinary(20, 5, 3);
        Pix pixd = pixs.ExpandBinaryPower2(1);
        Assert.NotSame(pixs, pixd);
        Assert.Equal(pixs.Data, pixd.Data);
    }

    [Theory]
    [InlineData(16, 4)]
    [InlineData(17, 5)]
    [InlineData(65, 9)]
    [InlineData(100, 7)]
    [InlineData(3, 2)]
    public void ReduceBinary2_Subsamples(int w, int h)
    {
        Pix pixs = RandomBinary(w, h, 5 + w);
        Pix pixd = pixs.ReduceBinary2();
        Assert.Equal(w / 2, pixd.Width);
        Assert.Equal(h / 2, pixd.Height);
        for (int y = 0; y < pixd.Height; y++)
            for (int x = 0; x < pixd.Width; x++)
                Assert.True(Naive.GetPixel(pixs, 2 * x, 2 * y) == Naive.GetPixel(pixd, x, y),
                    $"pixel ({x},{y})");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ReduceRankBinary2_ThresholdsBlockCount(int level)
    {
        foreach (var (w, h) in new[] { (16, 4), (65, 9), (100, 7), (31, 30) }) {
            Pix pixs = RandomBinary(w, h, 17 + w + level);
            Pix pixd = pixs.ReduceRankBinary2(level);
            Assert.Equal(w / 2, pixd.Width);
            Assert.Equal(h / 2, pixd.Height);
            for (int y = 0; y < pixd.Height; y++) {
                for (int x = 0; x < pixd.Width; x++) {
                    uint count = Naive.GetPixel(pixs, 2 * x, 2 * y) + Naive.GetPixel(pixs, 2 * x + 1, 2 * y)
                               + Naive.GetPixel(pixs, 2 * x, 2 * y + 1) + Naive.GetPixel(pixs, 2 * x + 1, 2 * y + 1);
                    uint expected = count >= (uint)level ? 1u : 0u;
                    Assert.True(expected == Naive.GetPixel(pixd, x, y), $"{w}x{h} level {level} pixel ({x},{y})");
                }
            }
        }
    }

    [Fact]
    public void ReduceRankBinary2_InvalidArguments()
    {
        Pix pixs = RandomBinary(16, 16, 1);
        Assert.Throws<ArgumentException>(() => pixs.ReduceRankBinary2(0));
        Assert.Throws<ArgumentException>(() => pixs.ReduceRankBinary2(5));
        Assert.Throws<ArgumentException>(() => RandomBinary(16, 1, 1).ReduceRankBinary2(1));
        Assert.Throws<ArgumentException>(() => RandomBinary(16, 1, 1).ReduceBinary2());
    }

    [Fact]
    public void ReduceRankBinaryCascade_ComposesReductions()
    {
        Pix pixs = RandomBinary(130, 70, 99);
        Pix expected = pixs.ReduceRankBinary2(1).ReduceRankBinary2(2).ReduceRankBinary2(3);
        Pix actual = pixs.ReduceRankBinaryCascade(1, 2, 3, 0);
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Data, actual.Data);

        Pix four = pixs.ReduceRankBinaryCascade(4, 3, 2, 1);
        Assert.Equal(130 / 16, four.Width);
        Assert.Equal(70 / 2 / 2 / 2 / 2, four.Height);

        Pix copy = pixs.ReduceRankBinaryCascade(0, 1, 1, 1);
        Assert.Equal(pixs.Data, copy.Data);
        Assert.Throws<ArgumentException>(() => pixs.ReduceRankBinaryCascade(1, 5, 1, 1));
    }

    [Fact]
    public void MakeSubsampleTab2x_PermutesBits()
    {
        byte[] tab = Pix.MakeSubsampleTab2x();
        Assert.Equal(256, tab.Length);
        Assert.Equal(0x80, tab[0x80]);
        Assert.Equal(0x01, tab[0x01]);
        Assert.Equal(0x02, tab[0x04]);
        Assert.Equal(0x10, tab[0x02]);
        Assert.Equal(0xff, tab[0xff]);
    }
}
