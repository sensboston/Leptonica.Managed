using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// newspaper_reg.c: segmenting newspaper articles using morphology; most of the
/// work is done at 4x reduction.
/// </summary>
public class NewspaperRegTests
{
    [RegFact]
    public void Newspaper()
    {
        int w, h;
        Boxa boxa;
        Pix pixs, pixt, pix1, pix2, pix3, pix4, pix5;
        Pix pix6, pix7, pix8, pix9, pix10, pix11;
        Pixa pixa1, pixa2;
        PixColormap cmap;
        var rp = new RegTest("newspaper");

        pixs = TestPaths.ReadInput("scots-frag.tif");
        pixa1 = Pixa.Create(12);

        pixt = pixs.ScaleToGray4();
        pixa1.AddPix(pixt, L.L_INSERT);
        rp.WritePixAndCheck(pixt, L.IFF_JFIF_JPEG);  /* 0 */

            /* Rank reduce 2x */
        pix1 = pixs.ReduceRankBinary2(2);
        pixt = pix1.Scale(0.5f, 0.5f);
        pixa1.AddPix(pixt, L.L_INSERT);

            /* Open out the vertical lines */
        pix2 = pix1.MorphSequence("o1.50", 0);
        pixt = pix2.Scale(0.5f, 0.5f);
        pixa1.AddPix(pixt, L.L_INSERT);
        rp.WritePixAndCheck(pixt, L.IFF_TIFF_G4);  /* 1 */

            /* Seedfill back to get those lines in their entirety */
        pix3 = Pix.SeedfillBinary(null, pix2, pix1, 8);
        pixt = pix3.Scale(0.5f, 0.5f);
        pixa1.AddPix(pixt, L.L_INSERT);
        rp.WritePixAndCheck(pixt, L.IFF_TIFF_G4);  /* 2 */

            /* Remove the vertical lines (and some of the images) */
        Pix.Xor(pix2, pix1, pix3);
        pixt = pix2.Scale(0.5f, 0.5f);
        pixa1.AddPix(pixt, L.L_INSERT);
        rp.WritePixAndCheck(pixt, L.IFF_TIFF_G4);  /* 3 */

            /* Open out the horizontal lines */
        pix4 = pix2.MorphSequence("o50.1", 0);
        pixt = pix4.Scale(0.5f, 0.5f);
        pixa1.AddPix(pixt, L.L_INSERT);

            /* Seedfill back to get those lines in their entirety */
        pix5 = Pix.SeedfillBinary(null, pix4, pix2, 8);
        pixt = pix5.Scale(0.5f, 0.5f);
        pixa1.AddPix(pixt, L.L_INSERT);
        rp.WritePixAndCheck(pixt, L.IFF_TIFF_G4);  /* 4 */

            /* Remove the horizontal lines */
        Pix.Xor(pix4, pix2, pix5);
        pixt = pix4.Scale(0.5f, 0.5f);
        pixa1.AddPix(pixt, L.L_INSERT);
        rp.WritePixAndCheck(pixt, L.IFF_TIFF_G4);  /* 5 */

            /* Invert and identify vertical gutters between text columns */
        pix6 = pix4.ReduceRankBinaryCascade(1, 1, 0, 0);
        Pix.Invert(pix6, pix6);
        pixt = pix6.Scale(2.0f, 2.0f);
        pixa1.AddPix(pixt, L.L_INSERT);
        rp.WritePixAndCheck(pixt, L.IFF_TIFF_G4);  /* 6 */
        pix7 = pix6.MorphSequence("o1.50", 0);
        pixt = pix7.Scale(2.0f, 2.0f);
        pixa1.AddPix(pixt, L.L_INSERT);
        rp.WritePixAndCheck(pixt, L.IFF_TIFF_G4);  /* 7 */
        pix8 = pix7.ExpandBinaryPower2(4);  /* gutter mask */
        rp.WritePixAndCheck(pix8, L.IFF_TIFF_G4);  /* 8 */

            /* Solidify text blocks */
        pix9 = pix4.MorphSequence("c50.1 + c1.10", 0);
        Pix.Subtract(pix9, pix9, pix8);  /* preserve gutter */
        pix10 = pix9.MorphSequence("d3.3", 0);
        pixt = pix10.Scale(0.5f, 0.5f);
        pixa1.AddPix(pixt, L.L_INSERT);
        rp.WritePixAndCheck(pixt, L.IFF_TIFF_G4);  /* 9 */

            /* Show stuff under this mask */
        w = pix10.Width;
        h = pix10.Height;
        boxa = pix10.ConnComp(out pixa2, 8);
        pix11 = pixa2.DisplayRandomCmap(w, h);
        pix11.PaintThroughMask(pix4, 0, 0, 0);
        pixt = pix11.Scale(0.5f, 0.5f);
        pixa1.AddPix(pixt, L.L_INSERT);
        rp.WritePixAndCheck(pixt, L.IFF_PNG);  /* 10 */

            /* Paint the background white */
        cmap = pix11.Colormap!;
        cmap.ResetColor(0, 255, 255, 255);
        rp.WritePixAndCheck(pix11, L.IFF_PNG);  /* 11 */
        pixt = pix11.Scale(0.5f, 0.5f);
        pixa1.AddPix(pixt, L.L_INSERT);
        rp.WritePixAndCheck(pixt, L.IFF_PNG);  /* 12 */

        Assert.Equal(12, rp.Index);
        rp.Cleanup();
    }
}
