using System.IO;
using System.Text;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// encoding_reg.c: regression test for encoding/decoding of binary data.
/// </summary>
public class EncodingRegTests
{
    [RegFact]
    public void Encoding()
    {
        var rp = new RegTest("encoding");
        Utils.LeptMkdir("lept/encode");

            /* Test ascii85 encoding */
        byte[] bina = Utils.BinaryRead(Path.Combine(TestPaths.ProgDir, "karen8.jpg"));
        string a85a = LeptEncoding.EncodeAscii85(bina);
        byte[] bina2 = LeptEncoding.DecodeAscii85(a85a);
        rp.CompareValues(bina.Length, bina2.Length, 0.0f);  /* 0 */

            /* Test the reconstructed image */
        using var ms = new MemoryStream(bina2);
        Pix pix1 = Pix.ReadStream(ms, 0);
        rp.WritePixAndCheck(pix1, L.IFF_JFIF_JPEG);  /* 1 */

            /* Test with compression, starting with ascii data */
        byte[] a85aBytes = System.Text.Encoding.ASCII.GetBytes(a85a);
        string a85c = LeptEncoding.EncodeAscii85WithComp(a85aBytes);
        byte[] bin85c = LeptEncoding.DecodeAscii85WithComp(a85c);
        rp.CompareStrings(a85aBytes, bin85c);  /* 2 */

            /* Test with compression, starting with binary data */
        string a85c2 = LeptEncoding.EncodeAscii85WithComp(bin85c);
        byte[] bin85c2 = LeptEncoding.DecodeAscii85WithComp(a85c2);
        rp.CompareStrings(bin85c, bin85c2);  /* 3 */

            /* Test storing and retrieving compressed text from pix */
        bina = Utils.BinaryRead(Path.Combine(TestPaths.ProgDir, "weasel32.png"));
        pix1 = TestPaths.ReadInput("rabi.png");
        pix1.SetTextCompNew(bina);
        bina2 = pix1.GetTextCompNew();
        rp.CompareStrings(bina, bina2);  /* 4 */

        Assert.Equal(4, rp.Index);
        rp.Cleanup();
    }

    [RegFact]
    public void EncodingPngRoundTrip()
    {
        byte[] bina = Utils.BinaryRead(Path.Combine(TestPaths.ProgDir, "weasel32.png"));
        string a85a = LeptEncoding.EncodeAscii85(bina);
        byte[] bina2 = LeptEncoding.DecodeAscii85(a85a);
        Assert.Equal(bina, bina2);

        Pix pix1 = Pix.ReadMemPng(bina2);
        Pix pixOrig = TestPaths.ReadInput("weasel32.png");
        Assert.True(Pix.Equal(pixOrig, pix1));

        byte[] a85aBytes = System.Text.Encoding.ASCII.GetBytes(a85a);
        string a85c = LeptEncoding.EncodeAscii85WithComp(a85aBytes);
        byte[] bin85c = LeptEncoding.DecodeAscii85WithComp(a85c);
        Assert.Equal(a85aBytes, bin85c);

        string a85c2 = LeptEncoding.EncodeAscii85WithComp(bin85c);
        byte[] bin85c2 = LeptEncoding.DecodeAscii85WithComp(a85c2);
        Assert.Equal(bin85c, bin85c2);

        Pix rabi = TestPaths.ReadInput("rabi.png");
        rabi.SetTextCompNew(bina);
        Assert.Equal(bina, rabi.GetTextCompNew());
    }
}
