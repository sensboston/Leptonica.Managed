using Leptonica;
using Xunit.Abstractions;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// rasterop_reg.c: dilation with a brick sel against dilation by painting
/// a solid brick with pixRasterop() at each foreground pixel, for 63 sizes.
/// </summary>
public class RasteropRegTests
{
    private readonly ITestOutputHelper _output;

    public RasteropRegTests(ITestOutputHelper output) { _output = output; }

    [RegFact]
    public void Rasterop()
    {
        var rp = new RegTest("rasterop");

        Pix pix0 = TestPaths.ReadInput("feyn-fract.tif");
        Box box = Box.Create(293, 37, pix0.Width - 691, pix0.Height - 145);
        Pix pixs = pix0.ClipRectangle(box, out _)!;
        _output.WriteLine($"pixs {pixs.Width}x{pixs.Height}x{pixs.Depth}, fg {pixs.CountPixels(null)}");

        // Test 63 different sizes
        for (int width = 1; width <= 25; width += 3) {   // 9 values
            for (int height = 1; height <= 25; height += 4) {  // 7 values

                int cx = width / 2;
                int cy = height / 2;

                // Dilate using an actual sel
                Sel sel = Sel.CreateBrick(height, width, cy, cx, L.SEL_HIT);
                Pix pixd1 = Pix.Dilate(null, pixs, sel);

                // Dilate using a pix as a sel
                Pix pixse = Pix.Create(width, height, 1);
                pixse.SetAll();
                Pix pixd2 = Pix.Copy(null, pixs);
                int w = pixs.Width;
                int h = pixs.Height;
                for (int i = 0; i < h; i++) {
                    for (int j = 0; j < w; j++) {
                        pixs.GetPixel(j, i, out uint val);
                        if (val != 0)
                            pixd2.Rasterop(j - cx, i - cy, width, height,
                                           L.PIX_SRC | L.PIX_DST, pixse, 0, 0);
                    }
                }

                bool same = Pix.Equal(pixd1, pixd2);
                rp.CompareValues(1, same ? 1 : 0, 0.0f);  // 0 - 62
                if (!same)
                    _output.WriteLine($"Results differ for SE (width,height) = ({width},{height})");
            }
        }
        Assert.Equal(62, rp.Index);
        rp.Cleanup();
    }
}
