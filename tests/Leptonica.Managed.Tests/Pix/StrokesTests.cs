using System;
using Leptonica;

namespace Leptonica.Managed.Tests.PixTests;

/// <summary>Tests of strokes.c. </summary>
public class StrokesTests
{
    private static Pix Block(int w, int h, int x0, int y0, int x1, int y1)
    {
        Pix pix = Pix.Create(w, h, 1);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                pix.SetPixel(x, y, 1);
        return pix;
    }

    private static uint Get(Pix pix, int x, int y)
    {
        pix.GetPixel(x, y, out uint v);
        return v;
    }

    [Fact]
    public void ModifyStrokeWidth_DilatesAndErodes()
    {
        Pix dot = Block(11, 11, 5, 5, 5, 5);
        Pix d = dot.ModifyStrokeWidth(1.0f, 3.0f);     /* diff 2: "d3.3" */
        Assert.Equal(9, d.CountPixels(null));
        Assert.Equal(1u, Get(d, 4, 4));
        Assert.Equal(1u, Get(d, 6, 6));
        Assert.Equal(0u, Get(d, 3, 5));

        Pix big = Block(15, 15, 4, 4, 8, 8);           /* 5 x 5 */
        Pix e = big.ModifyStrokeWidth(3.0f, 1.0f);     /* diff -2: "e3.3" */
        Assert.Equal(9, e.CountPixels(null));
        Assert.Equal(1u, Get(e, 6, 6));
        Assert.Equal(0u, Get(e, 4, 4));
    }

    [Fact]
    public void ModifyStrokeWidth_NoChangeIsACopy()
    {
        Pix pix = Block(11, 11, 3, 3, 6, 6);
        Pix d = pix.ModifyStrokeWidth(2.2f, 2.0f);     /* rounds to 0 */
        Assert.NotSame(pix, d);
        Assert.True(Pix.Equal(pix, d));
    }

    [Fact]
    public void ModifyStrokeWidth_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Block(5, 5, 1, 1, 2, 2).ModifyStrokeWidth(1.0f, 0.5f));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).ModifyStrokeWidth(1.0f, 2.0f));
    }

    [Fact]
    public void SetStrokeWidth_DilatesWithoutThinning()
    {
        Pix dot = Block(11, 11, 5, 5, 5, 5);
        dot.Text = "label";
        Pix d = dot.SetStrokeWidth(3, 0, 8);
        d.GetDimensions(out int w, out int h, out int depth);
        Assert.Equal((13, 13, 1), (w, h, depth));      /* border = width / 2 = 1 */
        Assert.Equal(9, d.CountPixels(null));
        Assert.Equal(1u, Get(d, 5, 5));
        Assert.Equal(1u, Get(d, 7, 7));
        Assert.Equal("label", d.Text);
    }

    [Fact]
    public void SetStrokeWidth_WidthOneWithoutThinningIsACopy()
    {
        Pix pix = Block(9, 9, 2, 2, 4, 4);
        Pix d = pix.SetStrokeWidth(1, 0, 4);
        Assert.NotSame(pix, d);
        Assert.True(Pix.Equal(pix, d));
    }

    [Fact]
    public void SetStrokeWidth_ThinFirstReducesASolidBlock()
    {
        Pix pix = Block(20, 20, 4, 4, 14, 14);
        Pix thin = pix.SetStrokeWidth(1, 1, 8);
        Assert.True(thin.CountPixels(null) < pix.CountPixels(null) / 4);
        Assert.Equal(1, thin.CountConnComp(8));
        Pix wide = pix.SetStrokeWidth(3, 1, 8);
        Assert.True(wide.CountPixels(null) > thin.CountPixels(null));
    }

    [Fact]
    public void SetStrokeWidth_InvalidArguments()
    {
        Pix pix = Block(9, 9, 2, 2, 4, 4);
        Assert.Throws<ArgumentException>(() => pix.SetStrokeWidth(0, 0, 8));
        Assert.Throws<ArgumentException>(() => pix.SetStrokeWidth(101, 0, 8));
        Assert.Throws<ArgumentException>(() => pix.SetStrokeWidth(3, 0, 6));
        Assert.Throws<ArgumentException>(() => Pix.Create(5, 5, 8).SetStrokeWidth(3, 0, 8));
    }

    [Fact]
    public void PixaSetStrokeWidth_AppliesToEachPix()
    {
        Pixa pixa = Pixa.Create(2);
        pixa.AddPix(Block(11, 11, 5, 5, 5, 5), L.L_COPY);
        pixa.AddPix(Block(9, 9, 4, 4, 4, 4), L.L_COPY);
        Pixa res = pixa.SetStrokeWidth(3, 0, 8);
        Assert.Equal(2, res.Count);
        Assert.Equal(9, res.GetPix(0, L.L_CLONE).CountPixels(null));
        Assert.Equal(9, res.GetPix(1, L.L_CLONE).CountPixels(null));
        Assert.Throws<ArgumentException>(() => pixa.SetStrokeWidth(0, 0, 8));
        Pixa gray = Pixa.Create(1);
        gray.AddPix(Pix.Create(5, 5, 8), L.L_COPY);
        Assert.Throws<ArgumentException>(() => gray.SetStrokeWidth(3, 0, 8));
        Assert.Throws<ArgumentException>(() => gray.ModifyStrokeWidth(2.0f));
        Assert.Throws<ArgumentException>(() => gray.FindStrokeWidth(0.1f, null, 0));
    }

    [Fact]
    public void FindStrokeWidth_Bar()
    {
        Pix bar = Block(30, 12, 2, 4, 27, 6);   /* 3 pixels thick */
        float w = bar.FindStrokeWidth(0.15f, null, out Numa histo);
        Assert.InRange(w, 2.0f, 4.0f);
        Assert.True(histo.Count > 1);
        Assert.InRange(bar.FindStrokeLength(null), 20, 40);
    }
}
