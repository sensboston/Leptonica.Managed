#nullable disable
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, SYSLIB0011, SYSLIB0014, CS8981
namespace BitMiracle.LibJpeg.Classic.Internal
{
    /// <summary>
    /// Coefficient buffer control
    /// </summary>
    interface jpeg_c_coef_controller
    {
        void start_pass(J_BUF_MODE pass_mode);
        bool compress_data(byte[][][] input_buf);
    }
}
