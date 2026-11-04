using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.RecogTests;

public class BootNumGenTests
{
    [Fact]
    public void Gen1ThroughGen4DecodeLabeledDigitTemplatesAndTrainBootRecognizer()
    {
        Pixa p1 = BootNumGen.Gen1();
        Pixa p2 = BootNumGen.Gen2();
        Pixa p3 = BootNumGen.Gen3();
        Pixa p4 = BootNumGen.Gen4(5);

        Assert.True(p1.Count >= 100);
        Assert.True(p2.Count >= 100);
        Assert.True(p3.Count >= 100);
        Assert.Equal(50, p4.Count);

        Assert.Throws<ArgumentException>(() => BootNumGen.Gen4(0));

        // Build boot digit recognizers both from Gen1..3 (nsamp == 0) and Gen4 (nsamp > 0)
        Recog recogDefault = Recog.MakeBootDigitRecog(0, 40, 0, 1, 0);
        Assert.Equal(10, recogDefault.SetSize);
        Assert.True(recogDefault.TrainDone);

        Recog recogGen4 = Recog.MakeBootDigitRecog(5, 40, 0, 1, 0);
        Assert.Equal(10, recogGen4.SetSize);
        Assert.True(recogGen4.TrainDone);

        // Identify a digit template using the trained boot recognizer
        Pix sample = p4.GetPix(25, L.L_CLONE); // labeled digit "5"
        Assert.Equal("5", sample.Text);
        recogGen4.IdentifyPix(sample);
        Assert.NotNull(recogGen4.Rch);
        recogGen4.Rch!.Extract(out _, out _, out string? text, out _, out _, out _, out _);
        Assert.Equal("5", text);
    }
}
