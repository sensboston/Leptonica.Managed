using System;
using System.IO;
using Leptonica;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// selio_reg.c: selaWrite() / selaRead(), creation of a Sela from a file
/// and from strings, and rejection of strings without exactly one origin.
/// </summary>
public class SelIoRegTests
{
    private const string textsel1 = "x  oo " +
                                    "x oOo " +
                                    "x  o  " +
                                    "x     " +
                                    "xxxxxx";
    private const string textsel2 = " oo  x" +
                                    " oOo x" +
                                    "  o  x" +
                                    "     x" +
                                    "xxxxxx";
    private const string textsel3 = "xxxxxx" +
                                    "x     " +
                                    "x  o  " +
                                    "x oOo " +
                                    "x  oo ";
    private const string textsel4 = "xxxxxx" +
                                    "     x" +
                                    "  o  x" +
                                    " oOo x" +
                                    " oo  x";
    private const string textsel5 = "xxxxxx" +
                                    "     x" +
                                    "  o  x" +
                                    " ooo x" +
                                    " oo  x";
    private const string textsel6 = "xxXxxx" +
                                    "     x" +
                                    "  o  x" +
                                    " oOo x" +
                                    " oo  x";

    [RegFact]
    public void SelIo()
    {
        float val;
        Pix pix;
        Sel? sel;
        Sela sela1, sela2;
        var rp = new RegTest("selio");

            /* selaRead() / selaWrite()  */
        sela1 = Sela.AddBasic(null);
        sela1.Write("/tmp/lept/regout/sel.0.sela");
        rp.CheckFile("/tmp/lept/regout/sel.0.sela");  /* 0 */
        sela2 = Sela.Read("/tmp/lept/regout/sel.0.sela");
        sela2.Write("/tmp/lept/regout/sel.1.sela");
        rp.CheckFile("/tmp/lept/regout/sel.1.sela");  /* 1 */
        rp.CompareFiles(0, 1);  /* 2 */

            /* Create from file and display result */
        sela1 = Sela.CreateFromFile(Path.Combine(TestPaths.ProgDir, "flipsels.txt"));
        pix = sela1.DisplayInPix(31, 3, 15, 4);
        rp.WritePixAndCheck(pix, L.IFF_PNG);  /* 3 */
        sela1.Write("/tmp/lept/regout/sel.3.sela");
        rp.CheckFile("/tmp/lept/regout/sel.3.sela");  /* 4 */

            /* Create the same set of Sels from compiled strings and compare */
        sela2 = Sela.Create(4);
        sel = Sel.CreateFromString(textsel1, 5, 6, "textsel1");
        sela2.AddSel(sel, null, 0);
        sel = Sel.CreateFromString(textsel2, 5, 6, "textsel2");
        sela2.AddSel(sel, null, 0);
        sel = Sel.CreateFromString(textsel3, 5, 6, "textsel3");
        sela2.AddSel(sel, null, 0);
        sel = Sel.CreateFromString(textsel4, 5, 6, "textsel4");
        sela2.AddSel(sel, null, 0);
        sela2.Write("/tmp/lept/regout/sel.4.sela");
        rp.CheckFile("/tmp/lept/regout/sel.4.sela");  /* 5 */
        rp.CompareFiles(4, 5);  /* 6 */

            /* Attempt to create sels from invalid strings (0 or 2 origins).
             * C returns NULL after logging an error; the managed function
             * throws ArgumentException. */
        sel = null;
        try {
            sel = Sel.CreateFromString(textsel5, 5, 6, "textsel5");
        } catch (ArgumentException) {
        }
        val = (sel != null) ? 1.0f : 0.0f;
        rp.CompareValues(val, 0.0f, 0.0f);  /* 7 */
        sel = null;
        try {
            sel = Sel.CreateFromString(textsel6, 5, 6, "textsel6");
        } catch (ArgumentException) {
        }
        val = (sel != null) ? 1.0f : 0.0f;
        rp.CompareValues(val, 0.0f, 0.0f);  /* 8 */

        Assert.Equal(8, rp.Index);
        rp.Cleanup();
    }
}
