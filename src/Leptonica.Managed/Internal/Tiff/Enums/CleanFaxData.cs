#nullable disable
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, SYSLIB0011, SYSLIB0014, CS8981
namespace BitMiracle.LibTiff.Classic
{
    /// <summary>
    /// Regenerated line info.<br/>
    /// Possible values for <see cref="TiffTag"/>.CLEANFAXDATA tag.
    /// </summary>
#if EXPOSE_LIBTIFF
    public
#endif
    enum CleanFaxData
    {
        /// <summary>
        /// No errors detected.
        /// </summary>
        CLEAN = 0,

        /// <summary>
        /// Receiver regenerated lines.
        /// </summary>
        REGENERATED = 1,

        /// <summary>
        /// Uncorrected errors exist.
        /// </summary>
        UNCLEAN = 2,
    }
}
