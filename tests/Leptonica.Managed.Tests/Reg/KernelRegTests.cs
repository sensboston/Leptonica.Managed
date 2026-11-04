using System.Text;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// kernel_reg.c: kernel creation, I/O, display and convolution.
/// </summary>
/// <remarks>
/// The index comments are the real check indices; in kernel_reg.c the comments
/// after index 11 are off by one (there is no check between pixCompareGray and
/// the first jpeg).
/// </remarks>
public class KernelRegTests
{
    private const string kdatastr = " 20.3    50   80  50   20 " +
                                    " 51.4   100  140  100  50 " +
                                    " 92.5   160  200  160  90 " +
                                    " 53.7   100  140  100  50 " +
                                    " 24.9    50   80   50  20 ";

    [RegFact]
    public void Kernel()
    {
        string str;
        int i, j, same, ok;
        float sum;
        L_Kernel kel1, kel2, kel3, kel4, kelx, kely;
        Box box;
        Pix pix, pix1, pix2, pix3, pixs, pixb, pixg, pixd;
        Sarray sa;
        var rp = new RegTest("kernel");

            /* Test creating from a string */
        kel1 = L_Kernel.CreateFromString(5, 5, 2, 2, kdatastr);
        pixd = kel1.DisplayInPix(41, 2);
        Pix.WritePng("/tmp/lept/regout/pixkern.png", pixd, 0.0f);
        rp.CheckFile("/tmp/lept/regout/pixkern.png");  /* 0 */

            /* Test read/write for kernel.  Note that both get
             * compared to the same golden file, which is
             * overwritten with a copy of /tmp/lept/regout/kern2.kel */
        kel1 = L_Kernel.CreateFromString(5, 5, 2, 2, kdatastr);
        kel1.Write("/tmp/lept/regout/kern1.kel");
        rp.CheckFile("/tmp/lept/regout/kern1.kel");  /* 1 */
        kel2 = L_Kernel.Read("/tmp/lept/regout/kern1.kel");
        kel2.Write("/tmp/lept/regout/kern2.kel");
        rp.CheckFile("/tmp/lept/regout/kern2.kel");  /* 2 */
        rp.CompareFiles(1, 2);  /* 3 */

            /* Test creating from a file */
        sa = Sarray.Create(0);
        sa.AddString("# small 3x3 kernel", L.L_COPY);
        sa.AddString("3 5", L.L_COPY);
        sa.AddString("1 2", L.L_COPY);
        sa.AddString("20.5   50   80    50   20", L.L_COPY);
        sa.AddString("82.    120  180   120  80", L.L_COPY);
        sa.AddString("22.1   50   80    50   20", L.L_COPY);
        str = sa.ToString(1);
        Utils.BinaryWrite("/tmp/lept/regout/kernfile.kel", "w", Encoding.ASCII.GetBytes(str));
        kel2 = L_Kernel.CreateFromFile("/tmp/lept/regout/kernfile.kel");
        pixd = kel2.DisplayInPix(41, 2);
        Pix.WritePng("/tmp/lept/regout/ker1.png", pixd, 0.0f);
        rp.CheckFile("/tmp/lept/regout/ker1.png");  /* 4 */

            /* Test creating from a pix */
        pix1 = Pix.Create(5, 3, 8);
        pix1.SetPixel(0, 0, 20);
        pix1.SetPixel(1, 0, 50);
        pix1.SetPixel(2, 0, 80);
        pix1.SetPixel(3, 0, 50);
        pix1.SetPixel(4, 0, 20);
        pix1.SetPixel(0, 1, 80);
        pix1.SetPixel(1, 1, 120);
        pix1.SetPixel(2, 1, 180);
        pix1.SetPixel(3, 1, 120);
        pix1.SetPixel(4, 1, 80);
        pix1.SetPixel(0, 0, 20);
        pix1.SetPixel(1, 2, 50);
        pix1.SetPixel(2, 2, 80);
        pix1.SetPixel(3, 2, 50);
        pix1.SetPixel(4, 2, 20);
        kel3 = L_Kernel.CreateFromPix(pix1, 1, 2);
        pixd = kel3.DisplayInPix(41, 2);
        Pix.WritePng("/tmp/lept/regout/ker2.png", pixd, 0.0f);
        rp.CheckFile("/tmp/lept/regout/ker2.png");  /* 5 */

            /* Test convolution with kel1 */
        pixs = TestPaths.ReadInput("test24.jpg");
        pixg = pixs.ScaleRGBToGrayFast(3, L.COLOR_GREEN);
        kel1 = L_Kernel.CreateFromString(5, 5, 2, 2, kdatastr);
        pixd = pixg.Convolve(kel1, 8, 1);
        Pix.WritePng("/tmp/lept/regout/ker3.png", pixd, 0.0f);
        rp.CheckFile("/tmp/lept/regout/ker3.png");  /* 6 */

            /* Test convolution with flat rectangular kel; also test
             * block convolution with tiling. */
        pixs = TestPaths.ReadInput("test24.jpg");
        pixg = pixs.ScaleRGBToGrayFast(3, L.COLOR_GREEN);
        kel2 = L_Kernel.MakeFlatKernel(11, 11, 5, 5);
        pixd = pixg.Convolve(kel2, 8, 1);
        Pix.WritePng("/tmp/lept/regout/ker4.png", pixd, 0.0f);
        rp.CheckFile("/tmp/lept/regout/ker4.png");  /* 7 */
        pix1 = pixg.Blockconv(5, 5);
        Pix.WritePng("/tmp/lept/regout/ker5.png", pix1, 0.0f);
        rp.CheckFile("/tmp/lept/regout/ker5.png");  /* 8 */
        pix2 = pixg.BlockconvTiled(5, 5, 3, 6);
        Pix.WritePng("/tmp/lept/regout/ker5a.png", pix2, 0.0f);
        rp.CheckFile("/tmp/lept/regout/ker5a.png");  /* 9 */

        ok = 1;
        for (i = 1; i <= 7; i++) {
            for (j = 1; j <= 7; j++) {
                if (i == 1 && j == 1) continue;
                pix2 = pixg.BlockconvTiled(5, 5, j, i);
                same = Pix.Equal(pix2, pixd) ? 1 : 0;
                if (same == 0)
                    ok = 0;
            }
        }
        Assert.Equal(1, ok);  /* tiled results identical to pixConvolve() */

            /* Do another flat rectangular test; this time with white at edge.
             * About 1% of the pixels near the image edge differ by 1 between
             * the pixConvolve() and pixBlockconv().  For what it's worth,
             * pixConvolve() gives the more accurate result; namely, 255 for
             * pixels at the edge. */
        pix = TestPaths.ReadInput("pageseg1.tif");
        box = Box.Create(100, 100, 2260, 3160);
        pixb = pix.ClipRectangle(box, out _)!;
        pixs = pixb.ScaleToGray4();

        kel3 = L_Kernel.MakeFlatKernel(7, 7, 3, 3);
        pix1 = pixs.Convolve(kel3, 8, 1);
        Pix.WritePng("/tmp/lept/regout/conv1.png", pix1, 0.0f);
        rp.CheckFile("/tmp/lept/regout/conv1.png");  /* 10 */

        pix2 = pixs.Blockconv(3, 3);
        Pix.WritePng("/tmp/lept/regout/conv2.png", pix2, 0.0f);  /* ditto */
        rp.CheckFile("/tmp/lept/regout/conv2.png");  /* 11 */

            /* Do yet another set of flat rectangular tests, this time
             * on an RGB image */
        pixs = TestPaths.ReadInput("test24.jpg");
        kel4 = L_Kernel.MakeFlatKernel(7, 7, 3, 3);
        pix1 = pixs.ConvolveRGB(kel4);
        Pix.Write("/tmp/lept/regout/conv4.jpg", pix1, L.IFF_JFIF_JPEG);
        rp.CheckFile("/tmp/lept/regout/conv4.jpg");  /* 12 */

        kelx = L_Kernel.MakeFlatKernel(1, 7, 0, 3);
        kely = L_Kernel.MakeFlatKernel(7, 1, 3, 0);
        pix2 = pixs.ConvolveRGBSep(kelx, kely);
        Pix.Write("/tmp/lept/regout/conv5.jpg", pix2, L.IFF_JFIF_JPEG);
        rp.CheckFile("/tmp/lept/regout/conv5.jpg");  /* 13 */

        pix3 = pixs.Blockconv(3, 3);
        Pix.Write("/tmp/lept/regout/conv6.jpg", pix3, L.IFF_JFIF_JPEG);
        rp.CheckFile("/tmp/lept/regout/conv6.jpg");  /* 14 */
        rp.ComparePix(pix1, pix2);  /* 15 */
        rp.CompareSimilarPix(pix2, pix3, 15, 0.0005f, 0);  /* 16 */

            /* Test generation and convolution with gaussian kernel */
        pixs = TestPaths.ReadInput("test8.jpg");
        kel1 = L_Kernel.MakeGaussianKernel(5, 5, 3.0f, 5.0f);
        sum = kel1.GetSum();
        pix1 = pixs.Convolve(kel1, 8, 1);
        pix2 = pixs.Convolve(kel1, 16, 0);
        Pix.WritePng("/tmp/lept/regout/ker6.png", pix1, 0.0f);
        rp.CheckFile("/tmp/lept/regout/ker6.png");  /* 17 */

            /* Test generation and convolution with separable gaussian kernel */
        pixs = TestPaths.ReadInput("test8.jpg");
        L_Kernel.MakeGaussianKernelSep(5, 5, 3.0f, 5.0f, out kelx, out kely);
        pix1 = pixs.ConvolveSep(kelx, kely, 8, 1);
        pix2 = pixs.ConvolveSep(kelx, kely, 16, 0);
        Pix.WritePng("/tmp/lept/regout/ker7.png", pix1, 0.0f);
        rp.CheckFile("/tmp/lept/regout/ker7.png");  /* 18 */

            /* Test generation and convolution with diff of gaussians kernel */
        pixs = TestPaths.ReadInput("test8.jpg");
        kel1 = L_Kernel.MakeDoGKernel(7, 7, 1.5f, 2.7f);
        pix1 = pixs.Convolve(kel1, 8, 0);
        Pix.WritePng("/tmp/lept/regout/ker8.png", pix1, 0.0f);
        rp.CheckFile("/tmp/lept/regout/ker8.png");  /* 19 */

        Assert.Equal(19, rp.Index);
        rp.Cleanup();
    }
}
