using System;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests;

public class PartifyTests
{
    [Fact]
    public void LocateStaveSetsAndRemoveVGapsWorkAndPartifyHitsG4Stub()
    {
            /* Build synthetic 1 bpp score page with two large stave-set blocks */
        Pix score = Pix.Create(1600, 1200, 1);
        score.Rasterop(100, 100, 1400, 300, L.PIX_SET, null, 0, 0);
        score.Rasterop(100, 600, 1400, 300, L.PIX_SET, null, 0, 0);

        Boxa staves = Partify.LocateStaveSets(score, 0, null);
        Assert.Equal(2, staves.Count);

            /* Verify vertical gap between stave boxes was closed by RemoveVGaps */
        staves.GetBoxGeometry(0, out _, out int y0, out _, out int h0);
        staves.GetBoxGeometry(1, out _, out int y1, out _, out _);
        Assert.True(Math.Abs((y0 + h0) - y1) <= 4);

            /* Single-box RemoveVGaps edge case */
        Boxa single = Boxa.Create(1);
        single.AddBox(Box.Create(10, 20, 100, 50), L.L_INSERT);
        Assert.Equal(0, Partify.RemoveVGaps(single));
        single.GetBoxGeometry(0, out int sx, out int sy, out int sw, out int sh);
        Assert.Equal((10, 20, 100, 50), (sx, sy, sw, sh));

            /* Partify.Pixac runs full preprocessing, stave extraction, and G4 PDF assembly */
        Pixacomp pixac = Pixacomp.Create(1);
        pixac.AddPix(score, L.IFF_PNG);
        Partify.Pixac(pixac, 1, "/tmp/lept/partify_test", null);
        Assert.Equal(2, PdfIo.GetPageCount("/tmp/lept/partify_test-0.pdf"));
    }
}
