using System;
using System.Collections.Generic;

namespace Leptonica.Internal.Png;

/// <summary>Raised wherever libpng would call <c>png_error</c>.</summary>
internal sealed class PngException : Exception
{
    public PngException(string message) : base(message) { }
}

internal readonly record struct PngColor(byte Red, byte Green, byte Blue);

/// <summary>Text chunk entry; every char holds one byte of the stored string (no decoding).</summary>
internal readonly record struct PngText(string Key, string Text);

/// <summary>Header and chunk data, mirroring the <c>png_get_*</c> accessors.</summary>
internal sealed class PngInfo
{
    public int Width, Height;
    public int BitDepth;        // png_get_bit_depth (after transforms)
    public int ColorType;       // png_get_color_type: 0 gray, 2 rgb, 3 palette, 4 gray+alpha, 6 rgba
    public int Channels;        // png_get_channels
    public int RowBytes;        // png_get_rowbytes (after transforms)
    public int Interlace;       // png_get_interlace_type: 0 none, 1 adam7
    public PngColor[]? Palette; // null if no PLTE
    public bool HasTrns;        // png_get_valid(..., PNG_INFO_tRNS)
    public byte[] Trans = [];   // palette alpha entries (png_get_tRNS trans/num_trans)
    public ushort TransGray, TransRed, TransGreen, TransBlue;  // png_get_tRNS trans_color
    public bool HasPhys;        // pHYs present
    public uint PhysX, PhysY;   // png_get_pHYs
    public int PhysUnit;        // 0 unknown, 1 meter
    public uint XPixelsPerMeter, YPixelsPerMeter;  // png_get_x/y_pixels_per_meter
    public double? Gamma;       // png_get_gAMA, null if absent
    public List<PngText> Texts = new();  // png_get_text: tEXt, zTXt, iTXt in file order
}

internal sealed class PngImage
{
    public required PngInfo Info;
    public required byte[] Pixels;   // Height rows of Info.RowBytes bytes, top to bottom
}

internal sealed class PngWriteInfo
{
    public int Width, Height, BitDepth, ColorType;
    public PngColor[]? Palette;
    public byte[]? Trans;            // palette alpha, written as tRNS if not null
    public uint PhysX, PhysY;        // written with unit 1 (meter); both 0 -> no pHYs chunk
    public double? Gamma;
    public List<PngText> Texts = new();
    public int CompressionLevel = -1;
}
