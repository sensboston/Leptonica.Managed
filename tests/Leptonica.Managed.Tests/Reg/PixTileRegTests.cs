using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>pixtile_reg.c: tiling an image with and without overlap, and reassembling it.</summary>
public class PixTileRegTests
{
    [RegFact]
    public void PixTile()
    {
        Pix pixs, pixd;

        Pix.LeptDebugOK = true;
        pixs = TestPaths.ReadInput("test24.jpg");
        pixd = Pix.CreateTemplateNoInit(pixs);

        TestTiling(pixd, pixs, 1, 1, 0, 0, 183, 83);
        TestTiling(pixd, pixs, 0, 1, 60, 0, 30, 20);
        TestTiling(pixd, pixs, 1, 0, 0, 60, 40, 40);
        TestTiling(pixd, pixs, 0, 0, 27, 31, 27, 31);
        TestTiling(pixd, pixs, 0, 0, 400, 400, 40, 20);
        TestTiling(pixd, pixs, 7, 9, 0, 0, 35, 35);
        TestTiling(pixd, pixs, 0, 0, 27, 31, 0, 0);
        TestTiling(pixd, pixs, 7, 9, 0, 0, 0, 0);
    }

        /* The C version only prints "Tiling OK" or "Tiling ERROR !"; here the
         * result is asserted. */
    private static void TestTiling(Pix pixd, Pix pixs, int nx, int ny, int w, int h,
                                   int xoverlap, int yoverlap)
    {
        int i, j;
        Pix pixt;
        Leptonica.PixTiling pt;

        pixd.ClearAll();
        pt = Leptonica.PixTiling.Create(pixs, nx, ny, w, h, xoverlap, yoverlap);
        pt.GetCount(out nx, out ny);
        pt.GetSize(out w, out h);

        for (i = 0; i < ny; i++) {
            for (j = 0; j < nx; j++) {
                pixt = pt.GetTile(i, j);
                pt.PaintTile(pixd, i, j, pixt);
            }
        }
        Assert.True(Pix.Equal(pixs, pixd),
                    $"Tiling ERROR: nx,ny = {nx},{ny}; w,h = {w},{h}; " +
                    $"overlap = {pt.XOverlap},{pt.YOverlap}");
    }
}
