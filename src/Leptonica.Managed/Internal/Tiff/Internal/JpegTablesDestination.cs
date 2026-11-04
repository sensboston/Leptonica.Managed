#nullable disable
#pragma warning disable CS0618, CS0649, CS0168, CS0169, CS0219, CS0414, SYSLIB0011, SYSLIB0014, CS8981
using BitMiracle.LibJpeg.Classic;

namespace BitMiracle.LibTiff.Classic.Internal
{
    /// <summary>
    /// Alternate destination manager for outputting to JPEGTables field.
    /// </summary>
    class JpegTablesDestination : jpeg_destination_mgr
    {
        private JpegCodec m_sp;

        public JpegTablesDestination(JpegCodec sp)
        {
            m_sp = sp;
        }

        public override void init_destination()
        {
            /* while building, jpegtables_length is allocated buffer size */
            initInternalBuffer(m_sp.m_jpegtables, 0);
        }

        public override bool empty_output_buffer()
        {
            /* the entire buffer has been filled; enlarge it by 1000 bytes */
            byte[] newbuf = Tiff.Realloc(m_sp.m_jpegtables, m_sp.m_jpegtables_length + 1000);

            initInternalBuffer(newbuf, m_sp.m_jpegtables_length);
            m_sp.m_jpegtables = newbuf;
            m_sp.m_jpegtables_length += 1000;
            return true;
        }

        public override void term_destination()
        {
            /* set tables length to number of bytes actually emitted */
            m_sp.m_jpegtables_length -= freeInBuffer;
        }
    }
}
