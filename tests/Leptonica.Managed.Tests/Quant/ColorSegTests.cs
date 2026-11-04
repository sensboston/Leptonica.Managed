using System;
using Leptonica;

namespace Leptonica.Managed.Tests.QuantTests;

/// <summary>
/// Tests of colorseg.c.  Phases 2 and 4 (pixAssignToNearestColor) need the octcube tables of
/// colorquant1.c, which is ported on another branch; those tests are skipped until it is merged.
/// </summary>
public class ColorSegTests
{

    private static uint Get(Pix pix, int x, int y)
    {
        pix.GetPixel(x, y, out uint v);
        return v;
    }

    private static uint Rgb(int r, int g, int b)
    {
        Pix.ComposeRGBPixel(r, g, b, out uint p);
        return p;
    }

    /* Four horizontal bands of nearly uniform colors, with a little noise */
    private static Pix Bands(int w, int h)
    {
        Pix pix = Pix.Create(w, h, 32);
        uint[] colors = { Rgb(220, 30, 30), Rgb(30, 220, 30), Rgb(30, 30, 220), Rgb(200, 200, 40) };
        var rng = new Random(3);
        for (int i = 0; i < h; i++) {
            uint c = colors[i * 4 / h];
            for (int j = 0; j < w; j++) {
                Pix.ExtractRGBValues(c, out int r, out int g, out int b);
                pix.SetPixel(j, i, Rgb(r + rng.Next(-6, 7), g + rng.Next(-6, 7), b + rng.Next(-6, 7)));
            }
        }
        return pix;
    }

    private static int[] CmapColor(PixColormap cmap, int i)
    {
        cmap.GetColor(i, out int r, out int g, out int b);
        return new[] { r, g, b };
    }

    [Fact]
    public void Cluster_FindsOneClusterPerBand()
    {
        Pix pixs = Bands(40, 40);
        Pix pixd = pixs.ColorSegmentCluster(60, 10, 0);
        pixd.GetDimensions(out int w, out int h, out int d);
        Assert.Equal((40, 40, 8), (w, h, d));
        PixColormap cmap = pixd.Colormap!;
        Assert.Equal(4, cmap.Count);
        /* greedy in raster order: the cluster index follows the band order */
        for (int band = 0; band < 4; band++)
            Assert.Equal((uint)band, Get(pixd, 20, band * 10 + 5));
        /* the colormap holds the cluster averages, close to the band colors */
        int[] c0 = CmapColor(cmap, 0);
        Assert.InRange(c0[0], 214, 226);
        Assert.InRange(c0[1], 24, 36);
        int[] c2 = CmapColor(cmap, 2);
        Assert.InRange(c2[2], 214, 226);
    }

    [Fact]
    public void Cluster_IncreasesTheDistanceWhenThereAreTooManyColors()
    {
        Pix pixs = Bands(40, 40);
        /* maxdist 10 gives too many clusters; it is increased by 1.3 until <= 4 colors fit */
        Pix pixd = pixs.ColorSegmentCluster(10, 4, 0);
        Assert.InRange(pixd.Colormap!.Count, 1, 4);
        /* maxdist 440 gives exactly one color */
        Assert.Equal(1, pixs.ColorSegmentCluster(440, 10, 0).Colormap!.Count);
    }

    [Fact]
    public void Cluster_FailsAfterTooManyIterations()
    {
        Pix pixs = Bands(40, 40);
        /* with maxdist 0 the factor 1.3 never changes the integer distance, so 20 tries fail */
        Assert.Throws<LeptonicaException>(() => pixs.ColorSegmentCluster(0, 3, 0));
    }

    [Fact]
    public void Cluster_InvalidInput()
    {
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).ColorSegmentCluster(60, 10, 0));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).ColorSegment(60, 10, 3, 4, 0));
    }

    [Fact]
    public void Clean_ClosingAbsorbsSmallHolesOfTheLargestColor()
    {
        Pix pixs = Pix.Create(30, 30, 8);
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(255, 0, 0);
        cmap.AddColor(0, 255, 0);
        pixs.SetColormap(cmap);
        for (int i = 0; i < 30; i++)
            for (int j = 0; j < 30; j++)
                pixs.SetPixel(j, i, 0);
        pixs.SetPixel(15, 15, 1);          /* an isolated pixel of the minority color */
        int[] counts = new int[256];
        counts[0] = 899;
        counts[1] = 1;
        pixs.ColorSegmentClean(3, counts);
        Assert.Equal(0u, Get(pixs, 15, 15));      /* closed by the majority color */
    }

    [Fact]
    public void Clean_SelsizeOneOrLessIsANoOp()
    {
        Pix pixs = Pix.Create(10, 10, 8);
        PixColormap cmap = PixColormap.Create(8);
        cmap.AddColor(255, 0, 0);
        cmap.AddColor(0, 255, 0);
        pixs.SetColormap(cmap);
        pixs.SetPixel(5, 5, 1);
        pixs.ColorSegmentClean(1, new int[256]);
        Assert.Equal(1u, Get(pixs, 5, 5));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).ColorSegmentClean(3, new int[256]));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 32).ColorSegmentClean(3, new int[256]));
    }

    [Fact]
    public void RemoveColors_NothingToDoWhenFewColors()
    {
        Pix pixs = Bands(20, 20);
        Pix pixd = pixs.ColorSegmentCluster(60, 10, 0);
        Pix copy = Pix.Copy(null, pixd);
        pixd.ColorSegmentRemoveColors(pixs, 4);
        Assert.True(Pix.Equal(copy, pixd));
        pixd.ColorSegmentRemoveColors(pixs, 10);
        Assert.True(Pix.Equal(copy, pixd));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).ColorSegmentRemoveColors(pixs, 2));
    }

    [Fact]
    public void AssignToNearestColor_AssignsEachPixelToTheNearestCluster()
    {
        Pix pixs = Bands(40, 40);
        Pix pixd = pixs.ColorSegmentCluster(60, 10, 0);
        int[] counts = new int[256];
        pixd.AssignToNearestColor(pixs, null, 4, counts);
        Assert.Equal(1600, counts[0] + counts[1] + counts[2] + counts[3]);
        Assert.Equal(400, counts[0]);
        Assert.Equal(0u, Get(pixd, 5, 2));
        Assert.Equal(3u, Get(pixd, 5, 38));
    }

    [Fact]
    public void AssignToNearestColor_MaskLimitsTheAssignment()
    {
        Pix pixs = Bands(40, 40);
        Pix pixd = pixs.ColorSegmentCluster(60, 10, 0);
        Pix mask = Pix.Create(40, 40, 1);
        mask.SetPixel(5, 2, 1);
        pixd.SetPixel(5, 2, 3);                    /* wrong on purpose */
        pixd.SetPixel(6, 2, 3);                    /* wrong but not in the mask */
        pixd.AssignToNearestColor(pixs, mask, 4, null);
        Assert.Equal(0u, Get(pixd, 5, 2));
        Assert.Equal(3u, Get(pixd, 6, 2));
        Assert.Throws<ArgumentException>(() => pixd.AssignToNearestColor(pixs, null, 7, null));
        Assert.Throws<ArgumentException>(() => pixd.AssignToNearestColor(Pix.Create(5, 5, 8), null, 4, null));
    }

    [Fact]
    public void ColorSegment_ReducesToTheFinalColors()
    {
        Pix pixs = Bands(40, 40);
        Pix pixd = pixs.ColorSegment(60, 10, 3, 3, 0);
        Assert.Equal(8, pixd.Depth);
        Assert.Equal(3, pixd.Colormap!.Count);
        Pix pix4 = pixs.ColorSegment(60, 10, 3, 4, 0);
        Assert.Equal(4, pix4.Colormap!.Count);
        Assert.Equal(pix4.Colormap.GetColor32((int)Get(pix4, 5, 2)),
                     pix4.Colormap.GetColor32((int)Get(pix4, 30, 8)));
    }
}
