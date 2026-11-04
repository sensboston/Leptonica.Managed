using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Text;

public class TextOpsTests
{
    private static readonly L_Bmf bmf = L_Bmf.Create(null, 8);

    [Fact]
    public void StringWidthAddsKernAndSkipsLast()
    {
        int wa = bmf.GetWidth('a');
        int wb = bmf.GetWidth('b');
        Assert.Equal(wa, bmf.GetStringWidth("a"));
        Assert.Equal(wa + wb + bmf.KernWidth, bmf.GetStringWidth("ab"));
        Assert.Equal(-bmf.KernWidth, bmf.GetStringWidth(""));
    }

    [Fact]
    public void WordWidthsMatchStringWidths()
    {
        Sarray words = Sarray.CreateWordsFromString("the quick brown fox");
        Numa na = bmf.GetWordWidths("the quick brown fox", words);
        Assert.Equal(4, na.Count);
        Assert.Equal(bmf.GetStringWidth("quick"), na.GetIValue(1));
    }

    [Fact]
    public void LineStringsWrapWithinMaxWidth()
    {
        string text = "one two three four five six seven eight nine ten";
        int maxw = bmf.GetStringWidth("one two three four");
        Sarray sa = bmf.GetLineStrings(text, maxw, 0, out int h);
        Assert.True(sa.Count > 1);
        Assert.Equal(sa.Count * bmf.LineHeight + (sa.Count - 1) * bmf.VertLineSep, h);
        var lines = new System.Collections.Generic.List<string>();
        for (int i = 0; i < sa.Count; i++)
            lines.Add(sa.GetString(i, L.L_NOCOPY));
        Assert.Equal(text, string.Join(" ", lines).TrimEnd());  // the last line keeps its trailing space, as in C
    }

    [Fact]
    public void LineStringsEmptyTextThrows()
    {
        Assert.Throws<System.ArgumentException>(() => bmf.GetLineStrings("", 100, 0, out _));
    }

    [Fact]
    public void SetTextlinePaintsAndReportsWidthAndOverflow()
    {
        Pix pix = Pix.Create(200, 30, 8);
        pix.SetAll();
        pix.SetTextline(bmf, "Hello", 0, 5, 20, out int width, out int overflow);
        Assert.Equal(bmf.GetStringWidth("Hello"), width);
        Assert.Equal(0, overflow);
        bool anyDark = false;
        for (int i = 0; i < 30 && !anyDark; i++)
            for (int j = 0; j < 200 && !anyDark; j++)
                if (pix.GetPixel(j, i) == 0) anyDark = true;
        Assert.True(anyDark);

        Pix narrow = Pix.Create(10, 30, 8);
        narrow.SetTextline(bmf, "Hello", 0, 0, 20, out _, out overflow);
        Assert.Equal(1, overflow);
    }

    [Fact]
    public void AddTextlinesGrowsTheImage()
    {
        Pix pix = Pix.Create(100, 50, 32);
        pix.SetAll();
        Pix above = pix.AddTextlines(bmf, "one\ntwo", 0xff000000, L.L_ADD_ABOVE);
        Assert.Equal(100, above.Width);
        Assert.True(above.Height > 50);
        Pix left = pix.AddTextlines(bmf, "one\ntwo", 0xff000000, L.L_ADD_LEFT);
        Assert.Equal(50, left.Height);
        Assert.True(left.Width > 100);
        /* no font: a copy */
        Pix copy = pix.AddTextlines(null, "one", 0xff000000, L.L_ADD_BELOW);
        Assert.True(Pix.Equal(pix, copy));
        Assert.Throws<System.ArgumentException>(() => pix.AddTextlines(bmf, "x", 0, L.L_ADD_AT_TOP));
    }

    [Fact]
    public void AddSingleTextblockLocations()
    {
        Pix pix = Pix.Create(300, 60, 8);
        pix.SetAll();
        Pix below = pix.AddSingleTextblock(bmf, "A short text block", 0, L.L_ADD_BELOW, out int ovf);
        Assert.Equal(0, ovf);
        Assert.True(below.Height > 60);
        Pix over = pix.AddSingleTextblock(bmf, "A short text block", 0, L.L_ADD_AT_TOP, out ovf);
        Assert.Equal((300, 60), (over.Width, over.Height));
        Pix tiny = Pix.Create(300, 8, 8);
        tiny.SetAll();
        tiny.AddSingleTextblock(bmf, "A short text block", 0, L.L_ADD_AT_BOT, out ovf);
        Assert.Equal(1, ovf);
        Pix none = pix.AddSingleTextblock(bmf, null, 0, L.L_ADD_BELOW, out _);
        Assert.True(Pix.Equal(pix, none));
    }

    [Fact]
    public void PixaAddTextFunctions()
    {
        Pixa pixa = Pixa.Create(2);
        for (int i = 0; i < 2; i++) {
            Pix pix = Pix.Create(80, 40, 32);
            pix.SetAll();
            pix.Text = $"label {i}";
            pixa.AddPix(pix, L.L_INSERT);
        }
        Pixa numbered = pixa.AddTextNumber(bmf, null, 0xff000000, L.L_ADD_ABOVE);
        Assert.Equal(2, numbered.Count);
        Assert.True(numbered.GetPix(0, L.L_CLONE).Height > 40);
        Pixa labeled = pixa.AddTextlines(bmf, null, 0xff000000, L.L_ADD_RIGHT);
        Assert.True(labeled.GetPix(1, L.L_CLONE).Width > 80);

        Pixa dest = Pixa.Create(0);
        dest.AddPixWithText(pixa.GetPix(0, L.L_CLONE), 1, bmf, "xyz", 0xff000000, L.L_ADD_BELOW);
        Assert.Equal(1, dest.Count);
        Assert.True(dest.GetPix(0, L.L_CLONE).Height > 40);
    }

    [Fact]
    public void SplitStringToParagraphs()
    {
        string text = "first line\nsecond line\n\nthird line\n   indented\n";
        Sarray blank = Pix.SplitStringToParagraphs(text, L.SPLIT_ON_BLANK_LINE);
        Assert.Equal(2, blank.Count);
        Sarray lead = Pix.SplitStringToParagraphs(text, L.SPLIT_ON_LEADING_WHITE);
        Assert.Equal(2, lead.Count);
        Sarray both = Pix.SplitStringToParagraphs(text, L.SPLIT_ON_BOTH);
        Assert.Equal(3, both.Count);
    }
}
