using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// logicops_reg.c: pixInvert, pixOr, pixAnd, pixXor and pixSubtract with
/// new, existing and in-place destinations.
/// </summary>
public class LogicOpsRegTests
{
    private delegate Pix LogicOp(Pix? pixd, Pix pixs1, Pix pixs2);

    [RegFact]
    public void LogicOps()
    {
        Pix pix1, pix2, pix3, pix4;
        var rp = new RegTest("logicops");

        Pix pixs = TestPaths.ReadInput("test1.png");

            /* pixInvert */
        pix1 = Pix.Invert(null, pixs);
        pix2 = Pix.CreateTemplate(pixs);  /* into pixd of same size */
        Pix.Invert(pix2, pixs);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 0 */
        rp.ComparePix(pix1, pix2);  /* 1 */

        pix3 = TestPaths.ReadInput("marge.jpg");  /* into pixd of different size */
        Pix.Invert(pix3, pixs);
        rp.ComparePix(pix1, pix3);  /* 2 */

        pix1 = Pix.OpenBrick(null, pixs, 1, 9);
        pix2 = Pix.DilateBrick(null, pixs, 1, 9);

            /* pixOr */
        Check(rp, pixs, pix1, Pix.Or, false);  /* 3 - 5 */
        Check(rp, pixs, pix2, Pix.Or, false);  /* 6 - 8 */

            /* pixAnd */
        Check(rp, pixs, pix1, Pix.And, false);  /* 9 - 11 */
        Check(rp, pixs, pix2, Pix.And, false);  /* 12 - 14 */

            /* pixXor */
        Check(rp, pixs, pix1, Pix.Xor, false);  /* 15 - 17 */
        Check(rp, pixs, pix2, Pix.Xor, false);  /* 18 - 20 */

            /* pixSubtract */
        Check(rp, pixs, pix1, Pix.Subtract, true);  /* 21 - 23 */
        Check(rp, pixs, pix2, Pix.Subtract, true);  /* 24 - 26 */

        pix4 = TestPaths.ReadInput("marge.jpg");
        Pix.Subtract(pix4, pixs, pixs);  /* subtract from itself; should be empty */
        pix3 = Pix.CreateTemplate(pixs);
        rp.ComparePix(pix3, pix4);  /* 27*/

        Pix.Subtract(pixs, pixs, pixs);  /* subtract from itself; should be empty */
        pix3 = Pix.CreateTemplate(pixs);
        rp.ComparePix(pix3, pixs);  /* 28*/

        Assert.Equal(28, rp.Index);
        rp.Cleanup();
    }

        /* Three checks: existing destination (written and checked against
         * the golden), new destination, in-place destination.  For
         * pixSubtract(), the in-place destination is the second source. */
    private static void Check(RegTest rp, Pix pixs, Pix pixo, LogicOp op, bool subtract)
    {
        Pix pix3 = Pix.CreateTemplate(pixs);
        op(pix3, pixs, pixo);  /* existing */
        Pix pix4 = op(null, pixs, pixo);  /* new */
        rp.WritePixAndCheck(pix3, L.IFF_PNG);
        rp.ComparePix(pix3, pix4);
        Pix.Copy(pix4, pixo);
        if (subtract)
            op(pix4, pixs, pix4);  /* in-place */
        else
            op(pix4, pix4, pixs);  /* in-place */
        rp.ComparePix(pix3, pix4);
    }
}
