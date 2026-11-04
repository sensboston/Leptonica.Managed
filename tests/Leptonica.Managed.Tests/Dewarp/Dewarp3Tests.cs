using System.IO;
using Leptonica;
using Leptonica.Managed.Tests.Reg;
using Xunit;

namespace Leptonica.Managed.Tests.DewarpTests;

public class Dewarp3Tests
{
    [RegFact]
    public void PopulateFullResAndMinimizeRoundTrip()
    {
        Dewarp dew = Dewarp.Read(Path.Combine(TestPaths.GoldenDir!, "dewarp_golden.06.dew"));
        Assert.Equal(0, dew.PopulateFullRes(null, 0, 0));
        Assert.NotNull(dew.FullVDispar);
        Assert.NotNull(dew.FullHDispar);
        dew.FullVDispar!.GetDimensions(out int fw, out int fh);
        Assert.True(fw >= dew.Width);
        Assert.True(fh >= dew.Height);

        Assert.Equal(0, dew.Minimize());
        Assert.Null(dew.FullVDispar);
        Assert.Null(dew.FullHDispar);
        Assert.NotNull(dew.SampVDispar);
        Assert.NotNull(dew.SampHDispar);
    }
}
