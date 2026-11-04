using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>Port of <c>leptonica/prog/pixcomp_reg.c</c>.</summary>
public class PixCompRegTests
{
    private static readonly string[] Fnames = {
        "weasel32.png", "weasel2.4c.png",
        "weasel4.16c.png", "weasel4.8g.png",
        "weasel8.149g.png", "weasel8.16g.png"
    };

    [RegFact]
    public void PixComp()
    {
        RegTest rp = new("pixcomp");
        Utils.LeptMkdir("lept/comp");

        Pixacomp pixac = Pixacomp.Create(1);
        Pixa pixa = Pixa.Create(0);

            /* --- Read in the images --- */
        Pix pix1 = TestPaths.ReadInput("marge.jpg");
        Pixcomp pixc1 = Pixcomp.CreateFromPix(pix1, L.IFF_JFIF_JPEG);
        Pix pix2 = Pix.CreateFromPixcomp(pixc1);
        Pixcomp pixc2 = Pixcomp.CreateFromPix(pix2, L.IFF_JFIF_JPEG);
        Pix pix3 = Pix.CreateFromPixcomp(pixc2);
        rp.WritePixAndCheck(pix3, L.IFF_JFIF_JPEG);  /* 0 */
        pixa.AddPix(pix3, L.L_INSERT);
        pixac.AddPix(pix1, L.IFF_DEFAULT);

        Pix pix = TestPaths.ReadInput("feyn.tif");
        pix1 = pix.ScaleToGray6();
        pixc1 = Pixcomp.CreateFromPix(pix1, L.IFF_JFIF_JPEG);
        pix2 = Pix.CreateFromPixcomp(pixc1);
        pixc2 = Pixcomp.CreateFromPix(pix2, L.IFF_JFIF_JPEG);
        pix3 = Pix.CreateFromPixcomp(pixc2);
        rp.WritePixAndCheck(pix3, L.IFF_JFIF_JPEG);  /* 1 */
        pixa.AddPix(pix3, L.L_INSERT);
        pixac.AddPix(pix1, L.IFF_DEFAULT);

        Box box = Box.Create(1144, 611, 690, 180);
        pix1 = pix.ClipRectangle(box, out _)!;
        pixc1 = Pixcomp.CreateFromPix(pix1, L.IFF_TIFF_G4);
        pix2 = Pix.CreateFromPixcomp(pixc1);
        pixc2 = Pixcomp.CreateFromPix(pix2, L.IFF_TIFF_G4);
        pix3 = Pix.CreateFromPixcomp(pixc2);
        rp.WritePixAndCheck(pix3, L.IFF_TIFF_G4);  /* 2 */
        pixa.AddPix(pix3, L.L_INSERT);
        pixac.AddPix(pix1, L.IFF_DEFAULT);

        pix1 = TestPaths.ReadInput("weasel4.11c.png");
        pixc1 = Pixcomp.CreateFromPix(pix1, L.IFF_PNG);
        pix2 = Pix.CreateFromPixcomp(pixc1);
        pixc2 = Pixcomp.CreateFromPix(pix2, L.IFF_PNG);
        pix3 = Pix.CreateFromPixcomp(pixc2);
        rp.WritePixAndCheck(pix3, L.IFF_PNG);  /* 3 */
        pixa.AddPix(pix3, L.L_INSERT);
        pixac.AddPix(pix1, L.IFF_DEFAULT);

            /* Save a tiled composite from the pixa */
        pix1 = pixa.DisplayTiledAndScaled(32, 400, 4, 0, 20, 2);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 4 */

            /* Convert the pixac --> pixa and save a tiled composite */
        Pixa pixa1 = Pixa.CreateFromPixacomp(pixac, L.L_COPY);
        pix1 = pixa1.DisplayTiledAndScaled(32, 400, 4, 0, 20, 2);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 5 */

            /* Make a pixacomp from files, and join */
        Sarray sa = Sarray.Create(0);
        for (int i = 0; i < 6; i++)
            sa.AddString(System.IO.Path.Combine(TestPaths.ProgDir, Fnames[i]), L.L_COPY);
        Pixacomp pixac1 = Pixacomp.CreateFromSA(sa, L.IFF_DEFAULT);
        pixac1.Join(pixac, 0, -1);
        pixa1 = Pixa.CreateFromPixacomp(pixac1, L.L_COPY);
        pix1 = pixa1.DisplayTiledAndScaled(32, 250, 10, 0, 20, 2);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 6 */

            /* Test serialized I/O */
        pixac.Write("/tmp/lept/comp/file1.pac");
        rp.CheckFile("/tmp/lept/comp/file1.pac");  /* 7 */
        pixac1 = Pixacomp.Read("/tmp/lept/comp/file1.pac");
        pixac1.Write("/tmp/lept/comp/file2.pac");
        rp.CheckFile("/tmp/lept/comp/file2.pac");  /* 8 */
        rp.CompareFiles(7, 8);  /* 9 */
        Pixacomp pixac2 = Pixacomp.Read("/tmp/lept/comp/file2.pac");
        pixa1 = Pixa.CreateFromPixacomp(pixac2, L.L_COPY);
        pix1 = pixa1.DisplayTiledAndScaled(32, 250, 4, 0, 20, 2);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 10 */

            /* Test serialized pixacomp I/O to and from memory */
        byte[] data1 = pixac.WriteMem();
        pixac1 = Pixacomp.ReadMem(data1);
        byte[] data2 = pixac1.WriteMem();
        pixac2 = Pixacomp.ReadMem(data2);
        pixac1.Write("/tmp/lept/comp/file3.pac");
        rp.CheckFile("/tmp/lept/comp/file3.pac");  /* 11 */
        pixac2.Write("/tmp/lept/comp/file4.pac");
        rp.CheckFile("/tmp/lept/comp/file4.pac");  /* 12 */
        rp.CompareFiles(11, 12);  /* 13 */

            /* Test pdf generation (both with and without transcoding) */
        pix1 = TestPaths.ReadInput("test24.jpg");
        pix2 = TestPaths.ReadInput("marge.jpg");
        pixac = Pixacomp.Create(2);
        pixac.AddPix(pix1, L.IFF_JFIF_JPEG);
        pixac.AddPix(pix2, L.IFF_JFIF_JPEG);
        PdfIo.SetDateAndVersion(0);
        data1 = pixac.ConvertToPdfData(0, 1.0f, L.L_DEFAULT_ENCODE, 0, "test1");
        rp.WriteDataAndCheck(data1, "pdf");  /* 14 */
        data2 = pixac.FastConvertToPdfData("test2");
        rp.WriteDataAndCheck(data2, "pdf");  /* 15 */

        Assert.Equal(15, rp.Index);
        rp.Cleanup();
    }
}
