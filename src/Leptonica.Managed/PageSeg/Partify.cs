using System;

namespace Leptonica;

/*
 *  partify.c: musical score stave-set location and part separation.
 */

/// <summary>Separates the parts (staves) of musical score pages into per-part documents.</summary>
public static class Partify
{
    /*---------------------------------------------------------------------*
     *                              Top level                              *
     *---------------------------------------------------------------------*/
    // leptonica: partifyFiles (partify.c)
    /// <summary>Reads the page images in a directory, separates them into parts and writes one pdf per part.</summary>
    /// <param name="dirname">Directory containing the page images.</param>
    /// <param name="substr">Required filename substring; <c>null</c> to use all files.</param>
    /// <param name="nparts">Number of parts to generate, counting from the top.</param>
    /// <param name="outroot">Root name of the output pdf files.</param>
    /// <param name="debugfile">Optional name of a debug pdf; <c>null</c> for no debug output.</param>
    /// <returns>0 on success.</returns>
    /// <exception cref="ArgumentException"><paramref name="dirname"/> is <c>null</c>, <paramref name="nparts"/> is out of range, or <paramref name="outroot"/> is empty.</exception>
    /// <remarks>
    /// <para>All page images are compressed in png format into a <see cref="Pixacomp"/>.</para>
    /// <para>Each page image is deskewed, binarized at 300 ppi, split into <paramref name="nparts"/> parts, and saved in a set of
    /// <see cref="Pixacomp"/> objects in tiff-g4 format.</para>
    /// <para>Each partified <see cref="Pixacomp"/> is then rendered into a set of page images and written as a pdf.</para>
    /// </remarks>
    public static int Files(string dirname, string? substr, int nparts,
                            string outroot, string? debugfile)
    {
        const string func = "partifyFiles";
        if (dirname == null)
            throw Lept.Arg(func, "dirname not defined");
        if (nparts < 0 || nparts > 10)
            throw Lept.Arg(func, "nparts not in [1 ... 10]");
        if (string.IsNullOrEmpty(outroot) || outroot[0] == '\n')
            throw Lept.Arg(func, "outroot undefined or empty");

        Pixa? pixadb = debugfile != null ? Pixa.Create(0) : null;
        Pixacomp pixac = Pixacomp.CreateFromFiles(dirname, substr, L.IFF_PNG);
        Pixac(pixac, nparts, outroot, pixadb);
        if (pixadb != null) {
            Log.Info(func, $"writing debug output to {debugfile}");
            pixadb.ConvertToPdf(300, 1.0f, L.L_FLATE_ENCODE, 0, "Partify Debug", debugfile!);
        }
        return 0;
    }

    // leptonica: partifyPixac (partify.c)
    /// <summary>Separates the pages of a compressed pixa into parts and writes one pdf per part.</summary>
    /// <param name="pixac">Compressed page images; must contain at least one image.</param>
    /// <param name="nparts">Number of parts to generate, counting from the top.</param>
    /// <param name="outroot">Root name of the output pdf files.</param>
    /// <param name="pixadb">Optional debug <see cref="Pixa"/> that receives intermediate images; <c>null</c> to skip.</param>
    /// <returns>0 on success.</returns>
    /// <exception cref="ArgumentException"><paramref name="pixac"/> is <c>null</c> or empty, <paramref name="nparts"/> is not in [1 ... 10], or <paramref name="outroot"/> is empty.</exception>
    /// <remarks>If the image files do not have a resolution, 300 ppi is assumed.</remarks>
    public static int Pixac(Pixacomp pixac, int nparts, string outroot, Pixa? pixadb)
    {
        const string func = "partifyPixac";
        if (pixac == null)
            throw Lept.Arg(func, "pixac not defined");
        int npage = pixac.Count;
        if (npage == 0)
            throw Lept.Arg(func, "pixac is empty");
        if (nparts < 1 || nparts > 10)
            throw Lept.Arg(func, "nparts not in [1 ... 10]");
        if (string.IsNullOrEmpty(outroot) || outroot[0] == '\n')
            throw Lept.Arg(func, "outroot undefined or empty");

        Pixacomp[] pixaca = new Pixacomp[nparts];
        for (int i = 0; i < nparts; i++)
            pixaca[i] = Pixacomp.Create(0);

        int line = 1;
        L_Bmf bmf = L_Bmf.Create(null, 10);
        for (int pageno = 0; pageno < npage; pageno++) {
            Pix pix1;
            try {
                pix1 = pixac.GetPix(pageno);
            } catch (Exception e) when (e is ArgumentException || e is LeptonicaException) {
                Log.Error(func, $"pix for page {pageno} not found");
                continue;
            }

            int res = pix1.XRes;
            Pix pix2;
            if (res == 0 || res == 300 || res > 600) {
                pix2 = Pix.Clone(pix1);
            } else {
                float factor = 300.0f / (float)res;
                if (factor > 3.0f)
                    Log.Warning(func, "resolution is very low");
                pix2 = pix1.Scale(factor, factor);
            }
            Pix pix3 = pix2.ConvertTo1Adaptive();
            Pix pix4 = pix3.Deskew(0);
            pix1 = Pix.Clone(pix4);

            Boxa boxa1 = LocateStaveSets(pix1, pageno, pixadb);
            int nbox = boxa1.Count;
            Console.Error.WriteLine($"number of boxes in page {pageno}: {nbox}");
            for (int i = 0; i < nbox; i++, line++) {
                string buf = $"{line}";
                Box box1 = boxa1.GetBox(i, L.L_COPY);
                pix2 = pix1.ClipRectangle(box1, out _)!;
                pix3 = pix2.MorphSequence("d1.20 + o50.1 + o1.30", 0);
                Boxa boxa2 = pix3.ConnCompBB(8);
                Boxa boxa3 = boxa2.Sort(L.L_SORT_BY_Y, L.L_SORT_INCREASING, out _);
                RemoveVGaps(boxa3);
                int icount = boxa3.Count;
                if (icount < nparts)
                    Log.Warning(func, $"nparts requested = {nparts}, but only found {icount}");
                for (int j = 0; j < icount && j < nparts; j++) {
                    Box box2 = boxa3.GetBox(j, L.L_COPY);
                    if (j == nparts - 1)
                        box2.SetSideLocations(-1, -1, -1, pix1.Height - 1);
                    pix4 = pix2.ClipRectangle(box2, out _)!;
                    Pix pix5 = pix4.AddTextlines(bmf, buf, 1, L.L_ADD_LEFT);
                    pixaca[j].AddPix(pix5, L.IFF_TIFF_G4);
                }
            }
        }

        for (int i = 0; i < nparts; i++) {
            string buf = $"{outroot}-{i}.pdf";
            Log.Info(func, $"writing part {i}: {buf}");
            pixaca[i].ConvertToPdf(300, 1.0f, L.L_G4_ENCODE, 0, null, buf);
        }
        return 0;
    }

    /*---------------------------------------------------------------------*
     *                               Helpers                               *
     *---------------------------------------------------------------------*/
    // leptonica: pixLocateStaveSets (partify.c)
    internal static Boxa LocateStaveSets(Pix pixs, int pageno, Pixa? pixadb)
    {
        const string func = "pixLocateStaveSets";
        if (pixs == null)
            throw Lept.Arg(func, "pixs not defined");

        Pix pix1 = pixs.MorphSequence("r11", 0);
        Boxa boxa1 = pix1.ConnCompBB(8);
        Boxa boxa2 = boxa1.SelectByArea(15000, L.L_SELECT_IF_GT, out _);
        Boxa boxa3 = boxa2.Sort(L.L_SORT_BY_Y, L.L_SORT_INCREASING, out _);
        if (pixadb != null) {
            Pix pix2 = pix1.ConvertTo32();
            pix2.RenderBoxaArb(boxa3, 2, 255, 0, 0);
            pixadb.AddPix(pix2, L.L_INSERT);
            pix2.Display(100 * pageno, 100);
        }

        RemoveVGaps(boxa3);
        if (pixadb != null) {
            Pix pix2 = pix1.ConvertTo32();
            pix2.RenderBoxaArb(boxa3, 2, 0, 255, 0);
            pixadb.AddPix(pix2, L.L_INSERT);
            pix2.Display(100 * pageno, 600);
        }
        return boxa3.Transform(0, 0, 4.0f, 4.0f);
    }

    // leptonica: boxaRemoveVGaps (partify.c)
    internal static int RemoveVGaps(Boxa boxa)
    {
        const string func = "boxaRemoveVGaps";
        if (boxa == null)
            throw Lept.Arg(func, "boxa not defined");
        int nbox = boxa.Count;
        if (nbox == 0)
            return 1;

        // NOTE(upstream): delta is uninitialized in C when nbox == 1; initialized to 0 here.
        int delta = 0;
        for (int i = 0; i < nbox - 1; i++) {
            boxa.GetBoxGeometry(i, out _, out int y1, out _, out int h1);
            boxa.GetBoxGeometry(i + 1, out _, out int y2, out _, out _);
            delta = (y2 - y1 - h1) / 2;
            boxa.AdjustBoxSides(i, 0, 0, 0, delta);
            boxa.AdjustBoxSides(i + 1, 0, 0, -delta, 0);
        }
        boxa.AdjustBoxSides(nbox - 1, 0, 0, 0, delta);
        return 0;
    }
}
