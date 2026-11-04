using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// colorquant_reg.c: various color quantizers.
/// </summary>
public class ColorQuantRegTests
{
    private const int MAX_WIDTH = 350;
    private static readonly string[] image = {"marge.jpg",
                                              "test24.jpg",
                                              "juditharismax.jpg",
                                              "hardlight2_2.jpg"};

    [RegFact]
    public void ColorQuant()
    {
        int i;
        var rp = new RegTest("colorquant");

        for (i = 0; i < 4; i++)
            TestImage(image[i], i, rp);

        Assert.Equal(167, rp.Index);
        rp.Cleanup();
    }

    private static void TestImage(string filename, int i, RegTest rp)
    {
        int w, h;
        float factor;
        Pix pix, pixs, pixc, pix32, pixt, pixd;
        Pixa pixa;

        pix = TestPaths.ReadInput(filename);
        pix.GetDimensions(out w, out h, out _);
        if (w > MAX_WIDTH) {
            factor = (float)MAX_WIDTH / (float)w;
            pixs = pix.Scale(factor, factor);
        }
        else
            pixs = Pix.Clone(pix);

        pixa = Pixa.Create(0);

            /* Median cut quantizer (no dither; 5 sigbits) */
        pixa.AddPix(pixs, L.L_COPY);
        pixc = pixs.MedianCutQuantGeneral(0, 0, 16, 5, 1, 1);
        PixSave32(pixa, pixc, rp);
        pixc = pixs.MedianCutQuantGeneral(0, 0, 128, 5, 1, 1);
        PixSave32(pixa, pixc, rp);
        pixc = pixs.MedianCutQuantGeneral(0, 0, 256, 5, 1, 1);
        PixSave32(pixa, pixc, rp);

            /* Median cut quantizer (with dither; 5 sigbits) */
        pixa.AddPix(pixs, L.L_COPY);
        pixc = pixs.MedianCutQuantGeneral(1, 0, 16, 5, 1, 1);
        PixSave32(pixa, pixc, rp);
        pixc = pixs.MedianCutQuantGeneral(1, 0, 128, 5, 1, 1);
        PixSave32(pixa, pixc, rp);
        pixc = pixs.MedianCutQuantGeneral(1, 0, 256, 5, 1, 1);
        PixSave32(pixa, pixc, rp);

            /* Median cut quantizer (no dither; 6 sigbits) */
        pixa.AddPix(pixs, L.L_COPY);
        pixc = pixs.MedianCutQuantGeneral(0, 0, 16, 6, 1, 1);
        PixSave32(pixa, pixc, rp);
        pixc = pixs.MedianCutQuantGeneral(0, 0, 128, 6, 1, 1);
        PixSave32(pixa, pixc, rp);
        pixc = pixs.MedianCutQuantGeneral(0, 0, 256, 6, 1, 1);
        PixSave32(pixa, pixc, rp);

            /* Median cut quantizer (with dither; 6 sigbits) */
        pixa.AddPix(pixs, L.L_COPY);
        pixc = pixs.MedianCutQuantGeneral(1, 0, 16, 6, 1, 1);
        PixSave32(pixa, pixc, rp);
        pixc = pixs.MedianCutQuantGeneral(1, 0, 128, 6, 1, 1);
        PixSave32(pixa, pixc, rp);
        pixc = pixs.MedianCutQuantGeneral(1, 0, 256, 6, 10, 1);
        PixSave32(pixa, pixc, rp);

            /* Median cut quantizer (mixed color/gray) */
        pixa.AddPix(pixs, L.L_COPY);
        pixc = pixs.MedianCutQuantMixed(20, 10, 0, 0, 0);
        PixSave32(pixa, pixc, rp);
        pixc = pixs.MedianCutQuantMixed(60, 20, 0, 0, 0);
        PixSave32(pixa, pixc, rp);
        pixc = pixs.MedianCutQuantMixed(180, 40, 0, 0, 0);
        PixSave32(pixa, pixc, rp);

            /* Simple 256 cube octcube quantizer */
        pixa.AddPix(pixs, L.L_COPY);
        pixc = pixs.FixedOctcubeQuant256(0);  /* no dither */
        PixSave32(pixa, pixc, rp);
        pixc = pixs.FixedOctcubeQuant256(1);  /* dither */
        PixSave32(pixa, pixc, rp);
        pixc = pixs.FixedOctcubeQuant256(1);  /* dither */
        PixSave32(pixa, pixc, rp);

            /* 2-pass octree quantizer */
        pixc = pixs.OctreeColorQuant(128, 0);  /* no dither */
        PixSave32(pixa, pixc, rp);
        pixc = pixs.OctreeColorQuant(240, 0);  /* no dither */
        PixSave32(pixa, pixc, rp);
        pixc = pixs.OctreeColorQuant(128, 1);  /* dither */
        PixSave32(pixa, pixc, rp);
        pixc = pixs.OctreeColorQuant(240, 1);  /* dither */
        PixSave32(pixa, pixc, rp);

            /* Simple adaptive quantization to 4 or 8 bpp, specifying ncolors */
        pixc = pixs.OctreeQuantNumColors(8, 0);    /* fixed: 8 colors */
        PixSave32(pixa, pixc, rp);
        pixc = pixs.OctreeQuantNumColors(16, 0);   /* fixed: 16 colors */
        PixSave32(pixa, pixc, rp);
        pixc = pixs.OctreeQuantNumColors(64, 0);   /* fixed: 64 colors */
        PixSave32(pixa, pixc, rp);
        pixc = pixs.OctreeQuantNumColors(256, 0);   /* fixed: 256 colors */
        PixSave32(pixa, pixc, rp);

            /* Quantize to fully populated octree (RGB) at given level */
        pixc = pixs.FixedOctcubeQuantGenRGB(2);  /* level 2 */
        PixSave32(pixa, pixc, rp);
        pixc = pixs.FixedOctcubeQuantGenRGB(3);  /* level 3 */
        PixSave32(pixa, pixc, rp);
        pixc = pixs.FixedOctcubeQuantGenRGB(4);  /* level 4 */
        PixSave32(pixa, pixc, rp);
        pixc = pixs.FixedOctcubeQuantGenRGB(5);  /* level 5 */
        PixSave32(pixa, pixc, rp);

            /* Generate 32 bpp RGB image with num colors <= 256 */
        pixt = pixs.OctreeQuantNumColors(256, 0);   /* cmapped version */
        pix32 = pixt.RemoveColormap(L.REMOVE_CMAP_BASED_ON_SRC);

            /* Quantize image with few colors at fixed octree leaf level */
        pixc = pix32.FewColorsOctcubeQuant1(2)!;   /* level 2 */
        PixSave32(pixa, pixc, rp);
        pixc = pix32.FewColorsOctcubeQuant1(3)!;   /* level 3 */
        PixSave32(pixa, pixc, rp);
        pixc = pix32.FewColorsOctcubeQuant1(4)!;   /* level 4 */
        PixSave32(pixa, pixc, rp);
        pixc = pix32.FewColorsOctcubeQuant1(5)!;   /* level 5 */
        PixSave32(pixa, pixc, rp);

            /* Quantize image by population */
        pixc = pixs.OctreeQuantByPopulation(3, 0);  /* level 3, no dither */
        PixSave32(pixa, pixc, rp);
        pixc = pixs.OctreeQuantByPopulation(3, 1);  /* level 3, dither */
        PixSave32(pixa, pixc, rp);
        pixc = pixs.OctreeQuantByPopulation(4, 0);  /* level 4, no dither */
        PixSave32(pixa, pixc, rp);
        pixc = pixs.OctreeQuantByPopulation(4, 1);  /* level 4, dither */
        PixSave32(pixa, pixc, rp);

            /* Mixed color/gray octree quantizer */
        pixa.AddPix(pixt, L.L_COPY);
        pixc = pix32.OctcubeQuantMixedWithGray(8, 64, 10);  /* max delta = 10 */
        PixSave32(pixa, pixc, rp);
        pixc = pix32.OctcubeQuantMixedWithGray(8, 64, 30);  /* max delta = 30 */
        PixSave32(pixa, pixc, rp);
        pixc = pix32.OctcubeQuantMixedWithGray(8, 64, 50);  /* max delta = 50 */
        PixSave32(pixa, pixc, rp);

            /* Run the high-level converter */
        pixa.AddPix(pixt, L.L_COPY);
        pixc = pix32.ConvertRGBToColormap(1);
        PixSave32(pixa, pixc, rp);

        pixd = pixa.DisplayTiledInColumns(4, 1.0f, 25, 2);
    }

    private static void PixSave32(Pixa pixa, Pix pixc, RegTest rp)
    {
        Pix pix32;

        pix32 = pixc.ConvertTo32();
        pixa.AddPix(pix32, L.L_INSERT);
        rp.WritePixAndCheck(pix32, L.IFF_JFIF_JPEG);
    }
}
