using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// paint_reg.c: regression test for painting on images of various types/depths
/// and painting through masks.
/// </summary>
[Collection("ConvolveSampling")]
public class PaintRegTests
{
    [RegFact]
    public void Paint()
    {
        var rp = new RegTest("paint");
        Pixa pixa = Pixa.Create(0);

            /* Color non-white pixels on RGB */
        Pix pixs = TestPaths.ReadInput("lucasta-frag.jpg");
        Pix pixt = pixs.Convert8To32();
        Box box = Box.Create(120, 30, 200, 200);
        pixt.ColorGray(box, L.L_PAINT_DARK, 220, 0, 0, 255);
        rp.WritePixAndCheck(pixt, L.IFF_JFIF_JPEG);  /* 0 */
        pixa.AddPix(pixt, L.L_COPY);
        pixt.ColorGray(null, L.L_PAINT_DARK, 220, 255, 100, 100);
        rp.WritePixAndCheck(pixt, L.IFF_JFIF_JPEG);  /* 1 */
        pixa.AddPix(pixt, L.L_INSERT);

            /* Color non-white pixels on colormap */
        pixt = pixs.ThresholdTo4bpp(6, true);
        box = Box.Create(120, 30, 200, 200);
        pixt.ColorGray(box, L.L_PAINT_DARK, 220, 0, 0, 255);
        rp.WritePixAndCheck(pixt, L.IFF_PNG);  /* 2 */
        pixa.AddPix(pixt, L.L_COPY);
        pixt.ColorGray(null, L.L_PAINT_DARK, 220, 255, 100, 100);
        rp.WritePixAndCheck(pixt, L.IFF_PNG);  /* 3 */
        pixa.AddPix(pixt, L.L_INSERT);

            /* Color non-black pixels on RGB */
        pixt = pixs.Convert8To32();
        box = Box.Create(120, 30, 200, 200);
        pixt.ColorGray(box, L.L_PAINT_LIGHT, 20, 0, 0, 255);
        rp.WritePixAndCheck(pixt, L.IFF_PNG);  /* 4 */
        pixa.AddPix(pixt, L.L_COPY);
        pixt.ColorGray(null, L.L_PAINT_LIGHT, 80, 255, 100, 100);
        rp.WritePixAndCheck(pixt, L.IFF_PNG);  /* 5 */
        pixa.AddPix(pixt, L.L_INSERT);

            /* Color non-black pixels on colormap */
        pixt = pixs.ThresholdTo4bpp(6, true);
        box = Box.Create(120, 30, 200, 200);
        pixt.ColorGray(box, L.L_PAINT_LIGHT, 20, 0, 0, 255);
        rp.WritePixAndCheck(pixt, L.IFF_PNG);  /* 6 */
        pixa.AddPix(pixt, L.L_COPY);
        pixt.ColorGray(null, L.L_PAINT_LIGHT, 20, 255, 100, 100);
        rp.WritePixAndCheck(pixt, L.IFF_PNG);  /* 7 */
        pixa.AddPix(pixt, L.L_INSERT);

            /* Add highlight color to RGB */
        pixt = pixs.Convert8To32();
        box = Box.Create(507, 5, 385, 45);
        Pix pixg = pixs.ClipRectangle(box, out _)!;
        Pix pixb = pixg.ThresholdToBinary(180);
        Pix.Invert(pixb, pixb);
        Pix.ComposeRGBPixel(50, 0, 250, out uint val32);
        pixt.PaintThroughMask(pixb, box.X, box.Y, val32);

        box = Box.Create(236, 107, 262, 40);
        pixg = pixs.ClipRectangle(box, out _)!;
        pixb = pixg.ThresholdToBinary(180);
        Pix.Invert(pixb, pixb);
        Pix.ComposeRGBPixel(250, 0, 50, out val32);
        pixt.PaintThroughMask(pixb, box.X, box.Y, val32);

        box = Box.Create(222, 208, 247, 43);
        pixg = pixs.ClipRectangle(box, out _)!;
        pixb = pixg.ThresholdToBinary(180);
        Pix.Invert(pixb, pixb);
        Pix.ComposeRGBPixel(60, 250, 60, out val32);
        pixt.PaintThroughMask(pixb, box.X, box.Y, val32);
        rp.WritePixAndCheck(pixt, L.IFF_PNG);  /* 8 */
        pixa.AddPix(pixt, L.L_INSERT);

            /* Add highlight color to colormap */
        pixt = pixs.ThresholdTo4bpp(5, true);
        PixColormap cmap = pixt.Colormap!;
        cmap.GetIndex(255, 255, 255, out int index);
        box = Box.Create(507, 5, 385, 45);
        pixt.SetSelectCmap(box, index, 50, 0, 250);
        box = Box.Create(236, 107, 262, 40);
        pixt.SetSelectCmap(box, index, 250, 0, 50);
        box = Box.Create(222, 208, 247, 43);
        pixt.SetSelectCmap(box, index, 60, 250, 60);
        rp.WritePixAndCheck(pixt, L.IFF_PNG);  /* 9 */
        pixa.AddPix(pixt, L.L_INSERT);

            /* Paint lines on RGB */
        pixt = pixs.Convert8To32();
        pixt.RenderLineArb(450, 20, 850, 320, 5, 200, 50, 125);
        pixt.RenderLineArb(30, 40, 440, 40, 5, 100, 200, 25);
        box = Box.Create(70, 80, 300, 245);
        pixt.RenderBoxArb(box, 3, 200, 200, 25);
        rp.WritePixAndCheck(pixt, L.IFF_JFIF_JPEG);  /* 10 */
        pixa.AddPix(pixt, L.L_INSERT);

            /* Paint lines on colormap */
        pixt = pixs.ThresholdTo4bpp(5, true);
        pixt.RenderLineArb(450, 20, 850, 320, 5, 200, 50, 125);
        pixt.RenderLineArb(30, 40, 440, 40, 5, 100, 200, 25);
        box = Box.Create(70, 80, 300, 245);
        pixt.RenderBoxArb(box, 3, 200, 200, 25);
        rp.WritePixAndCheck(pixt, L.IFF_PNG);  /* 11 */
        pixa.AddPix(pixt, L.L_INSERT);

            /* Blend lines on RGB */
        pixt = pixs.Convert8To32();
        pixt.RenderLineBlend(450, 20, 850, 320, 5, 200, 50, 125, 0.35f);
        pixt.RenderLineBlend(30, 40, 440, 40, 5, 100, 200, 25, 0.35f);
        box = Box.Create(70, 80, 300, 245);
        pixt.RenderBoxBlend(box, 3, 200, 200, 25, 0.6f);
        rp.WritePixAndCheck(pixt, L.IFF_JFIF_JPEG);  /* 12 */
        pixa.AddPix(pixt, L.L_INSERT);

            /* Colorize gray on cmapped image */
        Pix pix1 = TestPaths.ReadInput("lucasta.150.jpg");
        Pix pix2 = pix1.ThresholdTo4bpp(7, true);
        Box box1 = Box.Create(73, 206, 140, 27);
        pix2.ColorGrayCmap(box1, L.L_PAINT_LIGHT, 130, 207, 43);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 13 */
        pixa.AddPix(pix2, L.L_COPY);

        Box box2 = Box.Create(255, 404, 197, 25);
        pix2.ColorGrayCmap(box2, L.L_PAINT_LIGHT, 230, 67, 119);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 14 */
        pixa.AddPix(pix2, L.L_COPY);

        Box box3 = Box.Create(122, 756, 224, 22);
        pix2.ColorGrayCmap(box3, L.L_PAINT_DARK, 230, 67, 119);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 15 */
        pixa.AddPix(pix2, L.L_COPY);

        Box box4 = Box.Create(11, 780, 147, 22);
        pix2.ColorGrayCmap(box4, L.L_PAINT_LIGHT, 70, 137, 229);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 16 */
        pixa.AddPix(pix2, L.L_COPY);

        Box box5 = Box.Create(163, 605, 78, 22);
        pix2.ColorGrayCmap(box5, L.L_PAINT_LIGHT, 70, 137, 229);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 17 */
        pixa.AddPix(pix2, L.L_INSERT);

            /* Make a gray image and identify the fg pixels (val > 230) */
        pixs = TestPaths.ReadInput("feyn-fract.tif");
        pix1 = pixs.ConvertTo8(false);
        L_Kernel kel = L_Kernel.MakeGaussianKernel(2, 2, 1.5f, 1.0f);
        pix2 = pix1.Convolve(kel, 8, 1);
        Pix pix3 = pix2.ThresholdToBinary(230);
        Boxa boxa = pix3.ConnComp(8);

            /* Color the individual components in the gray image */
        Pix pix4 = pix2.ColorGrayRegions(boxa, L.L_PAINT_DARK, 230, 255, 0, 0);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 18 */
        pixa.AddPix(pix4, L.L_INSERT);

            /* Threshold to 10 levels of gray */
        pix3 = pix2.ThresholdOn8bpp(10, true);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 19 */
        pixa.AddPix(pix3, L.L_COPY);

            /* Color the individual components in the cmapped image */
        pix4 = pix3.ColorGrayRegions(boxa, L.L_PAINT_DARK, 230, 255, 0, 0);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);  /* 20 */
        pixa.AddPix(pix4, L.L_INSERT);

            /* Color the entire gray image (not component-wise) */
        pix2.ColorGray(null, L.L_PAINT_DARK, 230, 255, 0, 0);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 21 */
        pixa.AddPix(pix2, L.L_INSERT);

            /* Color the entire cmapped image (not component-wise) */
        pix3.ColorGray(null, L.L_PAINT_DARK, 230, 255, 0, 0);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 22 */
        pixa.AddPix(pix3, L.L_INSERT);

            /* Reconstruct cmapped images */
        Pix pixd = ReconstructByValue(rp, "weasel2.4c.png");  /* 23 */
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 24 */
        pixa.AddPix(pixd, L.L_INSERT);
        pixd = ReconstructByValue(rp, "weasel4.11c.png");  /* 25 */
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 26 */
        pixa.AddPix(pixd, L.L_INSERT);
        pixd = ReconstructByValue(rp, "weasel8.240c.png");  /* 27 */
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 28 */
        pixa.AddPix(pixd, L.L_INSERT);

            /* Fake reconstruct cmapped images, with one color into a band */
        pixd = FakeReconstructByBand("weasel2.4c.png");
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 29 */
        pixa.AddPix(pixd, L.L_INSERT);
        pixd = FakeReconstructByBand("weasel4.11c.png");
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 30 */
        pixa.AddPix(pixd, L.L_INSERT);
        pixd = FakeReconstructByBand("weasel8.240c.png");
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 31 */
        pixa.AddPix(pixd, L.L_INSERT);

        Assert.Equal(31, rp.Index);
        rp.Cleanup();
    }

    private static Pix ReconstructByValue(RegTest rp, string fname)
    {
        Pix pixs = TestPaths.ReadInput(fname);
        PixColormap cmap = pixs.Colormap!;
        int n = cmap.Count;
        Pix pixd = Pix.CreateTemplate(pixs);
        for (int i = 0; i < n; i++) {
            Pix pixm = pixs.GenerateMaskByValue(i, true);
            cmap.GetColor(i, out int rval, out int gval, out int bval);
            pixd.SetMaskedCmap(pixm, 0, 0, rval, gval, bval);
        }

        rp.ComparePix(pixs, pixd);
        return pixd;
    }

    private static Pix FakeReconstructByBand(string fname)
    {
        Pix pixs = TestPaths.ReadInput(fname);
        PixColormap cmaps = pixs.Colormap!;
        int n = cmaps.Count;
        int nbands = (n + 1) / 2;
        Pix pixd = Pix.CreateTemplate(pixs);
        PixColormap cmapd = PixColormap.Create(pixs.Depth);
        pixd.SetColormap(cmapd);
        for (int i = 0; i < nbands; i++) {
            int jlow = 2 * i;
            int jup = Math.Min(jlow + 1, n - 1);
            Pix pixm = pixs.GenerateMaskByBand(jlow, jup, true, true);

            cmaps.GetColor(jlow, out int rval1, out int gval1, out int bval1);
            cmaps.GetColor(jup, out int rval2, out int gval2, out int bval2);
            int rval = (rval1 + rval2) / 2;
            int gval = (gval1 + gval2) / 2;
            int bval = (bval1 + bval2) / 2;

            cmapd.AddColor(rval, gval, bval);
            pixd.SetMaskedCmap(pixm, 0, 0, rval, gval, bval);
        }

        return pixd;
    }
}
