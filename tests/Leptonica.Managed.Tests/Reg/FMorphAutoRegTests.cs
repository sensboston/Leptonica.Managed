using Leptonica;
using Xunit.Abstractions;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// fmorphauto_reg.c: for every sel of selaAddBasic(), the generated dwa code
/// (pixFMorphopGen_1) gives the same dilation and erosion (both boundary
/// conditions) as the rasterop implementation.  The C program only prints the
/// results; here any difference fails.  Input: feyn-fract.tif (the C program
/// takes the file as an argument).
/// </summary>
[Collection("MorphBoundaryCondition")]
public class FMorphAutoRegTests
{
    private readonly ITestOutputHelper _output;

    public FMorphAutoRegTests(ITestOutputHelper output) { _output = output; }

    [RegFact]
    public void FMorphAuto()
    {
        int ndiff = 0;
        Pix pixs = TestPaths.ReadInput("feyn-fract.tif");

        void Check(Pix pixt1, Pix pixt2, string what, int i, string selname) {
            Pix pixt3 = pixt2.RemoveBorder(32);
            Pix pixt4 = Pix.Xor(null, pixt1, pixt3);
            if (!pixt4.Zero()) {
                ndiff++;
                _output.WriteLine($"{what} differ for sel {i} ({selname})");
                _output.WriteLine($"Number of pixels in XOR: {pixt4.CountPixels(null)}");
            }
        }

        try {
            Sela sela = Sela.AddBasic(null);
            int nsels = sela.Count;
            for (int i = 0; i < nsels; i++) {
                Sel sel = sela.GetSel(i);
                string selname = sel.Name!;

                    /*  ---------  dilation  ----------*/
                Pix pixt1 = Pix.Dilate(null, pixs, sel);
                Pix pixs1 = pixs.AddBorder(32, 0);
                Pix pixt2 = Pix.FMorphopGen_1(null, pixs1, L.L_MORPH_DILATE, selname);
                Check(pixt1, pixt2, "dilations", i, selname);

                    /*  ---------  erosion with asymmetric b.c  ----------*/
                Pix.ResetMorphBoundaryCondition(L.ASYMMETRIC_MORPH_BC);
                pixt1 = Pix.Erode(null, pixs, sel);
                if (Pix.MORPH_BC == L.ASYMMETRIC_MORPH_BC)
                    pixs1 = pixs.AddBorder(32, 0);  /* OFF border pixels */
                else
                    pixs1 = pixs.AddBorder(32, 1);  /* ON border pixels */
                pixt2 = Pix.FMorphopGen_1(null, pixs1, L.L_MORPH_ERODE, selname);
                Check(pixt1, pixt2, "erosions", i, selname);

                    /*  ---------  erosion with symmetric b.c  ----------*/
                Pix.ResetMorphBoundaryCondition(L.SYMMETRIC_MORPH_BC);
                pixt1 = Pix.Erode(null, pixs, sel);
                if (Pix.MORPH_BC == L.ASYMMETRIC_MORPH_BC)
                    pixs1 = pixs.AddBorder(32, 0);  /* OFF border pixels */
                else
                    pixs1 = pixs.AddBorder(32, 1);  /* ON border pixels */
                pixt2 = Pix.FMorphopGen_1(null, pixs1, L.L_MORPH_ERODE, selname);
                Check(pixt1, pixt2, "erosions", i, selname);
            }
            Assert.Equal(58, nsels);
        } finally {
            Pix.ResetMorphBoundaryCondition(L.ASYMMETRIC_MORPH_BC);
        }
        Assert.Equal(0, ndiff);
    }
}
