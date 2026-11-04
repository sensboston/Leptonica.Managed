using System;
using Leptonica;

namespace Leptonica.Managed.Tests.PixTiling;

/// <summary>
/// Tests of pixtiling.c.  The round trip check follows pixtile_reg.c, on a synthetic image
/// (the size of test24.jpg is not known here, a larger one is used): tile and paint back every tile, then compare with the source.
/// </summary>
public class PixTilingTests
{
    private static Pix MakeRgb(int w, int h)
    {
        Pix pix = Pix.Create(w, h, 32);
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w; j++) {
                Pix.ComposeRGBPixel((j * 7 + i * 3) & 255, (j * 5 + i * 11) & 255,
                                    (j * i + 13) & 255, out uint p);
                pix.SetPixel(j, i, p);
            }
        }
        return pix;
    }

    private static Leptonica.PixTiling RoundTrip(Pix pixd, Pix pixs, int nx, int ny, int w, int h,
                                                 int xoverlap, int yoverlap)
    {
        pixd.SetAll();
        pixd.ClearAll();
        var pt = Leptonica.PixTiling.Create(pixs, nx, ny, w, h, xoverlap, yoverlap);
        pt.GetCount(out nx, out ny);
        for (int i = 0; i < ny; i++) {
            for (int j = 0; j < nx; j++) {
                Pix pixt = pt.GetTile(i, j);
                pt.PaintTile(pixd, i, j, pixt);
            }
        }
        return pt;
    }

    [Theory]
    [InlineData(1, 1, 0, 0, 183, 83)]
    [InlineData(0, 1, 60, 0, 30, 20)]
    [InlineData(1, 0, 0, 60, 40, 40)]
    [InlineData(0, 0, 27, 31, 27, 31)]
    [InlineData(0, 0, 400, 400, 40, 20)]
    [InlineData(7, 9, 0, 0, 35, 35)]
    [InlineData(0, 0, 27, 31, 0, 0)]
    [InlineData(7, 9, 0, 0, 0, 0)]
    public void TilingRoundTrip(int nx, int ny, int w, int h, int xoverlap, int yoverlap)
    {
        Pix pixs = MakeRgb(300, 330);
        Pix pixd = Pix.CreateTemplateNoInit(pixs);
        RoundTrip(pixd, pixs, nx, ny, w, h, xoverlap, yoverlap);
        Assert.True(Pix.Equal(pixs, pixd));
    }

    [Fact]
    public void TilingRoundTrip_Gray()
    {
        Pix pixs = Pix.Create(100, 70, 8);
        for (int i = 0; i < 70; i++)
            for (int j = 0; j < 100; j++)
                pixs.SetPixel(j, i, (uint)((i * 17 + j * 3) & 255));
        Pix pixd = Pix.CreateTemplateNoInit(pixs);
        RoundTrip(pixd, pixs, 3, 2, 0, 0, 10, 10);
        Assert.True(Pix.Equal(pixs, pixd));
    }

    [Fact]
    public void CountAndSize()
    {
        Pix pixs = MakeRgb(100, 70);
        var pt = Leptonica.PixTiling.Create(pixs, 0, 0, 30, 25, 5, 4);
        pt.GetCount(out int nx, out int ny);
        pt.GetSize(out int w, out int h);
        Assert.Equal((3, 2), (nx, ny));  /* 100 / 30 = 3, 70 / 25 = 2 */
        Assert.Equal((33, 35), (w, h));  /* 100 / 3, 70 / 2 */
        Assert.Equal((5, 4), (pt.XOverlap, pt.YOverlap));
    }

    [Fact]
    public void TileSizes()
    {
        Pix pixs = MakeRgb(100, 70);
        var pt = Leptonica.PixTiling.Create(pixs, 3, 2, 0, 0, 5, 4);
        /* all tiles are returned with the overlap (a mirrored border at the image edge) */
        pt.GetTile(0, 0).GetDimensions(out int w, out int h, out _);
        Assert.Equal((33 + 2 * 5, 35 + 2 * 4), (w, h));
        pt.GetTile(1, 1).GetDimensions(out w, out h, out _);
        Assert.Equal((33 + 2 * 5, 35 + 2 * 4), (w, h));
        /* the last column is 100 - 2 * 33 = 34 wide */
        pt.GetTile(1, 2).GetDimensions(out w, out h, out _);
        Assert.Equal((34 + 2 * 5, 35 + 2 * 4), (w, h));
    }

    [Fact]
    public void TileWithoutOverlapIsPlainClip()
    {
        Pix pixs = MakeRgb(100, 70);
        var pt = Leptonica.PixTiling.Create(pixs, 4, 2, 0, 0, 0, 0);
        Pix tile = pt.GetTile(1, 2);
        tile.GetDimensions(out int w, out int h, out _);
        Assert.Equal((25, 35), (w, h));
        for (int i = 0; i < h; i++) {
            for (int j = 0; j < w; j++) {
                tile.GetPixel(j, i, out uint a);
                pixs.GetPixel(2 * 25 + j, 35 + i, out uint b);
                Assert.Equal(b, a);
            }
        }
    }

    [Fact]
    public void NoStripOnPaint_PaintsWholeTile()
    {
        Pix pixs = MakeRgb(100, 70);
        var pt = Leptonica.PixTiling.Create(pixs, 2, 1, 0, 0, 6, 0);
        Pix tile = pt.GetTile(0, 1);
        pt.NoStripOnPaint();
        Pix pixd = Pix.Create(100, 70, 32);
        pt.PaintTile(pixd, 0, 1, tile);
        /* the whole tile (including its overlap) is copied to (j * w, i * h) */
        tile.GetDimensions(out int tw, out int th, out _);
        for (int i = 0; i < th; i++) {
            for (int j = 0; j < Math.Min(tw, 50); j++) {  /* clipped at the right edge of pixd */
                tile.GetPixel(j, i, out uint a);
                pixd.GetPixel(50 + j, i, out uint b);
                Assert.Equal(a, b);
            }
        }
    }

    [Fact]
    public void InvalidArguments()
    {
        Pix pixs = MakeRgb(100, 70);
        Assert.Throws<ArgumentException>(() => Leptonica.PixTiling.Create(pixs, 0, 1, 0, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => Leptonica.PixTiling.Create(pixs, 1, 0, 0, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => Leptonica.PixTiling.Create(pixs, 4, 2, 0, 0, 26, 0));
        var pt = Leptonica.PixTiling.Create(pixs, 4, 2, 0, 0, 0, 0);
        Assert.Throws<ArgumentException>(() => pt.GetTile(2, 0));
        Assert.Throws<ArgumentException>(() => pt.GetTile(0, 4));
        Assert.Throws<ArgumentException>(() => pt.GetTile(-1, 0));
        Assert.Throws<ArgumentException>(() => pt.PaintTile(pixs, 0, 4, pixs));
        Assert.Throws<ArgumentException>(() => pt.PaintTile(pixs, 2, 0, pixs));
    }
}
