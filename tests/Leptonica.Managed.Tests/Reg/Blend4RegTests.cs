using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// blend4_reg.c: pixAddAlphaToBlend(), with blending by pixBlendWithGrayMask(), and
/// blending of two color images using an alpha mask that varies linearly with
/// the distance from the center.
/// </summary>
public class Blend4RegTests
{
    private static readonly string[] blenders = {"feyn-word.tif", "weasel4.16c.png", "karen8.jpg"};

    [RegFact]
    public void Blend4()
    {
        int i, w, h;
        Pix pix0, pix1, pix2, pix3, pix4, pix5, pix6, pix7;
        var rp = new RegTest("blend4");

            /* Blending on a light image */
        pix1 = TestPaths.ReadInput("fish24.jpg");
        w = pix1.Width;
        h = pix1.Height;
        for (i = 0; i < 3; i++) {
            pix2 = TestPaths.ReadInput(blenders[i]);
            if (i == 2) {
                pix3 = pix2.Scale(0.5f, 0.5f);
                pix2 = pix3;
            }
            pix3 = pix2.AddAlphaToBlend(0.3f, 0);
            pix4 = pix3.MirroredTiling(w, h);
            pix5 = pix1.BlendWithGrayMask(pix4, null, 0, 0);
            rp.WritePixAndCheck(pix5, L.IFF_JFIF_JPEG);  /* 0 - 2 */
        }

            /* Blending on a dark image */
        pix0 = TestPaths.ReadInput("karen8.jpg");
        pix1 = pix0.Scale(2.0f, 2.0f);
        w = pix1.Width;
        h = pix1.Height;
        for (i = 0; i < 2; i++) {
            pix2 = TestPaths.ReadInput(blenders[i]);
            pix3 = pix2.AddAlphaToBlend(0.3f, 1);
            pix4 = pix3.MirroredTiling(w, h);
            pix5 = pix1.BlendWithGrayMask(pix4, null, 0, 0);
            rp.WritePixAndCheck(pix5, L.IFF_JFIF_JPEG);  /* 3 - 4 */
        }

            /* Blending of two color images using special mask */
        pix1 = TestPaths.ReadInput("fish24.jpg");
        pix2 = TestPaths.ReadInput("wyom.jpg");
        w = pix2.Width;
        h = pix2.Height;
        pix3 = pix1.RotateOrth(1);
        pix4 = pix3.ScaleToSize(w, h);   /* same size as wyom.jpg */
        pix5 = AlphaRectangle(w, h, 1.0f);
        pix6 = pix4.BlendWithGrayMask(pix2, pix5, 0, 0);
        pix7 = pix2.BlendWithGrayMask(pix4, pix5, 0, 0);
        rp.WritePixAndCheck(pix4, L.IFF_JFIF_JPEG);  /* 5 */
        rp.WritePixAndCheck(pix5, L.IFF_JFIF_JPEG);  /* 6 */
        rp.WritePixAndCheck(pix6, L.IFF_JFIF_JPEG);  /* 7 */
        rp.WritePixAndCheck(pix7, L.IFF_JFIF_JPEG);  /* 8 */

        Assert.Equal(8, rp.Index);
        rp.Cleanup();
    }

        /* Rectangular mask: opaque at center, linear change towards
         * transparency with distance from the center */
    private static Pix AlphaRectangle(int w, int h, float fract)
    {
        int i, j, wpl, w2, h2, val;
        float frdist;
        uint[] data;
        Pix pixd;

        pixd = Pix.Create(w, h, 8);
        data = pixd.Data;
        wpl = pixd.Wpl;
        w2 = w / 2;
        h2 = h / 2;
        for (i = 0; i < h; i++) {
            Span<uint> line = data.AsSpan(i * wpl);
            for (j = 0; j < w; j++) {
                frdist = (float)(Math.Sqrt((h2 - i) * (h2 - i) + (w2 - j) * (w2 - j)) /
                                 Math.Sqrt(w2 * w2 + h2 * h2));
                val = (int)(255.0 * (1.0 - frdist * fract));
                ArrayAccess.SetDataByte(line, j, val);
            }
        }

        return pixd;
    }
}
