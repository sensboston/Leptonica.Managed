using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>smoothedge_reg.c: analyzes edges of a 1 bpp (connected component) image for smoothness.</summary>
public class SmoothEdgeRegTests
{
    private const int MIN_JUMP = 2;
    private const int MIN_REVERSAL = 3;

    [RegFact]
    public void SmoothEdge()
    {
        int w;
        Pix pixs, pixt, pixd;
        Pixa pixa;

        Pix.LeptDebugOK = true;
        pixs = TestPaths.ReadInput("raggededge.png");
        w = pixs.Width;
        pixa = Pixa.Create(0);
        PixAddEdgeData(pixa, pixs, L.L_FROM_RIGHT, MIN_JUMP, MIN_REVERSAL);
        PixAddEdgeData(pixa, pixs, L.L_FROM_LEFT, MIN_JUMP, MIN_REVERSAL);
        pixt = pixs.RotateOrth(1);
        PixAddEdgeData(pixa, pixt, L.L_FROM_BOT, MIN_JUMP, MIN_REVERSAL);
        PixAddEdgeData(pixa, pixt, L.L_FROM_TOP, MIN_JUMP, MIN_REVERSAL);
        pixt = pixs.RotateOrth(2);
        PixAddEdgeData(pixa, pixt, L.L_FROM_LEFT, MIN_JUMP, MIN_REVERSAL);
        PixAddEdgeData(pixa, pixt, L.L_FROM_RIGHT, MIN_JUMP, MIN_REVERSAL);
        pixt = pixs.RotateOrth(3);
        PixAddEdgeData(pixa, pixt, L.L_FROM_TOP, MIN_JUMP, MIN_REVERSAL);
        PixAddEdgeData(pixa, pixt, L.L_FROM_BOT, MIN_JUMP, MIN_REVERSAL);

            /* Display at 2x scaling */
        pixd = pixa.DisplayTiledAndScaled(32, 2 * (w + 10), 2, 0, 25, 2);
        Assert.NotNull(pixd);
        Assert.Equal(8, pixa.Count);
    }

    private static void PixAddEdgeData(Pixa pixa, Pix pixs, int side, int minjump, int minreversal)
    {
        Pix pixt1, pixt2;

        pixs.MeasureEdgeSmoothness(side, minjump, minreversal, out _, out _, out _,
                                   "/tmp/junkedge.png");
        pixt1 = Pix.Read(Utils.GenPathname("/tmp/junkedge.png", null));
        pixt2 = pixt1.AddBorder(10, 0);  /* 10 pixel white border */
        pixa.AddPix(pixt2, L.L_INSERT);
    }
}
