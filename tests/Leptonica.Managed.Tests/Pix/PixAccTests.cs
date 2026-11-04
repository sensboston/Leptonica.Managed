using System;
using Leptonica;

namespace Leptonica.Managed.Tests.PixTests;

public class PixAccTests
{
    private static Pix Const(int w, int h, int d, uint val)
    {
        Pix pix = Pix.Create(w, h, d);
        for (int i = 0; i < h; i++)
            for (int j = 0; j < w; j++)
                pix.SetPixel(j, i, val);
        return pix;
    }

    private static uint Get(Pix pix, int x, int y)
    {
        pix.GetPixel(x, y, out uint v);
        return v;
    }

    [Fact]
    public void Create_Offset()
    {
        Assert.Equal(0, Pixacc.Create(4, 3, 0).GetOffset());
        Assert.Equal(0x40000000, Pixacc.Create(4, 3, 1).GetOffset());
        Pix pix = Pixacc.Create(4, 3, 1).GetPix();
        Assert.Equal(32, pix.Depth);
        Assert.Equal(0x40000000u, Get(pix, 2, 1));
        Assert.Equal(0u, Get(Pixacc.Create(4, 3, 0).GetPix(), 2, 1));
    }

    [Fact]
    public void Add_AccumulatesAndFinalClips()
    {
        Pixacc acc = Pixacc.Create(4, 3, 0);
        acc.Add(Const(4, 3, 8, 10));
        Assert.Equal(10u, Get(acc.Final(8), 3, 2));
        acc.Add(Const(4, 3, 8, 100));
        acc.Add(Const(4, 3, 8, 200));
        Pix p8 = acc.Final(8);
        Assert.Equal(8, p8.Depth);
        Assert.Equal(255u, Get(p8, 1, 1));      /* 310 clipped */
        Pix p16 = acc.Final(16);
        Assert.Equal(16, p16.Depth);
        Assert.Equal(310u, Get(p16, 1, 1));
        Assert.Equal(310u, Get(acc.Final(32), 1, 1));
    }

    [Fact]
    public void Subtract_NeedsTheOffsetForNegativeResults()
    {
        Pixacc acc = Pixacc.Create(4, 3, 1);
        acc.Add(Const(4, 3, 8, 100));
        acc.Subtract(Const(4, 3, 8, 30));
        Assert.Equal(70u, Get(acc.Final(8), 0, 0));
        acc.Subtract(Const(4, 3, 8, 200));       /* 70 - 200 < 0: clipped to 0 */
        Assert.Equal(0u, Get(acc.Final(8), 0, 0));
        acc.Add(Const(4, 3, 8, 250));            /* back to a positive value: 120 */
        Assert.Equal(120u, Get(acc.Final(8), 0, 0));
    }

    [Fact]
    public void MultConst_ScalesRelativeToTheOffset()
    {
        Pixacc acc = Pixacc.Create(3, 3, 1);
        acc.Add(Const(3, 3, 8, 100));
        acc.MultConst(0.5f);
        Assert.Equal(50u, Get(acc.Final(8), 1, 1));
        acc.MultConst(-1.0f);                    /* -50, clipped by the final conversion */
        Assert.Equal(0u, Get(acc.Final(8), 1, 1));
        acc.MultConst(-2.0f);                    /* 100 */
        Assert.Equal(100u, Get(acc.Final(8), 1, 1));
    }

    [Fact]
    public void LinearCombination()
    {
        Pix a = Const(5, 4, 8, 100);
        Pix b = Const(5, 4, 8, 40);
        Pixacc acc = Pixacc.CreateFromPix(a, 0);
        acc.MultConst(0.5f);                     /* 50 */
        acc.MultConstAccumulate(b, 0.5f);        /* + 20 */
        Pix d = acc.Final(8);
        Assert.Equal(70u, Get(d, 4, 3));
        Assert.Equal(70u, Get(d, 0, 0));
    }

    [Fact]
    public void MultConstAccumulate_NegativeFactorAndZeroFactor()
    {
        /* With a 32 bpp pix the negative product (as a two's complement word) is added in */
        Pix a = Const(5, 4, 32, 100);
        Pix b = Const(5, 4, 32, 40);
        Pixacc acc = Pixacc.CreateFromPix(a, 1);
        acc.MultConstAccumulate(b, -0.5f);       /* 100 - 20 */
        Assert.Equal(80u, Get(acc.Final(32), 2, 2));
        acc.MultConstAccumulate(b, 0.0f);        /* no change */
        Assert.Equal(80u, Get(acc.Final(32), 2, 2));

        /* With an 8 bpp pix the temporary result is clipped at 0, so nothing is subtracted */
        Pixacc acc8 = Pixacc.CreateFromPix(Const(5, 4, 8, 100), 1);
        acc8.MultConstAccumulate(Const(5, 4, 8, 40), -0.5f);
        Assert.Equal(100u, Get(acc8.Final(8), 2, 2));
    }

    [Fact]
    public void Add_BinaryPix_CountsForegroundPixels()
    {
        Pix a = Pix.Create(4, 4, 1);
        a.SetPixel(1, 1, 1);
        a.SetPixel(2, 3, 1);
        Pixacc acc = Pixacc.Create(4, 4, 0);
        acc.Add(a);
        acc.Add(a);
        acc.Add(a);
        Pix d = acc.Final(8);
        Assert.Equal(3u, Get(d, 1, 1));
        Assert.Equal(3u, Get(d, 2, 3));
        Assert.Equal(0u, Get(d, 0, 0));
    }

    [Fact]
    public void Add_SmallerAndLargerPix_AreClippedToTheCommonArea()
    {
        Pixacc acc = Pixacc.Create(4, 4, 0);
        acc.Add(Const(2, 2, 8, 9));
        Pix d = acc.Final(8);
        Assert.Equal(9u, Get(d, 1, 1));
        Assert.Equal(0u, Get(d, 3, 3));
        acc.Add(Const(10, 10, 8, 1));
        d = acc.Final(8);
        Assert.Equal(10u, Get(d, 1, 1));
        Assert.Equal(1u, Get(d, 3, 3));
    }
}
