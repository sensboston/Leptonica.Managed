using System;
using System.IO;
using System.IO.Compression;

namespace Leptonica;

/*
 *  zlibmem.c: zlib compression and decompression of byte arrays in memory.
 *
 *  Only the two functions that other ported files need (encoding.c) are here.
 *  They use System.IO.Compression; the compressed bytes can differ from those
 *  of the zlib library, but they are valid zlib streams and decompress to the same data.
 */
/// <summary>Zlib compression and decompression of byte arrays in memory.</summary>
public static class ZlibMem
{
    // leptonica: zlibCompress (zlibmem.c)
    /// <summary>Compresses data in a zlib stream, with the default compression level.</summary>
    /// <param name="datain">byte buffer with input data</param>
    /// <returns>compressed data</returns>
    public static byte[] ZlibCompress(ReadOnlySpan<byte> datain)
    {
        var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(datain);
        return ms.ToArray();
    }

    // leptonica: zlibUncompress (zlibmem.c)
    /// <summary>Decompresses a zlib stream.</summary>
    /// <param name="datain">byte buffer with compressed input data</param>
    /// <returns>decompressed data</returns>
    public static byte[] ZlibUncompress(ReadOnlySpan<byte> datain)
    {
        const string func = "zlibUncompress";
        if (datain.IsEmpty)
            throw Lept.Arg(func, "datain not defined");
        try {
            using var input = new MemoryStream(datain.ToArray());
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            var output = new MemoryStream();
            z.CopyTo(output);
            return output.ToArray();
        } catch (InvalidDataException e) {
            throw Lept.Fail(func, e.Message);
        }
    }
}
