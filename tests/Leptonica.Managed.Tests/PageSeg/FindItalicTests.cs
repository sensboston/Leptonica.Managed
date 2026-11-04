using Leptonica;
using Leptonica.Managed.Tests.Reg;
using System;
using System.IO;
using Xunit;

namespace Leptonica.Managed.Tests;

/// <summary>Tests for finditalic.c.</summary>
public class FindItalicTests
{
    private static Pix MakeItalicAndUprightWords()
    {
        Pix pix = Pix.Create(300, 100, 1);

        /* Word 1 (x ~ 25..65, y = 20..65): three slanted strokes with slope -2/7 */
        for (int s = 0; s < 3; s++) {
            int baseX = 40 + s * 8;
            for (int y = 20; y <= 65; y++) {
                int xLeft = baseX - ((y - 20) * 2) / 7;
                for (int dx = 0; dx < 6; dx++)
                    pix.SetPixel(xLeft + dx, y, 1);
            }
        }

        /* Word 2 (x = 180..202, y = 20..65): three upright vertical strokes */
        for (int s = 0; s < 3; s++) {
            int baseX = 180 + s * 8;
            pix.Rasterop(baseX, 20, 6, 46, L.PIX_SET, null, 0, 0);
        }

        return pix;
    }

    [Fact]
    public void DetectsItalicWordAndExcludesUprightWord()
    {
        Pix pix = MakeItalicAndUprightWords();

        /* Automatic word mask generation */
        pix.ItalicWords(null, null, out Boxa boxaAuto, 0);
        Assert.Equal(1, boxaAuto.Count);
        boxaAuto.GetBoxGeometry(0, out int bx, out _, out int bw, out _);
        Assert.True(bx < 100 && bx + bw < 120);

        /* Explicit word mask */
        Pix pixw = Pix.CreateTemplate(pix);
        pixw.Rasterop(20, 18, 65, 50, L.PIX_SET, null, 0, 0);
        pixw.Rasterop(175, 18, 60, 50, L.PIX_SET, null, 0, 0);
        pix.ItalicWords(null, pixw, out Boxa boxaMask, 0);
        Assert.Equal(1, boxaMask.Count);
        boxaMask.GetBoxGeometry(0, out bx, out int by, out bw, out int bh);
        Assert.Equal((20, 18, 65, 50), (bx, by, bw, bh));

        /* Explicit word bounding boxes */
        Boxa boxaw = pixw.ConnComp(8);
        pix.ItalicWords(boxaw, null, out Boxa boxaBoxes, 0);
        Assert.True(boxaMask.Equal(boxaBoxes, 0, out _));
    }

    [Fact]
    public void MatchesGoldenBoxesOnItalicPng()
    {
        if (TestPaths.GoldenDir == null || !Directory.Exists(TestPaths.ProgDir))
            return;

        Pix pixs = TestPaths.ReadInput("italic.png");
        Boxa golden = Boxa.Read(Path.Combine(TestPaths.GoldenDir, "italic_golden.00.ba"));

        pixs.ItalicWords(null, null, out Boxa boxa1, 0);
        Assert.True(golden.Equal(boxa1, 0, out _));

        pixs.WordMaskByDilation(out _, out int size, null);
        Pix pixm = pixs.MorphSequence($"d1.5 + c{size}.1", 0);
        pixs.ItalicWords(null, pixm, out Boxa boxa2, 0);
        Assert.True(golden.Equal(boxa2, 0, out _));

        Boxa boxa3 = pixm.ConnComp(8);
        pixs.ItalicWords(boxa3, null, out Boxa boxa4, 0);
        Assert.True(golden.Equal(boxa4, 0, out _));
    }

    [Fact]
    public void RejectsBothBoxawAndPixw()
    {
        Pix pix = Pix.Create(64, 64, 1);
        Pix pixw = Pix.Create(64, 64, 1);
        Boxa boxaw = Boxa.Create(1);
        Assert.Throws<ArgumentException>(() => pix.ItalicWords(boxaw, pixw, out _, 0));
    }
}
