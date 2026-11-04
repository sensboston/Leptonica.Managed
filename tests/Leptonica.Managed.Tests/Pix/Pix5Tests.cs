using System;
using Leptonica;

namespace Leptonica.Managed.Tests.PixCore;

/// <summary>
/// Tests of pix5.c with hand-computed expected values on small images.  Tests of
/// functions whose callees are still stubs are skipped until those files are ported.
/// </summary>
public class Pix5Tests
{

    private static Pix Make(int w, int h, int d, params int[] values)
    {
        Pix pix = Pix.Create(w, h, d);
        if (values.Length > 0) {
            Assert.Equal(w * h, values.Length);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    pix.SetPixel(x, y, (uint)values[y * w + x]);
        }
        return pix;
    }

    private static uint Get(Pix pix, int x, int y)
    {
        pix.GetPixel(x, y, out uint val);
        return val;
    }

    private static int CountOn(Pix pix)
    {
        pix.GetDimensions(out int w, out int h, out _);
        int count = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                count += (int)Get(pix, x, y);
        return count;
    }

    private static (int, int, int, int) G(Box box)
    {
        box.GetGeometry(out int x, out int y, out int w, out int h);
        return (x, y, w, h);
    }

    private static int[] Values(Numa na)
    {
        int[] vals = new int[na.Count];
        for (int i = 0; i < vals.Length; i++)
            vals[i] = na.GetIValue(i);
        return vals;
    }

    /* A 1 bpp image with the given fg rectangles */
    private static Pix Rects(int w, int h, params (int x, int y, int w, int h)[] rects)
    {
        Pix pix = Pix.Create(w, h, 1);
        foreach (var r in rects)
            for (int y = r.y; y < r.y + r.h; y++)
                for (int x = r.x; x < r.x + r.w; x++)
                    pix.SetPixel(x, y, 1);
        return pix;
    }

    /*------------------------ Measurement of properties ----------------------*/

    [Fact]
    public void FindAreaFraction_CountsFgOverArea()
    {
        Pix pix = Rects(8, 5, (1, 1, 3, 2), (6, 4, 1, 1));  /* 6 + 1 = 7 fg of 40 */
        Assert.Equal(7f / 40f, pix.FindAreaFraction(null));
        Assert.Equal(0f, Pix.Create(8, 5, 1).FindAreaFraction(null));
        Assert.Throws<ArgumentException>(() => Make(3, 3, 8).FindAreaFraction(null));
    }

    [Fact]
    public void FindPerimToAreaRatio_SolidRectangle()
    {
        /* 5 x 4 solid rectangle, 3 x 3 erosion leaves 3 x 2 = 6 interior pixels:
         * 20 fg, 14 boundary pixels */
        Pix pix = Rects(9, 8, (2, 2, 5, 4));
        Assert.Equal(14f / 20f, pix.FindPerimToAreaRatio(null));
        Assert.Equal(6f / 14f, pix.FindAreaPerimRatio(null));
        Assert.Equal((0.5f * 14) / (float)(9 + 8), pix.FindPerimSizeRatio(null));
        Assert.Equal(0f, Pix.Create(9, 8, 1).FindPerimToAreaRatio(null));
    }

    [Fact]
    public void FindAreaFractionMasked_UsesBoxOffset()
    {
        /* component 3 x 2, all fg; mask covers the columns 10..11 of a 20 x 10 image */
        Pix comp = Rects(3, 2, (0, 0, 3, 2));
        Pix mask = Rects(20, 10, (10, 3, 2, 10));
        /* component placed at x = 9, y = 3: its columns 1 and 2 (rows 0..1) fall under the mask */
        Assert.Equal(4f / 6f, comp.FindAreaFractionMasked(Box.Create(9, 3, 3, 2), mask, null));
        /* box == null aligns the UL corners: mask is empty there */
        Assert.Equal(0f, comp.FindAreaFractionMasked(null, mask, null));
        Assert.Equal(0f, Pix.Create(3, 2, 1).FindAreaFractionMasked(null, mask, null));
    }

    [Fact]
    public void FindOverlapFraction_IntersectionOverUnion()
    {
        /* pix2 has the size of pix1 and is placed at the origin */
        Pix pix1 = Rects(8, 8, (0, 0, 4, 4));      /* 16 pixels */
        Pix pix2 = Rects(8, 8, (2, 2, 4, 4));      /* 16 pixels */
        pix1.FindOverlapFraction(pix2, 0, 0, null, out float ratio, out int noverlap);
        /* intersection 2 x 2 = 4, union 16 + 16 - 4 = 28 */
        Assert.Equal(4, noverlap);
        Assert.Equal(4f / 28f, ratio);

        Pix.Create(10, 10, 1).FindOverlapFraction(Pix.Create(4, 4, 1), 0, 0, null,
                                                  out ratio, out noverlap);
        Assert.Equal(0f, ratio);
        Assert.Equal(0, noverlap);
    }

    [Fact]
    public void FindOverlapFraction_UpstreamCountsPixelsOutsideFootprint()
    {
        /* The rasterop ANDs only the footprint of pixs2 (4 x 4 at (2, 2)); fg pixels of pixs1
         * outside the footprint stay in the "intersection": 12 outside + 4 inside = 16. */
        Pix pix1 = Rects(10, 10, (0, 0, 4, 4));
        Pix pix2 = Rects(4, 4, (0, 0, 4, 4));
        pix1.FindOverlapFraction(pix2, 2, 2, null, out float ratio, out int noverlap);
        Assert.Equal(16, noverlap);
        Assert.Equal(16f / 28f, ratio);
    }

    [Fact]
    public void ConformsToRectangle_RectangleAndNotch()
    {
        Pix full = Rects(10, 8, (0, 0, 10, 8));
        Assert.True(full.ConformsToRectangle(null, 1));

        /* A notch of bg that reaches 3 pixels into the component from the top edge */
        Pix notch = Rects(10, 8, (0, 0, 10, 8));
        for (int y = 0; y < 3; y++)
            notch.SetPixel(5, y, 0);
        Assert.False(notch.ConformsToRectangle(null, 1));
        Assert.True(notch.ConformsToRectangle(null, 3));

        /* dist too large: automatic conformation */
        Assert.True(notch.ConformsToRectangle(null, 4));
    }

    [Fact]
    public void FindRectangleComps_NeedsConnComp()
    {
        Pix pix = Rects(30, 20, (1, 1, 8, 6), (15, 5, 10, 10));
        Boxa boxa = pix.FindRectangleComps(1, 4, 4);
        Assert.Equal(2, boxa.Count);
    }

    /*----------------------- Extract rectangular regions ---------------------*/

    private static Pix Ramp(int w, int h)
    {
        Pix pix = Pix.Create(w, h, 8);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pix.SetPixel(x, y, (uint)(10 * y + x));
        return pix;
    }

    [Fact]
    public void ClipRectangle_InsideBox()
    {
        Pix pix = Ramp(6, 4);
        Pix? clip = pix.ClipRectangle(Box.Create(1, 1, 3, 2), out Box? boxc);
        Assert.NotNull(clip);
        Assert.Equal((1, 1, 3, 2), G(boxc!));
        clip!.GetDimensions(out int w, out int h, out int d);
        Assert.Equal((3, 2, 8), (w, h, d));
        Assert.Equal(11u, Get(clip, 0, 0));
        Assert.Equal(13u, Get(clip, 2, 0));
        Assert.Equal(21u, Get(clip, 0, 1));
        Assert.Equal(23u, Get(clip, 2, 1));
    }

    [Fact]
    public void ClipRectangle_ClipsBoxToPix()
    {
        Pix pix = Ramp(6, 4);
        Pix? clip = pix.ClipRectangle(Box.Create(4, 2, 5, 5), out Box? boxc);
        Assert.Equal((4, 2, 2, 2), G(boxc!));
        clip!.GetDimensions(out int w, out int h, out _);
        Assert.Equal((2, 2), (w, h));
        Assert.Equal(24u, Get(clip, 0, 0));
        Assert.Equal(35u, Get(clip, 1, 1));

        /* box with a negative origin */
        clip = pix.ClipRectangle(Box.Create(-2, -1, 4, 3), out boxc);
        Assert.Equal((0, 0, 2, 2), G(boxc!));
        Assert.Equal(0u, Get(clip!, 0, 0));
        Assert.Equal(11u, Get(clip!, 1, 1));
    }

    [Fact]
    public void ClipRectangle_NoOverlapReturnsNull()
    {
        Pix pix = Ramp(6, 4);
        Assert.Null(pix.ClipRectangle(Box.Create(10, 10, 2, 2), out Box? boxc));
        Assert.Null(boxc);
        Assert.Null(pix.ClipRectangle(Box.Create(6, 0, 2, 2), out boxc));  /* x == w */
    }

    [Fact]
    public void ClipRectangle_CopiesColormapAndResolution()
    {
        Pix pix = Make(4, 4, 8);
        pix.XRes = 300;
        pix.YRes = 200;
        PixColormap cmap = PixColormap.Create(8);
        pix.SetColormap(cmap);
        Pix clip = pix.ClipRectangle(Box.Create(0, 0, 2, 2), out _)!;
        Assert.NotNull(clip.Colormap);
        Assert.NotSame(cmap, clip.Colormap);
        Assert.Equal(300, clip.XRes);
        Assert.Equal(200, clip.YRes);
    }

    [Fact]
    public void ClipRectangleWithBorder_UnderclipsByCommonBorder()
    {
        Pix pix = Pix.Create(20, 20, 8);
        /* bord = min(5, 6, 20 - 5 - 4, 20 - 6 - 3, maxbord = 2) = 2 */
        Pix clip = pix.ClipRectangleWithBorder(Box.Create(5, 6, 4, 3), 2, out Box? boxn)!;
        clip.GetDimensions(out int w, out int h, out _);
        Assert.Equal((8, 7), (w, h));
        Assert.Equal((2, 2, 4, 3), G(boxn!));

        /* maxbord is large: bord = min(5, 6, 11, 11) = 5 */
        clip = pix.ClipRectangleWithBorder(Box.Create(5, 6, 4, 3), 10, out boxn)!;
        clip.GetDimensions(out w, out h, out _);
        Assert.Equal((14, 13), (w, h));
        Assert.Equal((5, 5, 4, 3), G(boxn!));
    }

    [Fact]
    public void ClipRectangleWithBorder_NoBorderAtImageEdge()
    {
        Pix pix = Pix.Create(20, 20, 8);
        Pix clip = pix.ClipRectangleWithBorder(Box.Create(0, 3, 4, 4), 5, out Box? boxn)!;
        clip.GetDimensions(out int w, out int h, out _);
        Assert.Equal((4, 4), (w, h));
        Assert.Equal((0, 0, 4, 4), G(boxn!));
    }

    [Fact]
    public void ClipMasked_PaintsOutval()
    {
        Pix pix = Make(4, 2, 8, 1, 2, 3, 4, 5, 6, 7, 8);
        Pix mask = Rects(2, 2, (0, 0, 1, 2));
        Pix clip = pix.ClipMasked(mask, 1, 0, 255)!;
        Assert.Equal(2u, Get(clip, 0, 0));
        Assert.Equal(255u, Get(clip, 1, 0));
        Assert.Equal(6u, Get(clip, 0, 1));
        Assert.Equal(255u, Get(clip, 1, 1));
    }

    [Fact]
    public void CropToSize_CropsOrClones()
    {
        Pix pix = Ramp(8, 6);
        Pix crop = pix.CropToSize(5, 10);
        crop.GetDimensions(out int w, out int h, out _);
        Assert.Equal((5, 6), (w, h));
        Assert.Equal(44u, Get(crop, 4, 4));

        crop = pix.CropToSize(3, 2);
        crop.GetDimensions(out w, out h, out _);
        Assert.Equal((3, 2), (w, h));
        Assert.Equal(12u, Get(crop, 2, 1));

        Assert.Same(pix, pix.CropToSize(8, 6));
        Assert.Same(pix, pix.CropToSize(100, 100));
    }

    [Fact]
    public void CropToMatch_UsesMinimumSize()
    {
        Pix pix1 = Ramp(8, 4);
        Pix pix2 = Ramp(5, 6);
        pix1.CropToMatch(pix2, out Pix d1, out Pix d2);
        d1.GetDimensions(out int w1, out int h1, out _);
        d2.GetDimensions(out int w2, out int h2, out _);
        Assert.Equal((5, 4), (w1, h1));
        Assert.Equal((5, 4), (w2, h2));
        Assert.NotSame(pix1, d1);
        Assert.NotSame(pix2, d2);

        /* equal sizes return clones */
        pix2.CropToMatch(Ramp(5, 6), out d1, out d2);
        Assert.Same(pix2, d1);
    }

    [Fact]
    public void ResizeToMatch_ReplicatesLastColumnAndRow()
    {
        Pix pix = Make(3, 2, 8, 1, 2, 3, 4, 5, 6);
        Pix big = pix.ResizeToMatch(null, 5, 4);
        int[] expected = {
            1, 2, 3, 3, 3,
            4, 5, 6, 6, 6,
            4, 5, 6, 6, 6,
            4, 5, 6, 6, 6 };
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 5; x++)
                Assert.Equal((uint)expected[y * 5 + x], Get(big, x, y));

        /* use the size of pixt; extend only in width, crop in height */
        Pix wide = pix.ResizeToMatch(Pix.Create(4, 1, 8), 0, 0);
        wide.GetDimensions(out int w, out int h, out _);
        Assert.Equal((4, 1), (w, h));
        Assert.Equal(new uint[] { 1, 2, 3, 3 },
                     new uint[] { Get(wide, 0, 0), Get(wide, 1, 0), Get(wide, 2, 0), Get(wide, 3, 0) });

        /* same size: a copy */
        Pix same = pix.ResizeToMatch(null, 3, 2);
        Assert.NotSame(pix, same);
        Assert.Equal(pix.Data, same.Data);

        Assert.Throws<ArgumentException>(() => pix.ResizeToMatch(null, 0, 4));
    }

    [Fact]
    public void SelectComponentBySize_PicksLargest()
    {
        Pix pix = Rects(30, 20, (1, 1, 8, 6), (15, 5, 10, 10));
        Pix comp = pix.SelectComponentBySize(0, L.L_SELECT_BY_AREA, 8, out Box? box);
        Assert.Equal((15, 5, 10, 10), G(box!));
        comp.GetDimensions(out int w, out int h, out _);
        Assert.Equal((10, 10), (w, h));
        Pix filtered = pix.FilterComponentBySize(-1, L.L_SELECT_BY_AREA, 8, out box);
        Assert.Equal((1, 1, 8, 6), G(box!));
        Assert.Equal(48, CountOn(filtered));
    }

    /*---------------------------- Make special masks -------------------------*/

    [Fact]
    public void MakeFrameMask_FullAndEmpty()
    {
        Pix full = Pix.MakeFrameMask(5, 3, 0f, 1f, 0f, 1f);
        Assert.Equal(15, CountOn(full));
        Pix empty = Pix.MakeFrameMask(5, 3, 0.3f, 0.3f, 0.3f, 0.3f);
        Assert.Equal(0, CountOn(empty));
    }

    [Fact]
    public void MakeFrameMask_RectangleWithoutHole()
    {
        /* h1 = (int)(0.5 * 0.5 * 10) = 2: fg rectangle (2, 2, 6, 6) */
        Pix mask = Pix.MakeFrameMask(10, 10, 0.5f, 1.0f, 0.5f, 1.0f);
        Assert.Equal(36, CountOn(mask));
        Assert.Equal(0u, Get(mask, 1, 5));
        Assert.Equal(1u, Get(mask, 2, 5));
        Assert.Equal(1u, Get(mask, 7, 7));
        Assert.Equal(0u, Get(mask, 8, 7));
        Assert.Equal(0u, Get(mask, 5, 8));
    }

    [Fact]
    public void MakeFrameMask_FrameWithHole()
    {
        /* h2 = (int)(0.5 * 0.5 * 16) = 4, v2 = (int)(0.5 * 0.5 * 8) = 2:
         * 16 x 8 fg minus a hole (4, 2, 8, 4) */
        Pix mask = Pix.MakeFrameMask(16, 8, 0f, 0.5f, 0f, 0.5f);
        Assert.Equal(128 - 32, CountOn(mask));
        Assert.Equal(1u, Get(mask, 3, 3));
        Assert.Equal(0u, Get(mask, 4, 2));
        Assert.Equal(0u, Get(mask, 11, 5));
        Assert.Equal(1u, Get(mask, 12, 5));
        Assert.Equal(1u, Get(mask, 5, 1));
        Assert.Equal(1u, Get(mask, 5, 6));
    }

    [Fact]
    public void MakeFrameMask_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Pix.MakeFrameMask(0, 5, 0f, 1f, 0f, 1f));
        Assert.Throws<ArgumentException>(() => Pix.MakeFrameMask(5, 5, -0.1f, 1f, 0f, 1f));
        Assert.Throws<ArgumentException>(() => Pix.MakeFrameMask(5, 5, 0f, 1.5f, 0f, 1f));
        Assert.Throws<ArgumentException>(() => Pix.MakeFrameMask(5, 5, 0.6f, 0.4f, 0f, 1f));
    }

    [Fact]
    public void MakeSymmetricMask_InnerAndOuter()
    {
        Pix inner = Pix.MakeSymmetricMask(16, 8, 0.5f, 0.5f, L.L_USE_INNER);
        Assert.Equal(32, CountOn(inner));  /* rectangle (4, 2, 8, 4) */
        Assert.Equal(1u, Get(inner, 4, 2));
        Assert.Equal(1u, Get(inner, 11, 5));
        Assert.Equal(0u, Get(inner, 3, 2));
        Assert.Equal(0u, Get(inner, 11, 6));

        Pix outer = Pix.MakeSymmetricMask(16, 8, 0.5f, 0.5f, L.L_USE_OUTER);
        Assert.Equal(96, CountOn(outer));
        Assert.Equal(1u, Get(outer, 0, 0));
        Assert.Equal(0u, Get(outer, 8, 4));

        /* hf == vf == 0: inner covers everything */
        Assert.Equal(16 * 8, CountOn(Pix.MakeSymmetricMask(16, 8, 0f, 0f, L.L_USE_INNER)));

        Assert.Throws<ArgumentException>(() => Pix.MakeSymmetricMask(16, 8, 0.5f, 0.5f, 7));
        Assert.Throws<ArgumentException>(() => Pix.MakeSymmetricMask(16, 8, 1.5f, 0.5f, L.L_USE_INNER));
        Assert.Throws<ArgumentException>(() => Pix.MakeSymmetricMask(0, 8, 0.5f, 0.5f, L.L_USE_INNER));
    }

    [Fact]
    public void MakeCoveringOfRectangles_FillsBoundingBoxes()
    {
        /* an L-shaped component is covered by its bounding box */
        Pix pix = Rects(12, 10, (2, 2, 6, 2), (2, 2, 2, 6));
        Pix cover = pix.MakeCoveringOfRectangles(0);
        Assert.Equal(6 * 6, CountOn(cover));
        Assert.Equal(1u, Get(cover, 7, 7));
        Assert.Equal(0, CountOn(Pix.Create(12, 10, 1).MakeCoveringOfRectangles(0)));
    }

    [Fact]
    public void FractionFgInMask_FractionOfFgUnderMask()
    {
        Pix pix1 = Rects(10, 10, (0, 0, 4, 4));   /* 16 fg */
        Pix pix2 = Rects(10, 10, (2, 0, 8, 4));   /* covers columns 2..3 of pix1: 8 fg */
        Assert.Equal(8f / 16f, pix1.FractionFgInMask(pix2));
        Assert.Equal(0f, Pix.Create(10, 10, 1).FractionFgInMask(pix2));
        Assert.Equal(0f, pix1.FractionFgInMask(Pix.Create(10, 10, 1)));
    }

    /*---------------------------- Clip to foreground -------------------------*/

    [Fact]
    public void ClipToForeground_AcrossWordBoundary()
    {
        /* 70 wide: two full words and 6 extra bits */
        Pix pix = Pix.Create(70, 5, 1);
        pix.SetPixel(33, 1, 1);
        pix.SetPixel(65, 3, 1);
        pix.ClipToForeground(out Pix? clip, out Box? box);
        Assert.Equal((33, 1, 33, 3), G(box!));
        clip!.GetDimensions(out int w, out int h, out _);
        Assert.Equal((33, 3), (w, h));
        Assert.Equal(2, CountOn(clip));
        Assert.Equal(1u, Get(clip, 0, 0));
        Assert.Equal(1u, Get(clip, 32, 2));
    }

    [Fact]
    public void ClipToForeground_EmptyAndPadBits()
    {
        Pix pix = Pix.Create(70, 5, 1);
        pix.ClipToForeground(out Pix? clip, out Box? box);
        Assert.Null(clip);
        Assert.Null(box);

        /* set bits in the pad area of the last word of a line: not foreground */
        pix.Data[2 * pix.Wpl + 2] |= 0x1u;
        pix.ClipToForeground(out clip, out box);
        Assert.Null(clip);
        Assert.Null(box);
    }

    [Fact]
    public void ClipToForeground_SinglePixelAndFullImage()
    {
        Pix pix = Pix.Create(32, 4, 1);
        pix.SetPixel(31, 3, 1);
        pix.ClipToForeground(out Pix? clip, out Box? box);
        Assert.Equal((31, 3, 1, 1), G(box!));
        Assert.Equal(1, CountOn(clip!));

        Pix full = Rects(8, 3, (0, 0, 8, 3));
        full.ClipToForeground(out clip, out box);
        Assert.Equal((0, 0, 8, 3), G(box!));
        Assert.Equal(24, CountOn(clip!));
    }

    [Fact]
    public void TestClipToForeground_EdgesAndUpstreamBottomLine()
    {
        /* fg on all four edges: cannot clip */
        Pix frame = Rects(6, 5, (0, 0, 6, 1), (0, 4, 6, 1), (0, 0, 1, 5), (5, 0, 1, 5));
        Assert.False(frame.TestClipToForeground());

        /* empty top row: can clip */
        Assert.True(Rects(6, 5, (1, 1, 3, 3)).TestClipToForeground());

        /* empty left column */
        Assert.True(Rects(6, 5, (1, 0, 5, 5)).TestClipToForeground());

        /* empty right column */
        Assert.True(Rects(6, 5, (0, 0, 5, 5)).TestClipToForeground());

        /* upstream: the bottom row is never examined (the top row is scanned twice),
         * so an empty bottom row is not detected */
        Pix nobottom = Rects(6, 5, (0, 0, 6, 4));
        Assert.False(nobottom.TestClipToForeground());
    }

    [Fact]
    public void ScanForForeground_AllDirections()
    {
        Pix pix = Rects(20, 20, (5, 6, 1, 1), (12, 9, 1, 1));
        Assert.True(pix.ScanForForeground(null, L.L_FROM_LEFT, out int loc));
        Assert.Equal(5, loc);
        Assert.True(pix.ScanForForeground(null, L.L_FROM_RIGHT, out loc));
        Assert.Equal(12, loc);
        Assert.True(pix.ScanForForeground(null, L.L_FROM_TOP, out loc));
        Assert.Equal(6, loc);
        Assert.True(pix.ScanForForeground(null, L.L_FROM_BOT, out loc));
        Assert.Equal(9, loc);

        /* within a box that holds only the second pixel */
        Box box = Box.Create(10, 8, 6, 6);
        Assert.True(pix.ScanForForeground(box, L.L_FROM_LEFT, out loc));
        Assert.Equal(12, loc);
        Assert.True(pix.ScanForForeground(box, L.L_FROM_TOP, out loc));
        Assert.Equal(9, loc);

        /* no fg in the box */
        Assert.False(pix.ScanForForeground(Box.Create(0, 0, 4, 4), L.L_FROM_LEFT, out loc));
        Assert.Equal(0, loc);

        Assert.Throws<ArgumentException>(() => pix.ScanForForeground(null, 17, out _));
        Assert.Throws<ArgumentException>(() => pix.ScanForForeground(Box.Create(30, 30, 2, 2),
                                                                      L.L_FROM_LEFT, out _));
        Assert.Throws<ArgumentException>(() => Make(4, 4, 8).ScanForForeground(null, L.L_FROM_LEFT, out _));
    }

    [Fact]
    public void ClipBoxToForeground_WithAndWithoutBox()
    {
        Pix pix = Rects(20, 20, (5, 6, 1, 1), (12, 9, 1, 1));

        pix.ClipBoxToForeground(null, out Pix? clip, out Box? boxd);
        Assert.Equal((5, 6, 8, 4), G(boxd!));
        clip!.GetDimensions(out int w, out int h, out _);
        Assert.Equal((8, 4), (w, h));
        Assert.Equal(2, CountOn(clip));

        pix.ClipBoxToForeground(Box.Create(0, 0, 10, 10), out clip, out boxd);
        Assert.Equal((5, 6, 1, 1), G(boxd!));
        Assert.Equal(1, CountOn(clip!));

        /* box larger than the image */
        pix.ClipBoxToForeground(Box.Create(0, 0, 100, 100), out clip, out boxd);
        Assert.Equal((5, 6, 8, 4), G(boxd!));

        /* no fg inside the box */
        pix.ClipBoxToForeground(Box.Create(13, 0, 7, 20), out clip, out boxd);
        Assert.Null(clip);
        Assert.Null(boxd);

        Assert.Throws<ArgumentException>(
            () => pix.ClipBoxToForeground(Box.Create(25, 0, 4, 4), out _, out _));
    }

    [Fact]
    public void ScanForEdge_SharpAndGradualEdges()
    {
        /* fg rectangle x = 5..14, y = 2..7 */
        Pix pix = Rects(20, 10, (5, 2, 10, 6));
        Assert.True(pix.ScanForEdge(null, 3, 6, 2, 1, L.L_FROM_LEFT, out int loc));
        Assert.Equal(5, loc);
        Assert.True(pix.ScanForEdge(null, 3, 6, 2, 1, L.L_FROM_RIGHT, out loc));
        Assert.Equal(14, loc);
        Assert.True(pix.ScanForEdge(null, 3, 6, 2, 1, L.L_FROM_TOP, out loc));
        Assert.Equal(2, loc);
        Assert.True(pix.ScanForEdge(null, 3, 6, 2, 1, L.L_FROM_BOT, out loc));
        Assert.Equal(7, loc);

        /* a faint column at x = 4 (3 pixels) in front of the edge: the edge starts at x = 4
         * if the transition is within maxwidth */
        pix.SetPixel(4, 2, 1);
        pix.SetPixel(4, 3, 1);
        pix.SetPixel(4, 4, 1);
        Assert.True(pix.ScanForEdge(null, 3, 6, 2, 1, L.L_FROM_LEFT, out loc));
        Assert.Equal(4, loc);
        Assert.False(pix.ScanForEdge(null, 3, 6, 1, 1, L.L_FROM_LEFT, out loc));
        Assert.Equal(0, loc);

        /* no column reaches the high threshold */
        Assert.False(pix.ScanForEdge(null, 3, 9, 4, 1, L.L_FROM_LEFT, out _));

        Assert.Throws<ArgumentException>(() => pix.ScanForEdge(null, 0, 6, 2, 1, L.L_FROM_LEFT, out _));
        Assert.Throws<ArgumentException>(() => pix.ScanForEdge(null, 7, 6, 2, 1, L.L_FROM_LEFT, out _));
        Assert.Throws<ArgumentException>(() => pix.ScanForEdge(null, 3, 6, 0, 1, L.L_FROM_LEFT, out _));
        Assert.Throws<ArgumentException>(() => pix.ScanForEdge(null, 3, 6, 2, 1, 42, out _));
    }

    [Fact]
    public void ClipBoxToEdges_FindsRectangle()
    {
        Pix pix = Rects(20, 10, (5, 2, 10, 6));
        pix.ClipBoxToEdges(null, 3, 6, 2, 1, out Pix? clip, out Box? boxd);
        Assert.Equal((5, 2, 10, 6), G(boxd!));
        clip!.GetDimensions(out int w, out int h, out _);
        Assert.Equal((10, 6), (w, h));
        Assert.Equal(60, CountOn(clip));

        /* with a box that excludes part of the image */
        pix.ClipBoxToEdges(Box.Create(0, 0, 12, 10), 3, 6, 2, 1, out clip, out boxd);
        Assert.Equal((5, 2, 7, 6), G(boxd!));
    }

    [Fact]
    public void ClipBoxToEdges_EdgeNotFoundGivesNullOutputs()
    {
        /* too few fg pixels to reach the high threshold */
        Pix pix = Rects(20, 10, (5, 2, 10, 2));
        pix.ClipBoxToEdges(null, 3, 6, 2, 1, out Pix? clip, out Box? boxd);
        Assert.Null(clip);
        Assert.Null(boxd);
    }

    [Fact]
    public void ClipBoxToEdges_ThresholdsOneIsForegroundClip()
    {
        Pix pix = Rects(20, 10, (5, 2, 10, 6), (3, 9, 1, 1));
        pix.ClipBoxToEdges(null, 1, 1, 1, 1, out Pix? clip, out Box? boxd);
        Assert.Equal((3, 2, 12, 8), G(boxd!));
        Assert.NotNull(clip);

        Assert.Throws<ArgumentException>(() => pix.ClipBoxToEdges(null, 0, 1, 1, 1, out _, out _));
        Assert.Throws<ArgumentException>(() => pix.ClipBoxToEdges(null, 5, 4, 1, 1, out _, out _));
        Assert.Throws<ArgumentException>(() => pix.ClipBoxToEdges(null, 1, 2, 0, 1, out _, out _));
    }

    /*---------------- Pixel averages, reversals, variance on lines -----------*/

    /* 8 bpp 5 x 3; row 1 = 10 20 30 40 50; column 2 = 1, 30, 5 */
    private static Pix Lines8()
    {
        Pix pix = Make(5, 3, 8,
                       0, 0, 1, 0, 0,
                       10, 20, 30, 40, 50,
                       0, 0, 5, 0, 0);
        return pix;
    }

    [Fact]
    public void ExtractOnLine_HorizontalAndVertical()
    {
        Pix pix = Lines8();
        Numa na = pix.ExtractOnLine(0, 1, 4, 1, 1);
        Assert.Equal(new[] { 10, 20, 30, 40, 50 }, Values(na));
        na.GetParameters(out float startx, out float delx);
        Assert.Equal((0f, 1f), (startx, delx));

        na = pix.ExtractOnLine(0, 1, 4, 1, 2);
        Assert.Equal(new[] { 10, 30, 50 }, Values(na));
        na.GetParameters(out startx, out delx);
        Assert.Equal((0f, 2f), (startx, delx));

        /* end points are clipped; the order of the end points does not matter */
        na = pix.ExtractOnLine(100, 1, -5, 1, 1);
        Assert.Equal(new[] { 10, 20, 30, 40, 50 }, Values(na));

        na = pix.ExtractOnLine(2, 2, 2, 0, 1);
        Assert.Equal(new[] { 1, 30, 5 }, Values(na));
        na.GetParameters(out startx, out delx);
        Assert.Equal((0f, 1f), (startx, delx));

        /* a single point */
        na = pix.ExtractOnLine(3, 1, 3, 1, 1);
        Assert.Equal(new[] { 40 }, Values(na));
        na = pix.ExtractOnLine(30, 30, 30, 30, 1);  /* clipped to (4, 2) */
        Assert.Equal(new[] { 0 }, Values(na));
    }

    [Fact]
    public void ExtractOnLine_OneBpp()
    {
        Pix pix = Make(4, 2, 1, 1, 0, 1, 1, 0, 0, 0, 1);
        Assert.Equal(new[] { 1, 0, 1, 1 }, Values(pix.ExtractOnLine(0, 0, 3, 0, 1)));
        Assert.Equal(new[] { 1, 0 }, Values(pix.ExtractOnLine(0, 0, 0, 1, 1)));
    }

    [Fact]
    public void ExtractOnLine_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Make(4, 4, 16).ExtractOnLine(0, 0, 3, 0, 1));
        Pix cm = Make(4, 4, 8);
        cm.SetColormap(PixColormap.Create(8));
        Assert.Throws<ArgumentException>(() => cm.ExtractOnLine(0, 0, 3, 0, 1));
    }

    [Fact]
    public void ExtractOnLine_Oblique()
    {
        Pix pix = Lines8();
        /* the slope is (2 - 0) / (4 - 0) = 0 in integer arithmetic: quasi-horizontal */
        Numa na = pix.ExtractOnLine(0, 0, 4, 2, 1);
        Assert.Equal(5, na.Count);
    }

    [Fact]
    public void AverageOnLine_HorizontalAndVertical()
    {
        Pix pix = Lines8();
        Assert.Equal(30f, pix.AverageOnLine(0, 1, 4, 1, 1));
        Assert.Equal(30f, pix.AverageOnLine(0, 1, 4, 1, 2));          /* 10, 30, 50 */
        Assert.Equal(25f, pix.AverageOnLine(0, 1, 3, 1, 1));          /* 10 .. 40 */
        Assert.Equal(30f, pix.AverageOnLine(0, 1, 100, 1, 1));        /* x2 clipped to 4 */
        Assert.Equal(36f / 3f, pix.AverageOnLine(2, 0, 2, 2, 1));     /* 1, 30, 5 */
        Assert.Equal(3f, pix.AverageOnLine(2, 0, 2, 2, 2));           /* 1, 5 */
        Assert.Equal(36f / 3f, pix.AverageOnLine(2, 0, 2, 100, 1));   /* y2 clipped to 2 */

        /* factor < 1 is set to 1 */
        Assert.Equal(30f, pix.AverageOnLine(0, 1, 4, 1, 0));

        Pix bin = Make(4, 1, 1, 1, 0, 1, 1);
        Assert.Equal(0.75f, bin.AverageOnLine(0, 0, 3, 0, 1));
    }

    [Fact]
    public void AverageOnLine_InvalidArguments()
    {
        Pix pix = Lines8();
        Assert.Throws<ArgumentException>(() => pix.AverageOnLine(3, 1, 1, 1, 1));
        Assert.Throws<ArgumentException>(() => pix.AverageOnLine(0, 2, 4, 1, 1));
        Assert.Throws<ArgumentException>(() => pix.AverageOnLine(0, 0, 4, 2, 1));
        Assert.Throws<ArgumentException>(() => Make(4, 4, 32).AverageOnLine(0, 0, 3, 0, 1));
    }

    private static Pix Profile8()
    {
        return Make(4, 3, 8,
                    10, 10, 10, 10,
                    20, 20, 20, 20,
                    0, 0, 100, 100);
    }

    [Fact]
    public void AverageIntensityProfile_Rows()
    {
        Pix pix = Profile8();
        Numa na = pix.AverageIntensityProfile(1.0f, L.L_HORIZONTAL_LINE, 0, 2, 1, 1);
        Assert.Equal(new[] { 10, 20, 50 }, Values(na));
        na.GetParameters(out float startx, out float delx);
        Assert.Equal((0f, 1f), (startx, delx));

        /* last is clipped to h - 1; first < 0 is set to 0 */
        na = pix.AverageIntensityProfile(1.0f, L.L_HORIZONTAL_LINE, -3, 50, 1, 1);
        Assert.Equal(3, na.Count);

        /* sampling the rows by 2 */
        na = pix.AverageIntensityProfile(1.0f, L.L_HORIZONTAL_LINE, 0, 2, 1, 2);
        Assert.Equal(new[] { 10, 50 }, Values(na));
        na.GetParameters(out startx, out delx);
        Assert.Equal((0f, 2f), (startx, delx));

        /* central half of the width: x = 1 .. 3 (end = w - start = 3) */
        na = pix.AverageIntensityProfile(0.5f, L.L_HORIZONTAL_LINE, 2, 2, 1, 1);
        Assert.Equal(200f / 3f, na.GetFValue(0));
    }

    [Fact]
    public void AverageIntensityProfile_OneBpp()
    {
        /* 1 bpp needs no conversion */
        Pix pix = Make(4, 3, 1,
                       1, 1, 1, 1,
                       1, 0, 0, 0,
                       0, 0, 1, 1);
        Numa na = pix.AverageIntensityProfile(1.0f, L.L_HORIZONTAL_LINE, 0, 2, 1, 1);
        Assert.Equal(3, na.Count);
        Assert.Equal(1f, na.GetFValue(0));
        Assert.Equal(0.25f, na.GetFValue(1));
        Assert.Equal(0.5f, na.GetFValue(2));

        /* columns 0..3 over the central part: fract 1 */
        na = pix.AverageIntensityProfile(1.0f, L.L_VERTICAL_LINE, 0, 3, 1, 1);
        Assert.Equal(new[] { 2f / 3f, 1f / 3f, 2f / 3f, 2f / 3f },
                     new[] { na.GetFValue(0), na.GetFValue(1), na.GetFValue(2), na.GetFValue(3) });
    }

    [Fact]
    public void AverageIntensityProfile_Columns()
    {
        Pix pix = Profile8();
        Numa na = pix.AverageIntensityProfile(1.0f, L.L_VERTICAL_LINE, 0, 3, 1, 1);
        Assert.Equal(4, na.Count);
        Assert.Equal(10f, na.GetFValue(0));
        Assert.Equal(10f, na.GetFValue(1));
        Assert.Equal(130f / 3f, na.GetFValue(2));
        Assert.Equal(130f / 3f, na.GetFValue(3));
    }

    [Fact]
    public void AverageIntensityProfile_InvalidArguments()
    {
        Pix pix = Profile8();
        Assert.Throws<ArgumentException>(
            () => pix.AverageIntensityProfile(1.5f, L.L_HORIZONTAL_LINE, 0, 2, 1, 1));
        Assert.Throws<ArgumentException>(() => pix.AverageIntensityProfile(1.0f, 3, 0, 2, 1, 1));
        Assert.Throws<ArgumentException>(
            () => pix.AverageIntensityProfile(1.0f, L.L_HORIZONTAL_LINE, 2, 1, 1, 1));
    }

    [Fact]
    public void ReversalProfile_BinaryRowsAndColumns()
    {
        /* row 0: 1 0 1 0 1 -> 4 flips; row 1: 1 1 1 0 0 -> 1 flip; row 2: all 0 -> 0 */
        Pix pix = Make(5, 3, 1,
                       1, 0, 1, 0, 1,
                       1, 1, 1, 0, 0,
                       0, 0, 0, 0, 0);
        Numa na = pix.ReversalProfile(1.0f, L.L_HORIZONTAL_LINE, 0, 2, 1, 1, 1);
        Assert.Equal(new[] { 4, 1, 0 }, Values(na));

        /* minreversal is forced to 1 for 1 bpp */
        na = pix.ReversalProfile(1.0f, L.L_HORIZONTAL_LINE, 0, 2, 50, 1, 1);
        Assert.Equal(new[] { 4, 1, 0 }, Values(na));

        /* columns (top to bottom): 1 1 0 / 0 1 0 / 1 1 0 / 0 0 0 / 1 0 0 */
        na = pix.ReversalProfile(1.0f, L.L_VERTICAL_LINE, 0, 4, 1, 1, 1);
        Assert.Equal(new[] { 1, 2, 1, 0, 1 }, Values(na));
    }

    [Fact]
    public void ReversalProfile_GrayRows()
    {
        /* row 0: 0 100 0 100 0 -> 3 interior extrema (100, 0, 100) at minreversal 50
         * (FindExtrema), row 1 is constant */
        Pix pix = Make(5, 2, 8,
                       0, 100, 0, 100, 0,
                       7, 7, 7, 7, 7);
        Numa na = pix.ReversalProfile(1.0f, L.L_HORIZONTAL_LINE, 0, 1, 50, 1, 1);
        Assert.Equal(2, na.Count);
        Assert.Equal(0, na.GetIValue(1));
    }

    [Fact]
    public void WindowedVarianceOnLine_AlternatingRow()
    {
        Pix pix = Make(6, 2, 8,
                       0, 10, 0, 10, 0, 10,
                       4, 4, 4, 4, 4, 4);
        Numa nad = pix.WindowedVarianceOnLine(L.L_HORIZONTAL_LINE, 0, 0, 5, 2);
        /* windows of two samples {0,10}, {10,0}: mean 5, rms deviation 5;
         * the loop runs for i < n - size, so 4 windows */
        Assert.Equal(4, nad.Count);
        for (int i = 0; i < 4; i++)
            Assert.Equal(5f, nad.GetFValue(i));
        nad.GetParameters(out float startx, out float delx);
        Assert.Equal((1f, 1f), (startx, delx));

        /* constant row */
        nad = pix.WindowedVarianceOnLine(L.L_HORIZONTAL_LINE, 1, 0, 5, 3);
        Assert.Equal(3, nad.Count);
        for (int i = 0; i < 3; i++)
            Assert.Equal(0f, nad.GetFValue(i));
        nad.GetParameters(out startx, out _);
        Assert.Equal(1f, startx);  /* cmin + size / 2 */
    }

    [Fact]
    public void WindowedVarianceOnLine_VerticalAndClipping()
    {
        Pix pix = Make(2, 6, 8,
                       0, 9,
                       10, 9,
                       0, 9,
                       10, 9,
                       0, 9,
                       10, 9);
        /* end points beyond the image are clipped; c1 > c2 is allowed */
        Numa nad = pix.WindowedVarianceOnLine(L.L_VERTICAL_LINE, 0, 100, -3, 2);
        Assert.Equal(4, nad.Count);
        Assert.Equal(5f, nad.GetFValue(0));
        nad = pix.WindowedVarianceOnLine(L.L_VERTICAL_LINE, 1, 0, 5, 2);
        Assert.Equal(0f, nad.GetFValue(2));
    }

    [Fact]
    public void WindowedVarianceOnLine_InvalidArguments()
    {
        Pix pix = Make(6, 2, 8);
        Assert.Throws<ArgumentException>(() => pix.WindowedVarianceOnLine(L.L_HORIZONTAL_LINE, 0, 0, 5, 1));
        Assert.Throws<ArgumentException>(() => pix.WindowedVarianceOnLine(L.L_HORIZONTAL_LINE, 2, 0, 5, 2));
        Assert.Throws<ArgumentException>(() => pix.WindowedVarianceOnLine(L.L_VERTICAL_LINE, 6, 0, 1, 2));
        Assert.Throws<ArgumentException>(() => pix.WindowedVarianceOnLine(3, 0, 0, 5, 2));
        Assert.Throws<ArgumentException>(() => Make(6, 2, 1).WindowedVarianceOnLine(L.L_HORIZONTAL_LINE, 0, 0, 5, 2));
    }

    [Fact]
    public void MinMaxNearLine_HorizontalLine()
    {
        Pix pix = Make(5, 3, 8,
                       9, 9, 9, 9, 9,
                       5, 6, 7, 8, 9,
                       1, 1, 1, 1, 1);
        pix.MinMaxNearLine(0, 1, 4, 1, 1, L.L_SCAN_BOTH, out Numa? namin, out Numa? namax,
                           out float minave, out float maxave);
        Assert.Equal(new[] { 1, 1, 1, 1, 1 }, Values(namin!));
        Assert.Equal(new[] { 9, 9, 9, 9, 9 }, Values(namax!));
        Assert.Equal(1f, minave);
        Assert.Equal(9f, maxave);
    }

    /*------------------------- Rank row and column transforms ----------------*/

    [Fact]
    public void RankRowTransform_SortsEachRow()
    {
        Pix pix = Make(5, 2, 8,
                       30, 10, 20, 10, 255,
                       5, 4, 3, 2, 1);
        Pix pixd = pix.RankRowTransform();
        int[] expected = { 10, 10, 20, 30, 255, 1, 2, 3, 4, 5 };
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 5; x++)
                Assert.Equal((uint)expected[y * 5 + x], Get(pixd, x, y));
        /* the source is not modified */
        Assert.Equal(30u, Get(pix, 0, 0));
        pixd.GetDimensions(out int w, out int h, out int d);
        Assert.Equal((5, 2, 8), (w, h, d));
    }

    [Fact]
    public void RankColumnTransform_SortsEachColumn()
    {
        Pix pix = Make(3, 4, 8,
                       9, 1, 0,
                       3, 8, 0,
                       7, 2, 6,
                       1, 8, 5);
        Pix pixd = pix.RankColumnTransform();
        int[] expected = {
            1, 1, 0,
            3, 2, 0,
            7, 8, 5,
            9, 8, 6 };
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 3; x++)
                Assert.Equal((uint)expected[y * 3 + x], Get(pixd, x, y));
    }

    [Fact]
    public void RankTransforms_InvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => Make(4, 4, 1).RankRowTransform());
        Assert.Throws<ArgumentException>(() => Make(4, 4, 32).RankColumnTransform());
        Pix cm = Make(4, 4, 8);
        cm.SetColormap(PixColormap.Create(8));
        Assert.Throws<ArgumentException>(() => cm.RankRowTransform());
        Assert.Throws<ArgumentException>(() => cm.RankColumnTransform());
    }
}
