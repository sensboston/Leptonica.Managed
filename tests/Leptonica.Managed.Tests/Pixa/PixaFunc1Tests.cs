using System;
using Leptonica;

namespace Leptonica.Managed.Tests;

public class PixaFunc1Tests
{
    private static Pixa MakeSized(params (int w, int h)[] sizes)
    {
        Pixa pixa = Pixa.Create(0);
        int x = 0;
        foreach (var (w, h) in sizes) {
            Pix pix = Pix.Create(w, h, 1);
            pix.SetAll();
            pixa.AddPix(pix, L.L_INSERT);
            pixa.AddBox(Box.Create(x, 0, w, h), L.L_INSERT);
            x += w + 1;
        }
        return pixa;
    }

    private static int[] Ints(Numa na)
    {
        int[] v = new int[na.Count];
        for (int i = 0; i < v.Length; i++) v[i] = na.GetIValue(i);
        return v;
    }

    /*------------------------------ selection -------------------------------*/

    [Fact]
    public void MakeSizeIndicator_AllTypes()
    {
        Pixa pixa = MakeSized((5, 10), (20, 3), (8, 8));
        Assert.Equal(new[] { 1, 0, 1 }, Ints(pixa.MakeSizeIndicator(10, 0, L.L_SELECT_WIDTH, L.L_SELECT_IF_LT)));
        Assert.Equal(new[] { 1, 0, 0 }, Ints(pixa.MakeSizeIndicator(0, 9, L.L_SELECT_HEIGHT, L.L_SELECT_IF_GT)));
        Assert.Equal(new[] { 1, 1, 1 }, Ints(pixa.MakeSizeIndicator(8, 8, L.L_SELECT_IF_EITHER, L.L_SELECT_IF_LTE)));
        Assert.Equal(new[] { 0, 0, 1 }, Ints(pixa.MakeSizeIndicator(8, 8, L.L_SELECT_IF_BOTH, L.L_SELECT_IF_GTE)));
        Assert.Throws<ArgumentException>(() => pixa.MakeSizeIndicator(1, 1, 99, L.L_SELECT_IF_LT));
        Assert.Throws<ArgumentException>(() => pixa.MakeSizeIndicator(1, 1, L.L_SELECT_WIDTH, 99));
    }

    [Fact]
    public void SelectWithIndicator_ClonesAndChanged()
    {
        Pixa pixa = MakeSized((5, 10), (20, 3), (8, 8));
        Numa na = Numa.Create(3);
        na.AddNumber(1); na.AddNumber(0); na.AddNumber(1);
        Pixa sel = pixa.SelectWithIndicator(na, out bool changed);
        Assert.True(changed);
        Assert.Equal(2, sel.Count);
        Assert.Equal(2, sel.BoxaCount);
        Assert.Same(pixa.GetPix(2, L.L_CLONE), sel.GetPix(1, L.L_CLONE));

        Numa all = Numa.MakeConstant(1, 3);
        Pixa same = pixa.SelectWithIndicator(all, out changed);
        Assert.False(changed);
        Assert.Same(pixa, same);
    }

    [Fact]
    public void SelectBySize_AndByRatios()
    {
        Pixa pixa = MakeSized((5, 10), (20, 3), (8, 8));
        Pixa sel = pixa.SelectBySize(10, 0, L.L_SELECT_WIDTH, L.L_SELECT_IF_LT, out bool changed);
        Assert.True(changed);
        Assert.Equal(2, sel.Count);

        sel = pixa.SelectByWidthHeightRatio(1.0f, L.L_SELECT_IF_LT, out changed);
        Assert.True(changed);
        Assert.Equal(1, sel.Count);  // only 5 x 10
    }

    [Fact]
    public void SelectByArea_ThresholdSplitsAreas()
    {
        Pixa pixa = MakeSized((5, 10), (20, 3), (8, 8));
        Pixa sel = pixa.SelectByArea(55.0f, L.L_SELECT_IF_GT, out bool changed);
        Assert.True(changed);
        Assert.Equal(2, sel.Count);
        sel = pixa.SelectByArea(1000.0f, L.L_SELECT_IF_LT, out changed);
        Assert.False(changed);
        Assert.Throws<ArgumentException>(() => pixa.SelectByArea(1.0f, 99, out _));
    }

    [Fact]
    public void SelectWithString_CopiesAndReportsErrors()
    {
        Pixa pixa = MakeSized((5, 10), (20, 3), (8, 8));
        Pixa sel = pixa.SelectWithString("2, 0", out bool error);
        Assert.False(error);
        Assert.Equal(2, sel.Count);
        Assert.Equal(8, sel.GetPix(0, L.L_CLONE).Width);
        Assert.NotSame(pixa.GetPix(2, L.L_CLONE), sel.GetPix(0, L.L_CLONE));
        Assert.Equal(2, sel.BoxaCount);

        sel = pixa.SelectWithString("1, 7", out error);
        Assert.True(error);
        Assert.Equal(1, sel.Count);
    }

    [Fact]
    public void Pix_SelectBySize_RendersKeptComponents()
    {
        Pix pix = Pix.Create(40, 20, 1);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++) pix.SetPixel(2 + x, 2 + y, 1);      // 4 x 4
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 12; x++) pix.SetPixel(20 + x, 5 + y, 1);    // 12 x 10

        Pix small = pix.SelectBySize(8, 8, 8, L.L_SELECT_IF_BOTH, L.L_SELECT_IF_LT, out bool changed);
        Assert.True(changed);
        Assert.Equal(1u, small.GetPixel(3, 3));
        Assert.Equal(0u, small.GetPixel(25, 8));
        Assert.Equal(40, small.Width);

        Pix none = pix.SelectBySize(100, 100, 8, L.L_SELECT_IF_BOTH, L.L_SELECT_IF_GT, out changed);
        Assert.True(changed);
        Assert.True(none.Zero());

        Pix all = pix.SelectBySize(100, 100, 8, L.L_SELECT_IF_BOTH, L.L_SELECT_IF_LT, out changed);
        Assert.False(changed);
        Assert.Equal(pix.GetPixel(25, 8), all.GetPixel(25, 8));
        Assert.NotSame(pix, all);

        Assert.Throws<ArgumentException>(() => pix.SelectBySize(1, 1, 6, L.L_SELECT_WIDTH, L.L_SELECT_IF_LT, out _));
        Pix big = pix.SelectByArea(50.0f, 8, L.L_SELECT_IF_GT, out changed);
        Assert.True(changed);
        Assert.Equal(0u, big.GetPixel(3, 3));
        Assert.Equal(1u, big.GetPixel(25, 8));
    }

    [Fact]
    public void RemoveAndAddWithIndicator()
    {
        Pix pix = Pix.Create(40, 20, 1);
        pix.ConnComp(out Pixa pixa, 8);
        Assert.Equal(0, pixa.Count);

        Pix src = Pix.Create(30, 10, 1);
        src.SetPixel(1, 1, 1);
        src.SetPixel(20, 5, 1);
        src.ConnComp(out pixa, 8);
        Assert.Equal(2, pixa.Count);
        Numa na = Numa.Create(2);
        na.AddNumber(1); na.AddNumber(0);
        Pix work = Pix.Copy(null, src);
        work.RemoveWithIndicator(pixa, na);
        Assert.Equal(0u, work.GetPixel(1, 1));
        Assert.Equal(1u, work.GetPixel(20, 5));

        Pix empty = Pix.Create(30, 10, 1);
        empty.AddWithIndicator(pixa, na);
        Assert.Equal(1u, empty.GetPixel(1, 1));
        Assert.Equal(0u, empty.GetPixel(20, 5));
        Assert.Throws<ArgumentException>(() => empty.AddWithIndicator(pixa, Numa.Create(1)));
    }

    [Fact]
    public void RenderComponent_IntoNewOrGivenPix()
    {
        Pix src = Pix.Create(30, 10, 1);
        src.SetPixel(1, 1, 1);
        src.SetPixel(20, 5, 1);
        src.ConnComp(out Pixa pixa, 8);
        Pix pix = Pixa.RenderComponent(null, pixa, 1);
        Assert.Equal((21, 6), (pix.Width, pix.Height));
        Assert.Equal(1u, pix.GetPixel(20, 5));
        Assert.Equal(0u, pix.GetPixel(1, 1));
        Pix more = Pixa.RenderComponent(pix, pixa, 0);
        Assert.Same(pix, more);
        Assert.Equal(1u, pix.GetPixel(1, 1));
        Assert.Throws<ArgumentException>(() => Pixa.RenderComponent(null, pixa, 2));
        Assert.Throws<ArgumentException>(() => Pixa.RenderComponent(Pix.Create(5, 5, 8), pixa, 0));
    }

    [Fact]
    public void SelectByNumConnComp()
    {
        Pixa pixa = Pixa.Create(0);
        Pix one = Pix.Create(10, 10, 1);
        one.SetPixel(1, 1, 1);
        Pix two = Pix.Create(10, 10, 1);
        two.SetPixel(1, 1, 1);
        two.SetPixel(7, 7, 1);
        pixa.AddPix(one, L.L_INSERT);
        pixa.AddPix(two, L.L_INSERT);
        Pixa sel = pixa.SelectByNumConnComp(2, 5, 8, out bool changed);
        Assert.True(changed);
        Assert.Equal(1, sel.Count);
        Assert.Same(two, sel.GetPix(0, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => pixa.SelectByNumConnComp(5, 2, 8, out _));
        Assert.Throws<ArgumentException>(() => pixa.SelectByNumConnComp(1, 2, 6, out _));
    }

    /*-------------------------------- sorting -------------------------------*/

    [Fact]
    public void Sort_ByTypes()
    {
        Pixa pixa = MakeSized((5, 10), (20, 3), (8, 8));
        Pixa s = pixa.Sort(L.L_SORT_BY_WIDTH, L.L_SORT_INCREASING, out Numa? idx, L.L_CLONE);
        Assert.Equal(new[] { 0, 2, 1 }, Ints(idx!));
        Assert.Equal(20, s.GetPix(2, L.L_CLONE).Width);
        Assert.Same(pixa.GetPix(1, L.L_CLONE), s.GetPix(2, L.L_CLONE));
        Assert.Equal(3, s.BoxaCount);

        s = pixa.Sort(L.L_SORT_BY_HEIGHT, L.L_SORT_DECREASING, out idx, L.L_COPY);
        Assert.Equal(new[] { 0, 2, 1 }, Ints(idx!));
        Assert.NotSame(pixa.GetPix(0, L.L_CLONE), s.GetPix(0, L.L_CLONE));

        s = pixa.Sort(L.L_SORT_BY_AREA, L.L_SORT_INCREASING, out idx, L.L_CLONE);
        Assert.Equal(new[] { 0, 1, 2 }, Ints(idx!));  // 50, 60, 64
        s = pixa.Sort(L.L_SORT_BY_ASPECT_RATIO, L.L_SORT_INCREASING, out idx, L.L_CLONE);
        Assert.Equal(new[] { 0, 2, 1 }, Ints(idx!));
        s = pixa.Sort(L.L_SORT_BY_X, L.L_SORT_DECREASING, out idx, L.L_CLONE);
        Assert.Equal(new[] { 2, 1, 0 }, Ints(idx!));
        s = pixa.Sort(L.L_SORT_BY_MIN_DIMENSION, L.L_SORT_INCREASING, out idx, L.L_CLONE);
        Assert.Equal(new[] { 1, 0, 2 }, Ints(idx!));
        s = pixa.Sort(L.L_SORT_BY_MAX_DIMENSION, L.L_SORT_INCREASING, out idx, L.L_CLONE);
        Assert.Equal(new[] { 2, 0, 1 }, Ints(idx!));
        s = pixa.Sort(L.L_SORT_BY_PERIMETER, L.L_SORT_INCREASING, out idx, L.L_CLONE);
        Assert.Equal(new[] { 0, 2, 1 }, Ints(idx!)); // 15, 16, 23
    }

    [Fact]
    public void Sort_BuildsBoxaIfEmpty_AndErrors()
    {
        Pixa pixa = Pixa.Create(0);
        pixa.AddPix(Pix.Create(9, 2, 8), L.L_INSERT);
        pixa.AddPix(Pix.Create(3, 2, 8), L.L_INSERT);
        Pixa s = pixa.Sort(L.L_SORT_BY_WIDTH, L.L_SORT_INCREASING, out Numa? idx, L.L_CLONE);
        Assert.Equal(new[] { 1, 0 }, Ints(idx!));
        Assert.Equal(2, pixa.BoxaCount);  // the full size boxa was set

        Assert.Equal(0, Pixa.Create(0).Sort(L.L_SORT_BY_X, L.L_SORT_INCREASING, out _, L.L_CLONE).Count);
        Assert.Throws<ArgumentException>(() => pixa.Sort(99, L.L_SORT_INCREASING, out _, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => pixa.Sort(L.L_SORT_BY_X, 9, out _, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => pixa.Sort(L.L_SORT_BY_X, L.L_SORT_INCREASING, out _, 9));
        Pixa mismatch = MakeSized((5, 5), (6, 6));
        mismatch.AddPix(Pix.Create(2, 2, 1), L.L_INSERT);
        Assert.Throws<ArgumentException>(() => mismatch.Sort(L.L_SORT_BY_X, L.L_SORT_INCREASING, out _, L.L_CLONE));
    }

    [Fact]
    public void Sort_LargeUsesBinSortWithSameOrderAsShellSort()
    {
        Pixa pixa = Pixa.Create(0);
        for (int i = 0; i < 250; i++) {
            int w = 1 + (i * 37) % 53;
            pixa.AddPix(Pix.Create(w, 2, 1), L.L_INSERT);
            pixa.AddBox(Box.Create(i, 0, w, 2), L.L_INSERT);
        }
        Pixa s = pixa.Sort(L.L_SORT_BY_WIDTH, L.L_SORT_INCREASING, out Numa? idx, L.L_CLONE);
        Assert.Equal(250, s.Count);
        for (int i = 1; i < 250; i++)
            Assert.True(s.GetPix(i - 1, L.L_CLONE).Width <= s.GetPix(i, L.L_CLONE).Width);
        Pixa b = pixa.BinSort(L.L_SORT_BY_WIDTH, L.L_SORT_DECREASING, out idx, L.L_CLONE);
        for (int i = 1; i < 250; i++)
            Assert.True(b.GetPix(i - 1, L.L_CLONE).Width >= b.GetPix(i, L.L_CLONE).Width);
        Assert.Throws<ArgumentException>(() => pixa.BinSort(L.L_SORT_BY_AREA, L.L_SORT_INCREASING, out _, L.L_CLONE));
    }

    [Fact]
    public void SortByIndex_And_Sort2dByIndex()
    {
        Pixa pixa = MakeSized((5, 10), (20, 3), (8, 8));
        Numa idx = Numa.Create(3);
        idx.AddNumber(2); idx.AddNumber(0); idx.AddNumber(1);
        Pixa s = pixa.SortByIndex(idx, L.L_COPY);
        Assert.Equal(new[] { 8, 5, 20 }, new[] { s.GetPix(0, L.L_CLONE).Width, s.GetPix(1, L.L_CLONE).Width, s.GetPix(2, L.L_CLONE).Width });
        Assert.Throws<ArgumentException>(() => pixa.SortByIndex(idx, 9));

        Numaa naa = Numaa.Create(2);
        Numa a = Numa.Create(2); a.AddNumber(2); a.AddNumber(0);
        Numa b = Numa.Create(1); b.AddNumber(1);
        naa.AddNuma(a, L.L_INSERT);
        naa.AddNuma(b, L.L_INSERT);
        Pixaa paa = pixa.Sort2dByIndex(naa, L.L_CLONE);
        Assert.Equal(2, paa.Count);
        Assert.Equal(2, paa.GetPixa(0, L.L_CLONE).Count);
        Assert.Same(pixa.GetPix(1, L.L_CLONE), paa.GetPix(1, 0, L.L_CLONE));

        Numaa bad = Numaa.Create(1);
        bad.AddNuma(a, L.L_INSERT);
        Assert.Throws<ArgumentException>(() => pixa.Sort2dByIndex(bad, L.L_CLONE));
    }

    [Fact]
    public void SelectRange_Pixa_And_Pixaa()
    {
        Pixa pixa = MakeSized((1, 1), (2, 1), (3, 1), (4, 1));
        Pixa r = pixa.SelectRange(1, 2, L.L_CLONE);
        Assert.Equal(2, r.Count);
        Assert.Same(pixa.GetPix(1, L.L_CLONE), r.GetPix(0, L.L_CLONE));
        Assert.Equal(4, pixa.SelectRange(-3, -1, L.L_COPY).Count);
        Assert.Equal(2, pixa.SelectRange(2, 99, L.L_CLONE).Count);
        Assert.Throws<ArgumentException>(() => pixa.SelectRange(4, -1, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => pixa.SelectRange(3, 1, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => pixa.SelectRange(0, 1, 9));

        Pixaa paa = Pixaa.CreateFromPixa(pixa, 1, L.L_CHOOSE_CONSECUTIVE, L.L_CLONE);
        Assert.Equal(2, paa.SelectRange(1, 2, L.L_CLONE).Count);
        Assert.Equal(4, paa.SelectRange(0, -1, L.L_COPY).Count);
        Assert.Throws<ArgumentException>(() => paa.SelectRange(9, -1, L.L_CLONE));
    }

    /*------------------------ scaling, rotation, borders --------------------*/

    [Fact]
    public void ScaleToSize_And_Scale()
    {
        Pixa pixa = Pixa.Create(0);
        for (int i = 0; i < 2; i++) {
            Pix pix = Pix.Create(20, 10, 8);
            pix.SetAllArbitrary(100);
            pix.Text = "t" + i;
            pixa.AddPix(pix, L.L_INSERT);
            pixa.AddBox(Box.Create(10 * i, 0, 20, 10), L.L_INSERT);
        }
        Assert.Same(pixa, pixa.ScaleToSize(0, 0));
        Pixa s = pixa.ScaleToSize(10, 0);
        Assert.Equal((10, 5), (s.GetPix(0, L.L_CLONE).Width, s.GetPix(0, L.L_CLONE).Height));
        Assert.Equal("t1", s.GetPix(1, L.L_CLONE).Text);

        Pixa sc = pixa.Scale(2.0f, 3.0f);
        Assert.Equal((40, 30), (sc.GetPix(1, L.L_CLONE).Width, sc.GetPix(1, L.L_CLONE).Height));
        sc.GetBoxGeometry(1, out int x, out int y, out int w, out int h);
        Assert.Equal((20, 0, 40, 30), (x, y, w, h));
        Assert.Throws<ArgumentException>(() => pixa.Scale(0f, 1f));

        Pixa sm = pixa.ScaleBySampling(0.5f, 0.5f);
        Assert.Equal((10, 5), (sm.GetPix(0, L.L_CLONE).Width, sm.GetPix(0, L.L_CLONE).Height));
        Assert.Equal(2, sm.BoxaCount);

        Pixa rel = pixa.ScaleToSizeRel(4, -2);
        Assert.Equal((24, 8), (rel.GetPix(0, L.L_CLONE).Width, rel.GetPix(0, L.L_CLONE).Height));
    }

    [Fact]
    public void Pixaa_ScaleToSize_And_Var()
    {
        Pixaa paa = Pixaa.Create(0);
        paa.AddPixa(MakeSized((20, 10)), L.L_INSERT);
        paa.AddPixa(MakeSized((20, 10), (30, 10)), L.L_INSERT);
        Pixaa s = paa.ScaleToSize(10, 0);
        Assert.Equal(10, s.GetPix(1, 1, L.L_CLONE).Width);
        Assert.Throws<ArgumentException>(() => paa.ScaleToSize(0, 0));

        Numa wd = Numa.Create(2); wd.AddNumber(5); wd.AddNumber(40);
        Pixaa v = paa.ScaleToSizeVar(wd, null);
        Assert.Equal(5, v.GetPix(0, 0, L.L_CLONE).Width);
        Assert.Equal(40, v.GetPix(1, 0, L.L_CLONE).Width);
        Assert.Throws<ArgumentException>(() => paa.ScaleToSizeVar(null, null));
        Assert.Throws<ArgumentException>(() => paa.ScaleToSizeVar(Numa.Create(1), null));
    }

    [Fact]
    public void RotateOrth_Translate_Rotate()
    {
        Pixa pixa = Pixa.Create(0);
        Pix pix = Pix.Create(20, 10, 8);
        pix.SetAllArbitrary(100);
        pixa.AddPix(pix, L.L_INSERT);
        pixa.AddBox(Box.Create(2, 1, 6, 4), L.L_INSERT);

        Pixa r = pixa.RotateOrth(1);
        Assert.Equal((10, 20), (r.GetPix(0, L.L_CLONE).Width, r.GetPix(0, L.L_CLONE).Height));
        r.GetBoxGeometry(0, out int rx, out int ry, out int w, out int h);
        Assert.Equal((5, 2, 4, 6), (rx, ry, w, h));
        Assert.NotSame(pixa, pixa.RotateOrth(0));
        Assert.Throws<ArgumentException>(() => pixa.RotateOrth(4));

        Pixa t = pixa.Translate(3, 4, L.L_BRING_IN_WHITE);
        t.GetBoxGeometry(0, out int x, out int y, out _, out _);
        Assert.Equal((5, 5), (x, y));
        Assert.Equal(255u, t.GetPix(0, L.L_CLONE).GetPixel(0, 0));
        Assert.Equal(100u, t.GetPix(0, L.L_CLONE).GetPixel(5, 5));
        Assert.NotSame(pixa, pixa.Translate(0, 0, L.L_BRING_IN_WHITE));

        Assert.Equal(1, pixa.Rotate(0.0001f, L.L_ROTATE_SAMPLING, L.L_BRING_IN_WHITE, 0, 0).Count);
        Assert.Equal(1, pixa.Rotate(0.2f, L.L_ROTATE_SAMPLING, L.L_BRING_IN_WHITE, 0, 0).Count);
        Assert.Throws<ArgumentException>(() => pixa.Rotate(0.2f, 9, L.L_BRING_IN_WHITE, 0, 0));
        Assert.Throws<ArgumentException>(() => pixa.Rotate(0.2f, L.L_ROTATE_SAMPLING, 9, 0, 0));
    }

    private static Pixa BorderSource()
    {
        Pixa pixa = Pixa.Create(0);
        for (int i = 0; i < 2; i++) {
            pixa.AddPix(Pix.Create(10, 6, 8), L.L_INSERT);
            pixa.AddBox(Box.Create(20 * i, 5, 10, 6), L.L_INSERT);
        }
        return pixa;
    }

    [Fact]
    public void AddBorderGeneral_NewAndInPlace()
    {
        Pixa src = BorderSource();
        Pixa n = Pixa.AddBorderGeneral(null, src, 2, 3, 4, 5, 77);
        Assert.NotSame(src, n);
        Assert.Equal((15, 15), (n.GetPix(0, L.L_CLONE).Width, n.GetPix(0, L.L_CLONE).Height));
        Assert.Equal(77u, n.GetPix(0, L.L_CLONE).GetPixel(0, 0));
        n.GetBoxGeometry(1, out int x, out int y, out int w, out int h);
        Assert.Equal((18, 1, 15, 15), (x, y, w, h));
        Assert.Equal(10, src.GetPix(0, L.L_CLONE).Width);

        Pixa same = Pixa.AddBorderGeneral(src, src, 1, 1, 1, 1, 0);
        Assert.Same(src, same);
        Assert.Equal(12, src.GetPix(1, L.L_CLONE).Width);
        Assert.Throws<ArgumentException>(() => Pixa.AddBorderGeneral(null, src, -1, 0, 0, 0, 0));
        Assert.Throws<ArgumentException>(() => Pixa.AddBorderGeneral(BorderSource(), src, 1, 1, 1, 1, 0));
    }

    [Fact]
    public void ClipToPix_And_ClipToForeground()
    {
        Pix pixs = Pix.Create(30, 10, 1);
        pixs.SetAll();
        Pixa pixa = Pixa.Create(0);
        Pix mask = Pix.Create(5, 5, 1);
        mask.SetPixel(1, 1, 1);
        pixa.AddPix(mask, L.L_INSERT);
        pixa.AddBox(Box.Create(10, 2, 5, 5), L.L_INSERT);
        Pixa c = pixa.ClipToPix(pixs);
        Assert.Equal(1, c.Count);
        Assert.Equal(1u, c.GetPix(0, L.L_CLONE).GetPixel(1, 1));
        Assert.Equal(0u, c.GetPix(0, L.L_CLONE).GetPixel(0, 0));
        Assert.Equal(1, c.BoxaCount);

        Pix m2 = Pix.Create(8, 8, 1);
        m2.SetPixel(2, 3, 1);
        m2.SetPixel(4, 5, 1);
        Pixa p2 = Pixa.Create(0);
        p2.AddPix(m2, L.L_INSERT);
        p2.ClipToForeground(out Pixa clipped, out Boxa boxa);
        Assert.Equal((3, 3), (clipped.GetPix(0, L.L_CLONE).Width, clipped.GetPix(0, L.L_CLONE).Height));
        boxa.GetBoxGeometry(0, out int x, out int y, out _, out _);
        Assert.Equal((2, 3), (x, y));
    }

    [Fact]
    public void FlattenToPixa_And_PixaaSizeRange()
    {
        Pixaa paa = Pixaa.Create(0);
        paa.AddPixa(MakeSized((5, 4), (6, 9)), L.L_INSERT);
        Pixa noboxes = Pixa.Create(0);
        noboxes.AddPix(Pix.Create(12, 2, 1), L.L_INSERT);
        paa.AddPixa(noboxes, L.L_INSERT);
        Pixa flat = paa.FlattenToPixa(out Numa idx, L.L_CLONE);
        Assert.Equal(3, flat.Count);
        Assert.Equal(2, flat.BoxaCount);
        Assert.Equal(new[] { 0, 0, 1 }, Ints(idx));
        Assert.Throws<ArgumentException>(() => paa.FlattenToPixa(out _, 7));

        paa.SizeRange(out int minw, out int minh, out int maxw, out int maxh);
        Assert.Equal((5, 2, 12, 9), (minw, minh, maxw, maxh));
    }

    /*----------------------- size, depth, conversion, equality --------------*/

    [Fact]
    public void SizeRange_DepthInfo_Color_Cmaps()
    {
        Pixa pixa = Pixa.Create(0);
        pixa.AddPix(Pix.Create(10, 20, 1), L.L_INSERT);
        pixa.AddPix(Pix.Create(5, 30, 8), L.L_INSERT);
        pixa.SizeRange(out int minw, out int minh, out int maxw, out int maxh);
        Assert.Equal((5, 20, 10, 30), (minw, minh, maxw, maxh));
        pixa.GetDepthInfo(out int maxd, out bool same);
        Assert.Equal(8, maxd);
        Assert.False(same);
        Assert.False(pixa.HasColor());
        Assert.False(pixa.AnyColormaps());
        Assert.Equal(8, pixa.GetRenderingDepth());

        Pixa bin = Pixa.Create(0);
        bin.AddPix(Pix.Create(3, 3, 1), L.L_INSERT);
        Assert.Equal(1, bin.GetRenderingDepth());

        pixa.AddPix(Pix.CreateWithCmap(4, 4, 4, L.L_SET_WHITE), L.L_INSERT);
        Assert.True(pixa.AnyColormaps());
        Assert.False(pixa.HasColor());  // gray colormap
        pixa.AddPix(Pix.Create(2, 2, 32), L.L_INSERT);
        Assert.True(pixa.HasColor());
        Assert.Equal(32, pixa.GetRenderingDepth());

        Assert.Throws<ArgumentException>(() => Pixa.Create(0).GetDepthInfo(out _, out _));
        Pixa empty = Pixa.Create(0);
        empty.SizeRange(out minw, out _, out maxw, out _);
        Assert.Equal((1000000, 0), (minw, maxw));
    }

    [Fact]
    public void ConvertToSameDepth_And_GivenDepth()
    {
        Pixa pixa = Pixa.Create(0);
        pixa.AddPix(Pix.Create(10, 10, 1), L.L_INSERT);
        pixa.AddBox(Box.Create(0, 0, 10, 10), L.L_INSERT);
        pixa.AddPix(Pix.Create(10, 10, 8), L.L_INSERT);
        pixa.AddBox(Box.Create(0, 0, 10, 10), L.L_INSERT);
        Pixa d = pixa.ConvertToSameDepth();
        Assert.Equal(8, d.GetPix(0, L.L_CLONE).Depth);
        Assert.Equal(8, d.GetPix(1, L.L_CLONE).Depth);
        Assert.Equal(2, d.BoxaCount);
        Assert.NotSame(pixa.Boxa, d.Boxa);

        Pixa same = MakeSized((4, 4), (5, 5));
        Assert.Equal(1, same.ConvertToSameDepth().GetPix(0, L.L_CLONE).Depth);

        Pixa cm = Pixa.Create(0);
        cm.AddPix(Pix.CreateWithCmap(4, 4, 8, L.L_SET_WHITE), L.L_INSERT);
        Assert.Null(cm.ConvertToSameDepth().GetPix(0, L.L_CLONE).Colormap);

        Pixa g32 = pixa.ConvertToGivenDepth(32);
        Assert.Equal(32, g32.GetPix(0, L.L_CLONE).Depth);
        Assert.Equal(8, pixa.ConvertToGivenDepth(8).GetPix(0, L.L_CLONE).Depth);
        Assert.Throws<ArgumentException>(() => pixa.ConvertToGivenDepth(16));
        Assert.Throws<ArgumentException>(() => Pixa.Create(0).ConvertToGivenDepth(8));
        Assert.Throws<ArgumentException>(() => Pixa.Create(0).ConvertToSameDepth());
    }

    [Fact]
    public void Equal_WithBoxesAndPermutation()
    {
        Pixa a = MakeSized((5, 5), (6, 6));
        Pixa b = MakeSized((5, 5), (6, 6));
        Assert.True(a.Equal(b, 0, out Numa? idx));
        Assert.NotNull(idx);

        // different order: boxes are matched through the index array
        Pixa c = Pixa.Create(0);
        c.AddPix(Pix.Create(6, 6, 1), L.L_INSERT);
        c.GetPix(0, L.L_CLONE).SetAll();
        c.AddBox(Box.Create(6, 0, 6, 6), L.L_INSERT);
        c.AddPix(Pix.Create(5, 5, 1), L.L_INSERT);
        c.GetPix(1, L.L_CLONE).SetAll();
        c.AddBox(Box.Create(0, 0, 5, 5), L.L_INSERT);
        Assert.False(a.Equal(c, 0, out _));
        Assert.True(a.Equal(c, 1, out idx));
        Assert.Equal(new[] { 1, 0 }, Ints(idx!));

        Pixa diffcount = MakeSized((5, 5));
        Assert.False(a.Equal(diffcount, 0, out _));
        Pixa shifted = MakeSized((5, 5), (6, 6));
        shifted.Boxa.GetBox(1, L.L_CLONE).SetGeometry(40, -1, -1, -1);
        Assert.False(a.Equal(shifted, 2, out _));
        Assert.False(a.Equal(shifted, 100, out _));  // maxdist is a window on the indices

        // no boxes: strict order
        Pixa n1 = Pixa.Create(0);
        n1.AddPix(Pix.Create(5, 5, 1), L.L_INSERT);
        Pixa n2 = Pixa.Create(0);
        n2.AddPix(Pix.Create(5, 5, 1), L.L_INSERT);
        Assert.True(n1.Equal(n2, 0, out idx));
        Assert.Null(idx);
        Assert.False(n1.Equal(MakeSized((5, 5)), 0, out _));  // only one has boxes
        n2.GetPix(0, L.L_CLONE).SetPixel(1, 1, 1);
        Assert.False(n1.Equal(n2, 0, out _));
    }

    [Fact]
    public void SetFullSizeBoxa()
    {
        Pixa pixa = Pixa.Create(0);
        pixa.AddPix(Pix.Create(7, 3, 8), L.L_INSERT);
        pixa.AddPix(Pix.Create(4, 9, 8), L.L_INSERT);
        pixa.SetFullSizeBoxa();
        Assert.Equal(2, pixa.BoxaCount);
        pixa.GetBoxGeometry(1, out int x, out int y, out int w, out int h);
        Assert.Equal((0, 0, 4, 9), (x, y, w, h));
        Pixa.Create(0).SetFullSizeBoxa();  // no-op
    }
}
