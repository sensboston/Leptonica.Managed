using System.IO;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// circle_reg.c: finds the interior of filled circles by erosion and
/// counting of the connected components of the remainder.
/// </summary>
public class CircleRegTests
{
    private const int num_erodes = 8;

    [RegFact]
    public void Circle()
    {
        int i, k, count, n, maxloc, maxval, minval;
        Numa na;
        Pix pixs, pixsi, pixc, pixoc, pix1, pix2, pix3;
        Pixa pixas, pixa1, pixa2;
        var rp = new RegTest("circle");

            /* Read the images */
        pixas = Pixa.Read(Path.Combine(TestPaths.ProgDir, "circles.pa"));
        n = pixas.Count;

        pixa2 = Pixa.Create(0);
        for (k = 0; k < n; k++) {
            pixa1 = Pixa.Create(0);
            na = Numa.Create(0);
            pixs = pixas.GetPix(k, L.L_COPY);
            pixa1.AddPix(pixs, L.L_INSERT);
            pixsi = Pix.Invert(null, pixs);
            pixc = Pix.CreateTemplate(pixs);
            pixc.SetOrClearBorder(1, 1, 1, 1, L.PIX_SET);
            Pix.SeedfillBinary(pixc, pixc, pixsi, 4);
            Pix.Invert(pixc, pixc);
            pixoc = Pix.Copy(null, pixc);  /* original circle */
            pixa1.AddPix(pixoc, L.L_INSERT);
            pix1 = Pix.And(null, pixs, pixc);
            pixa1.AddPix(pix1, L.L_INSERT);
            count = pix1.CountConnComp(8);
            na.AddNumber(count);
            for (i = 1; i < num_erodes; i++) {
                Pix.ErodeBrick(pixc, pixc, 3, 3);
                pix1 = Pix.And(null, pixs, pixc);
                pixa1.AddPix(pix1, L.L_INSERT);
                count = pix1.CountConnComp(8);
                na.AddNumber(count);
            }

                /* Find the max value, not including the original image, which
                 * may have noise.  Then find the first occurrence of the
                 * min value that follows this max value. */
            maxval = maxloc = 0;
            for (i = 1; i < num_erodes; i++) {  /* get max value starting at 1 */
                count = na.GetIValue(i);
                if (count > maxval) {
                    maxval = count;
                    maxloc = i;
                }
            }
            minval = 1000;
            for (i = maxloc + 1; i < num_erodes; i++) {  /* get the min value */
                count = na.GetIValue(i);
                if (count < minval) minval = count;
            }
            for (i = maxloc + 1; i < num_erodes; i++) {  /* get first occurrence */
                count = na.GetIValue(i);
                if (count == minval) break;
            }

            pix1 = Pix.ErodeBrick(null, pixoc, 2 * i + 1, 2 * i + 1);
            pix2 = Pix.And(null, pixs, pix1);
            pixa1.AddPix(pix2, L.L_INSERT);
            pix3 = pixa1.DisplayTiledInColumns(11, 1.0f, 10, 2);
            rp.WritePixAndCheck(pix3, L.IFF_PNG);
            pixa2.AddPix(pix3, L.L_INSERT);
        }

        pix1 = pixa2.DisplayTiledInColumns(1, 1.0f, 10, 0);
        rp.WritePixAndCheck(pix1, L.IFF_PNG);
        Assert.Equal(n, rp.Index);
        rp.Cleanup();
    }
}
