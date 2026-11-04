#nullable disable
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, SYSLIB0011, SYSLIB0014, CS8981
namespace BitMiracle.LibJpeg.Classic.Internal
{
    /// <summary>
    /// Entropy encoding
    /// </summary>
    abstract class jpeg_entropy_encoder
    {
        public delegate bool encode_mcu_delegate(JBLOCK[][] MCU_data);
        public delegate void finish_pass_delegate();

        public encode_mcu_delegate encode_mcu;
        public finish_pass_delegate finish_pass;

        public abstract void start_pass(bool gather_statistics);
    }
}
