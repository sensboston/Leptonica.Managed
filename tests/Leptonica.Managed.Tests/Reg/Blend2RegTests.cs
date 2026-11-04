using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// blend2_reg.c: pixBlendWithGrayMask(), and png read/write with an alpha channel.
/// </summary>
public class Blend2RegTests
{
    [RegFact]
    public void Blend2()
    {
        int i, j, w1, h1, w2, h2, w, h;
        Box box1, box2;
        Pix pixg, pixs1, pixs2, pix1, pix2, pix3, pix4, pix5;
        Pixa pixa;
        var rp = new RegTest("blend2");

            /* --- Set up the 8 bpp blending image --- */
        pixg = Pix.Create(660, 500, 8);
        for (i = 0; i < 500; i++)
            for (j = 0; j < 660; j++)
                pixg.SetPixel(j, i, (uint)((int)(0.775 * j) % 256));

            /* --- Set up the initial color images to be blended together --- */
        pixs1 = TestPaths.ReadInput("wyom.jpg");
        pixs2 = TestPaths.ReadInput("fish24.jpg");
        pixs1.GetDimensions(out w, out h, out _);
        pixs1.GetDimensions(out w1, out h1, out _);
        pixs2.GetDimensions(out w2, out h2, out _);
        h = Math.Min(h1, h2);
        w = Math.Min(w1, w2);
        box1 = Box.Create(0, 0, w, h);
        box2 = Box.Create(0, 300, 660, 500);
        pix1 = pixs1.ClipRectangle(box1, out _)!;
        pix2 = pixs2.ClipRectangle(box2, out _)!;

            /* --- Blend 2 rgb images --- */
        pixa = Pixa.Create(0);
        pix3 = pix1.BlendWithGrayMask(pix2, pixg, 50, 50);
        pixa.AddPix(pix1, L.L_COPY);
        pixa.AddPix(pix2, L.L_COPY);
        pixa.AddPix(pix3, L.L_INSERT);
        rp.WritePixAndCheck(pixg, L.IFF_JFIF_JPEG);  /* 0 */
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 1 */
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 2 */
        rp.WritePixAndCheck(pix3, L.IFF_JFIF_JPEG);  /* 3 */

            /* --- Blend 2 grayscale images --- */
        pix3 = pix1.ConvertRGBToLuminance();
        pix4 = pix2.ConvertRGBToLuminance();
        pix5 = pix3.BlendWithGrayMask(pix4, pixg, 50, 50);
        pixa.AddPix(pix3, L.L_INSERT);
        pixa.AddPix(pix4, L.L_INSERT);
        pixa.AddPix(pix5, L.L_INSERT);
        rp.WritePixAndCheck(pix3, L.IFF_JFIF_JPEG);  /* 4 */
        rp.WritePixAndCheck(pix4, L.IFF_JFIF_JPEG);  /* 5 */
        rp.WritePixAndCheck(pix5, L.IFF_JFIF_JPEG);  /* 6 */

            /* --- Blend a colormap image and an rgb image --- */
        pix3 = pix2.FixedOctcubeQuantGenRGB(2);
        pix4 = pix1.BlendWithGrayMask(pix3, pixg, 50, 50);
        pixa.AddPix(pix1, L.L_COPY);
        pixa.AddPix(pix3, L.L_INSERT);
        pixa.AddPix(pix4, L.L_INSERT);
        rp.WritePixAndCheck(pix3, L.IFF_JFIF_JPEG);  /* 7 */
        rp.WritePixAndCheck(pix4, L.IFF_JFIF_JPEG);  /* 8 */

            /* --- Blend a colormap image and a grayscale image --- */
        pix3 = pix1.ConvertRGBToLuminance();
        pix4 = pix2.FixedOctcubeQuantGenRGB(2);
        pix5 = pix3.BlendWithGrayMask(pix4, pixg, 50, 50);
        pixa.AddPix(pix3, L.L_COPY);
        pixa.AddPix(pix4, L.L_COPY);
        pixa.AddPix(pix5, L.L_INSERT);
        rp.WritePixAndCheck(pix3, L.IFF_JFIF_JPEG);  /* 9 */
        rp.WritePixAndCheck(pix4, L.IFF_JFIF_JPEG);  /* 10 */
        rp.WritePixAndCheck(pix5, L.IFF_JFIF_JPEG);  /* 11 */
        pix5 = pix3.BlendWithGrayMask(pix4, pixg, -100, -100);
        pixa.AddPix(pix3, L.L_INSERT);
        pixa.AddPix(pix4, L.L_INSERT);
        pixa.AddPix(pix5, L.L_INSERT);
        rp.WritePixAndCheck(pix5, L.IFF_JFIF_JPEG);  /* 12 */

        /* --------- Test png read/write with alpha channel --------- */
            /* First make pix2, using pixg as the alpha channel */
        pix1 = TestPaths.ReadInput("fish24.jpg");
        box1 = Box.Create(0, 300, 660, 500);
        pix2 = pix1.ClipRectangle(box1, out _)!;
        pixa.AddPix(pix2, L.L_COPY);
        rp.WritePixAndCheck(pix2, L.IFF_JFIF_JPEG);  /* 13 */
        pix2.SetRGBComponent(pixg, L.L_ALPHA_CHANNEL);
        rp.WritePixAndCheck(pix2, L.IFF_PNG);  /* 14 */

            /* To see the alpha channel, blend with a black image */
        pix3 = Pix.Create(660, 500, 32);
        pix4 = pix3.BlendWithGrayMask(pix2, null, 0, 0);
        pixa.AddPix(pix4, L.L_INSERT);
        rp.WritePixAndCheck(pix4, L.IFF_JFIF_JPEG);  /* 15 */

            /* Read the RGBA image #14 back */
        pix4 = Pix.Read(Utils.GenPathname("/tmp/lept/regout/blend2.14.png", null));

            /* Make sure that the alpha channel image hasn't changed */
        pix5 = pix4.GetRGBComponent(L.L_ALPHA_CHANNEL);
        rp.ComparePix(pixg, pix5);  /* 16 */

            /* Blend again with a black image */
        pix5 = pix3.BlendWithGrayMask(pix4, null, 0, 0);
        pixa.AddPix(pix5, L.L_INSERT);
        rp.WritePixAndCheck(pix5, L.IFF_JFIF_JPEG);  /* 17 */

            /* Blend with a white image */
        pix3.SetAll();
        pix5 = pix3.BlendWithGrayMask(pix4, null, 0, 0);
        pixa.AddPix(pix5, L.L_INSERT);
        rp.WritePixAndCheck(pix5, L.IFF_JFIF_JPEG);  /* 18 */

            /* Display results */
        pix1 = pixa.DisplayTiledInColumns(3, 1.0f, 40, 2);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 19 */

        Assert.Equal(19, rp.Index);
        rp.Cleanup();
    }
}
