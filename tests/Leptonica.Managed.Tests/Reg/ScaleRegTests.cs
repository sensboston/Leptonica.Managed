using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// scale_reg.c: scaling of images of all depths, with and without colormap,
/// up and down, scale-to-gray of 1 bpp, and low-pass filtered smooth scaling.
/// </summary>
public class ScaleRegTests
{
    private static readonly string[] image = {"feyn-fract.tif",   /* 1 bpp */
                                              "weasel2.png",      /* 2 bpp; no cmap */
                                              "weasel2.4c.png",   /* 2 bpp; cmap */
                                              "weasel4.png",      /* 4 bpp; no cmap */
                                              "weasel4.16c.png",  /* 4 bpp; cmap */
                                              "weasel8.png",      /* 8 bpp; no cmap */
                                              "weasel8.240c.png", /* 8 bpp; cmap */
                                              "test16.png",       /* 16 bpp rgb */
                                              "marge.jpg",        /* 32 bpp rgb */
                                              "test24.jpg"};      /* 32 bpp rgb */

    private const int SPACE = 30;
    private const int WIDTH = 300;
    private static readonly float[] FACTOR = {2.3f, 1.5f, 1.1f, 0.6f, 0.3f};

    [RegFact]
    public void Scale()
    {
        int i;
        Pix pixs, pixc;
        Pixa pixa;
        var rp = new RegTest("scale");

            /* Test 1 bpp */
        pixa = Pixa.Create(0);
        pixs = TestPaths.ReadInput(image[0]);
        pixc = pixs.Scale(0.32f, 0.32f);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 0 */
        pixa.AddPix(pixc, L.L_INSERT);

        pixc = pixs.ScaleToGray3();
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 1 */
        pixa.AddPix(pixc, L.L_INSERT);

        pixc = pixs.ScaleToGray4();
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 2 */
        pixa.AddPix(pixc, L.L_INSERT);

        pixc = pixs.ScaleToGray6();
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 3 */
        pixa.AddPix(pixc, L.L_INSERT);

        pixc = pixs.ScaleToGray8();
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 4 */
        pixa.AddPix(pixc, L.L_INSERT);

        pixc = pixs.ScaleToGray16();
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 5 */
        pixa.AddPix(pixc, L.L_INSERT);
        PixaSaveDisplay(pixa, rp);  /* 6 */

        for (i = 1; i < 10; i++) {
            pixa = Pixa.Create(0);
            AddScaledImages(pixa, image[i], WIDTH);
            PixaSaveDisplay(pixa, rp);  /* 7 - 15 */
        }

            /* Test 2 bpp without colormap */
        pixa = Pixa.Create(0);
        pixs = TestPaths.ReadInput(image[1]);
        pixa.AddPix(pixs, L.L_INSERT);
        pixc = pixs.Scale(2.25f, 2.25f);
        rp.WritePixAndCheck(pixc, L.IFF_JFIF_JPEG);  /* 16 */
        pixa.AddPix(pixc, L.L_INSERT);
        pixc = pixs.Scale(0.85f, 0.85f);
        rp.WritePixAndCheck(pixc, L.IFF_JFIF_JPEG);  /* 17 */
        pixa.AddPix(pixc, L.L_INSERT);
        pixc = pixs.Scale(0.65f, 0.65f);
        rp.WritePixAndCheck(pixc, L.IFF_JFIF_JPEG);  /* 18 */
        pixa.AddPix(pixc, L.L_INSERT);
        PixaSaveDisplay(pixa, rp);  /* 19 */

            /* Test 2 bpp with colormap */
        pixa = Pixa.Create(0);
        pixs = TestPaths.ReadInput(image[2]);
        pixa.AddPix(pixs, L.L_INSERT);
        pixc = pixs.Scale(2.25f, 2.25f);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 20 */
        pixa.AddPix(pixc, L.L_INSERT);
        pixc = pixs.Scale(0.85f, 0.85f);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 21 */
        pixa.AddPix(pixc, L.L_INSERT);
        pixc = pixs.Scale(0.65f, 0.65f);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 22 */
        pixa.AddPix(pixc, L.L_INSERT);
        PixaSaveDisplay(pixa, rp);  /* 23 */

            /* Test 4 bpp without colormap */
        pixa = Pixa.Create(0);
        pixs = TestPaths.ReadInput(image[3]);
        pixa.AddPix(pixs, L.L_INSERT);
        pixc = pixs.Scale(1.72f, 1.72f);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 24 */
        pixa.AddPix(pixc, L.L_INSERT);
        pixc = pixs.Scale(0.85f, 0.85f);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 25 */
        pixa.AddPix(pixc, L.L_INSERT);
        pixc = pixs.Scale(0.65f, 0.65f);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 26 */
        pixa.AddPix(pixc, L.L_INSERT);
        PixaSaveDisplay(pixa, rp);  /* 27 */

            /* Test 4 bpp with colormap */
        pixa = Pixa.Create(0);
        pixs = TestPaths.ReadInput(image[4]);
        pixa.AddPix(pixs, L.L_INSERT);
        pixc = pixs.Scale(1.72f, 1.72f);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 28 */
        pixa.AddPix(pixc, L.L_INSERT);
        pixc = pixs.Scale(0.85f, 0.85f);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 29 */
        pixa.AddPix(pixc, L.L_INSERT);
        pixc = pixs.Scale(0.65f, 0.65f);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 30 */
        pixa.AddPix(pixc, L.L_INSERT);
        PixaSaveDisplay(pixa, rp);  /* 31 */

            /* Test 8 bpp without colormap */
        pixa = Pixa.Create(0);
        pixs = TestPaths.ReadInput(image[5]);
        pixa.AddPix(pixs, L.L_INSERT);
        pixc = pixs.Scale(1.92f, 1.92f);
        rp.WritePixAndCheck(pixc, L.IFF_JFIF_JPEG);  /* 32 */
        pixa.AddPix(pixc, L.L_INSERT);
        pixc = pixs.Scale(0.85f, 0.85f);
        rp.WritePixAndCheck(pixc, L.IFF_JFIF_JPEG);  /* 33 */
        pixa.AddPix(pixc, L.L_INSERT);
        pixc = pixs.Scale(0.65f, 0.65f);
        rp.WritePixAndCheck(pixc, L.IFF_JFIF_JPEG);  /* 34 */
        pixa.AddPix(pixc, L.L_INSERT);
        pixs = TestPaths.ReadInput("graytext.png");
        pixc = pixs.ScaleToSize(0, 32);  /* uses fast unsharp masking */
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 35 */
        pixa.AddPix(pixc, L.L_INSERT);
        PixaSaveDisplay(pixa, rp);  /* 36 */

            /* Test 8 bpp with colormap */
        pixa = Pixa.Create(0);
        pixs = TestPaths.ReadInput(image[6]);
        pixa.AddPix(pixs, L.L_INSERT);
        pixc = pixs.Scale(1.92f, 1.92f);
        rp.WritePixAndCheck(pixc, L.IFF_JFIF_JPEG);  /* 37 */
        pixa.AddPix(pixc, L.L_INSERT);
        pixc = pixs.Scale(0.85f, 0.85f);
        rp.WritePixAndCheck(pixc, L.IFF_JFIF_JPEG);  /* 38 */
        pixa.AddPix(pixc, L.L_INSERT);
        pixc = pixs.Scale(0.65f, 0.65f);
        rp.WritePixAndCheck(pixc, L.IFF_JFIF_JPEG);  /* 39 */
        pixa.AddPix(pixc, L.L_INSERT);
        PixaSaveDisplay(pixa, rp);  /* 40 */

            /* Test 16 bpp */
        pixa = Pixa.Create(0);
        pixs = TestPaths.ReadInput(image[7]);
        pixa.AddPix(pixs, L.L_INSERT);
        pixc = pixs.Scale(1.92f, 1.92f);
        rp.WritePixAndCheck(pixc, L.IFF_JFIF_JPEG);  /* 41 */
        pixa.AddPix(pixc, L.L_INSERT);
        pixc = pixs.Scale(0.85f, 0.85f);
        rp.WritePixAndCheck(pixc, L.IFF_JFIF_JPEG);  /* 42 */
        pixa.AddPix(pixc, L.L_INSERT);
        pixc = pixs.Scale(0.65f, 0.65f);
        rp.WritePixAndCheck(pixc, L.IFF_JFIF_JPEG);  /* 43 */
        pixa.AddPix(pixc, L.L_INSERT);
        PixaSaveDisplay(pixa, rp);  /* 44 */

            /* Test 32 bpp */
        pixa = Pixa.Create(0);
        pixs = TestPaths.ReadInput(image[8]);
        pixa.AddPix(pixs, L.L_INSERT);
        pixc = pixs.Scale(1.42f, 1.42f);
        rp.WritePixAndCheck(pixc, L.IFF_JFIF_JPEG);  /* 45 */
        pixa.AddPix(pixc, L.L_INSERT);
        pixc = pixs.Scale(0.85f, 0.85f);
        rp.WritePixAndCheck(pixc, L.IFF_JFIF_JPEG);  /* 46 */
        pixa.AddPix(pixc, L.L_INSERT);
        pixc = pixs.Scale(0.65f, 0.65f);
        rp.WritePixAndCheck(pixc, L.IFF_JFIF_JPEG);  /* 47 */
        pixa.AddPix(pixc, L.L_INSERT);
        PixaSaveDisplay(pixa, rp);  /* 48 */

            /* Test 32 bpp low-pass filtered smooth scaling */
        TestSmoothScaling("test24.jpg", rp);  /* 49 */
        Assert.Equal(49, rp.Index);
        rp.Cleanup();
    }

    private static void AddScaledImages(Pixa pixa, string fname, int width)
    {
        Pix pixs = TestPaths.ReadInput(fname);
        int w = pixs.Width;
        for (int i = 0; i < 5; i++) {
            float scalefactor = (float)width / (FACTOR[i] * (float)w);
            Pix pix1 = pixs.Scale(FACTOR[i], FACTOR[i]);
            Pix pix2 = pix1.Scale(scalefactor, scalefactor);
            Pix pix3 = pix2.ConvertTo32();
            pixa.AddPix(pix3, L.L_INSERT);
        }
    }

    private static void PixaSaveDisplay(Pixa pixa, RegTest rp)
    {
        Pix pixd = pixa.DisplayTiledInRows(32, 3000, 1.0f, 0, SPACE, 2);
        rp.WritePixAndCheck(pixd, L.IFF_JFIF_JPEG);
    }

    private static void TestSmoothScaling(string fname, RegTest rp)
    {
        Pix pix1 = TestPaths.ReadInput(fname);
        Pixa pixa = Pixa.Create(12);
        float scale = 0.5f;
        for (int i = 0; i < 12; i++) {
            scale *= 0.7f;
            float upscale = 0.25f / scale;
            Pix pix2 = pix1.ScaleSmooth(scale, scale);
            Pix pix3 = pix2.Scale(upscale, upscale);
            pixa.AddPix(pix3, L.L_INSERT);
        }
        Pix pixd = pixa.DisplayTiledInColumns(3, 1.0f, 10, 2);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);
    }
}
