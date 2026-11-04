using System;
using Leptonica;

namespace Leptonica.Managed.Tests.Morph;

/// <summary>
/// Tests for morphseq.c: the verification of operation sequences, and the results of the
/// sequence interpreters compared with the explicit calls of morph.c. The boundary condition
/// of the morphology is a process-wide static, so these tests share its collection.
/// </summary>
[Collection("MorphBoundaryCondition")]
public class MorphSeqTests
{
    private static Sarray Split(string sequence)
    {
        Sarray sa = Sarray.Create(0);
        sa.SplitString(sequence, "+");
        return sa;
    }

    [Theory]
    [InlineData("")]
    [InlineData("d3.3")]
    [InlineData("D1.1")]
    [InlineData("e2.5 + E7.1")]
    [InlineData("o5.5 + c3.3")]
    [InlineData("  o 5 . 5 +\tc3.3\n")]
    [InlineData("d3.3+")]
    [InlineData("r1")]
    [InlineData("R1234")]
    [InlineData("r23 + x4")]
    [InlineData("x2")]
    [InlineData("X16")]
    [InlineData("b32 + o1.3 + C3.1 + r23 + e2.2 + D3.2 + X4")]
    [InlineData("b5 + c3.3")]
    [InlineData("b5 + r1 + x2")]
    [InlineData("b5 + r12 + x4")]
    [InlineData("b5 + r1 + r1 + r1 + x8")]
    public void VerifyAcceptsValidSequences(string sequence)
    {
        Assert.True(Pix.MorphSequenceVerify(Split(sequence)), sequence);
    }

    [Theory]
    [InlineData("q3.3")]          // nonexistent op
    [InlineData("d3")]            // missing height
    [InlineData("d3.")]
    [InlineData("d.3")]
    [InlineData("d")]
    [InlineData("d0.3")]          // sizes must be > 0
    [InlineData("o3.0")]
    [InlineData("c-1.3")]
    [InlineData("e3.3 + d3.3 + z")]
    [InlineData("d3.3 +  + e1.1")] // an operation of white space only
    [InlineData("r")]             // no reduction level
    [InlineData("r12345")]        // more than 4 reductions
    [InlineData("r0")]            // level out of range
    [InlineData("r5")]
    [InlineData("r1a")]
    [InlineData("x")]
    [InlineData("x3")]            // not a power of 2 in {2, 4, 8, 16}
    [InlineData("x32")]
    [InlineData("x0")]
    [InlineData("b")]
    [InlineData("b0")]
    [InlineData("b-4")]
    [InlineData("d3.3 + b4")]     // a border must be the first op
    [InlineData("b4 + r1")]       // a border with a net reduction
    [InlineData("b4 + x2")]
    [InlineData("b4 + r1 + x4")]
    public void VerifyRejectsInvalidSequences(string sequence)
    {
        Assert.False(Pix.MorphSequenceVerify(Split(sequence)), sequence);
    }

    [Fact]
    public void MorphSequenceMatchesExplicitCalls()
    {
        var rng = new Random(1234);
        for (int iter = 0; iter < 20; iter++) {
            Pix src = NaiveMorph.RandomPix(rng, rng.Next(20, 80), rng.Next(20, 60), 40);
            Pix orig = Pix.Copy(null, src);
            string ctx = $"iter={iter}";

            Pix expected = Pix.OpenBrick(null, src, 3, 1);
            expected = Pix.CloseSafeBrick(null, expected, 1, 3);
            expected = Pix.DilateBrick(null, expected, 2, 2);
            expected = Pix.ErodeBrick(null, expected, 3, 4);
            Pix actual = src.MorphSequence("o3.1 + C1.3 + d2.2 + E3.4", 0);
            NaiveMorph.AssertSame(expected, actual, ctx + " seq1");
            NaiveMorph.AssertSame(orig, src, ctx + " (pixs changed)");
            Assert.NotSame(src, actual);

            // The white space is ignored
            actual = src.MorphSequence(" o 3.1 +\n C1 .3\t+ d2.2 + E3.4 ", 0);
            NaiveMorph.AssertSame(expected, actual, ctx + " seq1 with white space");

            // A border is added first and removed at the end
            expected = src.AddBorder(16, 0);
            expected = Pix.CloseSafeBrick(null, expected, 5, 5);
            expected = Pix.ErodeBrick(null, expected, 2, 2);
            expected = expected.RemoveBorder(16);
            actual = src.MorphSequence("b16 + c5.5 + e2.2", 0);
            NaiveMorph.AssertSame(expected, actual, ctx + " seq2");
            NaiveMorph.AssertSame(orig, src, ctx + " (pixs changed)");
        }
    }

    [Fact]
    public void MorphSequenceWithoutOperationsCopiesTheInput()
    {
        Pix src = NaiveMorph.RandomPix(new Random(5), 33, 21, 50);
        Pix actual = src.MorphSequence("", 0);
        Assert.NotSame(src, actual);
        NaiveMorph.AssertSame(src, actual, "empty sequence");
    }

    [Fact]
    public void MorphCompSequenceMatchesExplicitCalls()
    {
        var rng = new Random(4321);
        for (int iter = 0; iter < 10; iter++) {
            Pix src = NaiveMorph.RandomPix(rng, rng.Next(40, 120), rng.Next(40, 90), 35);
            Pix orig = Pix.Copy(null, src);
            string ctx = $"iter={iter}";

            Pix expected = Pix.OpenCompBrick(null, src, 7, 1);
            expected = Pix.CloseSafeCompBrick(null, expected, 1, 9);
            expected = Pix.DilateCompBrick(null, expected, 6, 5);
            expected = Pix.ErodeCompBrick(null, expected, 3, 12);
            Pix actual = src.MorphCompSequence("o7.1 + C1.9 + d6.5 + E3.12", 0);
            NaiveMorph.AssertSame(expected, actual, ctx + " seq1");
            NaiveMorph.AssertSame(orig, src, ctx + " (pixs changed)");

            expected = src.AddBorder(24, 0);
            expected = Pix.CloseSafeCompBrick(null, expected, 15, 15);
            expected = Pix.ErodeCompBrick(null, expected, 2, 2);
            expected = expected.RemoveBorder(24);
            actual = src.MorphCompSequence("b24 + c15.15 + e2.2", 0);
            NaiveMorph.AssertSame(expected, actual, ctx + " seq2");
        }
    }

    [Theory]
    [InlineData("q3.3")]
    [InlineData("d3")]
    [InlineData("d0.3")]
    [InlineData("x3")]
    [InlineData("r5")]
    [InlineData("d3.3 + b4")]
    [InlineData("b4 + r1")]
    public void BinarySequencesRejectInvalidSequences(string sequence)
    {
        Pix src = NaiveMorph.RandomPix(new Random(7), 30, 30, 50);
        Assert.Throws<ArgumentException>(() => src.MorphSequence(sequence, 0));
        Assert.Throws<ArgumentException>(() => src.MorphCompSequence(sequence, 0));
        Assert.Throws<ArgumentException>(() => src.MorphSequenceDwa(sequence, 0));
        Assert.Throws<ArgumentException>(() => src.MorphCompSequenceDwa(sequence, 0));
    }

    [Theory]
    [InlineData("q3.3")]          // nonexistent op
    [InlineData("d3")]            // missing height
    [InlineData("d2.3")]          // sizes must be odd
    [InlineData("o3.4")]
    [InlineData("c0.3")]
    [InlineData("x3.3")]          // not a gray op
    [InlineData("t3.3")]          // the tophat needs 'w' or 'b'
    [InlineData("tx3.3")]
    [InlineData("tw4.3")]
    [InlineData("tb3.2")]
    [InlineData("tw")]
    public void GrayMorphSequenceRejectsInvalidSequences(string sequence)
    {
        Pix src = Pix.Create(20, 20, 8);
        Assert.Throws<ArgumentException>(() => src.GrayMorphSequence(sequence, 0, 0));
    }

    [Theory]
    [InlineData("q3.3")]
    [InlineData("d3")]
    [InlineData("d2.3")]
    [InlineData("o3.4")]
    [InlineData("c0.3")]
    [InlineData("t3.3")]          // no tophat for color
    [InlineData("r1")]
    public void ColorMorphSequenceRejectsInvalidSequences(string sequence)
    {
        Pix src = Pix.Create(20, 20, 32);
        Assert.Throws<ArgumentException>(() => src.ColorMorphSequence(sequence, 0, 0));
    }
}
