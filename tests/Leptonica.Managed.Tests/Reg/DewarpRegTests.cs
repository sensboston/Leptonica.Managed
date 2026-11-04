using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// dewarp_reg.c: regression test for image dewarp based on text lines,
/// plus fpix and dpix scaling, serialization, and interconversion.
/// </summary>
public class DewarpRegTests
{
    [RegFact]
    public void Dewarp_Reg()
    {
        var rp = new RegTest("dewarp");

            /* Read page 7, normalize for varying background and binarize */
        Pix pixs = TestPaths.ReadInput("1555.007.jpg");
        Pix pixn = pixs.BackgroundNormSimple(null, null);
        Pix pixg = pixn.ConvertRGBToGray(0.5f, 0.3f, 0.2f);
        Pix pixb = pixg.ThresholdToBinary(130);
        rp.WritePixAndCheck(pixb, L.IFF_PNG);  /* 0 */

            /* Get the textline centers */
        Ptaa ptaa1 = Dewarp.GetTextlineCenters(pixb, 0)!;
        Pix pixt1 = Pix.CreateTemplate(pixs);
        Pix pixt2 = pixt1.DisplayPtaa(ptaa1);
        rp.WritePixAndCheck(pixt2, L.IFF_PNG);  /* 1 */

            /* Remove short lines */
        Ptaa ptaa2 = Dewarp.RemoveShortLines(pixb, ptaa1, 0.8f, 0);

            /* Fit to quadratic */
        int n = ptaa2.Count;
        for (int i = 0; i < n; i++) {
            Pta pta = ptaa2.GetPta(i, L.L_CLONE);
            pta.GetArrays(out Numa nax, out _);
            pta.GetQuadraticLSF(out _, out _, out _, out Numa nafit);
            Pta ptad = Pta.CreateFromNuma(nax, nafit);
            Pix.DisplayPta(pixt2, pixt2, ptad);
        }
        rp.WritePixAndCheck(pixt2, L.IFF_PNG);  /* 2 */

            /* Build the model for page 7 and dewarp */
        Dewarpa dewa1 = Dewarpa.Create(2, 30, 1, 15, 30);
        Dewarp dew1 = Dewarp.Create(pixb, 7);
        dewa1.UseBothArrays(1);
        dewa1.InsertDewarp(dew1);
        dew1.BuildPageModel(null);
        dewa1.ApplyDisparity(7, pixb, 200, 0, 0, out Pix pixd, null);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 3 */

            /* Read page 3, normalize background and binarize */
        Pix pixs2 = TestPaths.ReadInput("1555.003.jpg");
        Pix pixn2 = pixs2.BackgroundNormSimple(null, null);
        Pix pixg2 = pixn2.ConvertRGBToGray(0.5f, 0.3f, 0.2f);
        Pix pixb2 = pixg2.ThresholdToBinary(130);
        rp.WritePixAndCheck(pixb, L.IFF_PNG);  /* 4 */

            /* Minimize and re-apply page 7 disparity to this image */
        dewa1.InsertRefModels(0, 0);
        dewa1.ApplyDisparity(3, pixb2, 200, 0, 0, out pixd, null);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 5 */

            /* Write and read back minimized dewarp struct */
        dew1.Minimize();
        dew1.Write("/tmp/lept/regout/dewarp.6.dew");
        rp.CheckFile("/tmp/lept/regout/dewarp.6.dew");  /* 6 */
        Dewarp dew2 = Dewarp.Read("/tmp/lept/regout/dewarp.6.dew");
        dew2.Write("/tmp/lept/regout/dewarp.7.dew");
        rp.CheckFile("/tmp/lept/regout/dewarp.7.dew");  /* 7 */
        rp.CompareFiles(6, 7);  /* 8 */

            /* Apply this minimized dew to page 3 in a new dewa */
        Dewarpa dewa2 = Dewarpa.Create(2, 30, 1, 15, 30);
        dewa2.UseBothArrays(1);
        dewa2.InsertDewarp(dew2);
        dewa2.InsertRefModels(0, 0);
        dewa2.ListPages();
        dewa2.ApplyDisparity(3, pixb2, 200, 0, 0, out pixd, null);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 9 */

            /* Minimize, re-populate disparity arrays, and apply again */
        dew2.Minimize();
        dewa2.ApplyDisparity(3, pixb2, 200, 0, 0, out pixd, null);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 10 */
        rp.CompareFiles(9, 10);  /* 11 */

            /* Test a few of the fpix functions */
        FPix fpix1 = dew2.SampVDispar!.Clone();
        fpix1.Write("/tmp/lept/regout/dewarp.12.fpix");
        rp.CheckFile("/tmp/lept/regout/dewarp.12.fpix");  /* 12 */

        FPix fpix2 = FPix.Read("/tmp/lept/regout/dewarp.12.fpix");
        fpix2.Write("/tmp/lept/regout/dewarp.13.fpix");
        rp.CheckFile("/tmp/lept/regout/dewarp.13.fpix");  /* 13 */
        rp.CompareFiles(12, 13);  /* 14 */
        FPix fpix3 = fpix2.ScaleByInteger(30);
        Pix pix1 = fpix3.RenderContours(2.0f, 0.2f);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);  /* 15 */

            /* Test a few of the dpix functions */
        DPix dpix1 = dew2.SampVDispar!.ConvertToDPix();
        dpix1.Write("/tmp/lept/regout/dewarp.16.dpix");
        rp.CheckFile("/tmp/lept/regout/dewarp.16.dpix");  /* 16 */
        DPix dpix2 = DPix.Read("/tmp/lept/regout/dewarp.16.dpix");
        dpix2.Write("/tmp/lept/regout/dewarp.17.dpix");
        rp.CheckFile("/tmp/lept/regout/dewarp.17.dpix");  /* 17 */
        rp.CompareFiles(16, 17);  /* 18 */
        DPix dpix3 = dpix2.ScaleByInteger(30);
        fpix3 = dpix3.ConvertToFPix();
        pixt1 = fpix3.RenderContours(2.0f, 0.2f);
        rp.WritePixAndCheck(pixt1, L.IFF_PNG);  /* 19 */
        rp.CompareSimilarPix(pix1, pixt1, 1, 0.00001f, 0);  /* 20 */

        rp.Cleanup();
    }
}
