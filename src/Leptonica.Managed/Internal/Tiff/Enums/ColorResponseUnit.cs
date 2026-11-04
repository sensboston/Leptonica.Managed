#nullable disable
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, SYSLIB0011, SYSLIB0014, CS8981
namespace BitMiracle.LibTiff.Classic
{
    /// <summary>
    /// Color curve accuracy.<br/>
    /// Possible values for <see cref="TiffTag"/>.COLORRESPONSEUNIT tag.
    /// </summary>
#if EXPOSE_LIBTIFF
    public
#endif
    enum ColorResponseUnit
    {
        /// <summary>
        /// Tenths of a unit.
        /// </summary>
        CRU10S = 1,

        /// <summary>
        /// Hundredths of a unit.
        /// </summary>
        CRU100S = 2,

        /// <summary>
        /// Thousandths of a unit.
        /// </summary>
        CRU1000S = 3,

        /// <summary>
        /// Ten-thousandths of a unit.
        /// </summary>
        CRU10000S = 4,

        /// <summary>
        /// Hundred-thousandths.
        /// </summary>
        CRU100000S = 5,
    }
}
