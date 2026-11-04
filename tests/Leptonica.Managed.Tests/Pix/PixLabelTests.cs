using System;
using Leptonica;

namespace Leptonica.Managed.Tests.PixTests;

public class PixLabelTests
{
    private static uint Get(Pix pix, int x, int y)
    {
        pix.GetPixel(x, y, out uint v);
        return v;
    }

    private static Pix Binary(int w, int h, params (int x, int y)[] pts)
    {
        Pix pix = Pix.Create(w, h, 1);
        foreach (var (x, y) in pts)
            pix.SetPixel(x, y, 1);
        return pix;
    }

    /* Components (8-connected): {(8,0), (9,1)} first in raster order, then the bar
     * {(1,1), (2,1), (3,1)}, then the single pixel (6,3).  With 4-connectivity (9,1)
     * is a component of its own. */
    private static Pix Sample() => Binary(10, 6, (8, 0), (9, 1), (1, 1), (2, 1), (3, 1), (6, 3));

    [Fact]
    public void ConnCompTransform_Eight()
    {
        Pix d = Sample().ConnCompTransform(8, 0);
        d.GetDimensions(out int w, out int h, out int depth);
        Assert.Equal((10, 6, 8), (w, h, depth));
        Assert.Equal(1, d.Spp);
        Assert.Equal(1u, Get(d, 8, 0));
        Assert.Equal(1u, Get(d, 9, 1));
        Assert.Equal(2u, Get(d, 1, 1));
        Assert.Equal(2u, Get(d, 2, 1));
        Assert.Equal(2u, Get(d, 3, 1));
        Assert.Equal(3u, Get(d, 6, 3));
        Assert.Equal(0u, Get(d, 0, 0));
        Assert.Equal(0u, Get(d, 5, 5));
    }

    [Fact]
    public void ConnCompTransform_Four()
    {
        Pix d = Sample().ConnCompTransform(4, 16);
        Assert.Equal(16, d.Depth);
        Assert.Equal(1u, Get(d, 8, 0));
        Assert.Equal(2u, Get(d, 1, 1));
        Assert.Equal(3u, Get(d, 9, 1));
        Assert.Equal(4u, Get(d, 6, 3));
        Assert.Equal(32, Sample().ConnCompTransform(4, 32).Depth);
    }

    [Fact]
    public void ConnCompTransform_NoForegroundGivesBlankImage()
    {
        Pix d = Binary(7, 5).ConnCompTransform(8, 0);
        d.GetDimensions(out int w, out int h, out int depth);
        Assert.Equal((7, 5, 8), (w, h, depth));
        Assert.Equal(1, d.Spp);
        Assert.True(d.Zero());
    }

    [Fact]
    public void ConnCompTransform_LabelsWrapAround()
    {
        /* 300 single pixel components, 2 pixels apart */
        var pts = new (int, int)[300];
        for (int k = 0; k < 300; k++)
            pts[k] = (2 * k, 1);
        Pix pixs = Binary(600, 3, pts);
        Pix d8 = pixs.ConnCompTransform(4, 8);
        Assert.Equal(254u, Get(d8, 2 * 253, 1));    /* label 1 + 253 */
        Assert.Equal(1u, Get(d8, 2 * 254, 1));      /* 1 + 254 % 254 */
        Assert.Equal(2u, Get(d8, 2 * 255, 1));
        Pix d0 = pixs.ConnCompTransform(4, 0);
        Assert.Equal(16, d0.Depth);                 /* 300 >= 254 components */
        Assert.Equal(300u, Get(d0, 2 * 299, 1));
        Assert.Equal(300u, Get(pixs.ConnCompTransform(4, 32), 2 * 299, 1));
    }

    [Fact]
    public void ConnCompTransform_InvalidArguments()
    {
        Pix pix = Sample();
        Assert.Throws<ArgumentException>(() => pix.ConnCompTransform(6, 8));
        Assert.Throws<ArgumentException>(() => pix.ConnCompTransform(8, 4));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).ConnCompTransform(8, 8));
    }

    [Fact]
    public void ConnCompAreaTransform_ValuesAreAreas()
    {
        Pix d = Sample().ConnCompAreaTransform(8);
        Assert.Equal(32, d.Depth);
        Assert.Equal(1, d.Spp);
        Assert.Equal(2u, Get(d, 8, 0));
        Assert.Equal(2u, Get(d, 9, 1));
        Assert.Equal(3u, Get(d, 2, 1));
        Assert.Equal(1u, Get(d, 6, 3));
        Assert.Equal(0u, Get(d, 4, 4));
        Pix d4 = Sample().ConnCompAreaTransform(4);
        Assert.Equal(1u, Get(d4, 8, 0));
        Assert.Equal(1u, Get(d4, 9, 1));
        Assert.True(Binary(5, 5).ConnCompAreaTransform(8).Zero());
        Assert.Throws<ArgumentException>(() => Sample().ConnCompAreaTransform(5));
    }

    [Fact]
    public void ConnCompIncrInit_Empty()
    {
        Binary(8, 8).ConnCompIncrInit(8, out Pix pixd, out Ptaa ptaa, out int ncc);
        Assert.Equal(32, pixd.Depth);
        Assert.Equal(8, pixd.Special);
        Assert.Equal(1, ptaa.Count);        /* index 0 reserved for the background */
        Assert.Equal(0, ncc);
        Assert.True(pixd.Zero());
        Assert.Throws<ArgumentException>(() =>
            Binary(8, 8).ConnCompIncrInit(6, out _, out _, out _));
    }

    [Fact]
    public void ConnCompIncrInit_WithForeground()
    {
        Pix pixs = Binary(10, 6, (1, 1), (2, 1), (6, 3));
        pixs.ConnCompIncrInit(4, out Pix pixd, out Ptaa ptaa, out int ncc);
        Assert.Equal(2, ncc);
        Assert.Equal(3, ptaa.Count);
        Assert.Equal(4, pixd.Special);
        Assert.Equal(1u, Get(pixd, 1, 1));
        Assert.Equal(1u, Get(pixd, 2, 1));
        Assert.Equal(2u, Get(pixd, 6, 3));
        Assert.Equal(2, ptaa.GetPta(1, L.L_CLONE).Count);
        Assert.Equal(1, ptaa.GetPta(2, L.L_CLONE).Count);
    }

    [Fact]
    public void ConnCompIncrAdd_NewComponentJoinAndMerge()
    {
        Binary(10, 6).ConnCompIncrInit(8, out Pix pixd, out Ptaa ptaa, out int ncc);

        Assert.True(pixd.ConnCompIncrAdd(ptaa, ref ncc, 1, 1, 0));   /* new component 1 */
        Assert.Equal(1, ncc);
        Assert.Equal(1u, Get(pixd, 1, 1));
        Assert.True(pixd.ConnCompIncrAdd(ptaa, ref ncc, 5, 1, 0));   /* new component 2 */
        Assert.Equal(2, ncc);
        Assert.Equal(2u, Get(pixd, 5, 1));
        Assert.True(pixd.ConnCompIncrAdd(ptaa, ref ncc, 2, 1, 0));   /* joins component 1 */
        Assert.Equal(2, ncc);
        Assert.Equal(1u, Get(pixd, 2, 1));
        Assert.Equal(2, ptaa.GetPta(1, L.L_CLONE).Count);

        /* already labeled */
        Assert.False(pixd.ConnCompIncrAdd(ptaa, ref ncc, 2, 1, 0));
        Assert.Equal(2, ncc);

        Assert.True(pixd.ConnCompIncrAdd(ptaa, ref ncc, 4, 1, 0));   /* joins 2: still separate from 1 */
        Assert.Equal(2u, Get(pixd, 4, 1));
        Assert.Equal(2, ncc);
        /* (3,1) touches 1 at (2,1) and 2 at (4,1): the labels are merged into 1 */
        Assert.True(pixd.ConnCompIncrAdd(ptaa, ref ncc, 3, 1, 0));
        Assert.Equal(1, ncc);
        foreach (int x in new[] { 1, 2, 3, 4, 5 })
            Assert.Equal(1u, Get(pixd, x, 1));
        Assert.Equal(5, ptaa.GetPta(1, L.L_CLONE).Count);
        Assert.Throws<ArgumentException>(() => ptaa.GetPta(2, L.L_CLONE));   /* deleted */
    }

    [Fact]
    public void ConnCompIncrAdd_FourWayJoin()
    {
        Binary(7, 7).ConnCompIncrInit(4, out Pix pixd, out Ptaa ptaa, out int ncc);
        pixd.ConnCompIncrAdd(ptaa, ref ncc, 3, 1, 0);    /* 1: above */
        pixd.ConnCompIncrAdd(ptaa, ref ncc, 1, 3, 0);    /* 2: left */
        pixd.ConnCompIncrAdd(ptaa, ref ncc, 5, 3, 0);    /* 3: right */
        pixd.ConnCompIncrAdd(ptaa, ref ncc, 3, 5, 0);    /* 4: below */
        Assert.Equal(4, ncc);
        pixd.ConnCompIncrAdd(ptaa, ref ncc, 3, 3, 0);    /* nothing is adjacent in 4-conn */
        Assert.Equal(5, ncc);
        pixd.ConnCompIncrAdd(ptaa, ref ncc, 3, 2, 0);    /* joins comp 1 and the center */
        Assert.Equal(4, ncc);
        pixd.ConnCompIncrAdd(ptaa, ref ncc, 2, 3, 0);    /* joins 1 (via center) and 2 */
        Assert.Equal(3, ncc);
        pixd.ConnCompIncrAdd(ptaa, ref ncc, 4, 3, 0);
        Assert.Equal(2, ncc);
        pixd.ConnCompIncrAdd(ptaa, ref ncc, 3, 4, 0);
        Assert.Equal(1, ncc);
        Assert.Equal(1u, Get(pixd, 3, 5));
        Assert.Equal(1u, Get(pixd, 5, 3));
        Assert.Equal(1u, Get(pixd, 1, 3));
    }

    [Fact]
    public void ConnCompIncrAdd_InvalidArguments()
    {
        Binary(6, 6).ConnCompIncrInit(8, out Pix pixd, out Ptaa ptaa, out int ncc);
        Assert.Throws<ArgumentException>(() => pixd.ConnCompIncrAdd(ptaa, ref ncc, -1, 0, 0));
        Assert.Throws<ArgumentException>(() => pixd.ConnCompIncrAdd(ptaa, ref ncc, 6, 0, 0));
        Assert.Throws<ArgumentException>(() => pixd.ConnCompIncrAdd(ptaa, ref ncc, 0, 6, 0));
        Pix noconn = Pix.Create(6, 6, 32);   /* special == 0: not initialized */
        Assert.Throws<ArgumentException>(() => noconn.ConnCompIncrAdd(ptaa, ref ncc, 1, 1, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(6, 6, 8).ConnCompIncrAdd(ptaa, ref ncc, 1, 1, 0));
    }

    [Fact]
    public void GetSortedNeighborValues_UniqueSortedNonZero()
    {
        Pix pix = Pix.Create(5, 5, 32);
        pix.SetPixel(1, 1, 7);
        pix.SetPixel(2, 1, 3);
        pix.SetPixel(3, 1, 7);
        pix.SetPixel(1, 2, 5);
        pix.SetPixel(3, 2, 0);
        pix.SetPixel(1, 3, 3);
        pix.SetPixel(2, 3, 9);
        pix.SetPixel(3, 3, 5);
        Assert.Equal(new[] { 3, 5, 7, 9 }, pix.GetSortedNeighborValues(2, 2, 8));
        /* 4-connected: only the nearest neighbors (2,1)=3, (1,2)=5, (3,2)=0, (2,3)=9 */
        Assert.Equal(new[] { 3, 5, 9 }, pix.GetSortedNeighborValues(2, 2, 4));
        Assert.Empty(Pix.Create(5, 5, 32).GetSortedNeighborValues(2, 2, 8));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 1).GetSortedNeighborValues(2, 2, 8));
    }

    [Fact]
    public void LocToColorTransform_CodesLocationAndSize()
    {
        Pix pixs = Binary(9, 7, (4, 3), (0, 0));
        Pix d = pixs.LocToColorTransform();
        d.GetDimensions(out int w, out int h, out int depth);
        Assert.Equal((9, 7, 32), (w, h, depth));
        Pix.ComposeRGBPixel(0, 0, 1, out uint center);
        Pix.ComposeRGBPixel(255, 255, 1, out uint corner);
        Assert.Equal(center, Get(d, 4, 3));
        Assert.Equal(corner, Get(d, 0, 0));
        Assert.Equal(0u, Get(d, 2, 2));        /* background is black */
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).LocToColorTransform());
    }

    [Fact]
    public void LocToColorTransform_ClipsTheAreaAndSwapsAxesForTallImages()
    {
        /* a 20 x 20 block: area 400, clipped to 255 */
        var pts = new System.Collections.Generic.List<(int, int)>();
        for (int y = 0; y < 20; y++)
            for (int x = 0; x < 20; x++)
                pts.Add((x, y));
        Pix block = Binary(40, 40, pts.ToArray());
        Pix d = block.LocToColorTransform();
        d.GetPixel(10, 10, out uint p);
        Pix.ExtractRGBValues(p, out _, out _, out int b);
        Assert.Equal(255, b);

        /* w < h: red follows y, green follows x */
        Pix tall = Binary(5, 9, (0, 8));
        tall.GetDimensions(out _, out _, out _);
        tall.LocToColorTransform().GetPixel(0, 8, out p);
        Pix.ExtractRGBValues(p, out int r, out int g, out _);
        Assert.Equal((int)((255.0f / 4) * 4), r);   /* |8 - 4| * 255 / 4 */
        Assert.Equal((int)((255.0f / 2) * 2), g);   /* |0 - 2| * 255 / 2 */
    }
}
