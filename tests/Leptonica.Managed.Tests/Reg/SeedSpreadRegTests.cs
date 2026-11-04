using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// seedspread_reg.c: spreading of the values of sparse seed pixels, with
/// 4- and 8-connectivity, for dense, lattice and very sparse seeds.
/// </summary>
public class SeedSpreadRegTests
{
    private const float scalefact = 1.0f;

    [RegFact]
    public void SeedSpread()
    {
        int i, j, x, y, val;
        Pix pixs, pixc, pixd;
        Pixa pixa;
        var rp = new RegTest("seedspread");

        Pix pixsq = Pix.Create(3, 3, 32);
        pixsq.SetAllArbitrary(0x00ff0000);
        pixa = Pixa.Create(6);

            /* Moderately dense */
        pixs = Pix.Create(300, 300, 8);
        for (i = 0; i < 100; i++) {
            x = (153 * i * i * i + 59) % 299;
            y = (117 * i * i * i + 241) % 299;
            val = (97 * i + 74) % 256;
            pixs.SetPixel(x, y, (uint)val);
        }

        pixd = pixs.Seedspread(4);  /* 4-cc */
        pixc = pixd.ConvertTo32();
        for (i = 0; i < 100; i++) {
            x = (153 * i * i * i + 59) % 299;
            y = (117 * i * i * i + 241) % 299;
            pixc.Rasterop(x - 1, y - 1, 3, 3, L.PIX_SRC, pixsq, 0, 0);
        }
        pixa.AddPix(pixc, L.L_INSERT);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 0 */

        pixd = pixs.Seedspread(8);  /* 8-cc */
        pixc = pixd.ConvertTo32();
        for (i = 0; i < 100; i++) {
            x = (153 * i * i * i + 59) % 299;
            y = (117 * i * i * i + 241) % 299;
            pixc.Rasterop(x - 1, y - 1, 3, 3, L.PIX_SRC, pixsq, 0, 0);
        }
        pixa.AddPix(pixc, L.L_INSERT);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 1 */

            /* Regular lattice */
        pixs = Pix.Create(200, 200, 8);
        for (i = 5; i <= 195; i += 10) {
            for (j = 5; j <= 195; j += 10) {
                pixs.SetPixel(i, j, (uint)((7 * i + 17 * j) % 255));
            }
        }
        pixd = pixs.Seedspread(4);  /* 4-cc */
        pixc = pixd.ConvertTo32();
        for (i = 5; i <= 195; i += 10) {
            for (j = 5; j <= 195; j += 10) {
                pixc.Rasterop(j - 1, i - 1, 3, 3, L.PIX_SRC, pixsq, 0, 0);
            }
        }
        pixa.AddPix(pixc, L.L_INSERT);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 2 */

        pixd = pixs.Seedspread(8);  /* 8-cc */
        pixc = pixd.ConvertTo32();
        for (i = 5; i <= 195; i += 10) {
            for (j = 5; j <= 195; j += 10) {
                pixc.Rasterop(j - 1, i - 1, 3, 3, L.PIX_SRC, pixsq, 0, 0);
            }
        }
        pixa.AddPix(pixc, L.L_INSERT);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 3 */

            /* Very sparse points */
        pixs = Pix.Create(200, 200, 8);
        pixs.SetPixel(60, 20, 90);
        pixs.SetPixel(160, 40, 130);
        pixs.SetPixel(80, 80, 205);
        pixs.SetPixel(40, 160, 115);
        pixd = pixs.Seedspread(4);  /* 4-cc */
        pixc = pixd.ConvertTo32();
        pixc.Rasterop(60 - 1, 20 - 1, 3, 3, L.PIX_SRC, pixsq, 0, 0);
        pixc.Rasterop(160 - 1, 40 - 1, 3, 3, L.PIX_SRC, pixsq, 0, 0);
        pixc.Rasterop(80 - 1, 80 - 1, 3, 3, L.PIX_SRC, pixsq, 0, 0);
        pixc.Rasterop(40 - 1, 160 - 1, 3, 3, L.PIX_SRC, pixsq, 0, 0);
        pixa.AddPix(pixc, L.L_INSERT);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 4 */

        pixd = pixs.Seedspread(8);  /* 8-cc */
        pixc = pixd.ConvertTo32();
        pixc.Rasterop(60 - 1, 20 - 1, 3, 3, L.PIX_SRC, pixsq, 0, 0);
        pixc.Rasterop(160 - 1, 40 - 1, 3, 3, L.PIX_SRC, pixsq, 0, 0);
        pixc.Rasterop(80 - 1, 80 - 1, 3, 3, L.PIX_SRC, pixsq, 0, 0);
        pixc.Rasterop(40 - 1, 160 - 1, 3, 3, L.PIX_SRC, pixsq, 0, 0);
        pixa.AddPix(pixc, L.L_INSERT);
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 5 */

        pixd = pixa.DisplayTiledInColumns(2, scalefact, 20, 0);
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 6 */

        Assert.Equal(6, rp.Index);
        rp.Cleanup();
    }
}
