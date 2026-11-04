using System;

namespace Leptonica;

/*
 *  sel2.c: Contains definitions of simple structuring elements
 *
 *      Basic brick structuring elements
 *          Sela.AddBasic()
 *               Linear horizontal and vertical
 *               Square
 *               Diagonals
 *
 *      Simple hit-miss structuring elements
 *          Sela.AddHitMiss()
 *               Isolated foreground pixel
 *               Horizontal and vertical edges
 *               Slanted edge
 *               Corners
 *
 *      Structuring elements for comparing with DWA operations
 *          Sela.AddDwaLinear()
 *          Sela.AddDwaCombs()
 *
 *      Structuring elements for the intersection of lines
 *          Sela.AddCrossJunctions()
 *          Sela.AddTJunctions()
 *
 *      Structuring elements for connectivity-preserving thinning operations
 *          Sela.Thin4cc()
 *          Sela.Thin8cc()
 *          Sela.Thin4and8cc()
 *
 *      Other structuring elements
 *          Sel.MakePlusSign()
 *
 *  Differences from C:
 *    - The C functions taking an optional sela become static methods of Sela
 *      that return the (possibly newly created) sela.
 *    - sela4ccThin(), sela8ccThin() and sela4and8ccThin() are named Thin4cc(),
 *      Thin8cc() and Thin4and8cc().
 *    - Errors throw instead of returning NULL.
 */

public sealed partial class Sela
{
    /* Linear brick sel sizes, including all those that are required
     * for decomposable sels up to size 63. */
    private const int num_linear = 25;
    private static readonly int[] basic_linear = {2, 3, 4, 5, 6, 7, 8, 9, 10, 11,
           12, 13, 14, 15, 20, 21, 25, 30, 31, 35, 40, 41, 45, 50, 51};

    /* ------------------------------------------------------------------- *
     *                    Basic brick structuring elements                 *
     * ------------------------------------------------------------------- */
    // leptonica: selaAddBasic (sel2.c)
    /// <param name="sela">[optional]; if null, a new sela is made</param>
    /// <returns>sela with additional sels</returns>
    /// <remarks>
    /// (1) Adds the following sels:
    ///       ~ all linear (horiz, vert) brick sels that are
    ///         necessary for decomposable sels up to size 63
    ///       ~ square brick sels up to size 10
    ///       ~ 4 diagonal sels
    /// </remarks>
    public static Sela AddBasic(Sela? sela)
    {
        int i, size;
        Sel sel;
        string name;

        sela ??= Create(0);

            /*--------------------------------------------------------------*
             *             Linear horizontal and vertical sels              *
             *--------------------------------------------------------------*/
        for (i = 0; i < num_linear; i++) {
            size = basic_linear[i];
            sel = Sel.CreateBrick(1, size, 0, size / 2, L.SEL_HIT);
            name = $"sel_{size}h";
            sela.AddSel(sel, name, 0);
        }
        for (i = 0; i < num_linear; i++) {
            size = basic_linear[i];
            sel = Sel.CreateBrick(size, 1, size / 2, 0, L.SEL_HIT);
            name = $"sel_{size}v";
            sela.AddSel(sel, name, 0);
        }

            /*-----------------------------------------------------------*
             *                      2-d Bricks                           *
             *-----------------------------------------------------------*/
        for (i = 2; i <= 5; i++) {
            sel = Sel.CreateBrick(i, i, i / 2, i / 2, L.SEL_HIT);
            name = $"sel_{i}";
            sela.AddSel(sel, name, 0);
        }

            /*-----------------------------------------------------------*
             *                        Diagonals                          *
             *-----------------------------------------------------------*/
            /*  0c  1
                1   0  */
        sel = Sel.CreateBrick(2, 2, 0, 0, L.SEL_HIT);
        sel.SetElement(0, 0, 0);
        sel.SetElement(1, 1, 0);
        sela.AddSel(sel, "sel_2dp", 0);

            /*  1c  0
                0   1   */
        sel = Sel.CreateBrick(2, 2, 0, 0, L.SEL_HIT);
        sel.SetElement(0, 1, 0);
        sel.SetElement(1, 0, 0);
        sela.AddSel(sel, "sel_2dm", 0);

            /*  Diagonal, slope +, size 5 */
        sel = Sel.Create(5, 5, "sel_5dp");
        sel.SetOrigin(2, 2);
        sel.SetElement(0, 4, 1);
        sel.SetElement(1, 3, 1);
        sel.SetElement(2, 2, 1);
        sel.SetElement(3, 1, 1);
        sel.SetElement(4, 0, 1);
        sela.AddSel(sel, "sel_5dp", 0);

            /*  Diagonal, slope -, size 5 */
        sel = Sel.Create(5, 5, "sel_5dm");
        sel.SetOrigin(2, 2);
        sel.SetElement(0, 0, 1);
        sel.SetElement(1, 1, 1);
        sel.SetElement(2, 2, 1);
        sel.SetElement(3, 3, 1);
        sel.SetElement(4, 4, 1);
        sela.AddSel(sel, "sel_5dm", 0);

        return sela;
    }

    /* ------------------------------------------------------------------- *
     *                 Simple hit-miss structuring elements                *
     * ------------------------------------------------------------------- */
    // leptonica: selaAddHitMiss (sel2.c)
    /// <param name="sela">[optional]; if null, a new sela is made</param>
    /// <returns>sela with additional sels</returns>
    public static Sela AddHitMiss(Sela? sela)
    {
        Sel sel;

        sela ??= Create(0);

            /*--------------------------------------------------------------*
             *                   Isolated foreground pixel                  *
             *--------------------------------------------------------------*/
        sel = Sel.CreateBrick(3, 3, 1, 1, L.SEL_MISS);
        sel.SetElement(1, 1, L.SEL_HIT);
        sela.AddSel(sel, "sel_3hm", 0);

            /*--------------------------------------------------------------*
             *                Horizontal and vertical edges                 *
             *--------------------------------------------------------------*/
        sel = Sel.CreateBrick(2, 3, 0, 1, L.SEL_HIT);
        sel.SetElement(1, 0, L.SEL_MISS);
        sel.SetElement(1, 1, L.SEL_MISS);
        sel.SetElement(1, 2, L.SEL_MISS);
        sela.AddSel(sel, "sel_3de", 0);

        sel = Sel.CreateBrick(2, 3, 1, 1, L.SEL_HIT);
        sel.SetElement(0, 0, L.SEL_MISS);
        sel.SetElement(0, 1, L.SEL_MISS);
        sel.SetElement(0, 2, L.SEL_MISS);
        sela.AddSel(sel, "sel_3ue", 0);

        sel = Sel.CreateBrick(3, 2, 1, 0, L.SEL_HIT);
        sel.SetElement(0, 1, L.SEL_MISS);
        sel.SetElement(1, 1, L.SEL_MISS);
        sel.SetElement(2, 1, L.SEL_MISS);
        sela.AddSel(sel, "sel_3re", 0);

        sel = Sel.CreateBrick(3, 2, 1, 1, L.SEL_HIT);
        sel.SetElement(0, 0, L.SEL_MISS);
        sel.SetElement(1, 0, L.SEL_MISS);
        sel.SetElement(2, 0, L.SEL_MISS);
        sela.AddSel(sel, "sel_3le", 0);

            /*--------------------------------------------------------------*
             *                        Slanted edge                          *
             *--------------------------------------------------------------*/
        sel = Sel.CreateBrick(13, 6, 6, 2, L.SEL_DONT_CARE);
        sel.SetElement(0, 3, L.SEL_MISS);
        sel.SetElement(0, 5, L.SEL_HIT);
        sel.SetElement(4, 2, L.SEL_MISS);
        sel.SetElement(4, 4, L.SEL_HIT);
        sel.SetElement(8, 1, L.SEL_MISS);
        sel.SetElement(8, 3, L.SEL_HIT);
        sel.SetElement(12, 0, L.SEL_MISS);
        sel.SetElement(12, 2, L.SEL_HIT);
        sela.AddSel(sel, "sel_sl1", 0);

            /*--------------------------------------------------------------*
             *                           Corners                            *
             *  This allows for up to 3 missing edge pixels at the corner   *
             *--------------------------------------------------------------*/
        sel = Sel.CreateBrick(4, 4, 1, 1, L.SEL_MISS);
        sel.SetElement(1, 1, L.SEL_DONT_CARE);
        sel.SetElement(1, 2, L.SEL_DONT_CARE);
        sel.SetElement(2, 1, L.SEL_DONT_CARE);
        sel.SetElement(1, 3, L.SEL_HIT);
        sel.SetElement(2, 2, L.SEL_HIT);
        sel.SetElement(2, 3, L.SEL_HIT);
        sel.SetElement(3, 1, L.SEL_HIT);
        sel.SetElement(3, 2, L.SEL_HIT);
        sel.SetElement(3, 3, L.SEL_HIT);
        sela.AddSel(sel, "sel_ulc", 0);

        sel = Sel.CreateBrick(4, 4, 1, 2, L.SEL_MISS);
        sel.SetElement(1, 1, L.SEL_DONT_CARE);
        sel.SetElement(1, 2, L.SEL_DONT_CARE);
        sel.SetElement(2, 2, L.SEL_DONT_CARE);
        sel.SetElement(1, 0, L.SEL_HIT);
        sel.SetElement(2, 0, L.SEL_HIT);
        sel.SetElement(2, 1, L.SEL_HIT);
        sel.SetElement(3, 0, L.SEL_HIT);
        sel.SetElement(3, 1, L.SEL_HIT);
        sel.SetElement(3, 2, L.SEL_HIT);
        sela.AddSel(sel, "sel_urc", 0);

        sel = Sel.CreateBrick(4, 4, 2, 1, L.SEL_MISS);
        sel.SetElement(1, 1, L.SEL_DONT_CARE);
        sel.SetElement(2, 1, L.SEL_DONT_CARE);
        sel.SetElement(2, 2, L.SEL_DONT_CARE);
        sel.SetElement(0, 1, L.SEL_HIT);
        sel.SetElement(0, 2, L.SEL_HIT);
        sel.SetElement(0, 3, L.SEL_HIT);
        sel.SetElement(1, 2, L.SEL_HIT);
        sel.SetElement(1, 3, L.SEL_HIT);
        sel.SetElement(2, 3, L.SEL_HIT);
        sela.AddSel(sel, "sel_llc", 0);

        sel = Sel.CreateBrick(4, 4, 2, 2, L.SEL_MISS);
        sel.SetElement(1, 2, L.SEL_DONT_CARE);
        sel.SetElement(2, 1, L.SEL_DONT_CARE);
        sel.SetElement(2, 2, L.SEL_DONT_CARE);
        sel.SetElement(0, 0, L.SEL_HIT);
        sel.SetElement(0, 1, L.SEL_HIT);
        sel.SetElement(0, 2, L.SEL_HIT);
        sel.SetElement(1, 0, L.SEL_HIT);
        sel.SetElement(1, 1, L.SEL_HIT);
        sel.SetElement(2, 0, L.SEL_HIT);
        sela.AddSel(sel, "sel_lrc", 0);

        return sela;
    }

    /* ------------------------------------------------------------------- *
     *        Structuring elements for comparing with DWA operations       *
     * ------------------------------------------------------------------- */
    // leptonica: selaAddDwaLinear (sel2.c)
    /// <param name="sela">[optional]; if null, a new sela is made</param>
    /// <returns>sela with additional sels</returns>
    /// <remarks>
    /// (1) Adds all linear (horizontal, vertical) sels from
    ///     2 to 63 pixels in length, which are the sizes over
    ///     which dwa code can be generated.
    /// </remarks>
    public static Sela AddDwaLinear(Sela? sela)
    {
        int i;
        Sel sel;
        string name;

        sela ??= Create(0);

        for (i = 2; i < 64; i++) {
            sel = Sel.CreateBrick(1, i, 0, i / 2, L.SEL_HIT);
            name = $"sel_{i}h";
            sela.AddSel(sel, name, 0);
        }
        for (i = 2; i < 64; i++) {
            sel = Sel.CreateBrick(i, 1, i / 2, 0, L.SEL_HIT);
            name = $"sel_{i}v";
            sela.AddSel(sel, name, 0);
        }
        return sela;
    }

    // leptonica: selaAddDwaCombs (sel2.c)
    /// <param name="sela">[optional]; if null, a new sela is made</param>
    /// <returns>sela with additional sels</returns>
    /// <remarks>
    /// (1) Adds all comb (horizontal, vertical) Sels that are
    ///     used in composite linear morphological operations
    ///     up to 63 pixels in length, which are the sizes over
    ///     which dwa code can be generated.
    /// </remarks>
    public static Sela AddDwaCombs(Sela? sela)
    {
        int i, f1, f2, prevsize, size;
        Sel? selh, selv;
        string name;

        sela ??= Create(0);

        prevsize = 0;
        for (i = 4; i < 64; i++) {
            Pix.SelectComposableSizes(i, out f1, out f2);
            size = f1 * f2;
            if (size == prevsize)
                continue;
            Pix.SelectComposableSels(i, Sel.L_HORIZ, out _, out selh);
            if (selh != null) {
                name = $"sel_comb_{size}h";
                sela.AddSel(selh, name, 0);
            } else {
                Log.Error("selaAddDwaCombs", $"selh not made for i = {i}");
            }
            Pix.SelectComposableSels(i, Sel.L_VERT, out _, out selv);
            if (selv != null) {
                name = $"sel_comb_{size}v";
                sela.AddSel(selv, name, 0);
            } else {
                Log.Error("selaAddDwaCombs", $"selv not made for i = {i}");
            }
            prevsize = size;
        }

        return sela;
    }

    /* ------------------------------------------------------------------- *
     *          Structuring elements for the intersection of lines         *
     * ------------------------------------------------------------------- */
    // leptonica: selaAddCrossJunctions (sel2.c)
    /// <param name="sela">[optional]; if null, a new sela is made</param>
    /// <param name="hlsize">length of each line of hits from origin</param>
    /// <param name="mdist">distance of misses from the origin</param>
    /// <param name="norient">number of orientations; max of 8</param>
    /// <param name="debugflag">1 for debug output</param>
    /// <returns>sela with additional sels</returns>
    /// <remarks>
    /// (1) Adds hitmiss Sels for the intersection of two lines.
    ///     If the lines are very thin, they must be nearly orthogonal
    ///     to register.
    /// (2) The number of Sels generated is equal to %norient.
    /// (3) If %norient == 2, this generates 2 Sels of crosses, each with
    ///     two perpendicular lines of hits.  One Sel has horizontal and
    ///     vertical hits; the other has hits along lines at +-45 degrees.
    ///     Likewise, if %norient == 3, this generates 3 Sels of crosses
    ///     oriented at 30 degrees with each other.
    /// (4) It is suggested that %hlsize be chosen at least 1 greater
    ///     than %mdist.  Try values of (%hlsize, %mdist) such as
    ///     (6,5), (7,6), (8,7), (9,7), etc.
    /// </remarks>
    public static Sela AddCrossJunctions(Sela? sela, float hlsize, float mdist,
                                         int norient, int debugflag)
    {
        const string func = "selaAddCrossJunctions";
        int i, j, w, xc, yc;
        double pi, halfpi, radincr, radang;
        double angle;
        Pix pixc, pixm, pixt;
        Pixa pixa;
        Pta pta1, pta2, pta3, pta4;
        Sel sel;
        string name;

        if (hlsize <= 0)
            throw Lept.Arg(func, "hlsize not > 0");
        if (norient < 1 || norient > 8)
            throw Lept.Arg(func, "norient not in [1, ... 8]");

        sela ??= Create(0);

        pi = 3.1415926535;
        halfpi = 3.1415926535 / 2.0;
        radincr = halfpi / (double)norient;
        w = (int)(2.2 * (Math.Max(hlsize, mdist) + 0.5));
        if (w % 2 == 0)
            w++;
        xc = w / 2;
        yc = w / 2;

        pixa = Pixa.Create(norient);
        for (i = 0; i < norient; i++) {

                /* Set the don't cares */
            pixc = Pix.Create(w, w, 32);
            pixc.SetAll();

                /* Add the green lines of hits */
            pixm = Pix.Create(w, w, 1);
            radang = (double)(float)i * radincr;
            pta1 = Pix.GeneratePtaLineFromPt(xc, yc, hlsize + 1, radang);
            pta2 = Pix.GeneratePtaLineFromPt(xc, yc, hlsize + 1, radang + halfpi);
            pta3 = Pix.GeneratePtaLineFromPt(xc, yc, hlsize + 1, radang + pi);
            pta4 = Pix.GeneratePtaLineFromPt(xc, yc, hlsize + 1, radang + pi + halfpi);
            pta1.Join(pta2, 0, -1);
            pta1.Join(pta3, 0, -1);
            pta1.Join(pta4, 0, -1);
            pixm.RenderPta(pta1, L.L_SET_PIXELS);
            pixc.PaintThroughMask(pixm, 0, 0, 0x00ff0000);

                /* Add red misses between the lines */
            for (j = 0; j < 4; j++) {
                angle = radang + (j - 0.5) * halfpi;
                pixc.SetPixel(xc + (int)(mdist * Math.Cos(angle)),
                              yc + (int)(mdist * Math.Sin(angle)), 0xff000000);
            }

                /* Add dark green for origin */
            pixc.SetPixel(xc, yc, 0x00550000);

                /* Generate the sel */
            sel = Sel.CreateFromColorPix(pixc, null);
            name = $"sel_cross_{i}";
            sela.AddSel(sel, name, 0);

            if (debugflag != 0) {
                pixt = pixc.ScaleBySampling(10.0f, 10.0f);
                pixa.AddPix(pixt, L.L_INSERT);
            }
        }

        if (debugflag != 0) {
            Utils.LeptMkdir("lept/sel");
            pixa.GetPixDimensions(0, out w, out _, out _);
            pixt = pixa.DisplayTiledAndScaled(32, w, 1, 0, 10, 2);
            pixt.WriteDebug("/tmp/lept/sel/xsel1.png", L.IFF_PNG);
            pixt.Display(0, 100);
            pixt = sela.DisplayInPix(15, 2, 20, 1);
            pixt.WriteDebug("/tmp/lept/sel/xsel2.png", L.IFF_PNG);
            pixt.Display(500, 100);
            sela.WriteStream(Console.OpenStandardError());
        }

        return sela;
    }

    // leptonica: selaAddTJunctions (sel2.c)
    /// <param name="sela">[optional]; if null, a new sela is made</param>
    /// <param name="hlsize">length of each line of hits from origin</param>
    /// <param name="mdist">distance of misses from the origin</param>
    /// <param name="norient">number of orientations; max of 8</param>
    /// <param name="debugflag">1 for debug output</param>
    /// <returns>sela with additional sels</returns>
    /// <remarks>
    /// (1) Adds hitmiss Sels for the T-junction of two lines.
    ///     If the lines are very thin, they must be nearly orthogonal
    ///     to register.
    /// (2) The number of Sels generated is 4 * %norient.
    /// (3) It is suggested that %hlsize be chosen at least 1 greater
    ///     than %mdist.  Try values of (%hlsize, %mdist) such as
    ///     (6,5), (7,6), (8,7), (9,7), etc.
    /// </remarks>
    public static Sela AddTJunctions(Sela? sela, float hlsize, float mdist,
                                     int norient, int debugflag)
    {
        const string func = "selaAddTJunctions";
        int i, j, k, w, xc, yc;
        double pi, halfpi, radincr, jang, radang;
        double[] angle = new double[3], dist = new double[3];
        Pix pixc, pixm, pixt;
        Pixa pixa;
        Pta pta1, pta2, pta3;
        Sel sel;
        string name;

        if (hlsize <= 2)
            throw Lept.Arg(func, "hlsizel not > 1");
        if (norient < 1 || norient > 8)
            throw Lept.Arg(func, "norient not in [1, ... 8]");

        sela ??= Create(0);

        pi = 3.1415926535;
        halfpi = 3.1415926535 / 2.0;
        radincr = halfpi / (float)norient;
        w = (int)(2.4 * (Math.Max(hlsize, mdist) + 0.5));
        if (w % 2 == 0)
            w++;
        xc = w / 2;
        yc = w / 2;

        pixa = Pixa.Create(4 * norient);
        for (i = 0; i < norient; i++) {
            for (j = 0; j < 4; j++) {  /* 4 orthogonal orientations */
                jang = (double)(float)j * halfpi;

                    /* Set the don't cares */
                pixc = Pix.Create(w, w, 32);
                pixc.SetAll();

                    /* Add the green lines of hits */
                pixm = Pix.Create(w, w, 1);
                radang = (double)(float)i * radincr;
                pta1 = Pix.GeneratePtaLineFromPt(xc, yc, hlsize + 1, jang + radang);
                pta2 = Pix.GeneratePtaLineFromPt(xc, yc, hlsize + 1,
                                                 jang + radang + halfpi);
                pta3 = Pix.GeneratePtaLineFromPt(xc, yc, hlsize + 1,
                                                 jang + radang + pi);
                pta1.Join(pta2, 0, -1);
                pta1.Join(pta3, 0, -1);
                pixm.RenderPta(pta1, L.L_SET_PIXELS);
                pixc.PaintThroughMask(pixm, 0, 0, 0x00ff0000);

                    /* Add red misses between the lines */
                angle[0] = radang + jang - halfpi;
                angle[1] = radang + jang + 0.5 * halfpi;
                angle[2] = radang + jang + 1.5 * halfpi;
                dist[0] = 0.8 * mdist;
                dist[1] = dist[2] = mdist;
                for (k = 0; k < 3; k++) {
                    pixc.SetPixel(xc + (int)(dist[k] * Math.Cos(angle[k])),
                                  yc + (int)(dist[k] * Math.Sin(angle[k])),
                                  0xff000000);
                }

                    /* Add dark green for origin */
                pixc.SetPixel(xc, yc, 0x00550000);

                    /* Generate the sel */
                sel = Sel.CreateFromColorPix(pixc, null);
                name = $"sel_cross_{4 * i + j}";
                sela.AddSel(sel, name, 0);

                if (debugflag != 0) {
                    pixt = pixc.ScaleBySampling(10.0f, 10.0f);
                    pixa.AddPix(pixt, L.L_INSERT);
                }
            }
        }

        if (debugflag != 0) {
            Utils.LeptMkdir("lept/sel");
            pixa.GetPixDimensions(0, out w, out _, out _);
            pixt = pixa.DisplayTiledAndScaled(32, w, 4, 0, 10, 2);
            pixt.WriteDebug("/tmp/lept/sel/tsel1.png", L.IFF_PNG);
            pixt.Display(0, 100);
            pixt = sela.DisplayInPix(15, 2, 20, 4);
            pixt.WriteDebug("/tmp/lept/sel/tsel2.png", L.IFF_PNG);
            pixt.Display(500, 100);
            sela.WriteStream(Console.OpenStandardError());
        }

        return sela;
    }

    /* -------------------------------------------------------------------------- *
     *    Structuring elements for connectivity-preserving thinning operations    *
     * -------------------------------------------------------------------------- */

        /* ------------------------------------------------------------
         * These sels (and their rotated counterparts) are the useful
         * 3x3 Sels for thinning.   The notation is based on
         * "Connectivity-preserving morphological image transformations,"
         * a version of which can be found at
         *           http://www.leptonica.com/papers/conn.pdf
         * ------------------------------------------------------------ */

        /* Sels for 4-connected thinning */
    private const string sel_4_1 = "  x" +
                                   "oCx" +
                                   "  x";
    private const string sel_4_2 = "  x" +
                                   "oCx" +
                                   " o ";
    private const string sel_4_3 = " o " +
                                   "oCx" +
                                   "  x";
    private const string sel_4_4 = " o " +
                                   "oCx" +
                                   " o ";
    private const string sel_4_5 = " ox" +
                                   "oCx" +
                                   " o ";
    private const string sel_4_6 = " o " +
                                   "oCx" +
                                   " ox";
    private const string sel_4_7 = " xx" +
                                   "oCx" +
                                   " o ";
    private const string sel_4_8 = "  x" +
                                   "oCx" +
                                   "o x";
    private const string sel_4_9 = "o x" +
                                   "oCx" +
                                   "  x";

        /* Sels for 8-connected thinning */
    private const string sel_8_1 = " x " +
                                   "oCx" +
                                   " x ";
    private const string sel_8_2 = " x " +
                                   "oCx" +
                                   "o  ";
    private const string sel_8_3 = "o  " +
                                   "oCx" +
                                   " x ";
    private const string sel_8_4 = "o  " +
                                   "oCx" +
                                   "o  ";
    private const string sel_8_5 = "o x" +
                                   "oCx" +
                                   "o  ";
    private const string sel_8_6 = "o  " +
                                   "oCx" +
                                   "o x";
    private const string sel_8_7 = " x " +
                                   "oCx" +
                                   "oo ";
    private const string sel_8_8 = " x " +
                                   "oCx" +
                                   "ox ";
    private const string sel_8_9 = "ox " +
                                   "oCx" +
                                   " x ";

        /* Sels for both 4 and 8-connected thinning */
    private const string sel_48_1 = " xx" +
                                    "oCx" +
                                    "oo ";
    private const string sel_48_2 = "o x" +
                                    "oCx" +
                                    "o x";

    // leptonica: sela4ccThin (sel2.c)
    /// <param name="sela">[optional]; if null, a new sela is made</param>
    /// <returns>sela with additional sels</returns>
    /// <remarks>
    /// (1) Adds the 9 basic sels for 4-cc thinning.
    /// </remarks>
    public static Sela Thin4cc(Sela? sela)
    {
        Sel sel;

        sela ??= Create(9);

        sel = Sel.CreateFromString(sel_4_1, 3, 3, "sel_4_1");
        sela.AddSel(sel, null, 0);
        sel = Sel.CreateFromString(sel_4_2, 3, 3, "sel_4_2");
        sela.AddSel(sel, null, 0);
        sel = Sel.CreateFromString(sel_4_3, 3, 3, "sel_4_3");
        sela.AddSel(sel, null, 0);
        sel = Sel.CreateFromString(sel_4_4, 3, 3, "sel_4_4");
        sela.AddSel(sel, null, 0);
        sel = Sel.CreateFromString(sel_4_5, 3, 3, "sel_4_5");
        sela.AddSel(sel, null, 0);
        sel = Sel.CreateFromString(sel_4_6, 3, 3, "sel_4_6");
        sela.AddSel(sel, null, 0);
        sel = Sel.CreateFromString(sel_4_7, 3, 3, "sel_4_7");
        sela.AddSel(sel, null, 0);
        sel = Sel.CreateFromString(sel_4_8, 3, 3, "sel_4_8");
        sela.AddSel(sel, null, 0);
        sel = Sel.CreateFromString(sel_4_9, 3, 3, "sel_4_9");
        sela.AddSel(sel, null, 0);

        return sela;
    }

    // leptonica: sela8ccThin (sel2.c)
    /// <param name="sela">[optional]; if null, a new sela is made</param>
    /// <returns>sela with additional sels</returns>
    /// <remarks>
    /// (1) Adds the 9 basic sels for 8-cc thinning.
    /// </remarks>
    public static Sela Thin8cc(Sela? sela)
    {
        Sel sel;

        sela ??= Create(9);

        sel = Sel.CreateFromString(sel_8_1, 3, 3, "sel_8_1");
        sela.AddSel(sel, null, 0);
        sel = Sel.CreateFromString(sel_8_2, 3, 3, "sel_8_2");
        sela.AddSel(sel, null, 0);
        sel = Sel.CreateFromString(sel_8_3, 3, 3, "sel_8_3");
        sela.AddSel(sel, null, 0);
        sel = Sel.CreateFromString(sel_8_4, 3, 3, "sel_8_4");
        sela.AddSel(sel, null, 0);
        sel = Sel.CreateFromString(sel_8_5, 3, 3, "sel_8_5");
        sela.AddSel(sel, null, 0);
        sel = Sel.CreateFromString(sel_8_6, 3, 3, "sel_8_6");
        sela.AddSel(sel, null, 0);
        sel = Sel.CreateFromString(sel_8_7, 3, 3, "sel_8_7");
        sela.AddSel(sel, null, 0);
        sel = Sel.CreateFromString(sel_8_8, 3, 3, "sel_8_8");
        sela.AddSel(sel, null, 0);
        sel = Sel.CreateFromString(sel_8_9, 3, 3, "sel_8_9");
        sela.AddSel(sel, null, 0);

        return sela;
    }

    // leptonica: sela4and8ccThin (sel2.c)
    /// <param name="sela">[optional]; if null, a new sela is made</param>
    /// <returns>sela with additional sels</returns>
    /// <remarks>
    /// (1) Adds the 2 basic sels for either 4-cc or 8-cc thinning.
    /// </remarks>
    public static Sela Thin4and8cc(Sela? sela)
    {
        Sel sel;

        sela ??= Create(2);

        sel = Sel.CreateFromString(sel_48_1, 3, 3, "sel_48_1");
        sela.AddSel(sel, null, 0);
        sel = Sel.CreateFromString(sel_48_2, 3, 3, "sel_48_2");
        sela.AddSel(sel, null, 0);

        return sela;
    }
}

public sealed partial class Sel
{
    /* -------------------------------------------------------------------------- *
     *                        Other structuring elements                          *
     * -------------------------------------------------------------------------- */
    // leptonica: selMakePlusSign (sel2.c)
    /// <param name="size">side of containing square</param>
    /// <param name="linewidth">of lines</param>
    /// <remarks>
    /// (1) Useful for debugging to show location of selected pixels.
    /// (2) See displaySelectedPixels() for an example of use.
    /// </remarks>
    public static Sel MakePlusSign(int size, int linewidth)
    {
        if (size < 3 || linewidth > size)
            throw Lept.Arg("selMakePlusSign", "invalid input");

        Pix pix = Pix.Create(size, size, 1);
        pix.RenderLine(size / 2, 0, size / 2, size - 1,
                       linewidth, L.L_SET_PIXELS);
        pix.RenderLine(0, size / 2, size, size / 2,
                       linewidth, L.L_SET_PIXELS);
        return CreateFromPix(pix, size / 2, size / 2, "plus_sign");
    }
}
