using System;
using System.IO;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests;

/// <summary>Tests for jbclass.c on synthetic pages.</summary>
public class JbClassTests
{
    /// <summary>A glyph of w x h with a notch so that it is not a plain rectangle.</summary>
    private static void DrawGlyph(Pix pix, int x, int y, int w, int h, int notch)
    {
        pix.Rasterop(x, y, w, h, L.PIX_SET, null, 0, 0);
        pix.Rasterop(x + w - notch, y, notch, notch, L.PIX_CLR, null, 0, 0);
    }

    /// <summary>Page with two instances of glyph A (10 x 16) and one of glyph B (14 x 20).</summary>
    private static Pix TestPage()
    {
        Pix pix = Pix.Create(200, 100, 1);
        DrawGlyph(pix, 20, 20, 10, 16, 4);
        DrawGlyph(pix, 60, 22, 10, 16, 4);
        DrawGlyph(pix, 100, 30, 14, 20, 6);
        return pix;
    }

    [Fact]
    public void CorrelationClassifierFindsTwoClasses()
    {
        JbClasser classer = JbClasser.CorrelationInit(L.JB_CONN_COMPS, 0, 0, 0.8f, 0.6f);
        classer.AddPage(TestPage());

        Assert.Equal(1, classer.NPages);
        Assert.Equal(2, classer.NClass);
        Assert.Equal(3, classer.NaClass.Count);
        Assert.Equal(new[] { 0, 0, 1 }, classer.NaClass.GetIArray());
        Assert.Equal(new[] { 0, 0, 0 }, classer.NaPage.GetIArray());
        Assert.Equal(3, classer.PtaUl.Count);
        Assert.Equal(new[] { 3 }, classer.NaComps.GetIArray());
        /* instances of each class are kept, with their boxes */
        Assert.Equal(2, classer.PixaA.GetPixa(0, L.L_CLONE).Count);
        Assert.Equal(1, classer.PixaA.GetPixa(1, L.L_CLONE).Count);
    }

    [Fact]
    public void RankHausClassifierFindsTwoClasses()
    {
        JbClasser classer = JbClasser.RankHausInit(L.JB_CONN_COMPS, 0, 0, 2, 0.97f);
        classer.AddPage(TestPage());
        Assert.Equal(2, classer.NClass);
        Assert.Equal(new[] { 0, 0, 1 }, classer.NaClass.GetIArray());

        /* strict hausdorff */
        classer = JbClasser.RankHausInit(L.JB_CONN_COMPS, 0, 0, 2, 1.0f);
        classer.AddPage(TestPage());
        Assert.Equal(2, classer.NClass);
        Assert.Equal(new[] { 0, 0, 1 }, classer.NaClass.GetIArray());
    }

    [Fact]
    public void ClassesAreKeptAcrossPagesAndRenderingReproducesIdenticalGlyphs()
    {
        Pix page = TestPage();
        JbClasser classer = JbClasser.CorrelationInit(L.JB_CONN_COMPS, 0, 0, 0.8f, 0.6f);
        classer.AddPage(page);
        classer.AddPage(page);
        classer.AddPage(Pix.Create(200, 100, 1));   /* a blank page counts as a page */
        Assert.Equal(3, classer.NPages);
        Assert.Equal(2, classer.NClass);
        Assert.Equal(6, classer.NaClass.Count);
        Assert.Equal(new[] { 0, 0, 1, 0, 0, 1 }, classer.NaClass.GetIArray());
        Assert.Equal(new[] { 0, 0, 0, 1, 1, 1 }, classer.NaPage.GetIArray());

        JbData data = classer.DataSave();
        Assert.Equal(3, data.NPages);
        Assert.Equal(2, data.NClass);
        Pixa pages = data.Render(false);
        Assert.Equal(3, pages.Count);
        /* the instances are exact copies of the templates, so the rendering is exact */
        Assert.True(Pix.Equal(page, pages.GetPix(0, L.L_CLONE)));
        Assert.True(Pix.Equal(page, pages.GetPix(1, L.L_CLONE)));
        Assert.True(pages.GetPix(2, L.L_CLONE).Zero());

        Pixa dbg = data.Render(true);
        Assert.Equal(2, dbg.GetPix(0, L.L_CLONE).Depth);
    }

    [Fact]
    public void DataIsWrittenAndReadBack()
    {
        JbClasser classer = JbClasser.CorrelationInit(L.JB_CONN_COMPS, 0, 0, 0.8f, 0.6f);
        classer.AddPage(TestPage());
        JbData data = classer.DataSave();

        string root = Path.Combine(Path.GetTempPath(), "jbdata_" + Guid.NewGuid().ToString("N"));
        try {
            data.Write(root);
            string text = File.ReadAllText(root + L.JB_DATA_EXT);
            Assert.StartsWith("jb data file\nnum pages = 1\npage size: w = 200, h = 100\nnum components = 3\n" +
                              "num classes = 2\ntemplate lattice size: w = ", text);

            JbData data2 = JbData.Read(root);
            Assert.Equal((data.NPages, data.W, data.H, data.NClass, data.LatticeW, data.LatticeH),
                         (data2.NPages, data2.W, data2.H, data2.NClass, data2.LatticeW, data2.LatticeH));
            Assert.Equal(data.NaClass.GetIArray(), data2.NaClass.GetIArray());
            Assert.Equal(data.NaPage.GetIArray(), data2.NaPage.GetIArray());
            for (int i = 0; i < data.PtaUl.Count; i++) {
                data.PtaUl.GetIPt(i, out int x1, out int y1);
                data2.PtaUl.GetIPt(i, out int x2, out int y2);
                Assert.Equal((x1, y1), (x2, y2));
            }
            Assert.True(Pix.Equal(data.Pix, data2.Pix));
            Assert.True(Pix.Equal(TestPage(), data2.Render(false).GetPix(0, L.L_CLONE)));
        } finally {
            File.Delete(root + L.JB_DATA_EXT);
            File.Delete(root + L.JB_TEMPLATE_EXT);
        }
    }

    [Fact]
    public void ULCornersAreAdjustedToTheTemplateAndLLCornersFollow()
    {
        JbClasser classer = JbClasser.CorrelationInit(L.JB_CONN_COMPS, 0, 0, 0.8f, 0.6f);
        classer.AddPage(TestPage());
        /* identical instances: the UL corner is the box corner */
        classer.PtaUl.GetIPt(0, out int x, out int y);
        Assert.Equal((20, 20), (x, y));
        classer.PtaUl.GetIPt(1, out x, out y);
        Assert.Equal((60, 22), (x, y));

        classer.GetLLCorners();
        Assert.NotNull(classer.PtaLl);
        classer.PtaLl!.GetIPt(0, out x, out y);
        Assert.Equal((20, 20 + 16 - 1), (x, y));   /* y + h - 1 for the unbordered template */
        classer.PtaLl.GetIPt(2, out x, out y);
        Assert.Equal((100, 30 + 20 - 1), (x, y));
    }

    [Fact]
    public void HaustestAcceptsTheSameShapeAndRejectsDifferentOnes()
    {
        Sel sel = Sel.CreateBrick(2, 2, 1, 1, L.SEL_HIT);
        Pix a = Pix.Create(10, 16, 1);
        DrawGlyph(a, 0, 0, 10, 16, 4);
        Pix b = Pix.Create(10, 16, 1);
        b.Rasterop(0, 0, 10, 16, L.PIX_SET, null, 0, 0);
        Pix ad = Pix.Dilate(null, a, sel);
        Pix bd = Pix.Dilate(null, b, sel);
        Assert.True(a.Haustest(ad, a, ad, 0.0f, 0.0f, 2, 2));
        /* b has the notch filled: a is covered by dilated b, but b is not covered by dilated a */
        Assert.False(a.Haustest(ad, b, bd, 0.0f, 0.0f, 2, 2) && b.Haustest(bd, a, ad, 0.0f, 0.0f, 2, 2));
        /* too different in size */
        Pix big = Pix.Create(20, 16, 1);
        Assert.False(a.Haustest(ad, big, big, 0.0f, 0.0f, 2, 2));

        int[] tab = Pix.MakePixelSumTab8();
        int area = a.CountPixels(tab);
        Assert.True(a.RankHaustest(ad, a, ad, 0.0f, 0.0f, 2, 2, area, area, 0.97f, tab));
        Assert.False(a.RankHaustest(ad, big, big, 0.0f, 0.0f, 2, 2, area, 1, 0.97f, tab));
    }

    [Fact]
    public void ComponentsAreExtractedByType()
    {
        /* an 'i' made of a stem and a dot, and a wide pair of letters one next to the other */
        Pix pix = Pix.Create(200, 60, 1);
        pix.Rasterop(10, 20, 4, 14, L.PIX_SET, null, 0, 0);   /* stem */
        pix.Rasterop(10, 12, 4, 4, L.PIX_SET, null, 0, 0);    /* dot, 4 lines above the stem */
        pix.Rasterop(40, 20, 8, 14, L.PIX_SET, null, 0, 0);
        pix.Rasterop(52, 20, 8, 14, L.PIX_SET, null, 0, 0);
        pix.SetResolution(150, 150);

        JbClasser.GetComponents(pix, L.JB_CONN_COMPS, 350, 120, out Boxa boxa, out Pixa pixa);
        Assert.Equal(4, boxa.Count);
        Assert.Equal(4, pixa.Count);

        JbClasser.GetComponents(pix, L.JB_CHARACTERS, 350, 120, out boxa, out pixa);
        Assert.Equal(3, boxa.Count);   /* the dot is joined to the stem by the vertical closing */

        JbClasser.GetComponents(pix, L.JB_WORDS, 1000, 120, out boxa, out pixa);
        /* with so few components the count of c.c. never drops to 30%, so the minimal
         * dilation is used (a 4 x 1 closing), which does not join letters 4 pixels apart */
        Assert.Equal(4, boxa.Count);

        /* components that are too large are dropped */
        JbClasser.GetComponents(pix, L.JB_CONN_COMPS, 6, 120, out boxa, out pixa);
        Assert.Equal(2, boxa.Count);   /* only the stem and the dot are not wider than 6 */
        Assert.Equal(2, pixa.Count);

        JbClasser.GetComponents(Pix.Create(50, 50, 1), L.JB_CONN_COMPS, 350, 120, out boxa, out pixa);
        Assert.Equal(0, boxa.Count);
    }

    [Fact]
    public void WordMaskJoinsTheCharactersOfAWord()
    {
        /* two lines of three words, each word has five letters of 8 x 12 pixels, 3 pixels apart */
        Pix pix = Pix.Create(500, 100, 1);
        pix.SetResolution(150, 150);
        int nwords = 0;
        for (int line = 0; line < 2; line++)
            for (int word = 0; word < 3; word++, nwords++)
                for (int ch = 0; ch < 5; ch++)
                    pix.Rasterop(20 + word * 150 + ch * 11, 15 + line * 40, 8, 12, L.PIX_SET, null, 0, 0);

        pix.WordMaskByDilation(out Pix mask, out int size, null);
        Assert.InRange(size, 3, 13);
        Assert.Equal(nwords, mask.CountConnComp(8));

        pix.WordBoxesByDilation(5, 5, 200, 100, out Boxa boxa, out int size2, null);
        Assert.Equal(size, size2);
        Assert.Equal(nwords, boxa.Count);
        boxa.GetBoxGeometry(0, out int x, out int y, out int w, out int h);
        Assert.Equal((20, 15), (x, y));
        Assert.True(w >= 5 * 8 + 4 * 3 && w < 150);

        /* too small and too large boxes are removed */
        pix.WordBoxesByDilation(5, 5, 20, 100, out boxa, out _, null);
        Assert.Equal(0, boxa.Count);

        Assert.Throws<ArgumentException>(() => Pix.Create(10, 10, 8).WordMaskByDilation(out _, out _, null));
    }

    [Fact]
    public void CompositesAreAccumulatedAndConvertedToTemplates()
    {
        JbClasser classer = JbClasser.CorrelationInit(L.JB_CONN_COMPS, 0, 0, 0.8f, 0.6f);
        classer.AddPage(TestPage());
        Pixa composites = JbClasser.AccumulateComposites(classer.PixaA, out Numa na, out Pta ptat);
        Assert.Equal(2, composites.Count);
        Assert.Equal(new[] { 2, 1 }, na.GetIArray());
        Assert.Equal(2, ptat.Count);

        Pixa templates = JbClasser.TemplatesFromComposites(composites, na);
        Assert.Equal(2, templates.Count);
        Pix t0 = templates.GetPix(0, L.L_CLONE);
        Assert.Equal(8, t0.Depth);
        /* two identical instances: pixels are 255 where the glyph is ON and 0 elsewhere */
        t0.GetPixel(2, 2, out uint on);
        t0.GetPixel(9, 0, out uint off);
        Assert.Equal((255u, 0u), (on, off));
    }

    [Fact]
    public void ArgumentChecks()
    {
        Assert.Throws<ArgumentException>(() => JbClasser.CorrelationInit(7, 0, 0, 0.8f, 0.6f));
        Assert.Throws<ArgumentException>(() => JbClasser.CorrelationInit(L.JB_CONN_COMPS, 0, 0, 0.2f, 0.6f));
        Assert.Throws<ArgumentException>(() => JbClasser.CorrelationInit(L.JB_CONN_COMPS, 0, 0, 0.8f, 1.5f));
        Assert.Throws<ArgumentException>(() => JbClasser.RankHausInit(L.JB_CONN_COMPS, 0, 0, 0, 0.97f));
        Assert.Throws<ArgumentException>(() => JbClasser.RankHausInit(L.JB_CONN_COMPS, 0, 0, 2, 0.3f));
        Assert.Throws<ArgumentException>(() => JbClasser.Create(5, L.JB_CONN_COMPS));
        JbClasser classer = JbClasser.CorrelationInit(L.JB_CONN_COMPS, 0, 0, 0.8f, 0.6f);
        Assert.Throws<ArgumentException>(() => classer.AddPage(Pix.Create(10, 10, 8)));
        Assert.Throws<ArgumentException>(() => JbClasser.GetComponents(Pix.Create(10, 10, 1), 9, 10, 10, out _, out _));
    }
}
