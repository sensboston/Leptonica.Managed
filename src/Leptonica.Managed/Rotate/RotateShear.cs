using System;

namespace Leptonica;

/*
 *  rotateshear.c: Rotation by shear.
 *
 *  Shear rotation about arbitrary point using 2 and 3 shears
 *      pixRotateShear, pixRotate2Shear, pixRotate3Shear
 *  Shear rotation in-place about arbitrary point using 3 shears
 *      pixRotateShearIP
 *  Shear rotation around the image center
 *      pixRotateShearCenter (2 or 3 shears), pixRotateShearCenterIP (3 shears)
 *
 *  Rotation is measured in radians; clockwise rotations are positive.
 *
 *  Rotation by shear works on images of any depth, including 8 bpp color
 *  paletted images and 32 bpp rgb images.  It works by translating each src
 *  pixel value to the appropriate pixel in the rotated dest.  For small
 *  angles (typically less than 0.05 radians) rotation can be done with
 *  2 orthogonal shears; otherwise 3 shears are used.  All horizontal and
 *  vertical shears are implemented by rasterop.  The in-place rotation uses
 *  special in-place shears that copy rows sideways or columns vertically
 *  without buffering, and then rewrite old pixels that are no longer covered
 *  by sheared pixels.  In-place shear operations on cmapped pix are not
 *  allowed.  See rotateshear.c for the full discussion.
 *
 *  Differences from C:
 *    - Where C logs L_ERROR and returns NULL for an angle that is too large,
 *      the functions return null.
 *    - pixRotateShearCenter throws where pixRotateShear returns NULL, so that its
 *      callers (pixRotate) get a non-null pix.
 *    - pixRotateShearIP and pixRotateShearCenterIP return void and throw on
 *      invalid arguments.
 *    - pixClone is Pix.Clone (the same reference).
 *
 *  Not ported: nothing.
 */
public sealed partial class Pix
{
        /* Angle limits:
         *     angle < MinAngleToRotate    ==>  clone
         *     angle > MaxTwoShearAngle    ==>  warning for 2-angle shears
         *     angle > MaxThreeShearAngle  ==>  warning for 3-angle shears
         *     angle > MaxShearAngle       ==>  error  */
        /* MinAngleToRotate (0.001f, radians; ~0.06 deg) is shared with rotate.c
         * and is defined in Rotate.cs. */
    private const float MaxTwoShearAngle = 0.06f;    /* radians; ~3 deg    */
    private const float MaxThreeShearAngle = 0.35f;  /* radians; ~20 deg   */
    private const float MaxShearAngle = 0.50f;       /* radians; ~29 deg   */

    /*------------------------------------------------------------------*
     *                Rotations about an arbitrary point                *
     *------------------------------------------------------------------*/
    // leptonica: pixRotateShear (rotateshear.c)
    /// <summary>Rotation about the given point, using 2 or 3 shears.</summary>
    /// <param name="xcen">x value for which there is no horizontal shear</param>
    /// <param name="ycen">y value for which there is no vertical shear</param>
    /// <param name="angle">radians</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>pixd, or null if the angle is too large</returns>
    /// <remarks>
    /// (1) This rotates an image about the given point, using
    ///     either 2 or 3 shears.
    /// (2) A positive angle gives a clockwise rotation.
    /// (3) This brings in 'incolor' pixels from outside the image.
    /// (4) For rotation angles larger than about 0.35 radians, we issue
    ///     a warning because you should probably be using another method
    ///     (either sampling or area mapping)
    /// </remarks>
    public Pix? RotateShear(int xcen, int ycen, float angle, int incolor)
    {
        const string func = "pixRotateShear";

        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid incolor value");

        if (Math.Abs(angle) > MaxShearAngle) {
            Log.Error(func, $"{Math.Abs(angle),6:F2} radians; too large for shear rotation");
            return null;
        }
        if (Math.Abs(angle) < MinAngleToRotate)
            return Clone(this);

        if (Math.Abs(angle) <= MaxTwoShearAngle)
            return Rotate2Shear(xcen, ycen, angle, incolor);
        else
            return Rotate3Shear(xcen, ycen, angle, incolor);
    }


    // leptonica: pixRotate2Shear (rotateshear.c)
    /// <summary>Rotation about the given point, using 2 shears.</summary>
    /// <param name="xcen">center of rotation</param>
    /// <param name="ycen">center of rotation</param>
    /// <param name="angle">radians</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>pixd, or null if the angle is too large</returns>
    /// <remarks>
    /// (1) This rotates the image about the given point, using the 2-shear
    ///     method.  It should only be used for angles no larger than
    ///     MaxTwoShearAngle.  For larger angles, a warning is issued.
    /// (2) A positive angle gives a clockwise rotation.
    /// (3) 2-shear rotation by a specified angle is equivalent
    ///     to the sequential transformations
    ///        x' = x + tan(angle) * (y - ycen)     for x-shear
    ///        y' = y + tan(angle) * (x - xcen)     for y-shear
    /// (4) Computation of tan(angle) is performed within the shear operation.
    /// (5) This brings in 'incolor' pixels from outside the image.
    /// (6) If the image has an alpha layer, it is rotated separately by
    ///     two shears.
    /// </remarks>
    public Pix? Rotate2Shear(int xcen, int ycen, float angle, int incolor)
    {
        const string func = "pixRotate2Shear";
        Pix pix1, pixd;

        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid incolor value");

        if (Math.Abs(angle) > MaxShearAngle) {
            Log.Error(func, $"{Math.Abs(angle),6:F2} radians; too large for shear rotation");
            return null;
        }
        if (Math.Abs(angle) < MinAngleToRotate)
            return Clone(this);
        if (Math.Abs(angle) > MaxTwoShearAngle)
            Log.Warning(func, $"{Math.Abs(angle),6:F2} radians; large angle for 2-shear rotation");

        pix1 = HShear(null, this, ycen, angle, incolor);
        pixd = VShear(null, pix1, xcen, angle, incolor);

        if (Depth == 32 && Spp == 4) {
            pix1 = GetRGBComponent(L.L_ALPHA_CHANNEL);
                /* L_BRING_IN_WHITE brings in opaque for the alpha component */
            Pix? pix2 = pix1.Rotate2Shear(xcen, ycen, angle, L.L_BRING_IN_WHITE);
            pixd.SetRGBComponent(pix2!, L.L_ALPHA_CHANNEL);
        }
        return pixd;
    }


    // leptonica: pixRotate3Shear (rotateshear.c)
    /// <summary>Rotation about the given point, using 3 shears.</summary>
    /// <param name="xcen">center of rotation</param>
    /// <param name="ycen">center of rotation</param>
    /// <param name="angle">radians</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>pixd, or null if the angle is too large</returns>
    /// <remarks>
    /// (1) This rotates the image about the given point, using the 3-shear
    ///     method.  It should only be used for angles smaller than
    ///     MaxThreeShearAngle.  For larger angles, a warning is issued.
    /// (2) A positive angle gives a clockwise rotation.
    /// (3) 3-shear rotation by a specified angle is equivalent
    ///     to the sequential transformations
    ///       y' = y + tan(angle/2) * (x - xcen)     for first y-shear
    ///       x' = x + sin(angle) * (y - ycen)       for x-shear
    ///       y' = y + tan(angle/2) * (x - xcen)     for second y-shear
    /// (4) Computation of tan(angle) is performed in the shear operations.
    /// (5) This brings in 'incolor' pixels from outside the image.
    /// (6) If the image has an alpha layer, it is rotated separately by
    ///     two shears.
    /// (7) The algorithm was published by Alan Paeth: "A Fast Algorithm
    ///     for General Raster Rotation," Graphics Interface '86,
    ///     pp. 77-81, May 1986.  A description of the method, along with
    ///     an implementation, can be found in Graphics Gems, p. 179,
    ///     edited by Andrew Glassner, published by Academic Press, 1990.
    /// </remarks>
    public Pix? Rotate3Shear(int xcen, int ycen, float angle, int incolor)
    {
        const string func = "pixRotate3Shear";
        float hangle;
        Pix pix1, pixd;

        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid incolor value");

        if (Math.Abs(angle) > MaxShearAngle) {
            Log.Error(func, $"{Math.Abs(angle),6:F2} radians; too large for shear rotation");
            return null;
        }
        if (Math.Abs(angle) < MinAngleToRotate)
            return Clone(this);
        if (Math.Abs(angle) > MaxThreeShearAngle) {
            Log.Warning(func, $"{Math.Abs(angle),6:F2} radians; large angle for 3-shear rotation");
        }

        hangle = (float)Math.Atan(Math.Sin(angle));
        pixd = VShear(null, this, xcen, angle / 2.0f, incolor);
        pix1 = HShear(null, pixd, ycen, hangle, incolor);
        VShear(pixd, pix1, xcen, angle / 2.0f, incolor);

        if (Depth == 32 && Spp == 4) {
            pix1 = GetRGBComponent(L.L_ALPHA_CHANNEL);
                /* L_BRING_IN_WHITE brings in opaque for the alpha component */
            Pix? pix2 = pix1.Rotate3Shear(xcen, ycen, angle, L.L_BRING_IN_WHITE);
            pixd.SetRGBComponent(pix2!, L.L_ALPHA_CHANNEL);
        }
        return pixd;
    }


    /*------------------------------------------------------------------*
     *             Rotations in-place about an arbitrary point          *
     *------------------------------------------------------------------*/
    // leptonica: pixRotateShearIP (rotateshear.c)
    /// <summary>In-place rotation about the given point, using 3 shears.</summary>
    /// <param name="xcen">center of rotation</param>
    /// <param name="ycen">center of rotation</param>
    /// <param name="angle">radians</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <remarks>
    /// (1) This does an in-place rotation of the image about the
    ///     specified point, using the 3-shear method.  It should only
    ///     be used for angles smaller than MaxThreeShearAngle.
    ///     For larger angles, a warning is issued.
    /// (2) A positive angle gives a clockwise rotation.
    /// (3) 3-shear rotation by a specified angle is equivalent
    ///     to the sequential transformations
    ///       y' = y + tan(angle/2) * (x - xcen)      for first y-shear
    ///       x' = x + sin(angle) * (y - ycen)        for x-shear
    ///       y' = y + tan(angle/2) * (x - xcen)      for second y-shear
    /// (4) Computation of tan(angle) is performed in the shear operations.
    /// (5) This brings in 'incolor' pixels from outside the image.
    /// (6) The pix cannot be colormapped, because the in-place operation
    ///     only blits in 0 or 1 bits, not an arbitrary colormap index.
    /// </remarks>
    public void RotateShearIP(int xcen, int ycen, float angle, int incolor)
    {
        const string func = "pixRotateShearIP";
        float hangle;

        if (incolor != L.L_BRING_IN_WHITE && incolor != L.L_BRING_IN_BLACK)
            throw Lept.Arg(func, "invalid value for incolor");
        if (Colormap != null)
            throw Lept.Arg(func, "pixs is colormapped");

        if (angle == 0.0)
            return;
        if (Math.Abs(angle) > MaxThreeShearAngle) {
            Log.Warning(func, $"{Math.Abs(angle),6:F2} radians; large angle for in-place 3-shear rotation");
        }

        hangle = (float)Math.Atan(Math.Sin(angle));
        HShearIP(ycen, angle / 2.0f, incolor);
        VShearIP(xcen, hangle, incolor);
        HShearIP(ycen, angle / 2.0f, incolor);
    }


    /*------------------------------------------------------------------*
     *                    Rotations about the image center              *
     *------------------------------------------------------------------*/
    // leptonica: pixRotateShearCenter (rotateshear.c)
    /// <summary>Rotation about the image center by shear.</summary>
    /// <param name="angle">radians; clockwise is positive</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    /// <returns>pixd</returns>
    public Pix RotateShearCenter(float angle, int incolor)
    {
        return RotateShear(Width / 2, Height / 2, angle, incolor)
            ?? throw Lept.Fail("pixRotateShearCenter", "angle too large for shear rotation");
    }


    // leptonica: pixRotateShearCenterIP (rotateshear.c)
    /// <summary>In-place rotation about the image center by 3 shears.</summary>
    /// <param name="angle">radians</param>
    /// <param name="incolor">L_BRING_IN_WHITE, L_BRING_IN_BLACK</param>
    public void RotateShearCenterIP(float angle, int incolor)
    {
        RotateShearIP(Width / 2, Height / 2, angle, incolor);
    }
}
