using System;
using System.IO;
using Leptonica;
using Leptonica.Managed.Tests.Reg;

namespace Leptonica.Managed.Tests;

/// <summary>Tests of bmf.c.</summary>
public class BmfTests
{
    private static readonly int[] Sizes = { 4, 6, 8, 10, 12, 14, 16, 18, 20 };

    private static string FontsDir => Path.Combine(TestPaths.ProgDir, "fonts");

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(14)]
    [InlineData(16)]
    [InlineData(18)]
    [InlineData(20)]
    public void CreateFromString_Has95CharsAndConsistentTables(int fontsize)
    {
        L_Bmf bmf = L_Bmf.Create(null, fontsize);
        Assert.Equal(fontsize, bmf.Size);
        Assert.Null(bmf.Directory);
        Assert.Equal(95, bmf.Pixa.Count);
        Assert.Equal(128, bmf.FontTab.Length);

        /* The baselines found in the image are those stored in the pixa files */
        Pixa.GetFont("nonexistent", fontsize, out int bl1, out int bl2, out int bl3);
        Assert.Equal((bl1, bl2, bl3), (bmf.Baseline1, bmf.Baseline2, bmf.Baseline3));

        for (int c = 32; c < 127; c++) {
            Pix pix = bmf.GetPix((char)c)!;
            Assert.Equal(1, pix.Depth);
            Assert.Equal(pix.Width, bmf.GetWidth((char)c));
            Assert.True(bmf.GetBaseline((char)c) > 0);
        }
        Assert.Equal(bmf.Baseline1, bmf.GetBaseline(' '));
        Assert.Equal(bmf.Baseline1, bmf.GetBaseline('0'));
        Assert.Equal(bmf.Baseline2, bmf.GetBaseline('A'));
        Assert.Equal(bmf.Baseline1, bmf.GetBaseline('\\'));
        Assert.Equal(bmf.Baseline3, bmf.GetBaseline('a'));

        /* The space is blank and twice as wide as '!' */
        Assert.Equal(2 * bmf.GetWidth('!'), bmf.GetWidth(' '));
        Assert.Equal(0, bmf.GetPix(' ')!.CountPixels(null));
        Assert.Equal(bmf.SpaceWidth, bmf.GetWidth(' '));

        /* '\' is the left-right flip of '/' */
        Pix slash = bmf.GetPix('/')!;
        Pix flipped = Pix.FlipLR(null, slash);
        Assert.True(Pix.Equal(flipped, bmf.GetPix('\\')!));

        int maxh = Math.Max(bmf.GetPix(' ')!.Height, Math.Max(bmf.GetPix('A')!.Height, bmf.GetPix('a')!.Height));
        Assert.Equal(maxh, bmf.LineHeight);
        Assert.True(bmf.KernWidth >= 1);
        Assert.Equal((int)(0.3f * bmf.LineHeight + 0.5), bmf.VertLineSep);
    }

    [Fact]
    public void Accessors_HandleNewlineAndInvalidChars()
    {
        L_Bmf bmf = L_Bmf.Create(null, 8);
        Assert.Null(bmf.GetPix('\n'));
        Assert.Equal(-1, bmf.GetWidth('\n'));
        Assert.Equal(0, bmf.GetBaseline('\n'));
        Assert.Throws<ArgumentException>(() => bmf.GetPix('\t'));
        Assert.Throws<ArgumentException>(() => bmf.GetWidth((char)127));
        Assert.Throws<ArgumentException>(() => bmf.GetBaseline((char)31));
        Assert.Throws<ArgumentException>(() => L_Bmf.Create(null, 7));
        Assert.Throws<ArgumentException>(() => L_Bmf.Create(null, 22));
    }

    [Fact]
    public void LargerFontsAreLarger()
    {
        int prev = 0;
        foreach (int size in Sizes) {
            L_Bmf bmf = L_Bmf.Create(null, size);
            Assert.True(bmf.LineHeight > prev);
            prev = bmf.LineHeight;
        }
    }

    [Fact]
    public void CreateFromDirectory_ReadsThePrecomputedPixa()
    {
        if (!File.Exists(Path.Combine(FontsDir, "chars-10.pa")))
            return;   /* testdata/prog/fonts not available */
        L_Bmf bmf = L_Bmf.Create(FontsDir, 10);
        Assert.Equal(FontsDir, bmf.Directory);
        Assert.Equal(95, bmf.Pixa.Count);
        Assert.Equal((30, 30, 30), (bmf.Baseline1, bmf.Baseline2, bmf.Baseline3));
        Assert.Equal(2 * bmf.GetWidth('!'), bmf.GetWidth(' '));
        Assert.True(bmf.GetWidth('W') > bmf.GetWidth('i'));
    }

    [Fact]
    public void CreateFromMissingDirectory_FallsBackToTheString()
    {
        L_Bmf bmf = L_Bmf.Create(Path.Combine(FontsDir, "no-such-dir"), 6);
        Assert.Equal(95, bmf.Pixa.Count);
        Assert.Equal(L_Bmf.Create(null, 6).GetWidth('m'), bmf.GetWidth('m'));
    }

    [Fact]
    public void GetFont_ReportsMissingFilesAndBadSizes()
    {
        Assert.Null(Pixa.GetFont("no-such-dir", 12, out int bl1, out _, out _));
        Assert.Equal(36, bl1);
        Assert.Throws<ArgumentException>(() => Pixa.GetFont("x", 3, out _, out _, out _));
        Assert.Throws<ArgumentException>(() => Pixa.GetFont("x", 22, out _, out _, out _));
    }

    [Fact]
    public void SaveFont_WritesAPixaThatGetFontReads()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bmftest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            Pixa.SaveFont(null, dir, 6);
            Pixa? pixa = Pixa.GetFont(dir, 6, out int bl1, out _, out _);
            Assert.NotNull(pixa);
            Assert.Equal(95, pixa!.Count);
            Assert.Equal(18, bl1);
            L_Bmf bmf = L_Bmf.Create(dir, 6);
            Assert.Equal(L_Bmf.Create(null, 6).GetWidth('W'), bmf.GetWidth('W'));
            Assert.Throws<ArgumentException>(() => Pixa.SaveFont(null, dir, 5));
        } finally {
            Directory.Delete(dir, true);
        }
    }
}
