namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// dwamorph1_reg.c: the linear brick dwa operations (dilation, erosion, opening,
/// closing) against the rasterop versions, with both boundary conditions.
/// </summary>
public class DwaMorph1RegTests
{
    [RegFact(Skip = "needs pixMorphDwa_3 (prog/dwalinear.3.c)")]
    public void DwaMorph1()
    {
        /* The test compares pixDilate/pixErode/pixOpen/pixCloseSafe of every sel of
         * selaAddDwaLinear() with pixMorphDwa_3(), which is not part of the library
         * (generated code in prog/dwalinear.3.c, dwalinearlow.3.c). */
    }
}
