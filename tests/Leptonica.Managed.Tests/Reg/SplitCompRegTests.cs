using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// splitcomp_reg.c: splittings of a single component and of an image composed of
/// several components, using different components and parameters.
/// </summary>
public class SplitCompRegTests
{
    [RegFact]
    public void SplitComp()
    {
        int i, j, w, h;
        int[] minsum = { 2, 40, 50, 50, 70 };
        int[] skipdist = { 5, 5, 10, 10, 30 };
        int[] delta = { 2, 10, 10, 25, 40 };
        int[] maxbg = { 10, 15, 10, 20, 40 };
        Box box1, box2, box3, box4;
        Boxa boxa;
        Pix pixs, pixc, pixt, pixd, pix32;
        Pixa pixas, pixad;
        var rp = new RegTest("splitcomp");

            /* Generate and save 1 bpp masks */
        pixas = Pixa.Create(0);
        pixs = Pix.Create(300, 250, 1);
        pixs.SetAll();
        box1 = Box.Create(50, 0, 140, 25);
        box2 = Box.Create(120, 100, 100, 25);
        box3 = Box.Create(75, 170, 80, 20);
        box4 = Box.Create(150, 80, 25, 70);

        pixs.ClearInRect(box1);
        pixas.AddPix(pixs, L.L_COPY);
        pixt = pixs.RotateOrth(1);
        pixas.AddPix(pixt, L.L_INSERT);

        pixs.ClearInRect(box2);
        pixas.AddPix(pixs, L.L_COPY);
        pixt = pixs.RotateOrth(1);
        pixas.AddPix(pixt, L.L_INSERT);

        pixs.ClearInRect(box3);
        pixas.AddPix(pixs, L.L_COPY);
        pixt = pixs.RotateOrth(1);
        pixas.AddPix(pixt, L.L_INSERT);

        pixs.ClearInRect(box4);
        pixas.AddPix(pixs, L.L_COPY);
        pixt = pixs.RotateOrth(1);
        pixas.AddPix(pixt, L.L_INSERT);

            /* Do 5 splittings on each of the 8 masks */
        pixad = Pixa.Create(0);
        for (j = 0; j < 8; j++) {
            pixt = pixas.GetPix(j, L.L_CLONE);
            pixt.GetDimensions(out w, out h, out _);
            pix32 = Pix.Create(w, h, 32);
            pix32.SetAll();
            pix32.PaintThroughMask(pixt, 0, 0, 0xc0c0c000);
            pixad.AddPix(pix32, L.L_INSERT);
            for (i = 0; i < 5; i++) {
                pixc = Pix.Copy(null, pix32);
                boxa = pixt.SplitComponentIntoBoxa(null, minsum[i], skipdist[i],
                                                   delta[i], maxbg[i], 0, 1);
                pixd = pixc.BlendBoxaRandom(boxa, 0.4f);
                pixd.RenderBoxaArb(boxa, 2, 255, 0, 0);
                pixad.AddPix(pixd, L.L_INSERT);
            }
        }

            /* Display results */
        pixd = pixad.DisplayTiledInColumns(6, 1.0f, 30, 0);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 0 */

            /* Put the 8 masks all together, and split 5 ways */
        pixad = Pixa.Create(0);
        pixs = pixas.DisplayOnLattice(325, 325, out _, out _);
        pixs.GetDimensions(out w, out h, out _);
        pix32 = Pix.Create(w, h, 32);
        pix32.SetAll();
        pix32.PaintThroughMask(pixs, 0, 0, 0xc0c0c000);
        pixad.AddPix(pix32, L.L_INSERT);
        for (i = 0; i < 5; i++) {
            pixc = Pix.Copy(null, pix32);
            boxa = pixs.SplitIntoBoxa(minsum[i], skipdist[i], delta[i], maxbg[i], 0, 1);
            pixd = pixc.BlendBoxaRandom(boxa, 0.4f);
            pixd.RenderBoxaArb(boxa, 2, 255, 0, 0);
            pixad.AddPix(pixd, L.L_INSERT);
        }

            /* Display results */
        pixd = pixad.DisplayTiledInColumns(6, 1.0f, 30, 0);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 1 */

        Assert.Equal(1, rp.Index);
        rp.Cleanup();
    }
}
