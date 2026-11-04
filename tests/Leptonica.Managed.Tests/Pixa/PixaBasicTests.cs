using System;
using System.IO;
using System.Text;
using Leptonica;

namespace Leptonica.Managed.Tests;

internal static class PixTestExtensions
{
    public static uint GetPixel(this Pix pix, int x, int y)
    {
        pix.GetPixel(x, y, out uint val);
        return val;
    }
}

[Collection("LeptDebugOK")]
public class PixaBasicTests
{
    private static Pix MakePix(int w, int h, int d, uint seed)
    {
        Pix pix = Pix.Create(w, h, d);
        for (int i = 0; i < pix.Wpl * h; i++)
            pix.Data[i] = (seed + (uint)i) * 2654435761u;
        if (d == 32)
            for (int i = 0; i < pix.Wpl * h; i++)
                pix.Data[i] |= 0xff;
        pix.Text = "text" + seed;
        return pix;
    }

    private static Pixa MakePixa(int count, bool withBoxes)
    {
        Pixa pixa = Pixa.Create(0);
        for (int i = 0; i < count; i++) {
            pixa.AddPix(Pix.Create(10 + i, 20 + i, 8), L.L_INSERT);
            if (withBoxes)
                pixa.AddBox(Box.Create(i, 2 * i, 10 + i, 20 + i), L.L_INSERT);
        }
        return pixa;
    }

    private static Pix SmallPix(int w, int h, int d, uint val)
    {
        Pix pix = Pix.Create(w, h, d);
        pix.SetAllArbitrary(val);
        return pix;
    }

    /*------------------------- creation and access -------------------------*/

    [Fact]
    public void Create_DefaultSizeAndEmpty()
    {
        Pixa pixa = Pixa.Create(0);
        Assert.Equal(0, pixa.Count);
        Assert.Equal(20, pixa.Nalloc);
        Assert.Equal(20, pixa.Boxa.Nalloc);
        Assert.Equal(0, pixa.BoxaCount);
        Assert.Equal(7, Pixa.Create(7).Nalloc);
        Assert.Equal(20, Pixa.Create(100001).Nalloc);
    }

    [Fact]
    public void AddPix_CopyFlags()
    {
        Pixa pixa = Pixa.Create(2);
        Pix pix = Pix.Create(8, 8, 8);
        pixa.AddPix(pix, L.L_INSERT);
        pixa.AddPix(pix, L.L_COPY);
        pixa.AddPix(pix, L.L_CLONE);
        Assert.Equal(3, pixa.Count);
        Assert.Same(pix, pixa.GetPix(0, L.L_CLONE));
        Assert.NotSame(pix, pixa.GetPix(1, L.L_CLONE));
        Assert.Same(pix, pixa.GetPix(2, L.L_CLONE));
        Assert.NotSame(pix, pixa.GetPix(0, L.L_COPY));
        Assert.Throws<ArgumentException>(() => pixa.AddPix(pix, 7));
        Assert.Equal(3, pixa.Count);
    }

    [Fact]
    public void AddPix_ExtendsBothArrays()
    {
        Pixa pixa = MakePixa(45, true);
        Assert.Equal(45, pixa.Count);
        Assert.Equal(45, pixa.BoxaCount);
        Assert.Equal(pixa.Nalloc, pixa.Boxa.Nalloc);
        Assert.True(pixa.Nalloc >= 45);
        pixa.GetPixDimensions(44, out int w, out int h, out int d);
        Assert.Equal((54, 64, 8), (w, h, d));
    }

    [Fact]
    public void ExtendArrayToSize_Limits()
    {
        Pixa pixa = Pixa.Create(5);
        pixa.ExtendArrayToSize(3);  // too small: no-op
        Assert.Equal(5, pixa.Nalloc);
        pixa.ExtendArrayToSize(40);
        Assert.Equal(40, pixa.Nalloc);
        Assert.Equal(40, pixa.Boxa.Nalloc);
        Assert.Throws<ArgumentException>(() => pixa.ExtendArrayToSize(5000001));
    }

    [Fact]
    public void GetPix_Errors()
    {
        Pixa pixa = MakePixa(2, false);
        Assert.Throws<ArgumentException>(() => pixa.GetPix(-1, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => pixa.GetPix(2, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => pixa.GetPix(0, L.L_INSERT));
        Assert.Throws<ArgumentException>(() => pixa.GetPixDimensions(2, out _, out _, out _));
    }

    [Fact]
    public void GetBox_NullIsNotAnError()
    {
        Pixa pixa = MakePixa(3, false);
        Assert.Throws<ArgumentException>(() => pixa.GetBox(0, L.L_CLONE));  // index beyond the boxa count
        pixa.Boxa.InitFull(null);
        Assert.Equal(20, pixa.Boxa.Count);
        Assert.NotNull(pixa.GetBox(0, L.L_CLONE));

        Pixa pixa2 = MakePixa(2, true);
        Box box = pixa2.GetBox(1, L.L_CLONE)!;
        Assert.Same(box, pixa2.GetBox(1, L.L_CLONE));
        Assert.NotSame(box, pixa2.GetBox(1, L.L_COPY));
        Assert.Throws<ArgumentException>(() => pixa2.GetBox(1, L.L_INSERT));
        pixa2.GetBoxGeometry(1, out int x, out int y, out int w, out int h);
        Assert.Equal((1, 2, 11, 21), (x, y, w, h));
        Assert.Throws<ArgumentException>(() => pixa2.GetBoxGeometry(2, out _, out _, out _, out _));
    }

    [Fact]
    public void GetBoxGeometry_MissingBox_Throws()
    {
        Pixa pixa = MakePixa(2, false);
        Assert.Throws<ArgumentException>(() => pixa.GetBoxGeometry(0, out _, out _, out _, out _));
    }

    [Fact]
    public void GetBoxaAndSetBoxa()
    {
        Pixa pixa = MakePixa(2, true);
        Assert.Same(pixa.Boxa, pixa.GetBoxa(L.L_CLONE));
        Boxa copy = pixa.GetBoxa(L.L_COPY);
        Assert.NotSame(pixa.Boxa, copy);
        Assert.Equal(2, copy.Count);
        Assert.Throws<ArgumentException>(() => pixa.GetBoxa(L.L_INSERT));

        Boxa boxa = Boxa.Create(0);
        boxa.AddBox(Box.Create(5, 5, 5, 5), L.L_INSERT);
        pixa.SetBoxa(boxa, L.L_INSERT);
        Assert.Same(boxa, pixa.Boxa);
        pixa.SetBoxa(boxa, L.L_COPY);
        Assert.NotSame(boxa, pixa.Boxa);
        Assert.Equal(1, pixa.BoxaCount);
        Assert.Throws<ArgumentException>(() => pixa.SetBoxa(boxa, 9));
    }

    [Fact]
    public void GetPixArray_IsTheActualArray()
    {
        Pixa pixa = MakePixa(3, false);
        Pix?[] array = pixa.GetPixArray();
        Assert.Equal(pixa.Nalloc, array.Length);
        Assert.Same(array[1], pixa.GetPix(1, L.L_CLONE));
        Assert.Null(array[3]);
    }

    /*-------------------------------- Copy --------------------------------*/

    [Fact]
    public void Copy_Modes()
    {
        Pixa pixa = MakePixa(3, true);
        Assert.Same(pixa, pixa.Copy(L.L_CLONE));

        Pixa copy = pixa.Copy(L.L_COPY);
        Assert.NotSame(pixa, copy);
        Assert.Equal(3, copy.Count);
        Assert.Equal(3, copy.BoxaCount);
        Assert.NotSame(pixa.GetPix(0, L.L_CLONE), copy.GetPix(0, L.L_CLONE));
        Assert.NotSame(pixa.GetBox(0, L.L_CLONE), copy.GetBox(0, L.L_CLONE));

        Pixa cc = pixa.Copy(L.L_COPY_CLONE);
        Assert.Same(pixa.GetPix(2, L.L_CLONE), cc.GetPix(2, L.L_CLONE));
        Assert.Same(pixa.GetBox(2, L.L_CLONE), cc.GetBox(2, L.L_CLONE));

        Assert.Throws<ArgumentException>(() => pixa.Copy(L.L_INSERT));
    }

    [Fact]
    public void Copy_PixaWithoutBoxes()
    {
        Pixa copy = MakePixa(4, false).Copy(L.L_COPY);
        Assert.Equal(4, copy.Count);
        Assert.Equal(0, copy.BoxaCount);
    }

    /*------------------------- CreateFrom* and split ------------------------*/

    [Fact]
    public void CreateFromPix_Lattice8bpp()
    {
        Pix pixs = Pix.Create(30, 20, 8);
        for (int y = 0; y < 20; y++)
            for (int x = 0; x < 30; x++)
                pixs.SetPixel(x, y, (uint)((x / 10) + 3 * (y / 10) + 1));

        Pixa pixa = Pixa.CreateFromPix(pixs, 5, 10, 10);
        Assert.Equal(5, pixa.Count);
        for (int i = 0; i < 5; i++) {
            Pix pix = pixa.GetPix(i, L.L_CLONE);
            pix.GetDimensions(out int w, out int h, out int d);
            Assert.Equal((10, 10, 8), (w, h, d));
            Assert.Equal((uint)(i + 1), pix.GetPixel(3, 4));
        }
        Assert.Throws<ArgumentException>(() => Pixa.CreateFromPix(pixs, 0, 10, 10));
    }

    [Fact]
    public void CreateFromPix_Binary_ClipsToForeground()
    {
        Pix pixs = Pix.Create(20, 10, 1);
        pixs.SetPixel(2, 3, 1);
        pixs.SetPixel(4, 5, 1);
        // Second cell is empty: its pix is a copy of the full cell.
        Pixa pixa = Pixa.CreateFromPix(pixs, 2, 10, 10);
        Assert.Equal(2, pixa.Count);
        pixa.GetPixDimensions(0, out int w, out int h, out _);
        Assert.Equal((3, 3), (w, h));
        pixa.GetPixDimensions(1, out w, out h, out _);
        Assert.Equal((10, 10), (w, h));
    }

    [Fact]
    public void CreateFromBoxa_InsideAndPartlyOutside()
    {
        Pix pixs = Pix.Create(40, 30, 8);
        pixs.SetAllArbitrary(77);
        Boxa boxa = Boxa.Create(0);
        boxa.AddBox(Box.Create(0, 0, 10, 10), L.L_INSERT);
        boxa.AddBox(Box.Create(20, 10, 20, 20), L.L_INSERT);
        Pixa pixa = Pixa.CreateFromBoxa(pixs, boxa, 0, 0, out bool cropwarn);
        Assert.False(cropwarn);
        Assert.Equal(2, pixa.Count);
        Assert.Equal(2, pixa.BoxaCount);
        pixa.GetBoxGeometry(1, out int x, out int y, out int w, out int h);
        Assert.Equal((20, 10, 20, 20), (x, y, w, h));

        boxa.AddBox(Box.Create(35, 25, 20, 20), L.L_INSERT);  // clipped
        boxa.AddBox(Box.Create(50, 40, 5, 5), L.L_INSERT);    // outside
        pixa = Pixa.CreateFromBoxa(pixs, boxa, 0, 0, out cropwarn);
        Assert.True(cropwarn);
        Assert.Equal(3, pixa.Count);
        Assert.Equal(3, pixa.BoxaCount);
        pixa.GetPixDimensions(2, out w, out h, out _);
        Assert.Equal((5, 5), (w, h));

        pixa = Pixa.CreateFromBoxa(pixs, boxa, 1, 2, out _);
        Assert.Equal(2, pixa.Count);
        Assert.Throws<ArgumentException>(() => Pixa.CreateFromBoxa(pixs, boxa, 0, -1, out _));
    }

    [Fact]
    public void SplitPix_NoBorder()
    {
        Pix pixs = Pix.Create(25, 15, 8);
        pixs.SetAllArbitrary(5);
        Pixa pixa = Pixa.SplitPix(pixs, 3, 2, 0, 0x12345600);
        Assert.Equal(6, pixa.Count);
        pixa.GetPixDimensions(0, out int w, out int h, out int d);
        Assert.Equal((9, 8, 8), (w, h, d));
        Assert.Equal(5u, pixa.GetPix(0, L.L_CLONE).GetPixel(8, 7));
        // The last column is clipped by the source; the rest is white (borderwidth == 0).
        Assert.Equal(5u, pixa.GetPix(2, L.L_CLONE).GetPixel(0, 0));
        Assert.Equal(255u, pixa.GetPix(2, L.L_CLONE).GetPixel(8, 0));
    }

    [Fact]
    public void SplitPix_WithBorder()
    {
        Pix pixs = Pix.Create(20, 20, 8);
        pixs.SetAllArbitrary(5);
        Pixa pixa = Pixa.SplitPix(pixs, 2, 2, 3, 0x40404000);
        Assert.Equal(4, pixa.Count);
        pixa.GetPixDimensions(3, out int w, out int h, out _);
        Assert.Equal((16, 16), (w, h));
        Pix pix = pixa.GetPix(3, L.L_CLONE);
        Assert.Equal(5u, pix.GetPixel(3, 3));
        Assert.Equal(5u, pix.GetPixel(12, 12));
        Assert.NotEqual(5u, pix.GetPixel(0, 0));
        Assert.Throws<ArgumentException>(() => Pixa.SplitPix(pixs, 0, 2, 0, 0));
    }

    /*----------------------------- verifications ----------------------------*/

    [Fact]
    public void VerifyDepthAndDimensions()
    {
        Pixa pixa = Pixa.Create(0);
        Assert.Throws<ArgumentException>(() => pixa.VerifyDepth(out _, out _));
        Assert.Throws<ArgumentException>(() => pixa.VerifyDimensions(out _, out _, out _));

        pixa.AddPix(Pix.Create(10, 12, 8), L.L_INSERT);
        pixa.AddPix(Pix.Create(10, 12, 8), L.L_INSERT);
        pixa.VerifyDepth(out bool same, out int maxd);
        Assert.True(same);
        Assert.Equal(8, maxd);
        pixa.VerifyDimensions(out same, out int maxw, out int maxh);
        Assert.True(same);
        Assert.Equal((10, 12), (maxw, maxh));

        // The running maximum is updated before the comparison, so a larger pix that
        // comes later is not flagged (as in the C code); a smaller one is.
        pixa.AddPix(Pix.Create(14, 12, 32), L.L_INSERT);
        pixa.VerifyDepth(out same, out maxd);
        Assert.True(same);
        Assert.Equal(32, maxd);
        pixa.VerifyDimensions(out same, out maxw, out maxh);
        Assert.True(same);
        Assert.Equal((14, 12), (maxw, maxh));

        pixa.AddPix(Pix.Create(10, 12, 8), L.L_INSERT);
        pixa.VerifyDepth(out same, out maxd);
        Assert.False(same);
        Assert.Equal(32, maxd);
        pixa.VerifyDimensions(out same, out maxw, out maxh);
        Assert.False(same);
        Assert.Equal((14, 12), (maxw, maxh));
    }

    [Fact]
    public void IsFull()
    {
        Pixa pixa = MakePixa(3, true);
        pixa.IsFull(out bool fullpa, out bool fullba);
        Assert.True(fullpa);
        Assert.True(fullba);

        pixa.GetPixArray()[1] = null;
        pixa.IsFull(out fullpa, out fullba);
        Assert.False(fullpa);
        Assert.True(fullba);
    }

    /*------------------------------- text ----------------------------------*/

    [Fact]
    public void CountTextAndSetText()
    {
        Pixa pixa = MakePixa(3, false);
        Assert.Equal(0, pixa.CountText());
        pixa.SetText("abc", null);
        Assert.Equal(3, pixa.CountText());
        Assert.Equal("abc", pixa.GetPix(2, L.L_CLONE).Text);

        Sarray sa = Sarray.Create(3);
        sa.AddString("x", L.L_COPY);
        sa.AddString("", L.L_COPY);
        sa.AddString("z", L.L_COPY);
        pixa.SetText("ignored", sa);
        Assert.Equal(2, pixa.CountText());
        Assert.Equal("x", pixa.GetPix(0, L.L_CLONE).Text);

        pixa.SetText(null, null);
        Assert.Equal(0, pixa.CountText());

        Sarray sa2 = Sarray.Create(2);
        Assert.Throws<ArgumentException>(() => pixa.SetText(null, sa2));
    }

    [Fact]
    public void WriteStreamInfo_Format()
    {
        Pixa pixa = Pixa.Create(0);
        Pix pix = Pix.Create(5, 6, 8);
        pix.Text = "hello";
        pixa.AddPix(pix, L.L_INSERT);
        pixa.AddPix(Pix.Create(3, 4, 32), L.L_INSERT);
        using MemoryStream ms = new MemoryStream();
        pixa.WriteStreamInfo(ms);
        Assert.Equal("Pix 0: w = 5, h = 6, d = 8, spp = 1, text = hello\n" +
                     "Pix 1: w = 3, h = 4, d = 32, spp = 3\n",
                     Encoding.ASCII.GetString(ms.ToArray()));
    }

    [Fact]
    public void GetLinePtrs_RequiresSameDepth()
    {
        Pixa pixa = Pixa.Create(0);
        pixa.AddPix(Pix.Create(40, 3, 8), L.L_INSERT);
        pixa.AddPix(Pix.Create(40, 2, 8), L.L_INSERT);
        int[][] lines = pixa.GetLinePtrs();
        Assert.Equal(2, lines.Length);
        Assert.Equal(new[] { 0, 10, 20 }, lines[0]);
        Assert.Equal(new[] { 0, 10 }, lines[1]);
        pixa.AddPix(Pix.Create(40, 2, 1), L.L_INSERT);
        Assert.Throws<ArgumentException>(() => pixa.GetLinePtrs());
    }

    /*----------------------------- modifiers -------------------------------*/

    [Fact]
    public void ReplacePix()
    {
        Pixa pixa = MakePixa(3, true);
        Pix pix = Pix.Create(3, 3, 1);
        Box box = Box.Create(7, 8, 9, 10);
        pixa.ReplacePix(1, pix, box);
        Assert.Same(pix, pixa.GetPix(1, L.L_CLONE));
        Assert.Same(box, pixa.GetBox(1, L.L_CLONE));
        Assert.Equal(3, pixa.Count);

        pixa.ReplacePix(2, pix, null);
        Assert.NotSame(box, pixa.GetBox(2, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => pixa.ReplacePix(3, pix, null));
        Assert.Throws<ArgumentException>(() => pixa.ReplacePix(-1, pix, null));
    }

    [Fact]
    public void InsertPix_ShiftsPixAndBoxes()
    {
        Pixa pixa = MakePixa(3, true);
        Pix pix = Pix.Create(2, 2, 1);
        pixa.InsertPix(1, pix, Box.Create(1, 1, 2, 2));
        Assert.Equal(4, pixa.Count);
        Assert.Equal(4, pixa.BoxaCount);
        Assert.Same(pix, pixa.GetPix(1, L.L_CLONE));
        pixa.GetPixDimensions(2, out int w, out _, out _);
        Assert.Equal(11, w);
        pixa.GetBoxGeometry(2, out int x, out _, out _, out _);
        Assert.Equal(1, x);

        pixa.InsertPix(0, pix, null);  // no box: the boxa is not shifted
        Assert.Equal(5, pixa.Count);
        Assert.Equal(4, pixa.BoxaCount);
        pixa.InsertPix(5, pix, null);
        Assert.Equal(6, pixa.Count);
        Assert.Throws<ArgumentException>(() => pixa.InsertPix(7, pix, null));
        Assert.Throws<ArgumentException>(() => pixa.InsertPix(-1, pix, null));
    }

    [Fact]
    public void InsertPix_ExtendsFullArrays()
    {
        Pixa pixa = Pixa.Create(2);
        for (int i = 0; i < 10; i++)
            pixa.InsertPix(0, Pix.Create(i + 1, 1, 8), Box.Create(i, 0, 1, 1));
        Assert.Equal(10, pixa.Count);
        Assert.Equal(10, pixa.BoxaCount);
        pixa.GetPixDimensions(0, out int w, out _, out _);
        Assert.Equal(10, w);
    }

    [Fact]
    public void RemovePix_RemovesBoxToo()
    {
        Pixa pixa = MakePixa(4, true);
        pixa.RemovePix(1);
        Assert.Equal(3, pixa.Count);
        Assert.Equal(3, pixa.BoxaCount);
        pixa.GetPixDimensions(1, out int w, out _, out _);
        Assert.Equal(12, w);
        pixa.GetBoxGeometry(1, out int x, out _, out _, out _);
        Assert.Equal(2, x);
        Assert.Throws<ArgumentException>(() => pixa.RemovePix(3));
        Assert.Throws<ArgumentException>(() => pixa.RemovePix(-1));
    }

    [Fact]
    public void RemovePix_WithoutBoxes()
    {
        Pixa pixa = MakePixa(3, false);
        pixa.RemovePix(0);
        Assert.Equal(2, pixa.Count);
        Assert.Equal(0, pixa.BoxaCount);
    }

    [Fact]
    public void RemovePixAndSave()
    {
        Pixa pixa = MakePixa(3, true);
        Pix pix1 = pixa.GetPix(1, L.L_CLONE);
        Box box1 = pixa.GetBox(1, L.L_CLONE)!;
        pixa.RemovePixAndSave(1, out Pix? ppix, out Box? pbox);
        Assert.Same(pix1, ppix);
        Assert.Same(box1, pbox);
        Assert.Equal(2, pixa.Count);
        Assert.Equal(2, pixa.BoxaCount);
        Assert.Throws<ArgumentException>(() => pixa.RemovePixAndSave(2, out _, out _));
    }

    [Fact]
    public void RemoveSelected_DecreasingOrder()
    {
        Pixa pixa = MakePixa(6, true);
        Numa na = Numa.Create(3);
        na.AddNumber(1);
        na.AddNumber(4);
        na.AddNumber(2);
        pixa.RemoveSelected(na);
        Assert.Equal(3, pixa.Count);
        pixa.GetPixDimensions(0, out int w, out _, out _);
        Assert.Equal(10, w);
        pixa.GetPixDimensions(1, out w, out _, out _);
        Assert.Equal(13, w);
        pixa.GetPixDimensions(2, out w, out _, out _);
        Assert.Equal(15, w);
        Assert.Throws<ArgumentException>(() => pixa.RemoveSelected(Numa.Create(1)));
    }

    [Fact]
    public void InitFull_And_Clear()
    {
        Pixa pixa = Pixa.Create(5);
        pixa.InitFull(null, null);
        Assert.Equal(5, pixa.Count);
        pixa.GetPixDimensions(4, out int w, out int h, out int d);
        Assert.Equal((1, 1, 1), (w, h, d));
        Assert.Equal(0, pixa.BoxaCount);

        Pixa pixa2 = Pixa.Create(4);
        Pix pix = SmallPix(6, 7, 8, 9);
        pixa2.InitFull(pix, Box.Create(1, 2, 3, 4));
        Assert.Equal(4, pixa2.Count);
        Assert.Equal(4, pixa2.BoxaCount);
        Assert.NotSame(pix, pixa2.GetPix(3, L.L_CLONE));
        Assert.Equal(9u, pixa2.GetPix(3, L.L_CLONE).GetPixel(5, 6));

        pixa2.Clear();
        Assert.Equal(0, pixa2.Count);
        Assert.Equal(0, pixa2.BoxaCount);
        Assert.Null(pixa2.GetPixArray()[0]);
    }

    /*--------------------------- Join / Interleave --------------------------*/

    [Fact]
    public void Join_RangeAndBoxes()
    {
        Pixa pixad = MakePixa(2, true);
        Pixa pixas = MakePixa(5, true);
        pixad.Join(pixas, 1, 3);
        Assert.Equal(5, pixad.Count);
        Assert.Equal(5, pixad.BoxaCount);
        Assert.Same(pixas.GetPix(1, L.L_CLONE), pixad.GetPix(2, L.L_CLONE));
        pixad.GetBoxGeometry(4, out int x, out _, out _, out _);
        Assert.Equal(3, x);

        pixad.Join(pixas, -1, -1);
        Assert.Equal(10, pixad.Count);
        pixad.Join(null, 0, -1);
        pixad.Join(Pixa.Create(0), 0, -1);
        Assert.Equal(10, pixad.Count);
        Assert.Throws<ArgumentException>(() => pixad.Join(pixas, 4, 2));
    }

    [Fact]
    public void Join_SourceWithoutBoxes()
    {
        Pixa pixad = MakePixa(2, true);
        Pixa pixas = MakePixa(3, false);
        pixad.Join(pixas, 0, -1);
        Assert.Equal(5, pixad.Count);
        Assert.Equal(2, pixad.BoxaCount);
    }

    [Fact]
    public void Interleave()
    {
        Pixa pixa1 = MakePixa(3, true);
        Pixa pixa2 = MakePixa(2, false);
        Pixa pixad = pixa1.Interleave(pixa2, L.L_CLONE);
        Assert.Equal(4, pixad.Count);
        Assert.Same(pixa1.GetPix(0, L.L_CLONE), pixad.GetPix(0, L.L_CLONE));
        Assert.Same(pixa2.GetPix(0, L.L_CLONE), pixad.GetPix(1, L.L_CLONE));
        Assert.Same(pixa1.GetPix(1, L.L_CLONE), pixad.GetPix(2, L.L_CLONE));
        Assert.Equal(2, pixad.BoxaCount);  // only pixa1 has boxes
        Assert.NotSame(pixa1.GetBox(0, L.L_CLONE), pixad.GetBox(0, L.L_CLONE));

        Pixa copied = pixa1.Interleave(pixa2, L.L_COPY);
        Assert.NotSame(pixa1.GetPix(0, L.L_CLONE), copied.GetPix(0, L.L_CLONE));

        Assert.Throws<ArgumentException>(() => pixa1.Interleave(pixa2, L.L_INSERT));
        Assert.Throws<ArgumentException>(() => pixa1.Interleave(Pixa.Create(0), L.L_CLONE));
    }

    /*------------------------------ serialization ---------------------------*/

    private static Pixa MakeSerializablePixa()
    {
        Pixa pixa = Pixa.Create(0);
        Pix pix1 = MakePix(13, 7, 8, 1);
        pix1.XRes = 300;
        pix1.YRes = 200;
        Pix pix2 = MakePix(9, 5, 32, 2);
        Pix pix3 = MakePix(33, 3, 1, 3);
        pixa.AddPix(pix1, L.L_INSERT);
        pixa.AddPix(pix2, L.L_INSERT);
        pixa.AddPix(pix3, L.L_INSERT);
        pixa.AddBox(Box.Create(1, 2, 13, 7), L.L_INSERT);
        pixa.AddBox(Box.Create(20, 2, 9, 5), L.L_INSERT);
        pixa.AddBox(Box.Create(40, 2, 33, 3), L.L_INSERT);
        return pixa;
    }

    private static void AssertSamePixa(Pixa expected, Pixa actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected.BoxaCount, actual.BoxaCount);
        for (int i = 0; i < expected.Count; i++) {
            Pix a = expected.GetPix(i, L.L_CLONE);
            Pix b = actual.GetPix(i, L.L_CLONE);
            a.GetDimensions(out int w, out int h, out int d);
            b.GetDimensions(out int w2, out int h2, out int d2);
            Assert.Equal((w, h, d), (w2, h2, d2));
            Assert.Equal(a.XRes, b.XRes);
            Assert.Equal(a.YRes, b.YRes);
            Assert.Equal(a.Data, b.Data);
        }
        for (int i = 0; i < expected.BoxaCount; i++) {
            expected.GetBoxGeometry(i, out int x, out int y, out int bw, out int bh);
            actual.GetBoxGeometry(i, out int x2, out int y2, out int bw2, out int bh2);
            Assert.Equal((x, y, bw, bh), (x2, y2, bw2, bh2));
        }
    }

    [Fact]
    public void WriteMem_Header()
    {
        byte[] data = MakeSerializablePixa().WriteMem();
        string head = Encoding.ASCII.GetString(data, 0, 120);
        Assert.StartsWith("\nPixa Version 2\nNumber of pix = 3\n\nBoxa Version 2\nNumber of boxes = 3\n" +
                          "  Box[0]: x = 1, y = 2, w = 13, h = 7\n", head);
        Assert.Contains(" pix[0]: xres = 300, yres = 200\n", Encoding.ASCII.GetString(data));
    }

    [Fact]
    public void Serialization_RoundTrip_Mem()
    {
        Pixa pixa = MakeSerializablePixa();
        Pixa back = Pixa.ReadMem(pixa.WriteMem());
        AssertSamePixa(pixa, back);
    }

    [Fact]
    public void Serialization_RoundTrip_File()
    {
        Pixa pixa = MakeSerializablePixa();
        string path = Path.Combine(Path.GetTempPath(), "pixa_basic_" + Guid.NewGuid().ToString("N") + ".pa");
        try {
            pixa.Write(path);
            AssertSamePixa(pixa, Pixa.Read(path));
        } finally {
            File.Delete(path);
        }
        Assert.Throws<LeptonicaException>(() => Pixa.Read(path));
    }

    [Fact]
    public void Serialization_RoundTrip_NonSeekableStream()
    {
        Pixa pixa = MakeSerializablePixa();
        byte[] data = pixa.WriteMem();
        using NonSeekableStream ns = new NonSeekableStream(data);
        AssertSamePixa(pixa, Pixa.ReadStream(ns));
    }

    [Fact]
    public void Serialization_EmptyPixa()
    {
        Pixa back = Pixa.ReadMem(Pixa.Create(0).WriteMem());
        Assert.Equal(0, back.Count);
        Assert.Equal(0, back.BoxaCount);
    }

    [Fact]
    public void Serialization_PixaWithoutBoxes()
    {
        Pixa pixa = Pixa.Create(0);
        pixa.AddPix(MakePix(5, 5, 8, 4), L.L_INSERT);
        Pixa back = Pixa.ReadMem(pixa.WriteMem());
        AssertSamePixa(pixa, back);
    }

    [Fact]
    public void Serialization_Colormap()
    {
        Pix pix = Pix.CreateWithCmap(7, 6, 4, L.L_SET_WHITE);
        Pixa pixa = Pixa.Create(0);
        pixa.AddPix(pix, L.L_INSERT);
        Pixa back = Pixa.ReadMem(pixa.WriteMem());
        Assert.NotNull(back.GetPix(0, L.L_CLONE).Colormap);
    }

    [Fact]
    public void Read_Errors()
    {
        Assert.Throws<ArgumentException>(() => Pixa.ReadMem(Encoding.ASCII.GetBytes("not a pixa")));
        Assert.Throws<ArgumentException>(() => Pixa.ReadMem(Encoding.ASCII.GetBytes("\nPixa Version 3\nNumber of pix = 0\n")));
        Assert.Throws<ArgumentException>(() => Pixa.ReadMem(Encoding.ASCII.GetBytes("\nPixa Version 2\nNumber of pix = -1\n")));
        Assert.Throws<ArgumentException>(() => Pixa.ReadMem(Encoding.ASCII.GetBytes("\nPixa Version 2\nNumber of pix = 5000001\n")));
        byte[] data = MakeSerializablePixa().WriteMem();
        Assert.ThrowsAny<Exception>(() => Pixa.ReadMem(data.AsSpan(0, data.Length - 40)));
    }

    [Fact]
    public void WriteDebug_DisabledByDefault()
    {
        using var debug = new LeptDebugScope(false);
        string path = Path.Combine(Path.GetTempPath(), "pixa_basic_debug_" + Guid.NewGuid().ToString("N") + ".pa");
        MakeSerializablePixa().WriteDebug(path);
        Assert.False(File.Exists(path));
    }

    private sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream inner;
        public NonSeekableStream(byte[] data) { inner = new MemoryStream(data); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /*--------------------------------- Pixaa -------------------------------*/

    [Fact]
    public void Pixaa_CreateAndAdd()
    {
        Pixaa paa = Pixaa.Create(0);
        Assert.Equal(0, paa.Count);
        Assert.Equal(20, paa.Nalloc);
        Assert.Equal(20, Pixaa.Create(100001).Nalloc);

        for (int i = 0; i < 45; i++)
            paa.AddPixa(MakePixa(i % 3 + 1, false), L.L_INSERT);
        Assert.Equal(45, paa.Count);
        Assert.True(paa.Nalloc >= 45);

        int n = paa.GetCount(out Numa na);
        Assert.Equal(45, n);
        Assert.Equal(45, na.Count);
        Assert.Equal(2, na.GetIValue(1));

        Pixa pixa = paa.GetPixa(1, L.L_CLONE);
        Assert.Same(pixa, paa.GetPixa(1, L.L_CLONE));
        Assert.NotSame(pixa, paa.GetPixa(1, L.L_COPY));
        Assert.Throws<ArgumentException>(() => paa.GetPixa(45, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => paa.GetPixa(0, L.L_INSERT));
        Assert.Throws<ArgumentException>(() => paa.AddPixa(pixa, 9));
    }

    [Fact]
    public void Pixaa_AddPixa_CopyFlags()
    {
        Pixaa paa = Pixaa.Create(2);
        Pixa pixa = MakePixa(2, true);
        paa.AddPixa(pixa, L.L_CLONE);
        paa.AddPixa(pixa, L.L_COPY);
        paa.AddPixa(pixa, L.L_COPY_CLONE);
        Assert.Same(pixa, paa.GetPixa(0, L.L_CLONE));
        Assert.NotSame(pixa, paa.GetPixa(1, L.L_CLONE));
        Assert.NotSame(pixa.GetPix(0, L.L_CLONE), paa.GetPixa(1, L.L_CLONE).GetPix(0, L.L_CLONE));
        Assert.Same(pixa.GetPix(0, L.L_CLONE), paa.GetPixa(2, L.L_CLONE).GetPix(0, L.L_CLONE));
    }

    [Fact]
    public void Pixaa_AddPixAndBox()
    {
        Pixaa paa = Pixaa.Create(0);
        paa.AddPixa(Pixa.Create(0), L.L_INSERT);
        Pix pix = Pix.Create(4, 4, 8);
        paa.AddPix(0, pix, Box.Create(1, 1, 4, 4), L.L_INSERT);
        paa.AddPix(0, Pix.Create(5, 5, 8), null, L.L_INSERT);
        Assert.Equal(2, paa.GetPixa(0, L.L_CLONE).Count);
        Assert.Equal(1, paa.GetPixa(0, L.L_CLONE).BoxaCount);
        Assert.Same(pix, paa.GetPix(0, 0, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => paa.GetPix(0, 2, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => paa.GetPix(1, 0, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => paa.AddPix(3, pix, null, L.L_CLONE));

        paa.AddBox(Box.Create(0, 0, 9, 9), L.L_INSERT);
        Assert.Equal(1, paa.GetBoxa(L.L_CLONE).Count);
        Assert.NotSame(paa.Boxa, paa.GetBoxa(L.L_COPY));
        Assert.Throws<ArgumentException>(() => paa.GetBoxa(L.L_INSERT));
        Assert.Throws<ArgumentException>(() => paa.AddBox(Box.Create(0, 0, 1, 1), 5));
    }

    [Fact]
    public void Pixaa_CreateFromPixa_Consecutive()
    {
        Pixa pixa = MakePixa(7, false);
        Pixaa paa = Pixaa.CreateFromPixa(pixa, 3, L.L_CHOOSE_CONSECUTIVE, L.L_CLONE);
        Assert.Equal(3, paa.Count);
        paa.GetCount(out Numa na);
        Assert.Equal(new[] { 3, 3, 1 }, new[] { na.GetIValue(0), na.GetIValue(1), na.GetIValue(2) });
        Assert.Same(pixa.GetPix(4, L.L_CLONE), paa.GetPix(1, 1, L.L_CLONE));

        paa = Pixaa.CreateFromPixa(MakePixa(6, false), 3, L.L_CHOOSE_CONSECUTIVE, L.L_COPY);
        Assert.Equal(2, paa.Count);
    }

    [Fact]
    public void Pixaa_CreateFromPixa_SkipBy()
    {
        Pixa pixa = MakePixa(7, false);
        Pixaa paa = Pixaa.CreateFromPixa(pixa, 3, L.L_CHOOSE_SKIP_BY, L.L_CLONE);
        Assert.Equal(3, paa.Count);
        paa.GetCount(out Numa na);
        Assert.Equal(new[] { 3, 2, 2 }, new[] { na.GetIValue(0), na.GetIValue(1), na.GetIValue(2) });
        Assert.Same(pixa.GetPix(6, L.L_CLONE), paa.GetPix(0, 2, L.L_CLONE));
        Assert.Same(pixa.GetPix(5, L.L_CLONE), paa.GetPix(2, 1, L.L_CLONE));
    }

    [Fact]
    public void Pixaa_CreateFromPixa_Errors()
    {
        Pixa pixa = MakePixa(3, false);
        Assert.Throws<ArgumentException>(() => Pixaa.CreateFromPixa(Pixa.Create(0), 2, L.L_CHOOSE_CONSECUTIVE, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => Pixaa.CreateFromPixa(pixa, 0, L.L_CHOOSE_CONSECUTIVE, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => Pixaa.CreateFromPixa(pixa, 2, 0, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => Pixaa.CreateFromPixa(pixa, 2, L.L_CHOOSE_SKIP_BY, L.L_INSERT));
    }

    [Fact]
    public void Pixaa_Verify()
    {
        Pixaa paa = Pixaa.Create(0);
        Assert.Throws<ArgumentException>(() => paa.VerifyDepth(out _, out _));
        Assert.Throws<ArgumentException>(() => paa.VerifyDimensions(out _, out _, out _));

        Pixa pixa1 = Pixa.Create(0);
        pixa1.AddPix(Pix.Create(10, 10, 8), L.L_INSERT);
        Pixa pixa2 = Pixa.Create(0);
        pixa2.AddPix(Pix.Create(10, 10, 8), L.L_INSERT);
        paa.AddPixa(pixa1, L.L_INSERT);
        paa.AddPixa(pixa2, L.L_INSERT);
        paa.VerifyDepth(out bool same, out int maxd);
        Assert.True(same);
        Assert.Equal(8, maxd);
        paa.VerifyDimensions(out same, out int maxw, out int maxh);
        Assert.True(same);
        Assert.Equal((10, 10), (maxw, maxh));

        // A pixa with larger pix that comes later is not flagged, a smaller one is (as in the C code).
        Pixa pixa3 = Pixa.Create(0);
        pixa3.AddPix(Pix.Create(12, 10, 32), L.L_INSERT);
        paa.AddPixa(pixa3, L.L_INSERT);
        paa.VerifyDepth(out same, out maxd);
        Assert.True(same);
        Assert.Equal(32, maxd);
        paa.VerifyDimensions(out same, out maxw, out maxh);
        Assert.True(same);
        Assert.Equal((12, 10), (maxw, maxh));

        Pixa pixa4 = Pixa.Create(0);
        pixa4.AddPix(Pix.Create(10, 10, 8), L.L_INSERT);
        paa.AddPixa(pixa4, L.L_INSERT);
        paa.VerifyDepth(out same, out maxd);
        Assert.False(same);
        Assert.Equal(32, maxd);
        paa.VerifyDimensions(out same, out maxw, out maxh);
        Assert.False(same);
        Assert.Equal((12, 10), (maxw, maxh));
    }

    [Fact]
    public void Pixaa_IsFull()
    {
        Pixaa paa = Pixaa.Create(0);
        Assert.True(paa.IsFull());
        paa.AddPixa(MakePixa(2, false), L.L_INSERT);
        Assert.True(paa.IsFull());
        Pixa pixa = MakePixa(2, false);
        pixa.GetPixArray()[0] = null;
        paa.AddPixa(pixa, L.L_INSERT);
        Assert.False(paa.IsFull());
    }

    [Fact]
    public void Pixaa_InitFull_Replace_Clear_Truncate()
    {
        Pixaa paa = Pixaa.Create(4);
        paa.InitFull(MakePixa(1, false));
        Assert.Equal(4, paa.Count);
        Assert.NotSame(paa.GetPixa(0, L.L_CLONE), paa.GetPixa(1, L.L_CLONE));

        Pixa pixa = MakePixa(3, false);
        paa.ReplacePixa(2, pixa);
        Assert.Same(pixa, paa.GetPixa(2, L.L_CLONE));
        Assert.Throws<ArgumentException>(() => paa.ReplacePixa(4, pixa));
        Assert.Throws<ArgumentException>(() => paa.ReplacePixa(-1, pixa));

        // Truncate removes the trailing empty pixa only.
        paa.ReplacePixa(3, Pixa.Create(0));
        paa.Truncate();
        Assert.Equal(3, paa.Count);
        paa.ReplacePixa(2, Pixa.Create(0));
        paa.ReplacePixa(1, Pixa.Create(0));
        paa.Truncate();
        Assert.Equal(1, paa.Count);

        paa.Clear();
        Assert.Equal(0, paa.Count);
    }

    [Fact]
    public void Pixaa_Join()
    {
        Pixaa paad = Pixaa.Create(0);
        paad.AddPixa(MakePixa(1, false), L.L_INSERT);
        Pixaa paas = Pixaa.Create(0);
        for (int i = 0; i < 4; i++)
            paas.AddPixa(MakePixa(i + 1, false), L.L_INSERT);

        paad.Join(paas, 1, 2);
        Assert.Equal(3, paad.Count);
        Assert.Same(paas.GetPixa(1, L.L_CLONE), paad.GetPixa(1, L.L_CLONE));
        paad.Join(paas, -1, -1);
        Assert.Equal(7, paad.Count);
        paad.Join(null, 0, -1);
        Assert.Equal(7, paad.Count);
        Assert.Throws<ArgumentException>(() => paad.Join(paas, 3, 1));
    }

    [Fact]
    public void Pixaa_Serialization_RoundTrip()
    {
        Pixaa paa = Pixaa.Create(0);
        paa.AddPixa(MakeSerializablePixa(), L.L_INSERT);
        Pixa pixa2 = Pixa.Create(0);
        pixa2.AddPix(MakePix(6, 6, 4, 9), L.L_INSERT);
        paa.AddPixa(pixa2, L.L_INSERT);
        paa.AddPixa(Pixa.Create(0), L.L_INSERT);
        paa.AddBox(Box.Create(3, 3, 30, 30), L.L_INSERT);

        byte[] data = paa.WriteMem();
        Assert.StartsWith("\nPixaa Version 2\nNumber of pixa = 3\n\nBoxa Version 2\nNumber of boxes = 1\n",
                          Encoding.ASCII.GetString(data, 0, 80));
        Assert.Contains("\n\n --------------- pixa[1] ---------------\n", Encoding.ASCII.GetString(data));

        Pixaa back = Pixaa.ReadMem(data);
        Assert.Equal(3, back.Count);
        Assert.Equal(1, back.Boxa.Count);
        AssertSamePixa(paa.GetPixa(0, L.L_CLONE), back.GetPixa(0, L.L_CLONE));
        AssertSamePixa(paa.GetPixa(1, L.L_CLONE), back.GetPixa(1, L.L_CLONE));
        Assert.Equal(0, back.GetPixa(2, L.L_CLONE).Count);

        using NonSeekableStream ns = new NonSeekableStream(data);
        Assert.Equal(3, Pixaa.ReadStream(ns).Count);
    }

    [Fact]
    public void Pixaa_Serialization_Files()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pixaa_basic_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            Pixa a = MakeSerializablePixa();
            Pixa b = MakePixa(2, true);
            a.Write(Path.Combine(dir, "a.pa"));
            b.Write(Path.Combine(dir, "b.pa"));

            string file = Path.Combine(dir, "all.paa");
            Pixaa single = Pixaa.Create(0);
            single.AddPixa(a, L.L_INSERT);
            single.Write(file);
            Assert.Equal(1, Pixaa.Read(file).Count);
            Assert.Throws<LeptonicaException>(() => Pixaa.Read(Path.Combine(dir, "missing")));
        } finally {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Pixaa_Read_Errors()
    {
        Assert.Throws<ArgumentException>(() => Pixaa.ReadMem(Encoding.ASCII.GetBytes("junk")));
        Assert.Throws<ArgumentException>(() => Pixaa.ReadMem(Encoding.ASCII.GetBytes("\nPixaa Version 7\nNumber of pixa = 0\n")));
        Assert.Throws<ArgumentException>(() => Pixaa.ReadMem(Encoding.ASCII.GetBytes("\nPixaa Version 2\nNumber of pixa = -3\n")));
    }
}
