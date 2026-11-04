namespace Leptonica;

/// <summary>
/// Constants from pix.h and imageio.h, with the same values as in C.
/// </summary>
public static partial class L
{
    /*-------------------------------------------------------------------------*
     *                         Colors for 32 bpp (pix.h)                       *
     *-------------------------------------------------------------------------*/
    /// <summary>Index of the red component in a 32 bpp pixel.</summary>
    public const int COLOR_RED = 0;
    /// <summary>Index of the green component in a 32 bpp pixel.</summary>
    public const int COLOR_GREEN = 1;
    /// <summary>Index of the blue component in a 32 bpp pixel.</summary>
    public const int COLOR_BLUE = 2;
    /// <summary>Index of the alpha component in a 32 bpp pixel.</summary>
    public const int L_ALPHA_CHANNEL = 3;

    /// <summary>Number of bits the red component is shifted left within a 32 bpp pixel (24).</summary>
    public const int L_RED_SHIFT = 8 * (sizeof(uint) - 1 - COLOR_RED);
    /// <summary>Number of bits the green component is shifted left within a 32 bpp pixel (16).</summary>
    public const int L_GREEN_SHIFT = 8 * (sizeof(uint) - 1 - COLOR_GREEN);
    /// <summary>Number of bits the blue component is shifted left within a 32 bpp pixel (8).</summary>
    public const int L_BLUE_SHIFT = 8 * (sizeof(uint) - 1 - COLOR_BLUE);
    /// <summary>Number of bits the alpha component is shifted left within a 32 bpp pixel (0).</summary>
    public const int L_ALPHA_SHIFT = 8 * (sizeof(uint) - 1 - L_ALPHA_CHANNEL);

    /*-------------------------------------------------------------------------*
     *                       Access and storage flags (pix.h)                  *
     *-------------------------------------------------------------------------*/
    /// <summary>Do not copy the object; the container takes the reference as it is.</summary>
    public const int L_NOCOPY = 0;
    /// <summary>Insert the object itself, without copying or cloning; equivalent to <see cref="L_NOCOPY"/>.</summary>
    public const int L_INSERT = L_NOCOPY;
    /// <summary>Make and use a copy of the object.</summary>
    public const int L_COPY = 1;
    /// <summary>Make and use a clone (an additional reference) of the object.</summary>
    public const int L_CLONE = 2;
    /// <summary>Make a new container object (for example, a <see cref="Pixa"/>) and fill it with clones of the elements.</summary>
    public const int L_COPY_CLONE = 3;

    /*-------------------------------------------------------------------------*
     *                      Initial colors (pix.h)                             *
     *-------------------------------------------------------------------------*/
    /// <summary>Set the pixels to white.</summary>
    public const int L_SET_WHITE = 1;
    /// <summary>Set the pixels to black.</summary>
    public const int L_SET_BLACK = 2;

    /*-------------------------------------------------------------------------*
     *                  Flags for removing colormaps (pix.h)                   *
     *-------------------------------------------------------------------------*/
    /// <summary>Remove the colormap by converting to a 1 bpp image.</summary>
    public const int REMOVE_CMAP_TO_BINARY = 0;
    /// <summary>Remove the colormap by converting to an 8 bpp gray image.</summary>
    public const int REMOVE_CMAP_TO_GRAYSCALE = 1;
    /// <summary>Remove the colormap by converting to a 32 bpp full color image.</summary>
    public const int REMOVE_CMAP_TO_FULL_COLOR = 2;
    /// <summary>Remove the colormap and keep the alpha channel.</summary>
    public const int REMOVE_CMAP_WITH_ALPHA = 3;
    /// <summary>Remove the colormap, choosing the output depending on the source image.</summary>
    public const int REMOVE_CMAP_BASED_ON_SRC = 4;

    /*-------------------------------------------------------------------------*
     *                       Image file formats (imageio.h)                    *
     *-------------------------------------------------------------------------*/
    /// <summary>Unknown file format.</summary>
    public const int IFF_UNKNOWN = 0;
    /// <summary>BMP format.</summary>
    public const int IFF_BMP = 1;
    /// <summary>JPEG format (JFIF).</summary>
    public const int IFF_JFIF_JPEG = 2;
    /// <summary>PNG format.</summary>
    public const int IFF_PNG = 3;
    /// <summary>TIFF format, with the compression chosen automatically.</summary>
    public const int IFF_TIFF = 4;
    /// <summary>TIFF format with PackBits compression.</summary>
    public const int IFF_TIFF_PACKBITS = 5;
    /// <summary>TIFF format with run-length compression.</summary>
    public const int IFF_TIFF_RLE = 6;
    /// <summary>TIFF format with CCITT Group 3 compression.</summary>
    public const int IFF_TIFF_G3 = 7;
    /// <summary>TIFF format with CCITT Group 4 compression.</summary>
    public const int IFF_TIFF_G4 = 8;
    /// <summary>TIFF format with LZW compression.</summary>
    public const int IFF_TIFF_LZW = 9;
    /// <summary>TIFF format with Zip (Deflate) compression.</summary>
    public const int IFF_TIFF_ZIP = 10;
    /// <summary>PNM format.</summary>
    public const int IFF_PNM = 11;
    /// <summary>PostScript format.</summary>
    public const int IFF_PS = 12;
    /// <summary>GIF format.</summary>
    public const int IFF_GIF = 13;
    /// <summary>JPEG 2000 format.</summary>
    public const int IFF_JP2 = 14;
    /// <summary>WebP format.</summary>
    public const int IFF_WEBP = 15;
    /// <summary>PDF format.</summary>
    public const int IFF_LPDF = 16;
    /// <summary>TIFF format with JPEG compression.</summary>
    public const int IFF_TIFF_JPEG = 17;
    /// <summary>Default format: TIFF with Group 4 compression for 1 bpp images and PNG for all others.</summary>
    public const int IFF_DEFAULT = 18;
    /// <summary>Serialized Pix format (SPIX).</summary>
    public const int IFF_SPIX = 19;

    /// <summary>File extensions indexed by IFF_* (ImageFileFormatExtensions in writefile.c).</summary>
    public static readonly string[] ImageFileFormatExtensions =
    {
        "unknown", "bmp", "jpg", "png", "tif", "tif", "tif", "tif", "tif", "tif",
        "tif", "pnm", "ps", "gif", "jp2", "webp", "pdf", "tif", "default", ""
    };
}
