#nullable disable
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, SYSLIB0011, SYSLIB0014, CS8981
namespace BitMiracle.LibTiff.Classic
{
    /// <summary>
    /// Storage organization.<br/>
    /// Possible values for <see cref="TiffTag"/>.PLANARCONFIG tag.
    /// </summary>
#if EXPOSE_LIBTIFF
    public
#endif
    enum PlanarConfig
    {
        /// <summary>
        /// Unknown (uninitialized).
        /// </summary>
        UNKNOWN = 0,

        /// <summary>
        /// Single image plane.
        /// </summary>
        CONTIG = 1,

        /// <summary>
        /// Separate planes of data.
        /// </summary>
        SEPARATE = 2
    }
}
