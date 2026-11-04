#nullable disable
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, SYSLIB0011, SYSLIB0014, CS8981
namespace BitMiracle.LibTiff.Classic
{
    /// <summary>
    /// Prediction scheme w/ LZW.<br/>
    /// Possible values for <see cref="TiffTag"/>.PREDICTOR tag.
    /// </summary>
#if EXPOSE_LIBTIFF
    public
#endif
    enum Predictor
    {
        /// <summary>
        /// No prediction scheme used.
        /// </summary>
        NONE = 1,

        /// <summary>
        /// Horizontal differencing.
        /// </summary>
        HORIZONTAL = 2,

        /// <summary>
        /// Floating point predictor.
        /// </summary>
        FLOATINGPOINT = 3,
    }
}
