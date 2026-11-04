using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// crop_reg.c: tests plotting pixel profiles and undercropping from a box
/// (clipping rectangle with added border).
/// </summary>
public class CropRegTests
{
    private const int MinDif = 60;
    private static readonly string[] FNames = { "lyra.005.jpg", "lyra.036.jpg" };

    [RegFact(Skip = "needs gnuplot output (gplotSimplePix1, gplot.c) and JPEG write (L_JPEG_ENCODE); non-gnuplot checks run in CropNoGnuplot")]
    public void Crop()
    {
        var rp = new RegTest("crop");

        Utils.LeptMkdir("lept/crop");

        Pixa pixa1 = Pixa.Create(2);
        for (int i = 0; i < 2; i++) {
            int pageno = Utils.ExtractNumberFromFilename(FNames[i], 5, 0);
            Pix pixs = TestPaths.ReadInput(FNames[i]);
            Pix pixr = pixs.Rotate90((pageno % 2 != 0) ? 1 : -1);
            Pix pixg = pixr.ConvertTo8(false);
            pixg.GetDimensions(out int w, out int h, out _);

            Numa nar = pixg.ReversalProfile(0.8f, L.L_VERTICAL_LINE, 0, h - 1, MinDif, 1, 1);
            Numa naro = nar.Open(11);
            Pix pix1Plot = Gplot.SimplePix1(naro, "Reversals Opened");
            rp.WritePixAndCheck(pix1Plot, L.IFF_PNG);  /* 0, 2 */
            Numa narl = naro.LowPassIntervals(0.1f, 0.0f);
            Numa nart = naro.ThresholdEdges(0.1f, 0.5f, 0.0f);

            Pix pixgi = Pix.Invert(null, pixg);
            Numa nai = pixgi.AverageIntensityProfile(0.8f, L.L_VERTICAL_LINE, 0, h - 1, 1, 1);
            Numa naio = nai.Open(11);
            Pix pix2Plot = Gplot.SimplePix1(naio, "Intensities Opened");
            rp.WritePixAndCheck(pix2Plot, L.IFF_PNG);  /* 1, 3 */
            Numa nait = naio.ThresholdEdges(0.4f, 0.6f, 0.0f);

            GetLeftCut(narl, nart, nait, out _);
            GetRightCut(nart, w, out _);

            Pixa pixa2 = Pixa.Create(3);
            pixa2.AddPix(pixr, L.L_INSERT);
            pixa2.AddPix(pix1Plot, L.L_INSERT);
            pixa2.AddPix(pix2Plot, L.L_INSERT);
            Pix pixd = pixa2.DisplayTiledInColumns(2, 1.0f, 25, 0);
            pixa1.AddPix(pixd, L.L_INSERT);
        }

        pixa1.ConvertToPdf(75, 1.0f, L.L_JPEG_ENCODE, 0, "Profiles", "/tmp/lept/crop/croptest.pdf");

        Pix pixsText = TestPaths.ReadInput("1555.007.jpg");
        pixsText.GetDimensions(out _, out int ht, out _);
        Numa na1 = pixsText.ReversalProfile(0.98f, L.L_HORIZONTAL_LINE, 0, ht - 1, 40, 3, 3);
        Pix pixRev = Gplot.SimplePix1(na1, "Reversals");
        na1 = pixsText.AverageIntensityProfile(0.98f, L.L_HORIZONTAL_LINE, 0, ht - 1, 1, 1);
        Pix pixInt = Gplot.SimplePix1(na1, "Intensities");
        rp.WritePixAndCheck(pixRev, L.IFF_PNG);  /* 4 */
        rp.WritePixAndCheck(pixInt, L.IFF_PNG);  /* 5 */

        RunClipWithBorderChecks(rp);  /* 6, 7, 8 */

        Assert.Equal(8, rp.Index);
        rp.Cleanup();
    }

    [RegFact]
    public void CropNoGnuplot()
    {
        var rp = new RegTest("crop");

        for (int i = 0; i < 2; i++) {
            int pageno = Utils.ExtractNumberFromFilename(FNames[i], 5, 0);
            Pix pixs = TestPaths.ReadInput(FNames[i]);
            Pix pixr = pixs.Rotate90((pageno % 2 != 0) ? 1 : -1);
            Pix pixg = pixr.ConvertTo8(false);
            pixg.GetDimensions(out int w, out int h, out _);

            Numa nar = pixg.ReversalProfile(0.8f, L.L_VERTICAL_LINE, 0, h - 1, MinDif, 1, 1);
            Numa naro = nar.Open(11);
            Numa narl = naro.LowPassIntervals(0.1f, 0.0f);
            Numa nart = naro.ThresholdEdges(0.1f, 0.5f, 0.0f);

            Pix pixgi = Pix.Invert(null, pixg);
            Numa nai = pixgi.AverageIntensityProfile(0.8f, L.L_VERTICAL_LINE, 0, h - 1, 1, 1);
            Numa naio = nai.Open(11);
            Numa nait = naio.ThresholdEdges(0.4f, 0.6f, 0.0f);

            GetLeftCut(narl, nart, nait, out int left);
            GetRightCut(nart, w, out int right);
            Assert.True(left >= 0 && right > left && right < w);
        }

        Pix pixsText = TestPaths.ReadInput("1555.007.jpg");
        pixsText.GetDimensions(out _, out int ht, out _);
        Numa naRev = pixsText.ReversalProfile(0.98f, L.L_HORIZONTAL_LINE, 0, ht - 1, 40, 3, 3);
        Numa naInt = pixsText.AverageIntensityProfile(0.98f, L.L_HORIZONTAL_LINE, 0, ht - 1, 1, 1);
        Assert.True(naRev.Count > 0 && naInt.Count > 0);

        RunClipWithBorderChecks(rp);  /* 0, 1, 2 */

        Assert.Equal(2, rp.Index);
        rp.Cleanup();
    }

    private static void RunClipWithBorderChecks(RegTest rp)
    {
        Pix pix1 = TestPaths.ReadInput("lyra.005.jpg");
        Pix pix2 = pix1.Scale(0.5f, 0.5f);
        Box box1 = Box.Create(125, 50, 180, 230);  /* fully contained */
        Pix pix3 = pix2.ClipRectangleWithBorder(box1, 30, out Box? box2)!;
        pix2.RenderBoxArb(box1, 2, 255, 0, 0);
        pix3.RenderBoxArb(box2!, 2, 255, 0, 0);
        Pixa pixa1 = Pixa.Create(2);
        pixa1.AddPix(pix2, L.L_INSERT);
        pixa1.AddPix(pix3, L.L_INSERT);
        Pix pix4 = pixa1.DisplayTiledInColumns(2, 1.0f, 15, 2);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);

        pix2 = pix1.Scale(0.5f, 0.5f);
        box1 = Box.Create(125, 10, 180, 270);  /* not full border */
        pix3 = pix2.ClipRectangleWithBorder(box1, 30, out box2)!;
        pix2.RenderBoxArb(box1, 2, 255, 0, 0);
        pix3.RenderBoxArb(box2!, 2, 255, 0, 0);
        pixa1 = Pixa.Create(2);
        pixa1.AddPix(pix2, L.L_INSERT);
        pixa1.AddPix(pix3, L.L_INSERT);
        pix4 = pixa1.DisplayTiledInColumns(2, 1.0f, 15, 2);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);

        pix2 = pix1.Scale(0.5f, 0.5f);
        box1 = Box.Create(125, 200, 180, 270);  /* not entirely within pix2 */
        pix3 = pix2.ClipRectangleWithBorder(box1, 30, out box2)!;
        pix2.RenderBoxArb(box1, 2, 255, 0, 0);
        pix3.RenderBoxArb(box2!, 2, 255, 0, 0);
        pixa1 = Pixa.Create(2);
        pixa1.AddPix(pix2, L.L_INSERT);
        pixa1.AddPix(pix3, L.L_INSERT);
        pix4 = pixa1.DisplayTiledInColumns(2, 1.0f, 15, 2);
        rp.WritePixAndCheck(pix4, L.IFF_PNG);
    }

    private static void GetLeftCut(Numa narl, Numa nart, Numa nait, out int pleft)
    {
        int nrl = narl.Count;
        int nrt = nart.Count;
        int nit = nait.Count;

        narl.GetSpanValues(0, out _, out int end);
        if (end < 20 || nrl <= 1) {
            pleft = 0;
            return;
        }

        int pap1 = 0;
        int txt1 = 0;
        if (nrt >= 4) {
            nart.GetEdgeValues(0, out int start, out _, out _);
            txt1 = start;
        }
        if (nit >= 4) {
            nait.GetEdgeValues(0, out _, out end, out int sign);
            if (end < txt1 && sign == -1)
                pap1 = end;
            else
                pap1 = (int)(0.5 * txt1);
        }
        int del = txt1 - pap1;
        if (del > 20) {
            txt1 -= Math.Min(20, (int)(0.5 * del));
            pap1 += Math.Min(20, (int)(0.5 * del));
        }
        pleft = pap1;
    }

    private static void GetRightCut(Numa nart, int w, out int pright)
    {
        int nrt = nart.Count;
        int ntrans = nrt / 3;
        int txt2 = 0, pap2 = 0;
        if (ntrans > 1) {
            bool found = false;
            for (int trans = ntrans - 1; trans > 0; --trans) {
                nart.GetEdgeValues(trans, out _, out int end, out int sign);
                if (sign == -1) {
                    txt2 = end;
                    found = true;
                }
            }
            if (!found) {
                pap2 = w - 1;
            } else {
                found = false;
                for (int trans = ntrans - 1; trans > 0; --trans) {
                    nart.GetEdgeValues(trans, out int start, out _, out int sign);
                    if (sign == 1 && start > txt2) {
                        pap2 = start;
                        found = true;
                    }
                }
                if (!found)
                    pap2 = w - 1;
            }
        } else {
            pap2 = w - 1;
        }
        pright = pap2;
    }
}
