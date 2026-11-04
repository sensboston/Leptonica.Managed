using System;

namespace Leptonica;

/*
 *  checkerboard.c: Finds the corners where four squares of a checkerboard come together.
 *
 *    Find the checker corners where 4 squares come together
 *      Pix.FindCheckerboardCorners (pixFindCheckerboardCorners)
 *
 *    Generate the hit-miss sels
 *      static Sela MakeCheckerboardCornerSela (makeCheckerboardCornerSela)
 *      static Pixa MakeCheckerboardCornerPixa (makeCheckerboardCornerPixa)
 *
 *  The functions in this file locate the corners where four squares
 *  in a checkerboard come together.  With a perfectly aligned checkerboard,
 *  the solution is trivial: take the union of two hit-miss transforms (HMTs),
 *  each having a simple diagonal structuring element (sel).  The two
 *  sels can be generated from strings such as these, using
 *  selCreateFromString():
 *
 *   static const char *str1 = "o     x"
 *                             "       "
 *                             "       "
 *                             "   C   "
 *                             "       "
 *                             "       "
 *                             "x     o";
 *   static const char *str2 = "x     o"
 *                             "       "
 *                             "       "
 *                             "   C   "
 *                             "       "
 *                             "       "
 *                             "o     x";
 *
 *  A more interesting problem is to consider the checkerboard viewed from
 *  some arbitrary angle and orientation from the normal.  The method
 *  developed here works for a camera located within a cone with an opening
 *  half-angle of about 45 degrees, and with its axis along the normal
 *  to the checkerboard.
 *
 *  See prog/checkerboard_reg.c for usage.
 */

/// <summary>Constants from morph.h used by checkerboard.c.</summary>
public static partial class L
{
    /// <summary>Morphological operation type: hit-miss transform.</summary>
    public const int L_MORPH_HMT = 5;
}

public sealed partial class Pix
{
    private const string selnames = "s_diag1 s_diag2 s_cross1 s_cross2";

    // leptonica: pixFindCheckerboardCorners (checkerboard.c)
    /// <summary>Finds the corners where four squares of a checkerboard come together.</summary>
    /// <param name="size">size of HMT sel; &gt;= 7, typ. 15; 0 for default</param>
    /// <param name="dilation">size of hit and miss squares; typ. 1 or 3; max 5</param>
    /// <param name="nsels">number to use (either 2 or 4)</param>
    /// <param name="pix_corners">1 bpp pix giving corner locations</param>
    /// <param name="pta_corners">pta giving corner locations</param>
    /// <param name="pixadb">[optional] pass in pre-allocated</param>
    /// <remarks>
    /// (1) Use %nsels = 4 if the checkerboard may be rotated by more
    ///     than 20 deg.
    /// (2) The values of %size and %dilation that can be used depend on
    ///     the square sizes.  Nominal values here are for squares of
    ///     size 30 to 50.  In general, because of the viewing angle
    ///     of the camera, the "squares" will appear approximately
    ///     as a rotated rectangle.
    /// (3) The outputs pix_corners and pta_corners are optional.
    /// </remarks>
    public void FindCheckerboardCorners(int size, int dilation, int nsels,
                                        out Pix pix_corners, out Pta pta_corners,
                                        Pixa? pixadb)
    {
        const string func = "pixFindCheckerboardCorners";
        Pix pixs = this;
        if (size <= 0) size = 7;
        if (size < 7)
            throw Lept.Arg(func, "size too small");
        if (dilation < 1 || dilation > 5)
            throw Lept.Arg(func, "dilation not in [1 ...5]");
        if (nsels != 2 && nsels != 4)
            throw Lept.Arg(func, "nsels not 2 or 4");

            /* Generate the hit-miss sels for finding corners */
        Sela sela = MakeCheckerboardCornerSela(size, dilation, nsels, pixadb);
        Pix pix1;
        if (pixadb != null) {
            pix1 = sela.DisplayInPix(15, 3, 15, 2);
            pixadb.AddPix(pix1, L.L_INSERT);
        }

            /* Do the hit-miss transform to find corner locations */
        pix1 = pixs.UnionOfMorphOps(sela, L.L_MORPH_HMT);
        if (pixadb != null) pixadb.AddPix(pix1, L.L_CLONE);

            /* Remove large noise c.c. */
        Pix pix2 = pix1.SelectBySize(size, size, 8, L.L_SELECT_IF_BOTH,
                                     L.L_SELECT_IF_LTE, out _);
        if (pixadb != null) pixadb.AddPix(pix2, L.L_CLONE);

            /* Thin remaining c.c. */
        Pix pix3 = pix2.ThinConnected(L.L_THIN_FG, 8, 0);
        if (pixadb != null) pixadb.AddPix(pix3, L.L_CLONE);

            /* Extract the location of the center of each component */
        Boxa boxa1 = pix3.ConnCompBB(8);
        Pta pta1 = boxa1.ExtractCorners(L.L_BOX_CENTER);
        if (pixadb != null) {  /* show the result as colored plus signs on the input */
            Sel sel = Sel.MakePlusSign(15, 2);
            pix1 = DisplaySelectedPixels(pixs, pix3, sel, 0xff000000);
            pixadb.AddPix(pix1, L.L_INSERT);
        }

        pix_corners = pix3;
        pta_corners = pta1;
    }

    // leptonica: makeCheckerboardCornerSela (checkerboard.c)
    /// <summary>Makes the hit-miss sels for finding the corners.</summary>
    /// <param name="size">size of HMT sel; &gt;= 7, typ. 15; 0 for default</param>
    /// <param name="dilation">size of hit and miss squares; typ. 1 or 3; max 5</param>
    /// <param name="nsels">number to use (either 2 or 4)</param>
    /// <param name="pixadb">[optional] pass in pre-allocated</param>
    /// <returns>sela hit-miss sels for finding corners</returns>
    /// <remarks>(1) Use 4 sels if the checkerboard may be rotated by more than 20 deg.</remarks>
    internal static Sela MakeCheckerboardCornerSela(int size, int dilation, int nsels,
                                                   Pixa? pixadb)
    {
        const string func = "makeCheckerboardCornerSela";
        if (size <= 0) size = 7;
        if (size < 7)
            throw Lept.Arg(func, "size too small");
        if (dilation < 1 || dilation > 5)
            throw Lept.Arg(func, "dilation not in [1 ...5]");
        if (nsels != 2 && nsels != 4)
            throw Lept.Arg(func, "nsels not 2 or 4");

        Pixa pixa1 = MakeCheckerboardCornerPixa(size, dilation, nsels);
        if (pixadb != null) {
            Pix pix1 = pixa1.DisplayTiledInColumns(4, 8.0f, 15, 2);
            pixadb.AddPix(pix1, L.L_INSERT);
        }
        Sarray sa = Sarray.CreateWordsFromString(selnames);
        return Sela.CreateFromColorPixa(pixa1, sa);
    }

    // leptonica: makeCheckerboardCornerPixa (checkerboard.c)
    /// <summary>Makes the pix that represent the hit-miss sels for finding the corners.</summary>
    /// <param name="size">size of HMT sel; &gt;= 7, typ. 15; 0 for default</param>
    /// <param name="dilation">size of hit and miss squares; typ. 1 or 3; max 5</param>
    /// <param name="nsels">number to use (either 2 or 4)</param>
    /// <returns>pixa representing hit-miss sels for finding corners</returns>
    /// <remarks>
    /// (1) Each pix can be used to generate a hit-miss sel, using the
    ///     function selCreateFromColorPix().  See that function for the
    ///     use of color and gray pixels to encode the hits, misses and
    ///     center in the structuring element.
    /// </remarks>
    internal static Pixa MakeCheckerboardCornerPixa(int size, int dilation, int nsels)
    {
        Pixa pixa1 = Pixa.Create(4);

            /* Represent diagonal neg slope hits and pos slope misses */
        Pix pix1 = Create(size, size, 32);
        pix1.SetAll();
        Pix pix2 = Create(size, size, 1);  /* slope -1 line (2 pixel) mask */
        pix2.SetPixel(1, 1, 1);  /* UL corner */
        pix2.SetPixel(size - 2, size - 2, 1);  /* LR corner */
        if (dilation > 1)
            DilateBrick(pix2, pix2, dilation, dilation);  /* dilate each pixel */
        pix1.SetMasked(pix2, 0x00ff0000);  /* green hit */
        Pix pix3 = pix2.Rotate90(1);  /* slope +1 line (2 pixel) mask */
        pix1.SetMasked(pix3, 0xff000000);  /* red miss */
        pix1.SetRGBPixel(size / 2, size / 2, 128, 128, 128);  /* gray center */
        pixa1.AddPix(pix1, L.L_INSERT);

            /* Represent diagonal pos slope hits and neg slope misses */
        pix1 = Create(size, size, 32);
        pix1.SetAll();
        pix1.SetMasked(pix2, 0xff000000);  /* red hit */
        pix1.SetMasked(pix3, 0x00ff0000);  /* green miss */
        pix1.SetRGBPixel(size / 2, size / 2, 128, 128, 128);  /* gray center */
        pixa1.AddPix(pix1, L.L_INSERT);

        if (nsels == 2)
            return pixa1;

            /* Represent cross: vertical hits and horizontal misses */
        pix1 = Create(size, size, 32);
        pix1.SetAll();
        pix2 = Create(size, size, 1);  /* vertical line (2 pixel) mask */
        pix2.SetPixel(size / 2, 1, 1);
        pix2.SetPixel(size / 2, size - 2, 1);
        if (dilation > 1)
            DilateBrick(pix2, pix2, dilation, dilation);  /* dilate each pixel */
        pix1.SetMasked(pix2, 0x00ff0000);  /* green hit */
        pix3 = pix2.Rotate90(1);  /* horizontal line (2 pixel) mask */
        pix1.SetMasked(pix3, 0xff000000);  /* red miss */
        pix1.SetRGBPixel(size / 2, size / 2, 128, 128, 128);  /* gray center */
        pixa1.AddPix(pix1, L.L_INSERT);

            /* Represent cross: horizontal hits and vertical misses */
        pix1 = Create(size, size, 32);
        pix1.SetAll();
        pix1.SetMasked(pix3, 0x00ff0000);  /* green hit */
        pix1.SetMasked(pix2, 0xff000000);  /* red miss */
        pix1.SetRGBPixel(size / 2, size / 2, 128, 128, 128);  /* gray center */
        pixa1.AddPix(pix1, L.L_INSERT);

        return pixa1;
    }
}
