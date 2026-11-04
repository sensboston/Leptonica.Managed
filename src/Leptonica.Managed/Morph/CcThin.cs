using System;

namespace Leptonica;

/*
 *  ccthin.c: Connectivity-preserving thinning.
 *
 *      Pixa.ThinConnected (pixaThinConnected)
 *      Pix.ThinConnected (pixThinConnected)
 *      Pix.ThinConnectedBySet (pixThinConnectedBySet)
 *      Sela.MakeThinSets (selaMakeThinSets)
 *
 *  ------------------------------------------------------------
 *  The sels used here (and their rotated counterparts) are the
 *  useful 3x3 Sels for thinning.   They are defined in sel2.c,
 *  and the sets are constructed in selaMakeThinSets().
 *  The notation is based on "Connectivity-preserving morphological
 *  image transformations", a version of which can be found at
 *            http://www.leptonica.com/papers/conn.pdf
 *  ------------------------------------------------------------
 */

/// <summary>Constants from pix.h used by ccthin.c.</summary>
public static partial class L
{
    /* Thinning of the foreground or the background */
    /// <summary>Thin the foreground of a 1 bpp image.</summary>
    public const int L_THIN_FG = 1;
    /// <summary>Thin the background of a 1 bpp image.</summary>
    public const int L_THIN_BG = 2;
}

public sealed partial class Pixa
{
    /*----------------------------------------------------------------*
     *                      CC-preserving thinning                    *
     *----------------------------------------------------------------*/
    // leptonica: pixaThinConnected (ccthin.c)
    /// <summary>Thins each pix of a pixa, preserving connectivity.</summary>
    /// <param name="type">L_THIN_FG, L_THIN_BG</param>
    /// <param name="connectivity">4 or 8</param>
    /// <param name="maxiters">max number of iters allowed;
    ///     use 0 to iterate until completion</param>
    /// <returns>pixds</returns>
    /// <remarks>(1) See notes in pixThinConnected().</remarks>
    public Pixa ThinConnected(int type, int connectivity, int maxiters)
    {
        const string func = "pixaThinConnected";
        Pixa pixas = this;
        if (type != L.L_THIN_FG && type != L.L_THIN_BG)
            throw Lept.Arg(func, "invalid fg/bg type");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not 4 or 8");
        if (maxiters == 0) maxiters = 10000;

        pixas.VerifyDepth(out _, out int d);
        if (d != 1)
            throw Lept.Arg(func, "pix are not all 1 bpp");

        Sela sela;
        if (connectivity == 4)
            sela = Sela.MakeThinSets(1, 0);
        else  /* connectivity == 8 */
            sela = Sela.MakeThinSets(5, 0);

        int n = pixas.Count;
        Pixa pixad = Create(n);
        for (int i = 0; i < n; i++) {
            Pix pix1 = pixas.GetPix(i, L.L_CLONE);
            Pix pix2 = pix1.ThinConnectedBySet(type, sela, maxiters);
            pixad.AddPix(pix2, L.L_INSERT);
        }

        return pixad;
    }
}

public sealed partial class Pix
{
    // leptonica: pixThinConnected (ccthin.c)
    /// <summary>Thins a 1 bpp image, preserving connectivity.</summary>
    /// <param name="type">L_THIN_FG, L_THIN_BG</param>
    /// <param name="connectivity">4 or 8</param>
    /// <param name="maxiters">max number of iters allowed;
    ///     use 0 to iterate until completion</param>
    /// <remarks>
    /// (1) See "Connectivity-preserving morphological image transformations,"
    ///     Dan S. Bloomberg, in SPIE Visual Communications and Image
    ///     Processing, Conference 1606, pp. 320-334, November 1991,
    ///     Boston, MA.   A web version is available at
    ///         http://www.leptonica.com/papers/conn.pdf
    /// (2) This is a simple interface for two of the best iterative
    ///     morphological thinning algorithms, for 4-c.c and 8-c.c.
    ///     Each iteration uses a mixture of parallel operations
    ///     (using several different 3x3 Sels) and serial operations.
    ///     Specifically, each thinning iteration consists of
    ///     four sequential thinnings from each of four directions.
    ///     Each of these thinnings is a parallel composite
    ///     operation, where the union of a set of HMTs are set
    ///     subtracted from the input.  For 4-cc thinning, we
    ///     use 3 HMTs in parallel, and for 8-cc thinning we use 4 HMTs.
    /// (3) A "good" thinning algorithm is one that generates a skeleton
    ///     that is near the medial axis and has neither pruned
    ///     real branches nor left extra dendritic branches.
    /// (4) Duality between operations on fg and bg require switching
    ///     the connectivity.  To thin the foreground, which is the usual
    ///     situation, use type == L_THIN_FG.  Thickening the foreground
    ///     is equivalent to thinning the background (type == L_THIN_BG),
    ///     where the alternate connectivity gets preserved.
    ///     For example, to thicken the fg with 2 rounds of iterations
    ///     using 4-c.c., thin the bg using Sels that preserve 8-connectivity:
    ///        Pix pix = pixs.ThinConnected(L_THIN_BG, 8, 2);
    /// (5) This makes and destroys the sela set each time. It's not a large
    ///     overhead, but if you are calling this thousands of times on
    ///     very small images, you can avoid the overhead; e.g.
    ///        Sela sela = Sela.MakeThinSets(1, 0);  // for 4-c.c.
    ///        Pix pix = pixs.ThinConnectedBySet(L_THIN_FG, sela, 0);
    ///     using set 1 for 4-c.c. and set 5 for 8-c.c operations.
    /// </remarks>
    public Pix ThinConnected(int type, int connectivity, int maxiters)
    {
        const string func = "pixThinConnected";
        Pix pixs = this;
        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (type != L.L_THIN_FG && type != L.L_THIN_BG)
            throw Lept.Arg(func, "invalid fg/bg type");
        if (connectivity != 4 && connectivity != 8)
            throw Lept.Arg(func, "connectivity not 4 or 8");
        if (maxiters == 0) maxiters = 10000;

        Sela sela;
        if (connectivity == 4)
            sela = Sela.MakeThinSets(1, 0);
        else  /* connectivity == 8 */
            sela = Sela.MakeThinSets(5, 0);

        return pixs.ThinConnectedBySet(type, sela, maxiters);
    }

    // leptonica: pixThinConnectedBySet (ccthin.c)
    /// <summary>Thins a 1 bpp image using a set of Sels for parallel composite HMTs.</summary>
    /// <param name="type">L_THIN_FG, L_THIN_BG</param>
    /// <param name="sela">of Sels for parallel composite HMTs</param>
    /// <param name="maxiters">max number of iters allowed;
    ///     use 0 to iterate until completion</param>
    /// <remarks>
    /// (1) See notes in pixThinConnected().
    /// (2) This takes a sela representing one of 11 sets of HMT Sels.
    ///     The HMTs from this set are run in parallel and the result
    ///     is OR'd before being subtracted from the source.  For each
    ///     iteration, this "parallel" thin is performed four times
    ///     sequentially, for sels rotated by 90 degrees in all four
    ///     directions.
    /// (3) The "parallel" and "sequential" nomenclature is standard
    ///     in digital filtering.  Here, "parallel" operations work on the
    ///     same source (pixd), and accumulate the results in a temp
    ///     image before actually applying them to the source (in this
    ///     case, using an in-place subtraction).  "Sequential" operations
    ///     operate directly on the source (pixd) to produce the result
    ///     (in this case, with four sequential thinning operations, one
    ///     from each of four directions).
    /// </remarks>
    public Pix ThinConnectedBySet(int type, Sela sela, int maxiters)
    {
        const string func = "pixThinConnectedBySet";
        Pix pixs = this;
        if (pixs.Depth != 1)
            throw Lept.Arg(func, "pixs not 1 bpp");
        if (type != L.L_THIN_FG && type != L.L_THIN_BG)
            throw Lept.Arg(func, "invalid fg/bg type");
        if (maxiters == 0) maxiters = 10000;

            /* Set up array of temp pix to hold hmts */
        int nsels = sela.Count;
        Pixa pixahmt = Pixa.Create(nsels);
        for (int i = 0; i < nsels; i++) {
            Pix pix1 = CreateTemplate(pixs);
            pixahmt.AddPix(pix1, L.L_INSERT);
        }
        Pix?[] pixhmt = pixahmt.GetPixArray();  /* array owned by pixahmt */

            /* Set up initial image for fg thinning */
        Pix pixd;
        if (type == L.L_THIN_FG)
            pixd = Copy(null, pixs);
        else  /* bg thinning */
            pixd = Invert(null, pixs);

            /* Thin the fg, with up to maxiters iterations */
        for (int i = 0; i < maxiters; i++) {
            Pix pix1 = Copy(null, pixd);  /* test for completion */
            for (int r = 0; r < 4; r++) {  /* over 90 degree rotations of Sels */
                for (int j = 0; j < nsels; j++) {  /* over individual sels in sela */
                    Sel sel = sela.GetSel(j);  /* not a copy */
                    Sel selr = sel.RotateOrth(r);
                    HMT(pixhmt[j], pixd, selr);
                    if (j > 0)
                        Or(pixhmt[0], pixhmt[0]!, pixhmt[j]!);  /* accum result */
                }
                Subtract(pixd, pixd, pixhmt[0]!);  /* remove result */
            }
            bool same = Equal(pixd, pix1);
            if (same) {
/*                Log.Info(func, CFormat.Sprintf("%d iterations to completion", i)); */
                break;
            }
        }

            /* This is a bit tricky. If we're thickening the foreground, then
             * we get a fg border of thickness equal to the number of
             * iterations.  This border is connected to all components that
             * were initially touching the border, but as it grows, it does
             * not touch other growing components -- it leaves a 1 pixel wide
             * background between it and the growing components, and that
             * thin background prevents the components from growing further.
             * This border can be entirely removed as follows:
             * (1) Subtract the original (unthickened) image pixs from the
             *     thickened image. This removes the pixels that were originally
             *     touching the border.
             * (2) Get all remaining pixels that are connected to the border.
             * (3) Remove those pixels from the thickened image.  */
        if (type == L.L_THIN_BG) {
            Invert(pixd, pixd);  /* finish with duality */
            Pix pix1 = Subtract(null, pixd, pixs);
            Pix pix2 = pix1.ExtractBorderConnComps(4);
            Subtract(pixd, pixd, pix2);
        }

        return pixd;
    }
}

public sealed partial class Sela
{
    // leptonica: selaMakeThinSets (ccthin.c)
    /// <summary>Makes a specific set of Sels for thinning.</summary>
    /// <param name="index">into specific sets</param>
    /// <param name="debug">1 to output display of sela</param>
    /// <remarks>
    /// (1) These are specific sets of HMTs to be used in parallel for
    ///     for thinning from each of four directions.
    /// (2) The sets are indexed as follows:
    ///     For thinning (e.g., run to completion):
    ///         index = 1     sel_4_1, sel_4_2, sel_4_3
    ///         index = 2     sel_4_1, sel_4_5, sel_4_6
    ///         index = 3     sel_4_1, sel_4_7, sel_4_7_rot
    ///         index = 4     sel_48_1, sel_48_1_rot, sel_48_2
    ///         index = 5     sel_8_2, sel_8_3, sel_8_5, sel_8_6
    ///         index = 6     sel_8_2, sel_8_3, sel_48_2
    ///         index = 7     sel_8_1, sel_8_5, sel_8_6
    ///         index = 8     sel_8_2, sel_8_3, sel_8_8, sel_8_9
    ///         index = 9     sel_8_5, sel_8_6, sel_8_7, sel_8_7_rot
    ///     For thickening (e.g., just a few iterations):
    ///         index = 10    sel_4_2, sel_4_3
    ///         index = 11    sel_8_4
    /// (3) For a very smooth skeleton, use set 1 for 4 connected and
    ///     set 5 for 8 connected thins.
    /// </remarks>
    public static Sela MakeThinSets(int index, int debug)
    {
        const string func = "selaMakeThinSets";
        if (index < 1 || index > 11)
            throw Lept.Arg(func, "invalid index");

        Sel? sel;
        Sela sela1;
        Sela sela2 = Create(4);
        switch (index) {
        case 1:
            sela1 = Thin4cc(null);
            sela1.FindSelByName("sel_4_1", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            sela1.FindSelByName("sel_4_2", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            sela1.FindSelByName("sel_4_3", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            break;
        case 2:
            sela1 = Thin4cc(null);
            sela1.FindSelByName("sel_4_1", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            sela1.FindSelByName("sel_4_5", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            sela1.FindSelByName("sel_4_6", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            break;
        case 3:
            sela1 = Thin4cc(null);
            sela1.FindSelByName("sel_4_1", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            sela1.FindSelByName("sel_4_7", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            sel = sel!.RotateOrth(1);
            sela2.AddSel(sel, "sel_4_7_rot", L.L_INSERT);
            break;
        case 4:
            sela1 = Thin4and8cc(null);
            sela1.FindSelByName("sel_48_1", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            sel = sel!.RotateOrth(1);
            sela2.AddSel(sel, "sel_48_1_rot", L.L_INSERT);
            sela1.FindSelByName("sel_48_2", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            break;
        case 5:
            sela1 = Thin8cc(null);
            sela1.FindSelByName("sel_8_2", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            sela1.FindSelByName("sel_8_3", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            sela1.FindSelByName("sel_8_5", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            sela1.FindSelByName("sel_8_6", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            break;
        case 6: {
            sela1 = Thin8cc(null);
            Sela sela3 = Thin4and8cc(null);
            sela1.FindSelByName("sel_8_2", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            sela1.FindSelByName("sel_8_3", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            sela3.FindSelByName("sel_48_2", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            break;
        }
        case 7:
            sela1 = Thin8cc(null);
            sela1.FindSelByName("sel_8_1", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            sela1.FindSelByName("sel_8_5", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            sela1.FindSelByName("sel_8_6", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            break;
        case 8:
            sela1 = Thin8cc(null);
            sela1.FindSelByName("sel_8_2", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            sela1.FindSelByName("sel_8_3", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            sela1.FindSelByName("sel_8_8", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            sela1.FindSelByName("sel_8_9", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            break;
        case 9:
            sela1 = Thin8cc(null);
            sela1.FindSelByName("sel_8_5", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            sela1.FindSelByName("sel_8_6", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            sela1.FindSelByName("sel_8_7", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            sel = sel!.RotateOrth(1);
            sela2.AddSel(sel, "sel_8_7_rot", L.L_INSERT);
            break;
        case 10:  /* thicken for this one; use just a few iterations */
            sela1 = Thin4cc(null);
            sela1.FindSelByName("sel_4_2", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            sela1.FindSelByName("sel_4_3", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            break;
        default:  /* case 11: thicken for this one; use just a few iterations */
            sela1 = Thin8cc(null);
            sela1.FindSelByName("sel_8_4", out _, out sel);
            sela2.AddSel(sel!, null, L.L_COPY);
            break;
        }

            /* Optionally display the sel set */
        if (debug != 0) {
            Utils.LeptMkdir("/lept/sels");
            Pix pix1 = sela2.DisplayInPix(35, 3, 15, 4);
            string buf = CFormat.Sprintf("/tmp/lept/sels/set%d.png", index);
            Pix.Write(buf, pix1, L.IFF_PNG);
            pix1.Display(100, 100);
        }

        return sela2;
    }
}
