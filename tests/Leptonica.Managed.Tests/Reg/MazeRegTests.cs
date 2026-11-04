using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>maze_reg.c: binary and gray maze search.</summary>
public class MazeRegTests
{
    private const int NPATHS = 6;
    private static readonly int[] x0 = {42, 73, 73, 42, 324, 471};
    private static readonly int[] y0 = {117, 319, 319, 117, 170, 201};
    private static readonly int[] x1 = {419, 419, 233, 326, 418, 128};
    private static readonly int[] y1 = {383, 383, 112, 168, 371, 341};

    [RegFact]
    public void Maze()
    {
        int i, w, h;
        Pix pixm, pixg, pixt, pixd;
        Pixa pixa;
        Pixaa paa;
        Pta pta;
        Ptaa ptaa;
        var rp = new RegTest("maze");

        paa = Pixaa.Create(2);

        /* ---------------- Shortest path in binary maze ---------------- */
            /* Generate the maze */
        pixa = Pixa.Create(0);
        pixm = Pix.GenerateBinaryMaze(200, 200, 20, 20, 0.65f, 0.25f);
        pixd = pixm.ExpandBinaryReplicate(3, 3);
        pixa.AddPix(pixd, L.L_INSERT);

            /* Find the shortest path between two points */
        pta = pixm.SearchBinaryMaze(20, 20, 170, 170, out _)!;
        pixt = Pix.DisplayPta(null, pixm, pta);
        pixd = pixt.ScaleBySampling(3.0f, 3.0f);
        pixa.AddPix(pixd, L.L_INSERT);
        paa.AddPixa(pixa, L.L_INSERT);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 0 */

        /* ---------------- Shortest path in gray maze ---------------- */
        pixg = TestPaths.ReadInput("test8.jpg");
        pixg.GetDimensions(out w, out h, out _);
        ptaa = Ptaa.Create(NPATHS);
        for (i = 0; i < NPATHS; i++) {
            if (x0[i] >= w || x1[i] >= w || y0[i] >= h || y1[i] >= h)
                continue;
            pta = pixg.SearchGrayMaze(x0[i], y0[i], x1[i], y1[i], out _);
            ptaa.AddPta(pta, L.L_INSERT);
        }

        pixt = pixg.DisplayPtaa(ptaa);
        pixd = pixt.ScaleBySampling(2.0f, 2.0f);
        pixa = Pixa.Create(0);
        pixa.AddPix(pixd, L.L_INSERT);
        paa.AddPixa(pixa, L.L_INSERT);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 1 */

            /* Bundle it all up */
        pixd = paa.DisplayByPixa(3, 1.0f, 20, 40, 0);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 2 */

        Assert.Equal(2, rp.Index);
        rp.Cleanup();
    }
}
