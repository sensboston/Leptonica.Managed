namespace Leptonica;

/*
 *  libversions.c
 *
 *      Image library version number
 *          Utils.GetImagelibVersions (getImagelibVersions)
 */
public static partial class Utils
{
    /*---------------------------------------------------------------------*
     *                    Image Library Version number                     *
     *---------------------------------------------------------------------*/
    // leptonica: getImagelibVersions (libversions.c)
    /// <summary>Returns a string of version numbers of all supported image codecs.</summary>
    public static string GetImagelibVersions()
    {
        return "libgif 5.2.2 : libjpeg 6b : libpng 1.6.43 : libtiff 4.0.10 : zlib 1.3.1 : libwebp 1.4.0 : libopenjp2 2.5.2";
    }
}
