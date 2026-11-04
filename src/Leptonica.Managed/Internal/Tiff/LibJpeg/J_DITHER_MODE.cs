#nullable disable
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, SYSLIB0011, SYSLIB0014, CS8981
namespace BitMiracle.LibJpeg.Classic
{
    /// <summary>
    /// Dithering options for decompression.
    /// </summary>
    /// <seealso cref="jpeg_decompress_struct.Dither_mode"/>
#if EXPOSE_LIBJPEG
    public
#endif
    enum J_DITHER_MODE
    {
        /// <summary>
        /// No dithering: fast, very low quality
        /// </summary>
        JDITHER_NONE,

        /// <summary>
        /// Ordered dither: moderate speed and quality
        /// </summary>
        JDITHER_ORDERED,

        /// <summary>
        /// Floyd-Steinberg dither: slow, high quality
        /// </summary>
        JDITHER_FS
    }
}
