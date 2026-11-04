#nullable disable
using System.Diagnostics;

namespace BitMiracle.LibTiff.Classic.Internal
{
    internal sealed class DeflateCodecTagMethods : TiffTagMethods
    {
        public override bool SetField(Tiff tif, TiffTag tag, FieldValue[] ap)
        {
            DeflateCodec sp = tif.m_currentCodec as DeflateCodec;
            Debug.Assert(sp != null);

            switch (tag)
            {
                case TiffTag.ZIPQUALITY:
                    sp.m_zipquality = ap[0].ToInt();
                    sp.OnQualityChanged();
                    return true;
            }

            return base.SetField(tif, tag, ap);
        }

        public override FieldValue[] GetField(Tiff tif, TiffTag tag)
        {
            DeflateCodec sp = tif.m_currentCodec as DeflateCodec;
            Debug.Assert(sp != null);

            switch (tag)
            {
                case TiffTag.ZIPQUALITY:
                    FieldValue[] result = new FieldValue[1];
                    result[0].Set(sp.m_zipquality);
                    return result;
            }

            return base.GetField(tif, tag);
        }
    }
}
