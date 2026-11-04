using System;
using System.IO;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests;

/// <summary>Tests for classapp.c on synthetic pages and word-box patterns.</summary>
public class ClassAppTests
{
    private static Pix MakeTwoLineWordPage()
    {
        /* Two textlines with two words per line, each word made of 3 close character strokes */
        Pix pix = Pix.Create(300, 140, 1);
        int[] lineY = { 20, 80 };
        int[] wordX = { 30, 150 };
        foreach (int y in lineY) {
            foreach (int wx in wordX) {
                for (int c = 0; c < 3; c++)
                    pix.Rasterop(wx + c * 7, y, 5, 18, L.PIX_SET, null, 0, 0);
            }
        }
        return pix;
    }

    [Fact]
    public void GetWordsAndWordBoxesInTextlinesAgree()
    {
        Pix pix = MakeTwoLineWordPage();

        pix.GetWordsInTextlines(5, 5, 200, 60, out Boxa boxad1, out Pixa pixad, out Numa nai1);
        pix.GetWordBoxesInTextlines(5, 5, 200, 60, out Boxa boxad2, out Numa nai2);

        Assert.Equal(4, boxad1.Count);
        Assert.Equal(4, pixad.Count);
        Assert.True(boxad1.Equal(boxad2, 0, out _));
        Assert.Equal(new[] { 0, 0, 1, 1 }, nai1.GetIArray());
        Assert.Equal(new[] { 0, 0, 1, 1 }, nai2.GetIArray());
    }

    [Fact]
    public void FindWordAndCharacterBoxesUsesGlobalCoordinates()
    {
        /* 8 bpp gray page (white = 255, dark ink = 40) at ~300 ppi scale */
        Pix pix = Pix.Create(600, 260, 8);
        pix.SetAll();
        /* Word 1 at x = 80, y = 80: 3 characters 12x32 spaced by 6 px */
        for (int c = 0; c < 3; c++)
            pix.SetInRectArbitrary(Box.Create(80 + c * 18, 80, 12, 32), 40);
        /* Word 2 at x = 300, y = 80: 2 characters 12x32 spaced by 6 px */
        for (int c = 0; c < 2; c++)
            pix.SetInRectArbitrary(Box.Create(300 + c * 18, 80, 12, 32), 40);

        Box region = Box.Create(50, 50, 500, 150);
        pix.FindWordAndCharacterBoxes(region, 130, out Boxa boxaw, out Boxaa boxaac, null);

        Assert.Equal(2, boxaw.Count);
        Assert.Equal(2, boxaac.Count);
        Assert.Equal(3, boxaac.GetBoxa(0, L.L_CLONE).Count);
        Assert.Equal(2, boxaac.GetBoxa(1, L.L_CLONE).Count);

        /* First character box must be reported in full-image coordinates (~80, 80) */
        boxaac.GetBoxa(0, L.L_CLONE).GetBoxGeometry(0, out int cx, out int cy, out _, out _);
        Assert.InRange(cx, 75, 85);
        Assert.InRange(cy, 75, 85);

        /* Rejects 1 bpp input */
        Assert.Throws<ArgumentException>(() =>
            Pix.Create(64, 64, 1).FindWordAndCharacterBoxes(null, 128, out _, out _, null));
    }

    [Fact]
    public void ExtractSortedPatternAndCompareImagesByBoxes()
    {
        Boxa boxa1 = Boxa.Create(6);
        boxa1.AddBox(Box.Create(20, 30, 40, 16), L.L_INSERT);
        boxa1.AddBox(Box.Create(80, 30, 50, 16), L.L_INSERT);
        boxa1.AddBox(Box.Create(20, 70, 45, 16), L.L_INSERT);
        boxa1.AddBox(Box.Create(85, 70, 55, 16), L.L_INSERT);
        Numa nai = Numa.CreateFromIArray(new[] { 0, 0, 1, 1 }, 4);

        Numaa naa1 = boxa1.ExtractSortedPattern(nai);
        Assert.Equal(2, naa1.Count);
        /* Row 0: y_center = 30 + 8 = 38, then [20, 59, 80, 129] */
        Assert.Equal(new[] { 38, 20, 59, 80, 129 }, naa1.GetNuma(0, L.L_CLONE).GetIArray());
        /* Row 1: y_center = 70 + 8 = 78, then [20, 64, 85, 139] */
        Assert.Equal(new[] { 78, 20, 64, 85, 139 }, naa1.GetNuma(1, L.L_CLONE).GetIArray());

        /* Translated copy (+12, +8) matches within maxshift=(20,20), del=(2,2) */
        Boxa boxa2 = boxa1.Transform(12, 8, 1.0f, 1.0f);
        Numaa naa2 = boxa2.ExtractSortedPattern(nai);
        Assert.True(naa1.CompareImagesByBoxes(naa2, 2, 2, 20, 20, 2, 2, 0));
        Assert.False(naa1.CompareImagesByBoxes(naa2, 2, 2, 5, 5, 2, 2, 0));
        Assert.False(naa1.CompareImagesByBoxes(naa2, 2, 3, 20, 20, 2, 2, 0));

        /* Empty boxa -> empty numaa */
        Assert.Equal(0, Boxa.Create(0).ExtractSortedPattern(Numa.Create(0)).Count);
    }

    [Fact]
    public void DirectoryPipelinesCorrelationRankHausAndWordsInTextlines()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lept_classapp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            string inDir = Path.Combine(dir, "in");
            Directory.CreateDirectory(inDir);
            Pix page = MakeTwoLineWordPage();
            Pix.WritePng(Path.Combine(inDir, "p0.png"), page, 0.0f);
            Pix.WritePng(Path.Combine(inDir, "p1.png"), page, 0.0f);

            string corrRoot = Path.Combine(dir, "corr");
            JbClass.Correlation(inDir, 0.8f, 0.6f, L.JB_CONN_COMPS, corrRoot, 0, 0, 1);
            Assert.True(File.Exists(corrRoot + L.JB_TEMPLATE_EXT));
            Assert.True(File.Exists(corrRoot + L.JB_DATA_EXT));
            Assert.True(File.Exists(corrRoot + ".0000"));
            Assert.True(File.Exists(corrRoot + ".0001"));

            string hausRoot = Path.Combine(dir, "haus");
            JbClass.RankHaus(inDir, 2, 0.97f, L.JB_CONN_COMPS, hausRoot, 0, 0, 1);
            Assert.True(File.Exists(hausRoot + L.JB_TEMPLATE_EXT));
            Assert.True(File.Exists(hausRoot + ".0000"));

            JbClasser wc = JbClass.WordsInTextlines(inDir, 1, 200, 60, 0.8f, 0.6f, out Numa natl, 0, 0);
            Assert.Equal(2, wc.NPages);
            Assert.Equal(8, natl.Count);

            Assert.Throws<ArgumentException>(() =>
                JbClass.Correlation(inDir, 0.8f, 0.6f, 99, corrRoot, 0, 0, 0));
            Assert.Throws<ArgumentException>(() =>
                JbClass.WordsInTextlines(inDir, 3, 200, 60, 0.8f, 0.6f, out _, 0, 0));
        } finally {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }
}
