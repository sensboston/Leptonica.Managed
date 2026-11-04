using System;
using System.Collections.Generic;
using Leptonica;

namespace Leptonica.Managed.Tests.Morph;

/// <summary>
/// The morphological boundary condition is a process-wide static (as in C), so the tests that
/// change it must not run in parallel with any other test.
/// </summary>
[CollectionDefinition("MorphBoundaryCondition", DisableParallelization = true)]
public class MorphBoundaryConditionCollection
{
}

/// <summary>
/// Compares the binary morphology functions of morph.c against the naive pixel reference
/// (NaiveMorph) on random images (fixed seeds), for both boundary conditions and for the
/// three destination cases (new pix, in place, existing pix of another size).
/// </summary>
[Collection("MorphBoundaryCondition")]
public class MorphTests
{
    private const int Iterations = 70;

    private static void WithBc(bool symmetric, Action action)
    {
        int saved = Pix.MORPH_BC;
        Pix.ResetMorphBoundaryCondition(symmetric ? L.SYMMETRIC_MORPH_BC : L.ASYMMETRIC_MORPH_BC);
        try {
            action();
        } finally {
            Pix.MORPH_BC = saved;
        }
    }

    /// <summary>
    /// Runs op(pixd, pixs) for the destination case given by mode (0 = new, 1 = in place,
    /// 2 = existing pix of another size) and checks the pixd / pixs contract.
    /// </summary>
    private static Pix Run(Func<Pix?, Pix, Pix> op, Pix src, int mode, Random rng, string ctx)
    {
        Pix orig = Pix.Copy(null, src);
        Pix result;
        switch (mode) {
        case 0:
            result = op(null, src);
            NaiveMorph.AssertSame(orig, src, ctx + " (pixs changed)");
            break;
        case 1:
            Pix work = Pix.Copy(null, src);
            result = op(work, work);
            Assert.Same(work, result);
            break;
        default:
            Pix dest = NaiveMorph.RandomPix(rng, rng.Next(1, 71), rng.Next(1, 41), 50);
            result = op(dest, src);
            Assert.Same(dest, result);
            NaiveMorph.AssertSame(orig, src, ctx + " (pixs changed)");
            break;
        }
        return result;
    }

    private delegate Pix SelOp(Pix? pixd, Pix pixs, Sel sel);
    private delegate Pix BrickOp(Pix? pixd, Pix pixs, int hsize, int vsize);

    // Rows: (pix op, naive op, hits-and-misses needed, name)
    private static void CheckSelOp(string name, SelOp op, Func<Pix, Sel, bool, Pix> naive,
                                   bool symmetric, int seed, bool needHitOrMiss,
                                   int percentHit, int percentMiss)
    {
        WithBc(symmetric, () => {
            var rng = new Random(seed);
            for (int iter = 0; iter < Iterations; iter++) {
                int w = 1 + (iter % 70);
                int h = rng.Next(1, 41);
                Pix src = NaiveMorph.RandomPix(rng, w, h, rng.Next(5, 95));
                Sel sel = NaiveMorph.RandomSel(rng, 7, percentHit, percentMiss);
                if (needHitOrMiss && !NaiveMorph.HasHitOrMiss(sel))
                    sel.SetElement(0, 0, L.SEL_HIT);
                string ctx = $"{name} sym={symmetric} seed={seed} iter={iter} {w}x{h}";
                Pix expected = naive(src, sel, symmetric);
                for (int mode = 0; mode < 3; mode++) {
                    Pix actual = Run((pd, ps) => op(pd, ps, sel), src, mode, rng, ctx + $" mode={mode}");
                    NaiveMorph.AssertSame(expected, actual, ctx + $" mode={mode}");
                }
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DilateMatchesNaive(bool symmetric) =>
        CheckSelOp("Dilate", Pix.Dilate, (s, sel, _) => NaiveMorph.Dilate(s, sel),
                   symmetric, 100, false, 40, 20);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ErodeMatchesNaive(bool symmetric) =>
        CheckSelOp("Erode", Pix.Erode, NaiveMorph.Erode, symmetric, 200, false, 40, 20);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ErodeWithDenseSelMatchesNaive(bool symmetric) =>
        CheckSelOp("Erode", Pix.Erode, NaiveMorph.Erode, symmetric, 201, false, 90, 0);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HmtMatchesNaive(bool symmetric) =>
        CheckSelOp("HMT", Pix.HMT, (s, sel, _) => NaiveMorph.Hmt(s, sel),
                   symmetric, 300, true, 30, 30);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HmtMissesOnlyMatchesNaive(bool symmetric) =>
        CheckSelOp("HMT", Pix.HMT, (s, sel, _) => NaiveMorph.Hmt(s, sel),
                   symmetric, 301, true, 0, 50);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenMatchesNaive(bool symmetric) =>
        CheckSelOp("Open", Pix.Open, NaiveMorph.Open, symmetric, 400, false, 50, 10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CloseMatchesNaive(bool symmetric) =>
        CheckSelOp("Close", Pix.Close, NaiveMorph.Close, symmetric, 500, false, 50, 10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenGeneralizedMatchesNaive(bool symmetric) =>
        CheckSelOp("OpenGeneralized", Pix.OpenGeneralized,
                   (s, sel, _) => NaiveMorph.OpenGeneralized(s, sel),
                   symmetric, 600, true, 30, 30);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CloseGeneralizedMatchesNaive(bool symmetric) =>
        CheckSelOp("CloseGeneralized", Pix.CloseGeneralized,
                   (s, sel, _) => NaiveMorph.CloseGeneralized(s, sel),
                   symmetric, 700, true, 30, 30);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CloseSafeMatchesNaive(bool symmetric)
    {
        WithBc(symmetric, () => {
            var rng = new Random(800);
            for (int iter = 0; iter < Iterations; iter++) {
                int w = 1 + (iter % 70);
                int h = rng.Next(1, 41);
                Pix src = NaiveMorph.RandomPix(rng, w, h, rng.Next(5, 95));
                Sel sel = NaiveMorph.RandomSel(rng, 7, 50, 10);
                string ctx = $"CloseSafe sym={symmetric} iter={iter} {w}x{h}";
                // Symmetric: the plain closing.  Asymmetric: the closing of the image
                // embedded in an infinite OFF background, cropped back.
                Pix expected = symmetric
                    ? NaiveMorph.Close(src, sel, true)
                    : NaiveMorph.Crop(NaiveMorph.Close(NaiveMorph.Pad(src, 40), sel, false), 40);
                for (int mode = 0; mode < 3; mode++) {
                    Pix actual = Run((pd, ps) => Pix.CloseSafe(pd, ps, sel), src, mode, rng,
                                     ctx + $" mode={mode}");
                    NaiveMorph.AssertSame(expected, actual, ctx + $" mode={mode}");
                    if (!symmetric)
                        Assert.True(NaiveMorph.IsSubset(src, actual), ctx + ": not extensive");
                }
            }
        });
    }

    // ---- brick operations --------------------------------------------------------------

    private static readonly int[] BrickSizes = { 1, 2, 3, 4, 5, 8, 11 };

    private static void CheckBrickOp(string name, BrickOp op, Func<Pix, Sel, Pix> naive, int seed,
                                     bool symmetric)
    {
        WithBc(symmetric, () => {
            var rng = new Random(seed);
            int iter = 0;
            foreach (int hsize in BrickSizes) {
                foreach (int vsize in BrickSizes) {
                    int w = 1 + ((iter * 7) % 70);
                    int h = rng.Next(1, 41);
                    iter++;
                    Pix src = NaiveMorph.RandomPix(rng, w, h, rng.Next(10, 90));
                    string ctx = $"{name} sym={symmetric} {hsize}x{vsize} on {w}x{h}";
                    Pix expected = naive(src, NaiveMorph.Box(hsize, vsize));
                    for (int mode = 0; mode < 3; mode++) {
                        Pix actual = Run((pd, ps) => op(pd, ps, hsize, vsize), src, mode, rng,
                                         ctx + $" mode={mode}");
                        NaiveMorph.AssertSame(expected, actual, ctx + $" mode={mode}");
                    }
                }
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DilateBrickMatchesNaive(bool symmetric) =>
        CheckBrickOp("DilateBrick", Pix.DilateBrick, NaiveMorph.Dilate, 1000, symmetric);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ErodeBrickMatchesNaive(bool symmetric) =>
        CheckBrickOp("ErodeBrick", Pix.ErodeBrick, (s, sel) => NaiveMorph.Erode(s, sel, symmetric),
                     1100, symmetric);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenBrickMatchesNaive(bool symmetric) =>
        CheckBrickOp("OpenBrick", Pix.OpenBrick, (s, sel) => NaiveMorph.Open(s, sel, symmetric),
                     1200, symmetric);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CloseBrickMatchesNaive(bool symmetric) =>
        CheckBrickOp("CloseBrick", Pix.CloseBrick, (s, sel) => NaiveMorph.Close(s, sel, symmetric),
                     1300, symmetric);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CloseSafeBrickMatchesNaive(bool symmetric) =>
        CheckBrickOp("CloseSafeBrick", Pix.CloseSafeBrick,
                     (s, sel) => symmetric
                         ? NaiveMorph.Close(s, sel, true)
                         : NaiveMorph.Crop(NaiveMorph.Close(NaiveMorph.Pad(s, 40), sel, false), 40),
                     1400, symmetric);

    // ---- composite brick operations ----------------------------------------------------

    private static readonly int[] CompSizes = { 1, 2, 3, 5, 7, 10, 13, 17 };

    private static List<NaiveMorph.Step> Steps(char kind, int hsize, int vsize)
    {
        var steps = new List<NaiveMorph.Step>();
        if (hsize > 1) {
            Pix.SelectComposableSels(hsize, Sel.L_HORIZ, out Sel h1, out Sel h2);
            steps.Add(new NaiveMorph.Step(kind, h1));
            steps.Add(new NaiveMorph.Step(kind, h2));
        }
        if (vsize > 1) {
            Pix.SelectComposableSels(vsize, Sel.L_VERT, out Sel v1, out Sel v2);
            steps.Add(new NaiveMorph.Step(kind, v1));
            steps.Add(new NaiveMorph.Step(kind, v2));
        }
        return steps;
    }

    private static void CheckCompBrickOp(string name, BrickOp op, string kinds, int pad,
                                         bool safeOp, int seed, bool symmetric)
    {
        WithBc(symmetric, () => {
            var rng = new Random(seed);
            int iter = 0;
            foreach (int hsize in CompSizes) {
                foreach (int vsize in CompSizes) {
                    int w = 1 + ((iter * 11) % 70);
                    int h = rng.Next(1, 41);
                    iter++;
                    Pix src = NaiveMorph.RandomPix(rng, w, h, rng.Next(10, 90));
                    var steps = new List<NaiveMorph.Step>();
                    foreach (char k in kinds)
                        steps.AddRange(Steps(k, hsize, vsize));
                    int p = pad;
                    if (p < 0) {   // safe closing: border of whole 32-bit words
                        int maxtrans = Math.Max(hsize / 2, vsize / 2);
                        p = 32 * ((maxtrans + 31) / 32);
                    }
                    // Symmetric safe closing is the plain closing, without a border.
                    if (safeOp && symmetric)
                        p = 0;
                    string ctx = $"{name} sym={symmetric} {hsize}x{vsize} on {w}x{h}";
                    Pix expected = (hsize == 1 && vsize == 1)
                        ? src
                        : NaiveMorph.Sequence(src, steps, symmetric, p);
                    for (int mode = 0; mode < 3; mode++) {
                        Pix actual = Run((pd, ps) => op(pd, ps, hsize, vsize), src, mode, rng,
                                         ctx + $" mode={mode}");
                        NaiveMorph.AssertSame(expected, actual, ctx + $" mode={mode}");
                    }
                }
            }
        });
    }

    [Fact]
    public void DilateCompBrickTwentyOneMatchesScalarAtWordBoundaries()
    {
        var rng = new Random(826017);
        foreach (int width in new[] { 1, 31, 32, 33, 63, 64, 65, 127, 257 }) {
            foreach (var size in new[] { (21, 1), (1, 21), (21, 21) }) {
                Pix src = NaiveMorph.RandomPix(rng, width, 9, 35);
                Pix expected = NaiveMorph.Sequence(src, Steps('D', size.Item1, size.Item2), false, 32);
                for (int mode = 0; mode < 3; mode++) {
                    Pix actual = Run((pd, ps) => Pix.DilateCompBrick(pd, ps, size.Item1, size.Item2),
                                     src, mode, rng, $"composite dilation width={width} mode={mode}");
                    Assert.Equal(expected.Data, actual.Data);
                }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DilateCompBrickMatchesNaive(bool symmetric) =>
        CheckCompBrickOp("DilateCompBrick", Pix.DilateCompBrick, "D", 32, false, 2000, symmetric);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ErodeCompBrickMatchesNaive(bool symmetric) =>
        CheckCompBrickOp("ErodeCompBrick", Pix.ErodeCompBrick, "E", 0, false, 2100, symmetric);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenCompBrickMatchesNaive(bool symmetric) =>
        CheckCompBrickOp("OpenCompBrick", Pix.OpenCompBrick, "ED", 0, false, 2200, symmetric);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CloseCompBrickMatchesNaive(bool symmetric) =>
        CheckCompBrickOp("CloseCompBrick", Pix.CloseCompBrick, "DE", 0, false, 2300, symmetric);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CloseSafeCompBrickMatchesNaive(bool symmetric) =>
        CheckCompBrickOp("CloseSafeCompBrick", Pix.CloseSafeCompBrick, "DE", -1, true, 2400,
                         symmetric);

    // ---- composable sizes --------------------------------------------------------------

    [Fact]
    public void SelectComposableSizesKnownValues()
    {
        Pix.SelectComposableSizes(1, out int f1, out int f2);
        Assert.Equal((1, 1), (f1, f2));
        Pix.SelectComposableSizes(36, out f1, out f2);
        Assert.Equal((6, 6), (f1, f2));
        Pix.SelectComposableSizes(37, out f1, out f2);   // documented: 37 -> 6 x 6
        Assert.Equal((6, 6), (f1, f2));
        Pix.SelectComposableSizes(10000, out f1, out f2);
        Assert.Equal((100, 100), (f1, f2));
    }

    [Fact]
    public void SelectComposableSizesAreCloseToRequestedSize()
    {
        for (int size = 1; size <= 300; size++) {
            Pix.SelectComposableSizes(size, out int f1, out int f2);
            Assert.True(f1 >= f2 && f2 >= 1, $"size {size}: factors {f1}, {f2}");
            if (size > 1)
                Assert.True(f1 > 1, $"size {size}: factor1 = 1");
            // The C notes say the maximum difference is 2 up to size 300; the cost function
            // gives 3 for a few sizes (e.g. 213 -> 15 * 14).
            Assert.True(Math.Abs(f1 * f2 - size) <= 3, $"size {size}: {f1} * {f2}");
        }
        for (int size = 301; size <= 10000; size += 37) {
            Pix.SelectComposableSizes(size, out int f1, out int f2);
            Assert.True(f1 >= f2 && f2 >= 1, $"size {size}: factors {f1}, {f2}");
        }
    }

    [Fact]
    public void SelectComposableSizesRejectsBadSize()
    {
        Assert.Throws<ArgumentException>(() => Pix.SelectComposableSizes(0, out _, out _));
        Assert.Throws<ArgumentException>(() => Pix.SelectComposableSizes(10001, out _, out _));
    }

    [Fact]
    public void SelectComposableSelsShapes()
    {
        Pix.SelectComposableSizes(10, out int f1, out int f2);

        Pix.SelectComposableSels(10, Sel.L_HORIZ, out Sel sel1, out Sel sel2);
        sel1.GetParameters(out int sy, out int sx, out int cy, out int cx);
        Assert.Equal((1, f1, 0, f1 / 2), (sy, sx, cy, cx));
        foreach (int v in sel1.Data[0])
            Assert.Equal(L.SEL_HIT, v);
        Assert.NotNull(sel2);

        Pix.SelectComposableSels(10, Sel.L_VERT, out sel1, out sel2);
        sel1.GetParameters(out sy, out sx, out cy, out cx);
        Assert.Equal((f1, 1, f1 / 2, 0), (sy, sx, cy, cx));

        Assert.Throws<ArgumentException>(() => Pix.SelectComposableSels(10, 0, out _, out _));
        Assert.Throws<ArgumentException>(() => Pix.SelectComposableSels(0, Sel.L_HORIZ, out _, out _));
        Assert.Throws<ArgumentException>(() => Pix.SelectComposableSels(10001, Sel.L_VERT, out _, out _));
    }

    // ---- identities --------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenCloseAreComposedOfErodeAndDilate(bool symmetric)
    {
        WithBc(symmetric, () => {
            var rng = new Random(3000);
            for (int iter = 0; iter < Iterations; iter++) {
                Pix src = NaiveMorph.RandomPix(rng, 1 + (iter % 70), rng.Next(1, 41), rng.Next(5, 95));
                Sel sel = NaiveMorph.RandomSel(rng, 6, 50, 10);
                string ctx = $"sym={symmetric} iter={iter}";
                NaiveMorph.AssertSame(Pix.Dilate(null, Pix.Erode(null, src, sel), sel),
                                      Pix.Open(null, src, sel), ctx + " open");
                NaiveMorph.AssertSame(Pix.Erode(null, Pix.Dilate(null, src, sel), sel),
                                      Pix.Close(null, src, sel), ctx + " close");
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpeningIsAntiExtensiveClosingIsExtensive(bool symmetric)
    {
        WithBc(symmetric, () => {
            var rng = new Random(3100);
            for (int iter = 0; iter < Iterations; iter++) {
                Pix src = NaiveMorph.RandomPix(rng, 1 + (iter % 70), rng.Next(1, 41), rng.Next(5, 95));
                Sel sel = NaiveMorph.RandomSel(rng, 6, 60, 0);
                string ctx = $"sym={symmetric} iter={iter}";
                Assert.True(NaiveMorph.IsSubset(Pix.Open(null, src, sel), src), ctx + " open");
                Assert.True(NaiveMorph.IsSubset(Pix.OpenBrick(null, src, 1 + iter % 5, 1 + iter % 4), src),
                            ctx + " open brick");
                if (symmetric)
                    Assert.True(NaiveMorph.IsSubset(src, Pix.Close(null, src, sel)), ctx + " close");
                // The safe closings are extensive for the asymmetric convention too.
                Assert.True(NaiveMorph.IsSubset(src, Pix.CloseSafe(null, src, sel)), ctx + " close safe");
                Assert.True(NaiveMorph.IsSubset(src, Pix.CloseSafeBrick(null, src, 1 + iter % 6, 1 + iter % 3)),
                            ctx + " close safe brick");
                // The composite closing has no border in the symmetric case and crops the
                // intermediate dilations, so it is only extensive with the added border.
                if (!symmetric)
                    Assert.True(NaiveMorph.IsSubset(src, Pix.CloseSafeCompBrick(null, src, 1 + iter % 9, 1 + iter % 7)),
                                ctx + " close safe comp brick");
            }
        });
    }

    [Fact]
    public void OpeningIsIdempotentWithAsymmetricBoundary()
    {
        WithBc(false, () => {
            var rng = new Random(3200);
            for (int iter = 0; iter < Iterations; iter++) {
                Pix src = NaiveMorph.RandomPix(rng, 1 + (iter % 70), rng.Next(1, 41), rng.Next(5, 95));
                Sel sel = NaiveMorph.RandomSel(rng, 6, 60, 0);
                Pix once = Pix.Open(null, src, sel);
                NaiveMorph.AssertSame(once, Pix.Open(null, once, sel), $"iter={iter}");
                Pix onceBrick = Pix.OpenBrick(null, src, 1 + iter % 5, 1 + iter % 4);
                NaiveMorph.AssertSame(onceBrick,
                    Pix.OpenBrick(null, onceBrick, 1 + iter % 5, 1 + iter % 4), $"brick iter={iter}");
            }
        });
    }

    [Fact]
    public void HmtWithHitsOnlyIsAsymmetricErosion()
    {
        WithBc(false, () => {
            var rng = new Random(3300);
            for (int iter = 0; iter < Iterations; iter++) {
                Pix src = NaiveMorph.RandomPix(rng, 1 + (iter % 70), rng.Next(1, 41), rng.Next(5, 95));
                Sel sel = NaiveMorph.RandomSel(rng, 6, 50, 0);
                sel.SetElement(0, 0, L.SEL_HIT);
                NaiveMorph.AssertSame(Pix.Erode(null, src, sel), Pix.HMT(null, src, sel),
                                      $"iter={iter}");
            }
        });
    }

    [Fact]
    public void SingleHitAtOriginIsIdentity()
    {
        WithBc(false, () => {
            var rng = new Random(3400);
            Pix src = NaiveMorph.RandomPix(rng, 45, 17, 50);
            Sel sel = Sel.Create(3, 3);
            sel.SetElement(1, 1, L.SEL_HIT);
            sel.SetOrigin(1, 1);
            NaiveMorph.AssertSame(src, Pix.Dilate(null, src, sel), "dilate");
            NaiveMorph.AssertSame(src, Pix.Erode(null, src, sel), "erode");
            NaiveMorph.AssertSame(src, Pix.Open(null, src, sel), "open");
            NaiveMorph.AssertSame(src, Pix.Close(null, src, sel), "close");
        });
    }

    [Fact]
    public void BrickOfSizeOneCopies()
    {
        var rng = new Random(3500);
        Pix src = NaiveMorph.RandomPix(rng, 37, 9, 50);
        NaiveMorph.AssertSame(src, Pix.DilateBrick(null, src, 1, 1), "dilate");
        NaiveMorph.AssertSame(src, Pix.ErodeBrick(null, src, 1, 1), "erode");
        NaiveMorph.AssertSame(src, Pix.OpenBrick(null, src, 1, 1), "open");
        NaiveMorph.AssertSame(src, Pix.CloseBrick(null, src, 1, 1), "close");
        NaiveMorph.AssertSame(src, Pix.CloseSafeBrick(null, src, 1, 1), "close safe");
        Assert.NotSame(src, Pix.DilateBrick(null, src, 1, 1));
    }

    // ---- boundary conditions and argument checks ---------------------------------------

    [Fact]
    public void ErosionDiffersBetweenBoundaryConditionsOnlyAtTheEdge()
    {
        Pix src = Pix.Create(10, 8, 1);
        src.SetAll();
        WithBc(false, () => {
            Pix er = Pix.ErodeBrick(null, src, 3, 3);
            Assert.Equal(0u, NaiveMorph.Get(er, 0, 4));
            Assert.Equal(0u, NaiveMorph.Get(er, 5, 0));
            Assert.Equal(0u, NaiveMorph.Get(er, 9, 7));
            Assert.Equal(1u, NaiveMorph.Get(er, 4, 4));
        });
        WithBc(true, () => {
            Pix er = Pix.ErodeBrick(null, src, 3, 3);
            NaiveMorph.AssertSame(src, er, "symmetric erosion of an all-ON image");
        });
    }

    [Fact]
    public void ResetMorphBoundaryCondition()
    {
        int saved = Pix.MORPH_BC;
        try {
            Pix.ResetMorphBoundaryCondition(L.SYMMETRIC_MORPH_BC);
            Assert.Equal(L.SYMMETRIC_MORPH_BC, Pix.MORPH_BC);
            Pix.ResetMorphBoundaryCondition(L.ASYMMETRIC_MORPH_BC);
            Assert.Equal(L.ASYMMETRIC_MORPH_BC, Pix.MORPH_BC);
            Pix.ResetMorphBoundaryCondition(L.SYMMETRIC_MORPH_BC);
            Pix.ResetMorphBoundaryCondition(7);   // invalid: falls back to asymmetric
            Assert.Equal(L.ASYMMETRIC_MORPH_BC, Pix.MORPH_BC);
        } finally {
            Pix.MORPH_BC = saved;
        }
    }

    [Fact]
    public void DefaultBoundaryConditionIsAsymmetric()
    {
        // Other tests in this collection restore the value they change.
        Assert.Equal(L.ASYMMETRIC_MORPH_BC, Pix.MORPH_BC);
    }

    [Fact]
    public void GetMorphBorderPixelColorTable()
    {
        WithBc(false, () => {
            foreach (int d in new[] { 1, 2, 4, 8, 16, 32 }) {
                Assert.Equal(0u, Pix.GetMorphBorderPixelColor(L.L_MORPH_DILATE, d));
                Assert.Equal(0u, Pix.GetMorphBorderPixelColor(L.L_MORPH_ERODE, d));
            }
        });
        WithBc(true, () => {
            foreach (int d in new[] { 1, 2, 4, 8, 16, 32 })
                Assert.Equal(0u, Pix.GetMorphBorderPixelColor(L.L_MORPH_DILATE, d));
            Assert.Equal(1u, Pix.GetMorphBorderPixelColor(L.L_MORPH_ERODE, 1));
            Assert.Equal(3u, Pix.GetMorphBorderPixelColor(L.L_MORPH_ERODE, 2));
            Assert.Equal(15u, Pix.GetMorphBorderPixelColor(L.L_MORPH_ERODE, 4));
            Assert.Equal(255u, Pix.GetMorphBorderPixelColor(L.L_MORPH_ERODE, 8));
            Assert.Equal(65535u, Pix.GetMorphBorderPixelColor(L.L_MORPH_ERODE, 16));
            Assert.Equal(0xffffff00u, Pix.GetMorphBorderPixelColor(L.L_MORPH_ERODE, 32));
        });
        Assert.Throws<ArgumentException>(() => Pix.GetMorphBorderPixelColor(0, 8));
        Assert.Throws<ArgumentException>(() => Pix.GetMorphBorderPixelColor(L.L_MORPH_ERODE, 3));
    }

    [Fact]
    public void RejectsInvalidArguments()
    {
        Pix gray = Pix.Create(10, 10, 8);
        Pix bin = Pix.Create(10, 10, 1);
        Sel sel = NaiveMorph.Box(3, 3);

        Assert.Throws<ArgumentException>(() => Pix.Dilate(null, gray, sel));
        Assert.Throws<ArgumentException>(() => Pix.Erode(null, gray, sel));
        Assert.Throws<ArgumentException>(() => Pix.HMT(null, gray, sel));
        Assert.Throws<ArgumentException>(() => Pix.Open(null, gray, sel));
        Assert.Throws<ArgumentException>(() => Pix.Close(null, gray, sel));
        Assert.Throws<ArgumentException>(() => Pix.CloseSafe(null, gray, sel));
        Assert.Throws<ArgumentException>(() => Pix.OpenGeneralized(null, gray, sel));
        Assert.Throws<ArgumentException>(() => Pix.CloseGeneralized(null, gray, sel));

        Assert.Throws<ArgumentException>(() => Pix.DilateBrick(null, gray, 3, 3));
        Assert.Throws<ArgumentException>(() => Pix.ErodeBrick(null, gray, 3, 3));
        Assert.Throws<ArgumentException>(() => Pix.OpenBrick(null, gray, 3, 3));
        Assert.Throws<ArgumentException>(() => Pix.CloseBrick(null, gray, 3, 3));
        Assert.Throws<ArgumentException>(() => Pix.CloseSafeBrick(null, gray, 3, 3));
        Assert.Throws<ArgumentException>(() => Pix.DilateCompBrick(null, gray, 3, 3));
        Assert.Throws<ArgumentException>(() => Pix.ErodeCompBrick(null, gray, 3, 3));
        Assert.Throws<ArgumentException>(() => Pix.OpenCompBrick(null, gray, 3, 3));
        Assert.Throws<ArgumentException>(() => Pix.CloseCompBrick(null, gray, 3, 3));
        Assert.Throws<ArgumentException>(() => Pix.CloseSafeCompBrick(null, gray, 3, 3));

        Assert.Throws<ArgumentException>(() => Pix.DilateBrick(null, bin, 0, 3));
        Assert.Throws<ArgumentException>(() => Pix.ErodeBrick(null, bin, 3, 0));
        Assert.Throws<ArgumentException>(() => Pix.OpenBrick(null, bin, -1, 3));
        Assert.Throws<ArgumentException>(() => Pix.CloseBrick(null, bin, 3, -2));
        Assert.Throws<ArgumentException>(() => Pix.CloseSafeBrick(null, bin, 0, 0));
        Assert.Throws<ArgumentException>(() => Pix.DilateCompBrick(null, bin, 0, 3));
        Assert.Throws<ArgumentException>(() => Pix.ErodeCompBrick(null, bin, 3, 0));
        Assert.Throws<ArgumentException>(() => Pix.OpenCompBrick(null, bin, -1, 3));
        Assert.Throws<ArgumentException>(() => Pix.CloseCompBrick(null, bin, 3, -2));
        Assert.Throws<ArgumentException>(() => Pix.CloseSafeCompBrick(null, bin, 0, 0));
    }
}
