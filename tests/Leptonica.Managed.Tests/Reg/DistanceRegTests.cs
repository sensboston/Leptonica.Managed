using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// distance_reg.c: pixDistanceFunction() for a variety of usage with all 8
/// combinations of connectivity (4 or 8), dest depth (8 or 16) and
/// boundary condition (L_BOUNDARY_BG or L_BOUNDARY_FG).
/// </summary>
public class DistanceRegTests
{
    [RegFact]
    public void Distance()
    {
        int i, j, k, conn, depth, bc;
        Box box;
        Pix pix, pixs, pixd;
        Pixa pixa;
        var rp = new RegTest("distance");

        pix = TestPaths.ReadInput("feyn.tif");
        box = Box.Create(383, 338, 1480, 1050);
        pixs = pix.ClipRectangle(box, out _)!;
        rp.WritePixAndCheck(pixs, L.IFF_PNG);  /* 0 */

        for (i = 0; i < 2; i++) {
            conn = 4 + 4 * i;
            for (j = 0; j < 2; j++) {
                depth = 8 + 8 * j;
                for (k = 0; k < 2; k++) {
                    bc = k + 1;
                    pixa = Pixa.Create(0);
                    pixa.AddPix(pixs, L.L_COPY);
                    TestDistance(pixa, pixs, conn, depth, bc, rp);
                    pixd = pixa.DisplayTiledInColumns(4, 1.0f, 20, 2);
                }
            }
        }

        Assert.Equal(60, rp.Index);
        rp.Cleanup();
    }

    private static void TestDistance(Pixa pixa, Pix pixs, int conn, int depth, int bc,
                                     RegTest rp)
    {
        Pix pixt1, pixt2, pixt3, pixt4, pixt5;

            /* Test the distance function and display */
        Pix.Invert(pixs, pixs);
        pixt1 = pixs.DistanceFunction(conn, depth, bc);
        rp.WritePixAndCheck(pixt1, L.IFF_PNG);  /* a + 1 */
        pixa.AddPix(pixt1, L.L_INSERT);
        Pix.Invert(pixs, pixs);
        pixt2 = pixt1.MaxDynamicRange(L.L_LOG_SCALE);
        rp.WritePixAndCheck(pixt2, L.IFF_JFIF_JPEG);  /* a + 2 */
        pixa.AddPix(pixt2, L.L_INSERT);

            /* Test the distance function and display with contour rendering */
        Pix.Invert(pixs, pixs);
        pixt1 = pixs.DistanceFunction(conn, depth, bc);
        rp.WritePixAndCheck(pixt1, L.IFF_PNG);  /* a + 3 */
        pixa.AddPix(pixt1, L.L_INSERT);
        Pix.Invert(pixs, pixs);
        pixt2 = pixt1.RenderContours(2, 4, 1);  /* binary output */
        rp.WritePixAndCheck(pixt2, L.IFF_PNG);  /* a + 4 */
        pixa.AddPix(pixt2, L.L_INSERT);
        pixt3 = pixt1.RenderContours(2, 4, depth);
        pixt4 = pixt3.MaxDynamicRange(L.L_LINEAR_SCALE);
        rp.WritePixAndCheck(pixt4, L.IFF_JFIF_JPEG);  /* a + 5 */
        pixa.AddPix(pixt4, L.L_INSERT);
        pixt5 = pixt3.MaxDynamicRange(L.L_LOG_SCALE);
        rp.WritePixAndCheck(pixt5, L.IFF_JFIF_JPEG);  /* a + 6 */
        pixa.AddPix(pixt5, L.L_INSERT);

            /* Label all pixels in each c.c. with a color equal to the
             * max distance of any pixel within that c.c. from the bg.
             * Note that we've normalized so the dynamic range extends
             * to 255.  For the image here, each unit of distance is
             * represented by about 21 grayscale units.  The largest
             * distance is 12.  */
        if (depth == 8) {
            pixt1 = pixs.DistanceFunction(conn, depth, bc);
            pixt4 = pixt1.MaxDynamicRange(L.L_LOG_SCALE);
            rp.WritePixAndCheck(pixt4, L.IFF_JFIF_JPEG);  /* b + 1 */
            pixa.AddPix(pixt4, L.L_INSERT);
            pixt2 = Pix.CreateTemplate(pixt1);
            pixt2.SetMasked(pixs, 255);
            rp.WritePixAndCheck(pixt2, L.IFF_JFIF_JPEG);  /* b + 2 */
            pixa.AddPix(pixt2, L.L_INSERT);
            pixt1.SeedfillGray(pixt2, 4);
            pixt3 = pixt1.MaxDynamicRange(L.L_LINEAR_SCALE);
            rp.WritePixAndCheck(pixt3, L.IFF_JFIF_JPEG);  /* b + 3 */
            pixa.AddPix(pixt3, L.L_INSERT);
        }
    }
}
