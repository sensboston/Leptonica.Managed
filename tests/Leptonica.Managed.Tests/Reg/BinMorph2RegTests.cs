using Leptonica;
using Xunit.Abstractions;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// binmorph2_reg.c: composite (comb) Sels of sizes that are not exactly
/// decomposable, against the equivalent brick sequences, including dwa.
/// </summary>
[Collection("MorphBoundaryCondition")]
public class BinMorph2RegTests
{
    private const int MAX_SEL_SIZE = 120;

    private readonly ITestOutputHelper _output;

    public BinMorph2RegTests(ITestOutputHelper output) { _output = output; }

    [RegFact]
    public void BinMorph2()
    {
        Pix pixs = TestPaths.ReadInput("feyn-fract.tif");
        Pix pixsd = pixs.MorphCompSequence("d5.5", 0);
        bool success = true;
        for (int i = 1; i < MAX_SEL_SIZE; i++) {

                /* Check if the size is exactly decomposable */
            Pix.SelectComposableSizes(i, out int factor1, out int factor2);
            int diff = factor1 * factor2 - i;
            _output.WriteLine($"{i}: ({factor1}, {factor2}): {diff}");

                /* Carry out operations on identical sized Sels: dilation,
                 * erosion, opening, closing */
            foreach (char op in new[] { 'd', 'e', 'o', 'c' }) {
                string buffer1 = $"{op}{i + diff}.{i + diff}";
                string buffer2 = $"{op}{i}.{i}";
                Pix pixt1 = pixsd.MorphSequence(buffer1, 0);
                Pix pixt2 = pixsd.MorphCompSequence(buffer2, 0);
                bool same = Pix.Equal(pixt1, pixt2);
                bool same2;
                if (i < 64) {
                    Pix pixt3 = pixsd.MorphCompSequenceDwa(buffer2, 0);
                    same2 = Pix.Equal(pixt1, pixt3);
                } else {
                    same2 = true;
                }
                if (same && same2) {
                    _output.WriteLine($"Sequence {buffer1}: SUCCESS");
                } else {
                    _output.WriteLine($"Sequence {buffer1}: FAILURE");
                    success = false;
                }
            }
        }

        Assert.True(success, "Failure: error(s) found");
    }
}
