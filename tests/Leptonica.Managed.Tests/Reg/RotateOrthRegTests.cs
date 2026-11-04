using Leptonica;
using Xunit.Abstractions;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// rotateorth_reg.c: successive orthogonal rotations and flips of images
/// of all depths give back the original.
/// </summary>
public class RotateOrthRegTests
{
    private const string BINARY_IMAGE = "test1.png";
    private const string FOUR_BPP_IMAGE = "weasel4.8g.png";
    private const string GRAYSCALE_IMAGE = "test8.jpg";
    private const string COLORMAP_IMAGE = "dreyfus8.png";
    private const string RGB_IMAGE = "marge.jpg";

    private readonly ITestOutputHelper _output;

    public RotateOrthRegTests(ITestOutputHelper output) { _output = output; }

    [RegFact]
    public void RotateOrth()
    {
        var rp = new RegTest("rotateorth");

        _output.WriteLine("Test binary image:");
        Pix pixs = TestPaths.ReadInput(BINARY_IMAGE);
        RotateOrthTest(pixs, rp);
        _output.WriteLine("Test 4 bpp colormapped image:");
        pixs = TestPaths.ReadInput(FOUR_BPP_IMAGE);
        RotateOrthTest(pixs, rp);
        _output.WriteLine("Test grayscale image:");
        pixs = TestPaths.ReadInput(GRAYSCALE_IMAGE);
        RotateOrthTest(pixs, rp);
        _output.WriteLine("Test colormap image:");
        pixs = TestPaths.ReadInput(COLORMAP_IMAGE);
        RotateOrthTest(pixs, rp);
        _output.WriteLine("Test rgb image:");
        pixs = TestPaths.ReadInput(RGB_IMAGE);
        RotateOrthTest(pixs, rp);

        Assert.Equal(19, rp.Index);
        rp.Cleanup();
    }

    private void RotateOrthTest(Pix pixs, RegTest rp)
    {
        Pix pixt, pixd;

            /* Test 4 successive 90 degree rotations */
        pixt = pixs.Rotate90(1);
        pixd = pixt.Rotate90(1);
        pixt = pixd.Rotate90(1);
        pixd = pixt.Rotate90(1);
        rp.ComparePix(pixs, pixd);
        Pix.Xor(pixd, pixd, pixs);
        if (pixd.Zero())
            _output.WriteLine("OK.  Four 90-degree rotations gives I");
        else
            _output.WriteLine($"Failure for four 90-degree rots; count = {pixd.CountPixels(null)}");

            /* Test 2 successive 180 degree rotations */
        pixt = Pix.Rotate180(null, pixs);
        Pix.Rotate180(pixt, pixt);
        rp.ComparePix(pixs, pixt);
        Pix.Xor(pixt, pixt, pixs);
        if (pixt.Zero())
            _output.WriteLine("OK.  Two 180-degree rotations gives I");
        else
            _output.WriteLine($"Failure for two 180-degree rots; count = {pixt.CountPixels(null)}");

            /* Test 2 successive LR flips */
        pixt = Pix.FlipLR(null, pixs);
        Pix.FlipLR(pixt, pixt);
        rp.ComparePix(pixs, pixt);
        Pix.Xor(pixt, pixt, pixs);
        if (pixt.Zero())
            _output.WriteLine("OK.  Two LR flips gives I");
        else
            _output.WriteLine($"Failure for two LR flips; count = {pixt.CountPixels(null)}");

            /* Test 2 successive TB flips */
        pixt = Pix.FlipTB(null, pixs);
        Pix.FlipTB(pixt, pixt);
        rp.ComparePix(pixs, pixt);
        Pix.Xor(pixt, pixt, pixs);
        if (pixt.Zero())
            _output.WriteLine("OK.  Two TB flips gives I");
        else
            _output.WriteLine($"Failure for two TB flips; count = {pixt.CountPixels(null)}");
    }
}
