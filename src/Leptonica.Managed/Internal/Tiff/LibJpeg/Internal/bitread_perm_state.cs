#nullable disable
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, SYSLIB0011, SYSLIB0014, CS8981
namespace BitMiracle.LibJpeg.Classic.Internal
{
    /// <summary>
    /// Bitreading state saved across MCUs
    /// </summary>
    struct bitread_perm_state
    {
        public int get_buffer;    /* current bit-extraction buffer */
        public int bits_left;      /* # of unused bits in it */
    }
}
