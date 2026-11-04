#nullable disable
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace BitMiracle.LibTiff.Classic.Internal
{
    internal sealed class DeflateCodec : CodecWithPredictor
    {
        public const int ZSTATE_INIT_DECODE = 0x01;
        public const int ZSTATE_INIT_ENCODE = 0x02;
        public const int Z_DEFAULT_COMPRESSION = -1;

        public int m_zipquality;
        public int m_state;

        private MemoryStream m_rawStream;
        private ZLibStream m_zlibStream;
        private bool m_hasWrittenData;

        private static readonly TiffFieldInfo[] zipFieldInfo =
        {
            new TiffFieldInfo(TiffTag.ZIPQUALITY, 0, 0, TiffType.ANY, FieldBit.Pseudo, true, false, string.Empty),
        };

        private readonly TiffTagMethods m_tagMethods;

        public DeflateCodec(Tiff tif, Compression scheme, string name)
            : base(tif, scheme, name)
        {
            m_tagMethods = new DeflateCodecTagMethods();
        }

        public override bool Init()
        {
            Debug.Assert((m_scheme == Compression.DEFLATE) || (m_scheme == Compression.ADOBE_DEFLATE));
            m_tif.MergeFieldInfo(zipFieldInfo, zipFieldInfo.Length);
            m_zipquality = Z_DEFAULT_COMPRESSION;
            m_state = 0;
            TIFFPredictorInit(m_tagMethods);
            return true;
        }

        public override bool CanEncode => true;

        public override bool CanDecode => true;

        public override bool PreDecode(short plane) => ZIPPreDecode(plane);

        public override bool PreEncode(short plane) => ZIPPreEncode(plane);

        public override bool PostEncode() => ZIPPostEncode();

        public override void Cleanup() => ZIPCleanup();

        public override bool predictor_setupdecode() => ZIPSetupDecode();

        public override bool predictor_decoderow(byte[] buffer, int offset, int count, short plane) =>
            ZIPDecode(buffer, offset, count, plane);

        public override bool predictor_decodestrip(byte[] buffer, int offset, int count, short plane) =>
            ZIPDecode(buffer, offset, count, plane);

        public override bool predictor_decodetile(byte[] buffer, int offset, int count, short plane) =>
            ZIPDecode(buffer, offset, count, plane);

        public override bool predictor_setupencode() => ZIPSetupEncode();

        public override bool predictor_encoderow(byte[] buffer, int offset, int count, short plane) =>
            ZIPEncode(buffer, offset, count, plane);

        public override bool predictor_encodestrip(byte[] buffer, int offset, int count, short plane) =>
            ZIPEncode(buffer, offset, count, plane);

        public override bool predictor_encodetile(byte[] buffer, int offset, int count, short plane) =>
            ZIPEncode(buffer, offset, count, plane);

        internal static CompressionLevel MapQuality(int quality)
        {
            if (quality == 0)
                return CompressionLevel.NoCompression;
            if (quality >= 1 && quality <= 3)
                return CompressionLevel.Fastest;
            if (quality >= 8)
                return CompressionLevel.SmallestSize;
            return CompressionLevel.Optimal;
        }

        internal void OnQualityChanged()
        {
            if ((m_state & ZSTATE_INIT_ENCODE) != 0 && !m_hasWrittenData && m_rawStream != null)
            {
                m_zlibStream?.Dispose();
                m_rawStream.SetLength(0);
                m_zlibStream = new ZLibStream(m_rawStream, MapQuality(m_zipquality), leaveOpen: true);
            }
        }

        private void ResetStreams()
        {
            m_zlibStream?.Dispose();
            m_zlibStream = null;
            m_rawStream?.Dispose();
            m_rawStream = null;
            m_hasWrittenData = false;
        }

        private void ZIPCleanup()
        {
            base.TIFFPredictorCleanup();
            ResetStreams();
            m_state = 0;
        }

        private bool ZIPDecode(byte[] buffer, int offset, int count, short plane)
        {
            const string module = "ZIPDecode";
            Debug.Assert(m_state == ZSTATE_INIT_DECODE);

            if (m_zlibStream == null)
                return false;

            try
            {
                int remaining = count;
                int pos = offset;
                while (remaining > 0)
                {
                    int n = m_zlibStream.Read(buffer, pos, remaining);
                    if (n <= 0)
                    {
                        Tiff.ErrorExt(m_tif, m_tif.m_clientdata, module,
                            "{0}: Not enough data at scanline {1} (short {2} bytes)",
                            m_tif.m_name, m_tif.m_row, remaining);
                        return false;
                    }
                    pos += n;
                    remaining -= n;
                }
                return true;
            }
            catch (Exception ex)
            {
                Tiff.ErrorExt(m_tif, m_tif.m_clientdata, module,
                    "{0}: Decoding error at scanline {1}, {2}",
                    m_tif.m_name, m_tif.m_row, ex.Message);
                return false;
            }
        }

        private bool ZIPEncode(byte[] buffer, int offset, int count, short plane)
        {
            const string module = "ZIPEncode";
            Debug.Assert(m_state == ZSTATE_INIT_ENCODE);

            if (m_zlibStream == null)
                return false;

            try
            {
                if (count > 0)
                {
                    m_hasWrittenData = true;
                    m_zlibStream.Write(buffer, offset, count);
                }
                return true;
            }
            catch (Exception ex)
            {
                Tiff.ErrorExt(m_tif, m_tif.m_clientdata, module,
                    "{0}: Encoder error: {1}", m_tif.m_name, ex.Message);
                return false;
            }
        }

        private bool ZIPPostEncode()
        {
            const string module = "ZIPPostEncode";
            try
            {
                if (m_zlibStream != null)
                {
                    m_zlibStream.Dispose();
                    m_zlibStream = null;
                }

                if (m_rawStream != null)
                {
                    byte[] compressed = m_rawStream.GetBuffer();
                    int total = (int)m_rawStream.Length;
                    int pos = 0;
                    int cap = m_tif.m_rawdatasize;

                    while (pos < total)
                    {
                        int chunk = Math.Min(cap, total - pos);
                        Buffer.BlockCopy(compressed, pos, m_tif.m_rawdata, 0, chunk);
                        m_tif.m_rawcc = chunk;
                        if (!m_tif.flushData1())
                            return false;
                        pos += chunk;
                    }

                    m_rawStream.Dispose();
                    m_rawStream = null;
                }

                m_hasWrittenData = false;
                return true;
            }
            catch (Exception ex)
            {
                Tiff.ErrorExt(m_tif, m_tif.m_clientdata, module,
                    "{0}: zlib error: {1}", m_tif.m_name, ex.Message);
                return false;
            }
        }

        private bool ZIPPreDecode(short s)
        {
            if ((m_state & ZSTATE_INIT_DECODE) == 0)
                SetupDecode();

            ResetStreams();
            m_rawStream = new MemoryStream(m_tif.m_rawdata, 0, m_tif.m_rawcc, writable: false);
            m_zlibStream = new ZLibStream(m_rawStream, CompressionMode.Decompress, leaveOpen: false);
            return true;
        }

        private bool ZIPPreEncode(short s)
        {
            if (m_state != ZSTATE_INIT_ENCODE)
                SetupEncode();

            ResetStreams();
            m_rawStream = new MemoryStream();
            m_zlibStream = new ZLibStream(m_rawStream, MapQuality(m_zipquality), leaveOpen: true);
            return true;
        }

        private bool ZIPSetupDecode()
        {
            if ((m_state & ZSTATE_INIT_ENCODE) != 0)
            {
                ResetStreams();
                m_state = 0;
            }
            m_state |= ZSTATE_INIT_DECODE;
            return true;
        }

        private bool ZIPSetupEncode()
        {
            if ((m_state & ZSTATE_INIT_DECODE) != 0)
            {
                ResetStreams();
                m_state = 0;
            }
            m_state |= ZSTATE_INIT_ENCODE;
            return true;
        }
    }
}
