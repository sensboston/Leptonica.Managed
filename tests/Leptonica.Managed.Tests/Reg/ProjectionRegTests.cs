using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>projection_reg.c: projection stats for rows and columns.</summary>
public class ProjectionRegTests
{
    [RegFact]
    public void Projection()
    {
        Pix pixs, pix1, pix2;
        var rp = new RegTest("projection");

            /* Use for input two different images */
        pixs = TestPaths.ReadInput("projectionstats.jpg");
        pix1 = pixs.ConvertTo8(false);
        pixs = TestPaths.ReadInput("feyn.tif");
        pix2 = pixs.ScaleToGray4();

        TestProjection(pix1);
        TestProjection(pix2);

            /* The checks of the C test are on the plots made by gplotSimplePix1(),
             * which needs gnuplot (out of scope); the C goldens were
             * made without it and there are none.  So there is no check by the
             * RegTest here. */
        Assert.Equal(-1, rp.Index);
        rp.Cleanup();
    }

    /*
     *  Test both vertical and horizontal projections on this image.
     *  Rotate the image by 90 degrees for the horizontal projection,
     *  so that the two results should be identical.
     *  (Here the stats are compared directly, instead of their plots.)
     */
    private static void TestProjection(Pix pixs)
    {
        Numa na1, na2, na3, na4, na5, na6, na7, na8, na9, na10, na11, na12;
        Pix pixd;

        pixs.ColumnStats(null, out na1, out na3, out na5, out na7, out na9, out na11);
        pixd = pixs.RotateOrth(1);
        pixd.RowStats(null, out na2, out na4, out na6, out na8, out na10, out na12);

        Assert.True(na1.Similar(na2, 0.0f));
        Assert.True(na3.Similar(na4, 0.0f));
        Assert.True(na5.Similar(na6, 0.0f));
        Assert.True(na7.Similar(na8, 0.0f));
        Assert.True(na9.Similar(na10, 0.0f));
        Assert.True(na11.Similar(na12, 0.0f));
    }
}
