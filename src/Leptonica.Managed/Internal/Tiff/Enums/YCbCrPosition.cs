#nullable disable
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, SYSLIB0011, SYSLIB0014, CS8981
namespace BitMiracle.LibTiff.Classic
{
    /// <summary>
    /// Subsample positioning.<br/>
    /// Possible values for <see cref="TiffTag"/>.YCBCRPOSITIONING tag.
    /// </summary>
#if EXPOSE_LIBTIFF
    public
#endif
    enum YCbCrPosition
    {
        /// <summary>
        /// As in PostScript Level 2
        /// </summary>
        CENTERED = 1,

        /// <summary>
        /// As in CCIR 601-1
        /// </summary>
        COSITED = 2,
    }
}
