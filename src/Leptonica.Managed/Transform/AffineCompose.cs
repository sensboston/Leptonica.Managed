using System;

namespace Leptonica;

/*
 *  affinecompose.c: Composable coordinate transforms.
 *
 *      Composable coordinate transforms
 *           createMatrix2dTranslate()
 *           createMatrix2dScale()
 *           createMatrix2dRotate()
 *
 *      Special coordinate transforms on pta
 *           ptaTranslate()
 *           ptaScale()
 *           ptaRotate()
 *
 *      Special coordinate transforms on boxa
 *           boxaTranslate()
 *           boxaScale()
 *           boxaRotate()
 *
 *      General coordinate transform on pta and boxa
 *           ptaAffineTransform()
 *           boxaAffineTransform()
 *
 *      Matrix operations
 *           l_productMatVec()
 *           l_productMat2()
 *           l_productMat3()
 *           l_productMat4()
 *
 *  Differences from C:
 *    - The 3x3 matrices and the vectors are float arrays (row-major).
 *    - The functions that have no Pix, Pta or Boxa argument are static
 *      members of Pix, the same as in Affine.cs.
 *    - The matrix functions return void; invalid input throws.
 *
 *  Not ported: nothing.
 */
public sealed partial class Pix
{
    /*-------------------------------------------------------------*
     *                Composable coordinate transforms             *
     *-------------------------------------------------------------*/
    // leptonica: createMatrix2dTranslate (affinecompose.c)
    /// <summary>Creates a 3x3 translation matrix.</summary>
    /// <param name="transx">x component of translation wrt. the origin</param>
    /// <param name="transy">y component of translation wrt. the origin</param>
    /// <returns>3x3 transform matrix</returns>
    /// <remarks>
    /// (1) The translation is equivalent to:
    ///        v' = Av
    ///     where v and v' are 1x3 column vectors in the form
    ///        v = [x, y, 1]^    ^ denotes transpose
    ///     and the affine translation matrix is
    ///        A = [ 1   0   tx
    ///              0   1   ty
    ///              0   0    1  ]
    ///
    /// (2) We consider translation as with respect to a fixed origin.
    ///     In a clipping operation, the origin moves and the points
    ///     are fixed, and you use (-tx, -ty) where (tx, ty) is the
    ///     translation vector of the origin.
    /// </remarks>
    public static float[] CreateMatrix2dTranslate(float transx, float transy)
    {
        float[] mat = new float[9];
        mat[0] = mat[4] = mat[8] = 1;
        mat[2] = transx;
        mat[5] = transy;
        return mat;
    }

    // leptonica: createMatrix2dScale (affinecompose.c)
    /// <summary>Creates a 3x3 scaling matrix.</summary>
    /// <param name="scalex">horizontal scale factor</param>
    /// <param name="scaley">vertical scale factor</param>
    /// <returns>3x3 transform matrix</returns>
    /// <remarks>
    /// (1) The scaling is equivalent to:
    ///        v' = Av
    ///     where v and v' are 1x3 column vectors in the form
    ///         v = [x, y, 1]^    ^ denotes transpose
    ///     and the affine scaling matrix is
    ///        A = [ sx  0    0
    ///              0   sy   0
    ///              0   0    1  ]
    ///
    /// (2) We consider scaling as with respect to a fixed origin.
    ///     In other words, the origin is the only point that doesn't
    ///     move in the scaling transform.
    /// </remarks>
    public static float[] CreateMatrix2dScale(float scalex, float scaley)
    {
        float[] mat = new float[9];
        mat[0] = scalex;
        mat[4] = scaley;
        mat[8] = 1;
        return mat;
    }

    // leptonica: createMatrix2dRotate (affinecompose.c)
    /// <summary>Creates a 3x3 rotation matrix about (xc, yc).</summary>
    /// <param name="xc">location of center of rotation</param>
    /// <param name="yc">location of center of rotation</param>
    /// <param name="angle">rotation in radians; clockwise is positive</param>
    /// <returns>3x3 transform matrix</returns>
    /// <remarks>
    /// (1) The rotation is equivalent to:
    ///        v' = Av
    ///     where v and v' are 1x3 column vectors in the form
    ///        v = [x, y, 1]^    ^ denotes transpose
    ///     and the affine rotation matrix is
    ///        A = [ cosa   -sina    xc*(1-cosa) + yc*sina
    ///              sina    cosa    yc*(1-cosa) - xc*sina
    ///                0       0                 1         ]
    ///
    ///     If the rotation is about the origin, (xc, yc) = (0, 0) and
    ///     this simplifies to
    ///        A = [ cosa   -sina    0
    ///              sina    cosa    0
    ///                0       0     1 ]
    ///
    ///     These relations follow from the following equations, which
    ///     you can convince yourself are correct as follows.  Draw a
    ///     circle centered on (xc,yc) and passing through (x,y), with
    ///     (x',y') on the arc at an angle 'a' clockwise from (x,y).
    ///     [ Hint: cos(a + b) = cosa * cosb - sina * sinb
    ///             sin(a + b) = sina * cosb + cosa * sinb ]
    ///
    ///       x' - xc =  (x - xc) * cosa - (y - yc) * sina
    ///       y' - yc =  (x - xc) * sina + (y - yc) * cosa
    /// </remarks>
    public static float[] CreateMatrix2dRotate(float xc, float yc, float angle)
    {
        float[] mat = new float[9];
        float sina = (float)Math.Sin(angle);
        float cosa = (float)Math.Cos(angle);
        mat[0] = mat[4] = cosa;
        mat[1] = -sina;
        mat[2] = (float)(xc * (1.0 - cosa) + yc * sina);
        mat[3] = sina;
        mat[5] = (float)(yc * (1.0 - cosa) - xc * sina);
        mat[8] = 1;
        return mat;
    }

    /*-------------------------------------------------------------*
     *                         Matrix operations                   *
     *-------------------------------------------------------------*/
    // leptonica: l_productMatVec (affinecompose.c)
    /// <summary>Multiplies a square matrix by a vector.</summary>
    /// <param name="mat">square matrix, size x size, row-major</param>
    /// <param name="vecs">input column vector of length size</param>
    /// <param name="vecd">result column vector of length size</param>
    /// <param name="size">of matrix side</param>
    public static void ProductMatVec(float[] mat, float[] vecs, float[] vecd, int size)
    {
        for (int i = 0; i < size; i++) {
            vecd[i] = 0;
            for (int j = 0; j < size; j++) {
                vecd[i] += mat[size * i + j] * vecs[j];
            }
        }
    }

    // leptonica: l_productMat2 (affinecompose.c)
    /// <summary>Multiplies two square matrices.</summary>
    /// <param name="mat1">square matrix, size x size</param>
    /// <param name="mat2">square matrix, size x size</param>
    /// <param name="matd">square matrix; product mat1 * mat2</param>
    /// <param name="size">of matrix side</param>
    public static void ProductMat2(float[] mat1, float[] mat2, float[] matd, int size)
    {
        for (int i = 0; i < size; i++) {
            for (int j = 0; j < size; j++) {
                int index = size * i + j;
                matd[index] = 0;
                for (int k = 0; k < size; k++)
                    matd[index] += mat1[size * i + k] * mat2[size * k + j];
            }
        }
    }

    // leptonica: l_productMat3 (affinecompose.c)
    /// <summary>Multiplies three square matrices.</summary>
    /// <param name="mat1">square matrix, size x size</param>
    /// <param name="mat2">square matrix, size x size</param>
    /// <param name="mat3">square matrix, size x size</param>
    /// <param name="matd">square matrix; product mat1 * mat2 * mat3</param>
    /// <param name="size">of matrix side</param>
    public static void ProductMat3(float[] mat1, float[] mat2, float[] mat3, float[] matd, int size)
    {
        float[] matt = new float[size * size];
        ProductMat2(mat1, mat2, matt, size);
        ProductMat2(matt, mat3, matd, size);
    }

    // leptonica: l_productMat4 (affinecompose.c)
    /// <summary>Multiplies four square matrices.</summary>
    /// <param name="mat1">square matrix, size x size</param>
    /// <param name="mat2">square matrix, size x size</param>
    /// <param name="mat3">square matrix, size x size</param>
    /// <param name="mat4">square matrix, size x size</param>
    /// <param name="matd">square matrix; product mat1 * mat2 * mat3 * mat4</param>
    /// <param name="size">of matrix side</param>
    public static void ProductMat4(float[] mat1, float[] mat2, float[] mat3, float[] mat4, float[] matd, int size)
    {
        float[] matt = new float[size * size];
        ProductMat3(mat1, mat2, mat3, matt, size);
        ProductMat2(matt, mat4, matd, size);
    }
}

public sealed partial class Pta
{
    /*-------------------------------------------------------------*
     *            Special coordinate transforms on pta             *
     *-------------------------------------------------------------*/
    // leptonica: ptaTranslate (affinecompose.c)
    /// <summary>Translates the points.</summary>
    /// <param name="transx">x component of translation wrt. the origin</param>
    /// <param name="transy">y component of translation wrt. the origin</param>
    /// <returns>translated points</returns>
    /// <remarks>
    /// (1) See createMatrix2dTranslate() for details of transform.
    /// </remarks>
    public Pta Translate(float transx, float transy)
    {
        int npts = Count;
        Pta ptad = Create(npts);
        for (int i = 0; i < npts; i++) {
            GetPt(i, out float x, out float y);
            ptad.AddPt(x + transx, y + transy);
        }

        return ptad;
    }

    // leptonica: ptaScale (affinecompose.c)
    /// <summary>Scales the points about the origin.</summary>
    /// <param name="scalex">horizontal scale factor</param>
    /// <param name="scaley">vertical scale factor</param>
    /// <returns>scaled points</returns>
    /// <remarks>
    /// (1) See createMatrix2dScale() for details of transform.
    /// </remarks>
    public Pta Scale(float scalex, float scaley)
    {
        int npts = Count;
        Pta ptad = Create(npts);
        for (int i = 0; i < npts; i++) {
            GetPt(i, out float x, out float y);
            ptad.AddPt(scalex * x, scaley * y);
        }

        return ptad;
    }

    // leptonica: ptaRotate (affinecompose.c)
    /// <summary>Rotates the points about (xc, yc).</summary>
    /// <param name="xc">location of center of rotation</param>
    /// <param name="yc">location of center of rotation</param>
    /// <param name="angle">rotation in radians; clockwise is positive</param>
    /// <returns>rotated points</returns>
    /// <remarks>
    /// (1) See createMatrix2dRotate() for details of transform.
    /// (2) This transform can be thought of as composed of the
    ///     sum of two parts:
    ///      a) an (x,y)-dependent rotation about the origin:
    ///         xr = x * cosa - y * sina
    ///         yr = x * sina + y * cosa
    ///      b) an (x,y)-independent translation that depends on the
    ///         rotation center and the angle:
    ///         xt = xc - xc * cosa + yc * sina
    ///         yt = yc - xc * sina - yc * cosa
    ///     The translation part (xt,yt) is equal to the difference
    ///     between the center (xc,yc) and the location of the
    ///     center after it is rotated about the origin.
    /// </remarks>
    public Pta Rotate(float xc, float yc, float angle)
    {
        int npts = Count;
        Pta ptad = Create(npts);
        float sina = (float)Math.Sin(angle);
        float cosa = (float)Math.Cos(angle);
        for (int i = 0; i < npts; i++) {
            GetPt(i, out float x, out float y);
            float xp = xc + (x - xc) * cosa - (y - yc) * sina;
            float yp = yc + (x - xc) * sina + (y - yc) * cosa;
            ptad.AddPt(xp, yp);
        }

        return ptad;
    }

    /*-------------------------------------------------------------*
     *            General coordinate transform on pta              *
     *-------------------------------------------------------------*/
    // leptonica: ptaAffineTransform (affinecompose.c)
    /// <summary>Applies a 3x3 transform matrix to the points.</summary>
    /// <param name="mat">3x3 transform matrix</param>
    /// <returns>transformed points</returns>
    public Pta AffineTransform(float[] mat)
    {
        float[] vecs = new float[3];
        float[] vecd = new float[3];
        vecs[2] = 1;
        int npts = Count;
        Pta ptad = Create(npts);
        for (int i = 0; i < npts; i++) {
            GetPt(i, out vecs[0], out vecs[1]);
            Pix.ProductMatVec(mat, vecs, vecd, 3);
            ptad.AddPt(vecd[0], vecd[1]);
        }

        return ptad;
    }
}

public sealed partial class Boxa
{
    /*-------------------------------------------------------------*
     *            Special coordinate transforms on boxa            *
     *-------------------------------------------------------------*/
    // leptonica: boxaTranslate (affinecompose.c)
    /// <summary>Translates the boxes.</summary>
    /// <param name="transx">x component of translation wrt. the origin</param>
    /// <param name="transy">y component of translation wrt. the origin</param>
    /// <returns>translated boxa</returns>
    /// <remarks>
    /// (1) See createMatrix2dTranslate() for details of transform.
    /// </remarks>
    public Boxa Translate(float transx, float transy)
    {
        Pta ptas = ConvertToPta(4);
        Pta ptad = ptas.Translate(transx, transy);
        return ptad.ConvertToBoxa(4);
    }

    // leptonica: boxaScale (affinecompose.c)
    /// <summary>Scales the boxes about the origin.</summary>
    /// <param name="scalex">horizontal scale factor</param>
    /// <param name="scaley">vertical scale factor</param>
    /// <returns>scaled boxa</returns>
    /// <remarks>
    /// (1) See createMatrix2dScale() for details of transform.
    /// </remarks>
    public Boxa Scale(float scalex, float scaley)
    {
        Pta ptas = ConvertToPta(4);
        Pta ptad = ptas.Scale(scalex, scaley);
        return ptad.ConvertToBoxa(4);
    }

    // leptonica: boxaRotate (affinecompose.c)
    /// <summary>Rotates the boxes about (xc, yc).</summary>
    /// <param name="xc">location of center of rotation</param>
    /// <param name="yc">location of center of rotation</param>
    /// <param name="angle">rotation in radians; clockwise is positive</param>
    /// <returns>rotated boxa</returns>
    /// <remarks>
    /// (1) See createMatrix2dRotate() for details of transform.
    /// </remarks>
    public Boxa Rotate(float xc, float yc, float angle)
    {
        Pta ptas = ConvertToPta(4);
        Pta ptad = ptas.Rotate(xc, yc, angle);
        return ptad.ConvertToBoxa(4);
    }

    // leptonica: boxaAffineTransform (affinecompose.c)
    /// <summary>Applies a 3x3 transform matrix to the boxes.</summary>
    /// <param name="mat">3x3 transform matrix</param>
    /// <returns>transformed boxa</returns>
    public Boxa AffineTransform(float[] mat)
    {
        Pta ptas = ConvertToPta(4);
        Pta ptad = ptas.AffineTransform(mat);
        return ptad.ConvertToBoxa(4);
    }
}
