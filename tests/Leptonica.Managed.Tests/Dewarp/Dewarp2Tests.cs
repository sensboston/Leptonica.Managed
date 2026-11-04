using System.IO;
using Leptonica;
using Leptonica.Managed.Tests.Reg;
using Xunit;

namespace Leptonica.Managed.Tests.DewarpTests;

public class Dewarp2Tests
{
    [RegFact]
    public void BuildPageModelMatchesGoldenDewarpFileByteForByte()
    {
        Pix pixb = Pix.Read(Path.Combine(TestPaths.GoldenDir!, "dewarp_golden.00.png"));

        Ptaa? ptaa1 = Dewarp.GetTextlineCenters(pixb, 0);
        Assert.NotNull(ptaa1);
        Assert.True(ptaa1!.Count >= 15);

        Ptaa ptaa2 = Dewarp.RemoveShortLines(pixb, ptaa1, 0.8f, 0);
        Assert.True(ptaa2.Count >= 15);

        Dewarpa dewa = Dewarpa.Create(2, 30, 1, 15, 30);
        Dewarp dew = Dewarp.Create(pixb, 7);
        dewa.UseBothArrays(1);
        dewa.InsertDewarp(dew);
        Assert.Equal(0, dew.BuildPageModel(null));
        Assert.Equal(0, dewa.ModelStatus(7, out int vsuccess, out int hsuccess));
        Assert.Equal(1, vsuccess);
        Assert.Equal(1, hsuccess);

        byte[] expectedDew = File.ReadAllBytes(Path.Combine(TestPaths.GoldenDir!, "dewarp_golden.06.dew"));
        Assert.Equal(expectedDew, dew.WriteMem());
    }
}
