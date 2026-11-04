using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// alphaxform_reg.c: tests the alpha blending functions when used with various
/// transforms (scaling, rotation, affine, projective, bilinear).
/// </summary>
public class AlphaXformRegTests
{
    private static readonly int[] x1 = {  300,   300,   300,    95,   32 };
    private static readonly int[] y1 = { 1200,  1200,  1250,  2821,  934 };
    private static readonly int[] x2 = { 1200,  1200,  1125,  1432,  487 };
    private static readonly int[] y2 = { 1100,  1100,  1100,  2682,  934 };
    private static readonly int[] x3 = {  200,   200,   200,   232,   32 };
    private static readonly int[] y3 = {  200,   200,   200,   657,   67 };
    private static readonly int[] x4 = { 1200,  1200,  1125,  1432,  487 };
    private static readonly int[] y4 = {  200,   200,   200,   242,   84 };

    private static readonly int[] xp1 = {  500,   300,   350,   117,   32 };
    private static readonly int[] yp1 = { 1700,  1400,  1100,  2629,  934 };
    private static readonly int[] xp2 = {  850,   400,  1100,  1664,  487 };
    private static readonly int[] yp2 = {  850,   500,  1300,  2432,  804 };
    private static readonly int[] xp3 = {  450,   200,   400,   183,   61 };
    private static readonly int[] yp3 = {  300,   300,   400,   490,   83 };
    private static readonly int[] xp4 = {  850,   1000, 1100,  1664,  487 };
    private static readonly int[] yp4 = {  350,    350,  400,   532,  114 };

    [RegFact]
    public void AlphaXform()
    {
        Pix pixs2, pixs3, pixb1, pixb2, pixb3;
        Pix pixr2, pixr3;
        Pix pixc1, pixc2, pixc3, pixcs1, pixcs2, pixcs3;
        Pix pixd, pixt1, pixt2, pixt3;
        Pta ptas1, ptas2, ptas3, ptad1, ptad2, ptad3;
        var rp = new RegTest("alphaxform");

        pixc1 = TestPaths.ReadInput("test24.jpg");
        pixc2 = TestPaths.ReadInput("wyom.jpg");
        pixc3 = TestPaths.ReadInput("marge.jpg");

            /* Test alpha blend scaling */
        pixd = Pix.Create(900, 400, 32);
        pixd.SetAll();
        pixs2 = pixc2.ScaleWithAlpha(0.5f, 0.5f, null, 0.3f);
        pixs3 = pixc3.ScaleWithAlpha(0.4f, 0.4f, null, 0.7f);
        pixb1 = pixd.BlendWithGrayMask(pixs3, null, 100, 100);
        pixb2 = pixb1.BlendWithGrayMask(pixs2, null, 300, 130);
        pixb3 = pixb2.BlendWithGrayMask(pixs3, null, 600, 160);
        rp.WritePixAndCheck(pixb3, L.IFF_PNG);  /* 0 */

            /* Test alpha blend rotation */
        pixd = Pix.Create(1200, 800, 32);
        pixd.SetAll();
        pixr3 = pixc3.RotateWithAlpha(-0.3f, null, 1.0f);
        pixr2 = pixc2.RotateWithAlpha(+0.3f, null, 1.0f);
        pixb3 = pixd.BlendWithGrayMask(pixr3, null, 100, 100);
        pixb2 = pixb3.BlendWithGrayMask(pixr2, null, 400, 100);
        rp.WritePixAndCheck(pixb2, L.IFF_PNG);  /* 1 */

        pixcs1 = pixc1.Scale(0.35f, 0.35f);
        pixcs2 = pixc2.Scale(0.55f, 0.55f);
        pixcs3 = pixc3.Scale(0.65f, 0.65f);

            /* Test alpha blend affine */
        pixd = Pix.Create(800, 900, 32);
        pixd.SetAll();
        MakePtas(2, 3, out ptas1, out ptad1);
        MakePtas(4, 3, out ptas2, out ptad2);
        MakePtas(3, 3, out ptas3, out ptad3);
        pixt1 = pixcs1.AffinePtaWithAlpha(ptad1, ptas1, null, 1.0f, 300);
        pixt2 = pixcs2.AffinePtaWithAlpha(ptad2, ptas2, null, 0.8f, 400);
        pixt3 = pixcs3.AffinePtaWithAlpha(ptad3, ptas3, null, 0.7f, 300);
        pixb1 = pixd.BlendWithGrayMask(pixt1, null, -250, 20);
        pixb2 = pixb1.BlendWithGrayMask(pixt2, null, -150, -250);
        pixb3 = pixb2.BlendWithGrayMask(pixt3, null, -100, 220);
        rp.WritePixAndCheck(pixb3, L.IFF_PNG);  /* 2 */

            /* Test alpha blend projective */
        pixd = Pix.Create(900, 900, 32);
        pixd.SetAll();
        MakePtas(2, 4, out ptas1, out ptad1);
        MakePtas(4, 4, out ptas2, out ptad2);
        MakePtas(3, 4, out ptas3, out ptad3);
        pixt1 = pixcs1.ProjectivePtaWithAlpha(ptad1, ptas1, null, 1.0f, 300);
        pixt2 = pixcs2.ProjectivePtaWithAlpha(ptad2, ptas2, null, 0.8f, 400);
        pixt3 = pixcs3.ProjectivePtaWithAlpha(ptad3, ptas3, null, 0.7f, 400);
        pixb1 = pixd.BlendWithGrayMask(pixt1, null, -150, 20);
        pixb2 = pixb1.BlendWithGrayMask(pixt2, null, -50, -250);
        pixb3 = pixb2.BlendWithGrayMask(pixt3, null, -100, 220);
        rp.WritePixAndCheck(pixb3, L.IFF_PNG);  /* 3 */

            /* Test alpha blend bilinear */
        pixd = Pix.Create(900, 900, 32);
        pixd.SetAll();
        MakePtas(2, 4, out ptas1, out ptad1);
        MakePtas(4, 4, out ptas2, out ptad2);
        MakePtas(3, 4, out ptas3, out ptad3);
        pixt1 = pixcs1.BilinearPtaWithAlpha(ptad1, ptas1, null, 1.0f, 300);
        pixt2 = pixcs2.BilinearPtaWithAlpha(ptad2, ptas2, null, 0.8f, 400);
        pixt3 = pixcs3.BilinearPtaWithAlpha(ptad3, ptas3, null, 0.7f, 400);
        pixb1 = pixd.BlendWithGrayMask(pixt1, null, -150, 20);
        pixb2 = pixb1.BlendWithGrayMask(pixt2, null, -50, -250);
        pixb3 = pixb2.BlendWithGrayMask(pixt3, null, -100, 220);
        rp.WritePixAndCheck(pixb3, L.IFF_PNG);  /* 4 */

        Assert.Equal(4, rp.Index);
        rp.Cleanup();
    }

    private static void MakePtas(int i, int npts, out Pta pptas, out Pta pptad)  /* npts is 3 or 4 */
    {
        pptas = Pta.Create(npts);
        pptas.AddPt(x1[i], y1[i]);
        pptas.AddPt(x2[i], y2[i]);
        pptas.AddPt(x3[i], y3[i]);
        if (npts == 4) pptas.AddPt(x4[i], y4[i]);
        pptad = Pta.Create(npts);
        pptad.AddPt(xp1[i], yp1[i]);
        pptad.AddPt(xp2[i], yp2[i]);
        pptad.AddPt(xp3[i], yp3[i]);
        if (npts == 4) pptad.AddPt(xp4[i], yp4[i]);
    }
}
