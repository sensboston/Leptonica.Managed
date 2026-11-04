using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Core;

public class PixTests
{
    [Theory]
    [InlineData(1, 1, 1, 1)]
    [InlineData(33, 1, 1, 2)]
    [InlineData(17, 2, 2, 2)]
    [InlineData(5, 3, 8, 2)]
    [InlineData(3, 2, 24, 3)]
    [InlineData(2, 2, 32, 2)]
    public void CreateComputesWpl(int w, int h, int d, int wpl)
    {
        Pix pix = Pix.Create(w, h, d);
        Assert.Equal(wpl, pix.Wpl);
        Assert.Equal(wpl * h, pix.Data.Length);
        Assert.Equal(d >= 24 ? 3 : 1, pix.Spp);
        Assert.Equal(L.IFF_UNKNOWN, pix.InputFormat);
    }

    [Fact]
    public void CreateRejectsBadArgs()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(10, 10, 3));
        Assert.Throws<ArgumentException>(() => Pix.Create(0, 10, 8));
        Assert.Throws<ArgumentException>(() => Pix.Create(1 << 25, 1, 32));
    }

    [Fact]
    public void CopyIsDeepAndKeepsFields()
    {
        Pix pixs = Pix.CreateWithCmap(10, 4, 8, L.L_SET_WHITE);
        pixs.SetResolution(300, 150);
        pixs.Text = "abc";
        pixs.Data[0] = 0x01020304;

        Pix pixd = Pix.Copy(null, pixs);
        Assert.NotSame(pixs.Data, pixd.Data);
        Assert.Equal(pixs.Data, pixd.Data);
        Assert.Equal(300, pixd.XRes);
        Assert.Equal("abc", pixd.Text);
        Assert.NotSame(pixs.Colormap, pixd.Colormap);
        Assert.Equal(1, pixd.Colormap!.Count);

        Pix other = Pix.Create(3, 3, 8);
        Pix.Copy(other, pixs);
        Assert.Equal(10, other.Width);
        Assert.Equal(pixs.Data, other.Data);
    }

    [Fact]
    public void InvertKeepsPadBits()
    {
        Pix pix = Pix.Create(33, 2, 1);
        Pix.Invert(pix, pix);
        Assert.Equal(0xffffffffu, pix.Data[0]);
        Assert.Equal(0x80000000u, pix.Data[1]);
        Assert.False(pix.Zero());
        Assert.Equal(1, pix.GetMaxColorIndex());
    }

    [Fact]
    public void SetPadBits()
    {
        Pix pix = Pix.Create(36, 1, 2);   /* 72 bits: 24 pad bits in word 2 */
        pix.SetPadBits(1);
        Assert.Equal(0x00ffffffu, pix.Data[2]);
        Assert.True(pix.Zero());
        pix.SetPadBits(0);
        Assert.Equal(0u, pix.Data[2]);
    }

    [Fact]
    public void ColormapValidityAgainstPix()
    {
        Pix pix = Pix.CreateWithCmap(4, 1, 2, L.L_SET_BLACK);
        Assert.True(pix.Colormap!.IsValid(pix));
        ArrayAccess.SetDataDibit(pix.Data, 2, 1);   /* index 1 is not in the cmap */
        Assert.False(pix.Colormap.IsValid(pix));
        pix.Colormap.AddColor(255, 0, 0);
        Assert.True(pix.Colormap.IsValid(pix));
    }

    [Fact]
    public void EndianByteSwap()
    {
        Pix pix = Pix.Create(1, 1, 32);
        pix.Data[0] = 0x11223344;
        Pix swapped = pix.EndianByteSwapNew();
        Assert.Equal(0x44332211u, swapped.Data[0]);
        swapped.EndianByteSwap();
        Assert.Equal(0x11223344u, swapped.Data[0]);
    }
}
