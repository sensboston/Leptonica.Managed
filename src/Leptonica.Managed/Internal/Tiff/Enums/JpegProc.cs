#nullable disable
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, SYSLIB0011, SYSLIB0014, CS8981
namespace BitMiracle.LibTiff.Classic
{
    /// <summary>
    /// JPEG processing algorithm.<br/>
    /// Possible values for <see cref="TiffTag"/>.JPEGPROC tag.
    /// </summary>
#if EXPOSE_LIBTIFF
    public
#endif
    enum JpegProc
    {
        /// <summary>
        /// Baseline sequential.
        /// </summary>
        BASELINE = 1,

        /// <summary>
        /// Huffman coded lossless.
        /// </summary>
        LOSSLESS = 14,
    }
}
