using System;
using System.IO;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests;

/// <summary>Tests for ccbord.c on small synthetic images.</summary>
public class CcBordTests
{
    private static Pix FromRows(params string[] rows)
    {
        Pix pix = Pix.Create(rows[0].Length, rows.Length, 1);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (rows[y][x] == '#')
                    pix.SetPixel(x, y, 1);
        return pix;
    }

    private static Pix RingImage() => FromRows(
        "..........",
        ".#####....",
        ".#...#..#.",
        ".#.#.#....",
        ".#...#.##.",
        ".#####.##.",
        "..........");

    [Fact]
    public void OuterBorderOfSinglePixelIsThatPixel()
    {
        Pix pix = FromRows("...", ".#.", "...");
        Ptaa ptaa = pix.GetOuterBordersPtaa();
        Assert.Equal(1, ptaa.Count);
        Pta pta = ptaa.GetPta(0, L.L_CLONE);
        Assert.Equal(1, pta.Count);
        pta.GetIPt(0, out int x, out int y);
        Assert.Equal((1, 1), (x, y));
    }

    [Fact]
    public void OuterBorderIsInGlobalCoords()
    {
        Pix pix = RingImage();
        Ptaa ptaa = pix.GetOuterBordersPtaa();
        Assert.Equal(4, ptaa.Count);
        Pta pta = ptaa.GetPta(0, L.L_CLONE);
        pta.GetIPt(0, out int x, out int y);
        Assert.Equal((1, 1), (x, y));   /* first on pixel of the ring */
        /* the 16 pixels of the square ring; the path is closed, so the start pixel is repeated at the end */
        Assert.Equal(17, pta.Count);
    }

    [Fact]
    public void DisplayImagesReconstructTheInput()
    {
        Pix pixs = RingImage();
        CCBorda ccba = pixs.GetAllCCBorders();
        Assert.Equal(4, ccba.Count);
        Assert.Equal(2, ccba.GetCcb(0).Local!.Count);   /* outer border and one hole */

        Pix pix2 = ccba.DisplayImage2();
        Assert.True(Pix.Equal(pix2, pixs));

        Pix pix1 = ccba.DisplayImage1();
        Assert.True(Pix.Equal(pix1, pixs));
    }

    [Fact]
    public void StepChainsRoundTripThroughCoordinates()
    {
        Pix pixs = RingImage();
        CCBorda ccba = pixs.GetAllCCBorders();
        ccba.GenerateGlobalLocs();
        Pix border1 = ccba.DisplayBorder();

        ccba.GenerateStepChains();
        ccba.StepChainsToPixCoords(L.CCB_GLOBAL_COORDS);
        Pix border2 = ccba.DisplayBorder();
        Assert.True(Pix.Equal(border1, border2));
        /* an isolated pixel has an empty step chain */
        Assert.Equal(0, ccba.GetCcb(1).Step!.GetNuma(0, L.L_CLONE).Count);
    }

    [Fact]
    public void SerializationRoundTrip()
    {
        Pix pixs = RingImage();
        CCBorda ccba = pixs.GetAllCCBorders();
        ccba.GenerateStepChains();

        using var ms = new MemoryStream();
        ccba.WriteStream(ms);
        ms.Position = 0;
        CCBorda ccba2 = CCBorda.ReadStream(ms);

        Assert.Equal(ccba.Count, ccba2.Count);
        Assert.Equal(pixs.Width, ccba2.Width);
        Assert.Equal(pixs.Height, ccba2.Height);
        ccba2.StepChainsToPixCoords(L.CCB_LOCAL_COORDS);
        Assert.True(Pix.Equal(ccba2.DisplayImage2(), pixs));
    }

    [Fact]
    public void SinglePathAndSvg()
    {
        Pix pixs = RingImage();
        CCBorda ccba = pixs.GetAllCCBorders();
        ccba.GenerateSinglePath();
        ccba.GenerateSPGlobalLocs(L.CCB_SAVE_TURNING_PTS);

        /* the hole is joined to the outer border: the single path is longer than the outer border */
        Assert.True(ccba.GetCcb(0).SpLocal!.Count > ccba.GetCcb(0).Local!.GetPta(0, L.L_CLONE).Count);

        string svg = ccba.WriteSVGString();
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"iso-8859-1\"?>", svg);
        Assert.Equal(4, svg.Split("<polygon").Length - 1);
        Assert.EndsWith("</svg> ", svg.Replace("\n", ""));
    }

    [Fact]
    public void ArgumentChecks()
    {
        Pix gray = Pix.Create(4, 4, 8);
        Assert.Throws<ArgumentException>(() => gray.GetAllCCBorders());
        Assert.Throws<ArgumentException>(() => Pix.Create(4, 4, 1).GetOuterBordersPtaa());
        CCBorda ccba = FromRows(".#.").GetAllCCBorders();
        Assert.Throws<ArgumentException>(() => ccba.StepChainsToPixCoords(7));
        Assert.Throws<ArgumentException>(() => ccba.GetCcb(5));
    }
}
