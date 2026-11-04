using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.RecogTests;

public class RecogIdentTests
{
    [Fact]
    public void ProcessToIdentifyAndExtractNumbersWork()
    {
        Recog recog = Recog.Create(0, 0, 0, 128, 1);
        Pix charImg = Pix.Create(30, 30, 8);
        charImg.SetAllArbitrary(255);
        for (int y = 5; y < 25; y++)
            for (int x = 10; x < 16; x++)
                charImg.SetPixel(x, y, 0);

        Pix? cropped = recog.ProcessToIdentify(charImg, 4);
        Assert.NotNull(cropped);
        Assert.Equal(1, cropped!.Depth);
        Assert.Equal(6 + 8, cropped.Width);
        Assert.Equal(20, cropped.Height);

        recog.SkipIdentify();
        Assert.NotNull(recog.Rch);
        recog.Rch!.Extract(out int idx, out float score, out string? txt, out _, out _, out _, out _);
        Assert.Equal(0, idx);
        Assert.Equal(0.0f, score);
        Assert.Equal(string.Empty, txt);
    }
}
