using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// checkerboard_reg.c: the function that locates corners where four checkerboard
/// squares are joined.
/// </summary>
public class CheckerboardRegTests
{
    [RegFact]
    public void Checkerboard()
    {
        var rp = new RegTest("checkerboard");

        LocateCheckerboardCorners(rp, "checkerboard1.tif", 2);
        LocateCheckerboardCorners(rp, "checkerboard2.tif", 4);

        Assert.Equal(5, rp.Index);
        rp.Cleanup();
    }

    private static void LocateCheckerboardCorners(RegTest rp, string fname, int nsels)
    {
        int w, h;
        Pix pix1, pix2, pix3, pix4;
        Pixa pixa1;
        Pta pta1;

        pix1 = TestPaths.ReadInput(fname);
        pixa1 = Pixa.Create(0);
        pix1.FindCheckerboardCorners(15, 3, nsels, out pix2, out pta1, pixa1);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);    /* 0, 3 */
        pix3 = pixa1.DisplayTiledInColumns(1, 1.0f, 20, 2);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 1, 4 */
        pix1.GetDimensions(out w, out h, out _);
        pix4 = Pix.GenerateFromPta(pta1, w, h);
        Pix.DilateBrick(pix4, pix4, 5, 5);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 2, 5 */
    }
}
