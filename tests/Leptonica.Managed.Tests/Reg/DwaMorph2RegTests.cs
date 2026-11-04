namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// dwamorph2_reg.c: timings of the binary morphological implementations.
/// </summary>
public class DwaMorph2RegTests
{
    [RegFact(Skip = "needs pixMorphDwa_3 (prog/dwalinear.3.c); only timings and gnuplot output, no golden check")]
    public void DwaMorph2()
    {
        /* The C test only times pixDilate, pixDilateCompBrick, pixMorphDwa_3 and
         * pixDilateCompBrickDwa (and the same for erosion, opening, closing) and
         * writes gnuplot graphs; it has no checks. */
    }
}
