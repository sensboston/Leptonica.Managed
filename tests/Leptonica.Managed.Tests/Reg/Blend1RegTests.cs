using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// blend1_reg.c: pixBlendGray(), pixBlendGrayAdapt() and pixBlendColor().
/// </summary>
public class Blend1RegTests
{
    [RegFact]
    public void Blend1()
    {
        Pix pixs, pixg, pixc, pix1;
        Pixa pixa;
        var rp = new RegTest("blend1");

            /* Set up blenders */
        pixg = TestPaths.ReadInput("blender8.png");
        pix1 = TestPaths.ReadInput("weasel4.11c.png");
        pixc = pix1.RemoveColormap(L.REMOVE_CMAP_TO_FULL_COLOR);
        pixa = Pixa.Create(0);

            /* Gray blend (straight) */
        pixs = TestPaths.ReadInput("test24.jpg");
        pix1 = pixs.Scale(0.4f, 0.4f);
        GrayBlend(pix1, pixg, L.L_BLEND_GRAY, 0.3f);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 0 */
        pixa.AddPix(pix1, L.L_INSERT);

        pixs = TestPaths.ReadInput("marge.jpg");
        GrayBlend(pixs, pixg, L.L_BLEND_GRAY, 0.2f);
        rp.WritePixAndCheck(pixs, L.IFF_JFIF_JPEG);  /* 1 */
        pixa.AddPix(pixs, L.L_INSERT);

        pixs = TestPaths.ReadInput("marge.jpg");
        pix1 = pixs.ConvertRGBToLuminance();
        GrayBlend(pix1, pixg, L.L_BLEND_GRAY, 0.2f);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 2 */
        pixa.AddPix(pix1, L.L_INSERT);

            /* Gray blend (inverse) */
        pixs = TestPaths.ReadInput("test24.jpg");
        pix1 = pixs.Scale(0.4f, 0.4f);
        GrayBlend(pix1, pixg, L.L_BLEND_GRAY_WITH_INVERSE, 0.6f);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 3 */
        pixa.AddPix(pix1, L.L_INSERT);

        pixs = TestPaths.ReadInput("marge.jpg");
        GrayBlend(pixs, pixg, L.L_BLEND_GRAY_WITH_INVERSE, 0.6f);
        rp.WritePixAndCheck(pixs, L.IFF_JFIF_JPEG);  /* 4 */
        pixa.AddPix(pixs, L.L_INSERT);

        pixs = TestPaths.ReadInput("marge.jpg");
        pix1 = pixs.ConvertRGBToLuminance();
        GrayBlend(pix1, pixg, L.L_BLEND_GRAY_WITH_INVERSE, 0.6f);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 5 */
        pixa.AddPix(pix1, L.L_INSERT);

        pixs = MakeGrayWash(1000, 120);
        GrayBlend(pixs, pixg, L.L_BLEND_GRAY_WITH_INVERSE, 0.3f);
        rp.WritePixAndCheck(pixs, L.IFF_PNG);  /* 6 */
        pixa.AddPix(pixs, L.L_INSERT);

        pixs = MakeColorWash(1000, 120, L.COLOR_RED);
        GrayBlend(pixs, pixg, L.L_BLEND_GRAY_WITH_INVERSE, 1.0f);
        rp.WritePixAndCheck(pixs, L.IFF_PNG);  /* 7 */
        pixa.AddPix(pixs, L.L_INSERT);

            /* Adaptive gray blend */
        pixs = TestPaths.ReadInput("test24.jpg");
        pix1 = pixs.Scale(0.4f, 0.4f);
        AdaptiveGrayBlend(pix1, pixg, 0.8f);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 8 */
        pixa.AddPix(pix1, L.L_INSERT);

        pixs = TestPaths.ReadInput("marge.jpg");
        AdaptiveGrayBlend(pixs, pixg, 0.8f);
        rp.WritePixAndCheck(pixs, L.IFF_JFIF_JPEG);  /* 9 */
        pixa.AddPix(pixs, L.L_INSERT);

        pix1 = pixs.ConvertRGBToLuminance();
        AdaptiveGrayBlend(pix1, pixg, 0.1f);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 10 */
        pixa.AddPix(pix1, L.L_INSERT);

        pixs = MakeGrayWash(1000, 120);
        AdaptiveGrayBlend(pixs, pixg, 0.3f);
        rp.WritePixAndCheck(pixs, L.IFF_PNG);  /* 11 */
        pixa.AddPix(pixs, L.L_INSERT);

        pixs = MakeColorWash(1000, 120, L.COLOR_RED);
        AdaptiveGrayBlend(pixs, pixg, 0.5f);
        rp.WritePixAndCheck(pixs, L.IFF_PNG);  /* 12 */
        pixa.AddPix(pixs, L.L_INSERT);

            /* Color blend */
        pixs = TestPaths.ReadInput("test24.jpg");
        pix1 = pixs.Scale(0.4f, 0.4f);
        ColorBlend(pix1, pixc, 0.3f);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 13 */
        pixa.AddPix(pix1, L.L_INSERT);

        pixs = TestPaths.ReadInput("marge.jpg");
        ColorBlend(pixs, pixc, 0.30f);
        rp.WritePixAndCheck(pixs, L.IFF_JFIF_JPEG);  /* 14 */
        pixa.AddPix(pixs, L.L_INSERT);

        pixs = TestPaths.ReadInput("marge.jpg");
        ColorBlend(pixs, pixc, 0.15f);
        rp.WritePixAndCheck(pixs, L.IFF_JFIF_JPEG);  /* 15 */
        pixa.AddPix(pixs, L.L_INSERT);

            /* Mosaic all results */
        pix1 = pixa.DisplayTiledInRows(32, 1700, 1.0f, 0, 20, 2);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 16 */

        Assert.Equal(16, rp.Index);
        rp.Cleanup();
    }

    private static void GrayBlend(Pix pixs, Pix pixb, int op, float fract)
    {
        int i, j, wb, hb, ws, hs, delx, dely, x, y;

        pixs.GetDimensions(out ws, out hs, out _);
        pixb.GetDimensions(out wb, out hb, out _);
        delx = wb + 30;
        dely = hb + 25;
        x = 200;
        y = 300;
        for (i = 0; i < 20; i++) {
            y = 20 + i * dely;
            if (y >= hs - hb)
                continue;
            for (j = 0; j < 20; j++) {
                x = 30 + j * delx;
                if (x >= ws - wb)
                    continue;
                Pix.BlendGray(pixs, pixs, pixb, x, y, fract, op, 1, 255);
            }
        }
    }

    private static void AdaptiveGrayBlend(Pix pixs, Pix pixb, float fract)
    {
        int i, j, wb, hb, ws, hs, delx, dely, x, y;

        pixs.GetDimensions(out ws, out hs, out _);
        pixb.GetDimensions(out wb, out hb, out _);
        delx = wb + 30;
        dely = hb + 25;
        x = 200;
        y = 300;
        for (i = 0; i < 20; i++) {
            y = 20 + i * dely;
            if (y >= hs - hb)
                continue;
            for (j = 0; j < 20; j++) {
                x = 30 + j * delx;
                if (x >= ws - wb)
                    continue;
                Pix.BlendGrayAdapt(pixs, pixs, pixb, x, y, fract, 80);
            }
        }
    }

    private static void ColorBlend(Pix pixs, Pix pixb, float fract)
    {
        int i, j, wb, hb, ws, hs, delx, dely, x, y;

        pixs.GetDimensions(out ws, out hs, out _);
        pixb.GetDimensions(out wb, out hb, out _);
        delx = wb + 30;
        dely = hb + 25;
        x = 200;
        y = 300;
        for (i = 0; i < 20; i++) {
            y = 20 + i * dely;
            if (y >= hs - hb)
                continue;
            for (j = 0; j < 20; j++) {
                x = 30 + j * delx;
                if (x >= ws - wb)
                    continue;
                Pix.BlendColor(pixs, pixs, pixb, x, y, fract, 1, 255);
            }
        }
    }

    private static Pix MakeGrayWash(int w, int h)
    {
        int i, j, wpl, val;
        uint[] data;
        Pix pixd;

        pixd = Pix.Create(w, h, 8);
        data = pixd.Data;
        wpl = pixd.Wpl;
        for (i = 0; i < h; i++) {
            var line = data.AsSpan(i * wpl);
            for (j = 0; j < w; j++) {
                val = (j * 255) / w;
                ArrayAccess.SetDataByte(line, j, val);
            }
        }
        return pixd;
    }

    private static Pix MakeColorWash(int w, int h, int color)
    {
        int i, j, wpl;
        uint val;
        uint[] data;
        Pix pixd;

        pixd = Pix.Create(w, h, 32);
        data = pixd.Data;
        wpl = pixd.Wpl;
        for (i = 0; i < h; i++) {
            int line = i * wpl;
            for (j = 0; j < w; j++) {
                if (color == L.COLOR_RED)
                    val = (uint)(((j * 255) / w) << L.L_GREEN_SHIFT |
                                 ((j * 255) / w) << L.L_BLUE_SHIFT |
                                 255 << L.L_RED_SHIFT);
                else if (color == L.COLOR_GREEN)
                    val = (uint)(((j * 255) / w) << L.L_RED_SHIFT |
                                 ((j * 255) / w) << L.L_BLUE_SHIFT |
                                 255 << L.L_GREEN_SHIFT);
                else
                    val = (uint)(((j * 255) / w) << L.L_RED_SHIFT |
                                 ((j * 255) / w) << L.L_GREEN_SHIFT |
                                 255 << L.L_BLUE_SHIFT);
                data[line + j] = val;
            }
        }
        return pixd;
    }
}
