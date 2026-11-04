using System.IO;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// rectangle_reg.c: tests largest rectangle in bg/fg and rectangles in connected components.
/// </summary>
public class RectangleRegTests
{
    private const int NBoxes = 20;
    private const int Polarity = 0;  /* background */

    [RegFact]
    public void Rectangle()
    {
        var rp = new RegTest("rectangle");

        /* ---------------- Largest rectangles in image ---------------- */
        Pix pixs = TestPaths.ReadInput("test1.png");
        Pix pix1 = pixs.ConvertTo8(false);
        PixColormap cmap = PixColormap.CreateRandom(8, true, true);
        pix1.SetColormap(cmap);

        Boxa boxa = Boxa.Create(0);
        for (int i = 0; i < NBoxes; i++) {
            pixs.FindLargestRectangle(Polarity, out Box box1);
            pixs.SetInRect(box1);
            boxa.AddBox(box1, L.L_INSERT);
        }

        for (int i = 0; i < NBoxes; i++) {
            int index = 32 + (i & 254);
            cmap.GetColor(index, out int rval, out int gval, out int bval);
            Box box1 = boxa.GetBox(i, L.L_CLONE);
            pix1.RenderHashBoxArb(box1, 6, 2, L.L_NEG_SLOPE_LINE, 1,
                                  (byte)rval, (byte)gval, (byte)bval);
        }
        Pix pix2 = pix1.AddBorder(2, 0x0u);
        Pix pix3 = pix2.AddBorder(20, 0xffffff00u);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 0 */

        /* ----------- Rectangle(s) from connected component ----------- */
        pixs = TestPaths.ReadInput("singlecc.tif");
        pix1 = pixs.Scale(0.5f, 0.5f);
        boxa = pix1.ConnCompBB(8);
        Box boxCc = boxa.GetBox(0, L.L_COPY);

        int[] dirs = { L.L_SCAN_VERTICAL, L.L_SCAN_HORIZONTAL };
        int[] selects = {
            L.L_GEOMETRIC_UNION, L.L_GEOMETRIC_INTERSECTION,
            L.L_LARGEST_AREA, L.L_SMALLEST_AREA
        };
        int fileNo = 2;
        foreach (int dir in dirs) {
            foreach (int sel in selects) {
                pix1.FindRectangleInCC(boxCc, 0.75f, dir, sel, 1);
                string buf = $"rectangle.{fileNo++:D2}.png";
                Utils.LeptCp("/tmp/lept/rect/fitrect.png", "lept/regout", buf, out string newpath);
                rp.CheckFile(newpath);  /* 1 - 8 */
            }
        }

        Assert.Equal(8, rp.Index);
        rp.Cleanup();
    }

    [RegFact]
    public void LargestRectanglesAndFindRectangleInCCNoPdf()
    {
        CRand.Seed(1);
        Pix pixs = TestPaths.ReadInput("test1.png");
        Pix pix1 = pixs.ConvertTo8(false);
        PixColormap cmap = PixColormap.CreateRandom(8, true, true);
        pix1.SetColormap(cmap);

        Boxa boxa = Boxa.Create(0);
        for (int i = 0; i < NBoxes; i++) {
            pixs.FindLargestRectangle(Polarity, out Box box1);
            pixs.SetInRect(box1);
            boxa.AddBox(box1, L.L_INSERT);
        }

        for (int i = 0; i < NBoxes; i++) {
            int index = 32 + (i & 254);
            cmap.GetColor(index, out int rval, out int gval, out int bval);
            Box box1 = boxa.GetBox(i, L.L_CLONE);
            pix1.RenderHashBoxArb(box1, 6, 2, L.L_NEG_SLOPE_LINE, 1,
                                  (byte)rval, (byte)gval, (byte)bval);
        }
        Pix pix2 = pix1.AddBorder(2, 0x0u);
        Pix pix3 = pix2.AddBorder(20, 0xffffff00u);

        Pix pixGolden;
        lock (TestPaths.PngReadLock)
            pixGolden = Pix.Read(Path.Combine(TestPaths.GoldenDir!, "rectangle_golden.00.png"));
        Assert.True(Pix.Equal(pixGolden, pix3));

        pixs = TestPaths.ReadInput("singlecc.tif");
        pix1 = pixs.Scale(0.5f, 0.5f);
        boxa = pix1.ConnCompBB(8);
        Box boxCc = boxa.GetBox(0, L.L_COPY);
        foreach (int dir in new[] { L.L_SCAN_VERTICAL, L.L_SCAN_HORIZONTAL }) {
            foreach (int sel in new[] {
                L.L_GEOMETRIC_UNION, L.L_GEOMETRIC_INTERSECTION,
                L.L_LARGEST_AREA, L.L_SMALLEST_AREA
            }) {
                Box? b = pix1.FindRectangleInCC(boxCc, 0.75f, dir, sel, 0);
                if (sel != L.L_GEOMETRIC_INTERSECTION) {
                    Assert.NotNull(b);
                    Assert.True(b!.Width > 0 && b.Height > 0);
                }
            }
        }
    }
}
