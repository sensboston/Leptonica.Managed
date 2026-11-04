#nullable disable
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, SYSLIB0011, SYSLIB0014, CS8981
namespace BitMiracle.LibTiff.Classic
{
    /// <summary>
    /// Units of resolutions.<br/>
    /// Possible values for <see cref="TiffTag"/>.RESOLUTIONUNIT tag.
    /// </summary>
#if EXPOSE_LIBTIFF
    public
#endif
    enum ResUnit
    {
        /// <summary>
        /// No meaningful units.
        /// </summary>
        NONE = 1,

        /// <summary>
        /// English.
        /// </summary>
        INCH = 2,

        /// <summary>
        /// Metric.
        /// </summary>
        CENTIMETER = 3,
    }
}
