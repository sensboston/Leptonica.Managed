using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.RecogTests;

public class RecogBasicTests
{
    [Fact]
    public void CreateParamsAndClassLookupRoundTrip()
    {
        Recog recog = Recog.Create(0, 40, 0, 128, 1);
        Assert.Equal(0, recog.Count);
        Assert.Equal(40, recog.ScaleH);
        Assert.Equal(128, recog.Threshold);
        Assert.Equal(1, recog.MaxYShift);
        Assert.Equal(L.L_ARABIC_NUMERALS, recog.CharsetType);
        Assert.Equal(10, recog.CharsetSize);

        recog.SetParams(L.L_LC_ALPHA, 2, 3.5f, 2.2f);
        Assert.Equal(L.L_LC_ALPHA, recog.CharsetType);
        Assert.Equal(26, recog.CharsetSize);
        Assert.Equal(2, recog.MinNopad);

        int vA = Recog.ConvertCharstrToInt("a");
        Assert.Equal((int)'a', vA);
        Assert.Equal(1, recog.GetClassIndex(vA, "a", out int idxA));
        Assert.Equal(0, idxA);
        Assert.Equal(0, recog.GetClassIndex(vA, "a", out int idxA2));
        Assert.Equal(0, idxA2);
        Assert.Equal(1, recog.Count);

        int vUmlaut = Recog.ConvertCharstrToInt("ä"); /* 2-byte UTF-8: 0xC3 0xA4 */
        Assert.Equal((0xC3 << 8) + 0xA4, vUmlaut);
        Assert.Equal(1, recog.GetClassIndex(vUmlaut, "ä", out int idxU));
        Assert.Equal(1, idxU);

        Assert.Equal("a", recog.GetClassString(0));
        Assert.Equal("ä", recog.GetClassString(1));
        Assert.True(recog.StringToIndex("ä", out int foundIdx));
        Assert.Equal(1, foundIdx);
        Assert.False(recog.StringToIndex("z", out int missingIdx));
        Assert.Equal(-1, missingIdx);

        Assert.Throws<ArgumentException>(() => Recog.ConvertCharstrToInt(""));
        Assert.Throws<ArgumentException>(() => Recog.ConvertCharstrToInt("abcde"));
    }
}
