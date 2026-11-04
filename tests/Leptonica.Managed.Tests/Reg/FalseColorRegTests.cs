using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// falsecolor_reg.c: conversion of 8 and 16 bpp gray ramps to false color
/// with different gamma.
/// </summary>
public class FalseColorRegTests
{
    private static readonly float[] gamma = {1.0f, 2.0f, 3.0f};

    [RegFact]
    public void FalseColor()
    {
        int i, j, val8, val16;
        Pix pix8f, pix16f;
        Pixa pixa;
        var rp = new RegTest("falsecolor");

        Pix pix8 = Pix.Create(768, 100, 8);
        Pix pix16 = Pix.Create(768, 100, 16);
        for (i = 0; i < 100; i++) {
            for (j = 0; j < 768; j++) {
                val16 = 0xffff * j / 768;
                pix16.SetPixel(j, i, (uint)val16);
                val8 = 0xff * j / 768;
                pix8.SetPixel(j, i, (uint)val8);
            }
        }
        rp.WritePixAndCheck(pix8, L.IFF_PNG);   /* 0 */
        rp.WritePixAndCheck(pix16, L.IFF_PNG);  /* 1 */

        pixa = Pixa.Create(8);
        pixa.AddPix(pix8, L.L_INSERT);
        for (i = 0; i < 3; i++) {
            pix8f = pix8.ConvertGrayToFalseColor(gamma[i]);
            rp.WritePixAndCheck(pix8f, L.IFF_PNG);  /* 2 - 4 */
            pixa.AddPix(pix8f, L.L_INSERT);
        }
        pixa.AddPix(pix16, L.L_INSERT);
        for (i = 0; i < 3; i++) {
            pix16f = pix16.ConvertGrayToFalseColor(gamma[i]);
            rp.WritePixAndCheck(pix16f, L.IFF_PNG);  /* 5 - 7 */
            pixa.AddPix(pix16f, L.L_INSERT);
        }

        Assert.Equal(7, rp.Index);
        rp.Cleanup();
    }
}
