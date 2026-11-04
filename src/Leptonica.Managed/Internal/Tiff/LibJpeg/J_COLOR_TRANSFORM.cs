#nullable disable
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, SYSLIB0011, SYSLIB0014, CS8981
namespace BitMiracle.LibJpeg.Classic
{
    /// <summary>
    /// Supported color transforms.
    /// </summary>
#if EXPOSE_LIBJPEG
    public
#endif
    enum J_COLOR_TRANSFORM
    {
        /// <summary>
        /// No transform
        /// </summary>
        JCT_NONE = 0,

        /// <summary>
        /// Substract green
        /// </summary>
        JCT_SUBTRACT_GREEN = 1
    }
}
