using System.IO;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>pixa2_reg.c: various replacement functions on pixa.</summary>
public class Pixa2RegTests
{
    [RegFact]
    public void Pixa2()
    {
        string name;
        int i, n;
        Box box;
        Pix pix0, pix1, pixd;
        Pixa pixa;
        Sarray sa1, sa2, sa3, sa4;
        var rp = new RegTest("pixa2");

        /* ----------------  Find all the jpg and tif images --------------- */
        sa1 = Sarray.GetSortedPathnamesInDirectory(TestPaths.ProgDir, ".jpg", 0, 0);
        sa2 = Sarray.GetSortedPathnamesInDirectory(TestPaths.ProgDir, ".tif", 0, 0);
        sa3 = sa1.SelectRange(10, 19);
        sa4 = sa2.SelectRange(10, 19);
        sa3.Join(sa4);
        n = sa3.Count;

        /* ---------------- Use replace to fill up a pixa -------------------*/
        pixa = Pixa.Create(1);
        pixa.ExtendArrayToSize(n);
        pix0 = TestPaths.ReadInput("marge.jpg");
        pix1 = pix0.ScaleToSize(144, 108);  /* scale 0.25 */
        pixa.InitFull(pix1, null);  /* fill it up */
        pixd = pixa.DisplayTiledInRows(32, 1000, 1.0f, 0, 25, 2);
        Assert.NotNull(pixd);

        /* ---------------- And again with jpgs and tifs -------------------*/
        for (i = 0; i < n; i++) {
            name = Path.GetFileName(sa3.GetString(i, L.L_NOCOPY));
            pix0 = TestPaths.ReadInput(name);
            pix1 = pix0.ScaleToSize(144, 108);
            pixa.ReplacePix(i, pix1, null);
        }
        pixd = pixa.DisplayTiledInRows(32, 1000, 1.0f, 0, 25, 2);
        Assert.NotNull(pixd);

        /* ---------------- And again, reversing the order ------------------*/
        box = Box.Create(0, 0, 0, 0);
        pixa.InitFull(null, box);
        for (i = 0; i < n; i++) {
            name = Path.GetFileName(sa3.GetString(i, L.L_NOCOPY));
            pix0 = TestPaths.ReadInput(name);
            pix1 = pix0.ScaleToSize(144, 108);
            pixa.ReplacePix(n - 1 - i, pix1, null);
        }
        pixd = pixa.DisplayTiledInRows(32, 1000, 1.0f, 0, 25, 2);
        Assert.NotNull(pixd);
        rp.Cleanup();
    }
}
