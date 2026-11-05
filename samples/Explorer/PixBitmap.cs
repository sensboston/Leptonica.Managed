using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Leptonica;

namespace Explorer;

/// <summary>
/// Shows a Leptonica image in Avalonia without encoding it: the pixels are copied straight into a
/// WriteableBitmap. The same approach works for any UI or graphics library that accepts raw BGRA
/// pixels (SkiaSharp, WPF WriteableBitmap, System.Drawing, ImageSharp).
/// </summary>
public static class PixBitmap
{
    public static WriteableBitmap ToBitmap(Pix pix)
    {
        // Any depth (1, 2, 4, 8, 16 bpp, colormapped or not) to 32 bpp RGB.
        Pix rgb = pix.Depth == 32 ? pix : pix.ConvertTo32();
        int w = rgb.Width, h = rgb.Height, wpl = rgb.Wpl;
        uint[] data = rgb.Data;

        var bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using ILockedFramebuffer fb = bitmap.Lock();
        var row = new int[w];
        for (int y = 0; y < h; y++)
        {
            int line = y * wpl;
            for (int x = 0; x < w; x++)
            {
                // A Leptonica RGB pixel is the word 0xRRGGBBAA; BGRA8888 in memory on a
                // little-endian machine is the word 0xAARRGGBB.
                uint p = data[line + x];
                row[x] = unchecked((int)(0xFF000000u | (p >> 8)));
            }
            Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, w);
        }
        return bitmap;
    }
}
