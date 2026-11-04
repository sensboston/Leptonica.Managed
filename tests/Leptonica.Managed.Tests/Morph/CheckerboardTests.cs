using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Morph;

/// <summary>
/// Tests of checkerboard.c.  pixFindCheckerboardCorners() calls pixUnionOfMorphOps(); here
/// the sel generation is tested directly and the corner
/// detection is checked with the HMT of each sel on a synthetic checkerboard.
/// </summary>
public class CheckerboardTests
{
    private static uint Get(Pix pix, int x, int y)
    {
        pix.GetPixel(x, y, out uint v);
        return v;
    }

    private static Pix Board(int nsq, int sq)
    {
        int n = nsq * sq;
        Pix pix = Pix.Create(n, n, 1);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                if (((x / sq) + (y / sq)) % 2 == 0)
                    pix.SetPixel(x, y, 1);
        return pix;
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void CornerPixa_Count(int nsels)
    {
        Pixa pixa = Pix.MakeCheckerboardCornerPixa(15, 3, nsels);
        Assert.Equal(nsels, pixa.Count);
        for (int i = 0; i < nsels; i++) {
            Pix p = pixa.GetPix(i, L.L_CLONE);
            p.GetDimensions(out int w, out int h, out int d);
            Assert.Equal((15, 15, 32), (w, h, d));
        }
    }

    [Fact]
    public void CornerPixa_Colors()
    {
        Pixa pixa = Pix.MakeCheckerboardCornerPixa(15, 3, 2);
        Pix p = pixa.GetPix(0, L.L_CLONE);
        Pix.ComposeRGBPixel(0, 255, 0, out uint green);
        Pix.ComposeRGBPixel(255, 0, 0, out uint red);
        Pix.ComposeRGBPixel(128, 128, 128, out uint gray);
        Assert.Equal(green, Get(p, 1, 1));       /* dilated 3x3 around (1,1) */
        Assert.Equal(green, Get(p, 0, 0));
        Assert.Equal(green, Get(p, 13, 13));
        Assert.Equal(red, Get(p, 1, 13));        /* rotated mask */
        Assert.Equal(red, Get(p, 13, 1));
        Assert.Equal(gray, Get(p, 7, 7));
        Assert.Equal(0xffffffffu, Get(p, 7, 3));
        /* second sel has the colors swapped */
        Pix q = pixa.GetPix(1, L.L_CLONE);
        Assert.Equal(red, Get(q, 1, 1));
        Assert.Equal(green, Get(q, 1, 13));
    }

    [Fact]
    public void CornerSela_NamesAndSize()
    {
        Sela s2 = Pix.MakeCheckerboardCornerSela(15, 3, 2, null);
        Assert.Equal(2, s2.Count);
        Assert.Equal("s_diag1", s2.GetSel(0).Name);
        Assert.Equal("s_diag2", s2.GetSel(1).Name);
        Sela s4 = Pix.MakeCheckerboardCornerSela(15, 1, 4, null);
        Assert.Equal(4, s4.Count);
        Assert.Equal("s_cross2", s4.GetSel(3).Name);
        s4.GetSel(0).GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((15, 15, 7, 7), (sy, sx, cy, cx));
    }

    [Fact]
    public void CornerSela_DebugAddsTheTiledSels()
    {
        Pixa db = Pixa.Create(0);
        Pix.MakeCheckerboardCornerSela(15, 3, 2, db);
        Assert.Equal(1, db.Count);
    }

    [Fact]
    public void CornerSela_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Pix.MakeCheckerboardCornerSela(5, 3, 2, null));
        Assert.Throws<ArgumentException>(() => Pix.MakeCheckerboardCornerSela(15, 0, 2, null));
        Assert.Throws<ArgumentException>(() => Pix.MakeCheckerboardCornerSela(15, 6, 2, null));
        Assert.Throws<ArgumentException>(() => Pix.MakeCheckerboardCornerSela(15, 3, 3, null));
    }

    [Fact]
    public void FindCheckerboardCorners_InvalidArguments()
    {
        Pix pix = Board(3, 30);
        Assert.Throws<ArgumentException>(() => pix.FindCheckerboardCorners(5, 3, 2, out _, out _, null));
        Assert.Throws<ArgumentException>(() => pix.FindCheckerboardCorners(15, 9, 2, out _, out _, null));
        Assert.Throws<ArgumentException>(() => pix.FindCheckerboardCorners(15, 3, 3, out _, out _, null));
    }

    [Fact]
    public void Hmt_FindsTheJunctionsOfAnAlignedBoard()
    {
        Pix board = Board(4, 30);
        Sela sela = Pix.MakeCheckerboardCornerSela(15, 3, 2, null);
        Pix hit = Pix.HMT(null, board, sela.GetSel(0));
        Pix.Or(hit, hit, Pix.HMT(null, board, sela.GetSel(1)));
        Assert.False(hit.Zero());
        /* every interior junction (30k, 30m) has a hit within one pixel */
        for (int k = 1; k <= 3; k++) {
            for (int m = 1; m <= 3; m++) {
                bool found = false;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (Get(hit, 30 * k + dx, 30 * m + dy) == 1)
                            found = true;
                Assert.True(found, $"junction ({30 * k},{30 * m})");
            }
        }
        /* no hit in the middle of a square or on an edge */
        Assert.Equal(0u, Get(hit, 15, 15));
        Assert.Equal(0u, Get(hit, 45, 15));
        Assert.Equal(0u, Get(hit, 30, 15));
    }
}
