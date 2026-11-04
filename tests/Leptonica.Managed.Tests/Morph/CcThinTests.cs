using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Morph;

/// <summary>
/// Tests of ccthin.c.  Thinning is checked through its defining properties: the result is a
/// subset of the source, it has the same number of connected components, it contains no solid
/// 2 x 2 block, and thinning it again changes nothing.
/// </summary>
public class CcThinTests
{
    private static Pix Rect(int w, int h, int x0, int y0, int x1, int y1)
    {
        Pix pix = Pix.Create(w, h, 1);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                pix.SetPixel(x, y, 1);
        return pix;
    }

    private static Pix Noise(int seed, int w, int h, double density)
    {
        var rng = new Random(seed);
        Pix pix = Pix.Create(w, h, 1);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (rng.NextDouble() < density)
                    pix.SetPixel(x, y, 1);
        return pix;
    }

    private static int Count(Pix pix) => pix.CountPixels(null);

    private static uint Get(Pix pix, int x, int y)
    {
        pix.GetPixel(x, y, out uint v);
        return v;
    }

    private static bool IsSubset(Pix sub, Pix sup)
    {
        return Pix.Subtract(null, sub, sup).Zero();
    }

    private static bool HasSolidBlock(Pix pix)
    {
        pix.GetDimensions(out int w, out int h, out _);
        for (int y = 0; y + 1 < h; y++)
            for (int x = 0; x + 1 < w; x++)
                if (Get(pix, x, y) + Get(pix, x + 1, y) + Get(pix, x, y + 1) + Get(pix, x + 1, y + 1) == 4)
                    return true;
        return false;
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void ThinConnected_Rectangle(int conn)
    {
        Pix pixs = Rect(30, 20, 4, 4, 24, 13);
        Pix pixd = pixs.ThinConnected(L.L_THIN_FG, conn, 0);
        Assert.True(IsSubset(pixd, pixs));
        Assert.True(Count(pixd) > 0);
        Assert.True(Count(pixd) < Count(pixs) / 4);
        Assert.Equal(1, pixd.CountConnComp(conn));
        Assert.False(HasSolidBlock(pixd));
        Assert.True(Pix.Equal(pixd, pixd.ThinConnected(L.L_THIN_FG, conn, 0)));
    }

    [Theory]
    [InlineData(4, 1)]
    [InlineData(4, 2)]
    [InlineData(8, 3)]
    [InlineData(8, 4)]
    public void ThinConnected_KeepsComponents(int conn, int seed)
    {
        Pix pixs = Noise(seed, 40, 30, 0.62);
        int ncc = pixs.CountConnComp(conn);
        Pix pixd = pixs.ThinConnected(L.L_THIN_FG, conn, 0);
        Assert.True(IsSubset(pixd, pixs));
        Assert.Equal(ncc, pixd.CountConnComp(conn));
        Assert.True(Count(pixd) <= Count(pixs));
        Assert.True(Pix.Equal(pixd, pixd.ThinConnected(L.L_THIN_FG, conn, 0)));
    }

    [Fact]
    public void ThinConnected_MaxItersLimitsTheThinning()
    {
        Pix pixs = Rect(31, 31, 2, 2, 28, 28);
        Pix one = pixs.ThinConnected(L.L_THIN_FG, 8, 1);
        Pix full = pixs.ThinConnected(L.L_THIN_FG, 8, 0);
        Assert.True(Count(one) < Count(pixs));
        Assert.True(Count(full) < Count(one));
        /* the result of one iteration is thinned further by the remaining iterations */
        Assert.True(IsSubset(full, one));
        Assert.True(Pix.Equal(full, one.ThinConnected(L.L_THIN_FG, 8, 0)));
    }

    [Fact]
    public void ThinConnected_EmptyAndNoChange()
    {
        Pix empty = Pix.Create(10, 10, 1);
        Assert.True(empty.ThinConnected(L.L_THIN_FG, 8, 0).Zero());
        Pix.Equal(empty, empty.ThinConnected(L.L_THIN_BG, 4, 1));
    }

    [Fact]
    public void ThinConnected_ThickensWithTheBackground()
    {
        Pix pixs = Rect(21, 21, 6, 10, 14, 10);   /* a horizontal line */
        Pix pixd = pixs.ThinConnected(L.L_THIN_BG, 8, 2);
        Assert.True(IsSubset(pixs, pixd));
        Assert.True(Count(pixd) > Count(pixs));
        Assert.Equal(1, pixd.CountConnComp(4));
    }

    [Fact]
    public void ThinConnected_DoesNotModifyTheSource()
    {
        Pix pixs = Noise(11, 25, 25, 0.6);
        Pix copy = Pix.Copy(null, pixs);
        pixs.ThinConnected(L.L_THIN_FG, 8, 0);
        pixs.ThinConnected(L.L_THIN_BG, 4, 3);
        Assert.True(Pix.Equal(pixs, copy));
    }

    [Fact]
    public void ThinConnectedBySet_MatchesThinConnected()
    {
        Pix pixs = Noise(5, 30, 30, 0.6);
        Assert.True(Pix.Equal(pixs.ThinConnected(L.L_THIN_FG, 4, 0),
                              pixs.ThinConnectedBySet(L.L_THIN_FG, Sela.MakeThinSets(1, 0), 0)));
        Assert.True(Pix.Equal(pixs.ThinConnected(L.L_THIN_FG, 8, 0),
                              pixs.ThinConnectedBySet(L.L_THIN_FG, Sela.MakeThinSets(5, 0), 0)));
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 3)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(5, 4)]
    [InlineData(6, 3)]
    [InlineData(7, 3)]
    [InlineData(8, 4)]
    [InlineData(9, 4)]
    [InlineData(10, 2)]
    [InlineData(11, 1)]
    public void MakeThinSets_SizesAndNames(int index, int nsels)
    {
        Sela sela = Sela.MakeThinSets(index, 0);
        Assert.Equal(nsels, sela.Count);
        for (int i = 0; i < nsels; i++)
            Assert.NotNull(sela.GetSel(i));
    }

    [Fact]
    public void MakeThinSets_SelNames()
    {
        Sela s1 = Sela.MakeThinSets(1, 0);
        Assert.Equal(new[] { "sel_4_1", "sel_4_2", "sel_4_3" },
                     new[] { s1.GetSel(0).Name, s1.GetSel(1).Name, s1.GetSel(2).Name });
        /* A rotated sel keeps the name of its source, so the "_rot" names that
         * selaMakeThinSets() passes to selaAddSel() are not used. */
        Sela s3 = Sela.MakeThinSets(3, 0);
        Assert.Equal(new[] { "sel_4_1", "sel_4_7", "sel_4_7" },
                     new[] { s3.GetSel(0).Name, s3.GetSel(1).Name, s3.GetSel(2).Name });
        Sela s4 = Sela.MakeThinSets(4, 0);
        Assert.Equal(new[] { "sel_48_1", "sel_48_1", "sel_48_2" },
                     new[] { s4.GetSel(0).Name, s4.GetSel(1).Name, s4.GetSel(2).Name });
        Sela s9 = Sela.MakeThinSets(9, 0);
        Assert.Equal("sel_8_7", s9.GetSel(3).Name);
        Sela s5 = Sela.MakeThinSets(5, 0);
        Assert.Equal(new[] { "sel_8_2", "sel_8_3", "sel_8_5", "sel_8_6" },
                     new[] { s5.GetSel(0).Name, s5.GetSel(1).Name, s5.GetSel(2).Name, s5.GetSel(3).Name });
    }

    [Fact]
    public void MakeThinSets_InvalidIndex()
    {
        Assert.Throws<ArgumentException>(() => Sela.MakeThinSets(0, 0));
        Assert.Throws<ArgumentException>(() => Sela.MakeThinSets(12, 0));
    }

    [Fact]
    public void PixaThinConnected_ThinsEachPix()
    {
        Pixa pixa = Pixa.Create(2);
        Pix a = Rect(20, 12, 2, 2, 17, 9);
        Pix b = Noise(9, 20, 20, 0.6);
        pixa.AddPix(a, L.L_COPY);
        pixa.AddPix(b, L.L_COPY);
        Pixa res = pixa.ThinConnected(L.L_THIN_FG, 8, 0);
        Assert.Equal(2, res.Count);
        Assert.True(Pix.Equal(a.ThinConnected(L.L_THIN_FG, 8, 0), res.GetPix(0, L.L_CLONE)));
        Assert.True(Pix.Equal(b.ThinConnected(L.L_THIN_FG, 8, 0), res.GetPix(1, L.L_CLONE)));
    }

    [Fact]
    public void InvalidArguments()
    {
        Pix pix = Rect(10, 10, 2, 2, 7, 7);
        Assert.Throws<ArgumentException>(() => pix.ThinConnected(5, 8, 0));
        Assert.Throws<ArgumentException>(() => pix.ThinConnected(L.L_THIN_FG, 6, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(10, 10, 8).ThinConnected(L.L_THIN_FG, 8, 0));
        Assert.Throws<ArgumentException>(() => pix.ThinConnectedBySet(0, Sela.MakeThinSets(1, 0), 0));
        Pixa pixa = Pixa.Create(1);
        pixa.AddPix(Pix.Create(10, 10, 8), L.L_COPY);
        Assert.Throws<ArgumentException>(() => pixa.ThinConnected(L.L_THIN_FG, 8, 0));
    }
}
