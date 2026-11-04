using System.IO;
using Leptonica;
using Leptonica.Managed.Tests.Reg;
using Xunit;

namespace Leptonica.Managed.Tests.DewarpTests;

public class Dewarp1Tests
{
    [RegFact]
    public void GoldenDewarpFileRoundTripsByteIdentically()
    {
        string goldenPath = Path.Combine(TestPaths.GoldenDir!, "dewarp_golden.06.dew");
        byte[] expected = File.ReadAllBytes(goldenPath);

        Dewarp dew = Dewarp.Read(goldenPath);
        Assert.Equal(7, dew.PageNo);
        Assert.Equal(944, dew.Width);
        Assert.Equal(1472, dew.Height);
        Assert.True(dew.VSuccess);
        Assert.True(dew.HSuccess);
        Assert.NotNull(dew.SampVDispar);
        Assert.NotNull(dew.SampHDispar);

        byte[] actual = dew.WriteMem();
        Assert.Equal(expected, actual);

        Dewarp dew2 = Dewarp.ReadMem(actual);
        Assert.Equal(expected, dew2.WriteMem());
    }
}
