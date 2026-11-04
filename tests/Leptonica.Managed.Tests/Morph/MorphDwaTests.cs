using System;
using Leptonica;
using Leptonica.Managed.Tests.Reg;

namespace Leptonica.Managed.Tests.Morph;

/// <summary>
/// Compares the dwa brick operations of morphdwa.c with the rasterop versions of morph.c
/// (as in binmorph1_reg.c and dwamorph1_reg.c): new pixd, existing pixd, in place, for both
/// boundary conditions.  With symmetric b.c. a border of 32 pixels is added first, as in the
/// C test.  The composite operations may use a size that differs slightly from the request,
/// so they are compared with the rasterop operation of the size that is actually applied.
/// The boundary condition is a process-wide static: the default is restored in finally blocks.
/// </summary>
[Collection("MorphBoundaryCondition")]
public class MorphDwaTests
{
    private enum Op { Dilate, Erode, Open, Close }

    public static readonly TheoryData<int, int> Sizes = new()
    {
        { 1, 1 }, { 2, 1 }, { 1, 7 }, { 5, 3 }, { 21, 15 }, { 31, 1 }, { 1, 63 }, { 64, 3 }, { 100, 1 },
    };

    private static Pix RandomImage(int seed, int w, int h)
    {
        var rng = new Random(seed);
        Pix pix = Pix.Create(w, h, 1);
        for (int i = 0; i < 70; i++) {  // blobs
            int bw = 1 + rng.Next(40), bh = 1 + rng.Next(30);
            int x0 = rng.Next(w), y0 = rng.Next(h);
            for (int y = y0; y < Math.Min(h, y0 + bh); y++)
                for (int x = x0; x < Math.Min(w, x0 + bw); x++)
                    pix.SetPixel(x, y, 1);
        }
        for (int i = 0; i < w * h / 12; i++)  // noise
            pix.SetPixel(rng.Next(w), rng.Next(h), (uint)rng.Next(2));
        return pix;
    }

    private static int EffComp(int size)
    {
        if (size == 1) return 1;
        if (size <= 63) {
            Sel.GetCompositeParameters(size, out int s1, out int s2, out _, out _, out _, out _);
            return s1 * s2;
        }
        if (size == 64) return 63;  // approximate
        Pix.GetExtendedCompositeParameters(size, out _, out _, out int actual);
        return actual;
    }

    private static bool HasBasic(int size)
    {
        if (size == 1) return true;
        try {
            return Sela.AddBasic(null).GetBrickName(size, 1) != null;
        } catch (ArgumentException) {
            return false;
        }
    }

    // Keep an explicit rasterop reference here: DilateBrick/ErodeBrick/OpenBrick
    // may dispatch to DWA for large basic sels, and must not make these comparisons
    // self-referential.
    private static Pix Raster(Op op, Pix? pixd, Pix pixs, int h, int v)
    {
        if (op == Op.Close)
            return Pix.CloseSafeBrick(pixd, pixs, h, v);

        if (h == 1 || v == 1) {
            Sel sel = Sel.CreateBrick(v, h, v / 2, h / 2, L.SEL_HIT);
            return op switch
            {
                Op.Dilate => Pix.Dilate(pixd, pixs, sel),
                Op.Erode => Pix.Erode(pixd, pixs, sel),
                _ => Pix.Open(pixd, pixs, sel),
            };
        }

        Sel selh = Sel.CreateBrick(1, h, 0, h / 2, L.SEL_HIT);
        Sel selv = Sel.CreateBrick(v, 1, v / 2, 0, L.SEL_HIT);
        if (op == Op.Dilate) {
            Pix pixt = Pix.Dilate(null, pixs, selh);
            return Pix.Dilate(pixd, pixt, selv);
        }
        if (op == Op.Erode) {
            Pix pixt = Pix.Erode(null, pixs, selh);
            return Pix.Erode(pixd, pixt, selv);
        }

        Pix temp = Pix.Erode(null, pixs, selh);
        pixd = Pix.Erode(pixd, temp, selv);
        Pix.Dilate(temp, pixd, selh);
        return Pix.Dilate(pixd, temp, selv);
    }

    private static Pix PublicBrick(Op op, Pix? pixd, Pix pixs, int h, int v) => op switch
    {
        Op.Dilate => Pix.DilateBrick(pixd, pixs, h, v),
        Op.Erode => Pix.ErodeBrick(pixd, pixs, h, v),
        Op.Open => Pix.OpenBrick(pixd, pixs, h, v),
        _ => Pix.CloseBrick(pixd, pixs, h, v),
    };

    private static Pix Dwa(Op op, bool comp, Pix? pixd, Pix pixs, int h, int v) => (op, comp) switch
    {
        (Op.Dilate, false) => Pix.DilateBrickDwa(pixd, pixs, h, v),
        (Op.Erode, false) => Pix.ErodeBrickDwa(pixd, pixs, h, v),
        (Op.Open, false) => Pix.OpenBrickDwa(pixd, pixs, h, v),
        (Op.Close, false) => Pix.CloseBrickDwa(pixd, pixs, h, v),
        (Op.Dilate, true) => Pix.DilateCompBrickDwa(pixd, pixs, h, v),
        (Op.Erode, true) => Pix.ErodeCompBrickDwa(pixd, pixs, h, v),
        (Op.Open, true) => Pix.OpenCompBrickDwa(pixd, pixs, h, v),
        _ => Pix.CloseCompBrickDwa(pixd, pixs, h, v),
    };

    /// <summary>Runs all ops for one size on one image; pixs is not changed.</summary>
    private static void CheckAll(Pix src, int bc, int h, int v, bool comp)
    {
        try {
            Pix.ResetMorphBoundaryCondition(bc);
            // size that is really applied
            int eh = h, ev = v;
            bool useComp = comp || !(HasBasic(h) && HasBasic(v));
            if (useComp) {
                eh = EffComp(h);
                ev = EffComp(v);
            }

            Pix pixs = Pix.Copy(null, src);
            if (bc == L.SYMMETRIC_MORPH_BC) {
                // As in binmorph1_reg.c: the symmetric b.c. needs an added border, which must
                // be at least half the Sel size, otherwise the rasterop closing loses pixels
                // spread beyond the frame (the dwa closing adds its own larger border).
                Pix? pixb = pixs.AddBorder(Math.Max(eh, ev) / 2 > 32 ? 96 : 32, 0);
                pixs.TransferAllData(ref pixb, false, false);
            }
            Pix orig = Pix.Copy(null, pixs);

            foreach (Op op in Enum.GetValues<Op>()) {
                string what = $"{(comp ? "Comp" : "")}{op} {h}x{v} bc={bc}";
                Pix expected = Raster(op, null, pixs, eh, ev);

                if (op != Op.Close && !comp && HasBasic(h) && HasBasic(v) &&
                    (h >= 5 || v >= 5 || (h >= 3 && v >= 3))) {
                    Pix dispatched = PublicBrick(op, null, pixs, h, v);
                    Assert.True(Pix.Equal(expected, dispatched), what + " (public DWA dispatch)");
                    Pix dispatchedInPlace = Pix.Copy(null, pixs);
                    PublicBrick(op, dispatchedInPlace, dispatchedInPlace, h, v);
                    Assert.True(Pix.Equal(expected, dispatchedInPlace), what + " (public DWA dispatch, in place)");
                }

                // new pixd
                Pix p1 = Dwa(op, comp, null, pixs, h, v);
                Assert.True(Pix.Equal(expected, p1), what + $" (new) eh={eh} ev={ev} diff={Pix.Xor(null, expected, p1).CountPixels(null)}");
                Assert.True(Pix.Equal(orig, pixs), what + " modified pixs");

                // existing pixd, same size
                Pix p2 = Pix.CreateTemplate(pixs);
                Pix r2 = Dwa(op, comp, p2, pixs, h, v);
                Assert.Same(p2, r2);
                Assert.True(Pix.Equal(expected, p2), what + " (existing)");

                // existing pixd of another size
                Pix p3 = Pix.Create(7, 9, 1);
                Pix r3 = Dwa(op, comp, p3, pixs, h, v);
                Assert.Same(p3, r3);
                Assert.True(Pix.Equal(expected, p3), what + " (existing, other size)");

                // in place
                Pix p4 = Pix.Copy(null, pixs);
                Pix r4 = Dwa(op, comp, p4, p4, h, v);
                Assert.Same(p4, r4);
                Assert.True(Pix.Equal(expected, p4), what + " (in place)");
            }
        } finally {
            Pix.ResetMorphBoundaryCondition(L.ASYMMETRIC_MORPH_BC);
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void BrickDwaMatchesRasterop(int h, int v)
    {
        Pix pix = RandomImage(1234, 230, 170);
        CheckAll(pix, L.ASYMMETRIC_MORPH_BC, h, v, false);
        CheckAll(pix, L.SYMMETRIC_MORPH_BC, h, v, false);
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void CompBrickDwaMatchesRasterop(int h, int v)
    {
        Pix pix = RandomImage(98765, 230, 170);
        CheckAll(pix, L.ASYMMETRIC_MORPH_BC, h, v, true);
        CheckAll(pix, L.SYMMETRIC_MORPH_BC, h, v, true);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(37, 1)]
    [InlineData(64, 64)]
    [InlineData(130, 5)]
    [InlineData(1, 200)]
    public void ExtendedCompositeRuns(int h, int v)
    {
        // sizes beyond 63 go through the extended functions; check the dimensions and
        // that dilation/erosion are monotone w.r.t. the source
        Pix pixs = RandomImage(555, 200, 150);
        Pix d = Pix.DilateCompBrickExtendDwa(null, pixs, h, v);
        Pix e = Pix.ErodeCompBrickExtendDwa(null, pixs, h, v);
        Assert.Equal((pixs.Width, pixs.Height), (d.Width, d.Height));
        Assert.Equal((pixs.Width, pixs.Height), (e.Width, e.Height));
        Assert.True(Pix.Equal(d, Pix.Or(null, d, pixs)));         // pixs subset of dilation
        Assert.True(Pix.Equal(e, Pix.And(null, e, pixs)));        // erosion subset of pixs
    }

    [Fact]
    public void ExtendedCompositeParameters()
    {
        Pix.GetExtendedCompositeParameters(1, out int n, out int extra);
        Assert.Equal((0, 1), (n, extra));
        Pix.GetExtendedCompositeParameters(63, out n, out extra);
        Assert.Equal((0, 1), (n, extra));
        Pix.GetExtendedCompositeParameters(64, out n, out extra, out int actual);
        Assert.Equal((1, 2, 64), (n, extra, actual));
        Pix.GetExtendedCompositeParameters(125, out n, out extra, out actual);
        Assert.Equal((2, 1 + 125 - 63 - 62), (n, extra));
    }

    [RegFact]
    public void FeynFractMatchesRasterop()
    {
        Pix pix0 = TestPaths.ReadInput("feyn-fract.tif");
        Box box = Box.Create(293, 37, pix0.Width - 691, pix0.Height - 145);
        Pix pixs = pix0.ClipRectangle(box, out _)!;
        foreach (var (h, v) in new[] { (1, 7), (5, 3), (21, 15), (31, 1), (64, 3) }) {
            CheckAll(pixs, L.ASYMMETRIC_MORPH_BC, h, v, false);
            CheckAll(pixs, L.SYMMETRIC_MORPH_BC, h, v, false);
            CheckAll(pixs, L.ASYMMETRIC_MORPH_BC, h, v, true);
            CheckAll(pixs, L.SYMMETRIC_MORPH_BC, h, v, true);
        }
    }
}
