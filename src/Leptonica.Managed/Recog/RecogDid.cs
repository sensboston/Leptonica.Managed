using System;
using System.Globalization;

namespace Leptonica;

/*
 *  recogdid.c: document image decoding (DID / Viterbi) character splitter.
 */

public sealed partial class Recog
{
    private const float SetwidthFraction = 0.95f;
    private const int DidMaxYShift = 1;

    private static readonly float[] DefaultAlpha2 = { 0.95f, 0.9f };
    private static readonly float[] DefaultAlpha4 = { 0.95f, 0.9f, 0.75f, 0.25f };

    /*------------------------------------------------------------------------*
     *                       Top-level identification                         *
     *------------------------------------------------------------------------*/
    // leptonica: recogDecode (recogdid.c)
    /// <summary>Segments an image of touching characters into individual characters using document image decoding.</summary>
    /// <param name="pixs">A 1 bpp image, typically of multiple touching characters.</param>
    /// <param name="nlevels">Number of template levels; must be 2 for now.</param>
    /// <returns>The boxes that segment <paramref name="pixs"/> into characters.</returns>
    /// <exception cref="ArgumentException"><paramref name="pixs"/> is <c>null</c> or not 1 bpp, or <paramref name="nlevels"/> is not 2.</exception>
    /// <exception cref="LeptonicaException">Training is not finished or decoding fails.</exception>
    /// <remarks>
    /// <para>The input image must have been filtered so that it is likely to be composed of more than one touching character.
    /// Specifically, its height can only slightly exceed that of the tallest unscaled template, its width is somewhat larger
    /// than the width of the widest unscaled template, and the width-to-height aspect ratio is bounded by the maximum
    /// splitting ratio.</para>
    /// <para>This uses the decoding mechanism with labeled templates to segment the input image.
    /// The resulting segmentation is the returned set of boxes.</para>
    /// <para>In the overload that returns a debug image, the Viterbi path is rescored based on all the templates.
    /// Otherwise, the same procedure is carried out by <c>IdentifyPix</c> on the result of the segmentation.</para>
    /// </remarks>
    public Boxa? Decode(Pix pixs, int nlevels)
    {
        return DecodeCore(pixs, nlevels, false, out _);
    }

    /// <summary>Segments an image of touching characters into individual characters, and also returns a debug image.</summary>
    /// <param name="pixs">A 1 bpp image, typically of multiple touching characters.</param>
    /// <param name="nlevels">Number of template levels; must be 2 for now.</param>
    /// <param name="ppixdb">Receives the debug result image.</param>
    /// <returns>The boxes that segment <paramref name="pixs"/> into characters.</returns>
    /// <exception cref="ArgumentException"><paramref name="pixs"/> is <c>null</c> or not 1 bpp, or <paramref name="nlevels"/> is not 2.</exception>
    /// <exception cref="LeptonicaException">Training is not finished or decoding fails.</exception>
    public Boxa? Decode(Pix pixs, int nlevels, out Pix? ppixdb)
    {
        return DecodeCore(pixs, nlevels, true, out ppixdb);
    }

    private Boxa? DecodeCore(Pix pixs, int nlevels, bool wantdb, out Pix? ppixdb)
    {
        const string func = "recogDecode";
        ppixdb = null;
        if (pixs == null || pixs.Depth != 1)
            throw Lept.Arg(func, "pixs undefined or not 1 bpp");
        if (!train_done)
            throw Lept.Fail(func, "training not finished");
        if (nlevels != 2)
            throw Lept.Arg(func, "nlevels != 2 (for now)");

        int debug = wantdb ? 1 : 0;
        if (PrepareForDecoding(pixs, debug) != 0)
            throw Lept.Fail(func, "error making arrays");
        SetChannelParams(nlevels);

            /* Normal path; just run Viterbi */
        if (!wantdb) {
            if (RunViterbi(false, out _) == 0)
                return did!.boxa.Copy(L.L_COPY);
            throw Lept.Fail(func, "error in Viterbi");
        }

            /* Debug path */
        if (RunViterbi(true, out Pix? pix1) != 0)
            throw Lept.Fail(func, "error in viterbi");
        Pixa pixaOut = Pixa.Create(2);
        pixaOut.AddPix(pix1!, L.L_INSERT);
        if (RescoreDidResult(true, out Pix? pixRescored) != 0)
            throw Lept.Fail(func, "error in rescoring");
        pixaOut.AddPix(pixRescored!, L.L_INSERT);
        ppixdb = pixaOut.DisplayTiledInRows(32, 2 * pixRescored!.Width + 100,
                                            1.0f, 0, 30, 2);
        return did!.boxa.Copy(L.L_COPY);
    }

    /*------------------------------------------------------------------------*
     *                       Generate decoding arrays                         *
     *------------------------------------------------------------------------*/
    // leptonica: recogPrepareForDecoding (recogdid.c)
    private int PrepareForDecoding(Pix pixs, int debug)
    {
        const string func = "recogPrepareForDecoding";
        if (pixs == null || pixs.Depth != 1) {
            Log.Error(func, "pixs not defined or not 1 bpp");
            return 1;
        }
        if (!train_done) {
            Log.Error(func, "training not finished");
            return 1;
        }

        if (!ave_done) {
            int ret = AverageSamples(0);
            // NOTE(upstream): inverted check (!ret) fails on the first call when ave_done == 0.
            if (ret == 0) {
                Log.Error(func, "averaging of samples failed");
                return 1;
            }
        }

            /* Binarize and crop to foreground if necessary */
        Pix? pix1 = ProcessToIdentify(pixs, 0);
        if (pix1 == null) {
            Log.Error(func, "pix1 not made");
            return 1;
        }

            /* Remove any existing RecogDID and set up a new one */
        DestroyDid();
        if (CreateDid(pix1) != 0) {
            Log.Error(func, "decoder not made");
            return 1;
        }

            /* Compute vertical sum and first moment arrays */
        Rdid curDid = GetDid();
        curDid.nasum = pix1.CountPixelsByColumn();
        curDid.namoment = pix1.GetMomentByColumn(1);

            /* Generate the arrays */
        for (int i = 0; i < curDid.narray; i++)
            MakeDecodingArray(i, debug);

        return 0;
    }

    // leptonica: recogMakeDecodingArray (recogdid.c)
    private int MakeDecodingArray(int index, int debug)
    {
        const string func = "recogMakeDecodingArray";
        Rdid curDid = GetDid();
        if (index < 0 || index >= curDid.narray) {
            Log.Error(func, "invalid index");
            return 1;
        }

        Pix pix1 = curDid.pixs;
        pix1.GetDimensions(out int w1, out int h1, out _);
        Pix pix2 = pixa_u!.GetPix(index, L.L_CLONE);
        pix2.GetDimensions(out int w2, out int h2, out _);
        if (w1 < w2) {
            Log.Info(func, $"w1 = {w1} < w2 = {w2} for index {index}");
            return 0;
        }

        Numa nasumCol = curDid.nasum!;
        Numa namomentCol = curDid.namoment!;
        pta_u!.GetIPt(index, out _, out int ycent2);
        int[] counta = curDid.counta![index];
        int[] delya = curDid.delya![index];

        int nx = w1 - w2 + 1;
        int[] ycent1 = new int[nx];
        int[] arraysum = nasumCol.GetIArray();
        int[] arraymoment = namomentCol.GetIArray();
        int sum = 0, moment = 0;
        for (int i = 0; i < w2; i++) {
            sum += arraysum[i];
            moment += arraymoment[i];
        }
        for (int i = 0; i < nx - 1; i++) {
            ycent1[i] = (sum == 0) ? ycent2 : (int)((float)moment / (float)sum);
            sum += arraysum[w2 + i] - arraysum[i];
            moment += arraymoment[w2 + i] - arraymoment[i];
        }
        ycent1[nx - 1] = (sum == 0) ? ycent2 : (int)((float)moment / (float)sum);

        Pix pix3 = Pix.Create(w2, h1, 1);
        for (int i = 0; i < nx; i++) {
            int shifty = (int)(ycent1[i] - ycent2 + 0.5);
            int maxcount = 0;
            int maxdely = 0;
            for (int j = -DidMaxYShift; j <= DidMaxYShift; j++) {
                pix3.ClearAll();
                int dely = shifty + j;
                pix3.Rasterop(0, dely, w2, h2, L.PIX_SRC, pix2, 0, 0);
                pix3.Rasterop(0, 0, w2, h1, L.PIX_SRC & L.PIX_DST, pix1, i, 0);
                int count = pix3.CountPixels(sumtab);
                if (count > maxcount) {
                    maxcount = count;
                    maxdely = dely;
                }
            }
            counta[i] = maxcount;
            delya[i] = maxdely;
        }
        curDid.fullarrays = true;
        return 0;
    }

    /*------------------------------------------------------------------------*
     *                  Dynamic programming for best path                     *
     *------------------------------------------------------------------------*/
    // leptonica: recogRunViterbi (recogdid.c)
    private int RunViterbi(bool wantdb, out Pix? ppixdb)
    {
        const string func = "recogRunViterbi";
        ppixdb = null;
        Rdid curDid = GetDid();
        if (!curDid.fullarrays) {
            Log.Error(func, "did full arrays not made");
            return 1;
        }

        int w1 = curDid.size;
        int narray = curDid.narray;
        int spacetempl = narray;
        int[] setw = curDid.setwidth;
        int minsetw = 100000;
        for (int i = 0; i < narray; i++) {
            if (setw[i] < minsetw)
                minsetw = setw[i];
        }
        if (minsetw <= 2) {
            Log.Error(func, "minsetw <= 2; bad templates");
            return 1;
        }

        float[] didscore = curDid.trellisscore;
        int[] didtempl = curDid.trellistempl;
        int[] area2 = nasum_u!.GetIArray();
        int besttempl = 0;
        float maxscore = 0.0f;
        for (int x = minsetw; x < w1; x++) {
            bool first = true;
            for (int i = 0; i < narray; i++) {
                if (x - setw[i] < 0) continue;
                float matchscore = didscore[x - setw[i]] +
                                   curDid.gamma[1] * curDid.counta![i][x - setw[i]] +
                                   curDid.beta[1] * area2[i];
                if (first) {
                    maxscore = matchscore;
                    besttempl = i;
                    first = false;
                } else if (matchscore > maxscore) {
                    maxscore = matchscore;
                    besttempl = i;
                }
            }

            float prevscore = didscore[x - 1];
            if (prevscore > maxscore) {
                maxscore = prevscore;
                besttempl = spacetempl;
            }
            didscore[x] = maxscore;
            didtempl[x] = besttempl;
        }

        int xPos;
        for (xPos = w1 - 1; xPos >= 0; xPos--) {
            if (didtempl[xPos] != spacetempl) break;
        }
        int h1 = curDid.pixs.Height;
        while (xPos > 0) {
            if (didtempl[xPos] == spacetempl) {
                xPos--;
                continue;
            }
            int templ = didtempl[xPos];
            int xloc = xPos - setw[templ];
            if (xloc < 0) break;
            int counts = curDid.counta![templ][xloc];
            GetWindowedArea(templ, xloc, out int dely, out int area1);
            float correl = ((float)counts * counts) /
                           (float)(area2[templ] * area1);
            Pix pix1 = pixa_u!.GetPix(templ, L.L_CLONE);
            int w2 = pix1.Width;
            curDid.natempl.AddNumber(templ);
            curDid.naxloc.AddNumber(xloc);
            curDid.nadely.AddNumber(dely);
            curDid.nawidth.AddNumber(w2);
            curDid.nascore.AddNumber(correl);
            int xnz = Math.Max(xloc, 0);
            Box box = Box.Create(xnz, dely, w2, h1);
            curDid.boxa.AddBox(box, L.L_INSERT);
            xPos = xloc;
        }

        if (wantdb) {
            curDid.natempl.WriteStderr();
            curDid.naxloc.WriteStderr();
            curDid.nadely.WriteStderr();
            curDid.nawidth.WriteStderr();
            curDid.nascore.WriteStderr();
            curDid.boxa.WriteStderr();
            ppixdb = ShowPath(0);
        }

        return 0;
    }

    // leptonica: recogRescoreDidResult (recogdid.c)
    private int RescoreDidResult(bool wantdb, out Pix? ppixdb)
    {
        const string func = "recogRescoreDidResult";
        ppixdb = null;
        Rdid curDid = GetDid();
        if (!curDid.fullarrays) {
            Log.Error(func, "did full arrays not made");
            return 1;
        }
        int n = curDid.naxloc.Count;
        if (n == 0) {
            Log.Error(func, "no elements in path");
            return 1;
        }

        Pix pixs = curDid.pixs;
        for (int i = 0; i < n; i++) {
            Box box1 = curDid.boxa.GetBox(i, L.L_COPY);
            box1.GetGeometry(out int x, out int dely, out _, out _);
            Pix pix1 = pixs.ClipRectangle(box1, out _)!;
            if (!TryIdentifyPixCore(pix1, false, out _))
                SkipIdentify();
            TransferRchToDid(x, dely);
            if (wantdb && rch != null) {
                rch.Extract(out int index, out float score, out string? text,
                            out int sample, out _, out _, out _);
                Log.Info(func, $"text = {text}, index = {index}, sample = {sample}, score = {score:F3}");
            }
        }

        if (wantdb)
            ppixdb = ShowPath(1);

        return 0;
    }

    // leptonica: recogShowPath (recogdid.c)
    private Pix ShowPath(int select)
    {
        Rdid curDid = GetDid();
        L_Bmf bmf8 = L_Bmf.Create(null, 8);
        Pix pixScaled = curDid.pixs.Scale(4.0f, 4.0f);
        Pix pix0 = pixScaled.AddBorderGeneral(0, 0, 0, 40, 0);
        Pix pix1 = pix0.ConvertTo32();

        Numa natemplS, nascoreS, naxlocS, nadelyS;
        Numa? nasampleS = null;
        if (select == 0) {
            natemplS = curDid.natempl;
            nascoreS = curDid.nascore;
            naxlocS = curDid.naxloc;
            nadelyS = curDid.nadely;
        } else {
            natemplS = curDid.natempl_r;
            nasampleS = curDid.nasample_r;
            nascoreS = curDid.nascore_r;
            naxlocS = curDid.naxloc_r;
            nadelyS = curDid.nadely_r;
        }

        int n = natemplS.Count;
        for (int i = 0; i < n; i++) {
            int index = natemplS.GetIValue(i);
            Pix pix2;
            if (select == 0) {
                pix2 = pixa_u!.GetPix(index, L.L_CLONE);
            } else {
                int j = nasampleS!.GetIValue(i);
                pix2 = pixaa_u.GetPix(index, j, L.L_CLONE);
            }
            Pix pix3 = pix2.Scale(4.0f, 4.0f);
            Pix pix4 = Pix.ErodeBrick(null, pix3, 5, 5);
            Pix.Xor(pix4, pix4, pix3);
            float score = nascoreS.GetFValue(i);
            string textstr = string.Format(CultureInfo.InvariantCulture, "{0,5:F3}", score);
            Pix pix5 = pix4.AddTextlines(bmf8, textstr, 1, L.L_ADD_BELOW);
            int xloc = naxlocS.GetIValue(i);
            int dely = nadelyS.GetIValue(i);
            pix1.PaintThroughMask(pix5, 4 * xloc, 4 * dely, 0xff000000u);
        }
        return pix1;
    }

    /*------------------------------------------------------------------------*
     *                  Create/destroy temporary DID data                     *
     *------------------------------------------------------------------------*/
    // leptonica: recogCreateDid (recogdid.c)
    /// <summary>Creates the temporary decoding data for an image, replacing any existing data.</summary>
    /// <param name="pixs">The 1 bpp image to match.</param>
    /// <returns>0 on success.</returns>
    /// <exception cref="ArgumentException"><paramref name="pixs"/> is <c>null</c>.</exception>
    public int CreateDid(Pix pixs)
    {
        const string func = "recogCreateDid";
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");

        DestroyDid();

        Rdid newDid = new Rdid();
        did = newDid;
        newDid.pixs = Pix.Clone(pixs);
        newDid.narray = setsize;
        newDid.size = pixs.Width;
        newDid.natempl = Numa.Create(5);
        newDid.naxloc = Numa.Create(5);
        newDid.nadely = Numa.Create(5);
        newDid.nawidth = Numa.Create(5);
        newDid.boxa = Boxa.Create(5);
        newDid.nascore = Numa.Create(5);
        newDid.natempl_r = Numa.Create(5);
        newDid.nasample_r = Numa.Create(5);
        newDid.naxloc_r = Numa.Create(5);
        newDid.nadely_r = Numa.Create(5);
        newDid.nawidth_r = Numa.Create(5);
        newDid.nascore_r = Numa.Create(5);

        newDid.setwidth = new int[newDid.narray];
        newDid.counta = new int[newDid.narray][];
        newDid.delya = new int[newDid.narray][];
        newDid.beta = new float[5];
        newDid.gamma = new float[5];
        newDid.trellisscore = new float[newDid.size];
        newDid.trellistempl = new int[newDid.size];
        for (int i = 0; i < newDid.narray; i++) {
            newDid.counta[i] = new int[newDid.size];
            newDid.delya[i] = new int[newDid.size];
        }

        for (int i = 0; i < newDid.narray; i++) {
            Pix pix1 = pixa_u!.GetPix(i, L.L_CLONE);
            newDid.setwidth[i] = (int)(SetwidthFraction * pix1.Width);
        }

        return 0;
    }

    // leptonica: recogDestroyDid (recogdid.c)
    /// <summary>Discards the temporary decoding data.</summary>
    public void DestroyDid()
    {
        did = null;
    }

    /*------------------------------------------------------------------------*
     *                            Various helpers                             *
     *------------------------------------------------------------------------*/
    // leptonica: recogDidExists (recogdid.c)
    /// <summary>Gets a value indicating whether the temporary decoding data exist.</summary>
    public bool DidExists => did != null;

    // leptonica: recogGetDid (recogdid.c)
    /// <summary>Gets the temporary decoding data, after checking that they are complete.</summary>
    /// <returns>The decoding data.</returns>
    /// <exception cref="LeptonicaException">The decoding data or its arrays are not defined.</exception>
    public Rdid GetDid()
    {
        const string func = "recogGetDid";
        if (did == null)
            throw Lept.Fail(func, "did not defined");
        if (did.counta == null || did.delya == null)
            throw Lept.Fail(func, "did array ptrs not defined");
        for (int i = 0; i < did.narray; i++) {
            if (did.counta[i] == null || did.delya[i] == null)
                throw Lept.Fail(func, "did arrays not defined");
        }
        return did;
    }

    // leptonica: recogGetWindowedArea (recogdid.c)
    private void GetWindowedArea(int index, int x, out int pdely, out int pwsum)
    {
        const string func = "recogGetWindowedArea";
        pdely = 0;
        pwsum = 0;
        Rdid curDid = GetDid();
        if (index < 0 || index >= curDid.narray)
            throw Lept.Arg(func, "invalid index");
        Pix pix1 = curDid.pixs;
        pix1.GetDimensions(out int w1, out int h1, out _);
        if (x >= w1)
            throw Lept.Arg(func, "invalid x position");

        Pix pix2 = pixa_u!.GetPix(index, L.L_CLONE);
        pix2.GetDimensions(out int w2, out int h2, out _);
        if (w1 < w2) {
            Log.Info(func, $"template {index} too small");
            return;
        }

        pdely = curDid.delya![index][x];
        Pix pixt = Pix.Create(w2, h1, 1);
        pixt.Rasterop(0, pdely, w2, h2, L.PIX_SRC, pix2, 0, 0);
        pixt.Rasterop(0, 0, w2, h1, L.PIX_SRC & L.PIX_DST, pix1, x, 0);
        pwsum = pixt.CountPixels(sumtab);
    }

    // leptonica: recogSetChannelParams (recogdid.c)
    /// <summary>Sets the channel parameters used for decoding.</summary>
    /// <param name="nlevels">Number of template levels: 2 or 4.</param>
    /// <exception cref="ArgumentException"><paramref name="nlevels"/> is not 2 or 4.</exception>
    /// <remarks>
    /// This converts the independent bit-flip probabilities in the channel into log-likelihood coefficients on image sums.
    /// These coefficients are only defined for the non-background template levels. Thus for two levels (one foreground,
    /// one background) only the coefficients at index 1 are used. For four levels (three foreground templates) the
    /// coefficients at indices 1 to 3 are used.
    /// </remarks>
    public void SetChannelParams(int nlevels)
    {
        const string func = "recogSetChannelParams";
        Rdid curDid = GetDid();
        float[] da;
        if (nlevels == 2)
            da = DefaultAlpha2;
        else if (nlevels == 4)
            da = DefaultAlpha4;
        else
            throw Lept.Arg(func, "nlevels not 2 or 4");

        for (int i = 1; i < nlevels; i++) {
            curDid.beta[i] = (float)Math.Log((1.0 - da[i]) / da[0]);
            curDid.gamma[i] = (float)Math.Log(da[0] * da[i] / ((1.0 - da[0]) * (1.0 - da[i])));
        }
    }

    // leptonica: recogTransferRchToDid (recogdid.c)
    private void TransferRchToDid(int x, int y)
    {
        const string func = "recogTransferRchToDid";
        Rdid curDid = GetDid();
        if (rch == null)
            throw Lept.Fail(func, "rch not defined");

        curDid.natempl_r.AddNumber(rch.index);
        curDid.nasample_r.AddNumber(rch.sample);
        curDid.naxloc_r.AddNumber(rch.xloc + x);
        curDid.nadely_r.AddNumber(rch.yloc + y);
        curDid.nawidth_r.AddNumber(rch.width);
        curDid.nascore_r.AddNumber(rch.score);
    }
}
