using System.Text;
using Leptonica;
using Xunit;

namespace Leptonica.Managed.Tests.Reg;

/// <summary>
/// ccbord_reg.c: regression test for border-following representations of binary images.
/// </summary>
public class CcBordRegTests
{
    [RegFact]
    public void CcBord()
    {
        var rp = new RegTest("ccbord");
        Utils.LeptMkdir("lept/ccbord");

        RunCCBordTest("feyn-fract.tif", rp);
        RunCCBordTest("dreyfus1.png", rp);
        Assert.Equal(13, rp.Index);
        rp.Cleanup();
    }

    private static void RunCCBordTest(string fname, RegTest rp)
    {
        Pix pixs = TestPaths.ReadInput(fname);

        /*------------------------------------------------------------------*
         *        Get border representation and verify border pixels        *
         *------------------------------------------------------------------*/
        CCBorda ccba = pixs.GetAllCCBorders();

            /* Get global locs directly and display borders */
        ccba.GenerateGlobalLocs();
        Pix pixd = ccba.DisplayBorder();
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 0,7 */

            /* Get step chain code, then global coords, and display borders */
        ccba.GenerateStepChains();
        ccba.StepChainsToPixCoords(L.CCB_GLOBAL_COORDS);
        pixd = ccba.DisplayBorder();
        rp.WritePixAndCheck(pixd, L.IFF_PNG);  /* 1,8 */

            /* Display image */
        Pix pixc = ccba.DisplayImage2();
        rp.WritePixAndCheck(pixc, L.IFF_PNG);  /* 2,9 */

        /*----------------------------------------------------------*
         *        Write to file (compressed) and read back          *
         *----------------------------------------------------------*/
        ccba.Write("/tmp/lept/ccbord/stepdata.ccb");
        CCBorda ccba2 = CCBorda.Read("/tmp/lept/ccbord/stepdata.ccb");

            /* Display the border pixels again */
        ccba2.StepChainsToPixCoords(L.CCB_GLOBAL_COORDS);
        Pix pixd2 = ccba2.DisplayBorder();
        rp.WritePixAndCheck(pixd2, L.IFF_PNG);  /* 3,10 */

            /* Display image again */
        ccba2.StepChainsToPixCoords(L.CCB_LOCAL_COORDS);
        Pix pixc2 = ccba2.DisplayImage2();
        rp.WritePixAndCheck(pixc2, L.IFF_PNG);  /* 4,11 */

        /*----------------------------------------------------------*
         *     Make, display and check single path border for svg   *
         *----------------------------------------------------------*/
            /* Make local single path border for svg */
        ccba.GenerateSinglePath();

            /* Generate global single path border */
        ccba.GenerateSPGlobalLocs(L.CCB_SAVE_TURNING_PTS);

            /* Display border pixels from single path */
        Pix pixd3 = ccba.DisplaySPBorder();
        rp.WritePixAndCheck(pixd3, L.IFF_PNG);  /* 5,12 */

            /* Output in svg file format */
        string svgstr = ccba.WriteSVGString();
        rp.WriteDataAndCheck(Encoding.Latin1.GetBytes(svgstr), "ccb"); /* 6,13 */
    }
}
