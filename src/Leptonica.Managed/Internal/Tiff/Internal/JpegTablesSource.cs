#nullable disable
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, SYSLIB0011, SYSLIB0014, CS8981
namespace BitMiracle.LibTiff.Classic.Internal
{
    /// <summary>
    /// Alternate source manager for reading from JPEGTables.
    /// We can share all the code except for the init routine.
    /// </summary>
    class JpegTablesSource : JpegStdSource
    {
        public JpegTablesSource(JpegCodec sp)
            : base(sp)
        {
        }

        public override void init_source()
        {
            initInternalBuffer(m_sp.m_jpegtables, m_sp.m_jpegtables_length);
        }
    }
}
