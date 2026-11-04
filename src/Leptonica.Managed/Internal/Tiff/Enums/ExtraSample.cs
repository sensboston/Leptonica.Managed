#nullable disable
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, SYSLIB0011, SYSLIB0014, CS8981
namespace BitMiracle.LibTiff.Classic
{
    /// <summary>
    /// Information about extra samples.<br/>
    /// Possible values for <see cref="TiffTag"/>.EXTRASAMPLES tag.
    /// </summary>
#if EXPOSE_LIBTIFF
    public
#endif
    enum ExtraSample
    {
        /// <summary>
        /// Unspecified data.
        /// </summary>
        UNSPECIFIED = 0,

        /// <summary>
        /// Associated alpha data.
        /// </summary>
        ASSOCALPHA = 1,

        /// <summary>
        /// Unassociated alpha data.
        /// </summary>
        UNASSALPHA = 2,
    }
}
