using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests;

/// <summary>Tests for flipdetect.c.</summary>
public class FlipDetectTests
{
    [Theory]
    [InlineData(20.0f, 3.0f, L.L_TEXT_ORIENT_UP)]
    [InlineData(3.0f, 20.0f, L.L_TEXT_ORIENT_LEFT)]
    [InlineData(-20.0f, 3.0f, L.L_TEXT_ORIENT_DOWN)]
    [InlineData(3.0f, -20.0f, L.L_TEXT_ORIENT_RIGHT)]
    [InlineData(10.0f, 9.0f, L.L_TEXT_ORIENT_UNKNOWN)]     /* ratio too small */
    [InlineData(5.0f, 1.0f, L.L_TEXT_ORIENT_UNKNOWN)]      /* below the default minupconf of 8 */
    [InlineData(0.0f, 30.0f, L.L_TEXT_ORIENT_UNKNOWN)]     /* zero confidence: no decision */
    [InlineData(30.0f, 0.0f, L.L_TEXT_ORIENT_UNKNOWN)]
    public void MakeOrientDecisionUsesDefaultThresholds(float upconf, float leftconf, int expected)
    {
        Assert.Equal(expected, Pix.MakeOrientDecision(upconf, leftconf, 0.0f, 0.0f, 0));
    }

    [Fact]
    public void MakeOrientDecisionHonorsGivenThresholds()
    {
        Assert.Equal(L.L_TEXT_ORIENT_UP, Pix.MakeOrientDecision(5.0f, 1.0f, 4.0f, 2.0f, 0));
        Assert.Equal(L.L_TEXT_ORIENT_UNKNOWN, Pix.MakeOrientDecision(5.0f, 1.0f, 4.0f, 6.0f, 0));
        Assert.Equal(L.L_TEXT_ORIENT_DOWN, Pix.MakeOrientDecision(-5.0f, 1.0f, 4.0f, 2.0f, 0));
    }

    [Fact]
    public void BlankImageHasNoOrientationSignal()
    {
        Pix pix = Pix.Create(300, 200, 1);
        Assert.Equal(0.0f, pix.UpDownDetect(0, 0, 0));
        Assert.Equal(0.0f, pix.UpDownDetect(0, 5, 0));
        Assert.Equal(0.0f, pix.MirrorDetect(0, 0));
        pix.OrientDetect(out float upconf, out float leftconf, 0, 0);
        Assert.Equal((0.0f, 0.0f), (upconf, leftconf));

        /* no decision: a copy without rotation */
        Pix result = pix.OrientCorrect(0.0f, 0.0f, out upconf, out leftconf, out int rotation, 0);
        Assert.Equal(0, rotation);
        Assert.NotSame(pix, result);
        Assert.True(Pix.Equal(pix, result));
    }

    [Fact]
    public void ArgumentChecks()
    {
        Pix gray = Pix.Create(50, 50, 8);
        Assert.Throws<ArgumentException>(() => gray.UpDownDetect(0, 0, 0));
        Assert.Throws<ArgumentException>(() => gray.MirrorDetect(0, 0));
        Assert.Throws<ArgumentException>(() => gray.OrientDetect(out _, out _, 0, 0));
        Assert.Throws<ArgumentException>(() => gray.OrientCorrect(0.0f, 0.0f, out _, out _, out _, 0));
    }
}
