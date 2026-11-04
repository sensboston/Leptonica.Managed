#nullable disable
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, SYSLIB0011, SYSLIB0014, CS8981
namespace BitMiracle.LibTiff.Classic
{
    /// <summary>
    /// Kind of data in subfile.<br/>
    /// Possible values for <see cref="TiffTag"/>.OSUBFILETYPE tag.
    /// </summary>
#if EXPOSE_LIBTIFF
    public
#endif
    enum OFileType
    {
        /// <summary>
        /// Full resolution image data.
        /// </summary>
        IMAGE = 1,

        /// <summary>
        /// Reduced size image data.
        /// </summary>
        REDUCEDIMAGE = 2,

        /// <summary>
        /// One page of many.
        /// </summary>
        PAGE = 3
    }
}
